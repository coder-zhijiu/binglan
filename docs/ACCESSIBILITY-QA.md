# 无障碍与缩放验证

本清单用于避免只在开发机默认环境中检查界面。自动测试负责稳定发现 UI Automation、键盘顺序和布局边界回归；Narrator、高对比度切换以及跨显示器缩放仍需在真实 Windows 会话中执行。

## 自动回归门

在仓库根目录运行：

```powershell
dotnet run --project .\tests\BingLan.UiSmokeTests\BingLan.UiSmokeTests.csproj -c Release
```

当前自动检查覆盖：

- 设置中心、待办组件和桌面分组盒中所有可见、启用、可 Tab 聚焦控件均有 UI Automation 名称和标准角色；
- 待办、分组盒、设置布局页和设置组件页的 Tab 与 Shift+Tab 顺序能到达主要控件；
- 控件聚焦并滚动到视图后仍位于窗口客户区内；
- 文件映射卡片支持键盘聚焦，并向 UI Automation 提供 Invoke 操作；
- 输出 WPF 实际识别到的 DPI；可要求测试必须在指定缩放比例下运行；
- 自动模拟高对比度切换，检查四类组件、设置中心和外观面板改用不透明系统色，并在退出后恢复原冰蓝材质；
- 高对比度真实开启时，再额外确认 WPF 系统控件模板也随 Windows 对比度主题更新。

指定本轮必须在某个缩放比例运行：

```powershell
$env:BINGLAN_EXPECTED_DPI_PERCENT = '125'
dotnet run --project .\tests\BingLan.UiSmokeTests\BingLan.UiSmokeTests.csproj -c Release
Remove-Item Env:BINGLAN_EXPECTED_DPI_PERCENT
```

建议分别在 100%、125% 和 150% 下执行。多显示器机器还应把窗口移到不同缩放的显示器，关闭后重新打开，确认位置与尺寸恢复在可见工作区。

## 高对比度真实会话

先在 Windows 设置中开启一种对比度主题，再打开新的终端运行：

```powershell
$env:BINGLAN_REQUIRE_HIGH_CONTRAST = '1'
dotnet run --project .\tests\BingLan.UiSmokeTests\BingLan.UiSmokeTests.csproj -c Release
Remove-Item Env:BINGLAN_REQUIRE_HIGH_CONTRAST
```

除测试通过外，人工确认文字、焦点框、选中态、禁用态和滚动条均清晰；玻璃透明度不能成为读取内容的必要条件。关闭对比度主题后再运行一次，确认冰蓝主题恢复。

## Narrator 人工路径

启动 Narrator 后，只使用键盘完成以下路径，并记录实际播报名称、控件类型、状态和值：

1. 从设置左侧“设置类别”进入“布局”，在布局预设间移动并执行“应用布局”。
2. 进入“桌面组件”，遍历显示、锁定、字号、颜色、不透明度、圆角、密度、尺寸和内容设置；修改值后保存。
3. 在待办组件中编辑标题、切换完成状态、编辑内容、新增待办和切换已完成项目显示。
4. 在桌面分组盒中编辑标题、打开添加与排序菜单、聚焦文件映射并用 Enter 或 Space 调用、执行自动整理。
5. 对每个页面执行一次 Shift+Tab 反向遍历，确认无焦点陷阱、无跳出窗口、无聚焦到不可见控件。

自动 UI Automation 检查不能替代 Narrator 的真实语音、浏览模式、动态状态播报与输入体验。正式发布前建议再使用 Accessibility Insights for Windows 的 Live Inspect 和 FastPass 对运行中的窗口扫描。

## 结果记录

每轮至少记录：Windows 版本、显示器数量、每台显示器缩放、对比度主题、Narrator 是否开启、通过项、失败控件名称和截图。自动测试通过不代表跨显示器 DPI、高对比度视觉和屏幕阅读器人工路径已经完成。
