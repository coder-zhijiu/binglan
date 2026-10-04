using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using BingLan.App.Interop;
using Color = System.Windows.Media.Color;

namespace BingLan.App.Windows;

internal static class WidgetBackdropController
{
    internal static WidgetBackdropResult Apply(WidgetWindowBase window, HwndSource source)
    {
        PrepareTransparentHost(window, source);

        var borderColor = NativeMethods.DwmColorNone;
        _ = NativeMethods.DwmSetWindowAttribute(
            source.Handle,
            NativeMethods.DwmwaBorderColor,
            ref borderColor,
            Marshal.SizeOf<int>());

        var noSystemBackdrop = NativeMethods.DwmSystemBackdropNone;
        _ = NativeMethods.DwmSetWindowAttribute(
            source.Handle,
            NativeMethods.DwmwaSystemBackdropType,
            ref noSystemBackdrop,
            Marshal.SizeOf<int>());
        var clientFrame = new NativeMethods.Margins();
        _ = NativeMethods.DwmExtendFrameIntoClientArea(source.Handle, ref clientFrame);

        var detail = AccessibilityThemeManager.IsHighContrastEnabled
            ? "系统高对比度已开启，使用不透明系统色回退"
            : "逐像素圆角半透明材质（已禁用矩形 WCA 背景）";
        return new WidgetBackdropResult(WidgetBackdropMode.LayeredTint, detail);
    }

    private static void PrepareTransparentHost(
        WidgetWindowBase window,
        HwndSource source)
    {
        var color = Color.FromArgb(0, 0, 0, 0);
        source.CompositionTarget.BackgroundColor = color;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        window.Background = brush;
    }

}
