# 解析 global-metadata.dat 的字符串字面量表（精确，逐条），用于取证游戏自己的 Debug.Log 文本
$Path = "D:\RiderProjects\ml-installer-06\gamedata\assets\bin\Data\Managed\Metadata\global-metadata.dat"
$Out = "D:\RiderProjects\ml-installer-06\docs\flow\_dumps\metadata_literals.txt"
$b = [System.IO.File]::ReadAllBytes($Path)
function U32($o) { [BitConverter]::ToUInt32($b, $o) }
function I32($o) { [BitConverter]::ToInt32($b, $o) }
$sanity = U32 0
$version = I32 4
Write-Host ("sanity=0x{0:X8} version={1}" -f $sanity, $version)
$p = 8
$names = @("stringLiteral", "stringLiteralData", "string", "events", "properties", "methods",
  "parameterDefaultValues", "fieldDefaultValues", "fieldAndParameterDefaultValueData", "fieldMarshaledSizes",
  "parameters", "fields", "genericParameters", "genericParameterConstraints", "genericContainers", "nestedTypes",
  "interfaces", "vtableMethods", "interfaceOffsets", "typeDefinitions", "images", "assemblies", "fieldRefs",
  "referencedAssemblies", "attributeData", "attributeDataRange", "unresolvedVirtualCallParameterTypes",
  "unresolvedVirtualCallParameterRanges", "windowsRuntimeTypeNames", "windowsRuntimeStrings", "exportedTypeDefinitions")
$sec = @{}
for ($i = 0; $i -lt $names.Count; $i++) {
  $off = U32 $p; $size = U32 ($p + 4); $p += 8
  $sec[$names[$i]] = @($off, $size)
}
foreach ($k in $names) { Write-Host ("  {0,-40} off={1,10} size={2,10}" -f $k, $sec[$k][0], $sec[$k][1]) }

$litOff = $sec["stringLiteral"][0]; $litSize = $sec["stringLiteral"][1]
$dataOff = $sec["stringLiteralData"][0]
$count = $litSize / 8
Write-Host "literal count = $count"
$sw = New-Object System.IO.StreamWriter($Out, $false, (New-Object System.Text.UTF8Encoding($false)))
for ($i = 0; $i -lt $count; $i++) {
  $len = U32 ($litOff + $i * 8)
  $dIdx = I32 ($litOff + $i * 8 + 4)
  if ($len -le 0 -or $len -gt 4000) { continue }
  $start = $dataOff + $dIdx
  if ($start + $len -gt $b.Length) { continue }
  $s = [System.Text.Encoding]::UTF8.GetString($b, $start, $len)
  if ($s -match "[\x00-\x08\x0b\x0c\x0e-\x1f]") { continue }
  $sw.WriteLine($s)
}
$sw.Close()
Write-Host "wrote $Out ($((Get-Content $Out).Count) literals)"
