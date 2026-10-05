using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using BingLan.Core.Models;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace BingLan.App.Windows;

/// <summary>
/// A row of round colour swatches, one per card palette: the outer ring shows the title
/// bar colour and the centre the body colour, so the whole look is visible at a glance.
/// </summary>
internal static class PaletteSwatches
{
    private const double Size = 30d;

    internal static void Fill(WrapPanel panel, Action<CardPalette> chosen)
    {
        panel.Children.Clear();
        foreach (var palette in DesktopStyleRules.Palettes)
        {
            var button = new Button
            {
                Width = Size,
                Height = Size,
                // A window-wide button minimum must not stretch the circle into an oval.
                MinWidth = 0,
                MinHeight = 0,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(0),
                Tag = palette.Key,
                ToolTip = palette.Name,
                Cursor = System.Windows.Input.Cursors.Hand,
                Template = CreateTemplate(palette)
            };
            AutomationProperties.SetName(button, $"{palette.Name}配色");
            button.Click += (_, _) => chosen(palette);
            panel.Children.Add(button);
        }
    }

    /// <summary>Marks the swatch of the given palette; null marks none.</summary>
    internal static void Select(WrapPanel panel, string? key)
    {
        foreach (var button in panel.Children.OfType<Button>())
        {
            button.BorderBrush = Equals(button.Tag, key)
                ? (System.Windows.Media.Brush)button.FindResource("IceActionBrush")
                : System.Windows.Media.Brushes.Transparent;
        }
    }

    private static ControlTemplate CreateTemplate(CardPalette palette)
    {
        var selection = new FrameworkElementFactory(typeof(Border));
        selection.SetValue(Border.CornerRadiusProperty, new CornerRadius(Size / 2));
        selection.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        selection.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding(nameof(Button.BorderBrush))
        {
            RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
        });
        selection.SetValue(Border.PaddingProperty, new Thickness(2));

        var ring = new FrameworkElementFactory(typeof(Border));
        ring.SetValue(Border.CornerRadiusProperty, new CornerRadius(Size / 2));
        ring.SetValue(Border.BackgroundProperty, Brush(palette.Header));
        ring.SetValue(Border.PaddingProperty, new Thickness(5));
        selection.AppendChild(ring);

        var center = new FrameworkElementFactory(typeof(Border));
        center.SetValue(Border.CornerRadiusProperty, new CornerRadius(Size / 2));
        center.SetValue(Border.BackgroundProperty, Brush(palette.Body));
        center.SetValue(Border.BorderBrushProperty, Brush(palette.Text));
        center.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        ring.AppendChild(center);

        return new ControlTemplate(typeof(Button)) { VisualTree = selection };
    }

    private static SolidColorBrush Brush(string value)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)!);
        brush.Freeze();
        return brush;
    }
}
