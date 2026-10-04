namespace BingLan.App.Windows;

public enum WidgetBackdropMode
{
    LayeredTint,
    SolidFallback
}

public sealed record WidgetBackdropResult(
    WidgetBackdropMode Mode,
    string Detail);
