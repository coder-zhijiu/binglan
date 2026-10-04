using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using BingLan.DockSpike.Layout;

namespace BingLan.DockSpike.Interop;

internal sealed class AppBarController : IDisposable
{
    private const int ExplorerRecoveryAttemptCount = 5;
    private static readonly TimeSpan ExplorerRecoveryDelay = TimeSpan.FromMilliseconds(400);
    private readonly Window _window;
    private readonly nint _handle;
    private readonly HwndSource _source;
    private readonly uint _callbackMessage;
    private readonly uint _taskbarCreatedMessage;
    private readonly AppBarReservationState _reservation = new();
    private MonitorSnapshot? _monitor;
    private DockEdge _edge = DockEdge.Bottom;
    private bool _reserveWorkArea = true;
    private double _contentLengthDip = DockLayoutMetrics.EmptyLengthDip;
    private bool _fullScreenActive;
    private bool _positioning;
    private bool _disposed;
    private int _recoveryGeneration;

    internal AppBarController(Window window)
    {
        _window = window;
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle)
            ?? throw new InvalidOperationException("无法取得 Dock HWND 消息源。");
        _callbackMessage = NativeMethods.RegisterWindowMessageW(
            $"BingLan.DockSpike.AppBar.{Environment.ProcessId}");
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessageW("TaskbarCreated");
        _source.AddHook(WindowProc);
    }

    internal event Action? EnvironmentChanged;

    internal void Apply(
        MonitorSnapshot monitor,
        DockEdge edge,
        bool reserveWorkArea,
        double contentLengthDip)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _monitor = monitor;
        _edge = edge;
        _reserveWorkArea = reserveWorkArea;
        _contentLengthDip = Math.Max(DockLayoutMetrics.EmptyLengthDip, contentLengthDip);

        Dispatch(_reservation.Apply(reserveWorkArea));
        Position();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _recoveryGeneration++;
        Dispatch(_reservation.Release());
        UpdateWindowLayer();
        _source.RemoveHook(WindowProc);
        _disposed = true;
    }

    private void Dispatch(IReadOnlyList<AppBarMessage> messages)
    {
        foreach (var message in messages)
        {
            if (message == AppBarMessage.Register)
            {
                var data = CreateData();
                data.CallbackMessage = _callbackMessage;
                if (NativeMethods.SHAppBarMessage(NativeMethods.AbmNew, ref data) == 0)
                {
                    _reservation.MarkRegisterFailed();
                }
            }
            else
            {
                var data = CreateData();
                NativeMethods.SHAppBarMessage(NativeMethods.AbmRemove, ref data);
            }
        }
    }

    private void Position()
    {
        if (_monitor is null || _positioning)
        {
            return;
        }

        _positioning = true;
        try
        {
            var scale = _monitor.Dpi / 96d;
            var thicknessPixels = Math.Max(
                1,
                (int)Math.Round(DockLayoutMetrics.DockThicknessDip * scale));
            var edgeGapPixels = Math.Max(
                0,
                (int)Math.Round(DockLayoutMetrics.EdgeGapDip * scale));
            var stripThickness = thicknessPixels + edgeGapPixels;

            PixelRect strip;
            if (_reservation.IsRegistered)
            {
                var extent = _edge is DockEdge.Left or DockEdge.Right
                    ? _monitor.Bounds.Width
                    : _monitor.Bounds.Height;
                var requested = DockGeometry.Calculate(
                    _monitor.Bounds,
                    _edge,
                    Math.Min(stripThickness, extent));
                var data = CreateData();
                data.Edge = (uint)_edge;
                data.Rectangle = ToNative(requested);
                NativeMethods.SHAppBarMessage(NativeMethods.AbmQueryPos, ref data);
                var adjusted = RestoreThickness(
                    ToPixel(data.Rectangle),
                    _edge,
                    Math.Min(stripThickness, extent));
                data.Rectangle = ToNative(adjusted);
                NativeMethods.SHAppBarMessage(NativeMethods.AbmSetPos, ref data);
                strip = ToPixel(data.Rectangle);
            }
            else
            {
                var workArea = GetLiveWorkingArea(_monitor);
                var extent = _edge is DockEdge.Left or DockEdge.Right
                    ? workArea.Width
                    : workArea.Height;
                strip = DockGeometry.Calculate(
                    workArea,
                    _edge,
                    Math.Min(stripThickness, extent));
            }

            var stripExtent = _edge is DockEdge.Left or DockEdge.Right
                ? strip.Height
                : strip.Width;
            var requestedLength = Math.Max(
                1,
                (int)Math.Round(_contentLengthDip * scale));
            var contentLength = FloatingDockGeometry.ClampExtent(
                requestedLength,
                stripExtent,
                DockLayoutMetrics.MaxExtentFraction,
                Math.Max(1, (int)Math.Round(DockLayoutMetrics.EmptyLengthDip * scale)));
            var windowRectangle = FloatingDockGeometry.Calculate(
                strip,
                _edge,
                contentLength,
                thicknessPixels,
                edgeGapPixels);

            NativeMethods.MoveWindow(
                _handle,
                windowRectangle.Left,
                windowRectangle.Top,
                windowRectangle.Width,
                windowRectangle.Height,
                true);
            UpdateWindowLayer();
        }
        finally
        {
            _positioning = false;
        }
    }

    private nint WindowProc(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (_disposed)
        {
            return 0;
        }

        if ((uint)message == _taskbarCreatedMessage)
        {
            _reservation.ResetRegistration();
            var recoveryGeneration = ++_recoveryGeneration;
            _window.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                () => _ = RecoverAfterExplorerRestartAsync(recoveryGeneration));
            return 0;
        }

        if ((uint)message == _callbackMessage)
        {
            switch ((int)wParam)
            {
                case NativeMethods.AbnPosChanged:
                    Position();
                    break;
                case NativeMethods.AbnFullscreenApp:
                    SetFullScreenLayer(lParam != 0);
                    break;
            }
            return 0;
        }

        switch (message)
        {
            case NativeMethods.WmActivate when _reservation.IsRegistered:
                var activationState = (ushort)(wParam.ToInt64() & 0xFFFF);
                NotifyAppBar(
                    NativeMethods.AbmActivate,
                    activationState != NativeMethods.WaInactive ? 1 : 0);
                break;
            case NativeMethods.WmWindowPosChanged when _reservation.IsRegistered && !_positioning:
                NotifyAppBar(NativeMethods.AbmWindowPosChanged, 0);
                break;
            case NativeMethods.WmDisplayChange:
            case NativeMethods.WmDpiChanged:
                _window.Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    () => EnvironmentChanged?.Invoke());
                break;
        }

        return 0;
    }

    private async Task RecoverAfterExplorerRestartAsync(int recoveryGeneration)
    {
        for (var attempt = 1; attempt <= ExplorerRecoveryAttemptCount; attempt++)
        {
            if (_disposed || recoveryGeneration != _recoveryGeneration)
            {
                return;
            }

            if (!_reserveWorkArea)
            {
                Position();
                return;
            }

            Dispatch(_reservation.Apply(true));
            if (_reservation.IsRegistered)
            {
                Position();
                return;
            }

            Position();
            if (attempt < ExplorerRecoveryAttemptCount)
            {
                await Task.Delay(ExplorerRecoveryDelay);
            }
        }

        if (_disposed || recoveryGeneration != _recoveryGeneration)
        {
            return;
        }

        Position();
    }

    private static PixelRect GetLiveWorkingArea(MonitorSnapshot monitor) =>
        MonitorCatalog.GetAll()
            .FirstOrDefault(candidate => string.Equals(
                candidate.DeviceName,
                monitor.DeviceName,
                StringComparison.OrdinalIgnoreCase))?
            .WorkingArea
        ?? monitor.WorkingArea;

    private void NotifyAppBar(uint message, nint parameter)
    {
        var data = CreateData();
        data.Parameter = parameter;
        NativeMethods.SHAppBarMessage(message, ref data);
    }

    private void SetFullScreenLayer(bool fullScreenActive)
    {
        _fullScreenActive = fullScreenActive;
        UpdateWindowLayer();
    }

    private void UpdateWindowLayer()
    {
        var insertAfter = _reservation.IsRegistered
            ? _fullScreenActive
                ? NativeMethods.HwndBottom
                : NativeMethods.HwndTopmost
            : NativeMethods.HwndNotTopmost;
        NativeMethods.SetWindowPos(
            _handle,
            insertAfter,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove
                | NativeMethods.SwpNoSize
                | NativeMethods.SwpNoActivate);
    }

    private NativeMethods.AppBarData CreateData() => new()
    {
        Size = (uint)Marshal.SizeOf<NativeMethods.AppBarData>(),
        Window = _handle
    };

    private static PixelRect RestoreThickness(PixelRect rectangle, DockEdge edge, int thickness) =>
        edge switch
        {
            DockEdge.Left => rectangle with { Right = rectangle.Left + thickness },
            DockEdge.Top => rectangle with { Bottom = rectangle.Top + thickness },
            DockEdge.Right => rectangle with { Left = rectangle.Right - thickness },
            DockEdge.Bottom => rectangle with { Top = rectangle.Bottom - thickness },
            _ => rectangle
        };

    private static NativeMethods.NativeRect ToNative(PixelRect rectangle) => new()
    {
        Left = rectangle.Left,
        Top = rectangle.Top,
        Right = rectangle.Right,
        Bottom = rectangle.Bottom
    };

    private static PixelRect ToPixel(NativeMethods.NativeRect rectangle) =>
        new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
}
