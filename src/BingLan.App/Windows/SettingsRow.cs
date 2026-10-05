using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Brush = System.Windows.Media.Brush;

namespace BingLan.App.Windows;

/// <summary>
/// One line of the settings window: an optional icon, a title and a short description on
/// the left, and the control that changes the setting on the right. A control without its
/// own screen reader name is named after the row title.
/// </summary>
public sealed class SettingsRow : ContentControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingsRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingsRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(SettingsRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty GlyphBrushProperty = DependencyProperty.Register(
        nameof(GlyphBrush), typeof(Brush), typeof(SettingsRow), new PropertyMetadata(null));

    static SettingsRow()
    {
        FocusableProperty.OverrideMetadata(typeof(SettingsRow), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(SettingsRow), new FrameworkPropertyMetadata(false));
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Colour of the rounded tile behind the icon.</summary>
    public Brush? GlyphBrush
    {
        get => (Brush?)GetValue(GlyphBrushProperty);
        set => SetValue(GlyphBrushProperty, value);
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        if (newContent is FrameworkElement element
            && string.IsNullOrEmpty(AutomationProperties.GetName(element))
            && Header.Length > 0)
        {
            AutomationProperties.SetName(element, Header);
        }
    }
}

/// <summary>
/// Several settings rows in one rounded block, separated by thin lines. The template
/// clips the line above the first row, so each row can always draw the line above itself.
/// </summary>
public sealed class SettingsGroup : ItemsControl
{
    static SettingsGroup()
    {
        FocusableProperty.OverrideMetadata(typeof(SettingsGroup), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(SettingsGroup), new FrameworkPropertyMetadata(false));
    }
}
