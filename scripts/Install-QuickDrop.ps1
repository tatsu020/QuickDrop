param(
    [switch]$NoStartup,
    [switch]$RestartExplorer,
    [switch]$AddFirewallRules,
    [switch]$NoElevate
)

$ErrorActionPreference = "Stop"
$installDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$appPath = Join-Path $installDir "QuickDrop.App.exe"
$cliPath = Join-Path $installDir "QuickDrop.Cli.exe"
$shellPath = Join-Path $installDir "QuickDrop.ShellExtension.dll"

foreach ($path in @($appPath, $cliPath, $shellPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required file was not found: $path"
    }
}

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

if ($AddFirewallRules -and -not (Test-IsAdmin) -and -not $NoElevate) {
    $arguments = [System.Collections.Generic.List[string]]::new()
    $arguments.Add("-NoProfile")
    $arguments.Add("-ExecutionPolicy")
    $arguments.Add("Bypass")
    $arguments.Add("-File")
    $arguments.Add("`"$PSCommandPath`"")
    Add-SwitchArgument -List $arguments -Name "-NoStartup" -Enabled:$NoStartup
    Add-SwitchArgument -List $arguments -Name "-RestartExplorer" -Enabled:$RestartExplorer
    Add-SwitchArgument -List $arguments -Name "-AddFirewallRules" -Enabled:$AddFirewallRules
    $arguments.Add("-NoElevate")

    $elevated = Start-Process -FilePath "powershell.exe" -ArgumentList $arguments.ToArray() -Verb RunAs -Wait -PassThru
    exit $elevated.ExitCode
}

New-Item -Path "HKCU:\Software\QuickDrop" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\QuickDrop" -Name "InstallDirectory" -Value $installDir
Set-ItemProperty -Path "HKCU:\Software\QuickDrop" -Name "CliPath" -Value $cliPath
Set-ItemProperty -Path "HKCU:\Software\QuickDrop" -Name "ShellExtensionPath" -Value $shellPath

$reg = Start-Process -FilePath "$env:SystemRoot\System32\regsvr32.exe" -ArgumentList @("/s", $shellPath) -Wait -PassThru
if ($reg.ExitCode -ne 0) {
    throw "regsvr32 failed with exit code $($reg.ExitCode)"
}

if (-not $NoStartup) {
    New-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Force | Out-Null
    Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "QuickDrop" -Value "`"$appPath`""
}

if ($AddFirewallRules) {
    if (Test-IsAdmin) {
        foreach ($name in @("QuickDrop TCP Receiver", "QuickDrop UDP Discovery")) {
            Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
        }
        New-NetFirewallRule -DisplayName "QuickDrop TCP Receiver" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 48947 -Program $appPath | Out-Null
        New-NetFirewallRule -DisplayName "QuickDrop UDP Discovery" -Direction Inbound -Action Allow -Protocol UDP -LocalPort 48948 -Program $appPath | Out-Null
    } else {
        Write-Warning "Firewall rules were skipped because this process is not elevated."
    }
}

Start-Process -FilePath $appPath

if ($RestartExplorer) {
    Stop-Process -Name explorer -Force
    Start-Process explorer.exe
}

Write-Host "QuickDrop was installed. The Explorer context menu entry is registered."
