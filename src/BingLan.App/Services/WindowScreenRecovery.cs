using System.Windows;
using System.Windows.Interop;
using BingLan.App.Dock;
using BingLan.App.Interop;
using BingLan.Core.Dock;

namespace BingLan.App.Services;

/// <summary>
/// Moves a desktop card back into view when a monitor is disconnected or the
/// resolution or scaling changes and the card no longer shows on any work area.
/// </summary>
internal static class WindowScreenRecovery
{
    private const int MinimumVisiblePixels = 64;

    internal static bool EnsureOnScreen(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0 || !window.IsVisible || !DockNativeMethods.GetWindowRect(handle, out var native))
        {
            return false;
        }

        var monitors = MonitorCatalog.GetAll();
        if (monitors.Count == 0)
        {
            return false;
        }

        var current = new PixelRect(native.Left, native.Top, native.Right, native.Bottom);
        if (!current.HasArea)
        {
            return false;
        }

        var primary = monitors.FirstOrDefault(monitor => monitor.IsPrimary) ?? monitors[0];
        var target = PlacementResolver.EnsureVisible(
            current,
            monitors.Select(monitor => monitor.WorkingArea).ToArray(),
            primary.WorkingArea,
            MinimumVisiblePixels);
        if (target == current)
        {
            return false;
        }

        return NativeMethods.SetWindowPos(
            handle,
            0,
            target.Left,
            target.Top,
            target.Width,
            target.Height,
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }

    /// <summary>
    /// Moves a card that the taskbar or the dock now covers back into the work area.
    /// Called when the work area changes, for example when the dock starts reserving
    /// the bottom of the screen.
    /// </summary>
    internal static bool KeepOutOfReservedEdges(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0 || !window.IsVisible || !DockNativeMethods.GetWindowRect(handle, out var native))
        {
            return false;
        }

        var current = new PixelRect(native.Left, native.Top, native.Right, native.Bottom);
        var centerX = (current.Left + current.Right) / 2;
        var centerY = (current.Top + current.Bottom) / 2;
        var monitor = MonitorCatalog.GetAll().FirstOrDefault(candidate =>
            centerX >= candidate.Bounds.Left && centerX < candidate.Bounds.Right &&
            centerY >= candidate.Bounds.Top && centerY < candidate.Bounds.Bottom);
        if (monitor is null || !current.HasArea)
        {
            return false;
        }

        var target = PlacementResolver.KeepOutOfReservedEdges(current, monitor.Bounds, monitor.WorkingArea);
        return target != current && NativeMethods.SetWindowPos(
            handle,
            0,
            target.Left,
            target.Top,
            0,
            0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }
}
