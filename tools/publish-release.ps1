# Builds a self-contained launcher + release zips for GitHub.
# Output: dist/WinterMP-Launcher-win-x64.zip, dist/WinterMP-payload.zip
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

$launcherProj = Join-Path $root "src\WinterMP.Launcher\WinterMP.Launcher.csproj"
$publishDir = Join-Path $root "src\WinterMP.Launcher\bin\publish\win-x64"

dotnet publish $launcherProj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -o $publishDir

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$launcherZip = Join-Path $dist "WinterMP-Launcher-win-x64.zip"
if (Test-Path $launcherZip) { Remove-Item $launcherZip -Force }
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $launcherZip

$payloadDir = Join-Path $publishDir "payload"
$payloadZip = Join-Path $dist "WinterMP-payload.zip"
if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
Compress-Archive -Path (Join-Path $payloadDir "*") -DestinationPath $payloadZip

Write-Host ""
Write-Host "Published:"
Write-Host "  Launcher: $launcherZip"
Write-Host "  Payload:  $payloadZip  (attach to GitHub release for in-launcher updates)"
Write-Host "  Folder:   $publishDir"
