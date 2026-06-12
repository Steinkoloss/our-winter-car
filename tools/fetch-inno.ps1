# Downloads Inno Setup 6 into tools/inno-setup/ for building WinterMP-Setup.exe (gitignored).
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$innoDir = Join-Path $PSScriptRoot "inno-setup"
$iscc = Join-Path $innoDir "ISCC.exe"

if (Test-Path $iscc) {
    Write-Host "Inno Setup already present: $iscc"
    exit 0
}

$onPath = Get-Command iscc -ErrorAction SilentlyContinue
foreach ($path in @(
    $(if ($onPath) { $onPath.Source } else { $null }),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
)) {
    if ($path -and (Test-Path $path)) {
        Write-Host "Inno Setup found: $path"
        exit 0
    }
}

Write-Host "Inno Setup not found. Installing via winget..."
$winget = Get-Command winget -ErrorAction SilentlyContinue
if ($null -eq $winget) {
    throw "Install Inno Setup from https://jrsoftware.org/isdl.php (needed to build WinterMP-Setup.exe)"
}

& winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements --silent | Out-Host
if (-not (Test-Path (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"))) {
    throw "winget install finished but ISCC.exe was not found."
}
Write-Host "Inno Setup installed."
