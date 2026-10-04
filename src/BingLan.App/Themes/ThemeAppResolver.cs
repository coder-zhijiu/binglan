using System.Diagnostics;
using System.IO;
using BingLan.App.Dock;
using BingLan.Core.Dock;
using BingLan.Core.Models;
using BingLan.Core.Themes;
using Microsoft.Win32;

namespace BingLan.App.Themes;

/// <summary>
/// Finds this machine's copy of an app named in a theme: packaged apps by app model ID,
/// desktop apps by executable name in running processes, Start menu shortcuts and the
/// App Paths registry. Only local information is read.
/// </summary>
internal sealed class ThemeAppResolver
{
    private readonly Dictionary<string, string> _executables = new(StringComparer.OrdinalIgnoreCase);

    private ThemeAppResolver()
    {
    }

    /// <summary>Builds the lookup; scans shortcuts, so call it off the UI thread.</summary>
    internal static ThemeAppResolver Create()
    {
        var resolver = new ThemeAppResolver();
        resolver.AddRunningProcesses();
        resolver.AddStartMenuShortcuts();
        resolver.AddWindowsDirectory();
        return resolver;
    }

    internal DockPinnedApp? Resolve(ThemeAppBinding binding, string? executableName)
    {
        if (binding.AppUserModelId is { } appUserModelId
            && executableName == binding.ExecutableName
            && DockAppResolver.AppsFolderItemExists(appUserModelId))
        {
            return new DockPinnedApp
            {
                DisplayName = DisplayName(binding, null),
                AppUserModelId = appUserModelId
            };
        }

        if (executableName is null)
        {
            return null;
        }

        var path = Find(executableName);
        return path is null
            ? null
            : new DockPinnedApp
            {
                DisplayName = executableName == binding.ExecutableName
                    ? DisplayName(binding, path)
                    : DockAppResolver.GetFileDescription(path) ?? Path.GetFileNameWithoutExtension(path),
                ExecutablePath = WindowGrouping.NormalizeExecutablePath(path)
            };
    }

    private static string DisplayName(ThemeAppBinding binding, string? path) =>
        binding.DisplayName.Length > 0
            ? binding.DisplayName
            : path is not null
                ? DockAppResolver.GetFileDescription(path) ?? Path.GetFileNameWithoutExtension(path)
                : AppSlotCatalog.GetName(binding.Slot);

    private string? Find(string executableName)
    {
        if (_executables.TryGetValue(executableName, out var known))
        {
            return known;
        }

        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = root.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{executableName}");
            if (key?.GetValue(null) is string value)
            {
                var path = value.Trim().Trim('"');
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }
        return null;
    }

    private void Add(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path)
            && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && !DockAppResolver.IsPackagedPath(path)
            && File.Exists(path))
        {
            _executables.TryAdd(Path.GetFileName(path), path);
        }
    }

    private void AddRunningProcesses()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    Add(process.MainModule?.FileName);
                }
                catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
                    or InvalidOperationException or NotSupportedException)
                {
                    // Protected or exiting processes are skipped.
                }
            }
        }
    }

    private void AddStartMenuShortcuts()
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return;
        }

        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            foreach (var folder in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                         Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
                     })
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                // Start menus contain localised junctions (for example "程序") that normal
                // users cannot open; skip them instead of failing the whole scan.
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
                };
                foreach (var shortcut in Directory.EnumerateFiles(folder, "*.lnk", options))
                {
                    try
                    {
                        Add((string)shell.CreateShortcut(shortcut).TargetPath);
                    }
                    catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException
                        or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                    {
                        // Broken shortcuts are skipped.
                    }
                }
            }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    private void AddWindowsDirectory()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Add(Path.Combine(windows, "explorer.exe"));
        Add(Path.Combine(windows, "System32", "notepad.exe"));
    }
}
