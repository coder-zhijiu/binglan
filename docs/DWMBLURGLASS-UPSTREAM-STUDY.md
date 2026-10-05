# DWMBlurGlass 上游研究记录

> - 文档状态：上游行为与风险研究，不是生产依赖决策
> - 研究日期：2026-08-06
> - 上游仓库：[Maplespe/DWMBlurGlass](https://github.com/Maplespe/DWMBlurGlass)
> - 固定版本：[e3a160c849c35415a4b959462815eebc6870270b](https://github.com/Maplespe/DWMBlurGlass/tree/e3a160c849c35415a4b959462815eebc6870270b)
> - 适用范围：冰蓝桌面材质设计、任务栏隔离实验、Windows 外观适配的风险控制

## 1. 结论

DWMBlurGlass 可以作为以下两类问题的研究样本：

1. Blur、Aero、Acrylic、Mica 等材质如何拆分为颜色、模糊、饱和度、噪点、活动状态和性能档位；
2. 高风险 Windows 外观模块如何做版本门控、启停配对、崩溃循环保护和失败回退。

它不能成为冰蓝桌面的生产依赖、代码来源或任务栏实现方案。上游主要修改全局窗口标题栏，并通过管理员启动项、向 `dwm.exe` 注入 DLL、解析私有 DWM 符号和 Hook 内部函数实现效果。这与冰蓝桌面“单应用宿主、核心功能不要求管理员权限、不注入 DWM、向 Explorer 只加载只改任务栏背景的自有组件、失败时恢复系统默认”的边界不一致。

后续引用本记录时，只能采纳产品行为、材质参数语义、验证方法和故障隔离原则；不得复制上游源码、私有结构、函数偏移、Hook、图标或其他资产。

## 2. 上游解决的问题

DWMBlurGlass 的产品目标是为 Windows 10/11 的全局系统标题栏增加自定义效果，而不是控制 Windows 任务栏。其 README 描述的主要能力包括：

- Blur、Aero、Acrylic、Mica 和 MicaAlt；
- CustomBlur、AccentBlur 和 SystemBackdrop 三类模糊路径；
- 活动/非活动窗口颜色、亮色/暗色模式颜色；
- 模糊半径、标题文字和标题按钮外观；
- 省电状态、系统透明效果开关和渲染质量选项；
- DWM 异常转储、重新加载和版本兼容维护。

参考：[上游 README 的能力与兼容性说明](https://github.com/Maplespe/DWMBlurGlass/blob/e3a160c849c35415a4b959462815eebc6870270b/README.md#effects)。

因此，它对冰蓝任务栏的直接代码价值很低，但对材质命名、能力矩阵和高风险适配器的生命周期设计有参考价值。

## 3. 上游实现边界

### 3.1 进程与权限

上游 GUI 会启动同一可执行文件的 `runhost` 模式，由宿主持续发现 DWM 进程并加载扩展：

- [`runhost` 与 DWM 进程监视](https://github.com/Maplespe/DWMBlurGlass/blob/e3a160c849c35415a4b959462815eebc6870270b/DWMBlurGlass/MHostHelper.cpp#L257-L297)；
- [以最高权限注册登录计划任务](https://github.com/Maplespe/DWMBlurGlass/blob/e3a160c849c35415a4b959462815eebc6870270b/DWMBlurGlass/Helper/Helper.cpp#L115-L228)。

这会引入额外宿主、管理员权限和独立启动生命周期，不能并入冰蓝桌面的一进程、一启动项模型。

### 3.2 DWM 注入

上游通过 `OpenProcess`、`VirtualAllocEx`、`WriteProcessMemory` 和 `CreateRemoteThread(LoadLibraryW)` 把扩展 DLL 加载进 `dwm.exe`，停用时再远程调用 `FreeLibrary`：

- [注入实现](https://github.com/Maplespe/DWMBlurGlass/blob/e3a160c849c35415a4b959462815eebc6870270b/DWMBlurGlass/Helper/Helper.cpp#L454-L537)；
- [卸载实现](https://github.com/Maplespe/DWMBlurGlass/blob/e3a160c849c35415a4b959462815eebc6870270b/DWMBlurGlass/Helper/Helper.cpp#L540-L610)。

这是全局桌面合成器级修改。错误不仅会影响冰蓝桌面，也可能影响所有窗口和当前用户的桌面会话，因此不属于 MVP 可接受的失败半径。

### 3.3 私有符号与 Hook

上游从 Microsoft Symbol Server 获取 `dwmcore.dll` 和 `uDwm.dll` 符号，解析私有函数偏移，再通过 MinHook 和 IAT 改写接管内部 DWM 路径：

- [符号获取与枚举](https://github.com/Maplespe/DWMBlurGlass/blob/e3a160c849c35415a4b959462815eebc6870270b/DWMBlurGlass/Helper/SymbolResolver.cpp#L103-L157)；
- [私有函数目标列表](https://github.com/Maplespe/DWMBlurGlass/blob/e3a160c849c35415a4b959462815eebc6870270b/DWMBlurGlassExt/Common/DefFunctionList.h#L25-L170)；
- [Hook 和 IAT 改写辅助层](https://github.com/Maplespe/DWMBlurGlass/blob/e3a160c849c35415a4b959462815eebc6870270b/DWMBlurGlassExt/Helper/HookHelper.cpp#L30-L215)。

Windows 更新后可能需要重新下载符号或调整私有结构。冰蓝桌面不承担这种持续逆向维护成本。

## 4. 可以借鉴的原则

以下内容只能作为需求和架构原则，由冰蓝桌面独立实现。

### 4.1 能力探测优先于效果选择

不要仅以“Windows 11”判断某种效果可用。任务栏或应用材质实验至少应记录：

- Windows Build 和 Explorer 版本；
- DWM 合成、系统透明效果、高对比度和减少动画设置；
- 电源节能状态、远程桌面状态；
- 主副屏、缩放、任务栏位置和自动隐藏状态；
- 当前后端是否公开、是否需要修改非本进程窗口、是否能完整恢复。

不满足能力条件时直接返回“不支持”，不得尝试无限重载或静默升级到更高风险方案。

### 4.2 策略与后端分离

用户看到的是“系统默认、透明/模糊、智能隐藏”；底层候选实现只是后端。产品状态不得保存私有 API 名称、Windows 版本特例或 Hook 类型。

建议的结果模型至少区分：

- `Applied`：策略已应用，并持有可恢复的原状态；
- `Unsupported`：当前系统或 Explorer 不支持，保持系统默认；
- `FailedAndRestored`：应用失败，已经恢复原状态；
- `RecoveryRequired`：上次进程异常退出，启动时必须先恢复，不得立即重放效果。

### 4.3 启停配对与故障熔断

上游在关闭时集中撤销 Hook、释放视觉资源并触发重绘，其结构说明高风险视觉能力必须拥有明确的 `Apply`、`Refresh`、`Restore` 和 `Dispose` 边界：[上游 Shutdown](https://github.com/Maplespe/DWMBlurGlass/blob/e3a160c849c35415a4b959462815eebc6870270b/DWMBlurGlassExt/DWMBlurGlass.cpp#L141-L179)。

上游检测到 DWM 短时间反复启动后会停止自动加载。冰蓝桌面应采用更保守的等价规则：Explorer 或任务栏适配连续失败后，本次会话禁用适配器、恢复系统默认，并等待用户明确重试：[上游崩溃循环保护](https://github.com/Maplespe/DWMBlurGlass/blob/e3a160c849c35415a4b959462815eebc6870270b/DWMBlurGlass/MHostHelper.cpp#L191-L234)。

### 4.4 材质参数语义

可将上游材质能力抽象为冰蓝设计令牌，而不复用渲染实现：

| 语义 | 冰蓝可采用的表达 | 约束 |
| --- | --- | --- |
| Tint | 冰蓝主色、透明度 | 必须保证文字对比度 |
| Blur | 模糊强度或关闭 | 不可用时回退半透明纯色 |
| Saturation/Luminosity | 饱和度、明度平衡 | 不作为 MVP 必需设置 |
| Noise | 极弱噪点纹理 | 必须有资源授权且可关闭 |
| Active/Inactive | 活动与非活动状态差异 | 不依赖颜色作为唯一状态提示 |
| Quality | 质量优先、节能回退 | 隐藏、全屏或节能时自动降级 |

微软将 Mica 定义为适合应用基础层的高性能不透明材质，将 Acrylic 定义为适合临时表面的半透明材质。后续命名和使用场景应以 [Windows 系统材质文档](https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops) 为准，而不是照搬上游效果名称。

## 5. 明确禁止复用

- 不复制或改写上游 DLL 注入、远程线程和提权逻辑；
- 不引入 MinHook、私有 DWM 类型、符号偏移表或 PDB 下载缓存；
- 不把 DWMBlurGlass 作为冰蓝桌面的子进程、外部工具或安装前置条件；
- 不修改所有应用的标题栏、按钮、边框或系统 DWM 渲染；
- 不复制上游图标、噪点、反射贴图、截图或 GUI 资产；
- 不因公开 `DwmSetWindowAttribute` 存在，就假定可以安全修改不属于冰蓝桌面的窗口；
- 不使用 `SetWindowCompositionAttribute` 作为无条件生产后端。微软明确说明不推荐使用该 API，并建议改用 `DwmSetWindowAttribute`：[Microsoft 文档](https://learn.microsoft.com/en-us/windows/win32/dwm/setwindowcompositionattribute)。

公开的 `DWMWA_SYSTEMBACKDROP_TYPE` 只可作为冰蓝自有窗口的隔离实验候选，并应按 Windows Build 门控。微软文档注明该属性从 Windows 11 Build 22621 开始受支持：[DWMWINDOWATTRIBUTE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute#dwmwa_systembackdrop_type)。

## 6. 与冰蓝桌面现状的映射

冰蓝桌面的任务栏外观没有采用 DWMBlurGlass 的做法：

- 透明模式由自有原生组件经 Windows XAML 诊断接口只改任务栏背景元素，限定已验证的 Build 和元素结构，失联或退出时恢复，见 [任务栏 XAML 适配](TASKBAR-XAML.md)；
- 模糊模式使用公开的系统材质，自动隐藏使用 Windows 自带设置；不注入 DWM、不使用私有符号或 Hook，不需要管理员权限；
- 生命周期（恢复记录、幂等应用、异常退出后恢复、Explorer 重建重连、连续失败熔断、卸载恢复）和真机结果见 [Windows 任务栏适配真机验证](TASKBAR-QA.md)；
- 桌面组件使用逐像素透明 WPF 窗口和同一份圆角 Clip，主动清除系统 Backdrop；设置窗口只在 Build 22621 及以上对自有窗口使用 Mica。

DWMBlurGlass 不解决 WPF 逐像素透明窗口的圆角与 Backdrop 裁剪问题，本记录只作为风险和验证方法的参考。

## 7. 许可证与引用规则

上游 README 和多数源码头声明 LGPL-3.0-or-later，根目录同时包含 GPLv3 与 LGPLv3 许可文本：

- [README 许可声明](https://github.com/Maplespe/DWMBlurGlass/blob/e3a160c849c35415a4b959462815eebc6870270b/README.md#L6-L19)；
- [GPLv3 文本](https://github.com/Maplespe/DWMBlurGlass/blob/e3a160c849c35415a4b959462815eebc6870270b/LICENSE.txt)；
- [LGPLv3 补充条款](https://github.com/Maplespe/DWMBlurGlass/blob/e3a160c849c35415a4b959462815eebc6870270b/COPYING.LESSER)。

LGPLv3 本身建立在 GPLv3 条款之上，因此同时存在两份文本不等于可以任意复制。仓库还包含或引用 MinHook、pugixml、WIL、MiaoUI Lite 等第三方内容。未经逐文件许可证、通知义务和分发方式审查，不得复制任何实现或资产到冰蓝桌面。

本记录引用的代码链接只用于证明技术判断和定位上游行为，不构成生产依赖批准。

## 8. 重新评估触发条件

只有出现以下情况之一，才需要重新审查 DWMBlurGlass：

- 上游改为完全公开、文档化、无需注入的实现；
- 微软发布受支持的任务栏材质控制 API；
- 冰蓝桌面产品边界明确改变，允许管理员权限、额外进程或全局窗口外观修改；
- 项目整体许可证策略经过明确调整。

即使触发重新评估，也必须重新固定 commit、复核许可证并在隔离实验中验证，不能直接把旧结论视为生产批准。
