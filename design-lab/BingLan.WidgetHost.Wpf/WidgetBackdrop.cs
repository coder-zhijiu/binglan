namespace BingLan.WidgetHost.Wpf;

public enum WidgetBackdropPreference
{
    Auto,
    PoggetLike,
    Solid
}

public enum WidgetBackdropMode
{
    AccentBlur,
    SystemBackdrop,
    SolidFallback
}

public sealed record WidgetBackdropResult(
    WidgetBackdropMode Mode,
    string Detail);
