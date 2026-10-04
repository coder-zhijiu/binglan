using System.IO;

namespace BingLan.DockSpike.Windowing;

public enum DockItemState
{
    Pinned,
    Running,
    Active
}

public sealed record PinnedApp(string ExecutablePath)
{
    public string DisplayName
    {
        get
        {
            var name = Path.GetFileNameWithoutExtension(ExecutablePath.Trim().Trim('"'));
            return string.IsNullOrWhiteSpace(name) ? ExecutablePath : name;
        }
    }

    public string IdentityKey => $"exe:{WindowGrouping.NormalizeExecutablePath(ExecutablePath)}";
}

public sealed record DockItem(
    string Key,
    string DisplayName,
    PinnedApp? Pinned,
    WindowGroup? Group)
{
    public bool IsPinned => Pinned is not null;
    public bool IsRunning => Group is not null;
    public int WindowCount => Group?.Windows.Count ?? 0;
    public bool HasMultipleWindows => WindowCount > 1;
    public bool IsActive => Group?.Windows.Any(window => window.IsForeground) ?? false;

    public DockItemState State =>
        IsActive
            ? DockItemState.Active
            : IsRunning
                ? DockItemState.Running
                : DockItemState.Pinned;
}

public static class DockItemComposer
{
    public static IReadOnlyList<DockItem> Compose(
        IReadOnlyList<PinnedApp> pinnedApps,
        IReadOnlyList<WindowGroup> runningGroups)
    {
        ArgumentNullException.ThrowIfNull(pinnedApps);
        ArgumentNullException.ThrowIfNull(runningGroups);

        var items = new List<DockItem>();
        var matchedGroups = new HashSet<WindowGroup>();
        foreach (var pinned in pinnedApps)
        {
            var group = runningGroups.FirstOrDefault(
                candidate => !matchedGroups.Contains(candidate) && Matches(pinned, candidate));
            if (group is not null)
            {
                matchedGroups.Add(group);
                items.Add(new DockItem(group.Key, group.DisplayName, pinned, group));
            }
            else
            {
                items.Add(new DockItem(pinned.IdentityKey, pinned.DisplayName, pinned, null));
            }
        }

        foreach (var group in runningGroups)
        {
            if (!matchedGroups.Contains(group))
            {
                items.Add(new DockItem(group.Key, group.DisplayName, null, group));
            }
        }

        return items;
    }

    private static bool Matches(PinnedApp pinned, WindowGroup group)
    {
        if (string.Equals(group.Key, pinned.IdentityKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var pinnedPath = WindowGrouping.NormalizeExecutablePath(pinned.ExecutablePath);
        return group.Windows.Any(window =>
            !string.IsNullOrWhiteSpace(window.ExecutablePath)
            && string.Equals(
                WindowGrouping.NormalizeExecutablePath(window.ExecutablePath),
                pinnedPath,
                StringComparison.OrdinalIgnoreCase));
    }
}
