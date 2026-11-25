<#
.SYNOPSIS
  Draws the Kino icon (night-sky square, warm play symbol, a few stars) and writes
  src\Kino.App\Assets\Kino.ico (16-256 px) and Kino.png (256 px). Re-run after changing the design.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'src\Kino.App\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function New-KinoBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded square with a night-sky gradient.
    $pad = [Math]::Max(1, $size * 0.04); $r = $size * 0.22; $w = $size - 2 * $pad
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc($pad, $pad, $d, $d, 180, 90); $path.AddArc($pad + $w - $d, $pad, $d, $d, 270, 90)
    $path.AddArc($pad + $w - $d, $pad + $w - $d, $d, $d, 0, 90); $path.AddArc($pad, $pad + $w - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $sky = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, 0)), ([System.Drawing.PointF]::new($size, $size)),
        ([System.Drawing.Color]::FromArgb(255, 0x16, 0x1A, 0x40)), ([System.Drawing.Color]::FromArgb(255, 0x5B, 0x3F, 0xA8))
    $g.FillPath($sky, $path)

    # Stars (skipped at the smallest sizes, where they'd only be noise).
    if ($size -ge 32) {
        $star = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(230, 255, 255, 255))
        foreach ($s in @(@(0.22, 0.24, 0.035), @(0.78, 0.2, 0.025), @(0.74, 0.78, 0.03), @(0.2, 0.74, 0.02), @(0.5, 0.15, 0.018))) {
            $rr = [Math]::Max(1, $s[2] * $size)
            $g.FillEllipse($star, [float]($s[0] * $size - $rr), [float]($s[1] * $size - $rr), [float](2 * $rr), [float](2 * $rr))
        }
    }

    # Play symbol, slightly right of centre so it looks centred.
    $tri = New-Object System.Drawing.Drawing2D.GraphicsPath
    $cx = $size * 0.54; $cy = $size * 0.5; $h = $size * 0.42
    $tri.AddPolygon([System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new([float]($cx - $h * 0.42), [float]($cy - $h / 2)),
        [System.Drawing.PointF]::new([float]($cx + $h * 0.5), [float]$cy),
        [System.Drawing.PointF]::new([float]($cx - $h * 0.42), [float]($cy + $h / 2))))
    $warm = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, [float]($cy - $h / 2))), ([System.Drawing.PointF]::new(0, [float]($cy + $h / 2))),
        ([System.Drawing.Color]::FromArgb(255, 0xFF, 0xC8, 0x57)), ([System.Drawing.Color]::FromArgb(255, 0xF0, 0x8A, 0x3C))
    $g.FillPath($warm, $tri)
    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-KinoBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($s -eq 256) { $bmp.Save((Join-Path $assets 'Kino.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    , $ms.ToArray()
}

# ICO container with PNG-compressed images (supported since Windows Vista).
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset); $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
[IO.File]::WriteAllBytes((Join-Path $assets 'Kino.ico'), $out.ToArray())
"Wrote $(Join-Path $assets 'Kino.ico') and Kino.png"
