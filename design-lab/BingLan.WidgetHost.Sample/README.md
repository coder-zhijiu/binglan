# BingLan WidgetHost 透明宿主原型

这是一个隔离在 `design-lab` 中的 Windows 11 技术原型，用于验证 Pogget 风格桌面组件的透明材质、鼠标命中和视觉参数。它不参与冰蓝主程序的运行，也不读写主程序用户数据。

## 这版已经证明什么

- WPF 可以继续承担业务控件和中文输入，不需要切换 UI 框架；
- 窗口使用普通非分层 HWND，不使用 `AllowsTransparency=True`；
- 未锁定时，按钮、输入框和文件项之外的整个窗口都能直接拖动；
- 7 DIP 边缘支持八方向系统缩放，锁定后移动和缩放同时失效；
- 组件默认不置顶、不穿透、不使用 `WS_EX_NOACTIVATE`，并通过 `ShowActivated=False` 配置为初次显示不主动激活；
- Pogget 风格动态模糊、Windows 11 系统材质和不透明纯色形成三级降级；
- 外窗只保留一套原生圆角轮廓，不再出现 DWM 圆角与 WPF 背景圆角错位形成的“双层角”；
- 文件盒和待办共用 `0–32 DIP` 圆角设置，运行时拖动滑块即可立即更新；
- 同一宿主可承载文件图标网格和可编辑待办两种真实调用方。

原型不包含位置/待办持久化、开机启动、桌面 WorkerW 嵌入和生产迁移。这些仍属于主应用后续工作。

## 项目结构

```text
BingLan.WidgetHost.Wpf
├─ WidgetWindow                 普通 HWND、单一圆角轮廓、锁定和 WM_NCHITTEST
├─ WidgetBackdropController     DWM/WCA 材质、阴影和纯色降级
├─ WidgetHitTest                显式交互区域附加属性
├─ Interop/NativeMethods        集中的最小 Win32 边界
└─ Themes/IceBlue.xaml          Pogget/冰蓝视觉令牌

BingLan.WidgetHost.Sample
├─ FileBoxDemoWindow            文件网格、拖放映射、打开与移除
├─ TodoDemoWindow               标题/待办编辑、勾选、新增、排序与隐藏完成
└─ Interop                      Shell 图标与仅用于 QA 的真实窗口捕获

BingLan.WidgetHost.Tests
└─ Program.cs                   真实 HWND/WPF 烟雾测试
```

## 运行

在仓库根目录执行：

```powershell
dotnet build .\design-lab\BingLan.WidgetHost.slnx -c Release
dotnet run --project .\design-lab\BingLan.WidgetHost.Tests\BingLan.WidgetHost.Tests.csproj -c Release
dotnet run --project .\design-lab\BingLan.WidgetHost.Sample\BingLan.WidgetHost.Sample.csproj -c Release
```

也可以直接运行：

```text
design-lab\BingLan.WidgetHost.Sample\bin\Release\net10.0-windows\BingLan.WidgetHost.Sample.exe
```

`--interactive-qa` 只用于让自动化工具临时识别两个窗口，因此会显示任务栏入口；不带参数的正常模式仍是 `WS_EX_TOOLWINDOW`，不显示任务栏按钮。

## 窗口与输入实现

窗口契约：

```text
WindowStyle=None
AllowsTransparency=False
ResizeMode=CanResize
ShowInTaskbar=False
ShowActivated=False
Topmost=False

WS_THICKFRAME       = on
WS_EX_TOOLWINDOW    = on
WS_EX_ACCEPTFILES   = on
WS_EX_LAYERED       = off
WS_EX_TRANSPARENT   = off
WS_EX_NOACTIVATE    = off
WS_EX_TOPMOST       = off

DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_DONOTROUND
```

`WM_NCCALCSIZE` 去掉不可见非客户区内缩，`WM_NCHITTEST` 按以下优先级返回：

1. 布局锁定：始终 `HTCLIENT`；
2. 四角和四边：对应八个系统缩放命中码；
3. `Button`、`TextBox`、`Selector`、滚动条或 `WidgetHitTest.IsInteractive=True`：`HTCLIENT`；
4. 其余客户区：`HTCAPTION`。

因此拖动依赖 Windows 自己的窗口移动循环，而不是在 `MouseMove` 中反复改 `Left/Top`。鼠标移出组件、快速拖动和 DPI 换算都交给系统处理。

## 单一圆角轮廓

旧实现同时启用了 DWM 系统圆角，并给 WPF 根背景设置了另一套 `CornerRadius`。两者的半径、像素取整和裁剪阶段不同；WPF 也无法裁掉 HWND 后方的原生材质，所以四角会露出第二层底色。这就是截图里看到的“双层圆角 / 背景漏角”。

当前实现只保留一个外轮廓：

```text
WidgetCornerRadius（DIP）
  -> 按当前窗口 DPI 换算为物理像素
  -> CreateRoundRectRgn
  -> SetWindowRgn 裁剪整个 HWND、材质和命中区域
  -> WPF 根背景保持矩形填满
  -> 同半径透明 Border 只画 1 px 描边，不再承载第二层背景
```

