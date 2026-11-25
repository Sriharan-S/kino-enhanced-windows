<#
.SYNOPSIS
  Downloads the pinned libmpv build for one architecture (or all) into libs\mpv\<arch> (not committed to git).

.DESCRIPTION
  Uses shinchiro's mpv-winbuild-cmake builds from their SourceForge mirror (GitHub release downloads may be blocked
  by corporate proxies). Each archive is verified against a pinned SHA-256 (cross-checked with SourceForge's MD5).

.EXAMPLE
  .\tools\Fetch-LibMpv.ps1              # x64
  .\tools\Fetch-LibMpv.ps1 -Arch arm64
  .\tools\Fetch-LibMpv.ps1 -All
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'x86', 'arm64')][string]$Arch = 'x64',
    [switch]$All,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$build = '20261108-git-55d3feb45c'
$pins = @{
    x64   = @{ Name = "mpv-dev-x86_64-$build";  Sha256 = '7c147cb7ceaba717414e782ef4398de728f679db6e86daaa8aeb175670cb7994' }
    x86   = @{ Name = "mpv-dev-i686-$build";    Sha256 = 'b5140046b4d48d8866ef3c968e591b0022cb4eb75c554aae8f12fd66ebbfcc66' }
    arm64 = @{ Name = "mpv-dev-aarch64-$build"; Sha256 = 'f7d3b2d97d07b5758cb277412ed95dc55aac2112397b6d06161d0b58c3e941fc' }
}
$root = Split-Path -Parent $PSScriptRoot

foreach ($a in $(if ($All) { 'x64', 'x86', 'arm64' } else { $Arch })) {
    $pin = $pins[$a]
    $target = Join-Path $root "libs\mpv\$a"
    $dll = Join-Path $target 'libmpv-2.dll'
    if ((Test-Path $dll) -and -not $Force) {
        Write-Host "libmpv ($a) already present at $dll (use -Force to re-download)."
        continue
    }

    New-Item -ItemType Directory -Force $target | Out-Null
    $archive = Join-Path ([IO.Path]::GetTempPath()) "$($pin.Name).7z"
    $url = "https://sourceforge.net/projects/mpv-player-windows/files/libmpv/$($pin.Name).7z/download"
    Write-Host "Downloading $($pin.Name) ..."
    # SourceForge serves the file directly to non-browser user agents.
    Invoke-WebRequest -Uri $url -OutFile $archive -UserAgent 'Wget/1.21' -UseBasicParsing

    $actual = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $pin.Sha256) {
        Remove-Item $archive -Force
        throw "SHA-256 mismatch for $($pin.Name): expected $($pin.Sha256), got $actual"
    }

    # Windows' built-in bsdtar can extract .7z archives.
    & "$env:SystemRoot\System32\tar.exe" -xf $archive -C $target
    if ($LASTEXITCODE -ne 0) { throw "Extraction failed (tar exit code $LASTEXITCODE)" }
    Remove-Item $archive -Force
    Write-Host "libmpv ($a) ready at $dll"
}
