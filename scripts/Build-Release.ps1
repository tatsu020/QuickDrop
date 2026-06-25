param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root "dist\QuickDrop"
$msbuildCandidates = @(
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
)
$msbuild = $msbuildCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $msbuild) {
    throw "MSBuild.exe was not found. Install Visual Studio Build Tools."
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

Write-Host "Built QuickDrop to $dist"
