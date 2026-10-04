using BingLan.DockSpike.Interop;
using BingLan.DockSpike.Layout;
using BingLan.DockSpike.Windowing;

var failures = new List<string>();
Run("Dock 四边几何与负坐标", TestDockGeometry);
Run("Dock 非法厚度", TestInvalidThickness);
Run("断开的显示器位置恢复", TestPlacementRecovery);
Run("窗口身份优先级", TestIdentityPriority);
Run("同应用多窗口稳定分组", TestStableGrouping);
Run("空标题窗口显示名回退", TestUntitledWindowDisplayName);
Run("Dock 点击动作决策", TestWindowActionPolicy);
Run("Dock 内容尺寸边界", TestContentLengthBounds);
Run("底部与顶部水平居中几何", TestHorizontalCentering);
Run("左右垂直几何", TestVerticalGeometry);
Run("固定/运行/活动/分组状态映射", TestDockItemStateMapping);
Run("AppBar 预留、释放与重建路径", TestAppBarReservation);

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("全部 Dock 纯逻辑烟雾测试通过。");
return 0;

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"通过：{name}");
    }
    catch (Exception exception)
    {
        failures.Add($"失败：{name} - {exception.Message}");
    }
}

static void TestDockGeometry()
{
    var monitor = new PixelRect(-1920, -100, 0, 980);

    AssertEqual(
        new PixelRect(-1920, -100, -1872, 980),
        DockGeometry.Calculate(monitor, DockEdge.Left, 48),
        "左侧 Dock 几何错误");
    AssertEqual(
        new PixelRect(-1920, -100, 0, -52),
        DockGeometry.Calculate(monitor, DockEdge.Top, 48),
        "顶部 Dock 几何错误");
    AssertEqual(
        new PixelRect(-48, -100, 0, 980),
        DockGeometry.Calculate(monitor, DockEdge.Right, 48),
        "右侧 Dock 几何错误");
    AssertEqual(
        new PixelRect(-1920, 932, 0, 980),
        DockGeometry.Calculate(monitor, DockEdge.Bottom, 48),
        "底部 Dock 几何错误");

    Assert(monitor.HasArea, "有效显示器矩形应有面积");
    Assert(monitor.Intersects(new PixelRect(-100, 0, 100, 100)), "负坐标矩形应正确相交");
    Assert(!monitor.Intersects(new PixelRect(0, 0, 100, 100)), "仅边缘接触不应视为相交");
}

static void TestInvalidThickness()
{
    var monitor = new PixelRect(0, 0, 1920, 1080);
    AssertThrows<ArgumentOutOfRangeException>(
        () => DockGeometry.Calculate(monitor, DockEdge.Bottom, 0),
        "厚度 0 应被拒绝");
    AssertThrows<ArgumentOutOfRangeException>(
        () => DockGeometry.Calculate(monitor, DockEdge.Left, 1921),
        "垂直 Dock 厚度超过显示器宽度应被拒绝");
    AssertThrows<ArgumentOutOfRangeException>(
        () => DockGeometry.Calculate(monitor, DockEdge.Top, 1081),
        "水平 Dock 厚度超过显示器高度应被拒绝");
}

static void TestPlacementRecovery()
{
    var primary = new PixelRect(0, 0, 1920, 1040);
    var disconnected = new PixelRect(-1600, 120, -1200, 420);
    var recovered = PlacementResolver.EnsureVisible(disconnected, [primary], primary);
    AssertEqual(
        new PixelRect(0, 120, 400, 420),
        recovered,
        "断屏后的窗口应夹取到最近可用工作区");

    var stillVisible = new PixelRect(-30, 80, 170, 280);
    AssertEqual(
        stillVisible,
        PlacementResolver.EnsureVisible(stillVisible, [primary], primary),
        "达到最小可见宽高时不应改变原位置");

    var fallbackOnly = PlacementResolver.EnsureVisible(
        new PixelRect(3000, 2000, 3300, 2300),
        [],
        primary);
    AssertEqual(
        new PixelRect(1620, 740, 1920, 1040),
        fallbackOnly,
        "没有工作区快照时应使用回退工作区");
}

