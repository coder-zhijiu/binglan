using System.Runtime.InteropServices;
using System.Text;

namespace BingLan.TaskbarSpike.Interop;

internal static class NativeMethods
{
    internal const int WcaAccentPolicy = 19;
    internal const int AccentDisabled = 0;
    internal const int AccentEnableTransparentGradient = 2;
    internal const int SmRemoteSession = 0x1000;
    internal const int SmMonitorCount = 80;
    internal const uint AbmGetState = 4;
    internal const nuint AbsAutoHide = 1;

    internal delegate bool EnumWindowsProc(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassNameW(nint window, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowCompositionAttribute(
        nint window,
        ref WindowCompositionAttributeData data);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint window);

    [DllImport("shell32.dll")]
    internal static extern nuint SHAppBarMessage(uint message, ref AppBarData data);

    [StructLayout(LayoutKind.Sequential)]
    internal struct AccentPolicy
    {
        internal int State;
        internal int Flags;
        internal uint GradientColor;
        internal int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowCompositionAttributeData
    {
        internal int Attribute;
        internal nint Data;
        internal int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AppBarData
    {
        internal int Size;
        internal nint Window;
        internal uint CallbackMessage;
        internal uint Edge;
        internal Rect Rectangle;
        internal nint Parameter;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }
}
