using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using ComServiceProvider = Windows.Win32.System.Com.IServiceProvider;

namespace BingLan.DesktopVisibilitySpike;

internal sealed class DesktopViewSession
{
    private static readonly Guid ShellWindowsClassId = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid TopLevelBrowserServiceId = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    private readonly IFolderView2 _folderView;
    private readonly IFolderViewOptions _folderViewOptions;

    private DesktopViewSession(int desktopWindowHandle, IFolderView2 folderView, IFolderViewOptions folderViewOptions)
    {
        DesktopWindowHandle = desktopWindowHandle;
        _folderView = folderView;
        _folderViewOptions = folderViewOptions;
    }

    public int DesktopWindowHandle { get; }

    public static DesktopViewSession Open()
    {
        var shellWindowsType = Type.GetTypeFromCLSID(ShellWindowsClassId, throwOnError: true)
            ?? throw new InvalidOperationException("Windows Shell automation class is unavailable.");
        var shellWindows = (IShellWindows?)Activator.CreateInstance(shellWindowsType)
            ?? throw new InvalidOperationException("Windows Shell automation object could not be created.");

        object location = 0;
        object root = 0;
        var desktopDispatch = shellWindows.FindWindowSW(
            in location,
            in root,
            ShellWindowTypeConstants.SWC_DESKTOP,
            out var desktopWindowHandle,
            ShellWindowFindWindowOptions.SWFO_NEEDDISPATCH);

        if (desktopDispatch is not ComServiceProvider serviceProvider)
        {
            throw new InvalidOperationException("The desktop Shell view does not expose IServiceProvider.");
        }

        serviceProvider.QueryService(TopLevelBrowserServiceId, out IShellBrowser shellBrowser);
        shellBrowser.QueryActiveShellView(out var shellView);

        if (shellView is not IFolderView2 folderView)
        {
            throw new InvalidOperationException("The desktop Shell view does not expose IFolderView2.");
        }

        if (shellView is not IFolderViewOptions folderViewOptions)
        {
            throw new InvalidOperationException("The desktop Shell view does not expose IFolderViewOptions.");
        }

        return new DesktopViewSession(desktopWindowHandle, folderView, folderViewOptions);
    }

    public DesktopViewSnapshot Read()
    {
        _folderView.GetCurrentFolderFlags(out var folderFlags);
        _folderViewOptions.GetFolderViewOptions(out var viewOptions);
        return new DesktopViewSnapshot(DesktopWindowHandle, folderFlags, (uint)viewOptions);
    }

    /// <summary>仅按 FVO_CUSTOMPOSITION 掩码写入视图选项，并读回验证。</summary>
    public DesktopViewSnapshot SetCustomPositionAdvertised(bool advertised)
    {
        _folderViewOptions.SetFolderViewOptions(
            (FOLDERVIEWOPTIONS)DesktopIconVisibilityRules.CustomPositionMask,
            advertised
                ? (FOLDERVIEWOPTIONS)DesktopIconVisibilityRules.CustomPositionMask
                : 0);
        var after = Read();
        if (after.SupportsCustomPosition != advertised)
        {
            throw new InvalidOperationException(
                "Windows did not apply the requested FVO_CUSTOMPOSITION state.");
        }
        return after;
    }

    /// <summary>仅按 FWF_NOICONS 掩码写入文件夹标志，并读回验证。</summary>
    public DesktopViewSnapshot SetIconsHidden(bool hidden)
    {
        var before = Read();
        var requestedFlags = DesktopIconVisibilityRules.Apply(before.FolderFlags, hidden);
        _folderView.SetCurrentFolderFlags(
            DesktopIconVisibilityRules.NoIconsMask,
            requestedFlags);

        var after = Read();
        if (after.IconsHidden != hidden)
        {
            throw new InvalidOperationException(
                "Windows did not apply the requested desktop icon visibility state.");
        }

        return after;
    }
}

internal sealed record DesktopViewSnapshot(int DesktopWindowHandle, uint FolderFlags, uint ViewOptions)
{
    public bool IconsHidden => DesktopIconVisibilityRules.AreIconsHidden(FolderFlags);

    public bool SupportsCustomPosition =>
        DesktopIconVisibilityRules.HasCustomPosition(ViewOptions);
}
