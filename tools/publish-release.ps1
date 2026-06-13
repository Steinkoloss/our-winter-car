# Builds a self-contained launcher + release zips for GitHub.
# Output: dist/OurWinterCar-Launcher-win-x64.zip, dist/OurWinterCar-payload.zip
param(
    [switch]$SkipRestore
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

function Write-Step([string]$Message) {
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-TimedStep([string]$Label, [scriptblock]$Action) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        & $Action
        if ($LASTEXITCODE -ne 0) { throw "exit $LASTEXITCODE" }
    }
    catch {
        throw "$Label failed: $($_.Exception.Message)"
    }
    Write-Host "    done in $($sw.Elapsed.TotalSeconds.ToString('0.0'))s" -ForegroundColor DarkGray
}

function New-ReleaseZip([string]$SourceDir, [string]$ZipPath) {
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    $tar = Get-Command tar -ErrorAction SilentlyContinue
    if ($null -ne $tar) {
        Push-Location $SourceDir
        try {
            & tar -a -cf $ZipPath *
            if ($LASTEXITCODE -ne 0) { throw "tar exit $LASTEXITCODE" }
        }
        finally {
            Pop-Location
        }
        return
    }

    Compress-Archive -Path (Join-Path $SourceDir "*") -DestinationPath $ZipPath
}

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

function Invoke-ParallelModBuilds {
    param([string[]]$DotnetArgs)

    $coreProj = Join-Path $root "src\WinterMP.Core\WinterMP.Core.csproj"
    $fastBootProj = Join-Path $root "src\WinterMP.FastBoot\WinterMP.FastBoot.csproj"

    $buildBlock = {
        param($WorkRoot, $Project, [string[]]$Args)
        Set-Location $WorkRoot
        & dotnet build $Project @Args
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed: $Project" }
    }

    $coreJob = Start-Job -ScriptBlock $buildBlock -ArgumentList $root, $coreProj, $DotnetArgs
    $fbJob = Start-Job -ScriptBlock $buildBlock -ArgumentList $root, $fastBootProj, $DotnetArgs
    Wait-Job $coreJob, $fbJob | Out-Null

    try {
        Receive-Job $coreJob, $fbJob -ErrorAction Stop | Out-Null
    }
    catch {
        Receive-Job $coreJob, $fbJob -ErrorAction SilentlyContinue | Write-Host
        throw
    }
    finally {
        Remove-Job $coreJob, $fbJob -Force -ErrorAction SilentlyContinue
    }
}

Sync-CompatManifest
& (Join-Path $PSScriptRoot "fetch-vendor.ps1")

$dotnetArgs = @('-c', 'Release', '-p:DeployToGame=false', '-v', 'q', '/clp:ErrorsOnly')
if ($SkipRestore) { $dotnetArgs += '--no-restore' }

if (-not $SkipRestore) {
    Invoke-TimedStep "dotnet restore" {
        dotnet restore (Join-Path $root "src\WinterMP.Core\WinterMP.Core.csproj") -v q
    }
    $dotnetArgs += '--no-restore'
}

Invoke-TimedStep "mod builds (parallel)" {
    Invoke-ParallelModBuilds -DotnetArgs $dotnetArgs
}

$launcherProj = Join-Path $root "src\WinterMP.Launcher\WinterMP.Launcher.csproj"
$publishDir = Join-Path $root "src\WinterMP.Launcher\bin\publish\win-x64"

Invoke-TimedStep "launcher publish" {
    dotnet publish $launcherProj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false -p:DeployToGame=false -v q /clp:ErrorsOnly `
        --no-restore -o $publishDir
}

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$launcherZip = Join-Path $dist "OurWinterCar-Launcher-win-x64.zip"
$payloadDir = Join-Path $publishDir "payload"
$payloadZip = Join-Path $dist "OurWinterCar-payload.zip"

Invoke-TimedStep "launcher zip" { New-ReleaseZip $publishDir $launcherZip }
Invoke-TimedStep "payload zip" { New-ReleaseZip $payloadDir $payloadZip }

Write-Host ""
Write-Host "Published:"
Write-Host "  Launcher: $launcherZip"
Write-Host "  Payload:  $payloadZip  (attach to GitHub release for in-launcher updates)"
Write-Host "  Folder:   $publishDir"
