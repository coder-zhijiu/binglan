using System.Runtime.InteropServices;

namespace BingLan.WidgetHost.Wpf.Interop;

internal static class NativeMethods
{
    internal const int WmSettingChange = 0x001A;
    internal const int WmNcCalcSize = 0x0083;
    internal const int WmNcHitTest = 0x0084;
    internal const int WmThemeChanged = 0x031A;
    internal const int WmDwmCompositionChanged = 0x031E;

    internal const int HtClient = 1;
    internal const int HtCaption = 2;
    internal const int HtLeft = 10;
    internal const int HtRight = 11;
    internal const int HtTop = 12;
    internal const int HtTopLeft = 13;
    internal const int HtTopRight = 14;
    internal const int HtBottom = 15;
    internal const int HtBottomLeft = 16;
    internal const int HtBottomRight = 17;

    internal const int GwlStyle = -16;
    internal const int GwlExStyle = -20;

    internal const long WsThickFrame = 0x00040000L;
    internal const long WsExAcceptFiles = 0x00000010L;
    internal const long WsExToolWindow = 0x00000080L;
    internal const long WsExAppWindow = 0x00040000L;
    internal const long WsExLayered = 0x00080000L;
    internal const long WsExNoActivate = 0x08000000L;
    internal const long WsExTopmost = 0x00000008L;
    internal const long WsExTransparent = 0x00000020L;

    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpFrameChanged = 0x0020;

    internal const int DwmwaWindowCornerPreference = 33;
    internal const int DwmwaBorderColor = 34;
    internal const int DwmwaSystemBackdropType = 38;
    internal const int DwmWindowCornerDoNotRound = 1;
    internal const int DwmColorNone = unchecked((int)0xFFFFFFFE);
    internal const int DwmSystemBackdropNone = 1;
    internal const int DwmSystemBackdropTransientWindow = 3;
    internal const int WcaAccentPolicy = 19;
    internal const int AccentDisabled = 0;
    internal const int AccentEnableBlurBehind = 3;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Margins
    {
        internal int Left;
        internal int Right;
        internal int Top;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

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
        internal nuint SizeOfData;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern nint SetWindowLongPtr(nint window, int index, nint newValue);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(
        nint window,
        out Rect rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowRgn(
        nint window,
        nint region,
        [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateRoundRectRgn(
        int left,
        int top,
        int right,
        int bottom,
        int ellipseWidth,
        int ellipseHeight);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowCompositionAttribute(
        nint window,
        ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmIsCompositionEnabled(
        [MarshalAs(UnmanagedType.Bool)] out bool enabled);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmExtendFrameIntoClientArea(
        nint window,
        ref Margins margins);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(
        nint window,
        int attribute,
        ref int value,
        int valueSize);
}
