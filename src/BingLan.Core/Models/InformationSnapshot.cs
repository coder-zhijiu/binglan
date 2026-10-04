namespace BingLan.Core.Models;

public enum WeatherStatus
{
    NoCity,
    Fresh,
    CachedFallback,
    Failed
}

public sealed record PerformanceSnapshot(
    double CpuPercent,
    double MemoryPercent,
    double UploadBytesPerSecond,
    double DownloadBytesPerSecond,
    DateTimeOffset CapturedAt);

public sealed record WeatherSnapshot(
    WeatherStatus Status,
    string City,
    double? TemperatureCelsius,
    double? DailyHighCelsius,
    double? DailyLowCelsius,
    int? HumidityPercent,
    string Condition,
    bool FromCache,
    DateTimeOffset? UpdatedAt,
    string? ErrorMessage);
