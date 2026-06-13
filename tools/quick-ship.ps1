# Fast ship: zips only (no Inno installer). Friends update via launcher payload zip.
# Use full ship-release.ps1 without -Fast when you need OurWinterCar-Setup.exe for new installs.
param(
    [ValidateSet('patch', 'minor', 'major')]
    [string]$Bump = 'patch',
    [string]$Notes = '',
    [switch]$SkipBump
)

$shipScript = Join-Path $PSScriptRoot 'ship-release.ps1'
& $shipScript -NonInteractive -Fast -SkipFetch @PSBoundParameters
