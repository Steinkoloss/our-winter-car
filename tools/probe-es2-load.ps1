$managed = "C:\Program Files (x86)\Steam\steamapps\common\My Winter Car\mywintercar_Data\Managed"
$asm = [Reflection.Assembly]::LoadFrom("$managed\ES2.dll")
$es2 = $asm.GetType("ES2")
$types = @(
    [float], [int], [bool], [string], [long], [double], [byte],
    [Reflection.Assembly]::LoadFrom("$managed\UnityEngine.dll").GetType("UnityEngine.Vector3"),
    [Reflection.Assembly]::LoadFrom("$managed\UnityEngine.dll").GetType("UnityEngine.Quaternion"),
    [Reflection.Assembly]::LoadFrom("$managed\UnityEngine.dll").GetType("UnityEngine.Color")
)
$names = @("Load", "LoadArray", "LoadList", "LoadHashSet", "LoadQueue", "LoadStack", "Load2DArray")
$patched = 0
foreach ($def in $es2.GetMethods([Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static)) {
    if (-not $names.Contains($def.Name)) { continue }
    if (-not $def.IsGenericMethodDefinition) { continue }
    $ps = $def.GetParameters()
    if ($ps.Length -lt 1 -or $ps[0].ParameterType -ne [string]) { continue }
    if ($ps.Length -eq 2 -and -not $ps[1].ParameterType.IsGenericParameter) { continue }
    if ($def.GetGenericArguments().Length -ne 1) { continue }
    foreach ($t in $types) {
        try {
            [void]$def.MakeGenericMethod($t)
            $patched++
        } catch { }
    }
}
Write-Host "Expected Load* patches: $patched"
