# Metrik 双层圆角与白角修复方案

## 1. 修订结论

用户实机截图证明，`CreateRoundRectRgn + SetWindowRgn` 不是最终方案。它能把 HWND 裁成圆形，但 HRGN 只有“显示/不显示”两种整数像素，没有半透明覆盖率；在高反差背景和 125% 等缩放下，会出现硬台阶、白角或像外面还有一层底。

这个问题可准确描述为：

- **双层圆角（double rounded-corner artifact）**：DWM、窗口 Region 和内容层各画一套圆角；
- **背景漏角（backdrop bleed-through）**：原生材质没有被内容层的圆角一起裁掉；
- **二值圆角硬边（aliased binary window region）**：HRGN 边缘没有逐像素 Alpha，圆弧呈台阶。

修复契约只有一句：**一个组件只允许存在一份抗锯齿外轮廓，背景、标题、正文和描边都复用它。**

## 2. Metrik 的优先路线：复用现有 D3D11/Composition 渲染

如果 Metrik 的“扩展（D3D11）”模式已经使用 DXGI/DirectComposition，优先在现有 GPU 视觉树中修复，不要再给 HWND 设置圆角 Region：

```text
用户圆角值（DIP）
  -> 依据当前窗口 DPI 换算到渲染坐标
  -> 在背景与内容的共同根 Visual 上设置一份软边圆角 Clip
  -> 标题栏、主体、描边全部位于该 Clip 内
  -> 阴影作为 Clip 外的 sibling visual
```

具体约束：

1. 合成表面使用预乘 Alpha，清屏颜色为透明；不能先画一个不透明矩形底。
2. 背景材质和内容必须进入同一 Composition 子树，不能让 DWM/WCA 在树外另画一层矩形 Backdrop。
3. 使用 `IDCompositionRectangleClip` 时为四角设置同一组半径，并为持有该 Clip 的 Visual 启用 `DCOMPOSITION_BORDER_MODE_SOFT`；默认硬边模式仍会锯齿。
4. 若用 Direct2D 绘制圆角遮罩，使用 per-primitive 抗锯齿和预乘 Alpha；描边复用同一几何，不再额外创建第二个填充圆角层。
5. 阴影读取同一圆角 Alpha mask，在主体外渲染；阴影不能是一个带底色的第二矩形窗口。
6. 明确关闭 DWM 系统圆角和非客户区边框，避免系统再叠加一套轮廓。

如果当前 D3D11 只是普通不透明 HWND SwapChain，Clip 无法产生透明窗外像素；应先迁到带预乘 Alpha 的 Composition SwapChain/Visual，再实现上述方案。

## 3. WPF 项目的近期路线

若项目仍以 WPF 控件为主，并且必须保留 `0–32 DIP` 连续可调半径，可采用逐像素透明窗口：

- `WindowStyle=None`；
- `AllowsTransparency=True`；
- `Background=Transparent`；
- 删除 `SetWindowRgn`；
- 关闭 DWM 系统圆角和非客户区绘制；
- 用唯一的 WPF `RectangleGeometry` 裁剪整个窗口视觉树；
- 圆外透明像素在 `WM_NCHITTEST` 中返回 `HTTRANSPARENT`；
- 正式逐像素路径禁用 WCA 模糊，使用同一 Clip 下的半透明 tint；只有在纹理背景实拍矩阵证明不会漏出矩形底层，或改成共享 Alpha mask 的 DirectComposition 后，才可重新启用模糊。

这个方案能输出 0–255 的逐像素 Alpha，因此比 HRGN 自然；代价是 DWM 不再负责自定义阴影，系统 Acrylic/WCA 在 Layered Window 上也没有可靠的公开兼容合同。

## 4. 如果优先要求官方稳定 Acrylic

若“官方稳定系统材质”比“任意圆角值”更重要，则使用普通 HWND 和：

- `DWMWCP_ROUND`；
- `DWMWCP_ROUNDSMALL`；
- `DWMWCP_DONOTROUND`。

这条路线由 DWM 提供抗锯齿、阴影和 Backdrop 裁剪，但只能提供系统标准/小圆角，设置界面也应改成“关闭 / 小 / 标准”，不能继续显示一个看似连续却无法真实生效的半径滑块。

## 5. 命中测试必须复用同一几何

无论采用 D3D11 Composition 还是 WPF Layered Window，输入几何都必须与视觉圆角一致：

1. 圆角外部返回 `HTTRANSPARENT`，不抢桌面或后方程序的鼠标；
2. 圆弧内侧保留约 `7 DIP` 环形缩放带，返回四个对角缩放值；
3. 再判断四条直边；
4. 按钮、文本框、滑块返回 `HTCLIENT`；
5. 其余空白区返回 `HTCAPTION`，保证整窗可直接拖动；
6. 布局锁定后关闭移动和缩放，但透明圆外仍不能截获输入。

## 6. DPI、状态与失败回退

- 配置继续保存 DIP 值，建议范围 `0–32 DIP`，默认值由主题决定；
- DPI、尺寸、跨屏、主题和透明效果变化后重建 Clip；
- 圆角、窗口位置和尺寸分别持久化，保存采用临时文件替换并保留有效备份；
- GPU/Composition 初始化失败时，回退到同一抗锯齿轮廓下的半透明或纯色 Surface；
- 失败回退不能重新启用 HRGN，否则视觉缺陷会回来。

## 7. 最低验收矩阵

- `0 / 3 / 15.5 / 32 DIP` 和越界钳制；
- 100%、125%、150%、175%、200% DPI；
- 黑、白、红和复杂壁纸背景；
- 四角 4× 放大后存在多级混合像素，不允许只有二值台阶；
- 任意扫描线只能从“桌面”过渡到“组件”一次，不出现“桌面 → 白圈 → 主体”；
- 标题空白可拖动，四边和四个圆弧可缩放；
- 输入控件、右键菜单和锁定状态可靠；
- 运行时改半径、连续缩放、跨屏和重启恢复；
- GPU/材质失败时仍只有一条外轮廓。

官方接口参考：

- [Windows 11 圆角窗口](https://learn.microsoft.com/windows/apps/desktop/modernize/ui/apply-rounded-corners)
- [WPF Window.AllowsTransparency](https://learn.microsoft.com/dotnet/api/system.windows.window.allowstransparency)
- [DirectComposition clipping](https://learn.microsoft.com/windows/win32/directcomp/clipping)
- [IDCompositionVisual::SetBorderMode](https://learn.microsoft.com/windows/win32/api/dcomp/nf-dcomp-idcompositionvisual-setbordermode)
- [Visual layer in desktop apps](https://learn.microsoft.com/windows/uwp/composition/visual-layer-in-desktop-apps)

冰蓝项目的 WPF 逐像素参考实现位于：

- `src/BingLan.App/Windows/WidgetWindowBase.cs`；
- `src/BingLan.App/Windows/WidgetBackdropController.cs`；
- `src/BingLan.App/Windows/TodoWidgetWindow.xaml`；
- `src/BingLan.App/Windows/FileBoxWindow.xaml`；
- `tests/BingLan.UiSmokeTests/Program.cs`。
