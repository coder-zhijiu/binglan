namespace BingLan.Core.Dock;

/// <summary>
/// Windows that asked for attention by flashing (a new chat message, a finished
/// download), until they are activated or closed. The dock marks their apps.
/// </summary>
public sealed class DockAttention
{
    private readonly HashSet<nint> _windows = [];

    public int Count => _windows.Count;

    /// <summary>Records a flash request; false when the window was already marked.</summary>
    public bool Flashed(nint window) => window != 0 && _windows.Add(window);

    /// <summary>The window was activated or closed; false when it was not marked.</summary>
    public bool Cleared(nint window) => _windows.Remove(window);

    /// <summary>Whether any of an app's windows is waiting for attention.</summary>
    public bool Wants(WindowGroup? group) =>
        group is not null && group.Windows.Any(window => _windows.Contains(window.Handle));

    /// <summary>Drops windows that no longer exist among the running ones.</summary>
    public void Retain(IEnumerable<nint> running)
    {
        var alive = running.ToHashSet();
        _windows.RemoveWhere(window => !alive.Contains(window));
    }
}
