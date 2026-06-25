param(
    [switch]$RestartExplorer,
    [switch]$RemoveFirewallRules,
    [switch]$NoElevate,
    [switch]$NoDialog
)

$ErrorActionPreference = "Stop"
$installDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$shellPath = Join-Path $installDir "QuickDrop.ShellExtension.dll"
$packageName = "Tatsu020.QuickDrop"
$packageCertPath = Join-Path $installDir "QuickDrop.Sparse.cer"
$shellExtensionClsid = "{9B75B6F7-8C63-4B52-A9E4-2CF777E83456}"
$logPath = Join-Path $installDir "QuickDrop.uninstall.log"

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
        Write-Host "Could not write uninstall log: $($_.Exception.Message)"
    }
}

function Show-QuickDropMessage {
    param(
        [string]$Message,
        [string]$Title = "QuickDrop Uninstaller",
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

function Remove-ClassicExplorerRegistration {
    Write-Step "Removing old classic Explorer context menu registration."
    $paths = @(
        "Registry::HKEY_CURRENT_USER\Software\Classes\CLSID\$shellExtensionClsid",
        "Registry::HKEY_CURRENT_USER\Software\Classes\*\shell\QuickDrop.Send",
        "Registry::HKEY_CURRENT_USER\Software\Classes\Directory\shell\QuickDrop.Send"
    )

    foreach ($path in $paths) {
        Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Remove-QuickDropIdentityPackage {
    $packages = Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue
    foreach ($package in $packages) {
        Write-Step "Removing QuickDrop identity package: $($package.PackageFullName)"
        Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop
    }
}

function Remove-QuickDropPackageCertificate {
    if (-not (Test-Path -LiteralPath $packageCertPath)) {
        return
    }

    try {
        $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($packageCertPath)
        $scopes = @("CurrentUser")
        if (Test-IsAdmin) {
            $scopes += "LocalMachine"
        }

        foreach ($scope in $scopes) {
            foreach ($store in @("TrustedPeople", "Root")) {
                $certStorePath = "Cert:\$scope\$store\$($certificate.Thumbprint)"
                if (Test-Path -LiteralPath $certStorePath) {
                    Write-Step "Removing QuickDrop sparse package certificate from $scope $store."
                    Remove-Item -LiteralPath $certStorePath -Force -ErrorAction SilentlyContinue
                }
            }
        }
    }
    catch {
        Write-Step "Could not inspect sparse package certificate: $($_.Exception.Message)"
    }
}

trap {
    $message = $_.Exception.Message
    Write-Step "ERROR: $message"
    Show-QuickDropMessage -Title "QuickDrop Uninstall Failed" -Icon "Error" -Message "QuickDrop uninstall failed.`r`n`r`n$message`r`n`r`nLog: $logPath"
    exit 1
}

Write-Step "Starting QuickDrop uninstall."
Write-Step "Install directory: $installDir"

if ($RemoveFirewallRules -and -not (Test-IsAdmin) -and -not $NoElevate) {
    Write-Step "Administrator permission is required to remove firewall rules."
    Write-Step "Showing Windows UAC prompt. Please approve it to continue."
    $arguments = [System.Collections.Generic.List[string]]::new()
    $arguments.Add("-NoProfile")
    $arguments.Add("-ExecutionPolicy")
    $arguments.Add("Bypass")
    $arguments.Add("-File")
    $arguments.Add("`"$PSCommandPath`"")
    Add-SwitchArgument -List $arguments -Name "-RestartExplorer" -Enabled:$RestartExplorer
    Add-SwitchArgument -List $arguments -Name "-RemoveFirewallRules" -Enabled:$RemoveFirewallRules
    Add-SwitchArgument -List $arguments -Name "-NoDialog" -Enabled:$NoDialog
    $arguments.Add("-NoElevate")

    $elevated = Start-Process -FilePath "powershell.exe" -ArgumentList $arguments.ToArray() -Verb RunAs -Wait -PassThru
    Write-Step "Elevated uninstaller exited with code $($elevated.ExitCode)."
    exit $elevated.ExitCode
}

Write-Step "Stopping QuickDrop tray app if it is running."
Get-Process -Name "QuickDrop.App" -ErrorAction SilentlyContinue | Stop-Process -Force

Remove-QuickDropIdentityPackage
Remove-ClassicExplorerRegistration
Remove-QuickDropPackageCertificate

Write-Step "Removing QuickDrop registry settings."
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "QuickDrop" -ErrorAction SilentlyContinue
Remove-Item -Path "HKCU:\Software\QuickDrop" -Recurse -Force -ErrorAction SilentlyContinue

if ($RemoveFirewallRules) {
    if (Test-IsAdmin) {
        Write-Step "Removing Windows Firewall rules."
        foreach ($name in @("QuickDrop TCP Receiver", "QuickDrop UDP Discovery")) {
            Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
        }
    } else {
        Write-Step "Firewall rules were not removed because this process is not elevated."
    }
} else {
    Write-Step "Skipping Windows Firewall rule removal."
}

if ($RestartExplorer) {
    Write-Step "Restarting Explorer so the context menu is refreshed."
    Stop-Process -Name explorer -Force
    Start-Process explorer.exe
} else {
    Write-Step "Skipping Explorer restart."
}

Write-Step "QuickDrop uninstall completed."
Show-QuickDropMessage -Message "QuickDrop uninstall completed.`r`n`r`nLog: $logPath"
