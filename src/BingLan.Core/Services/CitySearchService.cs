using System.Text.Json;
using BingLan.Core.Models;

namespace BingLan.Core.Services;

// Open-Meteo 地理编码客户端。只有调用 SearchAsync 时才联网，不读取设备位置。
public sealed class CitySearchService
{
    public const string GeocodingEndpoint =
        "https://geocoding-api.open-meteo.com/v1/search";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private readonly HttpClient _httpClient;

    public CitySearchService()
        : this(null, null)
    {
    }

    public CitySearchService(HttpMessageHandler? handler, TimeSpan? timeout = null)
    {
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        _httpClient.Timeout = timeout ?? DefaultTimeout;
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

        try
        {
            var json = await _httpClient
                .GetStringAsync(BuildRequestUrl(trimmedQuery), cancellationToken)
                .ConfigureAwait(false);
            var results = ParseResults(json);
            return new CitySearchOutcome(
                true,
                results,
                results.Count == 0 ? "没有找到匹配城市" : $"找到 {results.Count} 个候选城市");
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
    }

    public static string BuildRequestUrl(
        string query,
        int count = 8,
        string language = "zh")
    {
        var safeCount = Math.Clamp(count, 1, 100);
        var safeLanguage = string.IsNullOrWhiteSpace(language)
            ? "zh"
            : language.Trim().ToLowerInvariant();
        return $"{GeocodingEndpoint}?name={Uri.EscapeDataString(query.Trim())}" +
               $"&count={safeCount}&language={Uri.EscapeDataString(safeLanguage)}&format=json";
    }

    public static IReadOnlyList<CitySearchResult> ParseResults(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("results", out var resultsElement))
        {
            return [];
        }
        if (resultsElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("results 不是数组");
        }

        var results = new List<CitySearchResult>();
        foreach (var item in resultsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !TryReadString(item, "name", out var name) ||
                !TryReadDouble(item, "latitude", out var latitude) ||
                !TryReadDouble(item, "longitude", out var longitude) ||
                latitude is < -90d or > 90d ||
                longitude is < -180d or > 180d)
            {
                continue;
            }

            var id = item.TryGetProperty("id", out var idElement) &&
                     idElement.ValueKind == JsonValueKind.Number &&
                     idElement.TryGetInt64(out var parsedId)
                ? parsedId
                : 0L;
            _ = TryReadString(item, "admin1", out var admin1);
            _ = TryReadString(item, "country", out var country);
            results.Add(new CitySearchResult(
                id,
                name!,
                admin1 ?? string.Empty,
                country ?? string.Empty,
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

    private static bool TryReadDouble(
        JsonElement item,
        string propertyName,
        out double value)
    {
        value = 0d;
        return item.TryGetProperty(propertyName, out var element) &&
               element.ValueKind == JsonValueKind.Number &&
               element.TryGetDouble(out value) &&
               double.IsFinite(value);
    }
}
