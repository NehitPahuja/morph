# Builds dist\Morph.exe: one self-contained file (it carries its own .NET), for 64-bit Windows.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnet publish (Join-Path $root 'src\Morph\Morph.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o (Join-Path $root 'dist') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$exe = Get-Item (Join-Path $root 'dist\Morph.exe')
Write-Host ("Built {0} ({1:N0} MB)" -f $exe.FullName, ($exe.Length / 1MB)) -ForegroundColor Green
