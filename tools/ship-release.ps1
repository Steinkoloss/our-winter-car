# One-click release: bump version, build, commit, push, and publish a GitHub release.
# Usage:
#   .\tools\ship-release.ps1                  # bump patch, ship
#   .\tools\ship-release.ps1 -Bump minor      # 0.1.5 -> 0.2.0
#   .\tools\ship-release.ps1 -SkipBump        # ship current version (rebuild only)
#   .\tools\ship-release.ps1 -Notes "Fixed X" # override "What's new" bullets
param(
    [ValidateSet('patch', 'minor', 'major')]
    [string]$Bump = 'patch',
    [string]$Notes = '',
    [switch]$SkipBump,
    [switch]$NonInteractive
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Checked {
    param(
        [scriptblock]$Command,
        [string]$FailureMessage
    )
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

function Require-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command not found: $Name"
    }
}

function Get-ProjectVersion {
    $coreProj = Join-Path $root 'src\WinterMP.Core\WinterMP.Core.csproj'
    $match = [regex]::Match((Get-Content $coreProj -Raw), '<Version>([\d.]+)</Version>')
    if (-not $match.Success) { throw "Could not read version from WinterMP.Core.csproj" }
    return $match.Groups[1].Value
}

function Bump-SemVer([string]$Version, [string]$Part) {
    $segments = $Version.Split('.')
    if ($segments.Count -lt 3) { throw "Expected semver like 0.1.5, got $Version" }
    [int]$major = $segments[0]
    [int]$minor = $segments[1]
    [int]$patch = $segments[2]
    switch ($Part) {
        'major' { $major++; $minor = 0; $patch = 0 }
        'minor' { $minor++; $patch = 0 }
        'patch' { $patch++ }
    }
    return "$major.$minor.$patch"
}

function Set-ProjectVersion([string]$Version) {
    $coreProj = Join-Path $root 'src\WinterMP.Core\WinterMP.Core.csproj'
    $launcherProj = Join-Path $root 'src\WinterMP.Launcher\WinterMP.Launcher.csproj'
    $iss = Join-Path $root 'installer\WinterMP.iss'
    $compatFile = Join-Path $root 'src\WinterMP.Launcher\Assets\wintermp-compat.json'

    foreach ($file in @($coreProj, $launcherProj)) {
        $text = Get-Content $file -Raw
        $text = [regex]::Replace($text, '(<Version>)[\d.]+(</Version>)', "`${1}$Version`${2}")
        [System.IO.File]::WriteAllText($file, $text)
    }

    $issText = Get-Content $iss -Raw
    $issText = [regex]::Replace($issText, '(AppVersion=)[\d.]+', "`${1}$Version")
    [System.IO.File]::WriteAllText($iss, $issText)

    $compat = Get-Content $compatFile -Raw | ConvertFrom-Json
    $compat.modVersion = $Version
    $compat | ConvertTo-Json -Depth 4 | Set-Content $compatFile -Encoding utf8
}

function Sync-ProtocolVersion {
    $protocolFile = Join-Path $root 'src\WinterMP.Net\Protocol.cs'
    $compatFile = Join-Path $root 'src\WinterMP.Launcher\Assets\wintermp-compat.json'
    $match = [regex]::Match((Get-Content $protocolFile -Raw), 'Version\s*=\s*(\d+)')
    if (-not $match.Success) { throw 'Could not read protocol version from Protocol.cs' }

    $protocol = [int]$match.Groups[1].Value
    $compat = Get-Content $compatFile -Raw | ConvertFrom-Json
    $compat.protocolVersion = $protocol
    $compat | ConvertTo-Json -Depth 4 | Set-Content $compatFile -Encoding utf8
    return $protocol
}

function Get-ProtocolVersion {
    $protocolFile = Join-Path $root 'src\WinterMP.Net\Protocol.cs'
    $match = [regex]::Match((Get-Content $protocolFile -Raw), 'Version\s*=\s*(\d+)')
    if (-not $match.Success) { throw 'Could not read protocol version from Protocol.cs' }
    return [int]$match.Groups[1].Value
}

function Get-RecentChangeSubjects {
    git fetch --tags origin 2>$null | Out-Null
    $lastTag = git describe --tags --abbrev=0 2>$null
    $range = if ($lastTag) { "$lastTag..HEAD" } else { 'HEAD' }
    return @(git log $range --pretty=format:'%s' 2>$null | Where-Object { $_ -and $_ -notmatch '^Release v' })
}

function Get-ReleaseBullets {
    param([string]$Version)

    if ($Notes.Trim().Length -gt 0) {
        return ($Notes.Trim() -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ } | ForEach-Object {
            if ($_ -match '^-\s') { $_ } else { "- $_" }
        }) -join "`n"
    }

    $lines = Get-RecentChangeSubjects
    if ($lines.Count -eq 0) {
        return "- Release **v$Version**"
    }

    return ($lines | ForEach-Object { "- $_" }) -join "`n"
}

