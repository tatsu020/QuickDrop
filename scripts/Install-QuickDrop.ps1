param(
    [switch]$NoStartup,
    [switch]$RestartExplorer,
    [switch]$AddFirewallRules,
    [switch]$NoElevate,
    [switch]$NoDialog,
    [switch]$TrustPackageCertificateOnly
)

$ErrorActionPreference = "Stop"
$installDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$appPath = Join-Path $installDir "QuickDrop.App.exe"
$cliPath = Join-Path $installDir "QuickDrop.Cli.exe"
$shellPath = Join-Path $installDir "QuickDrop.ShellExtension.dll"
$packageName = "Tatsu020.QuickDrop"
$packagePath = Join-Path $installDir "QuickDrop.Sparse.msix"
$packageCertPath = Join-Path $installDir "QuickDrop.Sparse.cer"
$shellExtensionClsid = "{9B75B6F7-8C63-4B52-A9E4-2CF777E83456}"
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

function Import-QuickDropPackageCertificate {
    param([switch]$Machine)

    if (-not (Test-Path -LiteralPath $packageCertPath)) {
        Write-Step "Sparse package certificate file was not found. Continuing with existing certificate trust."
        return
    }

    $scope = if ($Machine) { "LocalMachine" } else { "CurrentUser" }
    if (Test-Path -LiteralPath $packageCertPath) {
        Write-Step "Trusting QuickDrop sparse package certificate in $scope certificate stores."
        Import-Certificate -FilePath $packageCertPath -CertStoreLocation "Cert:\$scope\TrustedPeople" | Out-Null
        Import-Certificate -FilePath $packageCertPath -CertStoreLocation "Cert:\$scope\Root" | Out-Null
    }
}

function Add-QuickDropIdentityPackage {
    $existingPackages = Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue
    foreach ($package in $existingPackages) {
        Write-Step "Removing existing QuickDrop identity package: $($package.PackageFullName)"
        Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop
    }

    Write-Step "Registering Windows 11 File Explorer context menu package."
    Add-AppxPackage -Path $packagePath -ExternalLocation $installDir -ErrorAction Stop
}

function Trust-PackageCertificateWithElevation {
    if (Test-IsAdmin) {
        Import-QuickDropPackageCertificate -Machine
        return $true
    }

    if ($NoElevate) {
        return $false
    }

    Write-Step "Package registration needs machine-level certificate trust."
    Write-Step "Showing Windows UAC prompt for certificate trust only. Please approve it to continue."
    $arguments = [System.Collections.Generic.List[string]]::new()
    $arguments.Add("-NoProfile")
    $arguments.Add("-ExecutionPolicy")
    $arguments.Add("Bypass")
    $arguments.Add("-File")
    $arguments.Add("`"$PSCommandPath`"")
    $arguments.Add("-TrustPackageCertificateOnly")
    Add-SwitchArgument -List $arguments -Name "-NoDialog" -Enabled:$NoDialog

    $elevated = Start-Process -FilePath "powershell.exe" -ArgumentList $arguments.ToArray() -Verb RunAs -Wait -PassThru
    Write-Step "Elevated certificate trust helper exited with code $($elevated.ExitCode)."
    return $elevated.ExitCode -eq 0
}

function Ensure-PackageCertificateTrust {
    Import-QuickDropPackageCertificate

    if (-not (Test-Path -LiteralPath $packageCertPath)) {
        return
    }

    try {
        $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($packageCertPath)
        $machineRootPath = "Cert:\LocalMachine\Root\$($certificate.Thumbprint)"
        $machineTrustedPeoplePath = "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)"
        if ((Test-Path -LiteralPath $machineRootPath) -and (Test-Path -LiteralPath $machineTrustedPeoplePath)) {
            Write-Step "QuickDrop sparse package certificate is already trusted for this machine."
            return
        }
    }
    catch {
        Write-Step "Could not inspect machine certificate trust: $($_.Exception.Message)"
    }

    if (-not (Trust-PackageCertificateWithElevation)) {
        Write-Step "Machine-level certificate trust was not added. Package registration may fail on this PC."
    }
}

function Register-QuickDropIdentityPackage {
    if (-not (Test-Path -LiteralPath $packagePath)) {
        throw "QuickDrop sparse package was not found: $packagePath"
    }

    Ensure-PackageCertificateTrust
    try {
        Add-QuickDropIdentityPackage
    }
    catch {
        $message = $_.Exception.Message
        if ($message -match "0x800B0109" -or $message -match "CERT_E_UNTRUSTEDROOT") {
            if (Trust-PackageCertificateWithElevation) {
                Write-Step "Retrying Windows 11 File Explorer context menu package registration."
                Add-QuickDropIdentityPackage
                return
            }
        }

        throw
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

if ($TrustPackageCertificateOnly) {
    if (-not (Test-IsAdmin)) {
        throw "Certificate trust helper must be run elevated."
    }

    Import-QuickDropPackageCertificate -Machine
    Write-Step "QuickDrop sparse package certificate trust completed."
    exit 0
}

foreach ($path in @($appPath, $cliPath, $shellPath)) {
    Write-Step "Checking required file: $path"
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required file was not found: $path"
    }
}

Write-Step "Checking required file: $packagePath"
if (-not (Test-Path -LiteralPath $packagePath)) {
    throw "Required file was not found: $packagePath"
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

Write-Step "Stopping QuickDrop tray app if it is running."
Get-Process -Name "QuickDrop.App" -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Step "Writing QuickDrop registry settings."
New-Item -Path "HKCU:\Software\QuickDrop" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\QuickDrop" -Name "InstallDirectory" -Value $installDir
Set-ItemProperty -Path "HKCU:\Software\QuickDrop" -Name "CliPath" -Value $cliPath
Set-ItemProperty -Path "HKCU:\Software\QuickDrop" -Name "ShellExtensionPath" -Value $shellPath

Remove-ClassicExplorerRegistration
Register-QuickDropIdentityPackage

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
