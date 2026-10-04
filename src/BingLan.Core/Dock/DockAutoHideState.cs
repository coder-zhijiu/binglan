namespace BingLan.Core.Dock;

public readonly record struct DockAutoHideInput(
    bool WindowOverlapsDock,
    bool ForegroundIsFullScreen,
    bool PointerInRevealZone,
    bool PointerOverDock,
    bool InteractionActive);

/// <summary>
/// Smart-hide decisions for the floating dock: it stays visible until a window
/// needs the space it covers, reappears when the pointer reaches the screen edge,
/// and never appears over a full-screen foreground window, even mid-interaction.
/// </summary>
public sealed class DockAutoHideState
{
    public static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(600);

    private DateTimeOffset? _hideRequestedAt;

    public bool IsShown { get; private set; } = true;

    public bool Update(DockAutoHideInput input, DateTimeOffset now)
    {
        if (input.ForegroundIsFullScreen)
        {
            return Hide();
        }

        if (input.InteractionActive
            || input.PointerOverDock || input.PointerInRevealZone || !input.WindowOverlapsDock)
        {
            return Show();
        }

        if (!IsShown)
        {
            return false;
        }

        _hideRequestedAt ??= now;
        return now - _hideRequestedAt.Value >= HideDelay && Hide();
    }

    private bool Show()
    {
        _hideRequestedAt = null;
        if (IsShown)
        {
            return false;
        }

        IsShown = true;
        return true;
    }

    private bool Hide()
    {
        _hideRequestedAt = null;
        if (!IsShown)
        {
            return false;
        }

        IsShown = false;
        return true;
    }
}
