# Builds sample files with ffmpeg, then converts each one to every format Morph offers for it
# (all three quality presets for a few), using Morph's window-less --convert mode.
# Usage: scripts\smoke-test.ps1 [-Exe path\to\Morph.exe]
param([string]$Exe = (Join-Path $PSScriptRoot '..\src\Morph\bin\Release\net10.0-windows10.0.19041.0\Morph.exe'))
$ErrorActionPreference = 'Stop'

$Exe = (Resolve-Path $Exe).Path
$ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
if (-not $ffmpeg) {
    $ffmpeg = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages') -Directory -Filter '*FFmpeg*' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem $_.FullName -Recurse -Filter ffmpeg.exe -ErrorAction SilentlyContinue } |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $ffmpeg -or -not (Test-Path $ffmpeg)) { throw 'ffmpeg is needed for the samples: winget install Gyan.FFmpeg' }

$work = Join-Path ([IO.Path]::GetTempPath()) ("morph-smoke-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
$in = Join-Path $work 'in'; $out = Join-Path $work 'out'
New-Item -ItemType Directory -Force $in, $out | Out-Null

function Invoke-FF([string[]]$a) {
    & $ffmpeg -hide_banner -loglevel error -y @a
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed making a sample: $a" }
}

Write-Host "Making samples in $in"
Invoke-FF @('-f', 'lavfi', '-i', 'sine=frequency=440:duration=3', '-ac', '2', (Join-Path $in 'tone.wav'))
Invoke-FF @('-f', 'lavfi', '-i', 'sine=frequency=330:duration=3', '-c:a', 'libmp3lame', (Join-Path $in 'song.mp3'))
# Odd dimensions on purpose: the encoders need even ones.
Invoke-FF @('-f', 'lavfi', '-i', 'testsrc=size=321x241:rate=25:duration=2', '-f', 'lavfi', '-i', 'sine=duration=2',
    '-c:v', 'ffv1', '-c:a', 'flac', '-shortest', (Join-Path $in 'clip.mkv'))
Invoke-FF @('-f', 'lavfi', '-i', 'testsrc=size=640x360:rate=25:duration=2', '-c:v', 'libx264', '-pix_fmt', 'yuv420p', (Join-Path $in 'silent.mp4'))
Invoke-FF @('-f', 'lavfi', '-i', 'testsrc=size=2400x1600', '-frames:v', '1', (Join-Path $in 'photo.jpg'))
Invoke-FF @('-f', 'lavfi', '-i', 'color=c=0x3080ff@0.5:size=301x203,format=rgba', '-frames:v', '1', (Join-Path $in 'glass.png'))
Invoke-FF @('-f', 'lavfi', '-i', 'testsrc=size=200x150', '-frames:v', '1', '-c:v', 'libwebp', (Join-Path $in 'web.webp'))

# A two-page PDF, written by hand.
$objs = @(
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R 5 0 R] /Count 2 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 7 0 R >> >> >>',
    $null,
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 6 0 R /Resources << /Font << /F1 7 0 R >> >> >>',
    $null,
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>')
$s1 = 'BT /F1 36 Tf 72 700 Td (Page one) Tj ET'; $s2 = 'BT /F1 36 Tf 72 700 Td (Page two) Tj ET'
$objs[3] = "<< /Length $($s1.Length) >>`nstream`n$s1`nendstream"
$objs[5] = "<< /Length $($s2.Length) >>`nstream`n$s2`nendstream"
$sb = New-Object Text.StringBuilder
[void]$sb.Append("%PDF-1.4`n")
$offsets = @()
for ($i = 0; $i -lt $objs.Count; $i++) {
    $offsets += $sb.Length
    [void]$sb.Append("$($i + 1) 0 obj`n$($objs[$i])`nendobj`n")
}
$xref = $sb.Length
[void]$sb.Append("xref`n0 $($objs.Count + 1)`n0000000000 65535 f `n")
foreach ($o in $offsets) { [void]$sb.Append(("{0:D10} 00000 n `n" -f $o)) }
[void]$sb.Append("trailer`n<< /Size $($objs.Count + 1) /Root 1 0 R >>`nstartxref`n$xref`n%%EOF`n")
[IO.File]::WriteAllText((Join-Path $in 'doc.pdf'), $sb.ToString(), [Text.Encoding]::ASCII)

$cases = @(
    @{ File = 'tone.wav'; Formats = 'mp3 m4a opus ogg flac wav' },
    @{ File = 'song.mp3'; Formats = 'wav flac' },
    @{ File = 'clip.mkv'; Formats = 'mp4 webm mkv mov anim-gif mp3 m4a opus ogg flac wav' },
    @{ File = 'silent.mp4'; Formats = 'mp4 webm' },
    @{ File = 'photo.jpg'; Formats = 'jpg png webp avif bmp tiff still-gif ico' },
    @{ File = 'glass.png'; Formats = 'jpg png webp bmp ico' },
    @{ File = 'web.webp'; Formats = 'png jpg' },
    @{ File = 'doc.pdf'; Formats = 'pages-png pages-jpg' }
)
$failures = 0; $count = 0
foreach ($c in $cases) {
    foreach ($fmt in $c.Formats.Split(' ')) {
        $qualities = if ($fmt -in 'mp4', 'jpg', 'mp3', 'pages-png') { 'small', 'balanced', 'best' } else { , 'balanced' }
        foreach ($q in $qualities) {
            $count++
            $result = & $Exe --convert $fmt --quality $q --out $out (Join-Path $in $c.File) | Out-String
            $line = $result.Trim()
            if ($LASTEXITCODE -ne 0 -or -not $line.StartsWith('OK')) {
                $failures++
                Write-Host ("FAIL {0,-10} {1,-9} {2}: {3}" -f $fmt, $q, $c.File, $line) -ForegroundColor Red
            } else {
                $path = ($line -replace '^OK\s+.*? -> (.*) \([0-9.]+s\)$', '$1')
                $size = if (Test-Path $path -PathType Leaf) { (Get-Item $path).Length } else { (Get-ChildItem $path | Measure-Object Length -Sum).Sum }
                if (-not $size) { $failures++; Write-Host "FAIL $fmt $($c.File): output is empty" -ForegroundColor Red }
                else { Write-Host ("ok   {0,-10} {1,-9} {2,-11} {3,10:N0} bytes  {4}" -f $fmt, $q, $c.File, $size, (Split-Path $path -Leaf)) }
            }
        }
    }
}

# Things that must fail cleanly.
$bad = & $Exe --convert mp4 --out $out (Join-Path $in 'tone.wav') | Out-String
if ($LASTEXITCODE -eq 0) { $failures++; Write-Host 'FAIL audio -> mp4 should have been refused' -ForegroundColor Red }
else { Write-Host "ok   refused audio -> mp4: $($bad.Trim())" }
$count++

Write-Host ""
if ($failures) { Write-Host "$failures of $count checks failed. Files are in $work" -ForegroundColor Red; exit 1 }
Write-Host "All $count checks passed." -ForegroundColor Green
Remove-Item -Recurse -Force $work
