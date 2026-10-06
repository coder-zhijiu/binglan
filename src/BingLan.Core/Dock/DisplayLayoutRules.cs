using BingLan.Core.Models;

namespace BingLan.Core.Dock;

/// <summary>
/// Remembers a card's bounds per arrangement of monitors. An arrangement is named by the
/// position, size and scaling of every monitor; device names are left out because Windows
/// may number the same screen differently after it is plugged in again.
/// </summary>
public static class DisplayLayoutRules
{
    /// <summary>Arrangements kept per card; the one used longest ago is dropped first.</summary>
    public const int MaximumRememberedLayouts = 6;

    public static string Key(IEnumerable<(PixelRect Bounds, uint Dpi)> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        return string.Join(
            ';',
            monitors
                .OrderBy(monitor => monitor.Bounds.Left)
                .ThenBy(monitor => monitor.Bounds.Top)
                .Select(monitor =>
                    $"{monitor.Bounds.Left},{monitor.Bounds.Top},{monitor.Bounds.Right},{monitor.Bounds.Bottom}@{monitor.Dpi}"));
    }

    public static void Remember(WindowPlacement placement, string layout, PixelRect bounds)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (string.IsNullOrEmpty(layout) || !bounds.HasArea)
        {
            return;
        }

        var layouts = placement.DisplayLayouts ??= [];
        layouts.RemoveAll(entry => entry is null || entry.Layout == layout);
        layouts.Add(new DisplayLayoutPlacement
        {
            Layout = layout,
            Left = bounds.Left,
            Top = bounds.Top,
            Right = bounds.Right,
            Bottom = bounds.Bottom
        });
        if (layouts.Count > MaximumRememberedLayouts)
        {
            layouts.RemoveRange(0, layouts.Count - MaximumRememberedLayouts);
        }
    }

    public static bool TryRecall(WindowPlacement placement, string layout, out PixelRect bounds)
    {
        ArgumentNullException.ThrowIfNull(placement);
        var entry = placement.DisplayLayouts?.LastOrDefault(candidate => candidate?.Layout == layout);
        bounds = entry is null
            ? default
            : new PixelRect(entry.Left, entry.Top, entry.Right, entry.Bottom);
        return entry is not null && bounds.HasArea;
    }
}
