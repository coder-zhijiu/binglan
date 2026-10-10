using System.Runtime.InteropServices;

namespace BingLan.App.Interop;

/// <summary>
/// The top bar's own Win32 surface. The AppBar, shell-hook and window APIs it shares
/// with the dock stay in <see cref="DockNativeMethods"/>; only what no other module
/// uses lives here.
/// </summary>
internal static class TopBarNativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemPowerStatus
    {
        internal byte AcLineStatus;
        internal byte BatteryFlag;
        internal byte BatteryLifePercent;
        internal byte Reserved1;
        internal int BatteryLifeTime;
        internal int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemPowerStatus(ref SystemPowerStatus status);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint GetKeyboardLayout(uint idThread);

    internal const int LeftMouseButton = 0x01;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    /// <summary>
    /// Whether the button is down now or was pressed since the last call in this
    /// thread; a click that falls entirely between two polls still counts.
    /// </summary>
    internal static bool IsMouseButtonDown(int key)
    {
        var state = GetAsyncKeyState(key);
        return (state & 0x8000) != 0 || (state & 0x1) != 0;
    }
}
