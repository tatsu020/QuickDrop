param(
    [switch]$RestartExplorer,
    [switch]$RemoveFirewallRules,
    [switch]$NoElevate
)

$ErrorActionPreference = "Stop"
$installDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$shellPath = Join-Path $installDir "QuickDrop.ShellExtension.dll"

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

if ($RemoveFirewallRules -and -not (Test-IsAdmin) -and -not $NoElevate) {
    $arguments = [System.Collections.Generic.List[string]]::new()
    $arguments.Add("-NoProfile")
    $arguments.Add("-ExecutionPolicy")
    $arguments.Add("Bypass")
    $arguments.Add("-File")
    $arguments.Add("`"$PSCommandPath`"")
    Add-SwitchArgument -List $arguments -Name "-RestartExplorer" -Enabled:$RestartExplorer
    Add-SwitchArgument -List $arguments -Name "-RemoveFirewallRules" -Enabled:$RemoveFirewallRules
    $arguments.Add("-NoElevate")

    $elevated = Start-Process -FilePath "powershell.exe" -ArgumentList $arguments.ToArray() -Verb RunAs -Wait -PassThru
    exit $elevated.ExitCode
}

Get-Process -Name "QuickDrop.App" -ErrorAction SilentlyContinue | Stop-Process -Force

if (Test-Path -LiteralPath $shellPath) {
    $reg = Start-Process -FilePath "$env:SystemRoot\System32\regsvr32.exe" -ArgumentList @("/u", "/s", $shellPath) -Wait -PassThru
    if ($reg.ExitCode -ne 0) {
        throw "regsvr32 uninstall failed with exit code $($reg.ExitCode)"
    }
}

Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "QuickDrop" -ErrorAction SilentlyContinue
Remove-Item -Path "HKCU:\Software\QuickDrop" -Recurse -Force -ErrorAction SilentlyContinue

if ($RemoveFirewallRules) {
    if (Test-IsAdmin) {
        foreach ($name in @("QuickDrop TCP Receiver", "QuickDrop UDP Discovery")) {
            Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
        }
    } else {
        Write-Warning "Firewall rules were not removed because this process is not elevated."
    }
}

if ($RestartExplorer) {
    Stop-Process -Name explorer -Force
    Start-Process explorer.exe
}

Write-Host "QuickDrop was uninstalled."
