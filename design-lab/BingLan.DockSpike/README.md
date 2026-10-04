# BingLan Dock 技术验证

状态：阶段 0 可运行实验，不是生产 Dock，也不读取或修改 `%LOCALAPPDATA%\BingLanWidgets`。正式 Dock 已迁入 `src/BingLan.App/Dock` 与 `src/BingLan.Core/Dock`，真机结果见 `docs/DOCK-QA.md`；本实验保留为阶段 0 记录。

## 验证目标

这个实验回答五个高风险问题：

1. 能否在不替换 Windows Shell 的情况下发现、分组和跟踪可切换窗口。
2. Dock 点击能否完成“启动未运行应用、后台窗口激活、最小化窗口恢复、当前前台窗口最小化”。
3. WPF 窗口能否在四个屏幕边缘注册 AppBar，并在退出后释放工作区。
4. 多显示器、负坐标和每显示器 DPI 能否用可测试的纯几何规则处理。
5. 全宽诊断条能否替换为内容宽度、底部居中的冰蓝视觉表面，同时保持窗口逻辑可测试（`docs/DESKTOP-SHELL-AND-SURFACES.md` 第 6 节第 2 步）。

实验与正式 `BingLan.App` 隔离：独立解决方案、独立进程、无状态文件、无开机启动项，也没有加入根 `BingLan.slnx`。固定应用列表只保存在内存中，进程退出即丢失，不会写入任何用户状态。

## 当前形态

Dock 是参考 `src/BingLan.App/Assets/Presets/transparent-apple.png` 的冰蓝悬浮条：默认位于目标显示器底部居中，宽度随项目数伸缩并被限制在工作区的 65% 以内，超出后内部滚动；表面为冰蓝半透明圆角细描边，使用更接近预设图的图标留白和离屏间距，不再显示 HWND、刷新次数、AppBar 诊断或开发说明。

- 图标来自真实来源：固定/运行应用的可执行文件图标（`SHGetFileInfo`），无路径窗口回退到窗口/类图标（`WM_GETICON`、`GetClassLongPtr`），最终回退到系统通用应用图标；不使用 emoji、ASCII、手绘 SVG 或占位方块。图标提取在后台线程完成，UI 线程只做位图封装与冻结。
- 每个项目是一个图标按钮：运行中的应用在图标下方显示指示点，包含前台窗口的应用显示冰蓝高亮。
- 点击未运行的固定应用启动它；点击运行中应用聚焦；点击当前前台应用最小化。
- 右键项目：启动新实例、同应用多窗口选择、固定到 Dock / 取消固定（仅内存）。
- 右键 Dock 空白：选择显示器、四个屏幕边缘、固定并保留工作区/浮层、解除占位并退出。
- 默认固定项为系统自带的 `explorer.exe` 和 `notepad.exe`（存在时），用于验证“未运行可启动”路径。

## 技术决策

阶段 0 选择最小自有 Win32 适配层，暂不引入 ManagedShell。

ManagedShell 0.0.358 是 Apache-2.0，支持 `net10.0-windows`，窗口模型也比较完整；但它的 `TasksService.Initialize()` 会调用 `SetTaskmanWindow`、设置 Taskband HWND，并通过 `SPI_SETMINIMIZEDMETRICS` 修改 Shell 级最小化布局。它还依赖微软标记为“不适用于一般用途”的 `RegisterShellHookWindow`。这些行为更适合 Shell 替代项目，不符合冰蓝桌面“伴侣而非 Shell”的边界。

参考：

