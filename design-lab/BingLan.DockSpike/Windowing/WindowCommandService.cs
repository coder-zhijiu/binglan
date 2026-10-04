using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using BingLan.DockSpike.Interop;

namespace BingLan.DockSpike.Windowing;

internal sealed record WindowCommandResult(bool Succeeded, string Message);

public enum DockClickAction
{
    MinimizeForeground,
    Activate,
    RestoreAndActivate
}

public static class WindowActionPolicy
{
    public static DockClickAction Decide(bool groupContainsForeground, bool targetIsMinimized)
    {
        if (groupContainsForeground)
        {
            return DockClickAction.MinimizeForeground;
        }
        return targetIsMinimized
            ? DockClickAction.RestoreAndActivate
            : DockClickAction.Activate;
    }
}

internal sealed class WindowCommandService
{
    internal WindowCommandResult Toggle(WindowGroup group)
    {
        if (group.Windows.Count == 0)
        {
            return new WindowCommandResult(false, "窗口组为空");
        }

        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundInGroup = group.Windows.FirstOrDefault(window => window.Handle == foreground);
        var target = group.Windows.FirstOrDefault(window => !window.IsMinimized)
            ?? group.Windows[0];
        var action = WindowActionPolicy.Decide(
            foregroundInGroup is not null,
            NativeMethods.IsIconic(target.Handle));
        if (action == DockClickAction.MinimizeForeground)
        {
            NativeMethods.ShowWindowAsync(foregroundInGroup!.Handle, NativeMethods.SwMinimize);
            return new WindowCommandResult(true, $"已最小化 {group.DisplayName}");
        }

        if (action == DockClickAction.RestoreAndActivate)
        {
            NativeMethods.ShowWindowAsync(target.Handle, NativeMethods.SwRestore);
        }

        if (NativeMethods.SetForegroundWindow(target.Handle))
        {
            return new WindowCommandResult(true, $"已切换到 {group.DisplayName}");
        }

        Flash(target.Handle);
        return new WindowCommandResult(
            false,
            $"Windows 阻止了聚焦 {group.DisplayName}，已闪烁提醒");
    }

    internal WindowCommandResult Launch(PinnedApp pinned)
    {
        if (string.IsNullOrWhiteSpace(pinned.ExecutablePath) || !File.Exists(pinned.ExecutablePath))
        {
            return new WindowCommandResult(false, "该应用没有可直接启动的路径");
        }

        try
        {
            Process.Start(new ProcessStartInfo(pinned.ExecutablePath) { UseShellExecute = true });
            return new WindowCommandResult(true, $"已启动 {pinned.DisplayName}");
        }
        catch (Exception exception)
        {
            return new WindowCommandResult(false, $"启动失败：{exception.Message}");
        }
    }

    internal WindowCommandResult LaunchNew(WindowGroup group)
    {
        var executablePath = group.Windows
            .Select(window => window.ExecutablePath)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return new WindowCommandResult(false, "该应用没有可直接启动的路径");
        }

        try
        {
            Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true });
            return new WindowCommandResult(true, $"已请求新建 {group.DisplayName} 实例");
        }
        catch (Exception exception)
        {
            return new WindowCommandResult(false, $"启动失败：{exception.Message}");
        }
    }

    private static void Flash(nint window)
    {
        var info = new NativeMethods.FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.FlashWindowInfo>(),
            Window = window,
            Flags = 2,
            Count = 3,
            Timeout = 0
        };
        NativeMethods.FlashWindowEx(ref info);
    }
}
