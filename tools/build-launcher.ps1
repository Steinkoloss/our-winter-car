# Builds WinterMP.Core + launcher with bundled mod payload and BepInEx zip.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

function Sync-CompatManifest {
    $protocolFile = Join-Path $root "src\WinterMP.Net\Protocol.cs"
    $compatFile = Join-Path $root "src\WinterMP.Launcher\Assets\wintermp-compat.json"
    if (-not (Test-Path $protocolFile) -or -not (Test-Path $compatFile)) { return }
    $match = [regex]::Match((Get-Content $protocolFile -Raw), 'Version\s*=\s*(\d+)')
    if (-not $match.Success) { return }
    $protocol = [int]$match.Groups[1].Value
    $compat = Get-Content $compatFile -Raw | ConvertFrom-Json
    if ([int]$compat.protocolVersion -ne $protocol) {
        $compat.protocolVersion = $protocol
        $compat | ConvertTo-Json -Depth 4 | Set-Content $compatFile -Encoding utf8
        Write-Host "Synced wintermp-compat.json protocolVersion -> $protocol"
    }
}

Sync-CompatManifest
& (Join-Path $PSScriptRoot "fetch-vendor.ps1")

dotnet build (Join-Path $root "src\WinterMP.Core\WinterMP.Core.csproj") -c Release
dotnet build (Join-Path $root "src\WinterMP.Launcher\WinterMP.Launcher.csproj") -c Release

$out = Join-Path $root "src\WinterMP.Launcher\bin\Release\net8.0-windows"
Write-Host ""
Write-Host "Launcher ready: $out\WinterMPLauncher.exe"
Write-Host "Zip that folder (or run Inno Setup on installer\WinterMP.iss) to distribute."
