using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using BingLan.App.Interop;
using BingLan.App.Taskbar;
using BingLan.Core.Models;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        SetProcessDpiAwarenessContext(new nint(-4));
        if (args.Length < 2 || !Path.IsPathFullyQualified(args[1]))
        {
            Console.Error.WriteLine("Usage: probe|hold|cycle|restore|capture <absolute evidence directory>");
            return 2;
        }
        var directory = args[1];
        Directory.CreateDirectory(directory);
        var checkpoint = Path.Combine(directory, "taskbar-recovery.json");
        if (args[0] == "capture") { Capture(Path.Combine(directory, "capture.png")); return 0; }
        if (args[0] == "restore")
        {
            var success = TaskbarAdapter.RecoverFromCheckpoint(checkpoint);
            Capture(Path.Combine(directory, "recovered.png"));
            Console.WriteLine($"Recovered={success}");
            return success ? 0 : 1;
        }
        if (args[0] is not ("probe" or "hold" or "cycle")) return 2;
        var result = 0;
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        dispatcher.BeginInvoke(async () =>
        {
            using var adapter = new TaskbarAdapter(checkpoint);
            try
            {
                var environment = TaskbarAdapter.ReadEnvironment();
                Console.WriteLine($"Windows={Environment.OSVersion.Version}; Competing={environment.CompetingCustomizer ?? "none"}");
                Capture(Path.Combine(directory, "before.png"));
                adapter.Apply(TaskbarMode.Transparent);
                var wait = Stopwatch.StartNew();
                while (adapter.ActiveMode != TaskbarMode.Transparent && adapter.Problem.Length == 0 && wait.Elapsed.TotalSeconds < 15)
                    await Task.Delay(50);
                if (adapter.ActiveMode != TaskbarMode.Transparent) throw new InvalidOperationException(adapter.Problem);
                await Task.Delay(1000);
                Capture(Path.Combine(directory, "during.png"));
                Console.WriteLine($"READY pid={Environment.ProcessId} checkpoint={checkpoint}");
                if (args[0] == "hold") await Task.Delay(Timeout.InfiniteTimeSpan);
                await Task.Delay(3000);
                adapter.Apply(TaskbarMode.SystemDefault);
                wait.Restart();
                while (File.Exists(checkpoint) && wait.Elapsed.TotalSeconds < 5) await Task.Delay(50);
                if (File.Exists(checkpoint) || adapter.Problem.Length != 0) throw new InvalidOperationException("Recovery was not confirmed: " + adapter.Problem);
                Capture(Path.Combine(directory, "after.png"));
                Console.WriteLine("RESTORED");
                if (args[0] == "cycle")
                {
                    for (var iteration = 0; iteration < 3; iteration++)
                    {
                        adapter.Apply(TaskbarMode.Transparent);
                        await Task.Delay(50);
                        adapter.Apply(TaskbarMode.SystemDefault);
                        adapter.Apply(TaskbarMode.Transparent);
                        wait.Restart();
                        while (adapter.ActiveMode != TaskbarMode.Transparent && adapter.Problem.Length == 0 && wait.Elapsed.TotalSeconds < 15)
                            await Task.Delay(50);
                        if (adapter.ActiveMode != TaskbarMode.Transparent) throw new InvalidOperationException("Cycle failed: " + adapter.Problem);
                        adapter.Apply(TaskbarMode.SystemDefault);
                        wait.Restart();
                        while (File.Exists(checkpoint) && wait.Elapsed.TotalSeconds < 5) await Task.Delay(50);
                        if (File.Exists(checkpoint) || adapter.Problem.Length != 0) throw new InvalidOperationException("Cycle recovery failed: " + adapter.Problem);
                        Console.WriteLine($"CYCLE {iteration + 1} RESTORED");
                    }
                    Capture(Path.Combine(directory, "after-cycles.png"));
                }
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); result = 1; }
            finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
        });
        Dispatcher.Run();
        return result;
    }

    private static void Capture(string path)
    {
        var window = TaskbarNativeMethods.FindWindowW("Shell_TrayWnd", null);
        if (window == 0 || !GetWindowRect(window, out var bounds)) throw new InvalidOperationException("Taskbar window is unavailable");
        using var bitmap = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size);
        bitmap.Save(path, ImageFormat.Png);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(nint context);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out TaskbarNativeMethods.Rect rectangle);
}
