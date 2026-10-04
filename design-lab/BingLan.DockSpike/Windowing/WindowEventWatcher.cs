using System.Windows.Threading;
using BingLan.DockSpike.Interop;

namespace BingLan.DockSpike.Windowing;

internal sealed class WindowEventWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _debounceTimer;
    private readonly DispatcherTimer _reconcileTimer;
    private readonly NativeMethods.WinEventProc _callback;
    private readonly List<nint> _hooks = [];
    private bool _disposed;

    internal WindowEventWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _callback = OnWindowEvent;
        _debounceTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(90),
            DispatcherPriority.Background,
            (_, _) => Flush(),
            dispatcher);
        _debounceTimer.Stop();
        _reconcileTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(5),
            DispatcherPriority.Background,
            (_, _) => SnapshotInvalidated?.Invoke(),
            dispatcher);
        _reconcileTimer.Stop();
    }

    internal event Action? SnapshotInvalidated;

    internal int Start()
    {
        Hook(NativeMethods.EventSystemForeground, NativeMethods.EventSystemForeground);
        Hook(NativeMethods.EventSystemMinimizeStart, NativeMethods.EventSystemMinimizeEnd);
        Hook(NativeMethods.EventSystemDesktopSwitch, NativeMethods.EventSystemDesktopSwitch);
        Hook(NativeMethods.EventObjectCreate, NativeMethods.EventObjectHide);
        Hook(NativeMethods.EventObjectNameChange, NativeMethods.EventObjectNameChange);
        Hook(NativeMethods.EventObjectCloaked, NativeMethods.EventObjectUncloaked);
        _reconcileTimer.Start();
        return _hooks.Count;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _debounceTimer.Stop();
        _reconcileTimer.Stop();
        foreach (var hook in _hooks)
        {
            NativeMethods.UnhookWinEvent(hook);
        }
        _hooks.Clear();
    }

    private void Hook(uint eventMin, uint eventMax)
    {
        var hook = NativeMethods.SetWinEventHook(
            eventMin,
            eventMax,
            0,
            _callback,
            0,
            0,
            NativeMethods.WineventOutOfContext | NativeMethods.WineventSkipOwnProcess);
        if (hook != 0)
        {
            _hooks.Add(hook);
        }
    }

    private void OnWindowEvent(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (_disposed)
        {
            return;
        }
        var objectEvent = eventType >= NativeMethods.EventObjectCreate;
        if (objectEvent
            && (window == 0 || objectId != NativeMethods.ObjidWindow))
        {
            return;
        }

        _dispatcher.BeginInvoke(DispatcherPriority.Background, QueueRefresh);
    }

    private void QueueRefresh()
    {
        if (_disposed)
        {
            return;
        }
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void Flush()
    {
        _debounceTimer.Stop();
        SnapshotInvalidated?.Invoke();
    }
}
