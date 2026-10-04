using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BingLan.DockSpike.Interop;
using BingLan.DockSpike.Layout;
using BingLan.DockSpike.Windowing;

namespace BingLan.DockSpike;

public partial class DockSpikeWindow : Window
{
    private static readonly SolidColorBrush ActiveItemBrush = new(Color.FromArgb(0x66, 0xBF, 0xE3, 0xF5));
    private static readonly SolidColorBrush ActiveItemBorderBrush = new(Color.FromArgb(0xC2, 0x55, 0x9E, 0xF3));
    private static readonly SolidColorBrush RunningDotBrush = new(Color.FromArgb(0xC2, 0x1F, 0x3A, 0x55));
    private readonly WindowCatalog _catalog = new();
    private readonly WindowCommandService _commands = new();
    private readonly WindowEventWatcher _watcher;
    private readonly List<PinnedApp> _pinned;
    private readonly Dictionary<string, ImageSource> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _automationMode;
    private IReadOnlyList<MonitorSnapshot> _monitors = [];
    private IReadOnlyList<DockItem> _items = [];
    private MonitorSnapshot? _selectedMonitor;
    private AppBarController? _appBar;
    private nint _handle;
    private DockEdge _edge;
    private bool _reserveWorkArea;
    private bool _closing;
    private string _lastItemSignature = string.Empty;
    private bool _refreshRunning;
    private bool _refreshQueued;

    public DockSpikeWindow()
    {
        InitializeComponent();
        var arguments = Environment.GetCommandLineArgs();
        _automationMode = arguments
            .Any(argument => string.Equals(argument, "--automation", StringComparison.OrdinalIgnoreCase));
        _reserveWorkArea = !arguments
            .Any(argument => string.Equals(argument, "--overlay", StringComparison.OrdinalIgnoreCase));
        _edge = ParseEdge(arguments) ?? DockEdge.Bottom;
        if (_automationMode)
        {
            ShowInTaskbar = true;
        }
        _pinned = DefaultPinnedApps().ToList();
        _watcher = new WindowEventWatcher(Dispatcher);
        _watcher.SnapshotInvalidated += RefreshWindows;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        if (!_automationMode)
        {
            var style = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle);
            NativeMethods.SetWindowLongPtr(
                _handle,
                NativeMethods.GwlExStyle,
                (nint)((long)style | NativeMethods.WsExToolWindow));
        }

