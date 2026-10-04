using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using BingLan.App.Dock;
using BingLan.App.Interop;
using BingLan.Core.Dock;
using BingLan.Core.Taskbar;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace BingLan.App.Taskbar;

/// <summary>
/// Lets an auto-hidden taskbar at the bottom of a monitor appear only when the pointer
/// reaches the left or right end of the edge. A strip two pixels high, nearly invisible
/// but able to take the pointer, covers the middle of the edge so the hidden taskbar
/// beneath it is not reached there. Nothing in Explorer or the Windows settings changes;
/// closing the strips puts the usual behaviour back.
/// </summary>
internal sealed class TaskbarCornerReveal : IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);
    private readonly Dictionary<string, EdgeStrip> _strips = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _timer;
    private bool _enabled;
    private bool _suspended;

    internal TaskbarCornerReveal()
    {
        // The taskbar raises itself as it slides in and out, so the strips are put back on
        // top regularly; the same check notices auto-hide being switched on or off.
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = CheckInterval };
        _timer.Tick += (_, _) => Update();
    }

    /// <summary>Turns the behaviour on or off.</summary>
    internal void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (enabled)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
        Update();
    }

    /// <summary>A full-screen app is in front: the strips step aside until it leaves.</summary>
    internal void SetSuspended(bool suspended)
    {
        _suspended = suspended;
        Update();
    }

    internal void Update()
    {
        var wanted = new Dictionary<string, PixelRect>(StringComparer.OrdinalIgnoreCase);
        if (_enabled && !_suspended)
        {
            foreach (var monitor in MonitorCatalog.GetAll())
            {
                if (TaskbarEdgeGuard.HasBottomAutoHideBar(monitor.Bounds))
                {
                    wanted[monitor.DeviceName] = TaskbarCornerRules.MiddleStrip(monitor.Bounds, monitor.Dpi);
                }
            }
        }

        foreach (var stale in _strips.Keys.Except(wanted.Keys).ToList())
        {
            _strips[stale].Close();
            _strips.Remove(stale);
        }
        foreach (var (device, bounds) in wanted)
        {
            if (!_strips.TryGetValue(device, out var strip))
            {
                strip = new EdgeStrip();
                _strips[device] = strip;
                strip.Show();
            }
            strip.Place(bounds);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        foreach (var strip in _strips.Values)
        {
            strip.Close();
        }
        _strips.Clear();
    }

    private sealed class EdgeStrip : Window
    {
        private static readonly SolidColorBrush NearlyClear = CreateNearlyClear();

        internal EdgeStrip()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            // Fully transparent pixels let the pointer through to the taskbar below, so
            // the strip keeps an alpha of one: invisible, but it takes the pointer.
            Background = NearlyClear;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            Title = "冰蓝任务栏边缘";
            SourceInitialized += (_, _) =>
            {
                var handle = new WindowInteropHelper(this).Handle;
                var style = (long)NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle);
                NativeMethods.SetWindowLongPtr(
                    handle,
                    NativeMethods.GwlExStyle,
                    (nint)(style | NativeMethods.WsExToolWindow | DockNativeMethods.WsExNoActivate));
            };
        }

        internal void Place(PixelRect bounds) =>
            NativeMethods.SetWindowPos(
                new WindowInteropHelper(this).Handle,
                DockNativeMethods.HwndTopmost,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                NativeMethods.SwpNoActivate);

        private static SolidColorBrush CreateNearlyClear()
        {
            var brush = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255));
            brush.Freeze();
            return brush;
        }
    }
}
