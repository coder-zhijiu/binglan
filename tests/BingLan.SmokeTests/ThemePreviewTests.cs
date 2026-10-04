using System.IO.Compression;
using System.Linq;
using System.Text;
using BingLan.Core.Models;
using BingLan.Core.Services;
using BingLan.Core.Themes;

/// <summary>
/// Core-layer tests for the optional theme preview entry. Rendering the schematic PNG
/// itself lives in the App layer (WPF), so these only exercise ThemeArchive's write/read
/// contract for "preview.png": accept a valid PNG, and treat a malformed or oversized one
/// as "no preview" rather than failing the whole theme import — a broken preview must
/// never block using an otherwise valid theme package.
/// </summary>
internal static class ThemePreviewTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    internal static void TestValidPreviewRoundTrips()
    {
        var state = LocalStateStore.CreateDefault();
        var package = ThemeRules.Export(
            state, "预览主题", new ThemeArea(0, 0, 1920, 1040), new Version(0, 1, 0));
        var preview = BuildMinimalPng();

        using var stream = new MemoryStream();
        ThemeArchive.Write(stream, package, preview);
        stream.Position = 0;

        var result = ThemeArchive.Read(stream, new Version(0, 1, 0));
        Assert(result.Succeeded, $"带预览图的主题包应能读取：{result.Error}");
        Assert(result.PreviewPng is not null, "有效预览图应被读取");
        Assert(result.PreviewPng!.SequenceEqual(preview), "读取到的预览图字节应与写入一致");
    }

    internal static void TestMissingPreviewIsFine()
    {
        var state = LocalStateStore.CreateDefault();
        var package = ThemeRules.Export(
            state, "无预览主题", new ThemeArea(0, 0, 1920, 1040), new Version(0, 1, 0));

        using var stream = new MemoryStream();
        ThemeArchive.Write(stream, package);
        stream.Position = 0;

        var result = ThemeArchive.Read(stream, new Version(0, 1, 0));
        Assert(result.Succeeded, $"没有预览图的主题包也应能正常读取：{result.Error}");
        Assert(result.PreviewPng is null, "没有写入预览图时读取结果应为空");
    }

    internal static void TestNonPngPreviewIsIgnored()
    {
        var appVersion = new Version(0, 1, 0);
        using var zip = BuildZipWithPreview(Encoding.UTF8.GetBytes("not a png"));

        var result = ThemeArchive.Read(zip, appVersion);

        // Chosen behaviour: a preview entry that fails the PNG signature check is ignored
        // (treated as absent), it does not reject the whole theme package.
        Assert(result.Succeeded, $"非 PNG 预览图不应导致主题包读取失败：{result.Error}");
        Assert(result.PreviewPng is null, "非 PNG 签名的预览图应被忽略");
    }

    internal static void TestOversizedPreviewIsRejected()
    {
        var appVersion = new Version(0, 1, 0);
        var oversized = new byte[ThemeArchive.MaximumPreviewBytes + 1];
        PngSignature.CopyTo(oversized, 0);
        using var zip = BuildZipWithPreview(oversized);

        var result = ThemeArchive.Read(zip, appVersion);

        Assert(result.Succeeded, $"超大预览图不应导致主题包读取失败：{result.Error}");
        Assert(result.PreviewPng is null, "超过大小限制的预览图应被拒绝");
    }

    internal static void TestExportWithPreviewStillHasNoPersonalData()
    {
        var state = LocalStateStore.CreateDefault();
        state.InformationWidgets[0].GreetingName = "张小明";
        state.InformationWidgets[0].WeatherCity = "杭州市";

        var package = ThemeRules.Export(
            state, "测试主题", new ThemeArea(0, 0, 1920, 1040), new Version(0, 1, 0));

        using var stream = new MemoryStream();
        ThemeArchive.Write(stream, package, BuildMinimalPng());
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert(archive.GetEntry("preview.png") is not null, "带预览图导出应写入 preview.png 条目");
        foreach (var entry in archive.Entries.Where(e => e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream);
            var text = reader.ReadToEnd();
            Assert(!text.Contains("张小明", StringComparison.Ordinal), "带预览图的导出仍不应包含称呼");
            Assert(!text.Contains("杭州市", StringComparison.Ordinal), "带预览图的导出仍不应包含城市");
        }
    }

    internal static byte[] BuildMinimalPng() =>
        // A tiny but structurally valid 1x1 PNG, enough to exercise the signature check
        // without depending on WPF rendering from a Core-layer test.
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        ];

    private static MemoryStream BuildZipWithPreview(byte[] previewBytes)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteTextEntry(
                archive,
                "manifest.json",
                "{\"Format\":\"binglan-theme\",\"SchemaVersion\":1,\"Name\":\"测试\",\"MinimumAppVersion\":\"0.1.0\"}");
            WriteTextEntry(archive, "tokens.json", "{}");
            WriteTextEntry(archive, "layout.json", "{}");
            WriteTextEntry(archive, "bindings.json", "{}");

            var previewEntry = archive.CreateEntry("preview.png");
            using var previewStream = previewEntry.Open();
            previewStream.Write(previewBytes, 0, previewBytes.Length);
        }
        stream.Position = 0;
        return stream;
    }

    private static void WriteTextEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var entryStream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        entryStream.Write(bytes, 0, bytes.Length);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
