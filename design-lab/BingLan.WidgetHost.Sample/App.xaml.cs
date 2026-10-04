using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using BingLan.WidgetHost.Sample.Interop;
using BingLan.WidgetHost.Sample.Windows;

namespace BingLan.WidgetHost.Sample;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var captureOutput = e.Args switch
        {
            ["--capture", var output] => output,
            ["--capture-on-image", _, var output] => output,
            ["--capture-at", var output, _, _] => output,
            ["--capture-at-on-image", _, var output, _, _] => output,
            _ => null
        };
        var backdropImage = e.Args switch
        {
            ["--capture-on-image", var imagePath, _] => imagePath,
            ["--capture-at-on-image", var imagePath, _, _, _] => imagePath,
            _ => null
        };
        var capturePosition = e.Args switch
        {
            ["--capture-at", _, var left, var top] => new Point(
                double.Parse(left, CultureInfo.InvariantCulture),
                double.Parse(top, CultureInfo.InvariantCulture)),
            ["--capture-at-on-image", _, _, var left, var top] => new Point(
                double.Parse(left, CultureInfo.InvariantCulture),
                double.Parse(top, CultureInfo.InvariantCulture)),
            _ => (Point?)null
        };
        var useDesktopSizedBackdrop =
            e.Args is ["--capture-at-on-image", _, _, _, _];
        var interactiveQa = e.Args is ["--interactive-qa"];
        var workArea = SystemParameters.WorkArea;
        ReferenceBackdropWindow? backdrop = null;
        if (backdropImage is not null)
        {
            backdrop = new ReferenceBackdropWindow(backdropImage)
            {
                Left = useDesktopSizedBackdrop
                    ? SystemParameters.VirtualScreenLeft
                    : Math.Max(workArea.Left + 12, workArea.Right - 401 - 20),
                Top = useDesktopSizedBackdrop
                    ? SystemParameters.VirtualScreenTop
                    : workArea.Top + 20,
                Width = useDesktopSizedBackdrop
                    ? SystemParameters.PrimaryScreenWidth
                    : 401,
                Height = useDesktopSizedBackdrop
                    ? SystemParameters.PrimaryScreenHeight
                    : 492
            };
            backdrop.Show();
        }

        var fileBox = new FileBoxDemoWindow
        {
            Left = capturePosition?.X ?? backdrop?.Left + 24 ??
                   Math.Max(workArea.Left + 24, workArea.Right - 353 - 48),
            Top = capturePosition?.Y ?? backdrop?.Top + 24 ?? workArea.Top + 36,
            ShowInTaskbar = interactiveQa,
            Topmost = captureOutput is not null
        };
        fileBox.Show();

        if (captureOutput is not null)
        {
            fileBox.Activate();
            await Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ApplicationIdle);
            await Task.Delay(800);
            LiveWindowCapture.Save(fileBox, captureOutput, 12);
            fileBox.Topmost = false;
            fileBox.Close();
            backdrop?.Close();
            Shutdown();
            return;
        }

        var todo = new TodoDemoWindow
        {
            Left = Math.Max(workArea.Left + 24, fileBox.Left - 353 - 28),
            Top = fileBox.Top + 24,
            ShowInTaskbar = interactiveQa
        };
        todo.Show();
        ShutdownMode = ShutdownMode.OnLastWindowClose;
    }
}
