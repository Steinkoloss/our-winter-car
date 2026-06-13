# Creates a desktop shortcut that runs tools\ship-release.ps1 with one double-click.
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$shipScript = Join-Path $PSScriptRoot 'ship-release.ps1'
$desktop = [Environment]::GetFolderPath('Desktop')
$batPath = Join-Path $desktop 'Ship Our Winter Car.bat'

if (-not (Test-Path $shipScript)) {
    throw "Missing ship script: $shipScript"
}

$bat = @"
@echo off
title Ship Our Winter Car
color 0A
echo.
echo  Our Winter Car - one-click release
echo  ===================================
echo.
echo  This will:
echo    1. Bump the patch version
echo    2. Build update zips (fast — no installer)
echo    3. Commit, push, and publish to GitHub
echo.
echo  For a full installer build, run tools\ship-release.ps1 without -Fast.
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$shipScript" -NonInteractive -Fast -SkipFetch
if errorlevel 1 (
    echo.
    echo  SHIP FAILED - see errors above.
    color 0C
    pause
    exit /b 1
)
echo.
echo  All done.
pause
"@

Set-Content $batPath $bat -Encoding ascii
Write-Host "Created desktop launcher:"
Write-Host "  $batPath"
Write-Host ""
Write-Host "Double-click 'Ship Our Winter Car' on your desktop to publish a release."
