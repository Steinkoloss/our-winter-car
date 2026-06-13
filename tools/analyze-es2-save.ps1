param(
    [string]$SavePath = "$env:USERPROFILE\AppData\LocalLow\Amistech\My Winter Car\savefile.txt",
    [string]$OutPath = ""
)

if (-not (Test-Path $SavePath)) {
    Write-Error "Save not found: $SavePath"
    exit 1
}

$bytes = [IO.File]::ReadAllBytes($SavePath)
$ascii = [Text.Encoding]::ASCII.GetString($bytes)
$tags = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)

foreach ($m in [regex]::Matches($ascii, '[\x20-\x7E]{3,80}')) {
    $s = $m.Value
    if ($s.StartsWith('~') -or $s.StartsWith('?') -or $s.Contains('{') -or $s.Contains('<')) { continue }
    if ($s -match '^(bool|int|float|string)\(') { continue }
    if ($s.EndsWith('~')) { $s = $s.Substring(0, $s.Length - 1) }
    if ($s.Length -ge 3) { [void]$tags.Add($s) }
}

$sorted = $tags | Sort-Object
$report = @(
    "=== ES2 save tag report (offline) ==="
    "  File: $SavePath"
    "  Size: $($bytes.Length) bytes"
    "  Tag count: $($sorted.Count)"
    "--- tags ---"
)
$report += $sorted | ForEach-Object { "  $_" }

$text = $report -join [Environment]::NewLine
Write-Output $text

if ($OutPath) {
    $dir = Split-Path $OutPath -Parent
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    Set-Content -Path $OutPath -Value $text -Encoding UTF8
    Write-Host "Wrote $OutPath"
}
