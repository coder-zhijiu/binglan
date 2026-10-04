using BingLan.DesktopVisibilitySpike;

var failures = new List<string>();

Check("visible flag is detected", !DesktopIconVisibilityRules.AreIconsHidden(0), failures);
Check(
    "hidden flag is detected",
    DesktopIconVisibilityRules.AreIconsHidden(DesktopIconVisibilityRules.NoIconsMask),
    failures);

const uint unrelatedFlags = 0xA5A50021;
var hiddenFlags = DesktopIconVisibilityRules.Apply(unrelatedFlags, hidden: true);
Check("hide sets only FWF_NOICONS", hiddenFlags == (unrelatedFlags | DesktopIconVisibilityRules.NoIconsMask), failures);
Check("hide is idempotent", DesktopIconVisibilityRules.Apply(hiddenFlags, hidden: true) == hiddenFlags, failures);

var visibleFlags = DesktopIconVisibilityRules.Apply(hiddenFlags, hidden: false);
Check("show clears only FWF_NOICONS", visibleFlags == unrelatedFlags, failures);
Check("show is idempotent", DesktopIconVisibilityRules.Apply(visibleFlags, hidden: false) == visibleFlags, failures);

Check(
    "custom position bit is detected",
    DesktopIconVisibilityRules.HasCustomPosition(DesktopIconVisibilityRules.CustomPositionMask | 0x1),
    failures);
Check(
    "vista layout alone requires addition",
    DesktopIconVisibilityRules.NeedsCustomPositionAddition(0x00000001),
    failures);
Check(
    "advertised custom position needs no addition",
    !DesktopIconVisibilityRules.NeedsCustomPositionAddition(DesktopIconVisibilityRules.CustomPositionMask),
    failures);

var restore = DesktopIconVisibilityRules.PlanRestore(
    originalIconsHidden: false,
    addedCustomPositionBySpike: true,
    currentFolderFlags: DesktopIconVisibilityRules.NoIconsMask);
Check("restore plan reverts hidden icons", restore.RestoreIconsHidden, failures);
Check("restore plan removes spike-added bit", restore.RemoveCustomPosition, failures);

var idempotentRestore = DesktopIconVisibilityRules.PlanRestore(
    originalIconsHidden: false,
    addedCustomPositionBySpike: false,
    currentFolderFlags: 0);
Check("restore plan is idempotent when state matches", !idempotentRestore.RestoreIconsHidden, failures);
Check("restore plan keeps user-owned custom position", !idempotentRestore.RemoveCustomPosition, failures);

var hiddenBaselineRestore = DesktopIconVisibilityRules.PlanRestore(
    originalIconsHidden: true,
    addedCustomPositionBySpike: false,
    currentFolderFlags: 0);
Check("restore plan preserves originally hidden icons", hiddenBaselineRestore.RestoreIconsHidden, failures);
Check("hidden baseline keeps user-owned custom position", !hiddenBaselineRestore.RemoveCustomPosition, failures);

if (failures.Count > 0)
{
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"FAIL: {failure}");
    }

    return 1;
}

Console.WriteLine("Desktop visibility rule checks passed: 15");
return 0;

static void Check(string name, bool condition, ICollection<string> failures)
{
    if (!condition)
    {
        failures.Add(name);
    }
}
