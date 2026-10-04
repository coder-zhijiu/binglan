using System.Runtime.InteropServices;
using System.Text;
using BingLan.DockSpike.Interop;

namespace BingLan.DockSpike.Windowing;

internal sealed class WindowCatalog
{
    private const int ErrorInsufficientBuffer = 122;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    internal IReadOnlyList<TrackedWindow> Capture(nint dockWindow)
    {
        var windows = new List<TrackedWindow>();
        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundRootOwner = GetRootOwnerOrSelf(foreground);
        var shell = NativeMethods.GetShellWindow();
        var identities = new Dictionary<uint, ProcessIdentity>();

        NativeMethods.EnumWindows((window, _) =>
        {
            if (!ShouldInclude(window, dockWindow, shell))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            if (processId == 0 || processId == _ownProcessId)
            {
                return true;
            }

            var title = GetTitle(window);
            if (!identities.TryGetValue(processId, out var identity))
            {
                identity = ResolveProcessIdentity(processId);
                identities.Add(processId, identity);
            }

            windows.Add(new TrackedWindow(
                window,
                processId,
                title,
                identity.AppUserModelId,
                identity.ExecutablePath,
                window == foreground
                    || GetRootOwnerOrSelf(window) == foregroundRootOwner,
                NativeMethods.IsIconic(window)));
            return true;
        }, 0);

        return windows;
    }

    private static nint GetRootOwnerOrSelf(nint window)
    {
        if (window == 0)
        {
            return 0;
        }

        var rootOwner = NativeMethods.GetAncestor(window, NativeMethods.GaRootOwner);
        return rootOwner != 0 ? rootOwner : window;
    }

    private static bool ShouldInclude(nint window, nint dockWindow, nint shell)
    {
        if (window == 0
            || window == dockWindow
            || window == shell
            || !NativeMethods.IsWindowVisible(window))
        {
            return false;
        }

        var extendedStyle = (long)NativeMethods.GetWindowLongPtr(
            window,
            NativeMethods.GwlExStyle);
        var appWindow = (extendedStyle & NativeMethods.WsExAppWindow) != 0;
        if (!appWindow
            && ((extendedStyle & NativeMethods.WsExToolWindow) != 0
                || (extendedStyle & NativeMethods.WsExNoActivate) != 0))
        {
            return false;
        }

        if (NativeMethods.DwmGetWindowAttribute(
                window,
                NativeMethods.DwmwaCloaked,
                out var cloaked,
                sizeof(int)) == 0
            && cloaked != 0)
        {
            return false;
        }

        if (appWindow)
        {
            return true;
        }

        var rootOwner = NativeMethods.GetAncestor(window, NativeMethods.GaRootOwner);
        if (rootOwner == 0)
        {
            return true;
        }

        var representative = rootOwner;
        while (true)
        {
            var popup = NativeMethods.GetLastActivePopup(representative);
            if (popup == representative)
            {
                break;
            }
            representative = popup;
            if (NativeMethods.IsWindowVisible(representative))
            {
                break;
            }
        }

        return window == representative;
    }

    private static string GetTitle(nint window)
    {
        var length = NativeMethods.GetWindowTextLengthW(window);
        if (length <= 0)
        {
            return string.Empty;
        }

        var title = new StringBuilder(length + 1);
        NativeMethods.GetWindowTextW(window, title, title.Capacity);
        return title.ToString().Trim();
    }

    private static ProcessIdentity ResolveProcessIdentity(uint processId)
    {
        var process = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryLimitedInformation,
            false,
            processId);
        if (process == 0)
        {
            return new ProcessIdentity(null, null);
        }

        try
        {
            return new ProcessIdentity(
                TryGetAppUserModelId(process),
                TryGetExecutablePath(process));
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    private static string? TryGetExecutablePath(nint process)
    {
        var buffer = new StringBuilder(32768);
        var size = (uint)buffer.Capacity;
        return NativeMethods.QueryFullProcessImageNameW(process, 0, buffer, ref size)
            ? buffer.ToString()
            : null;
    }

    private static string? TryGetAppUserModelId(nint process)
    {
        uint length = 0;
        var result = NativeMethods.GetApplicationUserModelId(process, ref length, null);
        if (result != ErrorInsufficientBuffer || length == 0)
        {
            return null;
        }

        var buffer = new StringBuilder((int)length);
        result = NativeMethods.GetApplicationUserModelId(process, ref length, buffer);
        return result == 0 && buffer.Length > 0 ? buffer.ToString() : null;
    }

    private sealed record ProcessIdentity(string? AppUserModelId, string? ExecutablePath);
}
