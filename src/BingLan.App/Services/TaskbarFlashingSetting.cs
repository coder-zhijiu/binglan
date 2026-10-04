using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BingLan.App.Services;

/// <summary>
/// The Windows setting "Show flashing on taskbar apps". While it is on, an app asking for
/// attention slides an auto-hidden taskbar into view; while off, the taskbar stays hidden
/// and the dock still marks the app. Turning it on again removes the value, which is the
/// Windows default.
/// </summary>
internal static class TaskbarFlashingSetting
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ValueName = "TaskbarFlashing";
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;

    internal static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) is not int value || value != 0;
    }

    internal static void SetEnabled(bool enabled)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
        {
            if (enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(ValueName, 0, RegistryValueKind.DWord);
            }
        }

        // Explorer rereads taskbar settings on this broadcast. Reaching every window can
        // take a moment, so it runs in the background; a hung window cannot stall it.
        Task.Run(() => SendMessageTimeoutW(0xFFFF, WmSettingChange, 0, "TraySettings", SmtoAbortIfHung, 2000, out _));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeoutW(
        nint window,
        uint message,
        nint wParam,
        string lParam,
        uint flags,
        uint timeout,
        out nint result);
}
