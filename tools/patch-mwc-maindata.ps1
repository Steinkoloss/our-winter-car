# Disable Unity 5 ScreenSelector in My Winter Car (displayResolutionDialog Enabled -> Disabled).
# Registry and -screen-* args alone do not skip the native "Play!" dialog on this build.
param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\My Winter Car"
)

$ErrorActionPreference = "Stop"
$path = Join-Path $GameDir "mywintercar_Data\mainData"
if (-not (Test-Path $path)) {
    Write-Error "mainData not found: $path"
}

$offset = 4224
$bytes = [IO.File]::ReadAllBytes($path)
if ($bytes.Length -lt ($offset + 4)) {
    Write-Error "mainData too small ($($bytes.Length) bytes) - game update may have changed layout."
}

$text = [Text.Encoding]::ASCII.GetString($bytes)
if ($text -notmatch 'Amistech' -or $text -notmatch 'My Winter Car') {
    Write-Error "mainData does not look like My Winter Car."
}

$current = [BitConverter]::ToInt32($bytes, $offset)
if ($current -eq 0) {
    Write-Host "Resolution dialog already disabled in mainData."
    exit 0
}
if ($current -ne 1 -and $current -ne 2) {
    Write-Error "Unexpected displayResolutionDialog value $current at offset $offset."
}

$backup = "$path.wintermp-original"
if (-not (Test-Path $backup)) {
    Copy-Item $path $backup
}

[BitConverter]::GetBytes([int32]0).CopyTo($bytes, $offset)
[IO.File]::WriteAllBytes($path, $bytes)
Write-Host "Patched mainData: displayResolutionDialog $current -> 0 (Disabled)."
