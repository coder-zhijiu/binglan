using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using BingLan.App.Interop;
using BingLan.App.Services;
using BingLan.App.Windows;
using BingLan.Core.Models;
using BingLan.Core.Services;
using BingLan.Core.TopBar;

/// <summary>
/// Drives the top bar without a mouse: window style, module layout, deep-link clicks and
/// the module switches. The tests always use smart-hide mode so no real AppBar is
/// registered on the machine running the suite.
/// </summary>
internal static class TopBarWindowTests
{
    private const int GwlExStyle = -20;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    internal static void Run(Action<Window> show, Action pump)
    {
        ShowModulesAndStyles(show, pump);
        ModuleSwitchesRemoveButtons(show, pump);
        ClicksOpenSettings(show, pump);
        SurfacePaintsGlass(show, pump);
    }

    private static void SurfacePaintsGlass(Action<Window> show, Action pump)
    {
        var state = BarState();
        state.VisibilityMode = TopBarVisibilityMode.SmartHide;
        state.FollowCardLook = false;
        state.SurfaceColor = "#523861";
        state.SurfaceOpacity = 0.84d;
        using var sampler = new WindowsPerformanceSamplingService();
        var bar = new TopBarWindow(state, new DesktopStyleState(), new FakeEnvironment(), sampler);
        show(bar);
        try
        {
            var background = bar.Surface.Background as SolidColorBrush
                ?? throw new InvalidOperationException("Surface.Background 应为 SolidColorBrush");
            var color = background.Color;
            Assert(color.A > 200 && color.R == 0x52 && color.G == 0x38 && color.B == 0x61,
                $"玻璃底画刷应为紫色 84%，实际 A={color.A} #{color.R:X2}{color.G:X2}{color.B:X2}");

            // What actually reaches the screen: render the surface and sample the middle.
            var render = new System.Windows.Media.Imaging.RenderTargetBitmap(
                200, 32, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            render.Render(bar.Surface);
            render.Freeze();
            var pixels = new byte[200 * 32 * 4];
            render.CopyPixels(pixels, 200 * 4, 0);
            var sample = pixels.AsSpan((160 * 4) + (16 * 200 * 4), 4);
            Assert(sample[3] > 100,
                $"玻璃底应实际渲染（中心像素 alpha={sample[3]}），而非只剩文字和边线");

            // And through the real layered-window path: capture this window's own pixels
            // off the screen and look for the glass tint in the middle of the strip.
            var handle = new WindowInteropHelper(bar).Handle;
            GetWindowRect(handle, out var screenRect);
            using var capture = new System.Drawing.Bitmap(
                Math.Max(1, screenRect.Right - screenRect.Left),
                Math.Max(1, screenRect.Bottom - screenRect.Top));
            using (var graphics = System.Drawing.Graphics.FromImage(capture))
            {
                graphics.CopyFromScreen(screenRect.Left, screenRect.Top, 0, 0, capture.Size);
            }
            var middle = capture.GetPixel(capture.Width / 2, capture.Height / 2);
            Assert(middle.R > 40 && middle.B > 60 && middle.B > middle.G,
                $"屏幕合成应有玻璃底色（中点 #{middle.R:X2}{middle.G:X2}{middle.B:X2}），" +
                "分层窗口只画了文字没有底");
        }
        finally
        {
            bar.Close();
            pump();
        }
    }

    private static void ShowModulesAndStyles(Action<Window> show, Action pump)
    {
        var state = BarState();
        state.VisibilityMode = TopBarVisibilityMode.SmartHide;
        using var sampler = new WindowsPerformanceSamplingService();
        var environment = new FakeEnvironment
        {
            Todos = 2,
            Weather = new WeatherSnapshot(
                WeatherStatus.Fresh, "北京", 18.4d, 24d, 11d, 42, "多云", false,
                DateTimeOffset.Now, null)
        };
        var bar = new TopBarWindow(state, new DesktopStyleState(), environment, sampler);
        show(bar);
        try
        {
            var hwnd = new WindowInteropHelper(bar).Handle;
            var style = (long)GetWindowLongPtr(hwnd, GwlExStyle);
            if ((style & NativeMethods.WsExToolWindow) == 0)
            {
                throw new InvalidOperationException("顶栏窗口缺少 ToolWindow 扩展样式，会出现在任务栏");
            }
            if ((style & DockNativeMethods.WsExNoActivate) == 0)
            {
                throw new InvalidOperationException("顶栏窗口缺少 NoActivate 扩展样式，点击会抢焦点");
            }

            var buttons = AllButtons(bar);
            var names = buttons.Select(AutomationProperties.GetName).ToHashSet();
            foreach (var module in new[]
                     {
                         "待办模块", "天气模块", "性能模块", "电量模块", "网络模块", "音量模块", "输入法模块",
                         "时间日期模块"
                     })
            {
                if (!names.Contains(module))
                {
                    throw new InvalidOperationException($"顶栏缺少模块按钮：{module}");
                }
            }

            var clock = ButtonByName(bar, "时间日期模块");
            if (!TextOf(clock).Contains("月"))
            {
                throw new InvalidOperationException("时钟模块应显示日期");
            }
            var todo = ButtonByName(bar, "待办模块");
            if (TextOf(todo) != "待办 2")
            {
                throw new InvalidOperationException($"待办概要应显示未完成计数，实际：{TextOf(todo)}");
            }
            var weather = ButtonByName(bar, "天气模块");
            if (!TextOf(weather).StartsWith("北京") || !TextOf(weather).Contains("18°"))
            {
                throw new InvalidOperationException($"天气模块应显示城市与温度，实际：{TextOf(weather)}");
            }
            if (weather.ToolTip is not ToolTip { Content: TextBlock detail }
                || !TextOfTextBlock(detail).Contains("湿度"))
            {
                throw new InvalidOperationException("天气模块悬停应提供高低温与湿度详情");
            }
            // System facts arrive from a background read; wait for them to land, then
            // hold them to the real thing: a dash or fallback label on this machine
            // means the underlying API call is dead, not that the value is unknown.
            var batteryButton = ButtonByName(bar, "电量模块");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (string.IsNullOrWhiteSpace(TextOf(batteryButton)) && DateTime.UtcNow < deadline)
            {
                pump();
            }
            if (string.IsNullOrWhiteSpace(TextOf(batteryButton)))
            {
                throw new InvalidOperationException("系统状态模块应在显示后数秒内完成首次渲染");
            }
            if (TextOf(ButtonByName(bar, "音量模块")) == "音量 —")
            {
                throw new InvalidOperationException("音量模块应读到真实音量而非占位符（COM 路径失效）");
            }
            if (TextOf(ButtonByName(bar, "输入法模块")) == "键盘")
            {
                throw new InvalidOperationException("输入法模块应读到真实布局名而非回退文案（注册表路径失效）");
            }
        }
        finally
        {
            bar.Close();
            pump();
        }
    }

    private static void ModuleSwitchesRemoveButtons(Action<Window> show, Action pump)
    {
        var state = BarState();
        state.VisibilityMode = TopBarVisibilityMode.SmartHide;
        state.Modules.Volume = false;
        state.Modules.Network = false;
        using var sampler = new WindowsPerformanceSamplingService();
        var bar = new TopBarWindow(state, new DesktopStyleState(), new FakeEnvironment(), sampler);
        show(bar);
        try
        {
            var names = AllButtons(bar).Select(AutomationProperties.GetName).ToHashSet();
            if (names.Contains("音量模块") || names.Contains("网络模块"))
            {
                throw new InvalidOperationException("关闭的模块不应出现在顶栏");
            }
            if (!names.Contains("时间日期模块"))
            {
                throw new InvalidOperationException("未关闭的模块应保持显示");
            }
        }
        finally
        {
            bar.Close();
            pump();
        }
    }

    private static void ClicksOpenSettings(Action<Window> show, Action pump)
    {
        var state = BarState();
        state.VisibilityMode = TopBarVisibilityMode.SmartHide;
        using var sampler = new WindowsPerformanceSamplingService();
        var environment = new FakeEnvironment();
        var bar = new TopBarWindow(state, new DesktopStyleState(), environment, sampler);
        show(bar);
        try
        {
            Invoke(ButtonByName(bar, "时间日期模块"));
            if (environment.ComponentRequested != DesktopComponentKind.TimeDate)
            {
                throw new InvalidOperationException("点击时钟模块应深链到时间日期组件设置");
            }
            Invoke(ButtonByName(bar, "音量模块"));
            if (environment.TopBarPageRequested != 1)
            {
                throw new InvalidOperationException("点击系统状态模块应打开顶端信息条设置页");
            }
        }
        finally
        {
            bar.Close();
            pump();
        }
    }

    private static TopBarState BarState() => new() { IsEnabled = true };

    private static IReadOnlyList<Button> AllButtons(DependencyObject root)
    {
        var buttons = new List<Button>();
        Collect(root, buttons);
        return buttons;

        static void Collect(DependencyObject node, List<Button> into)
        {
            if (node is Button button)
            {
                into.Add(button);
            }
            var children = VisualTreeHelper.GetChildrenCount(node);
            for (var index = 0; index < children; index++)
            {
                Collect(VisualTreeHelper.GetChild(node, index), into);
            }
        }
    }

    private static Button ButtonByName(DependencyObject root, string name) =>
        AllButtons(root).FirstOrDefault(button =>
            AutomationProperties.GetName(button) == name)
        ?? throw new InvalidOperationException($"找不到顶栏模块：{name}");

    private static string TextOf(Button button) => TextOfTextBlock(button.Content as TextBlock);

    private static string TextOfTextBlock(TextBlock? text) => text?.Text ?? string.Empty;

    private static void Invoke(Button button)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(button)
            ?? throw new InvalidOperationException("模块按钮没有自动化对等项");
        if (peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider)
        {
            throw new InvalidOperationException("模块按钮应支持 Invoke 模式");
        }
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private sealed class FakeEnvironment : ITopBarEnvironment
    {
        internal int Todos { get; set; }
        internal WeatherSnapshot? Weather { get; set; }
        internal DesktopComponentKind? ComponentRequested { get; private set; }
        internal int TopBarPageRequested { get; private set; }

        public bool Use24HourClock => true;

        public WeatherSnapshot? ReadWeather() => Weather;

        public void RefreshWeatherIfDue()
        {
        }

        public int CountIncompleteTodos() => Todos;

        public void OpenTopBarSettings() => TopBarPageRequested++;

        public void OpenComponentSettings(DesktopComponentKind kind) => ComponentRequested = kind;

        public bool ActivateWindow(nint handle) => false;

        public void ExitApp()
        {
        }
    }
}
