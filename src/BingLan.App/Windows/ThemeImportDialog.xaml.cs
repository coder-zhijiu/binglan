using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using BingLan.Core.Themes;

namespace BingLan.App.Windows;

/// <summary>Shows the schematic preview, theme name and a short change summary before an
/// import is applied.</summary>
public partial class ThemeImportDialog : Window
{
    public ThemeImportDialog(ThemePackage package, byte[]? previewPng)
    {
        InitializeComponent();
        ArgumentNullException.ThrowIfNull(package);

        ThemeNameText.Text = $"“{package.Manifest.Name}”";
        if (previewPng is { Length: > 0 } && Decode(previewPng) is { } bitmap)
        {
            PreviewImage.Source = bitmap;
            PreviewImage.Visibility = Visibility.Visible;
            NoPreviewText.Visibility = Visibility.Collapsed;
        }
    }

    // A preview that will not decode is shown as no preview.
    private static BitmapImage? Decode(byte[] png)
    {
        try
        {
            var bitmap = new BitmapImage();
            using var stream = new MemoryStream(png);
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception exception) when (exception is FileFormatException or NotSupportedException
            or IOException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
