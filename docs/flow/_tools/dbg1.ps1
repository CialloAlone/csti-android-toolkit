. "D:\RiderProjects\ml-installer-06\docs\flow\_tools\cecil.ps1"
Write-Host "TYPES=$($global:TYPES.Count)"
$t = GetT "UniqueIDScriptable"
$m = $t.Methods | Where-Object { $_.Name -eq "OnEnable" }
foreach ($ins in $m.Body.Instructions) {
  $op = $ins.Operand
  $tn = if ($null -eq $op) { "<null>" } else { $op.GetType().Name }
  Write-Host ("  {0,-14} {1}   [{2}]" -f $ins.OpCode.Name, $op, $tn)
}
Write-Host "=== brute force: who calls UniqueIDScriptable::* ==="
$n = 0
foreach ($tt in $global:TYPES) {
  foreach ($mm in $tt.Methods) {
    if (-not $mm.HasBody) { continue }
    foreach ($ins in $mm.Body.Instructions) {
      $op = $ins.Operand
      if ($null -eq $op) { continue }
      if ($op.GetType().Name -eq "MethodReference" -and $op.DeclaringType.Name -eq "UniqueIDScriptable") {
        Write-Host ("  {0}::{1}  ->  {2} {3}" -f $tt.FullName, $mm.Name, $ins.OpCode.Name, $op)
        $n++
      }
    }
  }
}
Write-Host "hits=$n"