        _appBar = new AppBarController(this);
        _appBar.EnvironmentChanged += RefreshEnvironment;
        _watcher.Start();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshEnvironment();
        RefreshWindows();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _closing = true;
        _watcher.Dispose();
        _appBar?.Dispose();
    }

    private void RefreshEnvironment()
    {
        if (_closing)
        {
            return;
        }

        var preferredDevice = _selectedMonitor?.DeviceName;
        _monitors = MonitorCatalog.GetAll();
        _selectedMonitor = _monitors.FirstOrDefault(
                monitor => string.Equals(
                    monitor.DeviceName,
                    preferredDevice,
                    StringComparison.OrdinalIgnoreCase))
            ?? _monitors.FirstOrDefault(monitor => monitor.IsPrimary)
            ?? _monitors.FirstOrDefault();
        ApplyDock();
    }

    private void RefreshWindows()
    {
        if (_closing || _handle == 0)
        {
            return;
        }

        if (_refreshRunning)
        {
            _refreshQueued = true;
            return;
        }

        _ = RefreshWindowsLoopAsync();
    }

    private async Task RefreshWindowsLoopAsync()
    {
        _refreshRunning = true;
        try
        {
            do
            {
                _refreshQueued = false;
                var pinned = _pinned.ToArray();
                var snapshot = await Task.Run(() => _catalog.Capture(_handle));
                var groups = WindowGrouping.Group(snapshot);
                var items = DockItemComposer.Compose(pinned, groups);
                var icons = await Task.Run(() => ResolveIcons(items));
                if (_closing)
                {
                    return;
                }
                ApplyItems(items, icons);
            }
            while (_refreshQueued);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"窗口刷新失败：{exception}");
        }
        finally
        {
            _refreshRunning = false;
        }
    }

    private List<(string Key, IconHandle Icon)> ResolveIcons(IReadOnlyList<DockItem> items)
    {
        var resolved = new List<(string Key, IconHandle Icon)>();
        foreach (var item in items)
        {
            if (_icons.ContainsKey(item.Key))
            {
                continue;
            }

            var icon = item.Group is not null
                ? IconProvider.ForGroup(item.Group)
                : IconProvider.FromExecutable(item.Pinned?.ExecutablePath);
            if (!icon.HasIcon)
            {
                icon = IconProvider.GenericApplication();
            }
            resolved.Add((item.Key, icon));
        }
        return resolved;
    }

    private void ApplyItems(
        IReadOnlyList<DockItem> items,
        List<(string Key, IconHandle Icon)> icons)
    {
        foreach (var (key, icon) in icons)
        {
            if (!icon.HasIcon)
            {
                continue;
            }

            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                _icons[key] = source;
            }
            finally
            {
                if (icon.OwnedByCaller)
                {
                    NativeMethods.DestroyIcon(icon.Handle);
                }
            }
        }

        var signature = BuildSignature(items);
        if (string.Equals(signature, _lastItemSignature, StringComparison.Ordinal))
        {
            return;
        }
        _lastItemSignature = signature;
        _items = items;

        GroupPanel.Children.Clear();
        foreach (var item in items)
        {
            GroupPanel.Children.Add(BuildItem(item));
        }
        ApplyDock();
    }

    private Button BuildItem(DockItem item)
    {
        var vertical = _edge is DockEdge.Left or DockEdge.Right;
        var content = new StackPanel
        {
            Orientation = vertical ? Orientation.Horizontal : Orientation.Vertical
        };
        var icon = new Image
        {
            Width = 40,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        if (_icons.TryGetValue(item.Key, out var source))
        {
            icon.Source = source;
        }
        content.Children.Add(icon);

        var runningDot = new Border
        {
            Width = 4,
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = RunningDotBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = vertical ? new Thickness(3, 0, 0, 0) : new Thickness(0, 3, 0, 0),
            Visibility = item.IsRunning ? Visibility.Visible : Visibility.Collapsed
        };
        content.Children.Add(runningDot);

        var button = new Button
        {
            Content = content,
            Margin = vertical ? new Thickness(0, 4, 0, 4) : new Thickness(4, 0, 4, 0),
            Background = item.IsActive ? ActiveItemBrush : Brushes.Transparent,
            BorderBrush = item.IsActive ? ActiveItemBorderBrush : Brushes.Transparent,
            ToolTip = BuildToolTip(item)
        };
        AutomationProperties.SetName(button, item.DisplayName);
        AutomationProperties.SetHelpText(button, BuildToolTip(item));
        button.Click += (_, _) => Activate(item);
        button.MouseRightButtonUp += (_, eventArgs) =>
        {
            eventArgs.Handled = true;
            OpenItemMenu(item);
        };
        return button;
    }

    private void Activate(DockItem item)
    {
        if (item.Group is not null)
        {
            _commands.Toggle(item.Group);
        }
        else if (item.Pinned is not null)
        {
            _commands.Launch(item.Pinned);
        }
    }

    private void OpenItemMenu(DockItem item)
    {
        var menu = new ContextMenu { Placement = PlacementMode.Mouse };
        if (item.Group is not null)
        {
            var newInstance = new MenuItem { Header = "启动新实例" };
            newInstance.Click += (_, _) => _commands.LaunchNew(item.Group);
            menu.Items.Add(newInstance);

            if (item.HasMultipleWindows)
            {
                menu.Items.Add(new Separator());
                foreach (var window in item.Group.Windows)
                {
                    var selectWindow = new MenuItem { Header = window.Title };
                    selectWindow.Click += (_, _) => _commands.Toggle(
                        new WindowGroup(item.Group.Key, item.Group.DisplayName, [window]));
                    menu.Items.Add(selectWindow);
                }
            }
        }

        var pinPath = item.Pinned?.ExecutablePath
            ?? item.Group?.Windows
                .Select(window => window.ExecutablePath)
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
        if (!string.IsNullOrWhiteSpace(pinPath))
        {
            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
            }

            if (item.IsPinned)
            {
                var unpin = new MenuItem { Header = "从 Dock 取消固定" };
                unpin.Click += (_, _) =>
                {
                    var normalized = WindowGrouping.NormalizeExecutablePath(pinPath);
                    _pinned.RemoveAll(pinned => string.Equals(
                        WindowGrouping.NormalizeExecutablePath(pinned.ExecutablePath),
                        normalized,
                        StringComparison.OrdinalIgnoreCase));
                    RefreshWindows();
                };
                menu.Items.Add(unpin);
            }
            else
            {
                var pin = new MenuItem { Header = "固定到 Dock" };
                pin.Click += (_, _) =>
                {
                    _pinned.Add(new PinnedApp(pinPath));
                    RefreshWindows();
                };
                menu.Items.Add(pin);
            }
        }

        if (menu.Items.Count > 0)
        {
            menu.IsOpen = true;
        }
    }

    private void OnDockSurfaceRightClick(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu { Placement = PlacementMode.Mouse };

        var monitorMenu = new MenuItem { Header = "显示器" };
        foreach (var monitor in _monitors)
        {
            var item = new MenuItem
            {
                Header = monitor.Label,
                IsCheckable = true,
                IsChecked = monitor == _selectedMonitor
            };
            item.Click += (_, _) =>
            {
                _selectedMonitor = monitor;
                ApplyDock();
            };
            monitorMenu.Items.Add(item);
        }
        menu.Items.Add(monitorMenu);

        var edgeMenu = new MenuItem { Header = "屏幕边缘" };
        foreach (var edge in Enum.GetValues<DockEdge>())
        {
            var item = new MenuItem
            {
                Header = EdgeLabel(edge),
                IsCheckable = true,
                IsChecked = edge == _edge
            };
            item.Click += (_, _) =>
            {
                _edge = edge;
                RebuildItems();
                ApplyDock();
            };
            edgeMenu.Items.Add(item);
        }
        menu.Items.Add(edgeMenu);

        var reserve = new MenuItem
        {
            Header = "固定并保留工作区",
            IsCheckable = true,
            IsChecked = _reserveWorkArea
        };
        reserve.Click += (_, _) =>
        {
            _reserveWorkArea = reserve.IsChecked;
            ApplyDock();
        };
        menu.Items.Add(reserve);
        menu.Items.Add(new Separator());

        var close = new MenuItem { Header = "解除占位并退出" };
        close.Click += (_, _) => Close();
        menu.Items.Add(close);
        menu.IsOpen = true;
    }

    private void RebuildItems()
    {
        GroupPanel.Children.Clear();
        foreach (var item in _items)
        {
            GroupPanel.Children.Add(BuildItem(item));
        }
    }

    private void ApplyDock()
    {
        if (_selectedMonitor is null || _appBar is null)
        {
            return;
        }

        var vertical = _edge is DockEdge.Left or DockEdge.Right;
        GroupPanel.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        GroupScroller.HorizontalScrollBarVisibility = vertical
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Hidden;
        GroupScroller.VerticalScrollBarVisibility = vertical
            ? ScrollBarVisibility.Hidden
            : ScrollBarVisibility.Disabled;
        DockSurface.Padding = vertical ? new Thickness(9, 14, 9, 14) : new Thickness(14, 9, 14, 9);
        _appBar.Apply(
            _selectedMonitor,
            _edge,
            _reserveWorkArea,
            DockLayoutMetrics.ContentLengthForItems(_items.Count));
    }

    private static string BuildToolTip(DockItem item)
    {
        if (item.Group is null)
        {
            return item.DisplayName;
        }

        var builder = new StringBuilder(item.DisplayName);
        foreach (var window in item.Group.Windows)
        {
            if (!string.IsNullOrWhiteSpace(window.Title))
            {
                builder.AppendLine().Append(window.Title);
            }
        }
        return builder.ToString();
    }

    private static string BuildSignature(IReadOnlyList<DockItem> items)
    {
        var builder = new StringBuilder();
        foreach (var item in items)
        {
            builder.Append(item.Key)
                .Append(':')
                .Append(item.IsPinned ? 'P' : '-')
                .Append(item.IsActive ? 'A' : '-')
                .Append(item.WindowCount)
                .Append(':');
            if (item.Group is not null)
            {
                foreach (var window in item.Group.Windows)
                {
                    builder.Append(window.Title).Append('`');
                }
            }
            builder.Append('|');
        }
        return builder.ToString();
    }

    private static IEnumerable<PinnedApp> DefaultPinnedApps()
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var relativePath in new[] { "explorer.exe", @"System32\notepad.exe" })
        {
            var path = Path.Combine(windowsDirectory, relativePath);
            if (File.Exists(path))
            {
                yield return new PinnedApp(path);
            }
        }
    }

    private static string EdgeLabel(DockEdge edge) => edge switch
    {
        DockEdge.Left => "左侧",
        DockEdge.Top => "顶部",
        DockEdge.Right => "右侧",
        DockEdge.Bottom => "底部",
        _ => edge.ToString()
    };

    private static DockEdge? ParseEdge(IEnumerable<string> arguments)
    {
        const string prefix = "--edge=";
        var value = arguments
            .FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?
            .Substring(prefix.Length);
        return Enum.TryParse<DockEdge>(value, true, out var edge) ? edge : null;
    }
}