宿主同时把 `DWMWA_WINDOW_CORNER_PREFERENCE` 固定为 `DWMWCP_DONOTROUND`，避免系统再叠一套不可调圆角。半径为 `0` 时清除窗口区域，恢复矩形；调整大小、DPI、主题或合成状态变化后会重算区域。`SetWindowRgn` 成功后 HRGN 所有权交给系统，失败时才由宿主释放，并清除旧裁剪回退为安全矩形。

最大 32 DIP 圆角会吃掉传统矩形角上的命中点，所以角缩放也改为沿可见圆弧内侧的命中带判断；大圆角下仍能抓住四角缩放。

## 材质实现

| 模式 | 实现 | 稳定性角色 |
|---|---|---|
| `PoggetLike` | 客户区 `SetWindowCompositionAttribute` + `ACCENT_ENABLE_BLURBEHIND`，再叠加 `#52FFFFFF` 内容表面；保留四边 1 px DWM Frame 生成外部阴影 | 最接近参考图；API 未公开，属于可选实验后端 |
| `Auto` | `DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_TRANSIENTWINDOW` | Windows 11 文档化默认后端 |
| `Solid` | 强制不透明本地颜色 | 高对比度、DWM/原生调用异常和用户手动降级 |

`PoggetLike` 的两个 DWM 前置调用和 WCA 调用必须全部成功，否则继续尝试 `Auto`；任意材质异常最终都在 WPF 层落到不透明纯色，不让窗口变成黑色矩形。纯色值通过依赖属性强制为 `A=255`。

这三个项目没有新增第三方 NuGet 包，没有附带原生 DLL，也没有第二个常驻进程。

## 已验证结果

自动测试于 2026-08-01 在 Windows `10.0.26200.0` 上通过：

- 真实 HWND 样式和 DWM 属性；
- `AllowsTransparency=True` 防误用；
- `0 / 3 / 15.5 / 32 DIP` 在 `PoggetLike / Auto / Solid` 三种材质下均只有同一原生轮廓；
- 圆角运行时切换、窗口缩放后重建，以及 32 DIP 圆弧可达位置的四角缩放命中；
- 空白区、交互控件、显式交互区、四边四角和锁定命中；
- 纯色降级、透明色矫正和材质重复应用；
- 两个 Sample 共同复用宿主；
- 待办新增、焦点、文字回写、勾选、划线、摘要、隐藏完成和锁定菜单。

真实鼠标验证环境为 Windows `10.0.26200.0`、125% 缩放：

| 场景 | 观察结果 |
|---|---|
| 整窗拖动 | 文件盒屏幕原点从 `(2059,45)` 移至 `(1984,0)`，与 125% 缩放下的拖动距离一致 |
| 拖边缩放 | 文件盒从 `353×444` 缩为 `313×404` |
| 锁定 | 相同拖动和右下角缩放操作后，位置与尺寸均不变 |
| 解锁 | 再次拖动后原点从 `(1984,0)` 变为 `(1959,25)` |
| 标题编辑 | 点击标题后输入“本周重点”成功 |
| 待办编辑 | 点击第一条并改为“完成透明宿主验证”成功 |
| 勾选 | 第一条变淡并划线，摘要变为 `1/3 已完成` |
| 新增 | 第四条出现并立即显示输入光标，可输入“检查锁定交互” |
| 隐藏完成 | 列表从 4 条变为 3 条，摘要仍保留 `1/4 已完成` |
| 设置与关闭 | 两种组件的设置菜单均可打开，菜单“关闭组件”均真实关闭窗口 |
| 圆角调节 | 文件盒从默认 `15.5 DIP` 真实拖到 `3 DIP`，外轮廓立即变为近直角且未出现第二层底角 |
| 共同设置 | 待办菜单显示并操作同一套圆角滑块，证明两种组件复用宿主实现 |

圆角修复前后实拍见 [widgethost-corner-before-after.png](../artifacts/widgethost-corner-before-after.png)，官方参考与修复后宿主并排图见 [widgethost-reference-vs-single-corner.png](../artifacts/widgethost-reference-vs-single-corner.png)。恢复 1 px DWM Frame 后的自然阴影实拍见 [widgethost-live-single-corner-frame1.png](../artifacts/widgethost-live-single-corner-frame1.png)。

## 仍未验证

- 100%、150%、175% 和 200% 缩放；
- GDI 窗口区域是整数像素二值边界；`3 DIP` 等极小圆角在其他 DPI 下的抗锯齿观感尚未逐档评估，若生产验收要求亚像素级边缘，应改用 DirectComposition 几何裁剪；
- 多显示器、负坐标和跨 DPI 拖动；
- Explorer 重启、远程桌面、休眠/唤醒和系统关闭透明效果；
- 真实外部文件拖入与 Shell 打开闭环；
- 折叠按钮的真实鼠标闭环，以及显示前后前台 HWND 是否保持不变；
- 长时间 GPU/CPU 和设备丢失恢复。

这些项目在进入主程序前仍需补齐。视觉对照与剩余差异记录见 [design-qa.md](design-qa.md)。
