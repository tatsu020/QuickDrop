using System.Diagnostics;
using Microsoft.Win32;

namespace QuickDrop.App;

public static class ExplorerIntegrationInstaller
{
    public static string InstallFrom(string baseDirectory)
    {
        var cliPath = Path.Combine(baseDirectory, "QuickDrop.Cli.exe");
        var shellExtensionPath = Path.Combine(baseDirectory, "QuickDrop.ShellExtension.dll");

        if (!File.Exists(cliPath))
        {
            throw new FileNotFoundException("QuickDrop.Cli.exe が見つかりません。publish 後のフォルダから実行してください。", cliPath);
        }

        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\QuickDrop"))
        {
            key.SetValue("InstallDirectory", baseDirectory);
            key.SetValue("CliPath", cliPath);
            key.SetValue("ShellExtensionPath", shellExtensionPath);
        }

        if (File.Exists(shellExtensionPath))
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "regsvr32.exe"),
                Arguments = $"/s \"{shellExtensionPath}\"",
                UseShellExecute = true,
                Verb = "open",
                CreateNoWindow = true
            });
            process?.WaitForExit();
            return "Windows 11 用の右クリックメニューを登録しました。Explorer の再起動後に反映されます。";
        }

        RegisterFallbackPicker(cliPath);
        return "C++ Shell Extension が未配置のため、クラシックメニュー用フォールバックを登録しました。publish 後に再実行すると Windows 11 用メニューを登録できます。";
    }

    public static void RegisterFallbackPicker(string cliPath)
    {
        WriteFallbackKey(@"Software\Classes\*\shell\QuickDrop.Send", cliPath);
        WriteFallbackKey(@"Software\Classes\Directory\shell\QuickDrop.Send", cliPath);
    }

    private static void WriteFallbackKey(string keyPath, string cliPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue("MUIVerb", "ファイルを送信");
        key.SetValue("Icon", cliPath);
        using var command = key.CreateSubKey("command");
        command.SetValue("", $"\"{cliPath}\" pick-send \"%1\"");
    }
}
