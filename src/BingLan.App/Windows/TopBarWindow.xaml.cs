using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using BingLan.App.Dock;
using BingLan.App.Interop;
using BingLan.App.TopBar;
using BingLan.Core.Dock;
using BingLan.Core.Models;
using BingLan.Core.Services;
using BingLan.Core.TopBar;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using ToolTip = System.Windows.Controls.ToolTip;

namespace BingLan.App.Windows;

/// <summary>
/// What the top bar needs from the rest of the app. The window stays a dumb surface:
/// every fact and action comes from here, so tests can fake all of it.
/// </summary>
internal interface ITopBarEnvironment
{
    bool Use24HourClock { get; }

    /// <summary>The cached weather, or null while there is no city or no data yet.</summary>
    WeatherSnapshot? ReadWeather();

    /// <summary>Kicks off a network refresh when the service's own cadence says it is due.</summary>
    void RefreshWeatherIfDue();

    int CountIncompleteTodos();

    void OpenTopBarSettings();

    void OpenComponentSettings(DesktopComponentKind kind);

    /// <summary>Brings one window of an app that asked for attention to the front.</summary>
    bool ActivateWindow(nint handle);

    void ExitApp();
}

/// <summary>
/// The full-width strip at the top edge of one monitor. It only shows the app's own data
/// and read-only system status; clicking a module opens the matching settings page, and
/// an attention mark focuses that app. It never takes focus itself.
/// </summary>
public sealed partial class TopBarWindow : Window
{
    private static readonly TimeSpan SmartHideCheckInterval = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan FullScreenCheckInterval = TimeSpan.FromMilliseconds(500);
    private const int SystemInfoTickDivisor = 5;

    private readonly TopBarState _state;
    private readonly DesktopStyleState _style;
    private readonly ITopBarEnvironment _environment;
    private readonly IPerformanceSamplingService _sampler;
    private DockAutoHideState _autoHide = new();
    private readonly TopBarAttentionQueue _attention = new();
    private readonly Dictionary<nint, string> _attentionNames = [];
    private readonly WindowCatalog _catalog = new();
    private readonly DispatcherTimer _foregroundTimer;
    private readonly Dictionary<TopBarModuleKind, Button> _moduleButtons = [];
    private TopBarAppBarController? _appBar;
    private PerformanceSnapshot? _lastSnapshot;
    private MonitorSnapshot? _monitor;
    private nint _handle;
    private uint _shellHookMessage;
    private bool _shellHookActive;
    private bool _closing;
    private bool _menuOpen;
    private int _tick;
    private nint _lastForegroundWindow;

    private const int HshellWindowDestroyed = 2;
    private const int HshellWindowActivated = 4;
    private const int HshellRudeAppActivated = 0x8004;
    private const int HshellFlash = 0x8006;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;

    internal TopBarWindow(
        TopBarState state,
        DesktopStyleState style,
        ITopBarEnvironment environment,
        IPerformanceSamplingService sampler)
    {
        _state = state;
        _style = style;
        _environment = environment;
        _sampler = sampler;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        _foregroundTimer = new DispatcherTimer(
            FullScreenCheckInterval,
            DispatcherPriority.Background,
            (_, _) => UpdateForForeground(),
            Dispatcher);
        _sampler.Sampled += OnSampled;
        BuildContextMenu();
    }

    /// <summary>
    /// Re-reads the state: which monitor, which display mode, which modules, what look.
    /// Called whenever the settings or the desktop style change.
    /// </summary>
    internal void ApplyState()
    {
        if (_closing)
        {
            return;
        }

        _monitor = ResolveMonitor();
        if (_monitor is null)
        {
            return;
        }

        BuildModules();
        ApplySurface();
        _autoHide = new DockAutoHideState();
        var reserve = _state.VisibilityMode == TopBarVisibilityMode.ReserveTopEdge
            && TopBarReserveRules.IsTopEdgeFree(
                _monitor.Bounds,
                BingLan.App.Dock.DockAppBarController.GetLiveWorkingArea(_monitor));
        // The top edge is taken (a taskbar moved to the top on Windows 10): behave as
        // smart-hide instead of fighting over the space.
        _appBar?.Apply(_monitor, reserve, TopBarState.HeightDip);
        _foregroundTimer.Interval = reserve ? FullScreenCheckInterval : SmartHideCheckInterval;
        _foregroundTimer.Start();
        RenderModules();
        RenderSystemModules();
    }

