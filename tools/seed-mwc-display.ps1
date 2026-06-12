# Pre-seed Unity 5 display PlayerPrefs for My Winter Car.
# Registry alone does NOT skip the native "Play!" ScreenSelector — the exe also needs
#   -screen-width / -screen-height / -screen-fullscreen / -screen-quality
# on the command line (see Local2PTest.bat). Used here for persistence + dev scripts.
param(
    [int]$Width = 1280,
    [int]$Height = 720,
    [int]$Fullscreen = 0,
    [int]$Quality = 5,
    [int]$Monitor = 0
)

$ErrorActionPreference = "Stop"
$path = "HKCU:\Software\Amistech\My Winter Car"

if (-not (Test-Path $path)) {
    New-Item -Path $path -Force | Out-Null
}

Set-ItemProperty -Path $path -Name "Screenmanager Resolution Width_h182942802" -Value $Width -Type DWord
Set-ItemProperty -Path $path -Name "Screenmanager Resolution Height_h2627697771" -Value $Height -Type DWord
Set-ItemProperty -Path $path -Name "Screenmanager Is Fullscreen mode_h3981298716" -Value $Fullscreen -Type DWord
Set-ItemProperty -Path $path -Name "UnityGraphicsQuality_h1669003810" -Value $Quality -Type DWord
Set-ItemProperty -Path $path -Name "UnitySelectMonitor_h17969598" -Value $Monitor -Type DWord

Write-Host "Seeded MWC display prefs: ${Width}x${Height} fullscreen=$Fullscreen quality=$Quality monitor=$Monitor"
