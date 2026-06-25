namespace QuickDrop.Core;

public static class QuickDropPaths
{
    public static string AppDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), QuickDropConstants.ProductName);

    public static string TempDirectory => Path.Combine(AppDataDirectory, "Temp");

    public static string SettingsPath => Path.Combine(AppDataDirectory, QuickDropConstants.SettingsFileName);

    public static string PeersMenuPath => Path.Combine(AppDataDirectory, QuickDropConstants.MenuFileName);

    public static string LogPath => Path.Combine(AppDataDirectory, QuickDropConstants.LogFileName);

    public static string DownloadsDirectory
    {
        get
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userProfile, "Downloads");
        }
    }

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(TempDirectory);
        Directory.CreateDirectory(DownloadsDirectory);
    }
}
