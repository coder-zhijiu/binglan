# BingLan TaskbarSpike

状态：隔离技术验证，不是生产任务栏模块，不读取或修改 `%LOCALAPPDATA%\BingLanWidgets`。正式适配器位于 `src/BingLan.App/Taskbar`，真机结果见 `docs/TASKBAR-QA.md`。

## 目标

只验证“Windows 系统默认 → 完全透明 → 系统默认”的最小闭环。用户已经把近期范围收缩为静态透明任务栏，因此本实验不实现模糊、颜色、智能隐藏、最大化窗口、开始菜单、搜索、任务视图或省电状态联动。

## 安全边界

- 不向 Explorer 注入 DLL，不修改 Explorer 文件，不要求管理员权限；
- 不复制 TranslucentTB 的 GPLv3 源码、DLL 或 XAML 视觉树实现；
- 检测到高对比度、远程桌面、系统透明效果关闭或其他任务栏工具运行时拒绝实验；
- `probe` 最长运行 60 秒，并在 `finally`、Ctrl+C 和下次启动恢复检查点中恢复系统默认；
- 探测期间每 500ms 检查任务栏 HWND；发现 Explorer 重建后，只在 5 秒稳定窗口内重复应用透明，随后停止重试；
- 命名互斥体禁止两个 Spike 会话并发修改任务栏；恢复时如检测到其他任务栏工具已经运行，则保留检查点且不覆盖对方；
- 外部 API 没有受支持的任务栏材质读取接口，因此不能无损快照第三方工具的外观。实验只在未运行其他任务栏工具时执行；恢复语义是恢复 Windows 系统默认。

## 候选后端

实验通过 `SetWindowCompositionAttribute(WCA_ACCENT_POLICY)` 对 `Shell_TrayWnd` 和 `Shell_SecondaryTrayWnd` 发送 `ACCENT_ENABLE_TRANSPARENTGRADIENT`，恢复时发送 `ACCENT_DISABLED`。

微软公开了函数签名，但明确标注“不建议使用”，并建议使用 `DwmSetWindowAttribute`。公开 DWM API 没有用于修改 Explorer 任务栏材质的等价属性，所以本后端只能作为 Build 门控实验，不能因调用返回成功就判定视觉效果成功：[SetWindowCompositionAttribute](https://learn.microsoft.com/windows/win32/dwm/setwindowcompositionattribute)。

当前 TranslucentTB 2026.1 在 Windows 11 上包含 `ExplorerTAP.dll`、XAML 视觉树监视和任务栏外观服务。冰蓝桌面只参考其 `clear + #00000000` 的用户可见目标，不采用 Explorer 内部实现：[TranslucentTB](https://github.com/TranslucentTB/TranslucentTB)、[TaskbarAppearanceService](https://github.com/TranslucentTB/TranslucentTB/blob/release/ExplorerTAP/taskbarappearanceservice.cpp)。

## 命令

```powershell
dotnet run --project .\design-lab\BingLan.TaskbarSpike\BingLan.TaskbarSpike.csproj -c Release -- status
dotnet run --project .\design-lab\BingLan.TaskbarSpike\BingLan.TaskbarSpike.csproj -c Release -- probe 15 --acknowledge-undocumented-api
dotnet run --project .\design-lab\BingLan.TaskbarSpike\BingLan.TaskbarSpike.csproj -c Release -- restore
dotnet run --project .\design-lab\BingLan.TaskbarSpike.Tests\BingLan.TaskbarSpike.Tests.csproj -c Release
```

## 当前验证矩阵

| 场景 | 状态 | 证据 |
| --- | --- | --- |
| Build 26200 / Windows 11 25H2 / Explorer 10.0.26100.8875 / 单屏 2560×1440（逻辑 2048×1152）/ 125% / 自动隐藏关闭 | 未达到透明 | 2026-08-10：API 返回成功；同一任务栏 HWND 从系统浅灰变为浅冰蓝，图标与托盘保持可用。2026-10-03 取样确认浅冰蓝是均匀底色，不透出壁纸，见 `docs/TASKBAR-QA.md` |
| 正常超时恢复 | 通过 | 60 秒后 API 报告恢复成功；同一 HWND 重新显示系统浅灰背景，恢复检查点已删除 |
| Ctrl+C 恢复 | 待验证 | 人工取消后确认任务栏恢复 |
| 强制终止后的下次启动恢复 | 通过 | 2026-08-10：精确终止 Spike PID 后，任务栏保持透明且检查点保留；下一次 `status` 自动恢复新任务栏为系统浅灰并删除检查点 |
| Explorer 重启 | 通过（实验） | 2026-08-10：首测暴露新 HWND 未重应用；加入 500ms 检测和 5 秒稳定窗口后复测，Explorer PID 25640→10632、任务栏 HWND →0x5D0BF4，透明保持可见，探测结束后恢复系统浅灰且检查点删除 |
| 多显示器、100%/150%、远程桌面 | 未覆盖 | 需要相应环境 |

只有 API 返回成功、视觉对比确认透明且恢复确认通过，才能把本机 Build 标记为“实验可用”。这仍不等于允许进入生产应用。
