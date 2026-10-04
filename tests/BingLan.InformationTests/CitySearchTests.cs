using System.Net;
using System.Net.Http;
using BingLan.Core.Services;
using static BingLan.InformationTests.TestSupport;

namespace BingLan.InformationTests;

// 城市搜索全部通过离线 HTTP 桩验证，不访问真实网络。
internal static class CitySearchTests
{
    private const string SearchJson = """
        {
          "results": [
            {
              "id": 1816670,
              "name": "北京",
              "latitude": 39.9075,
              "longitude": 116.3972,
              "country": "中国",
              "admin1": "北京市"
            },
            {
              "id": 0,
              "name": "无效坐标",
              "latitude": 999,
              "longitude": 116.4,
              "country": "中国"
            }
          ]
        }
        """;

    public static void SuccessParseAndUrl()
    {
        Uri? requestedUri = null;
        var stub = new StubHttpMessageHandler
        {
            Behavior = (request, _) =>
            {
                requestedUri = request.RequestUri;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SearchJson)
                });
            }
        };
        var service = new CitySearchService(stub);
        var outcome = Await(service.SearchAsync("  北京  "));

        Assert(outcome.Succeeded, "搜索成功状态");
        AssertEqual(1, outcome.Results.Count, "过滤非法坐标结果");
        var result = outcome.Results[0];
        AssertEqual("北京 · 北京市 · 中国", result.DisplayName, "候选显示名称");
        AssertEqual(39.9075, result.Latitude, "候选纬度");
        AssertEqual(116.3972, result.Longitude, "候选经度");
        AssertEqual(1, stub.CallCount, "只请求一次");
        Assert(requestedUri is not null, "记录请求地址");
        var url = requestedUri!.AbsoluteUri;
        Assert(url.StartsWith(CitySearchService.GeocodingEndpoint, StringComparison.Ordinal),
            "使用 Open-Meteo 地理编码端点");
        Assert(url.Contains("name=%E5%8C%97%E4%BA%AC", StringComparison.OrdinalIgnoreCase),
            "城市关键词进行 URL 编码");
        Assert(url.Contains("count=8", StringComparison.Ordinal), "候选数量参数");
        Assert(url.Contains("language=zh", StringComparison.Ordinal), "中文结果参数");
        Assert(url.Contains("format=json", StringComparison.Ordinal), "JSON 格式参数");
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
                Content = new StringContent("{}")
            })
        };
        var outcome = Await(new CitySearchService(stub).SearchAsync("不存在城市"));

        Assert(outcome.Succeeded, "空结果仍是成功请求");
        AssertEqual(0, outcome.Results.Count, "空结果列表");
        Assert(outcome.Message.Contains("没有找到"), "空结果提示");
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
