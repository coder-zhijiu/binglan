using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using BingLan.WidgetHost.Wpf.Interop;

namespace BingLan.WidgetHost.Wpf;

internal static class WidgetBackdropController
{
    internal static WidgetBackdropResult Apply(
        WidgetWindow window,
        HwndSource source,
        WidgetBackdropPreference preference)
    {
        if (preference == WidgetBackdropPreference.Solid)
        {
            return ApplySolid(window, source, "用户选择纯色材质");
        }

        if (SystemParameters.HighContrast)
        {
            return ApplySolid(window, source, "系统高对比度已开启");
        }

        var compositionResult = NativeMethods.DwmIsCompositionEnabled(out var compositionEnabled);
        if (compositionResult < 0 || !compositionEnabled)
        {
            return ApplySolid(window, source, "桌面窗口合成不可用");
        }

        var transparentBlack = Color.FromArgb(0, 0, 0, 0);
        source.CompositionTarget.BackgroundColor = transparentBlack;
        var transparentBrush = new SolidColorBrush(transparentBlack);
        transparentBrush.Freeze();
        window.Background = transparentBrush;

        var borderColor = NativeMethods.DwmColorNone;
        _ = NativeMethods.DwmSetWindowAttribute(
            source.Handle,
            NativeMethods.DwmwaBorderColor,
            ref borderColor,
            Marshal.SizeOf<int>());

        if (preference == WidgetBackdropPreference.PoggetLike)
        {
            var noSystemBackdrop = NativeMethods.DwmSystemBackdropNone;
            var clearBackdropResult = NativeMethods.DwmSetWindowAttribute(
                source.Handle,
                NativeMethods.DwmwaSystemBackdropType,
                ref noSystemBackdrop,
                Marshal.SizeOf<int>());
            var clientOnly = new NativeMethods.Margins
            {
                Left = 1,
                Right = 1,
                Top = 1,
                Bottom = 1
            };
            var clientFrameResult = NativeMethods.DwmExtendFrameIntoClientArea(
                source.Handle,
                ref clientOnly);
            if (clearBackdropResult >= 0
                && clientFrameResult >= 0
                && TrySetAccent(source.Handle, NativeMethods.AccentEnableBlurBehind))
            {
                return new WidgetBackdropResult(
                    WidgetBackdropMode.AccentBlur,
                    "Pogget-like accent blur");
            }
        }

        _ = TrySetAccent(source.Handle, NativeMethods.AccentDisabled);
        var margins = new NativeMethods.Margins
        {
            Left = -1,
            Right = -1,
            Top = -1,
            Bottom = -1
        };
        var frameResult = NativeMethods.DwmExtendFrameIntoClientArea(source.Handle, ref margins);
        if (frameResult < 0)
        {
            return ApplySolid(window, source, $"DWM Frame 扩展失败：0x{frameResult:X8}");
        }

        var backdrop = NativeMethods.DwmSystemBackdropTransientWindow;
        var backdropResult = NativeMethods.DwmSetWindowAttribute(
            source.Handle,
            NativeMethods.DwmwaSystemBackdropType,
            ref backdrop,
            Marshal.SizeOf<int>());
        if (backdropResult < 0)
        {
            return ApplySolid(window, source, $"系统桌面材质不可用：0x{backdropResult:X8}");
        }

        return new WidgetBackdropResult(
            WidgetBackdropMode.SystemBackdrop,
            "Windows 11 transient backdrop");
    }

    private static WidgetBackdropResult ApplySolid(
        WidgetWindow window,
        HwndSource source,
        string detail)
    {
        _ = TrySetAccent(source.Handle, NativeMethods.AccentDisabled);
        var backdrop = NativeMethods.DwmSystemBackdropNone;
        _ = NativeMethods.DwmSetWindowAttribute(
            source.Handle,
            NativeMethods.DwmwaSystemBackdropType,
            ref backdrop,
            Marshal.SizeOf<int>());

        var margins = new NativeMethods.Margins();
        _ = NativeMethods.DwmExtendFrameIntoClientArea(source.Handle, ref margins);

        var color = window.SolidFallbackColor;
        source.CompositionTarget.BackgroundColor = color;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        window.Background = brush;

        return new WidgetBackdropResult(WidgetBackdropMode.SolidFallback, detail);
    }

    private static bool TrySetAccent(nint window, int state)
    {
        var policy = new NativeMethods.AccentPolicy
        {
            State = state,
            Flags = 0,
            GradientColor = state == NativeMethods.AccentDisabled ? 0u : 0x01000000u
        };
        var size = Marshal.SizeOf<NativeMethods.AccentPolicy>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, pointer, false);
            var data = new NativeMethods.WindowCompositionAttributeData
            {
                Attribute = NativeMethods.WcaAccentPolicy,
                Data = pointer,
                SizeOfData = (nuint)size
            };
            return NativeMethods.SetWindowCompositionAttribute(window, ref data);
        }
        catch (Exception exception) when (exception is DllNotFoundException
                                          or EntryPointNotFoundException
                                          or BadImageFormatException
                                          or MarshalDirectiveException
                                          or SEHException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }
}
