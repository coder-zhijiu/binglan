using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using BingLan.WidgetHost.Wpf.Interop;

namespace BingLan.WidgetHost.Wpf;

public class WidgetWindow : Window
{
    private static readonly DependencyPropertyKey SurfaceCornerRadiusPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(SurfaceCornerRadius),
            typeof(CornerRadius),
            typeof(WidgetWindow),
            new FrameworkPropertyMetadata(new CornerRadius(15.5d)));

    public static readonly DependencyProperty SurfaceCornerRadiusProperty =
        SurfaceCornerRadiusPropertyKey.DependencyProperty;

    public static readonly DependencyProperty IsLayoutLockedProperty =
        DependencyProperty.Register(
            nameof(IsLayoutLocked),
            typeof(bool),
            typeof(WidgetWindow),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty ResizeBorderThicknessProperty =
        DependencyProperty.Register(
            nameof(ResizeBorderThickness),
            typeof(double),
            typeof(WidgetWindow),
            new FrameworkPropertyMetadata(7d),
            value => (double)value >= 0);

    public static readonly DependencyProperty BackdropPreferenceProperty =
        DependencyProperty.Register(
            nameof(BackdropPreference),
            typeof(WidgetBackdropPreference),
            typeof(WidgetWindow),
            new FrameworkPropertyMetadata(
                WidgetBackdropPreference.Auto,
                OnBackdropPreferenceChanged));

    public static readonly DependencyProperty WidgetCornerRadiusProperty =
        DependencyProperty.Register(
            nameof(WidgetCornerRadius),
            typeof(double),
            typeof(WidgetWindow),
            new FrameworkPropertyMetadata(
                15.5d,
                OnWidgetCornerRadiusChanged,
                CoerceWidgetCornerRadius));

    public static readonly DependencyProperty SolidFallbackColorProperty =
        DependencyProperty.Register(
            nameof(SolidFallbackColor),
            typeof(Color),
            typeof(WidgetWindow),
            new FrameworkPropertyMetadata(
                Color.FromRgb(238, 242, 246),
                OnSolidFallbackColorChanged,
                CoerceSolidFallbackColor));

    private HwndSource? _source;
    private bool _isUpdatingNativeWindowShape;
    private int _regionDiameter = -1;
    private int _regionHeight = -1;
    private int _regionWidth = -1;

    public WidgetWindow()
    {
        AllowsTransparency = false;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;
        Background = Brushes.Transparent;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => RefreshNativeWindowShape();
        DpiChanged += (_, _) =>
        {
            ResetRegionCache();
            RefreshNativeWindowShape();
        };
        Loaded += (_, _) => RefreshBackdrop();
        ContentRendered += (_, _) => RefreshBackdrop();
    }

    public event EventHandler<WidgetBackdropResult>? BackdropChanged;

    public bool IsLayoutLocked
    {
        get => (bool)GetValue(IsLayoutLockedProperty);
        set => SetValue(IsLayoutLockedProperty, value);
    }

    public double ResizeBorderThickness
    {
        get => (double)GetValue(ResizeBorderThicknessProperty);
        set => SetValue(ResizeBorderThicknessProperty, value);
    }

    public WidgetBackdropPreference BackdropPreference
    {
        get => (WidgetBackdropPreference)GetValue(BackdropPreferenceProperty);
        set => SetValue(BackdropPreferenceProperty, value);
    }

    public double WidgetCornerRadius
    {
        get => (double)GetValue(WidgetCornerRadiusProperty);
        set => SetValue(WidgetCornerRadiusProperty, value);
    }

    public CornerRadius SurfaceCornerRadius =>
        (CornerRadius)GetValue(SurfaceCornerRadiusProperty);

    public Color SolidFallbackColor
    {
        get => (Color)GetValue(SolidFallbackColorProperty);
        set => SetValue(SolidFallbackColorProperty, value);
    }

    public WidgetBackdropResult BackdropResult { get; private set; } =
        new(WidgetBackdropMode.SolidFallback, "窗口尚未初始化");

    public string? NativeStyleError { get; private set; }

    public string? NativeShapeError { get; private set; }

    public void RefreshBackdrop()
    {
        if (_source is null || _source.IsDisposed)
        {
            return;
        }

        try
        {
            BackdropResult = WidgetBackdropController.Apply(this, _source, BackdropPreference);
        }
        catch (Exception exception)
        {
            var color = SolidFallbackColor;
            _source.CompositionTarget.BackgroundColor = color;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Background = brush;
            BackdropResult = new WidgetBackdropResult(
                WidgetBackdropMode.SolidFallback,
                $"材质异常，已回退纯色：{exception.GetType().Name}");
        }
        BackdropChanged?.Invoke(this, BackdropResult);
    }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        if (AllowsTransparency)
        {
            throw new InvalidOperationException(
                "WidgetWindow 必须使用普通 HWND；不要设置 AllowsTransparency=True。");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_source is not null && !_source.IsDisposed)
        {
            _source.RemoveHook(WindowProc);
        }
        _source = null;
        base.OnClosed(e);
    }

    private static void OnBackdropPreferenceChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is WidgetWindow window)
        {
            window.RefreshBackdrop();
        }
    }

    private static object CoerceWidgetCornerRadius(
        DependencyObject dependencyObject,
        object baseValue)
    {
        var radius = (double)baseValue;
        if (double.IsNaN(radius) || double.IsInfinity(radius))
        {
            return 15.5d;
        }
        return Math.Clamp(radius, 0d, 32d);
    }

    private static void OnWidgetCornerRadiusChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not WidgetWindow window)
        {
            return;
        }

        var radius = (double)args.NewValue;
        window.SetValue(SurfaceCornerRadiusPropertyKey, new CornerRadius(radius));
        window.ResetRegionCache();
        window.RefreshNativeWindowShape();
    }

    private static object CoerceSolidFallbackColor(
        DependencyObject dependencyObject,
        object baseValue)
    {
        var color = (Color)baseValue;
        return Color.FromRgb(color.R, color.G, color.B);
    }

    private static void OnSolidFallbackColorChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is WidgetWindow
            {
                BackdropResult.Mode: WidgetBackdropMode.SolidFallback
            } window)
        {
            window.RefreshBackdrop();
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (AllowsTransparency)
        {
            throw new InvalidOperationException(
                "WidgetWindow 必须使用普通 HWND；不要设置 AllowsTransparency=True。");
        }

        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowProc);

        try
        {
            ApplyNativeWindowStyles(handle, ShowInTaskbar);
            NativeStyleError = null;
        }
        catch (Win32Exception exception)
        {
            NativeStyleError = exception.Message;
        }
        RefreshNativeWindowShape();
        RefreshBackdrop();
    }

    private void RefreshNativeWindowShape()
    {
        if (_source is null || _source.IsDisposed || _isUpdatingNativeWindowShape)
        {
            return;
        }

        _isUpdatingNativeWindowShape = true;
        try
        {
            string? error = null;
            var cornerPreference = NativeMethods.DwmWindowCornerDoNotRound;
            var cornerResult = NativeMethods.DwmSetWindowAttribute(
                _source.Handle,
                NativeMethods.DwmwaWindowCornerPreference,
                ref cornerPreference,
                Marshal.SizeOf<int>());
            if (cornerResult < 0)
            {
                error = $"关闭 DWM 系统圆角失败：0x{cornerResult:X8}";
            }

            try
            {
                ApplyNativeWindowRegion(_source.Handle);
            }
            catch (Win32Exception exception)
            {
                ResetRegionCache();
                var clearResult = NativeMethods.SetWindowRgn(
                    _source.Handle,
                    0,
                    redraw: true);
                var clearDetail = clearResult == 0
                    ? $"；清除旧裁剪失败：{new Win32Exception(Marshal.GetLastWin32Error()).Message}"
                    : string.Empty;
                error = error is null
                    ? $"{exception.Message}{clearDetail}"
                    : $"{error}；{exception.Message}{clearDetail}";
            }
            NativeShapeError = error;
        }
        finally
        {
            _isUpdatingNativeWindowShape = false;
        }
    }

    private void ApplyNativeWindowRegion(nint handle)
    {
        if (!NativeMethods.GetWindowRect(handle, out var rectangle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var width = rectangle.Right - rectangle.Left;
        var height = rectangle.Bottom - rectangle.Top;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var radius = Math.Min(
            (int)Math.Round(
                WidgetCornerRadius * dpi.DpiScaleX,
                MidpointRounding.AwayFromZero),
            Math.Min(width, height) / 2);
        var diameter = radius * 2;
        if (_regionWidth == width &&
            _regionHeight == height &&
            _regionDiameter == diameter)
        {
            return;
        }

        if (diameter == 0)
        {
            if (NativeMethods.SetWindowRgn(handle, 0, redraw: true) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        else
        {
            var region = NativeMethods.CreateRoundRectRgn(
                0,
                0,
                width,
                height,
                diameter,
                diameter);
            if (region == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (NativeMethods.SetWindowRgn(handle, region, redraw: true) == 0)
            {
                var error = Marshal.GetLastWin32Error();
                _ = NativeMethods.DeleteObject(region);
                throw new Win32Exception(error);
            }

            // SetWindowRgn owns the HRGN after success. Deleting it here would
            // invalidate the live window shape; only the failure path deletes it.
        }

        _regionWidth = width;
        _regionHeight = height;
        _regionDiameter = diameter;
    }

    private void ResetRegionCache()
    {
        _regionWidth = -1;
        _regionHeight = -1;
        _regionDiameter = -1;
    }

    private static void ApplyNativeWindowStyles(nint handle, bool showInTaskbar)
    {
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlStyle).ToInt64();
        style |= NativeMethods.WsThickFrame;
        SetWindowLongChecked(handle, NativeMethods.GwlStyle, style);

        var exStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        exStyle |= NativeMethods.WsExAcceptFiles;
        if (showInTaskbar)
        {
            exStyle |= NativeMethods.WsExAppWindow;
            exStyle &= ~NativeMethods.WsExToolWindow;
        }
        else
        {
            exStyle |= NativeMethods.WsExToolWindow;
            exStyle &= ~NativeMethods.WsExAppWindow;
        }
        exStyle &= ~(NativeMethods.WsExLayered |
                     NativeMethods.WsExNoActivate |
                     NativeMethods.WsExTopmost |
                     NativeMethods.WsExTransparent);
        SetWindowLongChecked(handle, NativeMethods.GwlExStyle, exStyle);

        if (!NativeMethods.SetWindowPos(
                handle,
                0,
                0,
                0,
                0,
                0,
                NativeMethods.SwpNoMove |
                NativeMethods.SwpNoSize |
                NativeMethods.SwpNoZOrder |
                NativeMethods.SwpNoActivate |
                NativeMethods.SwpFrameChanged))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static void SetWindowLongChecked(nint handle, int index, long value)
    {
        Marshal.SetLastPInvokeError(0);
        var previous = NativeMethods.SetWindowLongPtr(handle, index, (nint)value);
        var error = Marshal.GetLastPInvokeError();
        if (previous == 0 && error != 0)
        {
            throw new Win32Exception(error);
        }
    }

    private nint WindowProc(
        nint window,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message == NativeMethods.WmNcCalcSize && wParam != 0)
        {
            handled = true;
            return 0;
        }

        if (message == NativeMethods.WmNcHitTest)
        {
            return HandleHitTest(lParam, ref handled);
        }

        if (message is NativeMethods.WmDwmCompositionChanged
            or NativeMethods.WmThemeChanged
            or NativeMethods.WmSettingChange)
        {
            ResetRegionCache();
            RefreshNativeWindowShape();
            RefreshBackdrop();
        }

        return 0;
    }

    private nint HandleHitTest(nint lParam, ref bool handled)
    {
        handled = true;
        if (IsLayoutLocked)
        {
            return NativeMethods.HtClient;
        }

        var packed = lParam.ToInt64();
        var screenPoint = new Point(
            unchecked((short)(packed & 0xFFFF)),
            unchecked((short)((packed >> 16) & 0xFFFF)));
        var point = PointFromScreen(screenPoint);
        var border = ResizeBorderThickness;

        var cornerHit = GetRoundedCornerHit(point, border);
        if (cornerHit != 0)
        {
            return cornerHit;
        }

        var left = point.X <= border;
        var right = point.X >= ActualWidth - border;
        var top = point.Y <= border;
        var bottom = point.Y >= ActualHeight - border;

        return (left, right, top, bottom) switch
        {
            (true, _, _, _) => NativeMethods.HtLeft,
            (_, true, _, _) => NativeMethods.HtRight,
            (_, _, true, _) => NativeMethods.HtTop,
            (_, _, _, true) => NativeMethods.HtBottom,
            _ => IsInteractive(point) ? NativeMethods.HtClient : NativeMethods.HtCaption
        };
    }

    private nint GetRoundedCornerHit(Point point, double border)
    {
        var radius = Math.Min(
            WidgetCornerRadius,
            Math.Min(ActualWidth, ActualHeight) / 2d);
        if (radius <= 0d)
        {
            return 0;
        }

        if (IsInCornerResizeBand(radius - point.X, radius - point.Y, point.X, point.Y))
        {
            return NativeMethods.HtTopLeft;
        }
        if (IsInCornerResizeBand(
                point.X - (ActualWidth - radius),
                radius - point.Y,
                ActualWidth - point.X,
                point.Y))
        {
            return NativeMethods.HtTopRight;
        }
        if (IsInCornerResizeBand(
                radius - point.X,
                point.Y - (ActualHeight - radius),
                point.X,
                ActualHeight - point.Y))
        {
            return NativeMethods.HtBottomLeft;
        }
        if (IsInCornerResizeBand(
                point.X - (ActualWidth - radius),
                point.Y - (ActualHeight - radius),
                ActualWidth - point.X,
                ActualHeight - point.Y))
        {
            return NativeMethods.HtBottomRight;
        }
        return 0;

        bool IsInCornerResizeBand(
            double deltaX,
            double deltaY,
            double distanceFromVerticalEdge,
            double distanceFromHorizontalEdge)
        {
            if (distanceFromVerticalEdge < 0d ||
                distanceFromHorizontalEdge < 0d ||
                distanceFromVerticalEdge > radius ||
                distanceFromHorizontalEdge > radius)
            {
                return false;
            }

            var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            return distance >= Math.Max(0d, radius - border) &&
                   distance <= radius + 0.75d;
        }
    }

    private bool IsInteractive(Point point)
    {
        DependencyObject? current = InputHitTest(point) as DependencyObject;
        while (current is not null)
        {
            if (WidgetHitTest.GetIsInteractive(current)
                || current is ButtonBase
                or TextBoxBase
                or PasswordBox
                or Selector
                or RangeBase
                or ScrollBar
                or Thumb)
            {
                return true;
            }

            current = current is Visual or Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }
}
