using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using BingLan.Core.Models;

namespace BingLan.Core.Services;

// OpenStreetMap Nominatim 地理编码客户端。只有调用 SearchAsync 时才联网，不读取设备位置。
// 使用政策要求：带应用标识、每秒最多 1 次请求、不做输入即搜，界面需注明 © OpenStreetMap。
public sealed class CitySearchService
{
    public const string GeocodingEndpoint = "https://nominatim.openstreetmap.org/search";

    public static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    public CitySearchService()
        : this(null, null)
    {
    }

    public CitySearchService(HttpMessageHandler? handler, TimeSpan? timeout = null)
    {
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        _httpClient.Timeout = timeout ?? DefaultTimeout;
        var version = typeof(CitySearchService).Assembly.GetName().Version ?? new Version(0, 0, 0);
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("BingLan", version.ToString(3)));
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("(+https://github.com/keros68/binglan)"));
    }

    public async Task<CitySearchOutcome> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var trimmedQuery = query?.Trim() ?? string.Empty;
        if (trimmedQuery.Length < 2)
        {
            return new CitySearchOutcome(false, [], "请至少输入 2 个字符");
        }

        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = _lastRequestAt + MinimumRequestInterval - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }
            _lastRequestAt = DateTimeOffset.UtcNow;

            var json = await _httpClient
                .GetStringAsync(BuildRequestUrl(trimmedQuery), cancellationToken)
                .ConfigureAwait(false);
            var results = ParseResults(json);
            return new CitySearchOutcome(
                true,
                results,
                results.Count == 0 ? "没有找到匹配地点" : $"找到 {results.Count} 个候选地点");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CitySearchOutcome(false, [], "搜索超时，请稍后重试");
        }
        catch (HttpRequestException)
        {
            return new CitySearchOutcome(false, [], "城市搜索网络请求失败");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return new CitySearchOutcome(false, [], "城市搜索结果无法解析");
        }
        finally
        {
            _requestGate.Release();
        }
    }

    // featureType=settlement 只返回国家、省、市、区县、乡镇、村等居民点，排除车站、街道和山峰。
    public static string BuildRequestUrl(
        string query,
        int count = 8,
        string language = "zh-CN")
    {
        var safeCount = Math.Clamp(count, 1, 40);
        var safeLanguage = string.IsNullOrWhiteSpace(language) ? "zh-CN" : language.Trim();
        return $"{GeocodingEndpoint}?q={Uri.EscapeDataString(query.Trim())}" +
               $"&format=jsonv2&addressdetails=1&featureType=settlement&limit={safeCount}" +
               $"&accept-language={Uri.EscapeDataString(safeLanguage)}";
    }

    public static IReadOnlyList<CitySearchResult> ParseResults(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("搜索结果不是数组");
        }

        var results = new List<CitySearchResult>();
        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !TryReadCoordinate(item, "lat", out var latitude) ||
                !TryReadCoordinate(item, "lon", out var longitude) ||
                latitude is < -90d or > 90d ||
                longitude is < -180d or > 180d)
            {
                continue;
            }

            _ = TryReadString(item, "display_name", out var displayName);
            var parts = (displayName ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList();
            if (!TryReadString(item, "name", out var name))
            {
                name = parts.FirstOrDefault();
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var country = item.TryGetProperty("address", out var address) &&
                          address.ValueKind == JsonValueKind.Object &&
                          TryReadString(address, "country", out var addressCountry)
                ? addressCountry!
                : string.Empty;
            // display_name 形如“萧山区, 杭州市, 浙江省, 中国”；中间各级行政区用于区分同名地点，邮编不显示。
            var region = parts
                .Skip(parts.Count > 0 && parts[0] == name ? 1 : 0)
                .Where(part => part != country && !part.All(char.IsAsciiDigit));
            var id = item.TryGetProperty("place_id", out var idElement) &&
                     idElement.ValueKind == JsonValueKind.Number &&
                     idElement.TryGetInt64(out var parsedId)
                ? parsedId
                : 0L;
            results.Add(new CitySearchResult(
                id,
                name!,
                string.Join(" · ", region),
                country,
                latitude,
                longitude));
        }
        return results;
    }

    private static bool TryReadString(
        JsonElement item,
        string propertyName,
        out string? value)
    {
        value = null;
        if (!item.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString()?.Trim();
        return !string.IsNullOrWhiteSpace(value);
    }

    // Nominatim 以字符串返回经纬度。
    private static bool TryReadCoordinate(
        JsonElement item,
        string propertyName,
        out double value)
    {
        value = 0d;
        return TryReadString(item, propertyName, out var text) &&
               double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               double.IsFinite(value);
    }
}
