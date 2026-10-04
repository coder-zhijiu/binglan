using System.Globalization;
using System.Text.RegularExpressions;

namespace BingLan.Core.Themes;

/// <summary>
/// The look read from a Rainmeter skin: colours as #RRGGBB, the card backing's opacity
/// from 0 to 1. Any part the skin does not set is null.
/// </summary>
public sealed record RainmeterLook(
    string? FontFace,
    string? TextColor,
    string? BackgroundColor,
    double? BackgroundOpacity,
    string? AccentColor,
    double? CornerRadius)
{
    public bool IsEmpty =>
        FontFace is null && TextColor is null && BackgroundColor is null && AccentColor is null && CornerRadius is null;
}

/// <summary>
/// Reads the visual settings of a Rainmeter skin from its .ini and .inc text: the most
/// used font and text colour, the largest backing shape's fill and corner, and a lively
/// bar or line colour as the accent. It only reads text; measures, scripts and plugins
/// are ignored and nothing is run.
/// </summary>
public static partial class RainmeterSkinReader
{
    public static RainmeterLook Read(IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var sections = files.SelectMany(ParseSections).ToList();
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in sections.Where(section => section.Name.Equals("Variables", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var (key, value) in section.Values)
            {
                if (!key.StartsWith('@'))
                {
                    variables.TryAdd(key, value);
                }
            }
        }

        string Resolve(string value) => VariablePattern().Replace(
            value,
            match => variables.TryGetValue(match.Groups[1].Value, out var resolved) ? resolved : match.Value);

        var values = sections
            .Where(section => !section.Name.Equals("Variables", StringComparison.OrdinalIgnoreCase)
                && !section.Name.Equals("Metadata", StringComparison.OrdinalIgnoreCase))
            .SelectMany(section => section.Values.Select(pair => (pair.Key, Value: Resolve(pair.Value))))
            .ToList();

        var font = values
            .Where(pair => pair.Key.Equals("FontFace", StringComparison.OrdinalIgnoreCase)
                && pair.Value.Length > 0 && !pair.Value.Contains('#'))
            .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();

        var textColors = values
            .Where(pair => pair.Key.Equals("FontColor", StringComparison.OrdinalIgnoreCase))
            .Select(pair => ParseColor(pair.Value))
            .OfType<Rgba>()
            .ToList();
        var text = textColors
            .GroupBy(color => color.Hex)
            .OrderByDescending(group => group.Count())
            .Select(group => (Rgba?)group.First())
            .FirstOrDefault();

        // The backing is the largest filled rectangle, or failing that the skin's solid
        // colour when it is more than a click-catcher.
        Rgba? backing = null;
        double? corner = null;
        double largest = 0;
        foreach (var (key, value) in values.Where(pair => pair.Key.StartsWith("Shape", StringComparison.OrdinalIgnoreCase)))
        {
            var rectangle = RectanglePattern().Match(value);
            var fill = FillPattern().Match(value);
            if (!rectangle.Success || !fill.Success || ParseColor(fill.Groups[1].Value) is not { } color)
            {
                continue;
            }
            var numbers = rectangle.Groups[1].Value.Split(',').Select(ParseNumber).ToArray();
            if (numbers.Length < 4 || numbers[2] is not { } width || numbers[3] is not { } height)
            {
                continue;
            }
            if (width * height > largest)
            {
                largest = width * height;
                backing = color;
                corner = numbers.Length > 4 ? numbers[4] : null;
            }
        }
        backing ??= values
            .Where(pair => pair.Key.Equals("SolidColor", StringComparison.OrdinalIgnoreCase))
            .Select(pair => ParseColor(pair.Value))
            .OfType<Rgba>()
            .Where(color => color.A > 8)
            .Select(color => (Rgba?)color)
            .FirstOrDefault();

        var accent = values
            .Where(pair => pair.Key is var key
                && (key.Equals("BarColor", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("LineColor", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("FontColor", StringComparison.OrdinalIgnoreCase)))
            .Select(pair => ParseColor(pair.Value))
            .OfType<Rgba>()
            .Where(color => color.Saturation >= 0.4 && color.Hex != text?.Hex)
            .GroupBy(color => color.Hex)
            .OrderByDescending(group => group.Count())
            .Select(group => (Rgba?)group.First())
            .FirstOrDefault();

        return new RainmeterLook(
            font,
            text?.Hex,
            backing?.Hex,
            backing is { } solid ? Math.Round(solid.A / 255d, 2) : null,
            accent?.Hex,
            corner is { } radius ? Math.Clamp(radius, 0, 40) : null);
    }

    /// <summary>
    /// A Rainmeter colour: "R,G,B" or "R,G,B,A" in decimal, or "RRGGBB" / "RRGGBBAA" in hex.
    /// </summary>
    internal static Rgba? ParseColor(string value)
    {
        var text = value.Trim();
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is 3 or 4
            && parts.All(part => byte.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            var bytes = parts.Select(part => byte.Parse(part, CultureInfo.InvariantCulture)).ToArray();
            return new Rgba(bytes[0], bytes[1], bytes[2], bytes.Length == 4 ? bytes[3] : (byte)255);
        }
        if (text.Length is 6 or 8 && HexPattern().IsMatch(text))
        {
            byte At(int index) => byte.Parse(text.AsSpan(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new Rgba(At(0), At(2), At(4), text.Length == 8 ? At(6) : (byte)255);
        }
        return null;
    }

    private static double? ParseNumber(string value) =>
        double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static IEnumerable<Section> ParseSections(string text)
    {
        Section? current = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';'))
            {
                continue;
            }
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (current is not null)
                {
                    yield return current;
                }
                current = new Section(line[1..^1].Trim(), []);
                continue;
            }
            var equals = line.IndexOf('=');
            if (current is not null && equals > 0)
            {
                current.Values.Add((line[..equals].Trim(), line[(equals + 1)..].Trim().Trim('"')));
            }
        }
        if (current is not null)
        {
            yield return current;
        }
    }

    private sealed record Section(string Name, List<(string Key, string Value)> Values);

    internal readonly record struct Rgba(byte R, byte G, byte B, byte A)
    {
        public string Hex => $"#{R:X2}{G:X2}{B:X2}";

        public double Saturation
        {
            get
            {
                var max = Math.Max(R, Math.Max(G, B)) / 255d;
                var min = Math.Min(R, Math.Min(G, B)) / 255d;
                return max == 0 ? 0 : (max - min) / max;
            }
        }
    }

    [GeneratedRegex("#([A-Za-z0-9_]+)#")]
    private static partial Regex VariablePattern();

    [GeneratedRegex(@"Rectangle\s+([^|]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RectanglePattern();

    [GeneratedRegex(@"Fill\s+Color\s+([^|]+)", RegexOptions.IgnoreCase)]
    private static partial Regex FillPattern();

    [GeneratedRegex("^[0-9A-Fa-f]+$")]
    private static partial Regex HexPattern();
}