- [ManagedShell 官方仓库](https://github.com/cairoshell/ManagedShell)
- [ManagedShell TasksService](https://github.com/cairoshell/ManagedShell/blob/master/src/ManagedShell.WindowsTasks/TasksService.cs)
- [RegisterShellHookWindow](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-registershellhookwindow)

当前实验使用：

- `EnumWindows` + 可见性、扩展样式、owner 与 DWM cloaked 过滤生成窗口快照。
- 进程 AppUserModelID → 可执行路径 → PID 作为分组身份优先级。
- `SetWinEventHook` 作为失效通知，90 ms 去抖后重新生成快照；5 秒低频对账，不持续高频轮询。
- `ShowWindowAsync` + `SetForegroundWindow` 完成切换；聚焦被系统拒绝时只闪烁提醒，不强抢焦点。
- `SHAppBarMessage` 完成占用工作区模式；浮层模式不注册 AppBar。注册/释放/重建决策集中在可测试的 `AppBarReservationState`，原生消息发送由 `AppBarController` 执行。
- AppBar 模式下 Dock 保持在普通窗口之上；收到全屏通知时降到底层，离开全屏后恢复。浮层模式不强制置顶，避免未实现智能隐藏前遮挡独占内容。
- 浮动几何（内容长度、工作区上限、四边居中）集中在纯函数 `DockLayoutMetrics` 和 `FloatingDockGeometry`，视觉窗口只消费计算结果。
- `EnumDisplayMonitors` + `GetMonitorInfo` + PerMonitorV2 manifest 处理物理屏幕矩形和 DPI。

官方行为依据：

- [EnumWindows](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-enumwindows)
- [SetWinEventHook](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwineventhook)
- [SetForegroundWindow](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setforegroundwindow)
- [Application Desktop Toolbars](https://learn.microsoft.com/windows/win32/shell/application-desktop-toolbars)
- [EnumDisplayMonitors](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-enumdisplaymonitors)
- [WM_DPICHANGED](https://learn.microsoft.com/windows/win32/hidpi/wm-dpichanged)
- [SHGetFileInfo](https://learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-shgetfileinfow)
- [WM_GETICON](https://learn.microsoft.com/windows/win32/winmsg/wm-geticon)

## 运行

```powershell
dotnet run --project .\design-lab\BingLan.DockSpike\BingLan.DockSpike.csproj -c Release
```

Dock 默认位于主屏底部并注册 AppBar 保留工作区。右键 Dock 空白区域可以选择显示器、四个边缘、保留工作区/浮层和退出；右键图标可以固定/取消固定、启动新实例或选择同应用的某个窗口。

调试参数：

```text
--edge=Left|Top|Right|Bottom
--overlay       不注册 AppBar，仅贴边显示
--automation    显示任务栏按钮，便于自动化观察；正常运行不要使用
```

## 自动验证

```powershell
dotnet build .\design-lab\BingLan.DockSpike.slnx -c Release
dotnet run --project .\design-lab\BingLan.DockSpike.Tests\BingLan.DockSpike.Tests.csproj -c Release
```

纯逻辑测试覆盖：

- 四边 Dock 矩形和负坐标显示器。
- 非法厚度。
- 显示器断开后的可见位置恢复。
- AppUserModelID、路径和 PID 分组优先级。
- 同应用多窗口的稳定分组。
- 当前前台最小化、后台激活、已最小化恢复的点击决策。
- 0、少量和大量项目的内容长度与 65% 工作区上限。
- 底部/顶部水平居中与左右垂直几何（含负坐标和超长截断）。
- 固定、运行、活动、多窗口分组到 UI 状态的映射。
- AppBar 注册、重复预留、关闭预留、退出释放、Explorer 重建重注册和注册失败重试路径。

## 2026-07-31 本机验证记录（全宽诊断条版本）

环境：Windows build 26200，`DISPLAY5` 单屏，2048 × 1152；当前机器无法验证真实多显示器和混合 DPI。该记录来自视觉改造前的诊断条形态，交互行为在冰蓝视觉版本上尚未重新真机复核。

| 项目 | 结果 |
| --- | --- |
| Release 构建 | 通过，0 警告、0 错误 |
| 纯逻辑烟雾测试 | 7/7 通过 |
| 窗口枚举 | 识别 Coffee CLI、Metrik、微信、ChatGPT；启动 Notepad 后 350 ms 观察点已出现 |
| Dock 点击 | Metrik 的 `SetForegroundWindow` 返回成功，界面显示“已切换到 metrik” |
| 左侧 AppBar | 实际矩形 `0,0,92,1152`，工作区改变，正常退出恢复 |
| 顶部 AppBar | 实际矩形 `0,0,2048,92`，工作区改变，正常退出恢复 |
| 右侧 AppBar | 实际矩形 `1956,0,2048,1152`，工作区改变，正常退出恢复 |
| 底部 AppBar | 实际矩形 `0,1060,2048,1152`，工作区改变，正常退出恢复 |
| 强制终止 | 底部工作区由 1152 变为 1060，强制终止后恢复到 1152 |
| 首个 HWND | 约 651 ms |
| 5 秒空闲 CPU 样本 | 约 0.087%（18 逻辑处理器） |
| 工作集样本 | 约 97.1 MiB |
| Release 输出（框架依赖） | 约 0.24 MiB / 5 个文件 |

这些数字是单次技术样本，不是产品性能承诺。

## 2026-08-05 冰蓝视觉版本验证记录

| 项目 | 结果 |
| --- | --- |
| Release 构建 | 通过，0 警告、0 错误 |
| 纯逻辑烟雾测试 | 12/12 通过 |
| 底部/左侧冰蓝视觉与真实图标 | 通过，单屏 2048×1152、125% 缩放 |
| 固定项启动与应用右键菜单 | 通过；Notepad 固定项可启动，窗口组菜单可打开 |
| AppBar 预留、浮层切换与正常退出释放 | 通过；左侧预留 104 px，切换浮层和退出均恢复完整工作区 |
| AppBar 可交互层 | 通过；激活其他普通桌面表面后 Dock 仍可点击；全屏真机路径未验证 |

## 尚未通过的决策门

以下项目没有合适的本机环境，不能标记为通过：

- 两台以上显示器、负坐标实屏和 100%/125%/150% 混合缩放。
- 显示器插拔、更换主屏和 WPF `WM_DPICHANGED` 真机行为。
- Explorer 重启后的 `TaskbarCreated` 自动重注册。
- PWA、UWP/宿主窗口、虚拟桌面、管理员窗口和受保护进程。
- 当前只读取进程级 AUMID，尚未读取窗口的 `PKEY_AppUserModel_ID`；同一宿主进程中的多个 PWA/应用可能被误合并。
- 最大化、独占全屏、任务栏自动隐藏与其他第三方 AppBar 同时运行。
- 顶部/右侧冰蓝视觉、悬停/活动高亮的完整状态矩阵尚未真机复核；本轮只确认底部和左侧。
- 固定项只有内存态，未验证与运行中应用（尤其 AUMID 分组）合并的全部边界。
- 长时间空闲 CPU、内存和 WinEvent 漏事件情况。

## 当前结论

最小 Win32 方案已经证明“可做”，并且比直接采用 ManagedShell 更符合轻量和低副作用目标。诊断条已替换为内容宽度的冰蓝居中表面，窗口枚举、分组、激活、AppBar 和几何逻辑与视觉窗口保持分离并可测试。它还没有达到生产合并条件；下一步应在第二台显示器或虚拟测试环境完成上述真机矩阵，再把接口收敛到正式 `BingLan.App`。
