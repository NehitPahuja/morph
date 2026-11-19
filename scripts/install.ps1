# Builds Morph and installs it for the current user only (no admin rights needed):
#   - the app in %LOCALAPPDATA%\Programs\Morph
#   - a Start menu shortcut, and a "Send to → Morph" entry (no limit on how many files you send)
#   - "Convert with Morph" in Explorer's right-click menu for every file type Morph converts
# Run it again to update. scripts\uninstall.ps1 removes everything.
param([switch]$NoMenu)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\Morph\Morph.csproj'
$target = Join-Path $env:LOCALAPPDATA 'Programs\Morph'
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw 'The .NET 10 SDK is needed: winget install Microsoft.DotNet.SDK.10' }

# A running Morph holds its files open.
Get-Process Morph -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$target*" } | Stop-Process -Force
Start-Sleep -Milliseconds 300

Write-Host 'Building...'
$staging = Join-Path ([IO.Path]::GetTempPath()) ('morph-publish-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnet publish $project -c Release -o $staging --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

Write-Host "Installing to $target"
if (Test-Path $target) { Remove-Item -Recurse -Force $target }
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item -Recurse -Force (Join-Path $staging '*') $target
Remove-Item -Recurse -Force $staging
$exe = Join-Path $target 'Morph.exe'

$shell = New-Object -ComObject WScript.Shell
function New-Shortcut([string]$path, [string]$description) {
    $lnk = $shell.CreateShortcut($path)
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $target
    $lnk.IconLocation = "$exe,0"
    $lnk.Description = $description
    $lnk.Save()
}
New-Shortcut (Join-Path ([Environment]::GetFolderPath('Programs')) 'Morph.lnk') 'Convert audio, video, images and PDFs'
New-Shortcut (Join-Path ([Environment]::GetFolderPath('SendTo')) 'Morph.lnk') 'Convert with Morph'

if (-not $NoMenu) {
    $p = Start-Process -FilePath $exe -ArgumentList '--register-menu' -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw 'Registering the Explorer menu failed.' }
}

Write-Host ''
Write-Host 'Morph is installed.' -ForegroundColor Green
Write-Host '  Start menu:  Morph'
Write-Host '  Explorer:    right-click a file > Show more options > Convert with Morph'
Write-Host '               (or select files and press Shift+F10; or right-click > Send to > Morph)'