    private MonitorSnapshot? ResolveMonitor()
    {
        var monitors = MonitorCatalog.GetAll();
        return monitors.FirstOrDefault(candidate => string.Equals(
                candidate.DeviceName,
                _state.MonitorDeviceName,
                StringComparison.OrdinalIgnoreCase))
            ?? monitors.FirstOrDefault(candidate => candidate.IsPrimary)
            ?? monitors.FirstOrDefault();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        var style = (long)NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLongPtr(
            _handle,
            NativeMethods.GwlExStyle,
            (nint)(style | NativeMethods.WsExToolWindow | DockNativeMethods.WsExNoActivate));
        var cornerPreference = NativeMethods.DwmWindowCornerDoNotRound;
        NativeMethods.DwmSetWindowAttribute(
            _handle,
            NativeMethods.DwmwaWindowCornerPreference,
            ref cornerPreference,
            sizeof(int));
        HwndSource.FromHwnd(_handle)?.AddHook(PreventActivation);

        // The bar registers its own shell hook: the dock's hook lives on its window and
        // disappears with it, while the bar needs the flash signal either way.
        _shellHookMessage = DockNativeMethods.RegisterWindowMessageW("SHELLHOOK");
        if (DockNativeMethods.RegisterShellHookWindow(_handle))
        {
            _shellHookActive = true;
            HwndSource.FromHwnd(_handle)?.AddHook(OnShellHook);
        }

        _appBar = new TopBarAppBarController(this, TopBarState.HeightDip);
        _appBar.EnvironmentChanged += ApplyState;
        _appBar.ExplorerRestarted += RenderModules;
        ApplyState();
    }

