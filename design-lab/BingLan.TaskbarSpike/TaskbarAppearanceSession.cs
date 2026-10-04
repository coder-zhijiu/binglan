namespace BingLan.TaskbarSpike;

public interface ITaskbarAppearanceBackend
{
    bool ApplyTransparent(out string detail);

    bool RestoreSystemDefault(out string detail);
}

public enum TaskbarSessionStatus
{
    Applied,
    Reapplied,
    AlreadyApplied,
    FailedAndRestored,
    Restored,
    AlreadyRestored,
    RestoreFailed
}

public sealed record TaskbarSessionResult(TaskbarSessionStatus Status, string Detail);

public sealed class TaskbarAppearanceSession(ITaskbarAppearanceBackend backend) : IDisposable
{
    private bool _isApplied;
    private bool _disposed;

    public TaskbarSessionResult ApplyTransparent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isApplied)
        {
            return new(TaskbarSessionStatus.AlreadyApplied, "透明策略已经应用");
        }

        if (backend.ApplyTransparent(out var applyDetail))
        {
            _isApplied = true;
            return new(TaskbarSessionStatus.Applied, applyDetail);
        }

        var restored = backend.RestoreSystemDefault(out var restoreDetail);
        return new(
            restored ? TaskbarSessionStatus.FailedAndRestored : TaskbarSessionStatus.RestoreFailed,
            $"{applyDetail}；{restoreDetail}");
    }

    public TaskbarSessionResult ReapplyTransparent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isApplied)
        {
            return new(TaskbarSessionStatus.AlreadyRestored, "当前会话没有持有任务栏外观");
        }

        if (backend.ApplyTransparent(out var applyDetail))
        {
            return new(TaskbarSessionStatus.Reapplied, applyDetail);
        }

        var restored = backend.RestoreSystemDefault(out var restoreDetail);
        _isApplied = !restored;
        return new(
            restored ? TaskbarSessionStatus.FailedAndRestored : TaskbarSessionStatus.RestoreFailed,
            $"{applyDetail}；{restoreDetail}");
    }

    public TaskbarSessionResult Restore()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_isApplied)
        {
            return new(TaskbarSessionStatus.AlreadyRestored, "当前会话没有持有任务栏外观");
        }

        if (!backend.RestoreSystemDefault(out var detail))
        {
            return new(TaskbarSessionStatus.RestoreFailed, detail);
        }

        _isApplied = false;
        return new(TaskbarSessionStatus.Restored, detail);
    }

    public TaskbarSessionResult ForceRestore()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!backend.RestoreSystemDefault(out var detail))
        {
            return new(TaskbarSessionStatus.RestoreFailed, detail);
        }

        _isApplied = false;
        return new(TaskbarSessionStatus.Restored, detail);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_isApplied)
        {
            _ = backend.RestoreSystemDefault(out _);
            _isApplied = false;
        }

        _disposed = true;
    }
}