static void TestIdentityPriority()
{
    var allIdentities = Window(
        1,
        10,
        "浏览器",
        "Microsoft.Edge.Stable",
        @"C:\Program Files\Edge\msedge.exe");
    AssertEqual(
        "aumid:Microsoft.Edge.Stable",
        WindowGrouping.IdentityKey(allIdentities),
        "AUMID 应优先于路径和 PID");

    var byPath = Window(2, 20, "记事本", null, @"C:/Windows/System32/notepad.exe");
    AssertEqual(
        $"exe:{Path.GetFullPath(@"C:\Windows\System32\notepad.exe")}",
        WindowGrouping.IdentityKey(byPath),
        "可执行文件路径应被规范化");

    var byPid = Window(3, 30, "未知窗口", null, null);
    AssertEqual("pid:30", WindowGrouping.IdentityKey(byPid), "缺少应用身份时应回退 PID");
}

static void TestStableGrouping()
{
    var firstBrowser = Window(10, 100, "第一页", null, @"C:\Apps\Browser.exe");
    var editor = Window(20, 200, "文档", null, @"C:\Apps\Editor.exe");
    var secondBrowser = Window(11, 101, "第二页", null, @"c:\apps\BROWSER.EXE");
    var firstPackaged = Window(30, 300, "设置一", "Windows.Settings", null);
    var secondPackaged = Window(31, 301, "设置二", "windows.settings", null);
    var unknown = Window(40, 400, "工具窗口", null, null);

    var groups = WindowGrouping.Group(
        [firstBrowser, editor, secondBrowser, firstPackaged, secondPackaged, unknown]);

    AssertEqual(4, groups.Count, "应按应用身份合并为四组");
    AssertEqual("Browser", groups[0].DisplayName, "首组显示名应来自可执行文件名");
    AssertEqual(2, groups[0].Windows.Count, "同一路径的两个窗口应合并");
    AssertEqual(firstBrowser.Handle, groups[0].Windows[0].Handle, "组内窗口顺序应保持输入顺序");
    AssertEqual(secondBrowser.Handle, groups[0].Windows[1].Handle, "组内窗口顺序应保持输入顺序");
    AssertEqual("Editor", groups[1].DisplayName, "分组顺序应按首次出现保持稳定");
    AssertEqual("设置一", groups[2].DisplayName, "无路径的分组显示名应回退到标题");
    AssertEqual("工具窗口", groups[3].DisplayName, "PID 分组显示名应优先使用标题");
}

static void TestWindowActionPolicy()
{
    AssertEqual(
        DockClickAction.MinimizeForeground,
        WindowActionPolicy.Decide(groupContainsForeground: true, targetIsMinimized: false),
        "点击当前前台应用应最小化");
    AssertEqual(
        DockClickAction.Activate,
        WindowActionPolicy.Decide(groupContainsForeground: false, targetIsMinimized: false),
        "点击后台可见应用应激活");
    AssertEqual(
        DockClickAction.RestoreAndActivate,
        WindowActionPolicy.Decide(groupContainsForeground: false, targetIsMinimized: true),
        "点击最小化应用应恢复并激活");
}

static void TestUntitledWindowDisplayName()
{
    var packaged = Window(50, 500, string.Empty, "Package.Sample!App", null);
    var unknown = Window(51, 501, string.Empty, null, null);
    var groups = WindowGrouping.Group([packaged, unknown]);

    AssertEqual("Package.Sample!App", groups[0].DisplayName, "空标题打包应用应回退 AUMID");
    AssertEqual("PID 501", groups[1].DisplayName, "完全缺少身份时应回退 PID");
}

