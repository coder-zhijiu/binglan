using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

    /// <summary>The first to-do card's live items, for the to-do flyout.</summary>
    TopBarTodoList? ReadTodoList();

    /// <summary>An item was toggled in the flyout; persist at the host's pace.</summary>
    void TodoItemToggled();

    void OpenTopBarSettings();

    void OpenComponentSettings(DesktopComponentKind kind);

    /// <summary>Brings one window of an app that asked for attention to the front.</summary>
    bool ActivateWindow(nint handle);

    void ExitApp();
}

/// <summary>
/// The list the to-do flyout shows: the first card's title and its live items. The
/// items are the card's own state objects, so toggling one in the flyout reaches the
/// card and the persistence through the same objects.
/// </summary>
internal sealed record TopBarTodoList(string Title, IReadOnlyList<TodoItemState> Items);

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
    private readonly StackPanel _attentionPanel = new()
    {
        Orientation = System.Windows.Controls.Orientation.Horizontal
    };
    private DockAutoHideState _autoHide = new();
    private readonly TopBarAttentionQueue _attention = new();
    private readonly Dictionary<nint, string> _attentionNames = [];
    private readonly WindowCatalog _catalog = new();
    private readonly DispatcherTimer _foregroundTimer;
    private readonly Dictionary<TopBarModuleKind, Button> _moduleButtons = [];
    private TopBarAppBarController? _appBar;
    private PerformanceSnapshot? _lastSnapshot;
    private TopBarSystemFacts? _facts;
    private Popup? _todoFlyout;
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
        // A network address change is the one system fact with an event source; the
        // others ride the sampling tick.
        System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
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
        // The top edge is taken (a taskbar moved to the top on Windows 10): behave as
        // smart-hide instead of fighting over the space. A bar that already holds the
        // edge keeps it — its own reservation is not a foreign occupier.
        var reserve = TopBarReserveRules.ShouldReserve(
            _state.VisibilityMode == TopBarVisibilityMode.ReserveTopEdge,
            _appBar?.IsReserved == true,
            _monitor.Bounds,
            BingLan.App.Dock.DockAppBarController.GetLiveWorkingArea(_monitor));
        _appBar?.Apply(_monitor, reserve, TopBarState.HeightDip);
        // While the flyout is open the fast interval keeps its outside-click watch
        // prompt even in reserve mode.
        _foregroundTimer.Interval = reserve && _todoFlyout is null
            ? FullScreenCheckInterval
            : SmartHideCheckInterval;
        _foregroundTimer.Start();
        RenderModules();
        RefreshSystemFacts();
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
        CloseTodoFlyout();
        _foregroundTimer.Stop();
        _sampler.Sampled -= OnSampled;
        System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        if (_shellHookActive && _handle != 0)
        {
            DockNativeMethods.DeregisterShellHookWindow(_handle);
        }
        _appBar?.Dispose();
        _appBar = null;
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e) => RefreshSystemFacts();

    private void OnSampled(PerformanceSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        // System facts are read off the UI thread: network enumeration and the volume COM
        // call are not free. The tick counter keeps them on their slower cadence.
        TopBarSystemFacts? facts = null;
        if (Interlocked.Increment(ref _tick) % SystemInfoTickDivisor == 0)
        {
            facts = ReadSystemFacts();
        }
        Dispatcher.BeginInvoke(() =>
        {
            // A smart-hidden bar is not on screen; its projection waits until it shows.
            if (!IsLoaded || !IsVisible || _closing)
            {
                return;
            }
            if (facts is not null)
            {
                _facts = facts;
                RenderSystemModules();
            }
            _environment.RefreshWeatherIfDue();
            RenderModules();
        });
    }

    private static TopBarSystemFacts ReadSystemFacts() => new(
        TopBarSystemInfo.ReadBattery(),
        TopBarSystemInfo.ReadVolumePercent(),
        TopBarSystemInfo.ReadNetwork(),
        TopBarSystemInfo.ReadInputMethod());

    private void RefreshSystemFacts()
    {
        if (_closing)
        {
            return;
        }

        // The reads leave the UI thread: enumerating interfaces and the volume COM call
        // are not free, and ApplyState can run on every display or settings change.
        Task.Run(() =>
        {
            if (_closing)
            {
                return;
            }

            var facts = ReadSystemFacts();
            Dispatcher.BeginInvoke(() =>
            {
                if (_closing)
                {
                    return;
                }
                _facts = facts;
                if (IsLoaded)
                {
                    RenderSystemModules();
                }
            });
        });
    }

    private sealed record TopBarSystemFacts(
        (int Percent, TopBarBatteryStatus Status) Battery,
        int? VolumePercent,
        (bool Connected, string Label) Network,
        string InputMethod);

    // ---------------------------------------------------------------- modules

    private void BuildModules()
    {
        LeftModules.Children.Clear();
        RightModules.Children.Clear();
        _moduleButtons.Clear();
        CloseTodoFlyout();

        // The to-do module shows its own flyout instead of opening settings.
        AddTodoModule(LeftModules);
        AddModule(TopBarModuleKind.Weather, LeftModules, "天气",
            () => _environment.OpenComponentSettings(DesktopComponentKind.Weather));
        AddModule(TopBarModuleKind.Performance, LeftModules, "性能",
            () => _environment.OpenComponentSettings(DesktopComponentKind.Performance));

        // Right side from the outer edge inwards: the docked-right stack fills from the
        // screen edge leftwards, so the clock is added last to sit at the corner. The
        // attention marks sit between the input method and the clock.
        AddModule(TopBarModuleKind.Battery, RightModules, "电量", _environment.OpenTopBarSettings);
        AddModule(TopBarModuleKind.Network, RightModules, "网络", _environment.OpenTopBarSettings);
        AddModule(TopBarModuleKind.Volume, RightModules, "音量", _environment.OpenTopBarSettings);
        AddModule(TopBarModuleKind.InputMethod, RightModules, "输入法", _environment.OpenTopBarSettings);
        if (TopBarRules.IsModuleOn(_state.Modules, TopBarModuleKind.Attention))
        {
            RightModules.Children.Add(_attentionPanel);
        }
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

    private void AddTodoModule(StackPanel panel)
    {
        if (!TopBarRules.IsModuleOn(_state.Modules, TopBarModuleKind.TodoSummary))
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
        System.Windows.Automation.AutomationProperties.SetName(button, "待办模块");
        button.Click += (_, _) => ToggleTodoFlyout(button);
        _moduleButtons[TopBarModuleKind.TodoSummary] = button;
        panel.Children.Add(button);
    }

    // ---------------------------------------------------------------- 待办信息栏

    /// <summary>
    /// Shows the first to-do card's items under the module. A row click toggles that
    /// item's completion, exactly like the card's checkbox, and the card follows along
    /// because both share the same state object; clicking elsewhere closes the flyout.
    /// </summary>
    private void ToggleTodoFlyout(Button button)
    {
        if (_todoFlyout?.IsOpen == true)
        {
            _todoFlyout.IsOpen = false;
            return;
        }

        var glass = DesktopStyleRules.TopBar(_style, _state);
        var list = _environment.ReadTodoList();
        var items = list?.Items ?? [];
        var panel = new StackPanel { MaxWidth = 300 };

        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(list?.Title) ? "待办" : list!.Title,
            Foreground = Brushes.White,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 6),
            Effect = CreateTextShadow()
        });
        if (items.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "暂无待办",
                Foreground = Brushes.White,
                Opacity = 0.7,
                Effect = CreateTextShadow()
            });
        }
        else
        {
            foreach (var item in items)
            {
                panel.Children.Add(CreateTodoRow(item));
            }
        }

        var card = new Border
        {
            Background = FrozenBrush(glass.Surface),
            BorderBrush = FrozenBrush(glass.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 12),
            Child = panel
        };
        var flyout = new Popup
        {
            PlacementTarget = button,
            Placement = PlacementMode.Bottom,
            HorizontalOffset = -6,
            VerticalOffset = 2,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = card
        };
        flyout.Closed += (_, _) =>
        {
            if (ReferenceEquals(_todoFlyout, flyout))
            {
                _todoFlyout = null;
            }
        };
        // A popup outside the visual tree never hears outside clicks at all, so it
        // lives in the module panel; it takes no layout space while closed.
        LeftModules.Children.Add(flyout);
        _todoFlyout = flyout;
        flyout.IsOpen = true;
    }

    private FrameworkElement CreateTodoRow(TodoItemState item)
    {
        var glyph = new TextBlock
        {
            Width = 18,
            Foreground = Brushes.White,
            Opacity = 0.7,
            Effect = CreateTextShadow()
        };
        var glyphStyle = new Style(typeof(TextBlock));
        glyphStyle.Triggers.Add(TodoCompletedTrigger(
            new Setter(TextBlock.TextProperty, "✓"),
            new Setter(TextBlock.OpacityProperty, 0.9)));
        glyph.Style = glyphStyle;

        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 270,
            Foreground = Brushes.White,
            Effect = CreateTextShadow()
        };
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(TodoItemState.Text)));
        var textStyle = new Style(typeof(TextBlock));
        textStyle.Triggers.Add(TodoCompletedTrigger(
            new Setter(TextBlock.TextDecorationsProperty, TextDecorations.Strikethrough),
            new Setter(TextBlock.OpacityProperty, 0.55)));
        text.Style = textStyle;

        var row = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 3, 0, 3),
            Cursor = System.Windows.Input.Cursors.Hand,
            DataContext = item
        };
        row.Children.Add(glyph);
        row.Children.Add(text);
        row.MouseLeftButtonUp += (_, _) =>
        {
            // One behaviour, like the card's checkbox: the row toggles completion.
            item.IsCompleted = !item.IsCompleted;
            _environment.TodoItemToggled();
        };
        return row;
    }

    private static DataTrigger TodoCompletedTrigger(params Setter[] setters)
    {
        var trigger = new DataTrigger
        {
            Binding = new System.Windows.Data.Binding(nameof(TodoItemState.IsCompleted)),
            Value = true
        };
        foreach (var setter in setters)
        {
            trigger.Setters.Add(setter);
        }
        return trigger;
    }

    private void CloseTodoFlyout()
    {
        _todoFlyout?.SetCurrentValue(Popup.IsOpenProperty, false);
        _todoFlyout = null;
    }

    /// <summary>
    /// A StaysOpen=false popup never hears clicks that land in other processes: the
    /// system drops the capture instead of telling the popup. While the flyout is open
    /// the bar polls the button state and closes it when a click lands outside it.
    /// </summary>
    private void CloseTodoFlyoutOnOutsideClick()
    {
        if (_todoFlyout is not { IsOpen: true } flyout || flyout.Child is not FrameworkElement child)
        {
            return;
        }

        if (!TopBarNativeMethods.IsMouseButtonDown(TopBarNativeMethods.LeftMouseButton))
        {
            return;
        }

        if (System.Windows.PresentationSource.FromVisual(child) is not HwndSource source || source.Handle == 0)
        {
            return;
        }
        DockNativeMethods.GetCursorPos(out var cursor);
        DockNativeMethods.GetWindowRect(source.Handle, out var rect);
        if (cursor.X < rect.Left || cursor.X >= rect.Right || cursor.Y < rect.Top || cursor.Y >= rect.Bottom)
        {
            flyout.IsOpen = false;
        }
    }

    /// <summary>The open to-do flyout, for tests; popups sit outside the visual tree.</summary>
    internal Popup? TodoFlyout => _todoFlyout;

    private static System.Windows.Media.SolidColorBrush FrozenBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
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
        if (_facts is not { } facts)
        {
            return;
        }

        if (_moduleButtons.TryGetValue(TopBarModuleKind.Battery, out var battery))
        {
            var (percent, status) = facts.Battery;
            ((TextBlock)battery.Content).Text = percent < 0
                ? "电量 —"
                : status == TopBarBatteryStatus.Charging
                    ? $"充电 {percent}%"
                    : $"电量 {percent}%";
        }
        if (_moduleButtons.TryGetValue(TopBarModuleKind.Volume, out var volume))
        {
            var percent = facts.VolumePercent;
            ((TextBlock)volume.Content).Text = percent is { } value ? $"音量 {value}%" : "音量 —";
        }
        if (_moduleButtons.TryGetValue(TopBarModuleKind.Network, out var network))
        {
            ((TextBlock)network.Content).Text = facts.Network.Label;
        }
        if (_moduleButtons.TryGetValue(TopBarModuleKind.InputMethod, out var inputMethod))
        {
            ((TextBlock)inputMethod.Content).Text = facts.InputMethod;
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

        if (!TopBarRules.IsModuleOn(_state.Modules, TopBarModuleKind.Attention))
        {
            _attentionPanel.Children.Clear();
            return;
        }

        var running = _catalog.Capture();
        _attention.Retain(running.Select(window => window.Handle));
        // Names of windows that have since closed would survive handle reuse, so they go.
        var alive = running.Select(window => window.Handle).ToHashSet();
        foreach (var stale in _attentionNames.Keys.Where(handle => !alive.Contains(handle)).ToList())
        {
            _attentionNames.Remove(stale);
        }
        var groups = WindowGrouping.Group(running);
        var (shown, overflow) = TopBarAttentionQueue.ResolveDisplay(
            _attention.OrderedWindows,
            TopBarState.MaximumAttentionApps);

        _attentionPanel.Children.Clear();
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
            _attentionPanel.Children.Add(button);
        }
        if (overflow > 0)
        {
            _attentionPanel.Children.Add(new TextBlock
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
        CloseTodoFlyoutOnOutsideClick();
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
            InteractionActive: _menuOpen || _todoFlyout?.IsOpen == true);
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
