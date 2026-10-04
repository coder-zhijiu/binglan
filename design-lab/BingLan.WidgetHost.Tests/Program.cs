using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BingLan.WidgetHost.Sample.Models;
using BingLan.WidgetHost.Sample.Windows;
using BingLan.WidgetHost.Wpf;

internal static class Program
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int HtCaption = 2;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const long WsThickFrame = 0x00040000L;
    private const long WsExAcceptFiles = 0x00000010L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExAppWindow = 0x00040000L;
    private const long WsExLayered = 0x00080000L;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExTopmost = 0x00000008L;
    private const long WsExTransparent = 0x00000020L;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmWindowCornerDoNotRound = 1;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll")]
    private static extern nint SendMessage(
        nint window,
        int message,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetWindowRgn(nint window, nint region);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRectRgn(
        int left,
        int top,
        int right,
        int bottom);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PtInRegion(nint region, int x, int y);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint window,
        int attribute,
        out int value,
        int valueSize);

    [STAThread]
    private static int Main()
    {
        var failures = new List<string>();
        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/BingLan.WidgetHost.Wpf;component/Themes/IceBlue.xaml")
        });

        Run("普通 HWND 与材质契约", TestNativeWindowContract, failures);
        Run("Layered Window 防误用", TestLayeredWindowGuard, failures);
        Run("单一圆角裁剪与运行时调节", TestUnifiedCornerClipping, failures);
        Run("空白、控件、八方向与锁定命中", TestHitTesting, failures);
        Run("纯色降级与材质恢复", TestBackdropFallback, failures);
        Run("待办和文件盒共同复用宿主", TestTwoSampleCallers, failures);

        application.Shutdown();
        if (failures.Count > 0)
        {
            Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
            return 1;
        }

        Console.WriteLine("全部 WidgetHost 烟雾测试通过。");
        return 0;
    }

    private static void TestNativeWindowContract()
    {
        var window = CreateProbeWindow(out _, out _);
        ShowAndPump(window);
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            var style = GetWindowLongPtr(handle, GwlStyle).ToInt64();
            var exStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();

            Assert(!window.AllowsTransparency, "窗口不应使用 WPF Layered Window");
            Assert(window.WindowStyle == WindowStyle.None, "窗口应无系统标题栏");
            Assert(!window.ShowInTaskbar, "窗口不应显示任务栏按钮");
            Assert(!window.ShowActivated, "组件首次显示不应抢占当前输入焦点");
            Assert(!window.Topmost, "窗口默认不能置顶");
            Assert(window.NativeStyleError is null, $"原生窗口样式应用失败：{window.NativeStyleError}");
            Assert((style & WsThickFrame) != 0, "窗口缺少系统缩放框架");
            Assert((exStyle & WsExToolWindow) != 0, "窗口缺少 WS_EX_TOOLWINDOW");
            Assert((exStyle & WsExAcceptFiles) != 0, "窗口缺少 WS_EX_ACCEPTFILES");
            Assert((exStyle & WsExAppWindow) == 0, "窗口不应使用 WS_EX_APPWINDOW");
            Assert((exStyle & WsExLayered) == 0, "窗口错误地使用了 WS_EX_LAYERED");
            Assert((exStyle & WsExTransparent) == 0, "窗口不应全局点击穿透");
            Assert((exStyle & WsExNoActivate) == 0, "窗口必须能获得编辑焦点");
            Assert((exStyle & WsExTopmost) == 0, "窗口不应使用 WS_EX_TOPMOST");
            Assert(window.NativeShapeError is null, $"原生圆角应用失败：{window.NativeShapeError}");
            Assert(
                GetDwmAttribute(handle, DwmwaWindowCornerPreference) == DwmWindowCornerDoNotRound,
                "窗口未关闭 DWM 第二层系统圆角");

            if (window.BackdropResult.Mode == WidgetBackdropMode.SystemBackdrop)
            {
                Assert(
                    GetDwmAttribute(handle, DwmwaSystemBackdropType) == 3,
                    "控制器报告系统材质，但 HWND 未设置 transient backdrop");
            }
            else
            {
                Assert(
                    window.Background is SolidColorBrush { Color.A: byte.MaxValue },
                    "材质不可用时必须回退到不透明可读背景");
            }
        }
        finally
        {
            Close(window);
        }
    }

    private static void TestUnifiedCornerClipping()
    {
        var window = CreateProbeWindow(out _, out _);
        ShowAndPump(window);
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            Assert(Math.Abs(window.WidgetCornerRadius - 15.5d) < 0.01d, "默认圆角半径不正确");
            Assert(
                window.SurfaceCornerRadius == new CornerRadius(15.5d),
                "WPF 描边没有复用宿主默认圆角");
            AssertRoundedRegion(handle, "默认圆角");

            window.WidgetCornerRadius = 3d;
            Pump();
            Assert(
                window.SurfaceCornerRadius == new CornerRadius(3d),
                "运行时圆角没有同步到 WPF 描边");
            AssertRoundedRegion(handle, "3 DIP 圆角");

            window.WidgetCornerRadius = 100d;
            Pump();
            Assert(Math.Abs(window.WidgetCornerRadius - 32d) < 0.01d, "圆角没有限制在安全范围");

            window.WidgetCornerRadius = 0d;
            Pump();
            AssertWindowRegionCleared(handle);
            Assert(
                window.SurfaceCornerRadius == new CornerRadius(0d),
                "直角模式仍保留 WPF 圆角");

            foreach (var preference in new[]
                     {
                         WidgetBackdropPreference.PoggetLike,
                         WidgetBackdropPreference.Auto,
                         WidgetBackdropPreference.Solid
                     })
            {
                window.BackdropPreference = preference;
                foreach (var radius in new[] { 0d, 3d, 15.5d, 32d })
                {
                    window.WidgetCornerRadius = radius;
                    Pump();
                    Assert(window.NativeShapeError is null, $"{preference}/{radius} DIP 圆角应用失败");
                    Assert(
                        GetDwmAttribute(handle, DwmwaWindowCornerPreference) == DwmWindowCornerDoNotRound,
                        $"{preference}/{radius} DIP 恢复了第二层 DWM 圆角");
                    if (radius == 0d)
                    {
                        AssertWindowRegionCleared(handle);
                    }
                    else
                    {
                        AssertRoundedRegion(handle, $"{preference}/{radius} DIP");
                    }
                }
            }

            window.Width += 40d;
            Pump();
            AssertRoundedRegion(handle, "窗口缩放后的圆角");
        }
        finally
        {
            Close(window);
        }
    }

    private static void TestLayeredWindowGuard()
    {
        var window = new WidgetWindow
        {
            AllowsTransparency = true,
            WindowStyle = WindowStyle.None,
            Width = 220,
            Height = 160
        };
        try
        {
            var exception = AssertThrows<InvalidOperationException>(window.Show);
            Assert(
                exception.Message.Contains("AllowsTransparency=True", StringComparison.Ordinal),
                "Layered Window 防误用错误信息不明确");
        }
        finally
        {
            if (window.IsVisible)
            {
                Close(window);
            }
        }
    }

    private static void TestHitTesting()
    {
        var window = CreateProbeWindow(out var button, out var markedRegion);
        ShowAndPump(window);
        try
        {
            var width = window.ActualWidth;
            var height = window.ActualHeight;
            AssertHit(window, new Point(24, 24), HtCaption, "空白区");
            AssertHit(
                window,
                button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2), window),
                HtClient,
                "按钮");
            AssertHit(
                window,
                markedRegion.TranslatePoint(
                    new Point(markedRegion.ActualWidth / 2, markedRegion.ActualHeight / 2),
                    window),
                HtClient,
                "附加属性交互区");

            AssertHit(window, new Point(2, height / 2), HtLeft, "左边缘");
            AssertHit(window, new Point(width - 2, height / 2), HtRight, "右边缘");
            AssertHit(window, new Point(width / 2, 2), HtTop, "上边缘");
            AssertHit(window, new Point(width / 2, height - 2), HtBottom, "下边缘");
            var cornerOffset = RoundedCornerOffset(window.WidgetCornerRadius);
            AssertHit(window, new Point(cornerOffset, cornerOffset), HtTopLeft, "左上圆弧");
            AssertHit(window, new Point(width - cornerOffset, cornerOffset), HtTopRight, "右上圆弧");
            AssertHit(window, new Point(cornerOffset, height - cornerOffset), HtBottomLeft, "左下圆弧");
            AssertHit(
                window,
                new Point(width - cornerOffset, height - cornerOffset),
                HtBottomRight,
                "右下圆弧");

            window.WidgetCornerRadius = 32d;
            Pump();
            var largeCornerOffset = RoundedCornerOffset(window.WidgetCornerRadius);
            AssertHit(
                window,
                new Point(largeCornerOffset, largeCornerOffset),
                HtTopLeft,
                "大圆角可达缩放带");

            window.IsLayoutLocked = true;
            AssertHit(window, new Point(24, 24), HtClient, "锁定空白区");
            AssertHit(window, new Point(width - 2, height / 2), HtClient, "锁定边缘");
        }
        finally
        {
            Close(window);
        }
    }

    private static void TestBackdropFallback()
    {
        var window = CreateProbeWindow(out _, out _);
        ShowAndPump(window);
        try
        {
            window.BackdropPreference = WidgetBackdropPreference.Solid;
            Pump();
            Assert(
                window.BackdropResult.Mode == WidgetBackdropMode.SolidFallback,
                "显式纯色模式没有进入降级材质");
            Assert(
                window.Background is SolidColorBrush { Color.A: byte.MaxValue },
                "纯色降级背景必须不透明");

            window.SolidFallbackColor = Color.FromArgb(1, 12, 34, 56);
            Pump();
            Assert(window.SolidFallbackColor == Color.FromRgb(12, 34, 56), "纯色回退没有强制不透明");
            Assert(
                window.Background is SolidColorBrush { Color: var color }
                && color == Color.FromRgb(12, 34, 56),
                "运行中修改纯色回退没有立即刷新窗口");

            window.BackdropPreference = WidgetBackdropPreference.Auto;
            Pump();
            var first = window.BackdropResult.Mode;
            window.RefreshBackdrop();
            Pump();
            Assert(window.BackdropResult.Mode == first, "重复应用材质应保持幂等");
        }
        finally
        {
            Close(window);
        }
    }

    private static void TestTwoSampleCallers()
    {
        var fileBox = new FileBoxDemoWindow();
        var todo = new TodoDemoWindow();
        ShowAndPump(fileBox);
        ShowAndPump(todo);
        try
        {
            Assert(!fileBox.AllowsTransparency && !todo.AllowsTransparency, "示例窗口退回了 Layered Window");
            var source = HwndSource.FromHwnd(new WindowInteropHelper(fileBox).Handle);
            Console.WriteLine(
                $"样本材质：{fileBox.BackdropResult.Mode}（{fileBox.BackdropResult.Detail}）；" +
                $"Window.Background={DescribeBrush(fileBox.Background)}；" +
                $"CompositionTarget={source?.CompositionTarget.BackgroundColor}");
            var surface = fileBox.FindName("Surface") as Border
                ?? throw new InvalidOperationException("找不到文件盒表面");
            Assert(
                Math.Abs(surface.ActualWidth - fileBox.ActualWidth) < 0.5 &&
                Math.Abs(surface.ActualHeight - fileBox.ActualHeight) < 0.5,
                "非客户区挤压了组件内容");
            Assert(surface.Background == Brushes.Transparent, "外框描边不应再绘制第二层背景");
            Assert(fileBox.FindName("FileList") is ItemsControl { Items.Count: 11 }, "文件盒样本未完整显示");
            var list = todo.FindName("TodoList") as ItemsControl
                ?? throw new InvalidOperationException("找不到待办列表");
            Assert(list.Items.Count == 3, "待办默认数据不正确");

            var fileMoreButton = fileBox.FindName("MoreButton") as Button
                ?? throw new InvalidOperationException("找不到文件盒设置按钮");
            fileMoreButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump();
            var radiusSlider = fileBox.FindName("CornerRadiusSlider") as Slider
                ?? throw new InvalidOperationException("找不到圆角调节滑块");
            radiusSlider.Value = 3d;
            Pump();
            Assert(Math.Abs(fileBox.WidgetCornerRadius - 3d) < 0.01d, "圆角滑块没有更新宿主");
            Assert(surface.CornerRadius == new CornerRadius(3d), "圆角滑块没有更新可见描边");
            fileMoreButton.ContextMenu!.IsOpen = false;

            var addButton = FindVisualChildren<Button>(todo)
                .Single(button =>
                    button.Content is TextBlock { Text: "\uE710" });
            todo.Activate();
            Pump();
            addButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump();
            Assert(list.Items.Count == 4, "待办新增按钮没有工作");

            var todoEditors = FindVisualChildren<TextBox>(todo)
                .Where(editor => editor.DataContext is TodoItem)
                .ToList();
            var firstEditor = todoEditors.First();
            firstEditor.Text = "验证点击编辑";
            Pump();
            Assert(((TodoItem)list.Items[0]).Text == "验证点击编辑", "待办文字编辑没有回写模型");
            Assert(todoEditors.Last().IsKeyboardFocused, "新增待办没有立即聚焦文本输入");

            var firstCheckBox = FindVisualChildren<CheckBox>(todo).First();
            firstCheckBox.IsChecked = true;
            Pump();
            Assert(
                todo.FindName("SummaryText") is TextBlock { Text: "1/4 已完成" },
                "待办勾选没有更新完成状态");
            Assert(((TodoItem)list.Items[0]).IsCompleted, "待办勾选没有回写模型");
            Assert(firstEditor.TextDecorations?.Count > 0, "已完成待办没有显示划线");

            var hideCompleted = todo.FindName("HideCompletedMenuItem") as MenuItem
                ?? throw new InvalidOperationException("找不到隐藏已完成菜单");
            hideCompleted.IsChecked = true;
            hideCompleted.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump();
            Assert(list.Items.Count == 3, "隐藏已完成没有过滤列表");
            hideCompleted.IsChecked = false;
            hideCompleted.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump();
            Assert(list.Items.Count == 4, "恢复显示已完成没有恢复列表");

            var moreButton = todo.FindName("MoreButton") as Button
                ?? throw new InvalidOperationException("找不到待办设置按钮");
            moreButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump();
            Assert(moreButton.ContextMenu?.IsOpen == true, "待办设置菜单没有打开");
            moreButton.ContextMenu!.IsOpen = false;

            var lockItem = todo.FindName("LockMenuItem") as MenuItem
                ?? throw new InvalidOperationException("找不到锁定菜单");
            lockItem.IsChecked = true;
            lockItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump();
            Assert(todo.IsLayoutLocked, "锁定菜单没有同步宿主状态");
            AssertHit(todo, new Point(16, 64), HtClient, "真实待办锁定空白区");
            lockItem.IsChecked = false;
            lockItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump();
            Assert(!todo.IsLayoutLocked, "解锁菜单没有同步宿主状态");
            AssertHit(todo, new Point(16, 64), HtCaption, "真实待办解锁空白区");
        }
        finally
        {
            Close(todo);
            Close(fileBox);
        }
    }

    private static WidgetWindow CreateProbeWindow(
        out Button button,
        out Border markedRegion)
    {
        var workArea = SystemParameters.WorkArea;
        var window = new WidgetWindow
        {
            Width = 300,
            Height = 240,
            MinWidth = 180,
            MinHeight = 140,
            Left = workArea.Left + 40,
            Top = workArea.Top + 40
        };
        var root = new Grid
        {
            Background = Brushes.Transparent
        };
        button = new Button
        {
            Width = 100,
            Height = 40,
            Content = "交互按钮"
        };
        markedRegion = new Border
        {
            Width = 100,
            Height = 32,
            Background = Brushes.LightBlue,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 24)
        };
        WidgetHitTest.SetIsInteractive(markedRegion, true);
        root.Children.Add(button);
        root.Children.Add(markedRegion);
        window.Content = root;
        return window;
    }

    private static int GetDwmAttribute(nint handle, int attribute)
    {
        var result = DwmGetWindowAttribute(
            handle,
            attribute,
            out var value,
            Marshal.SizeOf<int>());
        if (result < 0)
        {
            throw new InvalidOperationException(
                $"DwmGetWindowAttribute({attribute}) 失败：0x{result:X8}");
        }
        return value;
    }

    private static void AssertRoundedRegion(nint handle, string name)
    {
        var region = CreateRectRgn(0, 0, 1, 1);
        if (region == 0)
        {
            throw new InvalidOperationException($"{name}测试区域创建失败");
        }

        try
        {
            Assert(GetWindowRgn(handle, region) != 0, $"{name}没有应用 HWND 裁剪区域");
            Assert(!PtInRegion(region, 0, 0), $"{name}仍显示矩形左上角像素");
            Assert(PtInRegion(region, 100, 100), $"{name}错误裁掉窗口主体");
        }
        finally
        {
            _ = DeleteObject(region);
        }
    }

    private static void AssertWindowRegionCleared(nint handle)
    {
        var region = CreateRectRgn(0, 0, 1, 1);
        if (region == 0)
        {
            throw new InvalidOperationException("直角模式测试区域创建失败");
        }

        try
        {
            Assert(GetWindowRgn(handle, region) == 0, "直角模式没有移除 HWND 圆角区域");
        }
        finally
        {
            _ = DeleteObject(region);
        }
    }

    private static void AssertHit(
        Window window,
        Point clientPoint,
        int expected,
        string name)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var screenPoint = window.PointToScreen(clientPoint);
        var actual = SendMessage(handle, WmNcHitTest, 0, Pack(screenPoint)).ToInt32();
        Assert(actual == expected, $"{name} 应为 {expected}，实际为 {actual}");
    }

    private static nint Pack(Point point)
    {
        var x = (int)Math.Round(point.X);
        var y = (int)Math.Round(point.Y);
        return (nint)((y << 16) | (x & 0xFFFF));
    }

    private static double RoundedCornerOffset(double radius) =>
        radius - (radius - 1d) / Math.Sqrt(2d);

    private static void ShowAndPump(Window window)
    {
        window.Show();
        Pump();
        Assert(
            window.IsVisible && new WindowInteropHelper(window).Handle != 0,
            "窗口未真实创建");
    }

    private static void Close(Window window)
    {
        window.Close();
        Pump();
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new DispatcherOperationCallback(value =>
            {
                ((DispatcherFrame)value!).Continue = false;
                return null;
            }),
            frame);
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T result)
            {
                yield return result;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void Run(string name, Action test, List<string> failures)
    {
        try
        {
            test();
            Console.WriteLine($"通过：{name}");
        }
        catch (Exception exception)
        {
            failures.Add($"失败：{name} - {exception}");
        }
    }

    private static string DescribeBrush(Brush brush) =>
        brush is SolidColorBrush solid ? solid.Color.ToString() : brush.ToString();

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static TException AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"预期抛出 {typeof(TException).Name}");
    }
}
