using System.Net;
using System.Net.Http;
using BingLan.Core.Models;
using BingLan.Core.Services;
using static BingLan.InformationTests.TestSupport;

namespace BingLan.InformationTests;

// 全部天气测试都通过注入的 HttpMessageHandler 完成，不访问真实网络。
internal static class WeatherOfflineTests
{
    private const string ForecastJson = """
        {
          "latitude": 39.9,
          "longitude": 116.4,
          "current": {
            "time": "2026-08-04T12:00",
            "temperature_2m": 31.5,
            "relative_humidity_2m": 62,
            "weather_code": 2
          },
          "daily": {
            "time": ["2026-08-04"],
            "temperature_2m_max": [34.2],
            "temperature_2m_min": [24.8]
          }
        }
        """;

    public static void SuccessParse()
    {
        var stub = new StubHttpMessageHandler
        {
            Behavior = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ForecastJson)
            })
        };
        var service = new WeatherService(stub);
        var snapshot = Await(service.RefreshAsync("北京", 39.9, 116.4));

        AssertEqual(WeatherStatus.Fresh, snapshot.Status, "成功状态");
        AssertEqual("北京", snapshot.City, "城市名");
        AssertEqual(31.5, snapshot.TemperatureCelsius, "当前温度");
        AssertEqual(34.2, snapshot.DailyHighCelsius, "最高温");
        AssertEqual(24.8, snapshot.DailyLowCelsius, "最低温");
        AssertEqual(62, snapshot.HumidityPercent, "湿度");
        AssertEqual("局部多云", snapshot.Condition, "天气现象");
        Assert(snapshot.UpdatedAt is not null, "记录更新时间");
        Assert(!snapshot.FromCache, "非缓存");
        AssertEqual(1, stub.CallCount, "只请求一次");
        Assert(service.ShouldAttemptRefresh(DateTimeOffset.Now), "成功后立即可再次刷新");
    }

    public static void TimeoutFailure()
    {
        var stub = new StubHttpMessageHandler
        {
            Behavior = async (_, cancellationToken) =>
            {
                await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        };
        var service = new WeatherService(stub, TimeSpan.FromMilliseconds(200));
        var snapshot = Await(service.RefreshAsync("北京", 39.9, 116.4));

        AssertEqual(WeatherStatus.Failed, snapshot.Status, "超时状态");
        AssertEqual("请求超时", snapshot.ErrorMessage, "超时消息");
        Assert(snapshot.TemperatureCelsius is null, "超时无数据");
        Assert(service.NextRetryAt is not null, "失败后记录退避时间");
        Assert(!service.ShouldAttemptRefresh(DateTimeOffset.Now), "退避期间不允许快速重试");
    }

    public static void ErrorResponse()
    {
        var stub = new StubHttpMessageHandler
        {
            Behavior = (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.InternalServerError))
        };
        var service = new WeatherService(stub);
        var snapshot = Await(service.RefreshAsync("北京", 39.9, 116.4));

        AssertEqual(WeatherStatus.Failed, snapshot.Status, "500 状态");
        AssertEqual("网络请求失败", snapshot.ErrorMessage, "500 消息");
    }

    public static void CacheFallback()
    {
        var stub = new StubHttpMessageHandler
        {
            Behavior = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ForecastJson)
            })
        };
        var service = new WeatherService(stub);
        var fresh = Await(service.RefreshAsync("北京", 39.9, 116.4));
        AssertEqual(WeatherStatus.Fresh, fresh.Status, "首次成功");

        stub.Behavior = (_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var snapshot = Await(service.RefreshAsync("北京", 39.9, 116.4));

        AssertEqual(WeatherStatus.CachedFallback, snapshot.Status, "缓存回退状态");
        Assert(snapshot.FromCache, "标记为缓存");
        AssertEqual(31.5, snapshot.TemperatureCelsius, "缓存保留温度");
        AssertEqual("局部多云", snapshot.Condition, "缓存保留现象");
        AssertEqual(fresh.UpdatedAt, snapshot.UpdatedAt, "缓存保留原更新时间");
        AssertEqual("网络请求失败", snapshot.ErrorMessage, "缓存附带失败原因");
    }

    public static void LocationChangeDropsOldCache()
    {
        var stub = new StubHttpMessageHandler
        {
            Behavior = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ForecastJson)
            })
        };
        var service = new WeatherService(stub);
        Await(service.RefreshAsync("北京", 39.9, 116.4));

        stub.Behavior = (_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var cached = Await(service.RefreshAsync("北京", 39.9, 116.4));
        AssertEqual(WeatherStatus.CachedFallback, cached.Status, "同城市失败使用缓存");
        Assert(
            service.ShouldAttemptRefresh(DateTimeOffset.Now, "上海", 31.23, 121.47),
            "切换城市不应继承旧城市退避");

        var changed = Await(service.RefreshAsync("上海", 31.23, 121.47));
        AssertEqual(WeatherStatus.Failed, changed.Status, "新城市失败不得显示旧城市缓存");
        AssertEqual("上海", changed.City, "失败状态保留新城市");
        Assert(changed.TemperatureCelsius is null, "新城市失败不得保留旧温度");
        Assert(
            service.GetLastSnapshot("北京", 39.9, 116.4) is null,
            "切换城市后旧城市缓存不应作为当前快照");
    }

    public static void BackoffSchedule()
    {
        AssertEqual(TimeSpan.Zero, WeatherService.ComputeRetryDelay(0), "无失败不退避");
        AssertEqual(TimeSpan.FromMinutes(1), WeatherService.ComputeRetryDelay(1), "第 1 次失败");
        AssertEqual(TimeSpan.FromMinutes(2), WeatherService.ComputeRetryDelay(2), "第 2 次失败");
        AssertEqual(TimeSpan.FromMinutes(4), WeatherService.ComputeRetryDelay(3), "第 3 次失败");
        AssertEqual(TimeSpan.FromMinutes(8), WeatherService.ComputeRetryDelay(4), "第 4 次失败");
        AssertEqual(TimeSpan.FromMinutes(16), WeatherService.ComputeRetryDelay(5), "第 5 次失败");
        AssertEqual(TimeSpan.FromMinutes(30), WeatherService.ComputeRetryDelay(6), "第 6 次封顶");
        AssertEqual(TimeSpan.FromMinutes(30), WeatherService.ComputeRetryDelay(12), "持续失败保持封顶");
    }

    public static void BackoffBlocksRapidRetry()
    {
        var stub = new StubHttpMessageHandler
        {
            Behavior = (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.InternalServerError))
        };
        var service = new WeatherService(stub);
        Await(service.RefreshAsync("北京", 39.9, 116.4));

        var now = DateTimeOffset.Now;
        Assert(!service.ShouldAttemptRefresh(now), "失败后立即重试被拦截");
        Assert(service.ShouldAttemptRefresh(now.AddMinutes(1).AddSeconds(1)), "退避结束后放行");

        stub.Behavior = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ForecastJson)
        });
        Await(service.RefreshAsync("北京", 39.9, 116.4));
        Assert(service.ShouldAttemptRefresh(DateTimeOffset.Now), "成功后清除退避");
    }

    public static void EmptyCitySkipsNetwork()
    {
        var stub = new StubHttpMessageHandler();
        var service = new WeatherService(stub);
        var snapshot = Await(service.RefreshAsync("", 0, 0));

        AssertEqual(WeatherStatus.NoCity, snapshot.Status, "空城市状态");
        AssertEqual(0, stub.CallCount, "空城市不发起请求");
    }

    public static void RequestUrlShape()
    {
        var url = WeatherService.BuildRequestUrl(39.9, 116.4);
        Assert(url.StartsWith(WeatherService.ForecastEndpoint, StringComparison.Ordinal), "使用 Open-Meteo 端点");
        Assert(url.Contains("latitude=39.9"), "纬度参数");
        Assert(url.Contains("longitude=116.4"), "经度参数");
        Assert(url.Contains("current=temperature_2m"), "当前天气字段");
        Assert(url.Contains("daily=temperature_2m_max"), "每日字段");
        Assert(url.Contains("forecast_days=1"), "只取一天");
    }

    public static void WeatherCodeMapping()
    {
        AssertEqual("晴", WeatherService.DescribeWeatherCode(0), "晴");
        AssertEqual("阴", WeatherService.DescribeWeatherCode(3), "阴");
        AssertEqual("小雨", WeatherService.DescribeWeatherCode(61), "小雨");
        AssertEqual("大雪", WeatherService.DescribeWeatherCode(75), "大雪");
        AssertEqual("雷暴", WeatherService.DescribeWeatherCode(95), "雷暴");
        AssertEqual("未知天气", WeatherService.DescribeWeatherCode(999), "未知代码");
    }

    public static void MalformedJson()
    {
        var stub = new StubHttpMessageHandler
        {
            Behavior = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("这不是 JSON")
            })
        };
        var service = new WeatherService(stub);
        var snapshot = Await(service.RefreshAsync("北京", 39.9, 116.4));

        AssertEqual(WeatherStatus.Failed, snapshot.Status, "非法 JSON 状态");
        AssertEqual("返回数据无法解析", snapshot.ErrorMessage, "非法 JSON 消息");
    }

    private static T Await<T>(Task<T> task)
    {
        PumpUntil(() => task.IsCompleted, TimeSpan.FromSeconds(10), "异步操作完成");
        return task.GetAwaiter().GetResult();
    }
}
