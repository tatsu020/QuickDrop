using System.Diagnostics;
using Microsoft.Win32;

namespace QuickDrop.App;

public static class ExplorerIntegrationInstaller
{
    public static string InstallFrom(string baseDirectory)
    {
        var cliPath = Path.Combine(baseDirectory, "QuickDrop.Cli.exe");
        var shellExtensionPath = Path.Combine(baseDirectory, "QuickDrop.ShellExtension.dll");
        var installerPath = Path.Combine(baseDirectory, "Install-QuickDrop.ps1");

        if (!File.Exists(cliPath))
        {
            throw new FileNotFoundException("QuickDrop.Cli.exe が見つかりません。publish 後のフォルダから実行してください。", cliPath);
        }

        if (!File.Exists(shellExtensionPath))
        {
            throw new FileNotFoundException("QuickDrop.ShellExtension.dll が見つかりません。publish 後のフォルダから実行してください。", shellExtensionPath);
        }

        if (!File.Exists(installerPath))
        {
            throw new FileNotFoundException("Install-QuickDrop.ps1 が見つかりません。install フォルダから実行してください。", installerPath);
        }

        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\QuickDrop"))
        {
            key.SetValue("InstallDirectory", baseDirectory);
            key.SetValue("CliPath", cliPath);
            key.SetValue("ShellExtensionPath", shellExtensionPath);
        }

        var arguments = string.Join(" ", new[]
        {
            "-NoProfile",
            "-ExecutionPolicy Bypass",
            $"-File \"{installerPath}\"",
            "-NoStartup",
            "-RestartExplorer"
        });

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = arguments,
            UseShellExecute = true
        });

        return "Windows 11 用の右クリックメニュー登録を開始しました。完了後に QuickDrop と Explorer が再起動します。";
    }
}
