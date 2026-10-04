using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BingLan.Core.Themes;

public sealed record ThemeReadResult(
    ThemePackage? Package,
    string Error,
    byte[]? PreviewPng = null,
    IReadOnlyDictionary<string, byte[]>? Icons = null)
{
    public bool Succeeded => Package is not null;
}

/// <summary>
/// Reads and writes theme packages (a zip of JSON files). Reading is defensive: size and
/// entry limits, no nested paths, no executable or script content, known files only.
/// </summary>
public static class ThemeArchive
{
    public const int CurrentSchemaVersion = 1;
    public const string FileExtension = ".binglan-theme";
    public const long MaximumArchiveBytes = 5 * 1024 * 1024;
    public const long MaximumEntryBytes = 256 * 1024;
    public const long MaximumPreviewBytes = 1024 * 1024;
    public const long MaximumIconBytes = 256 * 1024;
    public const int MaximumEntries = 32;

    private const string ManifestEntry = "manifest.json";
    private const string TokensEntry = "tokens.json";
    private const string LayoutEntry = "layout.json";
    private const string BindingsEntry = "bindings.json";
    private const string PreviewEntry = "preview.png";

    // A schematic preview is a rendered PNG, never a screenshot, so this is the only
    // signature it needs to satisfy.
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly string[] BlockedExtensions =
    [
        ".exe", ".dll", ".com", ".scr", ".msi", ".bat", ".cmd", ".ps1", ".psm1", ".vbs",
        ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".lnk", ".url", ".reg", ".jar", ".py"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        // Theme files are meant to stay readable when opened by hand.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Writes the package, plus an optional schematic preview PNG (never a screenshot) and
    /// the custom dock icons its bindings name. Returns whether the preview was written;
    /// an icon that is not a valid PNG within the size limit is left out and its binding
    /// falls back to the app's own icon.
    /// </summary>
    public static bool Write(
        Stream destination,
        ThemePackage package,
        byte[]? previewPng = null,
        IReadOnlyDictionary<string, byte[]>? icons = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(package);

        foreach (var binding in package.Bindings.Apps)
        {
            if (binding.IconEntry is { } entryName
                && (icons is null || !icons.TryGetValue(entryName, out var bytes) || !IsValidPng(bytes, MaximumIconBytes)))
            {
                binding.IconEntry = null;
            }
        }

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        WriteEntry(archive, ManifestEntry, package.Manifest);
        WriteEntry(archive, TokensEntry, package.Tokens);
        WriteEntry(archive, LayoutEntry, package.Layout);
        WriteEntry(archive, BindingsEntry, package.Bindings);

        foreach (var entryName in package.Bindings.Apps
                     .Select(binding => binding.IconEntry)
                     .OfType<string>()
                     .Distinct(StringComparer.Ordinal))
        {
            WriteBytes(archive, entryName, icons![entryName]);
        }

        var previewWritten = IsValidPreview(previewPng);
        if (previewWritten)
        {
            WriteBytes(archive, PreviewEntry, previewPng!);
        }
        return previewWritten;
    }

    private static void WriteBytes(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    public static ThemeReadResult Read(Stream source, Version appVersion)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(appVersion);

        if (source.CanSeek && source.Length > MaximumArchiveBytes)
        {
            return Fail("主题包超过 5 MB");
        }

        MemoryStream? buffered = null;
        try
        {
            if (!source.CanSeek)
            {
                // Read at most one buffer past the limit so an endless stream cannot fill memory.
                buffered = new MemoryStream();
                var buffer = new byte[81920];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    buffered.Write(buffer, 0, read);
                    if (buffered.Length > MaximumArchiveBytes)
                    {
                        return Fail("主题包超过 5 MB");
                    }
                }
                buffered.Position = 0;
                source = buffered;
            }

            using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > MaximumEntries)
            {
                return Fail("主题包包含的文件过多");
            }

            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName;
                if (name.Contains("..", StringComparison.Ordinal)
                    || Path.IsPathRooted(name)
                    || name.Contains(':', StringComparison.Ordinal))
                {
                    return Fail($"主题包包含不安全的路径：{name}");
                }

                if (BlockedExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
                {
                    return Fail($"主题包包含程序或脚本文件：{name}");
                }

                // "preview.png" is the only entry ever read as an image; any other name
                // (including one with a .png extension) is simply never opened, exactly
                // like other unknown entries.
            }

            var manifest = ReadEntry<ThemeManifest>(archive, ManifestEntry);
            if (manifest is null || manifest.Format != ThemeManifest.FormatName)
            {
                return Fail("这不是冰蓝桌面主题包");
            }

            if (manifest.SchemaVersion is < 1 or > CurrentSchemaVersion)
            {
                return Fail("主题包版本较新，请先更新冰蓝桌面");
            }

            if (ManifestProblem(manifest) is { } problem)
            {
                return Fail(problem);
            }

            if (Version.Parse(manifest.MinimumAppVersion) > appVersion)
            {
                return Fail($"主题包需要冰蓝桌面 {manifest.MinimumAppVersion} 或更新版本");
            }

            var tokens = ReadEntry<ThemeTokens>(archive, TokensEntry);
            var layout = ReadEntry<ThemeLayout>(archive, LayoutEntry);
            if (tokens is null || layout is null)
            {
                return Fail("主题包缺少外观或布局内容");
            }

            var package = new ThemePackage
            {
                Manifest = manifest,
                Tokens = tokens,
                Layout = layout,
                Bindings = ReadEntry<ThemeBindings>(archive, BindingsEntry) ?? new ThemeBindings()
            };
            ThemeRules.Normalize(package);
            return new ThemeReadResult(package, string.Empty, ReadPreview(archive), ReadIcons(archive, package));
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException
            or NotSupportedException or IOException)
        {
            return Fail($"无法读取主题包：{exception.Message}");
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    private static ThemeReadResult Fail(string error) => new(null, error);

    /// <summary>Why the manifest cannot be used, or null when every required field is valid.</summary>
    private static string? ManifestProblem(ThemeManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id)
            || manifest.Id.Length > 64
            || !manifest.Id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            return "主题包缺少有效的主题 ID";
        }
        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            return "主题包缺少主题名称";
        }
        if (!Version.TryParse(manifest.ThemeVersion, out _))
        {
            return "主题包的主题版本号无效";
        }
        if (!Version.TryParse(manifest.MinimumAppVersion, out _))
        {
            return "主题包的最低应用版本号无效";
        }
        return null;
    }

    /// <summary>
    /// Reads the custom icons the bindings name. A missing, oversized or non-PNG icon is
    /// dropped and its binding falls back to the app's own icon.
    /// </summary>
    private static IReadOnlyDictionary<string, byte[]> ReadIcons(ZipArchive archive, ThemePackage package)
    {
        var icons = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var binding in package.Bindings.Apps)
        {
            if (binding.IconEntry is not { } name)
            {
                continue;
            }
            if (!icons.ContainsKey(name))
            {
                var entry = archive.GetEntry(name);
                if (icons.Count < ThemeRules.MaximumIcons
                    && entry is { Length: > 0 and <= MaximumIconBytes })
                {
                    var bytes = ReadBytes(entry, MaximumIconBytes);
                    if (IsValidPng(bytes, MaximumIconBytes))
                    {
                        icons[name] = bytes;
                    }
                }
            }
            if (!icons.ContainsKey(name))
            {
                binding.IconEntry = null;
            }
        }
        return icons;
    }

    private static void WriteEntry<T>(ZipArchive archive, string name, T value)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, value, JsonOptions);
    }

    private static T? ReadEntry<T>(ZipArchive archive, string name)
        where T : class
    {
        var entry = archive.GetEntry(name);
        if (entry is null)
        {
            return null;
        }

        if (entry.Length > MaximumEntryBytes)
        {
            throw new InvalidDataException($"{name} 过大");
        }

        // The stream overload also skips a byte order mark left by hand editing.
        using var bounded = new MemoryStream(ReadBytes(entry, MaximumEntryBytes));
        return JsonSerializer.Deserialize<T>(bounded, JsonOptions);
    }

    /// <summary>
    /// Reads the optional preview image. Missing, oversized or non-PNG content is treated
    /// as "no preview" rather than a reason to reject the whole theme package.
    /// </summary>
    private static byte[]? ReadPreview(ZipArchive archive)
    {
        var entry = archive.GetEntry(PreviewEntry);
        if (entry is null || entry.Length <= 0 || entry.Length > MaximumPreviewBytes)
        {
            return null;
        }

        var bytes = ReadBytes(entry, MaximumPreviewBytes);
        return IsValidPreview(bytes) ? bytes : null;
    }

    // Reads at most maximumBytes, whatever size the entry's header claims.
    private static byte[] ReadBytes(ZipArchiveEntry entry, long maximumBytes)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            if (memory.Length + read > maximumBytes)
            {
                throw new InvalidDataException($"{entry.FullName} 过大");
            }
            memory.Write(buffer, 0, read);
        }
        return memory.ToArray();
    }

    private static bool IsValidPreview(byte[]? preview) => IsValidPng(preview, MaximumPreviewBytes);

    /// <summary>Largest width or height accepted, so a tiny file cannot unpack into a huge bitmap.</summary>
    public const int MaximumPngSide = 2048;

    /// <summary>
    /// A PNG within the byte limit whose header declares a size up to MaximumPngSide on each
    /// side; the pixels themselves are checked by the decoder.
    /// </summary>
    public static bool IsValidPng(byte[]? bytes, long maximumBytes)
    {
        // Signature (8 bytes), then the IHDR chunk: length, type, width, height (big-endian).
        if (bytes is not { Length: >= 24 }
            || bytes.Length > maximumBytes
            || !bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            return false;
        }
        var width = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        var height = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        return width is > 0 and <= MaximumPngSide && height is > 0 and <= MaximumPngSide;
    }
}
