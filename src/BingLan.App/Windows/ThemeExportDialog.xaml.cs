using System.Windows;

namespace BingLan.App.Windows;

/// <summary>Collects a theme name before the save-file dialog, and states in short text
/// what an exported theme package includes and excludes.</summary>
public partial class ThemeExportDialog : Window
{
    public ThemeExportDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ThemeNameEditor.SelectAll();
            ThemeNameEditor.Focus();
        };
    }

    public string ThemeName => string.IsNullOrWhiteSpace(ThemeNameEditor.Text)
        ? "我的冰蓝主题"
        : ThemeNameEditor.Text.Trim();

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
