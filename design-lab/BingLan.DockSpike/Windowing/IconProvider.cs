using System.IO;
using System.Runtime.InteropServices;
using BingLan.DockSpike.Interop;

namespace BingLan.DockSpike.Windowing;

internal readonly record struct IconHandle(nint Handle, bool OwnedByCaller)
{
    internal bool HasIcon => Handle != 0;
}

internal static class IconProvider
{
    internal static IconHandle ForGroup(WindowGroup group)
    {
        var executablePath = group.Windows
            .Select(window => window.ExecutablePath)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
        var icon = FromExecutable(executablePath);
        if (icon.HasIcon)
        {
            return icon;
        }

        var window = group.Windows.FirstOrDefault();
        return window is not null ? FromWindow(window.Handle) : default;
    }

    internal static IconHandle FromExecutable(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return default;
        }

        var info = NewFileInfo();
        var result = NativeMethods.SHGetFileInfoW(
            executablePath,
            0,
            ref info,
            (uint)Marshal.SizeOf<NativeMethods.ShFileInfo>(),
            NativeMethods.ShgfiIcon | NativeMethods.ShgfiLargeIcon);
        return result != 0 && info.Icon != 0 ? new IconHandle(info.Icon, true) : default;
    }

    internal static IconHandle FromWindow(nint window)
    {
        if (window == 0)
        {
            return default;
        }

        var icon = QueryWindowIcon(window, NativeMethods.IconSmall2);
        if (icon == 0)
        {
            icon = QueryWindowIcon(window, NativeMethods.IconSmall);
        }
        if (icon == 0)
        {
            icon = QueryWindowIcon(window, NativeMethods.IconBig);
        }
        if (icon == 0)
        {
            icon = NativeMethods.GetClassLongPtr(window, NativeMethods.GclpHIconSm);
        }
        if (icon == 0)
        {
            icon = NativeMethods.GetClassLongPtr(window, NativeMethods.GclpHIcon);
        }

        return icon != 0 ? new IconHandle(icon, false) : default;
    }

    internal static IconHandle GenericApplication()
    {
        var info = NewFileInfo();
        var result = NativeMethods.SHGetFileInfoW(
            "application.exe",
            NativeMethods.FileAttributeNormal,
            ref info,
            (uint)Marshal.SizeOf<NativeMethods.ShFileInfo>(),
            NativeMethods.ShgfiIcon
                | NativeMethods.ShgfiLargeIcon
                | NativeMethods.ShgfiUseFileAttributes);
        return result != 0 && info.Icon != 0 ? new IconHandle(info.Icon, true) : default;
    }

    private static NativeMethods.ShFileInfo NewFileInfo() =>
        new() { DisplayName = string.Empty, TypeName = string.Empty };

    private static nint QueryWindowIcon(nint window, int iconKind)
    {
        NativeMethods.SendMessageTimeoutW(
            window,
            NativeMethods.WmGetIcon,
            iconKind,
            0,
            NativeMethods.SmtoAbortIfHung,
            250,
            out var icon);
        return icon;
    }
}
