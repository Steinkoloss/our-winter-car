# Builds self-contained launcher + WinterMP-Setup.exe installer.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

& (Join-Path $PSScriptRoot "publish-release.ps1")
& (Join-Path $PSScriptRoot "fetch-inno.ps1")

$iscc = $null
$onPath = Get-Command iscc -ErrorAction SilentlyContinue
if ($null -ne $onPath) {
    $iscc = $onPath.Source
} else {
    $candidates = @(
        (Join-Path $PSScriptRoot "inno-setup\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    )
    foreach ($path in $candidates) {
        if (Test-Path $path) { $iscc = $path; break }
    }
}

if ($null -eq $iscc) {
    Write-Host ""
    Write-Host "Inno Setup compiler not found."
    Write-Host "Install from https://jrsoftware.org/isdl.php or run tools\fetch-inno.ps1"
    Write-Host "Portable zip alternative: dist\WinterMP-Launcher-win-x64.zip"
    exit 1
}

$iss = Join-Path $root "installer\WinterMP.iss"
& $iscc /Qp $iss

$setup = Join-Path $root "dist\OurWinterCar-Setup.exe"
Write-Host ""
if (Test-Path $setup) {
    Write-Host "Installer ready: $setup"
    Write-Host "Send OurWinterCar-Setup.exe to friends. Setup installs the launcher and mod automatically."
} else {
    throw "Build finished but $setup was not created."
}
