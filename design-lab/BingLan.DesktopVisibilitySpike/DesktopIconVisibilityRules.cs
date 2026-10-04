namespace BingLan.DesktopVisibilitySpike;

public static class DesktopIconVisibilityRules
{
    public const uint NoIconsMask = 0x00001000;
    public const uint CustomPositionMask = 0x00000002;

    public static bool AreIconsHidden(uint folderFlags)
    {
        return (folderFlags & NoIconsMask) != 0;
    }

    public static uint Apply(uint folderFlags, bool hidden)
    {
        return hidden
            ? folderFlags | NoIconsMask
            : folderFlags & ~NoIconsMask;
    }

    public static bool HasCustomPosition(uint viewOptions)
    {
        return (viewOptions & CustomPositionMask) != 0;
    }

    /// <summary>开启协议：仅当视图未公告自定义定位时才需要由实验临时添加该位。</summary>
    public static bool NeedsCustomPositionAddition(uint viewOptions)
    {
        return !HasCustomPosition(viewOptions);
    }

    /// <summary>恢复协议：仅恢复本实验拥有的两个位，其余位一律保持现状。</summary>
    public static (bool RestoreIconsHidden, bool RemoveCustomPosition) PlanRestore(
        bool originalIconsHidden,
        bool addedCustomPositionBySpike,
        uint currentFolderFlags)
    {
        var restoreNeeded = AreIconsHidden(currentFolderFlags) != originalIconsHidden;
        return (restoreNeeded, addedCustomPositionBySpike);
    }
}
