using BingLan.Core.Models;
using BingLan.Core.Services;
using static BingLan.InformationTests.TestSupport;

namespace BingLan.InformationTests;

internal static class CoreLogicTests
{
    public static void TimeFormatting()
    {
        var midnight = new DateTime(2026, 8, 4, 0, 0, 0);
        AssertEqual("00:00", GreetingService.FormatTime(midnight, true), "24 小时制零点");
        AssertEqual("上午 12:00", GreetingService.FormatTime(midnight, false), "12 小时制零点");
        AssertEqual(
            "上午 12:30",
            GreetingService.FormatTime(new DateTime(2026, 8, 4, 0, 30, 0), false),
            "12 小时制凌晨");
        AssertEqual(
            "上午 9:07",
            GreetingService.FormatTime(new DateTime(2026, 8, 4, 9, 7, 0), false),
            "12 小时制上午");
        AssertEqual(
            "下午 12:00",
            GreetingService.FormatTime(new DateTime(2026, 8, 4, 12, 0, 0), false),
            "12 小时制正午");
        AssertEqual(
            "下午 1:05",
            GreetingService.FormatTime(new DateTime(2026, 8, 4, 13, 5, 0), false),
            "12 小时制下午");
        AssertEqual(
            "下午 11:59",
            GreetingService.FormatTime(new DateTime(2026, 8, 4, 23, 59, 0), false),
            "12 小时制深夜前");
        AssertEqual(
            "23:59",
            GreetingService.FormatTime(new DateTime(2026, 8, 4, 23, 59, 0), true),
            "24 小时制深夜前");
    }

    public static void DateFormatting()
    {
        // 2026-08-04 是星期二。
        AssertEqual(
            "2026年8月4日 星期二",
            GreetingService.FormatDate(new DateTime(2026, 8, 4)),
            "日期与星期");
        AssertEqual(
            "2026年1月1日 星期四",
            GreetingService.FormatDate(new DateTime(2026, 1, 1)),
            "元旦星期");
    }

    public static void GreetingPeriods()
    {
        var cases = new (int Hour, string Expected)[]
        {
            (0, "夜深了"), (4, "夜深了"),
            (5, "早上好"), (8, "早上好"),
            (9, "上午好"), (11, "上午好"),
            (12, "中午好"), (13, "中午好"),
            (14, "下午好"), (17, "下午好"),
            (18, "晚上好"), (22, "晚上好"),
            (23, "夜深了")
        };
        foreach (var (hour, expected) in cases)
        {
            AssertEqual(
                expected,
                GreetingService.GetTimeOfDayGreeting(new DateTime(2026, 8, 4, hour, 0, 0)),
                $"时段 {hour}:00");
        }
    }

    public static void GreetingWithName()
    {
        var afternoon = new DateTime(2026, 8, 4, 15, 30, 0);
        AssertEqual(
            "下午好，小明",
            GreetingService.BuildGreeting(afternoon, "小明"),
            "带称呼问候");
        AssertEqual(
            "下午好，小明",
            GreetingService.BuildGreeting(afternoon, "  小明  "),
            "称呼去空白");
        AssertEqual("下午好", GreetingService.BuildGreeting(afternoon, ""), "空称呼");
        AssertEqual("下午好", GreetingService.BuildGreeting(afternoon, "   "), "纯空白称呼");
        AssertEqual("下午好", GreetingService.BuildGreeting(afternoon, null), "null 称呼");
    }

    public static void SamplingIntervals()
    {
        AssertEqual(
            TimeSpan.FromSeconds(1),
            PerformanceSamplingRules.ResolveInterval(PerformanceSamplingMode.Normal),
            "正常采样 1 秒");
        AssertEqual(
            TimeSpan.FromSeconds(15),
            PerformanceSamplingRules.ResolveInterval(PerformanceSamplingMode.Reduced),
            "降频采样 15 秒");
    }

    public static void PercentClamping()
    {
        AssertEqual(0d, PerformanceSamplingRules.ClampPercent(double.NaN), "NaN 归零");
        AssertEqual(0d, PerformanceSamplingRules.ClampPercent(double.PositiveInfinity), "无穷归零");
        AssertEqual(0d, PerformanceSamplingRules.ClampPercent(-5d), "负值归零");
        AssertEqual(100d, PerformanceSamplingRules.ClampPercent(150d), "超过 100 截断");
        AssertEqual(42.5d, PerformanceSamplingRules.ClampPercent(42.5d), "正常值保持");
        AssertEqual("12%", PerformanceSamplingRules.FormatPercent(12.3), "百分比取整");
        AssertEqual("0%", PerformanceSamplingRules.FormatPercent(-1d), "负百分比归零");
    }

    public static void ThroughputRules()
    {
        AssertEqual(
            500d,
            PerformanceSamplingRules.ComputeThroughputBytesPerSecond(
                1000, 2000, TimeSpan.FromSeconds(2)),
            "正常速率");
        AssertEqual(
            0d,
            PerformanceSamplingRules.ComputeThroughputBytesPerSecond(
                2000, 1000, TimeSpan.FromSeconds(1)),
            "计数器回退按 0");
        AssertEqual(
            0d,
            PerformanceSamplingRules.ComputeThroughputBytesPerSecond(
                1000, 2000, TimeSpan.Zero),
            "零时间差按 0");
        AssertEqual(
            0d,
            PerformanceSamplingRules.ComputeThroughputBytesPerSecond(
                1000, 2000, TimeSpan.FromSeconds(-1)),
            "负时间差按 0");
        AssertEqual(
            0d,
            PerformanceSamplingRules.ComputeThroughputBytesPerSecond(
                -1, 2000, TimeSpan.FromSeconds(1)),
            "负基线按 0");
    }

    public static void RateFormatting()
    {
        AssertEqual("0 B/s", PerformanceSamplingRules.FormatRate(0d), "零速率");
        AssertEqual("512 B/s", PerformanceSamplingRules.FormatRate(512d), "字节级");
        AssertEqual("2 KB/s", PerformanceSamplingRules.FormatRate(2048d), "KB 级");
        AssertEqual("1.5 KB/s", PerformanceSamplingRules.FormatRate(1536d), "KB 小数");
        AssertEqual("5 MB/s", PerformanceSamplingRules.FormatRate(5d * 1024 * 1024), "MB 级");
        AssertEqual("0 B/s", PerformanceSamplingRules.FormatRate(double.NaN), "NaN 归零");
        AssertEqual("0 B/s", PerformanceSamplingRules.FormatRate(-10d), "负值归零");
    }

    public static void WeatherCityGuard()
    {
        var state = new InformationWidgetState();
        Assert(!state.HasWeatherLocation, "默认无城市");
        state.WeatherCity = "北京";
        Assert(!state.HasWeatherLocation, "只有名称没有坐标不可用");
        state.WeatherLatitude = 39.9;
        state.WeatherLongitude = 116.4;
        Assert(state.HasWeatherLocation, "名称加坐标可用");
        state.WeatherLatitude = 91;
        Assert(!state.HasWeatherLocation, "纬度越界不可用");
        state.WeatherLatitude = 39.9;
        state.WeatherLongitude = 181;
        Assert(!state.HasWeatherLocation, "经度越界不可用");
    }
}
