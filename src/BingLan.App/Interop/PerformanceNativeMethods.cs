using System.Runtime.InteropServices;

namespace BingLan.App.Interop;

internal static class PerformanceNativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileTime64
    {
        internal uint Low;
        internal uint High;

        internal ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct MemoryStatusEx
    {
        internal uint Length;
        internal uint MemoryLoad;
        internal ulong TotalPhys;
        internal ulong AvailPhys;
        internal ulong TotalPageFile;
        internal ulong AvailPageFile;
        internal ulong TotalVirtual;
        internal ulong AvailVirtual;
        internal ulong AvailExtendedVirtual;

        internal static MemoryStatusEx Create() =>
            new() { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemTimes(
        out FileTime64 idleTime,
        out FileTime64 kernelTime,
        out FileTime64 userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
