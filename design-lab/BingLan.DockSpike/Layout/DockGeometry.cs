namespace BingLan.DockSpike.Layout;

public enum DockEdge
{
    Left,
    Top,
    Right,
    Bottom
}

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool HasArea => Width > 0 && Height > 0;

    public bool Intersects(PixelRect other) =>
        Math.Max(Left, other.Left) < Math.Min(Right, other.Right)
        && Math.Max(Top, other.Top) < Math.Min(Bottom, other.Bottom);
}

public static class DockGeometry
{
    public static PixelRect Calculate(
        PixelRect monitorBounds,
        DockEdge edge,
        int thicknessPixels)
    {
        if (!monitorBounds.HasArea)
        {
            throw new ArgumentException("Monitor bounds must have a positive area.", nameof(monitorBounds));
        }

        var available = edge is DockEdge.Left or DockEdge.Right
            ? monitorBounds.Width
            : monitorBounds.Height;
        if (thicknessPixels <= 0 || thicknessPixels > available)
        {
            throw new ArgumentOutOfRangeException(
                nameof(thicknessPixels),
                thicknessPixels,
                "Dock thickness must be positive and fit within the selected monitor edge.");
        }

        return edge switch
        {
            DockEdge.Left => monitorBounds with { Right = monitorBounds.Left + thicknessPixels },
            DockEdge.Top => monitorBounds with { Bottom = monitorBounds.Top + thicknessPixels },
            DockEdge.Right => monitorBounds with { Left = monitorBounds.Right - thicknessPixels },
            DockEdge.Bottom => monitorBounds with { Top = monitorBounds.Bottom - thicknessPixels },
            _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, "Unknown dock edge.")
        };
    }
}

public static class PlacementResolver
{
    public static PixelRect EnsureVisible(
        PixelRect desired,
        IReadOnlyList<PixelRect> workAreas,
        PixelRect fallback,
        int minimumVisiblePixels = 64)
    {
        ArgumentNullException.ThrowIfNull(workAreas);
        if (!desired.HasArea)
        {
            throw new ArgumentException("Desired bounds must have a positive area.", nameof(desired));
        }
        if (minimumVisiblePixels <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumVisiblePixels),
                minimumVisiblePixels,
                "Minimum visible pixels must be positive.");
        }

        var validWorkAreas = workAreas.Where(area => area.HasArea).ToList();
        if (validWorkAreas.Any(area => HasMinimumVisibility(desired, area, minimumVisiblePixels)))
        {
            return desired;
        }

        PixelRect target;
        if (validWorkAreas.Count > 0)
        {
            target = validWorkAreas
                .Select((area, index) => new
                {
                    Area = area,
                    Index = index,
                    Distance = DistanceSquared(desired, area)
                })
                .OrderBy(candidate => candidate.Distance)
                .ThenBy(candidate => candidate.Index)
                .First()
                .Area;
        }
        else if (fallback.HasArea)
        {
            target = fallback;
        }
        else
        {
            throw new ArgumentException(
                "At least one work area or a valid fallback must have a positive area.",
                nameof(workAreas));
        }

        return ClampInside(desired, target);
    }

    private static bool HasMinimumVisibility(
        PixelRect desired,
        PixelRect workArea,
        int minimumVisiblePixels)
    {
        var visibleWidth = Math.Max(
            0,
            Math.Min(desired.Right, workArea.Right) - Math.Max(desired.Left, workArea.Left));
        var visibleHeight = Math.Max(
            0,
            Math.Min(desired.Bottom, workArea.Bottom) - Math.Max(desired.Top, workArea.Top));

        return visibleWidth >= Math.Min(minimumVisiblePixels, desired.Width)
            && visibleHeight >= Math.Min(minimumVisiblePixels, desired.Height);
    }

    private static PixelRect ClampInside(PixelRect desired, PixelRect target)
    {
        var width = Math.Min(desired.Width, target.Width);
        var height = Math.Min(desired.Height, target.Height);
        var left = Math.Clamp(desired.Left, target.Left, target.Right - width);
        var top = Math.Clamp(desired.Top, target.Top, target.Bottom - height);
        return new PixelRect(left, top, left + width, top + height);
    }

    private static long DistanceSquared(PixelRect first, PixelRect second)
    {
        var horizontal = first.Right < second.Left
            ? (long)second.Left - first.Right
            : second.Right < first.Left
                ? (long)first.Left - second.Right
                : 0L;
        var vertical = first.Bottom < second.Top
            ? (long)second.Top - first.Bottom
            : second.Bottom < first.Top
                ? (long)first.Top - second.Bottom
                : 0L;

        return (horizontal * horizontal) + (vertical * vertical);
    }
}
