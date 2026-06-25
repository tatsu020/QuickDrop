using System.Text;

namespace QuickDrop.Core;

public static class Log
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            QuickDropPaths.EnsureDirectories();
            var line = $"{DateTimeOffset.Now:O}\t{level}\t{message}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            lock (Gate)
            {
                File.AppendAllText(QuickDropPaths.LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never break Explorer invocation or background receiving.
        }
    }
}
