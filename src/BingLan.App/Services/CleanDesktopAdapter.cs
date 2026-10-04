using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using BingLan.App.Interop;
using BingLan.Core.Desktop;

namespace BingLan.App.Services;

/// <summary>
/// Hides the native desktop icons for clean desktop and puts them back. It records the
/// original state in a checkpoint before the first write, restores on turn-off, on exit,
/// on the next start after a crash and from the uninstaller, and changes nothing but the
/// icon visibility flag: no files, icon positions or Explorer processes are touched.
/// All Shell calls run one at a time off the UI thread.
/// </summary>
internal sealed class CleanDesktopAdapter : IDisposable
{
    // Each check asks Explorer for its desktop view, so it runs rarely.
    private static readonly TimeSpan TakeoverCheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExitRestoreTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MachineLockTimeout = TimeSpan.FromSeconds(5);
    private static readonly SemaphoreSlim ShellGate = new(1, 1);

    // Shared with the --restore-taskbar process, so an uninstall or update restore never
    // writes the desktop view or the checkpoint at the same time as a running instance.
    private const string MachineLockName = @"Local\BingLan.DesktopIcons";

    private readonly string _checkpointPath;
    private readonly DispatcherTimer _takeoverTimer;
    private bool _checking;
    private bool _disposed;

