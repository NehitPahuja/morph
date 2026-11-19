# Draws Morph's icon (a coral rounded square with two swap arrows) and writes
# src\Morph\Assets\Morph.png (256 px) and Morph.ico (16-256 px, PNG frames).
# Runs in Windows PowerShell 5.1 or PowerShell 7 on Windows.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot '..\src\Morph\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'

    $r = [float]($size * 0.22)
    $rect = New-Object System.Drawing.RectangleF 0, 0, ($size - 1), ($size - 1)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($rect.Right - 2 * $r, $rect.Y, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($rect.Right - 2 * $r, $rect.Bottom - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $top = [System.Drawing.Color]::FromArgb(255, 255, 145, 100)
    $bottom = [System.Drawing.Color]::FromArgb(255, 236, 84, 62)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $top, $bottom, 60
    $g.FillPath($brush, $path)

    # Two arrows: one pointing right along the top, one pointing left along the bottom.
    $ink = [System.Drawing.Color]::FromArgb(255, 32, 16, 10)
    $pen = New-Object System.Drawing.Pen $ink, ([float]($size * 0.085))
    $pen.StartCap = 'Round'
    $pen.EndCap = 'Round'
    $pen.LineJoin = 'Round'
    $s = [float]$size
    $g.DrawLine($pen, $s * 0.26, $s * 0.38, $s * 0.72, $s * 0.38)
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ($s * 0.60), ($s * 0.26)),
        (New-Object System.Drawing.PointF ($s * 0.74), ($s * 0.38)),
        (New-Object System.Drawing.PointF ($s * 0.60), ($s * 0.50))))
    $g.DrawLine($pen, $s * 0.74, $s * 0.64, $s * 0.28, $s * 0.64)
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ($s * 0.40), ($s * 0.52)),
        (New-Object System.Drawing.PointF ($s * 0.26), ($s * 0.64)),
        (New-Object System.Drawing.PointF ($s * 0.40), ($s * 0.76))))
    $g.Dispose()
    return $bmp
}

function Get-PngBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

$big = New-IconBitmap 256
$big.Save((Join-Path $assets 'Morph.png'), [System.Drawing.Imaging.ImageFormat]::Png)

# ICO with PNG-compressed frames (supported since Windows Vista).
$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = foreach ($sz in $sizes) { , (Get-PngBytes (New-IconBitmap $sz)) }
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$frames[$i].Length); $w.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($f in $frames) { $w.Write($f) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'Morph.ico'), $out.ToArray())
Write-Host "Wrote Morph.png and Morph.ico to $assets"
