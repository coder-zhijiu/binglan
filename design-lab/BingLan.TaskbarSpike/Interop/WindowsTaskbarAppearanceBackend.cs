using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BingLan.TaskbarSpike.Interop;

public sealed class WindowsTaskbarAppearanceBackend : ITaskbarAppearanceBackend
{
    private static readonly HashSet<string> TaskbarClasses =
    [
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd"
    ];

    public IReadOnlyList<nint> FindTaskbars()
    {
        var result = new List<nint>();
        NativeMethods.EnumWindows((window, _) =>
        {
            var className = new StringBuilder(128);
            if (NativeMethods.GetClassNameW(window, className, className.Capacity) > 0 &&
                TaskbarClasses.Contains(className.ToString()) &&
                IsExplorerWindow(window))
            {
                result.Add(window);
            }

            return true;
        }, 0);
        return result;
    }

    private static bool IsExplorerWindow(nint window)
    {
        _ = NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public bool ApplyTransparent(out string detail) =>
        ApplyAccent(NativeMethods.AccentEnableTransparentGradient, "完全透明", out detail);

    public bool RestoreSystemDefault(out string detail) =>
        ApplyAccent(NativeMethods.AccentDisabled, "系统默认", out detail);

    private bool ApplyAccent(int accentState, string description, out string detail)
    {
        var taskbars = FindTaskbars();
        if (taskbars.Count == 0)
        {
            detail = "没有找到 Explorer 任务栏窗口";
            return false;
        }

        var failures = new List<string>();
        foreach (var taskbar in taskbars)
        {
            if (!NativeMethods.IsWindow(taskbar))
            {
                failures.Add($"0x{taskbar:X}: 窗口已经失效");
                continue;
            }

            if (!TrySetAccent(taskbar, accentState, out var error))
            {
                failures.Add($"0x{taskbar:X}: {error}");
            }
        }

        if (failures.Count > 0)
        {
            detail = $"应用{description}失败：{string.Join("；", failures)}";
            return false;
        }

        detail = $"已向 {taskbars.Count} 个任务栏 HWND 应用{description}";
        return true;
    }

    private static bool TrySetAccent(nint window, int accentState, out string error)
    {
        var policy = new NativeMethods.AccentPolicy
        {
            State = accentState,
            Flags = 0,
            GradientColor = 0,
            AnimationId = 0
        };
        var policyPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.AccentPolicy>());
        try
        {
            Marshal.StructureToPtr(policy, policyPointer, false);
            var data = new NativeMethods.WindowCompositionAttributeData
            {
                Attribute = NativeMethods.WcaAccentPolicy,
                Data = policyPointer,
                SizeOfData = Marshal.SizeOf<NativeMethods.AccentPolicy>()
            };

            if (NativeMethods.SetWindowCompositionAttribute(window, ref data))
            {
                error = string.Empty;
                return true;
            }

            error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(policyPointer);
        }
    }
}
