namespace BingLan.Core.Models;

public enum DockVisibilityMode
{
    ReserveWorkArea,
    SmartHide
}

public sealed class DockPinnedApp
{
    public string DisplayName { get; set; } = string.Empty;
    public string? ExecutablePath { get; set; }
    public string? AppUserModelId { get; set; }

    /// <summary>
    /// File name of a custom icon in the app's icon folder, or null for the app's own
    /// icon. Only a bare generated name is kept, never a path.
    /// </summary>
    public string? IconFile { get; set; }
}

public sealed class DockState
{
    public const double MinimumIconSize = 32d;
    public const double MaximumIconSize = 64d;
    public const double DefaultIconSize = 40d;
    public const string DefaultSurfaceColor = "#EAF5FC";
    public const double DefaultSurfaceOpacity = 0.84d;

    public bool IsEnabled { get; set; }
    public string? MonitorDeviceName { get; set; }
    public DockVisibilityMode VisibilityMode { get; set; } = DockVisibilityMode.ReserveWorkArea;
    public List<DockPinnedApp> PinnedApps { get; set; } = [];

    /// <summary>
    /// Apps whose running windows the dock leaves out, for windows that keep themselves
    /// off the Windows taskbar in ways the dock cannot detect.
    /// </summary>
    public List<DockPinnedApp> HiddenApps { get; set; } = [];

    /// <summary>Icon edge length in DIPs; the dock grows with it.</summary>
    public double IconSize { get; set; } = DefaultIconSize;

    /// <summary>Whether the dock takes its colours from the card material.</summary>
    public bool FollowCardLook { get; set; } = true;

    /// <summary>The dock's own colour, used when it does not follow the card material.</summary>
    public string SurfaceColor { get; set; } = DefaultSurfaceColor;

    /// <summary>The dock's own opacity from 0 to 1, used when it does not follow the cards.</summary>
    public double SurfaceOpacity { get; set; } = DefaultSurfaceOpacity;
}
