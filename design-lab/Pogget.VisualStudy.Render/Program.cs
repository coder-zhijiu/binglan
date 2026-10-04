using System.IO;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pogget.VisualStudy;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var output = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "artifacts", "pogget-visual-clone.png"));

        var app = new App();
        app.InitializeComponent();
        var window = new VisualStudyWindow();
        window.Show();
        Pump();

        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using (var stream = File.Create(output))
        {
            encoder.Save(stream);
        }

        window.Close();
        Console.WriteLine(output);
        return new WindowInteropHelper(window).Handle == 0 ? 0 : 0;
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new DispatcherOperationCallback(value =>
            {
                ((DispatcherFrame)value!).Continue = false;
                return null;
            }),
            frame);
        Dispatcher.PushFrame(frame);
    }
}
