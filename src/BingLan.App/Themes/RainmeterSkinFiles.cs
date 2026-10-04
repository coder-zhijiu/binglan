using System.IO;
using System.Text;

namespace BingLan.App.Themes;

/// <summary>
/// Collects the text of a Rainmeter skin for <see cref="BingLan.Core.Themes.RainmeterSkinReader"/>:
/// the .ini files under the chosen folder and the .inc files in the skin's @Resources,
/// where shared variables usually live. Files are only read, within fixed count and size
/// limits; nothing in the skin is run.
/// </summary>
internal static class RainmeterSkinFiles
{
    private const int MaximumFiles = 120;
    private const long MaximumFileBytes = 256 * 1024;

    internal static IReadOnlyList<string> Collect(string folder)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        var paths = Directory.EnumerateFiles(folder, "*.ini", options).Take(MaximumFiles).ToList();
        if (FindResources(folder) is { } resources)
        {
            paths.AddRange(Directory.EnumerateFiles(resources, "*.inc", options).Take(MaximumFiles));
        }

        var texts = new List<string>();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumFiles))
        {
            try
            {
                if (new FileInfo(path).Length <= MaximumFileBytes)
                {
                    texts.Add(ReadText(path));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // An unreadable file is skipped; the rest still give a look.
            }
        }
        return texts;
    }

    /// <summary>The folder Rainmeter keeps skins in, when Rainmeter is installed.</summary>
    internal static string? SkinsFolder()
    {
        var settings = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rainmeter", "Rainmeter.ini");
        try
        {
            if (File.Exists(settings))
            {
                var line = ReadText(settings).Split('\n')
                    .Select(text => text.Trim())
                    .FirstOrDefault(text => text.StartsWith("SkinPath=", StringComparison.OrdinalIgnoreCase));
                if (line is not null && Directory.Exists(line["SkinPath=".Length..]))
                {
                    return line["SkinPath=".Length..];
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        var documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Rainmeter", "Skins");
        return Directory.Exists(documents) ? documents : null;
    }

    // A skin's @Resources sits in the chosen folder or a few levels above it.
    private static string? FindResources(string folder)
    {
        var current = new DirectoryInfo(folder);
        for (var level = 0; level < 4 && current is not null; level++, current = current.Parent)
        {
            var resources = Path.Combine(current.FullName, "@Resources");
            if (Directory.Exists(resources))
            {
                return resources;
            }
        }
        return null;
    }

    // Rainmeter files are UTF-16 with a byte order mark, or UTF-8.
    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var encoding = bytes switch
        {
            [0xFF, 0xFE, ..] => Encoding.Unicode,
            [0xFE, 0xFF, ..] => Encoding.BigEndianUnicode,
            _ => Encoding.UTF8
        };
        return encoding.GetString(bytes).TrimStart('\uFEFF');
    }
}
