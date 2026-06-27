# Resolve the My Winter Car install used by Local2PTest and dotnet deploy.
# Priority: WINTERMP_GAME_DIR env > Directory.Build.props.user > common installs with BepInEx.
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

if ($env:WINTERMP_GAME_DIR -and (Test-Path (Join-Path $env:WINTERMP_GAME_DIR 'mywintercar.exe'))) {
    Write-Output $env:WINTERMP_GAME_DIR
    exit 0
}

$propsUser = Join-Path $RepoRoot 'Directory.Build.props.user'
if (Test-Path $propsUser) {
    # SelectSingleNode (not $xml.Project.PropertyGroup.MwcGamePath) so a props file
    # with multiple <PropertyGroup> elements returns the single value, not an array.
    $xml = [xml](Get-Content -Raw $propsUser)
    $node = $xml.SelectSingleNode('//MwcGamePath')
    if ($node) {
        $path = $node.InnerText.Trim()
        if ($path -and (Test-Path (Join-Path $path 'mywintercar.exe'))) {
            Write-Output $path
            exit 0
        }
    }
}

$candidates = @(
    'C:\Program Files (x86)\Steam\steamapps\common\MWCO TESTING',
    'C:\Program Files (x86)\Steam\steamapps\common\My Winter Car'
)

foreach ($candidate in $candidates) {
    $exe = Join-Path $candidate 'mywintercar.exe'
    $bepinex = Join-Path $candidate 'BepInEx'
    $core = Join-Path $candidate 'BepInEx\plugins\WinterMP\WinterMP.Core.dll'
    if ((Test-Path $exe) -and (Test-Path $bepinex) -and (Test-Path $core)) {
        Write-Output $candidate
        exit 0
    }
}

foreach ($candidate in $candidates) {
    if (Test-Path (Join-Path $candidate 'mywintercar.exe')) {
        Write-Output $candidate
        exit 0
    }
}

Write-Error 'Could not resolve a My Winter Car install. Set WINTERMP_GAME_DIR or Directory.Build.props.user MwcGamePath.'
