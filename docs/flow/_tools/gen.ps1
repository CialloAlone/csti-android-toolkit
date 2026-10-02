# 精确打印指定类型的字段（保留泛型实参）
$ErrorActionPreference = "Stop"
$CecilDll = "D:\RiderProjects\MelonLoaderInstaller\MelonLoaderInstaller.ConsolePatcher\bin\Debug\net7.0\temp\lemon_data\core\Mono.Cecil.dll"
if (-not ("Mono.Cecil.AssemblyDefinition" -as [type])) { Add-Type -Path $CecilDll }
$a = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("D:\RiderProjects\ml-installer-06\cpp2il_out\Assembly-CSharp.dll")
$types = @{}
$list = New-Object System.Collections.ArrayList
function W($t) { [void]$list.Add($t); foreach ($n in $t.NestedTypes) { W $n } }
foreach ($t in $a.MainModule.Types) { W $t }
foreach ($t in $list) { $types[$t.Name] = $t }
$names = $args
foreach ($n in $names) {
  if (-not $types.ContainsKey($n)) { Write-Host "MISS $n"; continue }
  $t = $types[$n]
  Write-Host "### $($t.FullName)  (base=$($t.BaseType))"
  foreach ($f in $t.Fields) {
    if ($f.Name -like "*k__BackingField") { continue }
    Write-Host ("    {0,-58} {1}" -f $f.FieldType.FullName, $f.Name)
  }
  Write-Host ""
}
