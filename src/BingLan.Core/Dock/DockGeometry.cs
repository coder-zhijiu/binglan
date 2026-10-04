namespace BingLan.Core.Dock;

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
