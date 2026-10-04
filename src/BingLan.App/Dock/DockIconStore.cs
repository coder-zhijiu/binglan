using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BingLan.Core.Themes;

namespace BingLan.App.Dock;

/// <summary>
/// Custom dock icons, kept as 128 × 128 PNG files under generated names in the app's
/// data folder. Pinned apps refer to them by file name only, so no path leaves the
/// machine and a theme package carries the image itself.
/// </summary>
internal sealed class DockIconStore(string directory)
{
    private const int IconPixels = 128;

    internal string Directory { get; } = directory;

    /// <summary>Converts an image the user picked into a stored icon; null if it cannot be read.</summary>
    internal string? SaveFromFile(string sourcePath)
    {
        try
        {
            var info = new FileInfo(sourcePath);
            if (!info.Exists || info.Length > 8 * 1024 * 1024)
            {
                return null;
            }

            // The header gives the size without decoding; the longer side is decoded straight
            // to icon size, so a huge picture never becomes a huge bitmap.
            var uri = new Uri(info.FullName);
            var header = BitmapDecoder.Create(uri, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            var decoded = new BitmapImage();
            decoded.BeginInit();
            decoded.CacheOption = BitmapCacheOption.OnLoad;
            decoded.UriSource = uri;
            if (header.PixelHeight > header.PixelWidth)
            {
                decoded.DecodePixelHeight = IconPixels;
            }
            else
            {
                decoded.DecodePixelWidth = IconPixels;
            }
            decoded.EndInit();
            decoded.Freeze();
            return Save(Encode(decoded));
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException
            or FileFormatException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Stores an icon that arrived in a theme package; null if it is not a usable PNG.</summary>
    internal string? SaveFromPng(byte[] png)
    {
        if (!ThemeArchive.IsValidPng(png, ThemeArchive.MaximumIconBytes))
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(png);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            // Re-encode rather than copy, so only decoded pixels are ever written to disk.
            return Save(Encode(decoder.Frames[0]));
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException
            or FileFormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    internal byte[]? ReadPng(string iconFile)
    {
        if (!ThemeRules.IsIconFileName(iconFile))
        {
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(Path.Combine(Directory, iconFile));
            return ThemeArchive.IsValidPng(bytes, ThemeArchive.MaximumIconBytes) ? bytes : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal ImageSource? Load(string iconFile)
    {
        if (ReadPng(iconFile) is not { } bytes)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is NotSupportedException or FileFormatException)
        {
            return null;
        }
    }

    internal void Delete(string? iconFile)
    {
        if (!ThemeRules.IsIconFileName(iconFile))
        {
            return;
        }

        try
        {
            File.Delete(Path.Combine(Directory, iconFile!));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string? Save(byte[] png)
    {
        if (!ThemeArchive.IsValidPng(png, ThemeArchive.MaximumIconBytes))
        {
            return null;
        }

        System.IO.Directory.CreateDirectory(Directory);
        var name = $"{Guid.NewGuid():N}.png";
        File.WriteAllBytes(Path.Combine(Directory, name), png);
        return name;
    }

    // Centres the image on a transparent square so every icon keeps its proportions.
    private static byte[] Encode(BitmapSource source)
    {
        var scale = Math.Min((double)IconPixels / source.PixelWidth, (double)IconPixels / source.PixelHeight);
        var width = source.PixelWidth * scale;
        var height = source.PixelHeight * scale;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(source, new Rect((IconPixels - width) / 2, (IconPixels - height) / 2, width, height));
        }

        var bitmap = new RenderTargetBitmap(IconPixels, IconPixels, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}
