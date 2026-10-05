namespace BingLan.Core.Dock;

/// <summary>
/// Explorer windows that can be the foreground window while covering the whole monitor
/// without being a full-screen app: the desktop, the taskbars, and the Microsoft account
/// prompt, which can stay up invisible and screen-sized when its sign-in page never loads.
/// Treating them as full-screen would hide the dock and suspend taskbar corner reveal.
/// </summary>
public static class ShellSurfaceWindows
{
    public static bool IsShellSurface(string className) =>
        className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Shell_OOBEProxy";
}
