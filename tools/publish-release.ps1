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

function Read-ProjectVersion([string]$ProjectFile) {
    if (-not (Test-Path $ProjectFile)) { return $null }
    $match = [regex]::Match((Get-Content $ProjectFile -Raw), '<Version>([^<]+)</Version>')
    if (-not $match.Success) { return $null }
    return $match.Groups[1].Value.Trim()
}

function Sync-CompatManifest {
    $protocolFile = Join-Path $root "src\WinterMP.Net\Protocol.cs"
    $coreProj = Join-Path $root "src\WinterMP.Core\WinterMP.Core.csproj"
    $compatFile = Join-Path $root "src\WinterMP.Launcher\Assets\wintermp-compat.json"
    if (-not (Test-Path $compatFile)) { return }

    $compat = Get-Content $compatFile -Raw | ConvertFrom-Json
    $changed = $false

    $modVersion = Read-ProjectVersion $coreProj
    if ($modVersion -and $compat.modVersion -ne $modVersion) {
        $compat.modVersion = $modVersion
        $changed = $true
        Write-Host "Synced wintermp-compat.json modVersion -> $modVersion"
    }

    if (Test-Path $protocolFile) {
        $match = [regex]::Match((Get-Content $protocolFile -Raw), 'Version\s*=\s*(\d+)')
        if ($match.Success) {
            $protocol = [int]$match.Groups[1].Value
            if ([int]$compat.protocolVersion -ne $protocol) {
                $compat.protocolVersion = $protocol
                $changed = $true
                Write-Host "Synced wintermp-compat.json protocolVersion -> $protocol"
            }
        }
    }

    if ($changed) {
        $compat | ConvertTo-Json -Depth 4 | Set-Content $compatFile -Encoding utf8
    }
}

function Normalize-VersionText([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    try {
        $v = [Version]$Text.Trim()
        return "$($v.Major).$($v.Minor).$([Math]::Max($v.Build, 0))"
    }
    catch {
        return $Text.Trim()
    }
}

function Assert-ReleasePayload {
    param([string]$PublishDir)

    $payloadDir = Join-Path $PublishDir "payload"
    $coreDll = Join-Path $payloadDir "WinterMP.Core.dll"
    $compatPath = Join-Path $payloadDir "wintermp-compat.json"

    if (-not (Test-Path $coreDll)) {
        throw "Release payload missing WinterMP.Core.dll (mod build did not run?)"
    }
    if (-not (Test-Path $compatPath)) {
        throw "Release payload missing wintermp-compat.json"
    }

    $dllVersion = (Get-Item $coreDll).VersionInfo.FileVersion
    $compat = Get-Content $compatPath -Raw | ConvertFrom-Json
    $manifestVersion = [string]$compat.modVersion

    $dllNorm = Normalize-VersionText $dllVersion
    $manifestNorm = Normalize-VersionText $manifestVersion
    if ($dllNorm -ne $manifestNorm) {
        throw "Payload version mismatch: WinterMP.Core.dll is v$dllVersion but wintermp-compat.json says v$manifestVersion. " +
              "This usually means an incremental build skipped recompiling after a version bump; rebuild with -t:Rebuild."
    }

    Write-Host "Payload OK: Core.dll v$dllVersion matches manifest v$manifestVersion"
}

function Invoke-ModBuilds {
    param([string[]]$DotnetArgs)

    $coreProj = Join-Path $root "src\WinterMP.Core\WinterMP.Core.csproj"
    $fastBootProj = Join-Path $root "src\WinterMP.FastBoot\WinterMP.FastBoot.csproj"

    # Run in-process so Directory.Build.props.user (MwcGamePath) is always picked up.
    dotnet build $coreProj @DotnetArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed: $coreProj" }
    dotnet build $fastBootProj @DotnetArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed: $fastBootProj" }
}

Sync-CompatManifest
& (Join-Path $PSScriptRoot "fetch-vendor.ps1")

# Version-only bumps do not invalidate incremental builds; always rebuild mod DLLs for release.
$dotnetArgs = @('-c', 'Release', '-p:DeployToGame=false', '-v', 'q', '/clp:ErrorsOnly', '-t:Rebuild')
if ($SkipRestore) { $dotnetArgs += '--no-restore' }

if (-not $SkipRestore) {
    Invoke-TimedStep "dotnet restore" {
        dotnet restore (Join-Path $root "src\WinterMP.Core\WinterMP.Core.csproj") -v q
    }
    $dotnetArgs += '--no-restore'
}

Invoke-TimedStep "mod builds" {
    Invoke-ModBuilds -DotnetArgs $dotnetArgs
}

$launcherProj = Join-Path $root "src\WinterMP.Launcher\WinterMP.Launcher.csproj"
$publishDir = Join-Path $root "src\WinterMP.Launcher\bin\publish\win-x64"

Invoke-TimedStep "launcher publish" {
    dotnet publish $launcherProj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false -p:DeployToGame=false -v q /clp:ErrorsOnly `
        --no-restore -o $publishDir
}

Invoke-TimedStep "payload verify" {
    Assert-ReleasePayload -PublishDir $publishDir
}

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$launcherZip = Join-Path $dist "OurWinterCar-Launcher-win-x64.zip"
$payloadDir = Join-Path $publishDir "payload"
$payloadZip = Join-Path $dist "OurWinterCar-payload.zip"

Invoke-TimedStep "launcher zip" { New-ReleaseZip $publishDir $launcherZip }
Invoke-TimedStep "payload zip" { New-ReleaseZip $payloadDir $payloadZip }

# AppImage — requires Linux or WSL2 (appimagetool is a Linux binary).
$appImage = Join-Path $dist "OurWinterCar-Launcher-linux-x64.AppImage"
$hasWSL2 = $false
if ($IsWindows) {
    try {
        $null = wsl --list --verbose 2>$null
        $hasWSL2 = $LASTEXITCODE -eq 0
    } catch { }
}
if ($IsLinux -or $hasWSL2) {
    Invoke-TimedStep "AppImage (linux-x64)" {
        if ($IsLinux) {
            bash (Join-Path $PSScriptRoot "build-appimage.sh")
        } else {
            wsl bash (Join-Path $PSScriptRoot "build-appimage.sh")
        }
        if ($LASTEXITCODE -ne 0) { throw "AppImage build failed" }
    }
}

Write-Host ""
Write-Host "Published:"
Write-Host "  Launcher (win): $launcherZip"
Write-Host "  Payload:        $payloadZip  (attach to GitHub release for in-launcher updates)"
Write-Host "  Folder:         $publishDir"
if ($IsLinux -and (Test-Path $appImage)) {
    Write-Host "  AppImage:       $appImage"
}
