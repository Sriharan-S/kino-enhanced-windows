<#
.SYNOPSIS
  Returns the path to ISCC.exe (Inno Setup 6 compiler), downloading the pinned NuGet package into tools\.cache if needed.
.DESCRIPTION
  Inno Setup's own installer is hosted on GitHub releases (blocked by some corporate proxies), so this uses the
  Tools.InnoSetup package from nuget.org: pinned by SHA-256, and ISCC.exe must carry a valid Authenticode signature.
  No installation and no admin rights.
#>
$ErrorActionPreference = 'Stop'
$version = '6.7.3'
$sha256 = 'f780898e402ff80612cc8d9fcb8c6e02932bd1cb4c900ffdaa31f9341cfb49f4'
$cache = Join-Path $PSScriptRoot '.cache'
$dir = Join-Path $cache "innosetup-$version"
$iscc = Join-Path $dir 'tools\ISCC.exe'

if (-not (Test-Path $iscc)) {
    New-Item -ItemType Directory -Force $cache | Out-Null
    $pkg = Join-Path $cache "tools.innosetup.$version.nupkg"
    Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/tools.innosetup/$version/tools.innosetup.$version.nupkg" -OutFile $pkg -UseBasicParsing
    $actual = (Get-FileHash $pkg -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $sha256) { Remove-Item $pkg -Force; throw "Inno Setup package SHA-256 mismatch: $actual" }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    [System.IO.Compression.ZipFile]::ExtractToDirectory($pkg, $dir)
}

$sig = Get-AuthenticodeSignature $iscc
if ($sig.Status -ne 'Valid') { throw "ISCC.exe signature is $($sig.Status)" }
$iscc
