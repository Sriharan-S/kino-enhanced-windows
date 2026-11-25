<#
.SYNOPSIS
  Builds the portable Windows release: a self-contained folder and a zip in dist\ (README TODO 17.1).
.DESCRIPTION
  Release build of Kino.App for win-x64, self-contained (.NET and the Windows App SDK are included), with libmpv-2.dll.
  No installer and no admin rights needed: unzip and run Kino.exe. Settings live in %LOCALAPPDATA%\Kino.
.EXAMPLE
  .\tools\Publish-Portable.ps1
#>
param([string]$Dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path $Dotnet)) { $Dotnet = "dotnet" } else { $env:DOTNET_ROOT = Split-Path $Dotnet -Parent }

if (-not (Test-Path "$root\libs\mpv\libmpv-2.dll")) { & "$PSScriptRoot\Fetch-LibMpv.ps1" }

[xml]$proj = Get-Content "$root\src\Kino.App\Kino.App.csproj"
$version = ($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
if (-not $version) { $version = "0.0.0" }
$name = "Kino-$version-win-x64"
$out = Join-Path $root "dist\$name"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

& $Dotnet publish "$root\src\Kino.App\Kino.App.csproj" -c Release -p:Platform=x64 -r win-x64 --self-contained true `
    -p:PublishReadyToRun=false -p:DebugType=none -p:GenerateDocumentationFile=false -o $out
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

@"
Kino $version for Windows (x64), portable

Run Kino.exe. No installation or admin rights needed.
Settings, preferences and your device identity are stored in %LOCALAPPDATA%\Kino.
The first run registers kino:// invite links for your user account.

Requirements: Windows 10 2004 (build 19041) or later, x64. YouTube items need the Microsoft Edge
WebView2 Runtime, which is built into Windows 11 and most up-to-date Windows 10 PCs.

Third-party components
- libmpv-2.dll: mpv (https://mpv.io), licensed under the GPL v2 or later (parts LGPL), with FFmpeg and other
  libraries. Source code: https://github.com/mpv-player/mpv. This build: shinchiro's mpv-dev-x86_64 from
  https://sourceforge.net/projects/mpv-player-windows/ (see tools\Fetch-LibMpv.ps1 in the source repository).
- Windows App SDK and .NET runtime: Microsoft, MIT licence.

Source: https://github.com/Sriharan-S/kino-enhanced-windows
"@ | Set-Content -Encoding utf8 "$out\README.txt"

$zip = Join-Path $root "dist\$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$out\*" -DestinationPath $zip -CompressionLevel Optimal
$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
"Portable build: $zip ($size MB)"
