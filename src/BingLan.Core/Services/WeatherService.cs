using System.Globalization;
using System.Text.Json;
using BingLan.Core.Models;

namespace BingLan.Core.Services;

// Open-Meteo 公开预报接口客户端。只发送调用方传入的城市坐标，不做任何自动定位。
// 城市到坐标的解析（地理编码）由上层在选择城市时完成，不在本模块范围内。
public sealed class WeatherService
{
    public const string ForecastEndpoint = "https://api.open-meteo.com/v1/forecast";

    public static readonly TimeSpan SuccessRefreshInterval = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan BaseRetryDelay = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private int _consecutiveFailures;
    private DateTimeOffset? _nextRetryAt;
    private WeatherSnapshot? _lastSuccess;
    private string? _locationKey;

    public WeatherService()
        : this(null, null)
    {
    }

    public WeatherService(HttpMessageHandler? handler, TimeSpan? timeout = null)
    {
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        _httpClient.Timeout = timeout ?? DefaultTimeout;
    }

    public DateTimeOffset? NextRetryAt => _nextRetryAt;

    // 退避是强制的调度建议：失败后调用方应等待 ShouldAttemptRefresh 放行再重试。
    public bool ShouldAttemptRefresh(DateTimeOffset now) =>
        _consecutiveFailures == 0 || _nextRetryAt is null || now >= _nextRetryAt.Value;

    public bool ShouldAttemptRefresh(
        DateTimeOffset now,
        string city,
        double latitude,
        double longitude) =>
        !string.Equals(_locationKey, CreateLocationKey(city, latitude, longitude), StringComparison.Ordinal) ||
        ShouldAttemptRefresh(now);

    public WeatherSnapshot? GetLastSnapshot(string city, double latitude, double longitude) =>
        string.Equals(_locationKey, CreateLocationKey(city, latitude, longitude), StringComparison.Ordinal)
            ? _lastSuccess
            : null;

    // 指数退避：1、2、4、8、16、30 分钟封顶，避免无限快速重试。
    public static TimeSpan ComputeRetryDelay(int consecutiveFailures)
    {
        if (consecutiveFailures <= 0)
        {
            return TimeSpan.Zero;
        }
        var shift = Math.Min(consecutiveFailures - 1, 5);
        var delay = TimeSpan.FromTicks(BaseRetryDelay.Ticks << shift);
        return delay > MaxRetryDelay ? MaxRetryDelay : delay;
    }

    public async Task<WeatherSnapshot> RefreshAsync(
        string city,
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        var trimmedCity = city?.Trim() ?? "";
        if (trimmedCity.Length == 0)
        {
            return new WeatherSnapshot(
                WeatherStatus.NoCity, "", null, null, null, null, "", false, null, null);
        }

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var locationKey = CreateLocationKey(trimmedCity, latitude, longitude);
            if (!string.Equals(_locationKey, locationKey, StringComparison.Ordinal))
            {
                _locationKey = locationKey;
                _lastSuccess = null;
                _consecutiveFailures = 0;
                _nextRetryAt = null;
            }

            try
            {
                var json = await _httpClient
                    .GetStringAsync(BuildRequestUrl(latitude, longitude), cancellationToken)
                    .ConfigureAwait(false);
                var snapshot = ParseForecast(json, trimmedCity, DateTimeOffset.Now);
                _lastSuccess = snapshot;
                _consecutiveFailures = 0;
                _nextRetryAt = null;
                return snapshot;
            }
            catch (Exception exception) when (exception is OperationCanceledException
                or HttpRequestException
                or JsonException)
            {
                _consecutiveFailures++;
                _nextRetryAt = DateTimeOffset.Now + ComputeRetryDelay(_consecutiveFailures);
                var message = exception switch
                {
                    OperationCanceledException => "请求超时",
                    JsonException => "返回数据无法解析",
                    _ => "网络请求失败"
                };
                if (_lastSuccess is not null)
                {
                    return _lastSuccess with
                    {
                        Status = WeatherStatus.CachedFallback,
                        FromCache = true,
                        ErrorMessage = message
                    };
                }
                return new WeatherSnapshot(
                    WeatherStatus.Failed, trimmedCity,
                    null, null, null, null, "", false, null, message);
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public static string BuildRequestUrl(double latitude, double longitude) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{ForecastEndpoint}?latitude={latitude:0.#####}&longitude={longitude:0.#####}&current=temperature_2m,relative_humidity_2m,weather_code&daily=temperature_2m_max,temperature_2m_min&forecast_days=1&timezone=auto");

    public static WeatherSnapshot ParseForecast(string json, string city, DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var current = root.GetProperty("current");
        var temperature = current.GetProperty("temperature_2m").GetDouble();
        int? humidity = current.TryGetProperty("relative_humidity_2m", out var humidityElement)
            ? humidityElement.GetInt32()
            : null;
        var condition = current.TryGetProperty("weather_code", out var codeElement)
            ? DescribeWeatherCode(codeElement.GetInt32())
            : "未知天气";

        double? high = null;
        double? low = null;
        if (root.TryGetProperty("daily", out var daily))
        {
            high = ReadFirstDailyValue(daily, "temperature_2m_max");
            low = ReadFirstDailyValue(daily, "temperature_2m_min");
        }

        return new WeatherSnapshot(
            WeatherStatus.Fresh, city, temperature, high, low, humidity,
            condition, false, now, null);
    }

    // WMO 天气代码，含义见 Open-Meteo 官方文档。
    public static string DescribeWeatherCode(int code) =>
        code switch
        {
            0 => "晴",
            1 => "大致晴",
            2 => "局部多云",
            3 => "阴",
            45 or 48 => "雾",
            51 or 53 or 55 => "毛毛雨",
            56 or 57 => "冻毛毛雨",
            61 => "小雨",
            63 => "中雨",
            65 => "大雨",
            66 or 67 => "冻雨",
            71 => "小雪",
            73 => "中雪",
            75 => "大雪",
            77 => "雪粒",
            80 => "小阵雨",
            81 => "阵雨",
            82 => "强阵雨",
            85 or 86 => "阵雪",
            95 => "雷暴",
            96 or 99 => "雷暴伴冰雹",
            _ => "未知天气"
        };

    private static double? ReadFirstDailyValue(JsonElement daily, string property)
    {
        if (daily.TryGetProperty(property, out var values) &&
            values.ValueKind == JsonValueKind.Array &&
            values.GetArrayLength() > 0 &&
            values[0].ValueKind == JsonValueKind.Number)
        {
            return values[0].GetDouble();
        }
        return null;
    }

    private static string CreateLocationKey(string city, double latitude, double longitude) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{city.Trim().ToUpperInvariant()}|{latitude:R}|{longitude:R}");
}
