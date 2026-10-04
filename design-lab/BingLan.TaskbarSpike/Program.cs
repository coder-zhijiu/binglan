using System.Diagnostics;
using Microsoft.Win32;
using BingLan.TaskbarSpike;
using BingLan.TaskbarSpike.Interop;

const string acknowledgeFlag = "--acknowledge-undocumented-api";
var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "status";
var recoveryPath = Path.Combine(Path.GetTempPath(), "BingLan.TaskbarSpike.recovery");
using var instanceGate = new Mutex(
    initiallyOwned: true,
    name: @"Local\BingLan.TaskbarSpike",
    createdNew: out var ownsInstanceGate);
if (!ownsInstanceGate)
{
    Console.Error.WriteLine("另一个 TaskbarSpike 会话仍在运行，拒绝并发修改任务栏。");
    return 65;
}

var backend = new WindowsTaskbarAppearanceBackend();
using var session = new TaskbarAppearanceSession(backend);

if (File.Exists(recoveryPath))
{
    if (HasCompetingCustomizer())
    {
        Console.Error.WriteLine("发现恢复检查点，但其他任务栏外观工具正在运行；保留检查点且不覆盖对方。");
        return 2;
    }

    var recovery = session.ForceRestore();
    Console.WriteLine($"恢复检查点：{recovery.Status} - {recovery.Detail}");
    if (recovery.Status == TaskbarSessionStatus.RestoreFailed)
    {
        return 2;
    }

    File.Delete(recoveryPath);
}

if (command == "restore")
{
    var result = session.ForceRestore();
    Console.WriteLine($"{result.Status}: {result.Detail}");
    return result.Status == TaskbarSessionStatus.RestoreFailed ? 2 : 0;
}

var compatibility = TaskbarCompatibility.Assess(
    Environment.OSVersion.Version,
    IsHighContrastEnabled(),
    NativeMethods.GetSystemMetrics(NativeMethods.SmRemoteSession) != 0,
    IsSystemTransparencyEnabled(),
    HasCompetingCustomizer());
var taskbars = backend.FindTaskbars();
var explorerVersion = FileVersionInfo.GetVersionInfo(
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
var appBarData = new NativeMethods.AppBarData
{
    Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.AppBarData>()
};
var appBarState = NativeMethods.SHAppBarMessage(NativeMethods.AbmGetState, ref appBarData);
var taskbarDpi = taskbars.Count > 0
    ? NativeMethods.GetDpiForWindow(taskbars[0])
    : NativeMethods.GetDpiForSystem();

Console.WriteLine($"系统：{Environment.OSVersion.VersionString}");
Console.WriteLine($"Explorer：{explorerVersion.FileVersion ?? "未知"}");
Console.WriteLine(
    $"显示环境：{NativeMethods.GetSystemMetrics(NativeMethods.SmMonitorCount)} 个显示器，" +
    $"任务栏 DPI {taskbarDpi} ({taskbarDpi * 100d / 96d:0}%)，" +
    $"自动隐藏{((appBarState & NativeMethods.AbsAutoHide) != 0 ? "开启" : "关闭")}");
Console.WriteLine($"任务栏 HWND：{taskbars.Count} ({string.Join(", ", taskbars.Select(x => $"0x{x:X}"))})");
Console.WriteLine($"探测门：{(compatibility.CanProbe ? "允许" : "拒绝")} - {compatibility.Detail}");
Console.WriteLine("说明：外部 API 无法读取 Explorer 当前材质，状态输出不声称知道当前透明度。");

if (command == "status")
{
    return compatibility.CanProbe ? 0 : 1;
}

if (command != "probe")
{
    Console.Error.WriteLine("用法：status | probe [5-60 秒] --acknowledge-undocumented-api | restore");
    return 64;
}

if (!compatibility.CanProbe)
{
    Console.Error.WriteLine("当前环境未通过探测门，保持系统默认。");
    return 3;
}

if (!args.Contains(acknowledgeFlag, StringComparer.Ordinal))
{
    Console.Error.WriteLine($"限时实验必须显式传入 {acknowledgeFlag}。");
    return 64;
}

var duration = ParseDuration(args);
var cancellation = new CancellationTokenSource();
var exitCode = 0;
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

File.WriteAllText(
    recoveryPath,
    $"pid={Environment.ProcessId}{Environment.NewLine}started={DateTimeOffset.Now:O}{Environment.NewLine}");

try
{
    var applied = session.ApplyTransparent();
    Console.WriteLine($"{applied.Status}: {applied.Detail}");
    if (applied.Status is not TaskbarSessionStatus.Applied and not TaskbarSessionStatus.AlreadyApplied)
    {
        exitCode = 4;
    }
    else
    {
        Console.WriteLine($"透明实验维持 {duration.TotalSeconds:0} 秒；按 Ctrl+C 可提前恢复。");
        try
        {
            var knownTaskbars = taskbars.ToHashSet();
            var probeClock = Stopwatch.StartNew();
            var reapplyUntil = TimeSpan.Zero;
            while (probeClock.Elapsed < duration)
            {
                var remaining = duration - probeClock.Elapsed;
                var delay = remaining < TimeSpan.FromMilliseconds(500)
                    ? remaining
                    : TimeSpan.FromMilliseconds(500);
                await Task.Delay(delay, cancellation.Token);

                var currentTaskbars = backend.FindTaskbars().ToHashSet();
                if (currentTaskbars.Count == 0)
                {
                    knownTaskbars.Clear();
                    continue;
                }

                if (knownTaskbars.SetEquals(currentTaskbars))
                {
                    if (probeClock.Elapsed > reapplyUntil)
                    {
                        continue;
                    }
                }
                else
                {
                    knownTaskbars = currentTaskbars;
                    reapplyUntil = probeClock.Elapsed + TimeSpan.FromSeconds(5);
                }

                var reapplied = session.ReapplyTransparent();
                Console.WriteLine($"任务栏重建：{reapplied.Status} - {reapplied.Detail}");
                if (reapplied.Status != TaskbarSessionStatus.Reapplied)
                {
                    exitCode = 6;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("收到取消请求，开始恢复。");
        }
    }
}
finally
{
    var restored = session.Restore();
    Console.WriteLine($"{restored.Status}: {restored.Detail}");
    if (restored.Status == TaskbarSessionStatus.RestoreFailed)
    {
        exitCode = 5;
    }
    else
    {
        File.Delete(recoveryPath);
    }
}

return exitCode;

static TimeSpan ParseDuration(string[] arguments)
{
    var value = arguments.Skip(1).FirstOrDefault(x => int.TryParse(x, out _));
    var seconds = int.TryParse(value, out var parsed) ? parsed : 15;
    return TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 60));
}

static bool IsHighContrastEnabled()
{
    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Accessibility\HighContrast");
    var flags = key?.GetValue("Flags")?.ToString();
    return int.TryParse(flags, out var parsed) && (parsed & 1) != 0;
}

static bool IsSystemTransparencyEnabled()
{
    using var key = Registry.CurrentUser.OpenSubKey(
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
    return key?.GetValue("EnableTransparency") is not int value || value != 0;
}

static bool HasCompetingCustomizer()
{
    var names = new[] { "TranslucentTB", "TaskbarX", "RoundedTB" };
    return names.Any(name => Process.GetProcessesByName(name).Length > 0);
}
