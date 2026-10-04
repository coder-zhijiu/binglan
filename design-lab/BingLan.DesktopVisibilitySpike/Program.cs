using System.Text.Json;
using BingLan.DesktopVisibilitySpike;

return Run(args);

static int Run(string[] args)
{
    var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "--status";
    if (args.Length > 1 || command is not ("--status" or "--hide" or "--show"))
    {
        PrintUsage();
        return 2;
    }

    try
    {
        return command switch
        {
            "--hide" => Hide(),
            "--show" => Show(),
            _ => Status(),
        };
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Desktop visibility spike failed: {exception.Message}");
        return 1;
    }
}

static int Status()
{
    var session = DesktopViewSession.Open();
    Print(session.Read());
    Console.WriteLine("No state was changed.");
    return 0;
}

// 开启协议（对应 DESKTOP-CLEAN-MODE-TECHNICAL-DESIGN §6.1，按真机结果修订）：
// 先落盘检查点，再直接按掩码切换 FWF_NOICONS；只有直接写入不生效时才尝试补前置位。
// 2026-08-07 真机结果：build 26200 桌面视图对 SetFolderViewOptions 返回 E_NOTIMPL，
// 但 FVO_VISTALAYOUT 下直接掩码写 FWF_NOICONS 生效，与微软“类似自定义定位效果”的说明一致。
static int Hide()
{
    var session = DesktopViewSession.Open();
    var before = session.Read();
    if (before.IconsHidden)
    {
        Print(before);
        Console.WriteLine("Desktop icons are already hidden; nothing to do.");
        return 0;
    }

    var checkpoint = new SpikeCheckpoint(
        Phase: "Prepared",
        OriginalIconsHidden: before.IconsHidden,
        OriginalCustomPositionEnabled: before.SupportsCustomPosition,
        AddedCustomPositionBySpike: false,
        CreatedAtUtc: DateTime.UtcNow);
    SaveCheckpoint(checkpoint);

    try
    {
        var after = session.SetIconsHidden(hidden: true);
        SaveCheckpoint(checkpoint with { Phase = "Applied" });
        Print(after);
        Console.WriteLine("Desktop icons hidden (direct masked write). Run --show to restore.");
        return 0;
    }
    catch (Exception directFailure)
    {
        Console.WriteLine($"Direct FWF_NOICONS write failed: {directFailure.Message}");
    }

    if (DesktopIconVisibilityRules.NeedsCustomPositionAddition(before.ViewOptions))
    {
        try
        {
            session.SetCustomPositionAdvertised(true);
            checkpoint = checkpoint with { AddedCustomPositionBySpike = true };
            SaveCheckpoint(checkpoint);
            var after = session.SetIconsHidden(hidden: true);
            SaveCheckpoint(checkpoint with { Phase = "Applied" });
            Print(after);
            Console.WriteLine("Desktop icons hidden (after FVO_CUSTOMPOSITION addition). Run --show to restore.");
            return 0;
        }
        catch (Exception fallbackFailure)
        {
            Console.WriteLine($"FVO_CUSTOMPOSITION fallback failed: {fallbackFailure.Message}");
        }
    }
    else
    {
        Console.WriteLine("FVO_CUSTOMPOSITION was already enabled; no additional safe fallback is available.");
    }

    // 两条路径都失败：按原始位回滚并删除检查点，报告“不支持”，保持 Windows 现状。
    try
    {
        var current = session.Read();
        if (current.IconsHidden != before.IconsHidden)
        {
            session.SetIconsHidden(before.IconsHidden);
        }
        if (checkpoint.AddedCustomPositionBySpike && session.Read().SupportsCustomPosition)
        {
            session.SetCustomPositionAdvertised(false);
        }
        DeleteCheckpoint();
    }
    catch
    {
        Console.Error.WriteLine("Rollback incomplete; checkpoint kept for --show recovery.");
    }
    Console.Error.WriteLine("This desktop view does not support clean mode; nothing was changed.");
    return 3;
}

// 恢复协议（§6.2）：逐位恢复本实验拥有的两个位，成功后删除检查点。幂等。
static int Show()
{
    var checkpoint = LoadCheckpoint();
    var session = DesktopViewSession.Open();
    var current = session.Read();

    if (checkpoint is null)
    {
        Print(current);
        Console.WriteLine("No recovery checkpoint exists; nothing was changed.");
        return 0;
    }

    var plan = DesktopIconVisibilityRules.PlanRestore(
        checkpoint.OriginalIconsHidden,
        checkpoint.AddedCustomPositionBySpike,
        current.FolderFlags);

    if (plan.RestoreIconsHidden)
    {
        session.SetIconsHidden(checkpoint.OriginalIconsHidden);
    }
    if (plan.RemoveCustomPosition)
    {
        session.SetCustomPositionAdvertised(false);
    }

    var after = session.Read();
    if (after.IconsHidden != checkpoint.OriginalIconsHidden)
    {
        throw new InvalidOperationException("Restore verification failed; checkpoint kept.");
    }

    DeleteCheckpoint();
    Print(after);
    Console.WriteLine("Original desktop icon state restored; checkpoint cleared.");
    return 0;
}

static void Print(object snapshot)
{
    var s = (dynamic)snapshot;
    Console.WriteLine($"Desktop HWND: 0x{(int)s.DesktopWindowHandle:X8}");
    Console.WriteLine($"Folder flags: 0x{(uint)s.FolderFlags:X8}");
    Console.WriteLine($"View options: 0x{(uint)s.ViewOptions:X8}");
    Console.WriteLine($"Custom position: {((bool)s.SupportsCustomPosition ? "supported" : "not advertised")}");
    Console.WriteLine($"Desktop icons: {((bool)s.IconsHidden ? "hidden" : "visible")}");
}

static string CheckpointPath()
{
    var directory = Path.Combine(Path.GetTempPath(), "BingLan-VisibilitySpike");
    Directory.CreateDirectory(directory);
    return Path.Combine(directory, "checkpoint.json");
}

static void SaveCheckpoint(SpikeCheckpoint checkpoint)
{
    var path = CheckpointPath();
    var temporary = path + ".tmp";
    File.WriteAllText(temporary, JsonSerializer.Serialize(checkpoint));
    File.Move(temporary, path, true);
}

static SpikeCheckpoint? LoadCheckpoint()
{
    var path = CheckpointPath();
    if (!File.Exists(path))
    {
        return null;
    }
    return JsonSerializer.Deserialize<SpikeCheckpoint>(File.ReadAllText(path));
}

static void DeleteCheckpoint()
{
    var path = CheckpointPath();
    if (File.Exists(path))
    {
        File.Delete(path);
    }
}

static void PrintUsage()
{
    Console.WriteLine("Usage: BingLan.DesktopVisibilitySpike [--status|--hide|--show]");
    Console.WriteLine("Default is --status, which is read-only.");
    Console.WriteLine("--hide persists a checkpoint first; --show restores from it and is idempotent.");
}

internal sealed record SpikeCheckpoint(
    string Phase,
    bool OriginalIconsHidden,
    bool OriginalCustomPositionEnabled,
    bool AddedCustomPositionBySpike,
    DateTime CreatedAtUtc);
