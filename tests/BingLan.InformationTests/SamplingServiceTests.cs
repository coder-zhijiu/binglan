using BingLan.App.Services;
using BingLan.Core.Models;
using BingLan.Core.Services;
using static BingLan.InformationTests.TestSupport;

namespace BingLan.InformationTests;

internal static class SamplingServiceTests
{
    public static void StartSampleThrottleStopDispose()
    {
        using var service = new WindowsPerformanceSamplingService();
        var received = new List<PerformanceSnapshot>();
        var gate = new object();
        service.Sampled += snapshot =>
        {
            lock (gate)
            {
                received.Add(snapshot);
            }
        };

        AssertEqual(TimeSpan.FromSeconds(1), service.CurrentInterval, "默认 1 秒采样");
        service.Start();
        Assert(service.IsRunning, "启动后运行中");

        PumpUntil(
            () =>
            {
                lock (gate)
                {
                    return received.Count > 0;
                }
            },
            TimeSpan.FromSeconds(5),
            "收到第一份性能快照");

        PerformanceSnapshot first;
        lock (gate)
        {
            first = received[0];
        }
        Assert(first.CpuPercent is >= 0d and <= 100d, $"CPU 百分比越界：{first.CpuPercent}");
        Assert(first.MemoryPercent is >= 0d and <= 100d, $"RAM 百分比越界：{first.MemoryPercent}");
        Assert(first.UploadBytesPerSecond >= 0d, "上传速率不为负");
        Assert(first.DownloadBytesPerSecond >= 0d, "下载速率不为负");

        service.SetMode(PerformanceSamplingMode.Reduced);
        AssertEqual(PerformanceSamplingMode.Reduced, service.Mode, "降频模式");
        AssertEqual(TimeSpan.FromSeconds(15), service.CurrentInterval, "降频到 15 秒");
        service.SetMode(PerformanceSamplingMode.Normal);
        AssertEqual(TimeSpan.FromSeconds(1), service.CurrentInterval, "恢复 1 秒");

        service.Stop();
        Assert(!service.IsRunning, "停止后不在运行");
        int countAfterStop;
        lock (gate)
        {
            countAfterStop = received.Count;
        }
        Thread.Sleep(1600);
        Pump();
        lock (gate)
        {
            Assert(received.Count == countAfterStop, "停止后不再产生快照");
        }

        service.Start();
        Assert(service.IsRunning, "停止后可再次启动");
        service.Stop();
    }
}
