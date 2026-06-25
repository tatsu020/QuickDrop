using System.Diagnostics;
using QuickDrop.Core;

namespace QuickDrop.App;

public static class ExplorerActions
{
    public static void OpenDownloads(AppSettings? settings = null) =>
        OpenPath(QuickDropPaths.GetDownloadsDirectory(settings));

    public static void OpenLog()
    {
        QuickDropPaths.EnsureDirectories();
        if (!File.Exists(QuickDropPaths.LogPath))
        {
            File.WriteAllText(QuickDropPaths.LogPath, "");
        }

        OpenPath(QuickDropPaths.LogPath);
    }

    private static void OpenPath(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
