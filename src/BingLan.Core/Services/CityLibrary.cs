using System.Reflection;
using System.Text.Json;
using BingLan.Core.Models;

namespace BingLan.Core.Services;

// 内置离线城市库：省、市、区县的中文名称与坐标，搜索完全在本地完成，不联网。
// 数据来源、生成脚本与许可证记录见 docs/CITY-DATA.md；本地无匹配时由
// CityLookupService 转向在线搜索，本类不感知网络。
public sealed class CityLibrary
{
    private const string ResourceName = "BingLan.Core.Assets.city-library.cn.json";
    private const int SupportedSchema = 1;

    private readonly List<CityLibraryEntry> _entries;

    private CityLibrary(List<CityLibraryEntry> entries) => _entries = entries;

    public int Count => _entries.Count;

    // 测试用空库：让查找服务保持“无本地匹配即转在线”的旧行为。
    public static CityLibrary Empty { get; } = Parse("""{"schema":1,"entries":[]}""");

    public static CityLibrary LoadDefault()
    {
        using var stream = typeof(CityLibrary).Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"找不到内嵌城市库资源 {ResourceName}。");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static CityLibrary Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("schema", out var schemaElement) ||
            !schemaElement.TryGetInt32(out var schema) || schema != SupportedSchema)
        {
            throw new JsonException("城市库结构版本不受支持");
        }

        var entries = new List<CityLibraryEntry>();
        foreach (var item in root.GetProperty("entries").EnumerateArray())
        {
            var name = item.GetProperty("n").GetString()?.Trim();
            var path = item.TryGetProperty("p", out var pathElement)
                ? pathElement.GetString()?.Trim() ?? ""
                : "";
            var latitude = item.GetProperty("la").GetDouble();
            var longitude = item.GetProperty("lo").GetDouble();
            if (string.IsNullOrWhiteSpace(name) ||
                latitude is < -90d or > 90d ||
                longitude is < -180d or > 180d)
            {
                continue;
            }
            entries.Add(new CityLibraryEntry(name, path, latitude, longitude));
        }
        return new CityLibrary(entries);
    }

    // 匹配优先级：名称相等 > 名称前缀 > 名称包含 > 全路径包含。单关键词支持“杭州西湖”
    // 这类市名连写区县（无命中时拆成两段再匹配）；多个关键词（如“朝阳 北京”）要求全部
    // 命中并按总分排序。同分保持条目顺序：直辖市与地级市在前、区县在后，
    // 同名区县（如两个西湖区）同时返回。
    public IReadOnlyList<CitySearchResult> Search(string query, int maxResults = 8)
    {
        var tokens = Tokenize(query);
        if (tokens.Count == 0)
        {
            return [];
        }

        var matches = CollectMatches(tokens);
        if (matches is null && tokens.Count == 1 && tokens[0].Length >= 4)
        {
            matches = CollectSplitMatches(tokens[0]);
        }
        if (matches is null)
        {
            return [];
        }

        matches.Sort(static (left, right) =>
            left.Score != right.Score ? right.Score.CompareTo(left.Score) : left.Index.CompareTo(right.Index));
        var limit = Math.Clamp(maxResults, 1, 40);
        var results = new List<CitySearchResult>(Math.Min(limit, matches.Count));
        for (var index = 0; index < matches.Count && results.Count < limit; index++)
        {
            results.Add(ToResult(matches[index].Entry));
        }
        return results;
    }

    private List<(int Score, int Index, CityLibraryEntry Entry)>? CollectMatches(
        List<string> tokens)
    {
        List<(int Score, int Index, CityLibraryEntry Entry)>? matches = null;
        for (var index = 0; index < _entries.Count; index++)
        {
            var score = ScoreEntry(_entries[index], tokens);
            if (score > 0)
            {
                (matches ??= []).Add((score, index, _entries[index]));
            }
        }
        return matches;
    }

    // “杭州西湖”“北京朝阳区”这类不带空格的连写：整词无命中时按每种两段拆法
    // 重新匹配，同一条目取各拆法中的最高分。
    private List<(int Score, int Index, CityLibraryEntry Entry)>? CollectSplitMatches(string token)
    {
        List<(int Score, int Index, CityLibraryEntry Entry)>? matches = null;
        var bestScores = new Dictionary<CityLibraryEntry, int>();
        for (var split = 2; split <= token.Length - 2; split++)
        {
            var parts = new List<string> { token[..split], token[split..] };
            for (var index = 0; index < _entries.Count; index++)
            {
                var score = ScoreEntry(_entries[index], parts);
                if (score <= 0)
                {
                    continue;
                }
                if (!bestScores.TryGetValue(_entries[index], out var best) || score > best)
                {
                    bestScores[_entries[index]] = score;
                    matches ??= [];
                    matches.RemoveAll(match => ReferenceEquals(match.Entry, _entries[index]));
                    matches.Add((score, index, _entries[index]));
                }
            }
        }
        return matches;
    }

    // 单个关键词在 2 字以上才参与匹配；空格、全角空格与制表符分隔多个关键词。
    private static List<string> Tokenize(string query)
    {
        return (query ?? "")
            .Split(' ', '\u3000', '\t')
            .Select(token => token.Trim())
            .Where(token => token.Length >= 2)
            .ToList();
    }

    private static int ScoreEntry(CityLibraryEntry entry, List<string> tokens)
    {
        var total = 0;
        foreach (var token in tokens)
        {
            var score = ScoreToken(entry, token);
            if (score <= 0)
            {
                // 多关键词是过滤条件：任一关键词未命中即排除该条目。
                return 0;
            }
            total += score;
        }
        return total;
    }

    // 中文地名不含空白；关键词与预计算的两种拼接（名称在前/路径在前）比较。
    private static int ScoreToken(CityLibraryEntry entry, string token)
    {
        if (entry.Name == token)
        {
            return 4;
        }
        if (entry.Name.StartsWith(token, StringComparison.Ordinal))
        {
            return 3;
        }
        if (entry.Name.Contains(token, StringComparison.Ordinal))
        {
            return 2;
        }
        return string.IsNullOrEmpty(entry.Region)
            ? 0
            : entry.NamePlusPath.Contains(token, StringComparison.Ordinal) ||
              entry.PathPlusName.Contains(token, StringComparison.Ordinal)
                ? 1
                : 0;
    }

    private static CitySearchResult ToResult(CityLibraryEntry entry) => new(
        0,
        entry.Name,
        entry.Region,
        "中国",
        entry.Latitude,
        entry.Longitude);

    private sealed record CityLibraryEntry(string Name, string Path, double Latitude, double Longitude)
    {
        // 展示用上级行政区（“浙江省 · 杭州市”）与匹配用的去分隔拼接在构造时一次算好；
        // 记录属性初始化器不能引用其他实例属性，公共前缀走静态方法。
        public string Region { get; } = Path.Replace(' ', '·');

        public string NamePlusPath { get; } = string.Concat(Name, CompactPath(Path));

        public string PathPlusName { get; } = string.Concat(CompactPath(Path), Name);

        private static string CompactPath(string path) => path.Replace(" ", "").Replace("·", "");
    }
}
