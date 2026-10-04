namespace BingLan.DockSpike.Layout;

public static class DockLayoutMetrics
{
    public const double DockThicknessDip = 76;
    public const double EdgeGapDip = 28;
    public const double AxisPaddingDip = 14;
    public const double ItemCellDip = 64;
    public const double EmptyLengthDip = 76;
    public const double MaxExtentFraction = 0.65;

    public static double ContentLengthForItems(int itemCount)
    {
        if (itemCount <= 0)
        {
            return EmptyLengthDip;
        }

        return (2 * AxisPaddingDip) + (itemCount * ItemCellDip);
    }
}
