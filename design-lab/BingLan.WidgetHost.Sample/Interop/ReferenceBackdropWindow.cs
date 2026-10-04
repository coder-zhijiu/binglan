using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BingLan.WidgetHost.Sample.Interop;

internal sealed class ReferenceBackdropWindow : Window
{
    internal ReferenceBackdropWindow(string imagePath)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(Path.GetFullPath(imagePath));
        bitmap.EndInit();
        bitmap.Freeze();

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Width = 401;
        Height = 492;
        Background = new ImageBrush(bitmap)
        {
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Center
        };
    }
}
