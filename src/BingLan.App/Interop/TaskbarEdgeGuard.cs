using System.Runtime.InteropServices;

namespace BingLan.App.Interop;

internal static class TaskbarEdgeGuard
{
    internal const int RevealInsetPixels = 8;

    private const uint AbmGetAutoHideBarEx = 0x0000000B;
    private const uint AbeLeft = 0;
    private const uint AbeTop = 1;
    private const uint AbeRight = 2;
    private const uint AbeBottom = 3;
    private const uint MonitorDefaultToNearest = 2;

    [Flags]
    internal enum AutoHideEdges
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8
    }

    /// <summary>Moves a proposed card position off an auto-hidden taskbar's reveal edge.</summary>
    internal static bool ConstrainRect(ref PixelRect rect) => ConstrainRectToDetectedEdges(ref rect);

    /// <summary>Whether an auto-hidden taskbar sits on the bottom edge of this monitor.</summary>
    internal static bool HasBottomAutoHideBar(BingLan.Core.Dock.PixelRect monitor) =>
        HasAutoHideBar(
            new PixelRect { Left = monitor.Left, Top = monitor.Top, Right = monitor.Right, Bottom = monitor.Bottom },
            AbeBottom);

    internal static bool EnsureWindowBounds(nint window)
    {
        if (window == 0 || !GetWindowRect(window, out var rect))
        {
            return false;
        }

        if (!ConstrainRectToDetectedEdges(ref rect))
        {
            return false;
        }

        return NativeMethods.SetWindowPos(
            window,
            0,
            rect.Left,
            rect.Top,
            0,
            0,
            NativeMethods.SwpNoSize |
            NativeMethods.SwpNoZOrder |
            NativeMethods.SwpNoActivate);
    }

    internal static bool ConstrainToAutoHideEdges(
        ref PixelRect rect,
        PixelRect monitorArea,
        AutoHideEdges edges)
    {
        var original = rect;
        var overlapsMonitorHorizontally =
            rect.Right > monitorArea.Left && rect.Left < monitorArea.Right;
        var overlapsMonitorVertically =
            rect.Bottom > monitorArea.Top && rect.Top < monitorArea.Bottom;

        if (overlapsMonitorVertically && edges.HasFlag(AutoHideEdges.Left))
        {
            var safeLeft = monitorArea.Left + RevealInsetPixels;
            if (rect.Left < safeLeft && rect.Right > monitorArea.Left)
            {
                rect.Offset(safeLeft - rect.Left, 0);
            }
        }

        if (overlapsMonitorVertically && edges.HasFlag(AutoHideEdges.Right))
        {
            var safeRight = monitorArea.Right - RevealInsetPixels;
            if (rect.Right > safeRight && rect.Left < monitorArea.Right)
            {
                rect.Offset(safeRight - rect.Right, 0);
            }
        }

        if (overlapsMonitorHorizontally && edges.HasFlag(AutoHideEdges.Top))
        {
            var safeTop = monitorArea.Top + RevealInsetPixels;
            if (rect.Top < safeTop && rect.Bottom > monitorArea.Top)
            {
                rect.Offset(0, safeTop - rect.Top);
            }
        }

        if (overlapsMonitorHorizontally && edges.HasFlag(AutoHideEdges.Bottom))
        {
            var safeBottom = monitorArea.Bottom - RevealInsetPixels;
            if (rect.Bottom > safeBottom && rect.Top < monitorArea.Bottom)
            {
                rect.Offset(0, safeBottom - rect.Bottom);
            }
        }

        return !rect.Equals(original);
    }

    private static bool ConstrainRectToDetectedEdges(ref PixelRect rect)
    {
        var monitor = MonitorFromRect(ref rect, MonitorDefaultToNearest);
        if (monitor == 0)
        {
            return false;
        }

        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        var edges = GetAutoHideEdges(monitorInfo.MonitorArea);
        return ConstrainToAutoHideEdges(ref rect, monitorInfo.MonitorArea, edges);
    }

    // Each lookup asks Explorer four times; during a drag the answer cannot change, so it
    // is kept per monitor until the drag ends.
    private static Dictionary<PixelRect, AutoHideEdges>? _moveEdges;

    internal static void BeginMove() => _moveEdges = [];

    internal static void EndMove() => _moveEdges = null;

    private static AutoHideEdges GetAutoHideEdges(PixelRect monitorArea)
    {
        if (_moveEdges is null)
        {
            return QueryAutoHideEdges(monitorArea);
        }
        if (!_moveEdges.TryGetValue(monitorArea, out var edges))
        {
            edges = QueryAutoHideEdges(monitorArea);
            _moveEdges[monitorArea] = edges;
        }
        return edges;
    }

    private static AutoHideEdges QueryAutoHideEdges(PixelRect monitorArea)
    {
        var edges = AutoHideEdges.None;
        if (HasAutoHideBar(monitorArea, AbeLeft))
        {
            edges |= AutoHideEdges.Left;
        }
        if (HasAutoHideBar(monitorArea, AbeTop))
        {
            edges |= AutoHideEdges.Top;
        }
        if (HasAutoHideBar(monitorArea, AbeRight))
        {
            edges |= AutoHideEdges.Right;
        }
        if (HasAutoHideBar(monitorArea, AbeBottom))
        {
            edges |= AutoHideEdges.Bottom;
        }
        return edges;
    }

    private static bool HasAutoHideBar(PixelRect monitorArea, uint edge)
    {
        var data = new AppBarData
        {
            Size = Marshal.SizeOf<AppBarData>(),
            Edge = edge,
            Rectangle = monitorArea
        };
        return SHAppBarMessage(AbmGetAutoHideBarEx, ref data) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PixelRect : IEquatable<PixelRect>
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal readonly int Width => Right - Left;

        internal readonly int Height => Bottom - Top;

        internal void Offset(int horizontal, int vertical)
        {
            Left += horizontal;
            Right += horizontal;
            Top += vertical;
            Bottom += vertical;
        }

        public readonly bool Equals(PixelRect other) =>
            Left == other.Left &&
            Top == other.Top &&
            Right == other.Right &&
            Bottom == other.Bottom;

        public override readonly bool Equals(object? obj) => obj is PixelRect other && Equals(other);

        public override readonly int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        internal int Size;
        internal PixelRect MonitorArea;
        internal PixelRect WorkArea;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        internal int Size;
        internal nint Window;
        internal uint CallbackMessage;
        internal uint Edge;
        internal PixelRect Rectangle;
        internal nint Parameter;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out PixelRect rect);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect(
        ref PixelRect rect,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        nint monitor,
        ref MonitorInfo monitorInfo);

    [DllImport("shell32.dll")]
    private static extern nint SHAppBarMessage(
        uint message,
        ref AppBarData data);
}
