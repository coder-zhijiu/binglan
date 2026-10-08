using System.Net;
using System.Net.Http;
using BingLan.Core.Services;
using static BingLan.InformationTests.TestSupport;

namespace BingLan.InformationTests;

// 内置离线城市库与本地优先查找：全部离线验证，不访问真实网络。
internal static class CityLibraryTests
{
    private const string SampleJson = """
        {
          "schema": 1,
          "entries": [
            { "n": "北京市", "p": "北京市", "la": 39.9, "lo": 116.4 },
            { "n": "杭州市", "p": "浙江省", "la": 30.27, "lo": 120.16 },
            { "n": "南昌市", "p": "江西省", "la": 28.68, "lo": 115.86 },
            { "n": "西湖区", "p": "浙江省 杭州市", "la": 30.28, "lo": 120.15 },
            { "n": "西湖区", "p": "江西省 南昌市", "la": 28.67, "lo": 115.87 },
            { "n": "朝阳区", "p": "北京市", "la": 39.92, "lo": 116.44 },
            { "n": "朝阳区", "p": "吉林省 长春市", "la": 43.83, "lo": 125.28 },
            { "n": "香港", "p": "", "la": 22.32, "lo": 114.17 },
            { "n": "坏坐标区", "p": "某省", "la": 999, "lo": 0 },
            { "n": "", "p": "某省", "la": 10, "lo": 10 }
          ]
        }
        """;

    public static void EmbeddedLibraryLoads()
    {
        var library = CityLibrary.LoadDefault();

        Assert(library.Count > 3000, $"内嵌城市库条目数充足（实际 {library.Count}）");
        var hangzhou = library.Search("杭州市");
        Assert(hangzhou.Count > 0 && hangzhou[0].Name == "杭州市", "内嵌城市库包含杭州市");
        Assert(hangzhou[0].Region.Contains("浙江省"), "内嵌城市库带省份路径");
    }

    public static void SearchRanksAndDisambiguates()
    {
        var library = CityLibrary.Parse(SampleJson);

        // 名称精确与前缀排在路径匹配之前。
        var hangzhou = library.Search("杭州市");
        AssertEqual("杭州市", hangzhou[0].Name, "精确名称排第一");

        // 前缀查询先返回城市，再按路径命中其区县。
        var prefix = library.Search("杭州");
        AssertEqual("杭州市", prefix[0].Name, "前缀命中城市在前");
        Assert(prefix.Any(result => result.Name == "西湖区" && result.Region.Contains("杭州市")),
            "前缀查询按路径带回区县");

        // 同名区县成对返回，坐标各归其位。
        var westLakes = library.Search("西湖区");
        AssertEqual(2, westLakes.Count, "同名区县都返回");
        var hangzhouWestLake = westLakes.Single(result => result.Region.Contains("杭州市"));
        AssertEqual(120.15, hangzhouWestLake.Longitude, "杭州西湖区坐标");
        var nanchangWestLake = westLakes.Single(result => result.Region.Contains("南昌市"));
        AssertEqual(115.87, nanchangWestLake.Longitude, "南昌西湖区坐标");

        // “省 + 区县”空格分隔与“市 + 区县”连写都能按全路径命中。
        Assert(library.Search("浙江 西湖").Any(result => result.Name == "西湖区"), "省名加区县可命中");
        Assert(library.Search("杭州西湖").Any(result => result.Name == "西湖区"), "市名连写区县可命中");

        // 多关键词过滤：同名区县按关键词所在地排序。
        var beijingChaoyang = library.Search("朝阳 北京");
        AssertEqual("朝阳区", beijingChaoyang[0].Name, "多关键词命中目标区县");
        Assert(beijingChaoyang.All(result => result.Region.Contains("北京") || result.Name.Contains("北京")),
            "多关键词排除不含关键词的候选");

        // 无路径条目（香港）仍按名称命中。
        Assert(library.Search("香港").Any(result => result.Name == "香港"), "无路径条目可命中");
        AssertEqual("中国", library.Search("香港")[0].Country, "本地结果国家为中国");
    }

    public static void SearchLimitsAndRejects()
    {
        var library = CityLibrary.Parse(SampleJson);

        AssertEqual(1, library.Search("西湖区", 1).Count, "结果数量受上限约束");
        AssertEqual(0, library.Search("北").Count, "单字符不搜索");
        AssertEqual(0, library.Search("  ").Count, "空白查询不搜索");
        AssertEqual(0, library.Search("不存在的地名").Count, "无匹配返回空列表");

        try
        {
            CityLibrary.Parse("""{"schema": 2, "entries": []}""");
            Assert(false, "结构版本不符时应抛出 JsonException");
        }
        catch (System.Text.Json.JsonException)
        {
            // 预期：结构版本不符时拒绝加载。
        }
    }

    public static void LookupPrefersLocalLibrary()
    {
        var stub = new StubHttpMessageHandler
        {
            Behavior = (_, _) => throw new InvalidOperationException("本地命中时不应联网")
        };
        var lookup = new CityLookupService(new CitySearchService(stub), CityLibrary.Parse(SampleJson));

        var outcome = Await(lookup.SearchAsync("西湖区"));

        Assert(outcome.Succeeded, "本地命中返回成功状态");
        AssertEqual(2, outcome.Results.Count, "本地命中返回同名区县");
        Assert(outcome.Message.Contains("找到"), "本地命中给出候选数量提示");
        AssertEqual(0, stub.CallCount, "本地命中不发起网络请求");
    }

    public static void LookupFallsBackOnline()
    {
        var stub = new StubHttpMessageHandler
        {
            Behavior = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    [
                      {
                        "place_id": 1547565,
                        "lat": "35.6895",
                        "lon": "139.6917",
                        "name": "东京",
                        "display_name": "东京, 日本"
                      }
                    ]
                    """)
            })
        };
        var lookup = new CityLookupService(new CitySearchService(stub), CityLibrary.Parse(SampleJson));

        var outcome = Await(lookup.SearchAsync("东京"));

        Assert(outcome.Succeeded, "本地无匹配时转在线搜索");
        AssertEqual(1, outcome.Results.Count, "在线结果正常返回");
        AssertEqual(1, stub.CallCount, "在线兜底只请求一次");
    }

    private static T Await<T>(Task<T> task)
    {
        PumpUntil(() => task.IsCompleted, TimeSpan.FromSeconds(10), "城市查找完成");
        return task.GetAwaiter().GetResult();
    }
}
