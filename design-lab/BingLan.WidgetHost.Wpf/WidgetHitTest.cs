using System.Windows;

namespace BingLan.WidgetHost.Wpf;

public static class WidgetHitTest
{
    public static readonly DependencyProperty IsInteractiveProperty =
        DependencyProperty.RegisterAttached(
            "IsInteractive",
            typeof(bool),
            typeof(WidgetHitTest),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.Inherits));

    public static void SetIsInteractive(DependencyObject element, bool value) =>
        element.SetValue(IsInteractiveProperty, value);

    public static bool GetIsInteractive(DependencyObject element) =>
        (bool)element.GetValue(IsInteractiveProperty);
}