    private static nint PreventActivation(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmMouseActivate)
        {
            handled = true;
            return MaNoActivate;
        }
        return 0;
    }

    private nint OnShellHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (_shellHookMessage == 0 || (uint)message != _shellHookMessage)
        {
            return 0;
        }

        var changed = (int)wParam switch
        {
            HshellFlash => _attention.Flashed(lParam),
            HshellWindowActivated or HshellRudeAppActivated or HshellWindowDestroyed => _attention.Cleared(lParam),
            _ => false
        };
        if (changed)
        {
            RefreshAttention();
        }
        return 0;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        _foregroundTimer.Stop();
        _sampler.Sampled -= OnSampled;
        if (_shellHookActive && _handle != 0)
        {
            DockNativeMethods.DeregisterShellHookWindow(_handle);
        }
        _appBar?.Dispose();
        _appBar = null;
    }

    private void OnSampled(PerformanceSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsLoaded || _closing)
            {
                return;
            }
            _environment.RefreshWeatherIfDue();
            RenderModules();
        });
    }

    // ---------------------------------------------------------------- modules

    private void BuildModules()
    {
        LeftModules.Children.Clear();
        RightModules.Children.Clear();
        _moduleButtons.Clear();

        AddModule(TopBarModuleKind.TodoSummary, LeftModules, "待办",
            () => _environment.OpenComponentSettings(DesktopComponentKind.Todo));
        AddModule(TopBarModuleKind.Weather, LeftModules, "天气",
            () => _environment.OpenComponentSettings(DesktopComponentKind.Weather));
        AddModule(TopBarModuleKind.Performance, LeftModules, "性能",
            () => _environment.OpenComponentSettings(DesktopComponentKind.Performance));

        // Right side from the outer edge inwards: the docked-right panels stack from
        // the screen edge leftwards, so the clock is added last to sit at the corner.
        AddModule(TopBarModuleKind.Battery, RightModules, "电量", _environment.OpenTopBarSettings);
        AddModule(TopBarModuleKind.Network, RightModules, "网络", _environment.OpenTopBarSettings);
        AddModule(TopBarModuleKind.Volume, RightModules, "音量", _environment.OpenTopBarSettings);
        AddModule(TopBarModuleKind.InputMethod, RightModules, "输入法", _environment.OpenTopBarSettings);
        AddModule(TopBarModuleKind.Clock, RightModules, "时间日期",
            () => _environment.OpenComponentSettings(DesktopComponentKind.TimeDate));
        RefreshAttention();
    }

    private void AddModule(TopBarModuleKind kind, StackPanel panel, string label, Action click)
    {
        if (!TopBarRules.IsModuleOn(_state.Modules, kind))
        {
            return;
        }

        var text = new TextBlock
        {
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Effect = CreateTextShadow()
        };
        var button = new Button
        {
            Style = (Style)Resources["ModuleButton"],
            Content = text,
            Focusable = true
        };
        System.Windows.Automation.AutomationProperties.SetName(button, $"{label}模块");
        button.Click += (_, _) => click();
        if (kind == TopBarModuleKind.Weather)
        {
            button.ToolTip = new ToolTip();
        }
        _moduleButtons[kind] = button;
        panel.Children.Add(button);
    }

    private static DropShadowEffect CreateTextShadow() => new()
    {
        Color = Colors.Black,
        Opacity = 0.55d,
        BlurRadius = 2d,
        ShadowDepth = 1d,
        Direction = 270
    };

    private void RenderModules()
    {
        if (_closing)
        {
            return;
        }

        _tick++;
        if (_moduleButtons.TryGetValue(TopBarModuleKind.Clock, out var clock))
        {
            ((TextBlock)clock.Content).Text = FormatClock(_environment.Use24HourClock);
        }
        if (_moduleButtons.TryGetValue(TopBarModuleKind.TodoSummary, out var todo))
        {
            ((TextBlock)todo.Content).Text = $"待办 {_environment.CountIncompleteTodos()}";
        }
        if (_moduleButtons.TryGetValue(TopBarModuleKind.Weather, out var weather))
        {
            RenderWeather(weather);
        }
        if (_moduleButtons.TryGetValue(TopBarModuleKind.Performance, out var performance))
        {
            var snapshot = _lastSnapshot;
            ((TextBlock)performance.Content).Text = snapshot is null
                ? "性能 …"
                : $"CPU {PerformanceSamplingRules.FormatPercent(snapshot.CpuPercent)}  "
                    + $"内存 {PerformanceSamplingRules.FormatPercent(snapshot.MemoryPercent)}  "
                    + $"↑{PerformanceSamplingRules.FormatRate(snapshot.UploadBytesPerSecond)} "
                    + $"↓{PerformanceSamplingRules.FormatRate(snapshot.DownloadBytesPerSecond)}";
        }

        // System facts change slowly and some calls are not free, so they refresh on a
        // slower cadence than the clock; the input method also follows focus changes.
        if (_tick % SystemInfoTickDivisor == 0)
        {
            RenderSystemModules();
        }
    }

    private void RenderSystemModules()
    {
        if (_moduleButtons.TryGetValue(TopBarModuleKind.Battery, out var battery))
        {
            var (percent, status) = TopBarSystemInfo.ReadBattery();
            ((TextBlock)battery.Content).Text = percent < 0
                ? "电量 —"
                : status == TopBarBatteryStatus.Charging
                    ? $"充电 {percent}%"
                    : $"电量 {percent}%";
        }
        if (_moduleButtons.TryGetValue(TopBarModuleKind.Volume, out var volume))
        {
            var percent = TopBarSystemInfo.ReadVolumePercent();
            ((TextBlock)volume.Content).Text = percent is { } value ? $"音量 {value}%" : "音量 —";
        }
        if (_moduleButtons.TryGetValue(TopBarModuleKind.Network, out var network))
        {
            var (connected, label) = TopBarSystemInfo.ReadNetwork();
            ((TextBlock)network.Content).Text = label;
        }
        if (_moduleButtons.TryGetValue(TopBarModuleKind.InputMethod, out var inputMethod))
        {
            ((TextBlock)inputMethod.Content).Text = TopBarSystemInfo.ReadInputMethod();
        }
    }

    private void RenderWeather(Button button)
    {
        var text = (TextBlock)button.Content;
        var snapshot = _environment.ReadWeather();
        if (snapshot is null)
        {
            text.Text = "天气 —";
            return;
        }

        text.Text = snapshot.Status == WeatherStatus.NoCity
            ? "天气 未设置"
            : snapshot.TemperatureCelsius is { } temperature
                ? $"{snapshot.City} {Math.Round(temperature):0}° {snapshot.Condition}"
                : $"{snapshot.City} {snapshot.Condition}";
        if (button.ToolTip is ToolTip toolTip)
        {
            toolTip.Content = new TextBlock
            {
                Text = DescribeWeather(snapshot),
                MaxWidth = 320,
                TextWrapping = TextWrapping.Wrap
            };
        }
    }

    private static string DescribeWeather(WeatherSnapshot snapshot)
    {
        var lines = new List<string>();
        if (snapshot.DailyHighCelsius is { } high && snapshot.DailyLowCelsius is { } low)
        {
            lines.Add($"最高 {Math.Round(high):0}° / 最低 {Math.Round(low):0}°");
        }
        if (snapshot.HumidityPercent is { } humidity)
        {
            lines.Add($"湿度 {humidity}%");
        }
        lines.Add(snapshot.UpdatedAt is { } updated
            ? $"更新于 {updated:HH:mm}"
            : "尚无数据");
        if (snapshot.Status == WeatherStatus.Failed && !string.IsNullOrEmpty(snapshot.ErrorMessage))
        {
            lines.Add($"刷新失败：{snapshot.ErrorMessage}");
        }
        return string.Join("\n", lines);
    }

    private static string FormatClock(bool use24HourClock)
    {
        var now = DateTime.Now;
        var date = $"{now.Month}月{now.Day}日 {"日一二三四五六"[(int)now.DayOfWeek]}";
        var time = use24HourClock ? now.ToString("HH:mm") : now.ToString("hh:mm tt");
        return $"{date}  {time}";
    }

    private void RefreshAttention()
    {
        if (_closing)
        {
            return;
        }

        var running = _catalog.Capture();
        _attention.Retain(running.Select(window => window.Handle));
        var groups = WindowGrouping.Group(running);
        var (shown, overflow) = TopBarAttentionQueue.ResolveDisplay(
            _attention.OrderedWindows,
            TopBarState.MaximumAttentionApps);

        AttentionPanel.Children.Clear();
        foreach (var window in shown)
        {
            if (!_attentionNames.TryGetValue(window, out var name))
            {
                name = ResolveAttentionName(running, groups, window);
                _attentionNames[window] = name;
            }

            var button = new Button
            {
                Style = (Style)Resources["ModuleButton"],
                Content = new TextBlock
                {
                    Text = name,
                    Foreground = Brushes.Orange,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 120,
                    Effect = CreateTextShadow()
                },
                Focusable = true,
                Tag = window
            };
            System.Windows.Automation.AutomationProperties.SetName(button, $"{name}有新消息");
            button.Click += (_, _) =>
            {
                if (button.Tag is nint handle)
                {
                    _environment.ActivateWindow(handle);
                }
            };
            AttentionPanel.Children.Add(button);
        }
        if (overflow > 0)
        {
            AttentionPanel.Children.Add(new TextBlock
            {
                Text = $"+{overflow}",
                Foreground = Brushes.Orange,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 8, 0),
                Effect = CreateTextShadow()
            });
        }
    }

    private static string ResolveAttentionName(
        IReadOnlyList<TrackedWindow> running,
        IReadOnlyList<WindowGroup> groups,
        nint window)
    {
        var group = groups.FirstOrDefault(candidate =>
            candidate.Windows.Any(item => item.Handle == window));
        if (group is not null && !string.IsNullOrWhiteSpace(group.DisplayName))
        {
            return group.DisplayName;
        }
        var tracked = running.FirstOrDefault(candidate => candidate.Handle == window);
        return !string.IsNullOrWhiteSpace(tracked?.Title) ? tracked!.Title : "消息";
    }

    // ---------------------------------------------------------------- appearance

    private void ApplySurface()
    {
        var glass = DesktopStyleRules.TopBar(_style, _state);
        var background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(glass.Surface));
        background.Freeze();
        Surface.Background = background;
        var border = new SolidColorBrush((Color)ColorConverter.ConvertFromString(glass.Border));
        border.Freeze();
        Surface.BorderBrush = border;
        Surface.BorderThickness = new Thickness(0, 0, 0, 1);
    }

    // ---------------------------------------------------------------- visibility

    private void UpdateForForeground()
    {
        if (_appBar?.Monitor is not { } monitor || _closing)
        {
            return;
        }

        var bounds = _appBar.Bounds;
        var foreground = ForegroundWindowRect(out var foregroundBounds, out var foregroundWindow);
        if (foregroundWindow != 0 && foregroundWindow != _lastForegroundWindow)
        {
            // The keyboard layout follows the focused window, so the module follows it too
            // instead of waiting for the slower system-info cadence.
            _lastForegroundWindow = foregroundWindow;
            if (_moduleButtons.TryGetValue(TopBarModuleKind.InputMethod, out var inputMethod)
                && inputMethod.Content is TextBlock text)
            {
                text.Text = TopBarSystemInfo.ReadInputMethod();
            }
        }

        var fullScreen = foreground
            && foregroundBounds.Left <= monitor.Bounds.Left
            && foregroundBounds.Top <= monitor.Bounds.Top
            && foregroundBounds.Right >= monitor.Bounds.Right
            && foregroundBounds.Bottom >= monitor.Bounds.Bottom;
        var zoomed = foreground && foregroundWindow != 0 && DockNativeMethods.IsZoomed(foregroundWindow);
        var trueFullScreen = DockVacateRules.IsTrueFullScreen(fullScreen, zoomed);

        if (_appBar.IsReserved)
        {
            // A reserved bar rides above normal windows and under a full-screen app,
            // like the taskbar; nothing else moves it.
            _appBar.SetFullScreenDetected(fullScreen);
            return;
        }

        _foregroundTimer.Interval = trueFullScreen
            ? FullScreenCheckInterval
            : SmartHideCheckInterval;
        DockNativeMethods.GetCursorPos(out var cursor);
        var revealDepth = Math.Max(2, (int)Math.Round(2 * monitor.Dpi / 96d));
        var pointerOverBar = IsVisible
            && cursor.X >= bounds.Left && cursor.X < bounds.Right
            && cursor.Y >= bounds.Top && cursor.Y < bounds.Bottom;
        var overlaps = foreground && foregroundBounds.Intersects(bounds);
        var input = new DockAutoHideInput(
            WindowOverlapsDock: overlaps,
            ForegroundIsFullScreen: trueFullScreen,
            PointerInRevealZone: TopBarRevealRules.IsInRevealZone(
                cursor.X,
                cursor.Y,
                monitor.Bounds,
                revealDepth),
            PointerOverDock: pointerOverBar,
            InteractionActive: _menuOpen);
        if (_autoHide.Update(input, DateTimeOffset.UtcNow))
        {
            if (_autoHide.IsShown)
            {
                Show();
                RenderModules();
                RenderSystemModules();
            }
            else
            {
                Hide();
            }
        }
    }

    private bool ForegroundWindowRect(out PixelRect bounds, out nint window)
    {
        bounds = default;
        window = DockNativeMethods.GetForegroundWindow();
        if (window == 0 || window == _handle || DockNativeMethods.IsIconic(window))
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
                out DockNativeMethods.NativeRect rectangle,
                Marshal.SizeOf<DockNativeMethods.NativeRect>()) != 0
            && !DockNativeMethods.GetWindowRect(window, out rectangle))
        {
            return false;
        }

        bounds = new PixelRect(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
        return true;
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenu();
        var settings = new MenuItem { Header = "在冰蓝桌面中设置…" };
        settings.Click += (_, _) => _environment.OpenTopBarSettings();
        var exit = new MenuItem { Header = "退出冰蓝桌面" };
        exit.Click += (_, _) => _environment.ExitApp();
        menu.Items.Add(settings);
        menu.Items.Add(exit);
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) => _menuOpen = false;
        ContextMenu = menu;
    }
}
