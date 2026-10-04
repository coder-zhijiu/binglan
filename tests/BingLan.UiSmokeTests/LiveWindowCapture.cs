using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class LiveWindowCapture
{
    private const int SrcCopy = 0x00CC0020;
    private const int CaptureBlt = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(
        nint deviceContext,
        int width,
        int height);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint deviceContext, nint value);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        nint destination,
        int destinationX,
        int destinationY,
        int width,
        int height,
        nint source,
        int sourceX,
        int sourceY,
        int operation);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);

    internal static void Save(Window window, string output, double paddingDip)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0 || !GetWindowRect(handle, out var rect))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var dpi = Math.Max(96u, GetDpiForWindow(handle));
        var paddingPixels = (int)Math.Round(paddingDip * dpi / 96d);
        var width = rect.Right - rect.Left + paddingPixels * 2;
        var height = rect.Bottom - rect.Top + paddingPixels * 2;
        var source = Capture(
            rect.Left - paddingPixels,
            rect.Top - paddingPixels,
            width,
            height);

        var targetWidth = Math.Max(1, (int)Math.Round(window.ActualWidth + paddingDip * 2));
        var targetHeight = Math.Max(1, (int)Math.Round(window.ActualHeight + paddingDip * 2));
        var normalized = Normalize(source, targetWidth, targetHeight);

        var path = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(normalized));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static BitmapSource Capture(int x, int y, int width, int height)
    {
        var screenDc = GetDC(0);
        if (screenDc == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmap = CreateCompatibleBitmap(screenDc, width, height);
        if (memoryDc == 0 || bitmap == 0)
        {
            if (bitmap != 0)
            {
                DeleteObject(bitmap);
            }
            if (memoryDc != 0)
            {
                DeleteDC(memoryDc);
            }
            ReleaseDC(0, screenDc);
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var previous = SelectObject(memoryDc, bitmap);
        try
        {
            if (!BitBlt(
                    memoryDc,
                    0,
                    0,
                    width,
                    height,
                    screenDc,
                    x,
                    y,
                    SrcCopy | CaptureBlt))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                0,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            SelectObject(memoryDc, previous);
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            ReleaseDC(0, screenDc);
        }
    }

    private static BitmapSource Normalize(BitmapSource source, int targetWidth, int targetHeight)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(source, new Rect(0, 0, targetWidth, targetHeight));
        }

        var result = new RenderTargetBitmap(
            targetWidth,
            targetHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }
}
