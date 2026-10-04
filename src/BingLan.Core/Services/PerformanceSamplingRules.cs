using BingLan.Core.Models;

namespace BingLan.Core.Services;

public enum PerformanceSamplingMode
{
    Normal,
    Reduced
}

// 集中的单一采样服务契约：所有可视元素共享同一份快照，禁止各自创建计时器。
public interface IPerformanceSamplingService : IDisposable
{
    event Action<PerformanceSnapshot>? Sampled;
    bool IsRunning { get; }
    PerformanceSamplingMode Mode { get; }
    TimeSpan CurrentInterval { get; }
    void Start();
    void SetMode(PerformanceSamplingMode mode);
    void Stop();
}

public static class PerformanceSamplingRules
{
    public static readonly TimeSpan NormalInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan ReducedInterval = TimeSpan.FromSeconds(15);

    public static TimeSpan ResolveInterval(PerformanceSamplingMode mode) =>
        mode == PerformanceSamplingMode.Reduced ? ReducedInterval : NormalInterval;

    // Nobody reads the numbers while a full-screen app is in front or the desktop
    // cards are hidden, so sampling slows down instead of stopping.
    public static PerformanceSamplingMode ResolveMode(bool fullScreenInFront, bool desktopHidden) =>
        fullScreenInFront || desktopHidden ? PerformanceSamplingMode.Reduced : PerformanceSamplingMode.Normal;

    public static double ClampPercent(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0d;
        }
        return Math.Clamp(value, 0d, 100d);
    }

    // 计数器回绕、接口重启导致的计数回退或无效时间差都按 0 处理，不产生负速率。
    public static double ComputeThroughputBytesPerSecond(
        long previousBytes,
        long currentBytes,
        TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero || previousBytes < 0 || currentBytes < previousBytes)
        {
            return 0d;
        }
        return (currentBytes - previousBytes) / elapsed.TotalSeconds;
    }

    public static string FormatPercent(double percent) =>
        $"{Math.Round(ClampPercent(percent)):0}%";

    public static string FormatRate(double bytesPerSecond)
    {
        if (double.IsNaN(bytesPerSecond) || bytesPerSecond < 0d)
        {
            bytesPerSecond = 0d;
        }

        const double kilo = 1024d;
        const double mega = kilo * 1024d;
        const double giga = mega * 1024d;
        return bytesPerSecond switch
        {
            < kilo => $"{bytesPerSecond:0} B/s",
            < mega => $"{bytesPerSecond / kilo:0.#} KB/s",
            < giga => $"{bytesPerSecond / mega:0.#} MB/s",
            _ => $"{bytesPerSecond / giga:0.#} GB/s"
        };
    }
}
