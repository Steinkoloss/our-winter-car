# One-click dev setup (Windows): install/repair BepInEx + mod into My Winter Car, then put
# Local2PTest on the desktop. Run from repo root on Windows:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\install-desktop-local2p.ps1
#
# On Linux (Steam/Proton): use tools/install-desktop-local2p.sh instead.
param(
    [string]$GameDir = $env:WINTERMP_GAME_DIR
)

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$tools = $PSScriptRoot
$local2p = Join-Path $tools 'Local2PTest.bat'
$userProps = Join-Path $root 'Directory.Build.props.user'
$exampleProps = Join-Path $root 'Directory.Build.props.user.example'
$defaultGame = 'C:\Program Files (x86)\Steam\steamapps\common\My Winter Car'
$desktop = [Environment]::GetFolderPath('Desktop')
$desktopBat = Join-Path $desktop 'Local 2P Test.bat'

function Read-MwcGamePath([string]$propsFile) {
    if (-not (Test-Path $propsFile)) { return $null }
    $match = [regex]::Match((Get-Content $propsFile -Raw), '<MwcGamePath>([^<]+)</MwcGamePath>')
    if ($match.Success) { return $match.Groups[1].Value.Trim() }
    return $null
}

function Set-MwcGamePath([string]$propsFile, [string]$exampleFile, [string]$path) {
    if (-not (Test-Path $propsFile)) {
        if (-not (Test-Path $exampleFile)) {
            throw "Missing template: $exampleFile"
        }
        Copy-Item $exampleFile $propsFile
    }

    $content = Get-Content $propsFile -Raw
    $escaped = [regex]::Escape($path)
    if ($content -match '<MwcGamePath>') {
        $content = [regex]::Replace($content, '<MwcGamePath>[^<]*</MwcGamePath>', "<MwcGamePath>$path</MwcGamePath>")
    }
    else {
        $content = $content -replace '</Project>', "  <PropertyGroup>`r`n    <MwcGamePath>$path</MwcGamePath>`r`n  </PropertyGroup>`r`n</Project>"
    }

    Set-Content $propsFile $content.TrimEnd() -Encoding utf8
}

function Resolve-GameDir {
    param([string]$Override)

    if ($Override) { return $Override.Trim() }

    $fromProps = Read-MwcGamePath $userProps
    if ($fromProps) { return $fromProps }

    return $defaultGame
}

function Test-GameInstall([string]$path) {
    return (Test-Path (Join-Path $path 'mywintercar.exe'))
}

if (-not (Test-Path $local2p)) {
    throw "Missing Local2P script: $local2p"
}

$gameDir = Resolve-GameDir -Override $GameDir
if (-not (Test-GameInstall $gameDir)) {
    throw @"
My Winter Car not found at:
  $gameDir

Install the game via Steam, or pass your folder:
  powershell -File tools\install-desktop-local2p.ps1 -GameDir 'D:\Steam\steamapps\common\My Winter Car'
"@
}

Write-Host ''
Write-Host 'Our Winter Car - mod + local 2P desktop setup' -ForegroundColor Cyan
Write-Host '=============================================' -ForegroundColor Cyan
Write-Host "Game: $gameDir"
Write-Host ''

Set-MwcGamePath $userProps $exampleProps $gameDir
Write-Host '[1/4] Directory.Build.props.user ready.'

& (Join-Path $tools 'fetch-vendor.ps1')
Write-Host '[2/4] Vendor packages ready.'

& (Join-Path $tools 'build-launcher.ps1')
$launcherExe = Join-Path $root 'src\WinterMP.Launcher\bin\Release\net8.0-windows\WinterMPLauncher.exe'
if (-not (Test-Path $launcherExe)) {
    throw "Launcher build failed — expected $launcherExe"
}

Write-Host '[3/4] Installing BepInEx + mod into the game...'
$installArgs = @(
    '--install-mod',
    '--silent',
    '--game-dir', $gameDir
)
& $launcherExe @installArgs
if ($LASTEXITCODE -ne 0) {
    throw "Mod install failed (exit $LASTEXITCODE). See %LOCALAPPDATA%\WinterMP\last-install.log"
}

$coreProj = Join-Path $root 'src\WinterMP.Core\WinterMP.Core.csproj'
$toolsProj = Join-Path $root 'src\WinterMP.Tools\WinterMP.Tools.csproj'
dotnet build $coreProj -c Release | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'WinterMP.Core build failed.' }
dotnet build $toolsProj -c Release | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'WinterMP.Tools build failed.' }
Write-Host '      Latest dev DLLs deployed from source.'

$gameForBat = $gameDir -replace "'", "''"
$local2pForBat = $local2p -replace "'", "''"

$bat = @"
@echo off
title Our Winter Car - Local 2P Test
set "WINTERMP_GAME_DIR=$gameForBat"
call "$local2pForBat"
"@

Set-Content $desktopBat $bat -Encoding ascii
Write-Host "[4/4] Desktop launcher created:"
Write-Host "      $desktopBat"
Write-Host ''
Write-Host 'Done. Double-click "Local 2P Test" on your desktop to start host + guest.'
Write-Host 'Rebuild Core anytime — DLLs auto-deploy when MwcGamePath is set.'
