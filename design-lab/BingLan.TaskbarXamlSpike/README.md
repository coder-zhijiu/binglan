# 任务栏 XAML 真机验证

此程序调用正式 `TaskbarAdapter`，使用独立恢复记录和原生组件缓存。运行环境必须有真实 Windows 桌面；测试前退出其他任务栏工具，测试后恢复原来的工具。不会读取正式冰蓝桌面的个人设置。

```powershell
dotnet build .\design-lab\BingLan.TaskbarXamlSpike\BingLan.TaskbarXamlSpike.csproj -c Release
.\design-lab\BingLan.TaskbarXamlSpike\bin\Release\net10.0-windows\BingLan.TaskbarXamlSpike.exe probe .\artifacts\taskbar-xaml\normal
ormal
ormal
```

命令：

- `probe`：记录前后截图，开启透明，等待 3 秒，切回系统默认并确认恢复。
- `cycle`：先完成 `probe`，再执行三轮启用中取消、重新启用和恢复。
- `hold`：开启透明后保持运行，打印测试宿主 PID。用于只结束该宿主，检查 Explorer 中的组件能否自行恢复。
- `capture`：只截图，所有截图使用每显示器 DPI 坐标。
- `restore`：恢复指定证据目录的检查点，不读取正式状态。

每个命令的第二个参数必须是绝对证据目录。`hold` 强制结束后先运行 `capture`，再运行 `restore`，这样可以区分自动恢复与恢复命令的作用。

结果与未覆盖范围见 [任务栏验证](../../docs/TASKBAR-QA.md)。
