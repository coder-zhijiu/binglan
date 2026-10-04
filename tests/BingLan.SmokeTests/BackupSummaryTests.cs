using System.IO;
using BingLan.Core.Models;
using BingLan.Core.Services;

/// <summary>The summary shown for each backup in the settings list.</summary>
internal static class BackupSummaryTests
{
    internal static void TestBackupIsDescribed()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"BingLan-backup-summary-{Guid.NewGuid():N}");
        try
        {
            var store = new LocalStateStore(temp);
            var state = store.Load();
            state.DesktopExperience.ActivePreset = DesktopLayoutPreset.GlacierWorkbench;
            state.NoteWidgets.Add(new NoteWidgetState());
            state.Dock.PinnedApps.Add(new DockPinnedApp { DisplayName = "浏览器", ExecutablePath = @"C:\Apps\browser.exe" });
            var backup = store.CreateBackup(state);

            var summary = LocalStateStore.DescribeBackup(backup);
            var visibleInformation = new[]
                {
                    DesktopComponentKind.TimeDate, DesktopComponentKind.Greeting,
                    DesktopComponentKind.Weather, DesktopComponentKind.Performance
                }
                .Count(kind => state.DesktopExperience.GetComponent(kind).IsVisible);
            var expectedCards = visibleInformation + state.TodoWidgets.Count + state.NoteWidgets.Count + state.FileBoxes.Count;
            Check(summary is { PresetName: "冰川工作台", DockAppCount: 1 } && summary.CardCount == expectedCards,
                "备份摘要应显示布局、卡片数和 Dock 应用数");

            var broken = Path.Combine(temp, "broken.json");
            File.WriteAllText(broken, "{ not json");
            Check(LocalStateStore.DescribeBackup(broken) is null, "无法读取的备份应返回空摘要");
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }

    internal static void TestNewCardTitlesAreUnique()
    {
        Check(WidgetTitleRules.NextTitle("今日待办", []) == "今日待办", "第一张卡片沿用默认标题");
        Check(WidgetTitleRules.NextTitle("今日待办", ["今日待办"]) == "今日待办 2", "重名时应编号");
        Check(WidgetTitleRules.NextTitle("桌面分组", ["桌面分组", "桌面分组 2", "工作"]) == "桌面分组 3",
            "应跳过已用的编号");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
