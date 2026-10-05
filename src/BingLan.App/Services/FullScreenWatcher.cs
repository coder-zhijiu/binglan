using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using BingLan.App.Interop;
using BingLan.Core.Dock;

namespace BingLan.App.Services;

/// <summary>
/// Reports whether the foreground window covers its whole monitor (a game, video or
/// presentation), checked on a slow timer so the desktop can pause non-essential work.
/// </summary>
internal sealed class FullScreenWatcher : IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);
    private readonly DispatcherTimer _timer;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    internal FullScreenWatcher()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = CheckInterval };
        _timer.Tick += (_, _) => Check();
    }

    internal bool IsFullScreenInFront { get; private set; }

    internal event Action? Changed;

    internal void Start()
    {
        Check();
        _timer.Start();
    }

    public void Dispose() => _timer.Stop();

    private void Check()
    {
        var fullScreen = IsForegroundFullScreen();
        if (fullScreen == IsFullScreenInFront)
        {
            return;
        }

        IsFullScreenInFront = fullScreen;
        Changed?.Invoke();
    }

    private bool IsForegroundFullScreen()
    {
        var window = DockNativeMethods.GetForegroundWindow();
        if (window == 0 || DockNativeMethods.IsIconic(window))
        {
            return false;
        }

        DockNativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == _ownProcessId)
        {
            return false;
        }

        var className = new StringBuilder(64);
        DockNativeMethods.GetClassNameW(window, className, className.Capacity);
        if (ShellSurfaceWindows.IsShellSurface(className.ToString()))
        {
            return false;
        }

        if (DockNativeMethods.DwmGetWindowAttribute(
                window,
                DockNativeMethods.DwmwaExtendedFrameBounds,
                out DockNativeMethods.NativeRect bounds,
                Marshal.SizeOf<DockNativeMethods.NativeRect>()) != 0
            && !DockNativeMethods.GetWindowRect(window, out bounds))
        {
            return false;
        }

        var monitor = DockNativeMethods.MonitorFromWindow(window, DockNativeMethods.MonitorDefaultToNearest);
        var info = new DockNativeMethods.MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<DockNativeMethods.MonitorInfo>(),
            DeviceName = string.Empty
        };
        return monitor != 0
            && DockNativeMethods.GetMonitorInfoW(monitor, ref info)
            && bounds.Left <= info.Monitor.Left
            && bounds.Top <= info.Monitor.Top
            && bounds.Right >= info.Monitor.Right
            && bounds.Bottom >= info.Monitor.Bottom;
    }
}
