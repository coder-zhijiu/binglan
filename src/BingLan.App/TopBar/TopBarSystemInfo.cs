using System.Net.NetworkInformation;
using BingLan.App.Interop;
using BingLan.Core.Models;
using BingLan.Core.TopBar;
using Microsoft.Win32;

namespace BingLan.App.TopBar;

/// <summary>
/// Reads the read-only system facts the top bar shows: battery, volume, network and the
/// current keyboard layout. Every call is cheap enough to ride the shared sampling tick
/// or a background thread; no method keeps state or a timer of its own.
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

        // 128 and 255 mean the level is unknown; the classification flags do the rest.
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

    /// <summary>
    /// The taskbar-style badge of the foreground window's input method plus the layout's
    /// full display name for the hover tooltip. The badge re-reads cheaply enough for the
    /// 150 ms focus cadence: the IME mode is one bounded cross-process call.
    /// </summary>
    internal static (string Label, string FullName) ReadInputMethod()
    {
        // No classic foreground window (a console host, for example) falls back to this
        // thread's own layout, which is the same list Windows would show.
        var foreground = DockNativeMethods.GetForegroundWindow();
        var threadId = foreground == 0
            ? 0u
            : DockNativeMethods.GetWindowThreadProcessId(foreground, out _);
        var layout = TopBarNativeMethods.GetKeyboardLayout(threadId);
        if (layout == 0)
        {
            layout = TopBarNativeMethods.GetKeyboardLayout(0);
        }

        var conversionMode = ReadConversionMode(foreground);
        return (TopBarModuleRules.DescribeInputMethod(layout, conversionMode), LayoutName(layout));
    }

    private static int? ReadConversionMode(nint foreground)
    {
        if (foreground == 0)
        {
            return null;
        }

        var imeWindow = TopBarNativeMethods.ImmGetDefaultIMEWnd(foreground);
        if (imeWindow == 0)
        {
            return null;
        }

        // The IME window belongs to the foreground window's thread, so the call crosses
        // a process boundary: bound it and give up on a hung target instead of stalling
        // the caller.
        if (!TopBarNativeMethods.SendMessageTimeout(
                imeWindow,
                TopBarNativeMethods.WmImeControl,
                TopBarNativeMethods.ImcGetConversionMode,
                0,
                TopBarNativeMethods.SmtoAbortIfHung,
                ConversionModeTimeoutMilliseconds,
                out var mode))
        {
            return null;
        }

        return unchecked((int)mode);
    }

    private const uint ConversionModeTimeoutMilliseconds = 50;

    private static string LayoutName(nint layout)
    {
        if (layout == 0)
        {
            return LayoutFallback;
        }

        // The layout's registry subkey: an IME names it with the whole HKL (E0200804),
        // a standard layout with its high word (00000409). Try both spellings.
        var candidates = new[]
        {
            layout.ToString("X8"),
            ((layout >> 16) & 0xFFFF).ToString("X8"),
            (layout & 0xFFFF).ToString("X8")
        };
        foreach (var id in candidates.Distinct())
        {
            var text = Registry.GetValue(
                $"HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Keyboard Layouts\\{id}",
                "Layout Text",
                null) as string;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return LayoutFallback;
    }

    private const string LayoutFallback = "键盘";
}
