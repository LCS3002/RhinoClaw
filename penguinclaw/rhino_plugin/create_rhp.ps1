# Copy DLL and rename to .rhp for Rhino plugin
$source = "bin/Release/net48/PenguinClaw.dll"
$target = "bin/Release/net48/PenguinClaw.rhp"
if (Test-Path $source) {
    Copy-Item $source $target -Force
    Write-Host "PenguinClaw.rhp created successfully."
} else {
    Write-Host "Source DLL not found: $source"
}
