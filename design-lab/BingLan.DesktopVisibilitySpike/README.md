# BingLan 清爽桌面可见性技术验证

状态：隔离技术验证，不属于正式应用，不读取或修改 `%LOCALAPPDATA%\BingLanWidgets`。

## 目标

验证能否只使用微软文档化的 Shell COM 接口读取和设置 Windows 桌面视图的 `FWF_NOICONS` 标志，并在正常退出、失败和 Explorer 重启场景中可靠恢复原状态。

实验不得移动、重命名、删除桌面文件，不修改注册表，不结束 Explorer，不发送未文档化的窗口消息，也不注入 DLL。

## API 依据

- `IFolderView2::GetCurrentFolderFlags`：读取当前文件夹视图标志。
- `IFolderView2::SetCurrentFolderFlags`：按掩码设置文件夹视图标志。
- `FOLDERFLAGS::FWF_NOICONS`：请求视图不显示图标。
- `IShellWindows::FindWindowSW`、`IShellBrowser::QueryActiveShellView`：定位桌面 Shell 视图。

## 构建期依赖

| 依赖 | 版本 | 许可证 | 用途 | 运行时成本 | 替代方案 |
| --- | --- | --- | --- | --- | --- |
| Microsoft.Windows.CsWin32 | 0.3.298 | MIT | 从微软 Win32 元数据生成准确 COM 绑定 | 无额外运行时依赖 | 手写 COM vtable，错误风险更高 |

NuGet 包约 7.72 MB，仅用于源代码生成，不进入正式应用运行时。是否将该依赖用于生产代码，需要在实验通过后单独决策。

## 使用方式

```powershell
dotnet run --project .\design-lab\BingLan.DesktopVisibilitySpike\BingLan.DesktopVisibilitySpike.csproj -c Release -- --status
dotnet run --project .\design-lab\BingLan.DesktopVisibilitySpike\BingLan.DesktopVisibilitySpike.csproj -c Release -- --hide
dotnet run --project .\design-lab\BingLan.DesktopVisibilitySpike\BingLan.DesktopVisibilitySpike.csproj -c Release -- --show
dotnet run --project .\design-lab\BingLan.DesktopVisibilitySpike.Tests\BingLan.DesktopVisibilitySpike.Tests.csproj -c Release
```

不传参数等同于 `--status`，只读取状态。`--hide` 会先原子保存恢复检查点，再尝试只按掩码写入 `FWF_NOICONS`；直接写入失败时才尝试临时添加 `FVO_CUSTOMPOSITION`。`--show` 只按检查点恢复本实验拥有的位；没有检查点时保持当前桌面不变。不要在无人观察或尚未完成恢复矩阵时运行写操作。

## 2026-08-04 首轮结果

- 环境：Windows 11 家庭中文版，build 26200，x64，2560 × 1440。
- 隔离项目和规则测试以 Release/x64 构建通过，0 警告、0 错误；6 个位标志规则检查通过。
- 只读查询成功定位桌面窗口 `0x00010174`。
- 原始文件夹标志为 `0x40200224`，`FWF_NOICONS` 未设置，桌面图标可见。
- 视图选项为 `0x00000001`（`FVO_VISTALAYOUT`），没有显式报告 `FVO_CUSTOMPOSITION`。
- 查询没有改变状态。按安全门，写操作会在当前环境拒绝执行。

微软文档说明 `FVO_VISTALAYOUT` 目前具有类似自定义定位的效果，但应用若依赖自定义定位应显式申请 `FVO_CUSTOMPOSITION`。因此生产方案不能把 `FVO_VISTALAYOUT` 当成稳定等价物；下一轮需要设计“先持久化原始快照、临时设置前置选项、仅切换 `FWF_NOICONS`、最终逐位恢复”的协议，并在用户可观察时验证隐藏、恢复和 Explorer 重启。

## 2026-08-07 第二轮结果

- 同一台 build 26200 设备上，在 `FVO_VISTALAYOUT` 下直接按掩码写入 `FWF_NOICONS` 可以隐藏桌面图标。
- 尝试通过 `SetFolderViewOptions` 显式添加 `FVO_CUSTOMPOSITION` 返回 `E_NOTIMPL`，因此实验改为“直接掩码写入优先、添加前置位仅作回退”。
- 写入前保存 `Prepared` 检查点，成功后更新为 `Applied`；恢复只处理检查点记录的位，成功后才删除检查点。
- 位标志、前置位所有权和恢复计划共 13 项规则检查通过；Explorer 重启、异常终止和多显示器矩阵仍未完成。

## 2026-08-08 恢复边界收口

- 没有检查点的 `--show` 不再猜测用户原始状态，也不会改变当前图标可见性。
- 回退路径只会撤销实验实际新增的 `FVO_CUSTOMPOSITION`，不会把系统原有位记成实验所有。
- 新增原始图标已隐藏的恢复计划检查，当前共 15 项规则检查通过。

## 安全门

在以下条件全部满足前，不得接入正式应用：

1. 只读查询不会改变任何桌面标志；
2. 仅修改 `FWF_NOICONS` 位，其他标志逐位保持；
3. 隐藏后显式恢复到进入实验前的状态；
4. 重复隐藏和恢复具有幂等性；
5. COM 或 Explorer 不可用时安静失败，不触碰文件和注册表；
6. Explorer 重启后能重新取得视图，并按持久化恢复意图处理；
7. 真机记录 Windows build、显示器、缩放和原始/恢复标志。
