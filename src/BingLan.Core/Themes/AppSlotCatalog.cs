namespace BingLan.Core.Themes;

/// <summary>
/// Semantic dock slots such as "browser" or "chat", with well-known apps for each. A
/// theme records slots so another machine can bind them to whatever it has installed.
/// </summary>
public static class AppSlotCatalog
{
    public const string OtherSlot = "other";

    /// <summary>Slots offered in the first-run guide, in display order.</summary>
    public static IReadOnlyList<string> StarterSlots { get; } = ["browser", "chat", "music", "cloud-drive"];

    private static readonly (string Slot, string Name, string[] Executables)[] Slots =
    [
        ("browser", "浏览器", ["chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe", "SogouExplorer.exe", "360se.exe", "360chrome.exe", "QQBrowser.exe", "vivaldi.exe"]),
        ("files", "文件", ["explorer.exe", "TotalCMD64.exe", "Files.exe"]),
        ("chat", "聊天", ["Weixin.exe", "WeChat.exe", "QQ.exe", "Telegram.exe", "DingTalk.exe", "Feishu.exe", "Lark.exe", "ms-teams.exe", "Discord.exe", "Slack.exe"]),
        ("mail", "邮件", ["olk.exe", "OUTLOOK.EXE", "Foxmail.exe", "thunderbird.exe"]),
        ("music", "音乐", ["cloudmusic.exe", "QQMusic.exe", "KuGou.exe", "Spotify.exe", "foobar2000.exe"]),
        ("editor", "编辑器", ["Code.exe", "notepad++.exe", "sublime_text.exe", "Notepad.exe", "notepad.exe"]),
        ("terminal", "终端", ["WindowsTerminal.exe", "wt.exe", "pwsh.exe"]),
        ("office", "办公", ["WINWORD.EXE", "EXCEL.EXE", "POWERPNT.EXE", "wps.exe"]),
        ("notes", "笔记", ["Obsidian.exe", "Notion.exe", "ONENOTE.EXE", "Typora.exe"]),
        ("cloud-drive", "网盘", ["BaiduNetdisk.exe", "OneDrive.exe", "AliyunDrive.exe", "Dropbox.exe"])
    ];

    public static string SlotFor(string? executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return OtherSlot;
        }

        foreach (var (slot, _, executables) in Slots)
        {
            if (executables.Contains(executableName, StringComparer.OrdinalIgnoreCase))
            {
                return slot;
            }
        }
        return OtherSlot;
    }

    public static IReadOnlyList<string> CandidatesFor(string slot) =>
        Slots.FirstOrDefault(entry => entry.Slot == slot).Executables ?? [];

    public static string GetName(string slot) =>
        Slots.FirstOrDefault(entry => entry.Slot == slot).Name ?? "应用";
}
