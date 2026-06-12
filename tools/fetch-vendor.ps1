# Downloads third-party packages bundled by the WinterMP launcher installer.
$ErrorActionPreference = "Stop"
$vendor = Join-Path (Join-Path $PSScriptRoot "..") "vendor"
New-Item -ItemType Directory -Force -Path $vendor | Out-Null

$zip = Join-Path $vendor "BepInEx_win_x64_5.4.23.5.zip"
if (Test-Path $zip) {
    Write-Host "Already present: $zip"
    exit 0
}

$url = "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip"
Write-Host "Downloading BepInEx..."
Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
Write-Host "Saved $zip ($((Get-Item $zip).Length) bytes)"
