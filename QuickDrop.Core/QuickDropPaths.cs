using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace QuickDrop.Core;

public static class QuickDropPaths
{
    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    public static string AppDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), QuickDropConstants.ProductName);

    public static string TempDirectory => Path.Combine(AppDataDirectory, "Temp");

    public static string SettingsPath => Path.Combine(AppDataDirectory, QuickDropConstants.SettingsFileName);

    public static string PeersMenuPath => Path.Combine(AppDataDirectory, QuickDropConstants.MenuFileName);

    public static string LogPath => Path.Combine(AppDataDirectory, QuickDropConstants.LogFileName);

    public static string DownloadsDirectory => GetDownloadsDirectory();

    public static string AutomaticDownloadsDirectory => ResolveAutomaticDownloadsDirectory();

    public static string GetDownloadsDirectory(AppSettings? settings = null) =>
        ResolveManualDownloadsDirectory(settings) ?? ResolveAutomaticDownloadsDirectory();

    public static void EnsureDirectories(AppSettings? settings = null)
    {
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(TempDirectory);
        Directory.CreateDirectory(GetDownloadsDirectory(settings));
    }

    private static string? ResolveManualDownloadsDirectory(AppSettings? settings)
    {
        if (settings is null || string.IsNullOrWhiteSpace(settings.DownloadDirectoryOverride))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.DownloadDirectoryOverride.Trim()));
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveAutomaticDownloadsDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            var knownFolder = TryGetKnownDownloadsFolder();
            if (!string.IsNullOrWhiteSpace(knownFolder))
            {
                return knownFolder;
            }

            var registryFolder = TryGetRegistryDownloadsFolder();
            if (!string.IsNullOrWhiteSpace(registryFolder))
            {
                return registryFolder;
            }
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, "Downloads");
    }

    private static string? TryGetKnownDownloadsFolder()
    {
        var folderId = DownloadsFolderId;
        var result = SHGetKnownFolderPath(ref folderId, 0, IntPtr.Zero, out var pathPointer);
        if (result != 0 || pathPointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(pathPointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathPointer);
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? TryGetRegistryDownloadsFolder()
    {
        var downloadsId = DownloadsFolderId.ToString("B").ToUpperInvariant();
        return TryReadRegistryFolder(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders", downloadsId) ??
            TryReadRegistryFolder(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders", "Downloads") ??
            TryReadRegistryFolder(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders", downloadsId) ??
            TryReadRegistryFolder(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders", "Downloads");
    }

    [SupportedOSPlatform("windows")]
    private static string? TryReadRegistryFolder(string subKeyName, string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKeyName);
            var value = key?.GetValue(valueName)?.ToString();
            return string.IsNullOrWhiteSpace(value)
                ? null
                : Path.GetFullPath(Environment.ExpandEnvironmentVariables(value));
        }
        catch
        {
            return null;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
