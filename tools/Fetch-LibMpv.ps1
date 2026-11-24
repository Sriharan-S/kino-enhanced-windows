<#
.SYNOPSIS
  Downloads the pinned libmpv build into libs/mpv (not committed to git).

.DESCRIPTION
  Uses shinchiro's mpv-winbuild-cmake builds from their SourceForge mirror
  (GitHub release downloads may be blocked by corporate proxies).
  The download is verified against the pinned SHA-1 published by SourceForge.
#>
[CmdletBinding()]
param(
    [string]$Version = 'mpv-dev-x86_64-20261108-git-55d3feb45c',
    [string]$Sha1    = 'da98b74a5982a59a8f3a5266815025f45b17c91e',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root   = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'libs\mpv'
$dll    = Join-Path $target 'libmpv-2.dll'

if ((Test-Path $dll) -and -not $Force) {
    Write-Host "libmpv already present at $dll (use -Force to re-download)."
    return
}

New-Item -ItemType Directory -Force $target | Out-Null
$archive = Join-Path ([IO.Path]::GetTempPath()) "$Version.7z"
$url     = "https://sourceforge.net/projects/mpv-player-windows/files/libmpv/$Version.7z/download"

Write-Host "Downloading $Version ..."
# SourceForge serves the file directly to non-browser user agents.
Invoke-WebRequest -Uri $url -OutFile $archive -UserAgent 'Wget/1.21' -UseBasicParsing

$actual = (Get-FileHash $archive -Algorithm SHA1).Hash.ToLowerInvariant()
if ($actual -ne $Sha1) {
    Remove-Item $archive -Force
    throw "SHA-1 mismatch: expected $Sha1, got $actual"
}

# Windows' built-in bsdtar can extract .7z archives.
& "$env:SystemRoot\System32\tar.exe" -xf $archive -C $target
if ($LASTEXITCODE -ne 0) { throw "Extraction failed (tar exit code $LASTEXITCODE)" }
Remove-Item $archive -Force

Write-Host "libmpv ready at $dll"
