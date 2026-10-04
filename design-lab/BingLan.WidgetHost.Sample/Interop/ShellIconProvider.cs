using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BingLan.WidgetHost.Sample.Interop;

internal static class ShellIconProvider
{
    private const uint ShgfiIcon = 0x00000100;
    private const uint ShgfiLargeIcon = 0x00000000;
    private const uint ShgfiUseFileAttributes = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        internal nint IconHandle;
        internal int IconIndex;
        internal uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        internal string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(
        string path,
        uint attributes,
        out ShellFileInfo info,
        uint infoSize,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint iconHandle);

    internal static ImageSource? Get(string extensionOrPath, bool useFileAttributes = true)
    {
        var flags = ShgfiIcon | ShgfiLargeIcon;
        if (useFileAttributes)
        {
            flags |= ShgfiUseFileAttributes;
        }

        var result = SHGetFileInfo(
            useFileAttributes ? $"widget-host{extensionOrPath}" : extensionOrPath,
            FileAttributeNormal,
            out var info,
            (uint)Marshal.SizeOf<ShellFileInfo>(),
            flags);
        if (result == 0 || info.IconHandle == 0)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                info.IconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(48, 48));
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.IconHandle);
        }
    }
}
