namespace BingLan.Core.Models;

public enum DesktopMode
{
    WindowsNative,
    IceBlueHybrid,
    AppleStyle
}

/// <summary>
/// The three desktop modes are presets over two independent settings, the ice-blue
/// dock and the native taskbar. Choosing a mode writes safe defaults to both; either
/// can still be changed on its own afterwards.
/// </summary>
public static class DesktopModeRules
{
    public static void Apply(DesktopMode mode, DockState dock, TaskbarState taskbar)
    {
        ArgumentNullException.ThrowIfNull(dock);
        ArgumentNullException.ThrowIfNull(taskbar);

        switch (mode)
        {
            case DesktopMode.IceBlueHybrid:
                dock.IsEnabled = true;
                dock.VisibilityMode = DockVisibilityMode.ReserveWorkArea;
                taskbar.Mode = TaskbarMode.Transparent;
                break;
            case DesktopMode.AppleStyle:
                dock.IsEnabled = true;
                dock.VisibilityMode = DockVisibilityMode.ReserveWorkArea;
                taskbar.Mode = TaskbarMode.SmartHide;
                break;
            default:
                dock.IsEnabled = false;
                taskbar.Mode = TaskbarMode.SystemDefault;
                break;
        }
    }

    /// <summary>The mode matching the current settings, or null when they were customised.</summary>
    public static DesktopMode? Detect(DockState dock, TaskbarState taskbar)
    {
        ArgumentNullException.ThrowIfNull(dock);
        ArgumentNullException.ThrowIfNull(taskbar);

        return (dock.IsEnabled, taskbar.Mode) switch
        {
            (false, TaskbarMode.SystemDefault) => DesktopMode.WindowsNative,
            (true, TaskbarMode.Transparent or TaskbarMode.Blur) => DesktopMode.IceBlueHybrid,
            (true, TaskbarMode.SmartHide) => DesktopMode.AppleStyle,
            _ => null
        };
    }

    public static string GetName(DesktopMode mode) => mode switch
    {
        DesktopMode.IceBlueHybrid => "冰蓝混合",
        DesktopMode.AppleStyle => "苹果式",
        _ => "Windows 原生"
    };
}
