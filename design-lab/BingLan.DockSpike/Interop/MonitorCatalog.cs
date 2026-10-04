using BingLan.DockSpike.Layout;
using System.Runtime.InteropServices;

namespace BingLan.DockSpike.Interop;

internal sealed record MonitorSnapshot(
    string DeviceName,
    PixelRect Bounds,
    PixelRect WorkingArea,
    bool IsPrimary,
    uint Dpi)
{
    internal string Label => $"{(IsPrimary ? "主屏" : "屏幕")} {DeviceName.Replace("\\\\.\\", "")}: {Bounds.Width}×{Bounds.Height} @ {Dpi * 100 / 96}%";
}

internal static class MonitorCatalog
{
    internal static IReadOnlyList<MonitorSnapshot> GetAll()
    {
        var monitors = new List<MonitorSnapshot>();
        NativeMethods.EnumDisplayMonitors(0, 0, AddMonitor, 0);

        return monitors
            .OrderByDescending(screen => screen.IsPrimary)
            .ThenBy(screen => screen.Bounds.Left)
            .ThenBy(screen => screen.Bounds.Top)
            .ToArray();

        bool AddMonitor(
            nint monitor,
            nint deviceContext,
            ref NativeMethods.NativeRect monitorRectangle,
            nint data)
        {
            var info = new NativeMethods.MonitorInfo
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>(),
                DeviceName = string.Empty
            };
            if (!NativeMethods.GetMonitorInfoW(monitor, ref info))
            {
                return true;
            }

            monitors.Add(new MonitorSnapshot(
                info.DeviceName,
                ToPixelRect(info.Monitor),
                ToPixelRect(info.WorkArea),
                (info.Flags & NativeMethods.MonitorInfoPrimary) != 0,
                GetDpi(monitor)));
            return true;
        }
    }

    private static uint GetDpi(nint monitor)
    {
        if (monitor != 0
            && NativeMethods.GetDpiForMonitor(
                monitor,
                NativeMethods.MdtEffectiveDpi,
                out var dpiX,
                out _) == 0
            && dpiX > 0)
        {
            return dpiX;
        }

        return 96;
    }

    private static PixelRect ToPixelRect(NativeMethods.NativeRect rectangle) =>
        new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
}
