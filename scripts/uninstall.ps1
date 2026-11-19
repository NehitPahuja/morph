# Removes Morph: the Explorer menu entries, the shortcuts and the program folder.
# -KeepSettings leaves %LOCALAPPDATA%\Morph (your remembered formats and options) in place.
param([switch]$KeepSettings)
$ErrorActionPreference = 'Stop'

$target = Join-Path $env:LOCALAPPDATA 'Programs\Morph'
$exe = Join-Path $target 'Morph.exe'

Get-Process Morph -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$target*" } | Stop-Process -Force

if (Test-Path $exe) {
    Start-Process -FilePath $exe -ArgumentList '--unregister-menu' -Wait
} else {
    # Without the program, remove the menu keys directly.
    Get-ChildItem 'HKCU:\Software\Classes\SystemFileAssociations' -ErrorAction SilentlyContinue | ForEach-Object {
        $verb = Join-Path $_.PSPath 'shell\MorphConvert'
        if (Test-Path $verb) { Remove-Item -Recurse -Force $verb }
    }
}

foreach ($lnk in (Join-Path ([Environment]::GetFolderPath('Programs')) 'Morph.lnk'),
                 (Join-Path ([Environment]::GetFolderPath('SendTo')) 'Morph.lnk')) {
    if (Test-Path $lnk) { Remove-Item -Force $lnk }
}
Start-Sleep -Milliseconds 300
if (Test-Path $target) { Remove-Item -Recurse -Force $target }
if (-not $KeepSettings) {
    $data = Join-Path $env:LOCALAPPDATA 'Morph'
    if (Test-Path $data) { Remove-Item -Recurse -Force $data }
}
Write-Host 'Morph is removed.' -ForegroundColor Green
