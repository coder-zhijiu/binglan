using System.Net.NetworkInformation;
using BingLan.App.Interop;
using BingLan.Core.Models;
using BingLan.Core.TopBar;
using Microsoft.Win32;

namespace BingLan.App.TopBar;

/// <summary>
/// Reads the read-only system facts the top bar shows: battery, volume, network and the
/// current keyboard layout. Every call is cheap enough to ride the shared sampling tick
/// while the bar is visible; no method keeps state or a timer of its own.
/// </summary>
internal static class TopBarSystemInfo
{
    internal static (int Percent, TopBarBatteryStatus Status) ReadBattery()
    {
        var status = new TopBarNativeMethods.SystemPowerStatus();
        if (!TopBarNativeMethods.GetSystemPowerStatus(ref status))
        {
            return (-1, TopBarBatteryStatus.Unknown);
        }

        // 255 (no battery) and 128 (unknown) both read as no percentage.
        var percent = TopBarModuleRules.IsBatteryPercentKnown(status.BatteryLifePercent)
            ? status.BatteryLifePercent
            : -1;
        return (percent, TopBarModuleRules.ClassifyBattery(status.AcLineStatus, status.BatteryFlag));
    }

    internal static int? ReadVolumePercent()
    {
        var scalar = AudioEndpointVolumeInterop.ReadMasterVolumeScalar();
        return scalar is { } value ? TopBarModuleRules.VolumePercentFromScalar(value) : null;
    }

    internal static (bool Connected, string Label) ReadNetwork()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(candidate => candidate.NetworkInterfaceType != NetworkInterfaceType.Loopback
                    && candidate.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .Select(candidate => (candidate.OperationalStatus == OperationalStatus.Up,
                    candidate.NetworkInterfaceType
                        is NetworkInterfaceType.Wireless80211
                        or NetworkInterfaceType.Wman));
            return TopBarModuleRules.ClassifyNetwork(interfaces);
        }
        catch (NetworkInformationException)
        {
            return (false, "未连接");
        }
    }

    /// <summary>The display name of the keyboard layout of the foreground window.</summary>
    internal static string ReadInputMethod()
    {
        var foreground = TopBarNativeMethods.GetForegroundWindow();
        if (foreground == 0)
        {
            return LayoutFallback;
        }

        var threadId = TopBarNativeMethods.GetWindowThreadProcessId(foreground, out _);
        var layout = TopBarNativeMethods.GetKeyboardLayout(threadId);
        if (layout == 0)
        {
            return LayoutFallback;
        }

        var languageId = TopBarModuleRules.ExtractLanguageId(layout);
        var text = Registry.GetValue(
            "HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Keyboard Layouts",
            layout.ToString("X8"),
            null) as string;
        if (!string.IsNullOrWhiteSpace(text))
        {
            return ShortenLayoutName(text);
        }

        var language = Registry.GetValue(
            "HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Keyboard Layouts",
            languageId.ToString("X4"),
            null) as string;
        return string.IsNullOrWhiteSpace(language) ? LayoutFallback : ShortenLayoutName(language);

        string ShortenLayoutName(string name) => name.Length > 12 ? name[..12] : name;
    }

    private const string LayoutFallback = "键盘";
}