    internal CleanDesktopAdapter(string checkpointPath)
    {
        _checkpointPath = checkpointPath;
        _takeoverTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TakeoverCheckInterval };
        _takeoverTimer.Tick += async (_, _) => await CheckTakeoverAsync();
    }

    /// <summary>Whether clean desktop currently keeps the icons hidden.</summary>
    internal bool IsActive { get; private set; }

    /// <summary>The icons were shown again by the user or another tool while active.</summary>
    internal event Action? TakenOver;

    /// <summary>
    /// Hides the icons. Returns an empty string on success, otherwise why not. When the
    /// state before a previous hide is unknown, icons that are already hidden are not
    /// taken over, since turning clean desktop off could then not bring them back.
    /// </summary>
    internal async Task<string> EnableAsync(bool originalStateKnown)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var problem = await RunShellAsync(
            () => WithMachineLock(() => Hide(_checkpointPath, originalStateKnown), "桌面图标正由另一个冰蓝桌面进程处理，请稍后再试"));
        IsActive = problem.Length == 0 && !_disposed;
        if (IsActive)
        {
            _takeoverTimer.Start();
        }
        return problem;
    }

    /// <summary>Puts the icons back as they were. Returns false when that could not be verified.</summary>
    internal async Task<bool> DisableAsync()
    {
        _takeoverTimer.Stop();
        IsActive = false;
        return await RunShellAsync(() => WithMachineLock(() => RecoverFromCheckpoint(_checkpointPath), false));
    }

    /// <summary>
    /// Restores the icon state left by a previous run, serialised with other Shell calls.
    /// Safe to repeat: with no checkpoint it changes nothing. An unreadable checkpoint is
    /// kept aside and the desktop is left as it is, since the original state is unknown.
    /// </summary>
    internal static bool RecoverNow(string checkpointPath)
    {
        ShellGate.Wait();
        try
        {
            return WithMachineLock(() => RecoverFromCheckpoint(checkpointPath), false);
        }
        finally
        {
            ShellGate.Release();
        }
    }

    private static T WithMachineLock<T>(Func<T> work, T busyResult)
    {
        using var machineLock = new Mutex(false, MachineLockName);
        bool acquired;
        try
        {
            acquired = machineLock.WaitOne(MachineLockTimeout);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        if (!acquired)
        {
            return busyResult;
        }

        try
        {
            return work();
        }
        finally
        {
            machineLock.ReleaseMutex();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _takeoverTimer.Stop();
        IsActive = false;
        // Exit must not hang on a busy Explorer; a restore that does not finish keeps
        // its checkpoint and runs again on the next start.
        try
        {
            var restore = Task.Run(() => RecoverNow(_checkpointPath));
            if (!restore.Wait(ExitRestoreTimeout))
            {
                Debug.WriteLine("退出时桌面图标恢复超时");
            }
        }
        catch (AggregateException exception)
        {
            Debug.WriteLine($"退出时桌面图标恢复失败：{exception.InnerException?.Message}");
        }
    }

    /// <summary>
    /// Whether the icon state from before clean desktop is known. A checkpoint that was
    /// set aside as unreadable means it is not, until the icons are seen visible again.
    /// </summary>
    internal static bool IsOriginalStateKnown(string checkpointPath) =>
        !File.Exists(InvalidCheckpointPath(checkpointPath));

    private static string InvalidCheckpointPath(string checkpointPath) => checkpointPath + ".invalid";

    private static bool RecoverFromCheckpoint(string checkpointPath)
    {
        CleanDesktopCheckpoint? checkpoint;
        try
        {
            // Read directly: only a file that is really absent means there is nothing
            // to restore; any other read failure is reported as a failed restore.
            checkpoint = CleanDesktopCheckpoint.Parse(File.ReadAllText(checkpointPath));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (checkpoint is null)
        {
            TryMove(checkpointPath, InvalidCheckpointPath(checkpointPath));
            return false;
        }

        try
        {
            checkpoint.Phase = CleanDesktopPhase.Restoring;
            WriteCheckpoint(checkpointPath, checkpoint);
            using var view = DesktopShellView.Open();
            if (CleanDesktopRules.NeedsRestore(checkpoint, view.ReadFolderFlags())
                && !view.SetIconsHidden(checkpoint.OriginalIconsHidden))
            {
                return false;
            }
            File.Delete(checkpointPath);
            return true;
        }
        catch (Exception exception) when (IsShellFailure(exception))
        {
            Debug.WriteLine($"桌面图标恢复失败：{exception.Message}");
            return false;
        }
    }

    private static string Hide(string checkpointPath, bool originalStateKnown)
    {
        try
        {
            using var view = DesktopShellView.Open();
            if (!CleanDesktopRules.NeedsHide(view.ReadFolderFlags()))
            {
                // Already hidden by the user: nothing of ours to restore later.
                return originalStateKnown
                    ? string.Empty
                    : "桌面图标当前处于隐藏状态，无法确认原来的设置。请先在桌面右键菜单“查看”中显示桌面图标，再开启清爽桌面";
            }

            var checkpoint = new CleanDesktopCheckpoint { OriginalIconsHidden = false };
            WriteCheckpoint(checkpointPath, checkpoint);
            // The icons were visible just now, so the original state is known again.
            TryDelete(InvalidCheckpointPath(checkpointPath));
            if (view.SetIconsHidden(true))
            {
                checkpoint.Phase = CleanDesktopPhase.Applied;
                WriteCheckpoint(checkpointPath, checkpoint);
                return string.Empty;
            }

            return RecoverFromCheckpoint(checkpointPath)
                ? "Windows 没有接受隐藏桌面图标"
                : "Windows 没有接受隐藏桌面图标，恢复未完成，下次启动时会再次恢复";
        }
        catch (Exception exception) when (IsShellFailure(exception))
        {
            Debug.WriteLine($"清爽桌面开启失败：{exception.Message}");
            RecoverFromCheckpoint(checkpointPath);
            return "当前桌面不支持清爽模式";
        }
    }

    private async Task CheckTakeoverAsync()
    {
        if (_checking || !IsActive)
        {
            return;
        }

        _checking = true;
        try
        {
            var iconsShowing = await RunShellAsync(() =>
            {
                try
                {
                    using var view = DesktopShellView.Open();
                    return CleanDesktopRules.IsTakenOver(true, view.ReadFolderFlags());
                }
                catch (Exception exception) when (IsShellFailure(exception))
                {
                    // Explorer may be restarting; the hidden state survives that, so wait.
                    return false;
                }
            });
            if (!iconsShowing || !IsActive)
            {
                return;
            }

            // The icons already show, which is the original state; drop the records.
            _takeoverTimer.Stop();
            IsActive = false;
            await RunShellAsync(() => WithMachineLock(() =>
            {
                TryDelete(_checkpointPath);
                TryDelete(InvalidCheckpointPath(_checkpointPath));
                return true;
            }, false));
            TakenOver?.Invoke();
        }
        finally
        {
            _checking = false;
        }
    }

    private static async Task<T> RunShellAsync<T>(Func<T> work)
    {
        await ShellGate.WaitAsync();
        try
        {
            return await Task.Run(work);
        }
        finally
        {
            ShellGate.Release();
        }
    }

    private static bool IsShellFailure(Exception exception) =>
        exception is COMException
            or InvalidCastException
            or UnauthorizedAccessException
            or IOException;

    private static void WriteCheckpoint(string path, CleanDesktopCheckpoint checkpoint)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, checkpoint.Serialize());
        File.Move(temporaryPath, path, true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryMove(string source, string destination)
    {
        try
        {
            File.Move(source, destination, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
