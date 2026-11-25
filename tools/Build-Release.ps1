<#
.SYNOPSIS
  Builds the release files for each architecture into dist\:
    Kino-Setup-<arch>.exe       installer (per user, no admin; uninstaller in Settings > Apps)
    Kino-Portable-<arch>.zip    portable edition (unzip and run; keeps its data in a Data folder beside Kino.exe)
    SHA256SUMS.txt              checksums of all of the above
.DESCRIPTION
  File names carry no version, so https://github.com/<repo>/releases/latest/download/<file> always points at the
  newest release. The version (from Directory.Build.props) is in each file's properties and in the release title.
.EXAMPLE
  .\tools\Build-Release.ps1                 # x64, x86 and arm64
  .\tools\Build-Release.ps1 -Arch x64 -SkipInstaller
#>
param(
    [ValidateSet('x64', 'x86', 'arm64')][string[]]$Arch = @('x64', 'x86', 'arm64'),
    [switch]$SkipInstaller,
    [string]$Dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (Test-Path $Dotnet) { $env:DOTNET_ROOT = Split-Path $Dotnet -Parent } else { $Dotnet = 'dotnet' }

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = "$($props.Project.PropertyGroup.Version)".Trim()
$numeric = ($version -split '[-+]')[0]
Write-Host "Kino $version"

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$iscc = if ($SkipInstaller) { $null } else { & (Join-Path $PSScriptRoot 'Get-InnoSetup.ps1') }
$platform = @{ x64 = 'x64'; x86 = 'x86'; arm64 = 'ARM64' }

foreach ($a in $Arch) {
    Write-Host "`n=== $a ==="
    & (Join-Path $PSScriptRoot 'Fetch-LibMpv.ps1') -Arch $a

    $publish = Join-Path $dist "publish\$a\Kino"
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
    & $Dotnet publish (Join-Path $root 'src\Kino.App\Kino.App.csproj') -c Release -p:Platform=$($platform[$a]) -r "win-$a" `
        --self-contained true -p:PublishReadyToRun=false -p:DebugType=none -p:GenerateDocumentationFile=false -o $publish | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "publish failed for $a" }
    foreach ($required in 'Kino.exe', 'libmpv-2.dll', 'Kino.pri', 'MainWindow.xbf', 'Assets\Kino.ico') {
        if (-not (Test-Path (Join-Path $publish $required))) { throw "$a build is missing $required" }
    }

    # Installer (from the plain publish folder).
    if ($iscc) {
        & $iscc /Q "/DAppVersion=$version" "/DNumericVersion=$numeric" "/DArch=$a" "/DSourceDir=$publish" "/DOutputDir=$dist" `
            (Join-Path $root 'installer\Kino.iss') | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "installer build failed for $a" }
    }

    # Portable edition: the same files plus the "portable" marker and a readme, in a Kino\ folder inside the zip.
    $portableRoot = Join-Path $dist "publish\$a-portable"
    if (Test-Path $portableRoot) { Remove-Item $portableRoot -Recurse -Force }
    Copy-Item $publish (Join-Path $portableRoot 'Kino') -Recurse
    Set-Content -Encoding utf8 (Join-Path $portableRoot 'Kino\portable') "This copy of Kino is portable: its data lives in the Data folder next to Kino.exe."
    @"
Kino $version for Windows ($a), portable edition

Run Kino.exe. No installation or admin rights needed.
Everything Kino stores (settings, preferences, your device identity) goes into the Data folder next to Kino.exe,
so you can keep this folder on a USB stick. Delete the folder to remove Kino completely.
Prefer a normal install with a Start menu entry and an uninstaller? Use Kino-Setup-$a.exe instead.

Requirements: Windows 10 2004 (build 19041) or later. YouTube items need the Microsoft Edge WebView2 Runtime,
which is built into Windows 11 and most up-to-date Windows 10 PCs.

Third-party components
- libmpv-2.dll: mpv (https://mpv.io), GPL v2 or later (parts LGPL), with FFmpeg and other libraries.
  Source: https://github.com/mpv-player/mpv. Build: shinchiro's mpv-dev from https://sourceforge.net/projects/mpv-player-windows/
- Windows App SDK and .NET runtime: Microsoft, MIT licence.

Releases: https://github.com/Sriharan-S/kino-enhanced-windows/releases/latest
"@ | Set-Content -Encoding utf8 (Join-Path $portableRoot 'Kino\README.txt')
    $zip = Join-Path $dist "Kino-Portable-$a.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $portableRoot 'Kino') -DestinationPath $zip -CompressionLevel Optimal
}

# Checksums for everything in dist (not just this run's architectures).
$files = Get-ChildItem $dist -File | Where-Object { $_.Name -match '^Kino-(Setup|Portable)-.+\.(exe|zip)$' } | Sort-Object Name
$files | ForEach-Object { "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name } |
    Set-Content -Encoding ascii (Join-Path $dist 'SHA256SUMS.txt')
Write-Host "`nRelease files in $dist"
$files | ForEach-Object { "{0,-28} {1,7:N1} MB" -f $_.Name, ($_.Length / 1MB) } | Out-Host
