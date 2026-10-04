# WidgetHost 设计 QA

日期：2026-08-01  
范围：Pogget 风格文件收纳盒 + 共用透明窗口宿主  
环境：Windows 11 `10.0.26200.0`，125% 缩放，单一验证显示器

## Source truth

- 用户提供的官方效果截图：[pogget-reference-source.png](../artifacts/pogget-reference-source.png)
- 根据截图测量建立的静态几何基准：[pogget-visual-clone.png](../artifacts/pogget-visual-clone.png)
- 静态参考并排图：[reference-vs-clone.png](../artifacts/reference-vs-clone.png)
- 普通 HWND + 真实 DWM/WCA 实拍：[widgethost-live-final.png](../artifacts/widgethost-live-final.png)
- 官方截图与真实宿主并排图：[widgethost-reference-vs-live-final.png](../artifacts/widgethost-reference-vs-live-final.png)
- 单一圆角修复后实拍：[widgethost-live-single-corner.png](../artifacts/widgethost-live-single-corner.png)
- 修复前后同尺寸并排图：[widgethost-corner-before-after.png](../artifacts/widgethost-corner-before-after.png)
- 官方截图与单一圆角宿主并排图：[widgethost-reference-vs-single-corner.png](../artifacts/widgethost-reference-vs-single-corner.png)
- 恢复 DWM 阴影后的桌面实拍：[widgethost-live-single-corner-frame1.png](../artifacts/widgethost-live-single-corner-frame1.png)
- 标题栏聚焦对照：[widgethost-header-compare.png](../artifacts/widgethost-header-compare.png)
- 文件网格聚焦对照：[widgethost-grid-compare.png](../artifacts/widgethost-grid-compare.png)

## 对照状态

- 官方截图原图：`605×1013`；基准裁片 `(27,16,377,468)`；
- 原型窗口：`353×444` DIP，捕获时四周加入 12 DIP 上下文；
- 最终实拍经窗口 DPI 归一化为 `377×468 @ 96 DPI`；
- 并排画布：`790×490`，参考位于 `(5,11)`，实现位于 `(408,11)`；
- 内容状态：文件盒展开、11 个相同样例、PoggetLike 模糊。

参考截图不是当前会话中可重新控制的官方组件窗口截图，且截图中的桌面调色/裁切原点无法从 PNG 唯一恢复。因此本 QA 不把两张图的背景像素差冒充“材质完全相同”；静态基准负责几何和令牌，真实截图只证明背景透出、模糊观感、圆角、边框和可读性。具体 DWM/WCA 路径由运行时返回值、代码和烟雾测试证明，不由 PNG 单独证明。

## 比较历史

1. 初版使用全窗口 DWM Frame/System Backdrop：主体发白且接近不透明，P1；改为客户区 WCA BlurBehind。
2. 初版保留系统非客户区：顶部出现白条，内容尺寸被挤压，P2；增加 `WM_NCCALCSIZE` 并在 FrameChanged 前安装 Hook。
3. `UniformGrid` 曾把少量行拉满全高，P2；固定内容宽度 300、四列各 75，并让面板顶部对齐。
4. Shell 图标和列间距偏小，P2；图标槽调整为 48，固定列节奏。
5. 材质异常、启动激活和误用 Layered Window 是截图不可见的 P1 风险；配置 `ShowActivated=False`，并增加显式防误用和纯色异常边界。前台 HWND 是否保持不变尚未做行为对照。
6. 旧版同时使用 DWM 系统圆角和 WPF 根背景圆角，四角出现第二层底色，P1；改为 `DWMWCP_DONOTROUND` + `SetWindowRgn` 单一 HWND 裁剪，WPF 根背景改为矩形填满，同半径 Border 只负责描边。
7. 初次切到窗口区域裁剪后外部阴影变弱，P2；`PoggetLike` 四边保留 1 px DWM Frame，实拍恢复自然阴影且未引入第二层圆角。
8. 真实鼠标把文件盒半径从 `15.5 DIP` 调到 `3 DIP`，外轮廓立即变化；待办显示相同控件。`0 / 3 / 15.5 / 32 DIP` 与三种材质组合由真实 HWND 测试覆盖。

## Fidelity surfaces

| 表面 | 结果 | 说明 |
|---|---|---|
| Typography | 可用，保留 P2 差异 | Microsoft YaHei UI / Segoe UI，19 Bold Italic；实拍标题约宽 7 px、高 2–3 px，并下移约 3 px |
| Spacing / layout rhythm | 通过 | 377×468 画布、353×444 外壳、12 外留白、50 标题栏、70 首行、四列 75 节奏一致；外窗默认 15.5 DIP 且可调至 0–32 DIP |
| Colors / tokens | 通过 | 标题栏代表色参考约 `#A1B5E0`，实拍约 `#A1B4E1`；主体令牌为 `#52FFFFFF` |
| Material appearance | 未判定像素保真度 | 已证明实时透出与模糊；官方和原型背后的壁纸像素不等价，不能据此签收亮度、色调或模糊半径相同 |
| Image quality / assets | 可用，保留 P2 差异 | 使用真实 Windows Shell 图标；实拍白色主体约窄 4 px、矮 2 px，并下移约 4 px |
| Copy / content | 通过 | 标题、11 个文件名、截断和换行状态与参考一致 |
| Controls / states | 核心状态通过，折叠未验证 | 设置、锁定、关闭、圆角实时调节、待办编辑、勾选、新增和隐藏完成经自动测试及 [真实鼠标记录](README.md) 验证；折叠已有实现，但未纳入本轮自动或手工记录 |

## 剩余差异

- 非阻塞 P2：标题约宽 7 px、高 2–3 px并下移约 3 px；“更多”字形向右约 11 px且偏小；Shell 图标可见主体约窄 4 px、矮 2 px并下移约 4 px；
- 证据限制、未评估：实时磨砂颜色随窗口后方内容、Windows 透明设置和显卡合成路径变化；当前 PNG 不能证明官方与原型使用完全相同的背景像素，因此不评价材质像素保真度；
- 行为未验证：折叠按钮的真实鼠标闭环，以及初次显示前后前台 HWND 是否保持不变；
- 边缘限制：当前 WPF 原型用 GDI HRGN 裁剪，轮廓为整数像素二值区域；极小圆角在 100%、150%、175% 和 200% DPI 下的抗锯齿观感尚未逐档评估；
- 预期差异：原型外部阴影由 1 px DWM Frame 触发，静态参考使用可控 DropShadowEffect，因此截图中的软阴影宽度不会逐像素相同。

没有剩余 P0、功能性 P1 或阻塞可复用宿主验证的 P2。这里的“通过”只表示窗口宿主、视觉结构、可读性和核心交互达到技术原型标准；它不表示 Pogget 材质像素复刻已经签收。

final result: passed
pass scope: reusable host prototype
material pixel fidelity: not assessed
