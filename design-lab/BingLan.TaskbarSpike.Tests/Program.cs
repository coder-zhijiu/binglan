using BingLan.TaskbarSpike;

var failures = new List<string>();
Run("兼容门拒绝高对比度和竞争工具", TestCompatibilityGate);
Run("透明应用幂等且正常恢复", TestApplyAndRestore);
Run("任务栏重建后重新应用透明", TestReapplyAfterTaskbarRebuild);
Run("重新应用失败立即恢复系统默认", TestReapplyFailureRestoresDefault);
Run("应用失败立即恢复系统默认", TestFailureRestoresDefault);
Run("恢复失败明确暴露", TestRestoreFailure);

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("全部 TaskbarSpike 纯逻辑烟雾测试通过。");
return 0;

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"通过：{name}");
    }
    catch (Exception exception)
    {
        failures.Add($"失败：{name} - {exception.Message}");
    }
}

static void TestCompatibilityGate()
{
    var supported = TaskbarCompatibility.Assess(
        new Version(10, 0, 26200), false, false, true, false);
    Assert(supported.CanProbe, "当前 Windows 11 实验 Build 应允许限时探测");

    Assert(
        !TaskbarCompatibility.Assess(new Version(10, 0, 26200), true, false, true, false).CanProbe,
        "高对比度下不应允许材质探测");
    Assert(
        !TaskbarCompatibility.Assess(new Version(10, 0, 26200), false, false, true, true).CanProbe,
        "检测到竞争工具时不应允许材质探测");
    Assert(
        !TaskbarCompatibility.Assess(new Version(10, 0, 19045), false, false, true, false).CanProbe,
        "Windows 10 不在本轮 Windows 11 实验范围内");
}

static void TestApplyAndRestore()
{
    var backend = new FakeBackend();
    using var session = new TaskbarAppearanceSession(backend);

    Assert(session.ApplyTransparent().Status == TaskbarSessionStatus.Applied, "首次应用状态错误");
    Assert(session.ApplyTransparent().Status == TaskbarSessionStatus.AlreadyApplied, "重复应用不幂等");
    Assert(backend.ApplyCount == 1, "重复应用调用了原生后端");
    Assert(session.Restore().Status == TaskbarSessionStatus.Restored, "恢复状态错误");
    Assert(session.Restore().Status == TaskbarSessionStatus.AlreadyRestored, "重复恢复不幂等");
    Assert(backend.RestoreCount == 1, "重复恢复调用了原生后端");
}

static void TestFailureRestoresDefault()
{
    var backend = new FakeBackend { ApplySucceeds = false };
    using var session = new TaskbarAppearanceSession(backend);
    var result = session.ApplyTransparent();

    Assert(result.Status == TaskbarSessionStatus.FailedAndRestored, "应用失败没有报告已恢复");
    Assert(backend.ApplyCount == 1 && backend.RestoreCount == 1, "应用失败没有立即恢复系统默认");
}

static void TestReapplyAfterTaskbarRebuild()
{
    var backend = new FakeBackend();
    using var session = new TaskbarAppearanceSession(backend);

    Assert(session.ApplyTransparent().Status == TaskbarSessionStatus.Applied, "测试前置应用失败");
    Assert(session.ReapplyTransparent().Status == TaskbarSessionStatus.Reapplied, "任务栏重建后没有重新应用");
    Assert(backend.ApplyCount == 2, "重新应用没有调用原生后端");
    Assert(session.Restore().Status == TaskbarSessionStatus.Restored, "重新应用后无法恢复");
}

static void TestReapplyFailureRestoresDefault()
{
    var backend = new FakeBackend();
    using var session = new TaskbarAppearanceSession(backend);

    Assert(session.ApplyTransparent().Status == TaskbarSessionStatus.Applied, "测试前置应用失败");
    backend.ApplySucceeds = false;
    Assert(
        session.ReapplyTransparent().Status == TaskbarSessionStatus.FailedAndRestored,
        "重新应用失败没有报告已恢复");
    Assert(backend.ApplyCount == 2 && backend.RestoreCount == 1, "重新应用失败没有立即恢复系统默认");
    Assert(session.Restore().Status == TaskbarSessionStatus.AlreadyRestored, "失败恢复后会话状态错误");
}

static void TestRestoreFailure()
{
    var backend = new FakeBackend { RestoreSucceeds = false };
    using var session = new TaskbarAppearanceSession(backend);
    Assert(session.ApplyTransparent().Status == TaskbarSessionStatus.Applied, "测试前置应用失败");
    Assert(session.Restore().Status == TaskbarSessionStatus.RestoreFailed, "恢复失败被静默吞掉");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

sealed class FakeBackend : ITaskbarAppearanceBackend
{
    public bool ApplySucceeds { get; set; } = true;
    public bool RestoreSucceeds { get; init; } = true;
    public int ApplyCount { get; private set; }
    public int RestoreCount { get; private set; }

    public bool ApplyTransparent(out string detail)
    {
        ApplyCount++;
        detail = ApplySucceeds ? "applied" : "apply failed";
        return ApplySucceeds;
    }

    public bool RestoreSystemDefault(out string detail)
    {
        RestoreCount++;
        detail = RestoreSucceeds ? "restored" : "restore failed";
        return RestoreSucceeds;
    }
}
