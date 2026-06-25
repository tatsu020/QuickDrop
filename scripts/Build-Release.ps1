param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root "dist\QuickDrop"
$packagePublisher = "CN=tatsu"
$sparsePackageName = "QuickDrop.Sparse.msix"
$sparseCertificateName = "QuickDrop.Sparse.cer"
$msbuildCandidates = @(
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
)
$msbuild = $msbuildCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $msbuild) {
    throw "MSBuild.exe was not found. Install Visual Studio Build Tools."
}

function Get-WindowsSdkTool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ToolName
    )

    $sdkBin = "C:\Program Files (x86)\Windows Kits\10\bin"
    if (-not (Test-Path -LiteralPath $sdkBin)) {
        throw "Windows SDK bin directory was not found. Install Windows SDK."
    }

    $tool = Get-ChildItem -LiteralPath $sdkBin -Recurse -Filter $ToolName -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\x64\\$([regex]::Escape($ToolName))$" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if (-not $tool) {
        throw "$ToolName was not found in Windows SDK."
    }

    return $tool.FullName
}

function New-QuickDropLogo {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [int]$Size
    )

    Add-Type -AssemblyName System.Drawing
    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory | Out-Null
    }

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::FromArgb(0, 0, 0, 0))
        $background = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 32, 88, 121))
        $accent = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 88, 199, 171))
        try {
            $graphics.FillRectangle($background, 0, 0, $Size, $Size)
            $margin = [Math]::Max(4, [int]($Size * 0.18))
            $barHeight = [Math]::Max(3, [int]($Size * 0.18))
            $graphics.FillRectangle($accent, $margin, [int]($Size * 0.42), $Size - ($margin * 2), $barHeight)
            $graphics.FillRectangle($accent, [int]($Size * 0.58), $margin, $barHeight, $Size - ($margin * 2))
        }
        finally {
            $background.Dispose()
            $accent.Dispose()
        }

        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function New-QuickDropSigningCertificate {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Publisher,
        [Parameter(Mandatory = $true)]
        [string]$PfxPath,
        [Parameter(Mandatory = $true)]
        [string]$CerPath
    )

    $passwordText = [Guid]::NewGuid().ToString("N")
    $password = ConvertTo-SecureString -String $passwordText -Force -AsPlainText
    $certificate = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Publisher `
        -FriendlyName "QuickDrop Sparse Package Signing" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -KeyExportPolicy Exportable `
        -HashAlgorithm SHA256 `
        -KeyLength 2048 `
        -NotAfter (Get-Date).AddYears(3)

    Export-PfxCertificate -Cert $certificate -FilePath $PfxPath -Password $password | Out-Null
    Export-Certificate -Cert $certificate -FilePath $CerPath | Out-Null

    return [pscustomobject]@{
        PfxPath = $PfxPath
        CerPath = $CerPath
        Password = $passwordText
        Thumbprint = $certificate.Thumbprint
    }
}

function Get-QuickDropSigningCertificate {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Publisher
    )

    return Get-ChildItem "Cert:\CurrentUser\My" -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object {
            $_.HasPrivateKey -and
            $_.Subject -eq $Publisher -and
            $_.NotAfter -gt (Get-Date).AddDays(1)
        } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
}

if (Test-Path $dist) {
    Remove-Item -LiteralPath $dist -Recurse -Force
}
New-Item -ItemType Directory -Path $dist | Out-Null

$selfContained = "true"
if ($FrameworkDependent) {
    $selfContained = "false"
}
dotnet publish (Join-Path $root "QuickDrop.App\QuickDrop.App.csproj") -c $Configuration -r $Runtime --self-contained $selfContained -o $dist
dotnet publish (Join-Path $root "QuickDrop.Cli\QuickDrop.Cli.csproj") -c $Configuration -r $Runtime --self-contained $selfContained -o $dist

& $msbuild (Join-Path $root "QuickDrop.ShellExtension\QuickDrop.ShellExtension.vcxproj") /p:Configuration=$Configuration /p:Platform=x64 /m
Copy-Item -LiteralPath (Join-Path $root "QuickDrop.ShellExtension\x64\$Configuration\QuickDrop.ShellExtension.dll") -Destination $dist -Force

Copy-Item -LiteralPath (Join-Path $root "scripts\Install-QuickDrop.ps1") -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $root "scripts\Uninstall-QuickDrop.ps1") -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $root "scripts\Install-QuickDrop.cmd") -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $root "scripts\Uninstall-QuickDrop.cmd") -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination $dist -Force

Write-Host "Creating QuickDrop identity package."
$makeAppx = Get-WindowsSdkTool -ToolName "makeappx.exe"
$signTool = Get-WindowsSdkTool -ToolName "signtool.exe"
$packageRoot = Join-Path $root "dist\QuickDrop.Package"
$msixPath = Join-Path $dist $sparsePackageName
$cerPath = Join-Path $dist $sparseCertificateName
$pfxPath = Join-Path $dist "QuickDrop.Sparse.pfx"

if (Test-Path -LiteralPath $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $packageRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $root "packaging\QuickDrop.SparsePackage\AppxManifest.xml") -Destination (Join-Path $packageRoot "AppxManifest.xml") -Force

foreach ($assetRoot in @($packageRoot, $dist)) {
    New-QuickDropLogo -Path (Join-Path $assetRoot "Assets\Square150x150Logo.png") -Size 150
    New-QuickDropLogo -Path (Join-Path $assetRoot "Assets\Square44x44Logo.png") -Size 44
}

$signing = $null
try {
    & $makeAppx pack /o /d $packageRoot /nv /p $msixPath
    if ($LASTEXITCODE -ne 0) {
        throw "MakeAppx failed with exit code $LASTEXITCODE"
    }

    $storeCertificate = Get-QuickDropSigningCertificate -Publisher $packagePublisher
    if ($storeCertificate) {
        Write-Host "Signing sparse package with existing certificate $($storeCertificate.Thumbprint)."
        Export-Certificate -Cert $storeCertificate -FilePath $cerPath | Out-Null
        & $signTool sign /fd SHA256 /sha1 $storeCertificate.Thumbprint $msixPath
    } else {
        Write-Host "Creating a development signing certificate for $packagePublisher."
        $signing = New-QuickDropSigningCertificate -Publisher $packagePublisher -PfxPath $pfxPath -CerPath $cerPath
        & $signTool sign /fd SHA256 /f $signing.PfxPath /p $signing.Password $msixPath
    }

    if ($LASTEXITCODE -ne 0) {
        throw "SignTool failed with exit code $LASTEXITCODE"
    }
}
finally {
    if ($signing -and $signing.Thumbprint) {
        Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($signing.Thumbprint)" -Force -ErrorAction SilentlyContinue
    }

    Remove-Item -LiteralPath $pfxPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $packageRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Built QuickDrop to $dist"