static void TestContentLengthBounds()
{
    AssertEqual(
        DockLayoutMetrics.EmptyLengthDip,
        DockLayoutMetrics.ContentLengthForItems(0),
        "0 个项目应使用空 Dock 最小长度");
    AssertEqual(
        DockLayoutMetrics.EmptyLengthDip,
        DockLayoutMetrics.ContentLengthForItems(-3),
        "负项目数应按 0 个项目处理");
    AssertEqual(
        (2 * DockLayoutMetrics.AxisPaddingDip) + (3 * DockLayoutMetrics.ItemCellDip),
        DockLayoutMetrics.ContentLengthForItems(3),
        "少量项目应按单元格线性增长");
    Assert(
        DockLayoutMetrics.ContentLengthForItems(12) > DockLayoutMetrics.ContentLengthForItems(3),
        "项目越多内容长度应越大");

    var capped = FloatingDockGeometry.ClampExtent(
        (int)DockLayoutMetrics.ContentLengthForItems(500),
        1920,
        DockLayoutMetrics.MaxExtentFraction,
        (int)DockLayoutMetrics.EmptyLengthDip);
    AssertEqual(
        (int)Math.Floor(1920 * DockLayoutMetrics.MaxExtentFraction),
        capped,
        "大量项目应被工作区上限截断");

    var belowMinimum = FloatingDockGeometry.ClampExtent(
        10,
        1920,
        DockLayoutMetrics.MaxExtentFraction,
        (int)DockLayoutMetrics.EmptyLengthDip);
    AssertEqual(
        (int)DockLayoutMetrics.EmptyLengthDip,
        belowMinimum,
        "小于最小长度应回到空 Dock 长度");

    var unclamped = FloatingDockGeometry.ClampExtent(
        200,
        1920,
        DockLayoutMetrics.MaxExtentFraction,
        (int)DockLayoutMetrics.EmptyLengthDip);
    AssertEqual(200, unclamped, "上限内的内容长度应保持不变");
}

static void TestHorizontalCentering()
{
    var monitor = new PixelRect(0, 0, 1920, 1080);
    var bottomStrip = DockGeometry.Calculate(monitor, DockEdge.Bottom, 66);
    var bottom = FloatingDockGeometry.Calculate(bottomStrip, DockEdge.Bottom, 164, 56, 10);
    AssertEqual(
        new PixelRect(878, 1014, 1042, 1070),
        bottom,
        "底部 Dock 应水平居中并与屏幕边缘保持间距");

    var topStrip = DockGeometry.Calculate(monitor, DockEdge.Top, 66);
    var top = FloatingDockGeometry.Calculate(topStrip, DockEdge.Top, 164, 56, 10);
    AssertEqual(
        new PixelRect(878, 10, 1042, 66),
        top,
        "顶部 Dock 应水平居中并与屏幕边缘保持间距");

    var negativeMonitor = new PixelRect(-1920, -100, 0, 980);
    var negativeStrip = DockGeometry.Calculate(negativeMonitor, DockEdge.Bottom, 66);
    var negativeDock = FloatingDockGeometry.Calculate(negativeStrip, DockEdge.Bottom, 164, 56, 10);
    AssertEqual(
        new PixelRect(-1042, 914, -878, 970),
        negativeDock,
        "负坐标显示器上的底部 Dock 也应水平居中");
}

static void TestVerticalGeometry()
{
    var monitor = new PixelRect(0, 0, 1920, 1080);
    var leftStrip = DockGeometry.Calculate(monitor, DockEdge.Left, 66);
    var left = FloatingDockGeometry.Calculate(leftStrip, DockEdge.Left, 164, 56, 10);
    AssertEqual(
        new PixelRect(10, 458, 66, 622),
        left,
        "左侧 Dock 应垂直居中并与屏幕边缘保持间距");

    var rightStrip = DockGeometry.Calculate(monitor, DockEdge.Right, 66);
    var right = FloatingDockGeometry.Calculate(rightStrip, DockEdge.Right, 164, 56, 10);
    AssertEqual(
        new PixelRect(1854, 458, 1910, 622),
        right,
        "右侧 Dock 应垂直居中并与屏幕边缘保持间距");

    var clamped = FloatingDockGeometry.Calculate(leftStrip, DockEdge.Left, 5000, 56, 10);
    AssertEqual(
        new PixelRect(10, 0, 66, 1080),
        clamped,
        "内容长度超过工作区高度时应截断到条带范围");
}

