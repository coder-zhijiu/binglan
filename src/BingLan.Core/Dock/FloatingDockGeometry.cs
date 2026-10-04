namespace BingLan.Core.Dock;

public static class FloatingDockGeometry
{
    public static int ClampExtent(
        int requestedLength,
        int stripExtent,
        double maxFraction,
        int minimumLength)
    {
        if (stripExtent <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stripExtent),
                stripExtent,
                "Strip extent must be positive.");
        }
        if (maxFraction <= 0 || maxFraction > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxFraction),
                maxFraction,
                "Max fraction must be within (0, 1].");
        }
        if (minimumLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumLength),
                minimumLength,
                "Minimum length must be positive.");
        }

        var maximum = Math.Max(
            minimumLength,
            Math.Min(stripExtent, (int)Math.Floor(stripExtent * maxFraction)));
        return Math.Clamp(requestedLength, minimumLength, maximum);
    }

    public static PixelRect Calculate(
        PixelRect strip,
        DockEdge edge,
        int contentLength,
        int thicknessPixels,
        int edgeGapPixels)
    {
        if (!strip.HasArea)
        {
            throw new ArgumentException("Strip bounds must have a positive area.", nameof(strip));
        }
        if (thicknessPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(thicknessPixels),
                thicknessPixels,
                "Dock thickness must be positive.");
        }
        if (edgeGapPixels < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(edgeGapPixels),
                edgeGapPixels,
                "Edge gap cannot be negative.");
        }

        return edge switch
        {
            DockEdge.Bottom => CenterHorizontal(
                strip,
                contentLength,
                strip.Bottom - edgeGapPixels - thicknessPixels,
                strip.Bottom - edgeGapPixels),
            DockEdge.Top => CenterHorizontal(
                strip,
                contentLength,
                strip.Top + edgeGapPixels,
                strip.Top + edgeGapPixels + thicknessPixels),
            DockEdge.Left => CenterVertical(
                strip,
                contentLength,
                strip.Left + edgeGapPixels,
                strip.Left + edgeGapPixels + thicknessPixels),
            DockEdge.Right => CenterVertical(
                strip,
                contentLength,
                strip.Right - edgeGapPixels - thicknessPixels,
                strip.Right - edgeGapPixels),
            _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, "Unknown dock edge.")
        };
    }

    private static PixelRect CenterHorizontal(PixelRect strip, int length, int top, int bottom)
    {
        var clamped = Math.Min(length, strip.Width);
        var left = strip.Left + ((strip.Width - clamped) / 2);
        return new PixelRect(left, top, left + clamped, bottom);
    }

    private static PixelRect CenterVertical(PixelRect strip, int length, int left, int right)
    {
        var clamped = Math.Min(length, strip.Height);
        var top = strip.Top + ((strip.Height - clamped) / 2);
        return new PixelRect(left, top, right, top + clamped);
    }
}
