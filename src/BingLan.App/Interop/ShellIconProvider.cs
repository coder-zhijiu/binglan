using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BingLan.Core.Services;

namespace BingLan.App.Interop;

internal static class ShellIconProvider
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiPidl = 0x000000008;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint IconHandle;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(
        string path,
        uint fileAttributes,
        out ShellFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(
        nint itemIdList,
        uint fileAttributes,
        out ShellFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        string name,
        nint bindContext,
        out nint itemIdList,
        uint attributesIn,
        out uint attributesOut);

    [DllImport("shell32.dll")]
    private static extern void ILFree(nint itemIdList);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint iconHandle);

    public static ImageSource? GetIcon(string path)
    {
        if (FileMappingService.IsShellEntry(path))
        {
            return GetShellNamespaceIcon(path);
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return null;
        }

        var result = SHGetFileInfo(
            path,
            0,
            out var info,
            (uint)Marshal.SizeOf<ShellFileInfo>(),
            ShgfiIcon | ShgfiLargeIcon);
        return result == 0 || info.IconHandle == 0 ? null : ToImageSource(info.IconHandle);
    }

    private static ImageSource? GetShellNamespaceIcon(string shellPath)
    {
        if (SHParseDisplayName(shellPath, 0, out var itemIdList, 0, out _) != 0 || itemIdList == 0)
        {
            return null;
        }

        try
        {
            var result = SHGetFileInfo(
                itemIdList,
                0,
                out var info,
                (uint)Marshal.SizeOf<ShellFileInfo>(),
                ShgfiIcon | ShgfiLargeIcon | ShgfiPidl);
            return result == 0 || info.IconHandle == 0 ? null : ToImageSource(info.IconHandle);
        }
        finally
        {
            ILFree(itemIdList);
        }
    }

    private static ImageSource? ToImageSource(nint iconHandle)
    {
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                iconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(40, 40));
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }
}
