using System.Text.Json;
using BingLan.Core.Dock;
using BingLan.Core.Models;

namespace BingLan.Core.Taskbar;

public sealed record TaskbarEnvironment(
    int WindowsBuild,
    bool IsHighContrast,
    bool IsRemoteSession,
    bool IsSystemTransparencyEnabled,
    string? CompetingCustomizer);

public sealed record TaskbarCompatibilityResult(bool IsSupported, string Reason);

public static class TaskbarCompatibility
{
    public const int MinimumWindows11Build = 22000;

    public static TaskbarCompatibilityResult Assess(TaskbarMode mode, TaskbarEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (mode == TaskbarMode.SystemDefault)
        {
            return new(true, string.Empty);
        }

        if (environment.WindowsBuild < MinimumWindows11Build)
        {
            return new(false, "需要 Windows 11");
        }

        if (environment.CompetingCustomizer is { } customizer)
        {
            return new(false, $"{customizer} 正在调整任务栏，请先退出它");
        }

        if (mode == TaskbarMode.SmartHide)
        {
            return new(true, string.Empty);
        }

        if (environment.IsRemoteSession)
        {
            return new(false, "远程桌面中保持系统默认外观");
        }

        if (environment.IsHighContrast)
        {
            return new(false, "对比度主题开启时保持系统默认外观");
        }

        if (mode == TaskbarMode.Transparent && environment.WindowsBuild != 26200)
        {
            return new(false, "此 Windows 版本尚未验证任务栏透明，保持系统默认");
        }

        // Transparent and blur both depend on the system transparency effects.
        return environment.IsSystemTransparencyEnabled
            ? new(true, string.Empty)
            : new(false, "Windows 透明效果已关闭（设置 > 个性化 > 颜色）");
    }
}

/// <summary>
/// What the app changed on the native taskbar, written before the change so a later
/// start (or an uninstaller) can undo it after a crash. It never leaves this machine.
/// </summary>
public sealed class TaskbarCheckpoint
{
    public const int CurrentSchemaVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public bool TransparencyApplied { get; set; }
    public bool AutoHideApplied { get; set; }
    public bool OriginalAutoHide { get; set; }
    public string? XamlSessionName { get; set; }

    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    public static TaskbarCheckpoint? Parse(string json)
    {
        try
        {
            var checkpoint = JsonSerializer.Deserialize<TaskbarCheckpoint>(json, JsonOptions);
            return checkpoint is { SchemaVersion: >= 1 and <= CurrentSchemaVersion } ? checkpoint : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record TaskbarRestorePlan(bool ResetTransparency, bool? SetAutoHide);

public static class TaskbarRecovery
{
    /// <summary>
    /// Restores only what the app changed. Auto-hide goes back to the recorded value
    /// unless the user has already changed it again themselves.
    /// </summary>
    public static TaskbarRestorePlan Plan(TaskbarCheckpoint checkpoint, bool currentAutoHide)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        bool? autoHide = checkpoint.AutoHideApplied
            && currentAutoHide
            && !checkpoint.OriginalAutoHide
                ? false
                : null;
        return new TaskbarRestorePlan(checkpoint.TransparencyApplied, autoHide);
    }
}

/// <summary>
/// Where the strip that keeps an auto-hidden bottom taskbar from appearing in the middle
/// of the edge goes: two pixels high along the bottom of the monitor, leaving 15 % of the
/// width (at least 160 DIP) free at each end, where the taskbar still appears.
/// </summary>
public static class TaskbarCornerRules
{
    public const double CornerFraction = 0.15d;
    public const double MinimumCornerDip = 160d;
    public const int StripPixels = 2;

    public static PixelRect MiddleStrip(PixelRect monitor, uint dpi)
    {
        var corner = (int)Math.Round(Math.Max(monitor.Width * CornerFraction, MinimumCornerDip * dpi / 96d));
        var left = Math.Min(monitor.Left + corner, monitor.Left + monitor.Width / 2);
        var right = Math.Max(monitor.Right - corner, left);
        return new PixelRect(left, monitor.Bottom - StripPixels, right, monitor.Bottom);
    }
}

/// <summary>
/// Stops re-applying an appearance after repeated failures so a changed Explorer
/// cannot keep the adapter in a retry loop.
/// </summary>
public sealed class TaskbarFailureBreaker(int threshold = 3)
{
    public int ConsecutiveFailures { get; private set; }

    public bool IsTripped => ConsecutiveFailures >= threshold;

    public void RecordSuccess() => ConsecutiveFailures = 0;

    public bool RecordFailure()
    {
        ConsecutiveFailures++;
        return IsTripped;
    }

    public void Reset() => ConsecutiveFailures = 0;
}
