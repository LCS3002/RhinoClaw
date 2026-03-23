$ErrorActionPreference = 'Stop'

$pluginDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot  = Split-Path -Parent $pluginDir
$uiDir     = Join-Path $repoRoot 'ui'
$wwwDir    = Join-Path $pluginDir 'www'

# Build React UI
if (Test-Path $uiDir) {
    Write-Host 'Building React UI...'
    Push-Location $uiDir
    & npm install
    & npm run build
    Pop-Location

    Write-Host 'Copying UI build to www/ for embedding...'
    if (Test-Path $wwwDir) { Remove-Item $wwwDir -Recurse -Force }
    New-Item -ItemType Directory -Path $wwwDir -Force | Out-Null
    Copy-Item -Path (Join-Path $uiDir 'dist' '*') -Destination $wwwDir -Recurse -Force
} else {
    Write-Host "Warning: ui/ not found at $uiDir — skipping frontend build"
}

# Build C# plugin
Write-Host 'Building Rhino plugin...'
Set-Location $pluginDir
& dotnet build -c Release

# Copy DLL → .rhp
$dllPath = Join-Path $pluginDir 'bin/Release/net48/PenguinClaw.dll'
$rhpPath = Join-Path $pluginDir 'PenguinClaw.rhp'

if (-not (Test-Path $dllPath)) {
    throw "Build output not found at $dllPath — did the build succeed?"
}

Copy-Item $dllPath $rhpPath -Force

# Remove nested .rhp artefact if present
$nested = Join-Path $pluginDir 'bin/Release/net48/PenguinClaw.rhp'
if (Test-Path $nested) { Remove-Item $nested -Force -ErrorAction SilentlyContinue }

Write-Host ''
Write-Host 'Build complete:'
Get-Item $rhpPath | Select-Object FullName, LastWriteTime, Length | Format-List