static void TestDockItemStateMapping()
{
    var pinnedExplorer = new PinnedApp(@"C:\Windows\explorer.exe");
    var pinnedEditor = new PinnedApp(@"C:\Apps\Editor.exe");
    var explorerWindow = Window(
        100,
        900,
        "文件夹",
        null,
        @"c:\windows\EXPLORER.EXE",
        isForeground: true);
    var firstBrowser = Window(200, 800, "第一页", null, @"C:\Apps\Browser.exe");
    var secondBrowser = Window(201, 801, "第二页", null, @"C:\Apps\Browser.exe");
    var groups = WindowGrouping.Group([explorerWindow, firstBrowser, secondBrowser]);

    var items = DockItemComposer.Compose([pinnedExplorer, pinnedEditor], groups);

    AssertEqual(3, items.Count, "固定项与未固定运行项应合并为一个列表");
    Assert(items[0].IsPinned && items[0].IsRunning, "固定的运行中应用应保持固定身份并合并窗口");
    AssertEqual(DockItemState.Active, items[0].State, "包含前台窗口的组应映射为活动状态");
    AssertEqual(DockItemState.Pinned, items[1].State, "未运行的固定应用应映射为仅固定状态");
    Assert(!items[1].IsRunning && items[1].WindowCount == 0, "未运行的固定应用不应携带窗口");
    AssertEqual(DockItemState.Running, items[2].State, "未固定的后台运行应用应映射为运行状态");
    Assert(items[2].HasMultipleWindows, "同应用多窗口应保留分组标记");
    AssertEqual(2, items[2].WindowCount, "分组窗口计数应与快照一致");
    Assert(!items[2].IsPinned, "未固定的运行应用不应误标为固定");

    var unpinnedOnly = DockItemComposer.Compose([], groups);
    AssertEqual(2, unpinnedOnly.Count, "没有固定项时只列出运行分组");
    Assert(!unpinnedOnly[0].IsPinned, "纯运行项不应有固定身份");
}

static void TestAppBarReservation()
{
    var reservation = new AppBarReservationState();
    AssertEqual(
        AppBarMessage.Register,
        Single(reservation.Apply(true)),
        "首次预留应注册 AppBar");
    AssertEqual(0, reservation.Apply(true).Count, "重复预留不应重复注册");
    AssertEqual(
        AppBarMessage.Unregister,
        Single(reservation.Apply(false)),
        "关闭预留应发送释放");
    AssertEqual(0, reservation.Release().Count, "未注册时退出不应重复释放");

    reservation.Apply(true);
    AssertEqual(
        AppBarMessage.Unregister,
        Single(reservation.Release()),
        "退出路径必须释放已注册的工作区");
    Assert(!reservation.IsRegistered, "释放后状态应为未注册");

    reservation.Apply(true);
    reservation.ResetRegistration();
    AssertEqual(
        AppBarMessage.Register,
        Single(reservation.Apply(true)),
        "TaskbarCreated 重置后应重新注册");

    reservation.MarkRegisterFailed();
    Assert(!reservation.IsRegistered, "注册失败后状态应回退为未注册");
    AssertEqual(
        AppBarMessage.Register,
        Single(reservation.Apply(true)),
        "注册失败后下一次预留应允许重试");

    AssertEqual(
        AppBarMessage.Unregister,
        Single(reservation.Release()),
        "重试注册成功后退出仍应释放一次");

    reservation.Apply(true);
    AssertEqual(0, reservation.Apply(true).Count, "已注册状态下重复预留保持安静");
}

static TMessage Single<TMessage>(IReadOnlyList<TMessage> messages)
{
    AssertEqual(1, messages.Count, "应恰好产生一条消息");
    return messages[0];
}

static TrackedWindow Window(
    long handle,
    uint processId,
    string title,
    string? appUserModelId,
    string? executablePath,
    bool isForeground = false) =>
    new((nint)handle, processId, title, appUserModelId, executablePath, isForeground, false);

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message}。期望：{expected}；实际：{actual}");
    }
}

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}