function Get-CommitSummary {
    param([string]$Version)

    if ($Notes.Trim().Length -gt 0) {
        $first = ($Notes.Trim() -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -First 1)
        $first = $first -replace '^-\s*', ''
        return "Release v$Version`: $first"
    }

    $lines = Get-RecentChangeSubjects
    if ($lines.Count -eq 0) {
        return "Release v$Version."
    }

    return "Release v$Version`: $($lines[0])."
}

function Build-ReleaseNotes {
    param(
        [string]$Version,
        [int]$Protocol,
        [string]$Bullets
    )

    $repo = 'Steinkoloss/our-winter-car'
    $tag = "v$Version"
    $setupUrl = "https://github.com/$repo/releases/download/$tag/OurWinterCar-Setup.exe"
    $zipUrl = "https://github.com/$repo/releases/download/$tag/OurWinterCar-Launcher-win-x64.zip"

    return @"
## Download

**[OurWinterCar-Setup.exe]($setupUrl)**

If Windows Defender removes the installer, use **[OurWinterCar-Launcher-win-x64.zip]($zipUrl)** from this release instead.

All players must use **v$Version** (protocol **v$Protocol**). Older builds will be refused at handshake.

## What's new

$Bullets

## Quick start

1. Run **OurWinterCar-Setup.exe** (or unzip the launcher zip if Defender blocks the exe)
2. Open **Our Winter Car** - install runs automatically if needed
3. Click **HOST GAME**
4. Friends: same build, then Steam **Join Game**

Guests must never save — only the host saves.
"@
}

Write-Host ""
Write-Host "Our Winter Car - ship release" -ForegroundColor Green
Write-Host "Repo: $root"

Require-Command git
Require-Command gh
Require-Command dotnet

$branch = (git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne 'main') {
    if ($NonInteractive) {
        throw "Refusing to ship from branch '$branch'. Switch to main first."
    }
    Write-Host ""
    Write-Host "Warning: you are on '$branch', not main." -ForegroundColor Yellow
    $answer = Read-Host "Continue anyway? [y/N]"
    if ($answer -notmatch '^[yY]') { throw 'Aborted.' }
}

$currentVersion = Get-ProjectVersion
$version = if ($SkipBump) { $currentVersion } else { Bump-SemVer $currentVersion $Bump }

Write-Step "Version: v$currentVersion -> v$version"
if (-not $SkipBump) {
    Set-ProjectVersion $version
}
$protocol = Sync-ProtocolVersion
Write-Host "Protocol: v$protocol"

$status = git status --porcelain
if ($status) {
    Write-Step "Working tree has changes (will be included in the release commit)"
    git status --short
} else {
    Write-Host "Working tree clean before build."
}

if (-not $NonInteractive) {
    Write-Host ""
    Write-Host "About to build v$version, commit, push, and create GitHub release v$version."
    $answer = Read-Host "Continue? [Y/n]"
    if ($answer -match '^[nN]') { throw 'Aborted.' }
}

Write-Step "Building installer and release zips"
Invoke-Checked { & (Join-Path $PSScriptRoot 'build-installer.ps1') } 'Build failed.'

$setup = Join-Path $root 'dist\OurWinterCar-Setup.exe'
$payload = Join-Path $root 'dist\OurWinterCar-payload.zip'
$launcher = Join-Path $root 'dist\OurWinterCar-Launcher-win-x64.zip'

foreach ($asset in @($setup, $payload, $launcher)) {
    if (-not (Test-Path $asset)) {
        throw "Missing build output: $asset"
    }
}

Write-Step "Committing release"
git add -A
$pending = git diff --cached --name-only
if (-not $pending) {
    throw 'Nothing staged to commit.'
}
Invoke-Checked { git commit -m (Get-CommitSummary -Version $version) } 'Commit failed.'

Write-Step "Pushing to origin"
Invoke-Checked { git push origin HEAD } 'Push failed.'

$tag = "v$version"
$title = "v$version - Our Winter Car"
$bullets = Get-ReleaseBullets -Version $version
$body = Build-ReleaseNotes -Version $version -Protocol $protocol -Bullets $bullets
$notesFile = Join-Path $env:TEMP "our-winter-car-release-$version.md"
Set-Content $notesFile $body -Encoding utf8

Write-Step "Publishing GitHub release $tag"
$existing = gh release view $tag 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "Release $tag already exists - updating notes and assets."
    Invoke-Checked { gh release edit $tag --title $title --notes-file $notesFile } 'Release edit failed.'
    Invoke-Checked { gh release upload $tag $setup $payload $launcher --clobber } 'Release upload failed.'
} else {
    Invoke-Checked {
        gh release create $tag `
            --title $title `
            --notes-file $notesFile `
            $setup $payload $launcher
    } 'Release create failed.'
}

Remove-Item $notesFile -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Shipped v$version (protocol v$protocol)." -ForegroundColor Green
Write-Host "Release: https://github.com/Steinkoloss/our-winter-car/releases/tag/$tag"
Write-Host "Friends can update from the launcher in a few minutes."
