using System.Net;
using System.Net.Http;
using BingLan.Core.Services;
using static BingLan.InformationTests.TestSupport;

namespace BingLan.InformationTests;

// 城市搜索全部通过离线 HTTP 桩验证，不访问真实网络。
internal static class CitySearchTests
{
    private const string SearchJson = """
        [
          {
            "place_id": 231468810,
            "lat": "30.1861",
            "lon": "120.2597",
            "name": "萧山区",
            "display_name": "萧山区, 杭州市, 浙江省, 311200, 中国",
            "address": { "city": "萧山区", "state": "浙江省", "country": "中国" }
          },
          {
            "place_id": 1,
            "lat": "999",
            "lon": "116.4",
            "name": "无效坐标",
            "display_name": "无效坐标, 中国"
          }
        ]
        """;

    public static void SuccessParseAndUrl()
    {
        Uri? requestedUri = null;
        string? lastUserAgent = null;
        var stub = new StubHttpMessageHandler
        {
            Behavior = (request, _) =>
            {
                requestedUri = request.RequestUri;
                lastUserAgent = request.Headers.UserAgent.ToString();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SearchJson)
                });
            }
        };
        var service = new CitySearchService(stub);
        var outcome = Await(service.SearchAsync("  萧山  "));

        Assert(outcome.Succeeded, "搜索成功状态");
        AssertEqual(1, outcome.Results.Count, "过滤非法坐标结果");
        var result = outcome.Results[0];
        AssertEqual("萧山区 · 杭州市 · 浙江省 · 中国", result.DisplayName, "候选显示上级行政区，省略邮编");
        AssertEqual(30.1861, result.Latitude, "候选纬度");
        AssertEqual(120.2597, result.Longitude, "候选经度");
        AssertEqual(1, stub.CallCount, "只请求一次");
        Assert(requestedUri is not null, "记录请求地址");
        var url = requestedUri!.AbsoluteUri;
        Assert(url.StartsWith(CitySearchService.GeocodingEndpoint, StringComparison.Ordinal),
            "使用 Nominatim 地理编码端点");
        Assert(url.Contains("q=%E8%90%A7%E5%B1%B1&", StringComparison.OrdinalIgnoreCase),
            "城市关键词去除空白并进行 URL 编码");
        Assert(url.Contains("limit=8", StringComparison.Ordinal), "候选数量参数");
        Assert(url.Contains("accept-language=zh-CN", StringComparison.Ordinal), "中文结果参数");
        Assert(url.Contains("featureType=settlement", StringComparison.Ordinal), "只搜索居民点");
        Assert(url.Contains("format=jsonv2", StringComparison.Ordinal), "JSON 格式参数");
        Assert(lastUserAgent?.Contains("BingLan", StringComparison.Ordinal) == true,
            "请求带应用标识");
    }

    public static void ShortQuerySkipsNetwork()
    {
        var stub = new StubHttpMessageHandler();
        var service = new CitySearchService(stub);
        var outcome = Await(service.SearchAsync("北"));

        Assert(!outcome.Succeeded, "单字符不发起搜索");
        Assert(outcome.Message.Contains("2 个字符"), "单字符提示明确");
        AssertEqual(0, stub.CallCount, "单字符不联网");
    }

    public static void EmptyResults()
    {
        var stub = new StubHttpMessageHandler
        {
            Behavior = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            })
        };
        var outcome = Await(new CitySearchService(stub).SearchAsync("不存在城市"));

        Assert(outcome.Succeeded, "空结果仍是成功请求");
        AssertEqual(0, outcome.Results.Count, "空结果列表");
        Assert(outcome.Message.Contains("没有找到"), "空结果提示");
    }

    // Nominatim 使用政策：每秒最多 1 次请求。
    public static void ConsecutiveSearchesAreSpaced()
    {
        var times = new List<DateTimeOffset>();
        var stub = new StubHttpMessageHandler
        {
            Behavior = (_, _) =>
            {
                times.Add(DateTimeOffset.UtcNow);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                });
            }
        };
        var service = new CitySearchService(stub);
        Await(service.SearchAsync("北京"));
        Await(service.SearchAsync("上海"));

        AssertEqual(2, times.Count, "两次搜索都发出请求");
        Assert(times[1] - times[0] >= CitySearchService.MinimumRequestInterval - TimeSpan.FromMilliseconds(20),
            "相邻请求至少间隔 1 秒");
    }

    public static void FailuresAreQuiet()
    {
        var httpStub = new StubHttpMessageHandler
        {
            Behavior = (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.InternalServerError))
        };
        var httpFailure = Await(new CitySearchService(httpStub).SearchAsync("北京"));
        Assert(!httpFailure.Succeeded, "HTTP 错误返回失败状态");
        Assert(httpFailure.Message.Contains("网络请求失败"), "HTTP 错误提示");

        var jsonStub = new StubHttpMessageHandler
        {
            Behavior = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("不是 JSON")
            })
        };
        var jsonFailure = Await(new CitySearchService(jsonStub).SearchAsync("北京"));
        Assert(!jsonFailure.Succeeded, "非法 JSON 返回失败状态");
        Assert(jsonFailure.Message.Contains("无法解析"), "非法 JSON 提示");
    }

    public static void TimeoutIsQuiet()
    {
        var stub = new StubHttpMessageHandler
        {
            Behavior = async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        };
        var outcome = Await(new CitySearchService(
            stub,
            TimeSpan.FromMilliseconds(200)).SearchAsync("北京"));

        Assert(!outcome.Succeeded, "城市搜索超时返回失败状态");
        Assert(outcome.Message.Contains("搜索超时"), "城市搜索超时提示");
    }

    private static T Await<T>(Task<T> task)
    {
        PumpUntil(() => task.IsCompleted, TimeSpan.FromSeconds(10), "城市搜索完成");
        return task.GetAwaiter().GetResult();
    }
}
