using System.Runtime.InteropServices;

namespace BingLan.App.Interop;

/// <summary>
/// Resolves Windows known-folder paths that have no <see cref="Environment.SpecialFolder"/>
/// entry, such as the per-user Downloads folder.
/// </summary>
internal static class KnownFolders
{
    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        in Guid folderId,
        uint flags,
        nint token,
        out nint path);

    public static string? GetDownloadsPath()
    {
        if (SHGetKnownFolderPath(DownloadsFolderId, 0, 0, out var pathPtr) != 0 || pathPtr == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(pathPtr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathPtr);
        }
    }
}
