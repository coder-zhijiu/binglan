namespace BingLan.TaskbarSpike;

public sealed record TaskbarCompatibilityResult(bool CanProbe, string Detail);

public static class TaskbarCompatibility
{
    public static TaskbarCompatibilityResult Assess(
        Version windowsVersion,
        bool isHighContrast,
        bool isRemoteSession,
        bool isSystemTransparencyEnabled,
        bool hasCompetingCustomizer)
    {
        if (windowsVersion.Major < 10 || windowsVersion.Build < 22000)
        {
            return new(false, $"Windows Build {windowsVersion.Build} 不在 Windows 11 实验范围内");
        }

        if (isHighContrast)
        {
            return new(false, "Windows 对比度主题已开启");
        }

        if (isRemoteSession)
        {
            return new(false, "远程桌面会话不执行任务栏材质实验");
        }

        if (!isSystemTransparencyEnabled)
        {
            return new(false, "Windows 系统透明效果已关闭");
        }

        if (hasCompetingCustomizer)
        {
            return new(false, "检测到其他任务栏外观工具正在运行");
        }

        return new(
            true,
            $"Windows Build {windowsVersion.Build} 可执行非注入、限时透明实验；不代表生产支持");
    }
}
