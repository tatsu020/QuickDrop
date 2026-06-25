param(
    [switch]$NoStartup,
    [switch]$RestartExplorer,
    [switch]$AddFirewallRules,
    [switch]$NoElevate,
    [switch]$NoDialog
)

$ErrorActionPreference = "Stop"
$installDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$appPath = Join-Path $installDir "QuickDrop.App.exe"
$cliPath = Join-Path $installDir "QuickDrop.Cli.exe"
$shellPath = Join-Path $installDir "QuickDrop.ShellExtension.dll"
$logPath = Join-Path $installDir "QuickDrop.install.log"

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal] $identity
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Add-SwitchArgument {
    param(
        [System.Collections.Generic.List[string]]$List,
        [string]$Name,
        [switch]$Enabled
    )

    if ($Enabled) {
        $List.Add($Name)
    }
}

function Write-Step {
    param([string]$Message)

    $line = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $Message
    Write-Host $line
    try {
        Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
    } catch {
        Write-Host "Could not write install log: $($_.Exception.Message)"
    }
}

function Show-QuickDropMessage {
    param(
        [string]$Message,
        [string]$Title = "QuickDrop Installer",
        [string]$Icon = "Information"
    )

    if ($NoDialog) {
        Write-Host $Message
        return
    }

    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        $iconValue = [System.Windows.Forms.MessageBoxIcon]::Information
        try {
            $iconValue = [System.Enum]::Parse([System.Windows.Forms.MessageBoxIcon], $Icon, $true)
        } catch {
            $iconValue = [System.Windows.Forms.MessageBoxIcon]::Information
        }
        [System.Windows.Forms.MessageBox]::Show(
            $Message,
            $Title,
            [System.Windows.Forms.MessageBoxButtons]::OK,
            $iconValue
        ) | Out-Null
    } catch {
        Write-Host $Message
    }
}

trap {
    $message = $_.Exception.Message
    Write-Step "ERROR: $message"
    Show-QuickDropMessage -Title "QuickDrop Install Failed" -Icon "Error" -Message "QuickDrop install failed.`r`n`r`n$message`r`n`r`nLog: $logPath"
    exit 1
}

Write-Step "Starting QuickDrop install."
Write-Step "Install directory: $installDir"

foreach ($path in @($appPath, $cliPath, $shellPath)) {
    Write-Step "Checking required file: $path"
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required file was not found: $path"
    }
}

if ($AddFirewallRules -and -not (Test-IsAdmin) -and -not $NoElevate) {
    Write-Step "Administrator permission is required for firewall rules."
    Write-Step "Showing Windows UAC prompt. Please approve it to continue."
    $arguments = [System.Collections.Generic.List[string]]::new()
    $arguments.Add("-NoProfile")
    $arguments.Add("-ExecutionPolicy")
    $arguments.Add("Bypass")
    $arguments.Add("-File")
    $arguments.Add("`"$PSCommandPath`"")
    Add-SwitchArgument -List $arguments -Name "-NoStartup" -Enabled:$NoStartup
    Add-SwitchArgument -List $arguments -Name "-RestartExplorer" -Enabled:$RestartExplorer
    Add-SwitchArgument -List $arguments -Name "-AddFirewallRules" -Enabled:$AddFirewallRules
    Add-SwitchArgument -List $arguments -Name "-NoDialog" -Enabled:$NoDialog
    $arguments.Add("-NoElevate")

    $elevated = Start-Process -FilePath "powershell.exe" -ArgumentList $arguments.ToArray() -Verb RunAs -Wait -PassThru
    Write-Step "Elevated installer exited with code $($elevated.ExitCode)."
    exit $elevated.ExitCode
}

Write-Step "Writing QuickDrop registry settings."
New-Item -Path "HKCU:\Software\QuickDrop" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\QuickDrop" -Name "InstallDirectory" -Value $installDir
Set-ItemProperty -Path "HKCU:\Software\QuickDrop" -Name "CliPath" -Value $cliPath
Set-ItemProperty -Path "HKCU:\Software\QuickDrop" -Name "ShellExtensionPath" -Value $shellPath

Write-Step "Registering Explorer context menu extension."
$reg = Start-Process -FilePath "$env:SystemRoot\System32\regsvr32.exe" -ArgumentList @("/s", $shellPath) -Wait -PassThru
if ($reg.ExitCode -ne 0) {
    throw "regsvr32 failed with exit code $($reg.ExitCode)"
}

if (-not $NoStartup) {
    Write-Step "Enabling startup at Windows sign-in."
    New-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Force | Out-Null
    Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "QuickDrop" -Value "`"$appPath`""
} else {
    Write-Step "Skipping startup registration."
}

if ($AddFirewallRules) {
    if (Test-IsAdmin) {
        Write-Step "Adding Windows Firewall rules."
        foreach ($name in @("QuickDrop TCP Receiver", "QuickDrop UDP Discovery")) {
            Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
        }
        New-NetFirewallRule -DisplayName "QuickDrop TCP Receiver" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 48947 -Program $appPath | Out-Null
        New-NetFirewallRule -DisplayName "QuickDrop UDP Discovery" -Direction Inbound -Action Allow -Protocol UDP -LocalPort 48948 -Program $appPath | Out-Null
    } else {
        Write-Step "Firewall rules were skipped because this process is not elevated."
    }
} else {
    Write-Step "Skipping Windows Firewall rules."
}

Write-Step "Starting QuickDrop tray app."
Start-Process -FilePath $appPath

if ($RestartExplorer) {
    Write-Step "Restarting Explorer so the context menu is refreshed."
    Stop-Process -Name explorer -Force
    Start-Process explorer.exe
} else {
    Write-Step "Skipping Explorer restart."
}

Write-Step "QuickDrop install completed."
Show-QuickDropMessage -Message "QuickDrop install completed.`r`n`r`nThe tray app has been started. If you do not see it, check the hidden tray icons area.`r`n`r`nLog: $logPath"
