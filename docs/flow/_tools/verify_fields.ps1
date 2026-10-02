param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Pairs)
$ErrorActionPreference = "Stop"
$CecilDll = "D:\RiderProjects\MelonLoaderInstaller\MelonLoaderInstaller.ConsolePatcher\bin\Debug\net7.0\temp\lemon_data\core\Mono.Cecil.dll"
if (-not ("Mono.Cecil.AssemblyDefinition" -as [type])) { Add-Type -Path $CecilDll }
$a = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("D:\RiderProjects\ml-installer-06\cpp2il_out\Assembly-CSharp.dll")
$map = @{}
$list = New-Object System.Collections.ArrayList
function W($t) { [void]$list.Add($t); foreach ($n in $t.NestedTypes) { W $n } }
foreach ($t in $a.MainModule.Types) { W $t }
foreach ($t in $list) { $map[$t.FullName] = $t; if (-not $map.ContainsKey($t.Name)) { $map[$t.Name] = $t } }
$bad = 0
foreach ($p in $Pairs) {
  $parts = $p.Split(".")
  $fn = $parts[-1]
  $tn = ($parts[0..($parts.Count - 2)] -join ".")
  if (-not $map.ContainsKey($tn)) { Write-Host ("?? TYPE NOT FOUND: {0}" -f $tn); $bad++; continue }
  $t = $map[$tn]
  $f = $t.Fields | Where-Object { $_.Name -eq $fn }
  if (-not $f) { Write-Host ("MISS  {0}.{1}" -f $tn, $fn); $bad++; continue }
  Write-Host ("OK    {0,-46} {1,-34} : {2}" -f $tn, $fn, $f.FieldType.FullName)
}
Write-Host "---- issues: $bad ----"
