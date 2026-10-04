using System.Diagnostics;
using System.IO;

namespace BingLan.App.Services;

/// <summary>
/// A short local record of when each startup step that touches Explorer began and ended,
/// so a desktop that hangs after logon can be traced to a step, plus actions that failed
/// unexpectedly. It stays on this machine and is trimmed when it grows past 64 KB.
/// </summary>
internal static class StartupLog
{
    private const long MaximumBytes = 64 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    internal static void Open(string dataDirectory) => _path = Path.Combine(dataDirectory, "startup.log");

    internal static void Write(string step)
    {
        if (_path is not { } path)
        {
            return;
        }

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  开机后 {Environment.TickCount64 / 1000} 秒  {step}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > MaximumBytes)
                {
                    File.Delete(path);
                }
                File.AppendAllText(path, line);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"启动日志写入失败：{exception.Message}");
            }
        }
    }
}
