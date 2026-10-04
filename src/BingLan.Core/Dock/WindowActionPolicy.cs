namespace BingLan.Core.Dock;

public enum DockClickAction
{
    MinimizeForeground,
    Activate,
    RestoreAndActivate
}

public static class WindowActionPolicy
{
    public static DockClickAction Decide(bool groupContainsForeground, bool targetIsMinimized)
    {
        if (groupContainsForeground)
        {
            return DockClickAction.MinimizeForeground;
        }
        return targetIsMinimized
            ? DockClickAction.RestoreAndActivate
            : DockClickAction.Activate;
    }
}
