# Cecil 只读分析工具（flow-mapper / task-5）
# 用法： . "D:\RiderProjects\ml-installer-06\docs\flow\_tools\cecil.ps1"
$ErrorActionPreference = "Stop"
$CecilDll = "D:\RiderProjects\MelonLoaderInstaller\MelonLoaderInstaller.ConsolePatcher\bin\Debug\net7.0\temp\lemon_data\core\Mono.Cecil.dll"
$AsmPath  = "D:\RiderProjects\ml-installer-06\interop_out\Assembly-CSharp.dll"
if (-not ("Mono.Cecil.AssemblyDefinition" -as [type])) { Add-Type -Path $CecilDll }
$global:ASM = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($AsmPath)
$global:TYPES = @()
function _walk($t) { $global:TYPES += $t; foreach ($n in $t.NestedTypes) { _walk $n } }
foreach ($t in $global:ASM.MainModule.Types) { _walk $t }
$global:TMAP = @{}
foreach ($t in $global:TYPES) { $global:TMAP[$t.FullName] = $t }

function FindTypes([string]$pattern) {
  $global:TYPES | Where-Object { $_.FullName -like $pattern -or $_.Name -like $pattern } | ForEach-Object { $_.FullName }
}
function GetT([string]$full) {
  if ($global:TMAP.ContainsKey($full)) { return $global:TMAP[$full] }
  $m = @($global:TYPES | Where-Object { $_.Name -eq $full })
  if ($m.Count -eq 1) { return $m[0] }
  if ($m.Count -gt 1) { Write-Host "AMBIGUOUS: $full -> $($m.FullName -join ', ')" }
  Write-Host "TYPE NOT FOUND: $full"
  return $null
}
function Fields([string]$full) {
  $t = GetT $full; if (-not $t) { return }
  Write-Host "== FIELDS $($t.FullName)  base=$($t.BaseType) =="
  $t.Fields | ForEach-Object { "{0,-42} {1} {2}" -f ($_.FieldType.FullName), $_.Name, $(if ($_.IsStatic) {"[static]"} else {""}) }
}
function Methods([string]$full, [string]$filter = "") {
  $t = GetT $full; if (-not $t) { return }
  Write-Host "== METHODS $($t.FullName)  base=$($t.BaseType) =="
  $t.Methods | Where-Object { $_.Name -like "*$filter*" } | ForEach-Object {
    $ps = ($_.Parameters | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
    "{0,-16} {1,-7} {2}({3})" -f $_.ReturnType.Name, $(if ($_.IsStatic) {"static"} else {"inst"}), $_.Name, $ps
  }
}
function Sig([string]$full, [string]$mname) {
  $t = GetT $full; if (-not $t) { return }
  $t.Methods | Where-Object { $_.Name -like "*$mname*" } | ForEach-Object {
    $ps = ($_.Parameters | ForEach-Object { "$($_.ParameterType.FullName) $($_.Name)" }) -join ", "
    "  $($_.ReturnType.FullName)  $($t.FullName)::$($_.Name)($ps)"
  }
}
function SigAll([string]$full) { Sig $full "" }
function ShowIL([string]$full, [string]$mname, [int]$max = 500, [string]$only = "") {
  $t = GetT $full; if (-not $t) { return }
  $ms = @($t.Methods | Where-Object { $_.Name -eq $mname -or $_.Name -like "*$mname*" })
  if ($ms.Count -eq 0) { Write-Host "method not found: $full::$mname"; return }
  foreach ($m in $ms) {
    $ps = ($m.Parameters | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
    Write-Host "---- IL $($t.FullName)::$($m.Name)($ps) ----"
    if (-not $m.HasBody) { Write-Host "  <no body>"; continue }
    $i = 0
    foreach ($ins in $m.Body.Instructions) {
      $i++
      if ($only -eq "calls" -and $ins.OpCode.Name -notlike "*call*" -and $ins.OpCode.Name -notlike "newobj") { continue }
      if ($only -eq "fields" -and $ins.OpCode.Name -notlike "ldfld*" -and $ins.OpCode.Name -notlike "stfld*" -and $ins.OpCode.Name -notlike "ldsfld*" -and $ins.OpCode.Name -notlike "stsfld*") { continue }
      "  {0,5} {1,-14} {2}" -f $i, $ins.OpCode.Name, $ins.Operand
      if ($i -ge $max) { Write-Host "  ... (truncated)"; break }
    }
  }
}
function Callers([string]$target) {
  $res = @()
  foreach ($t in $global:TYPES) {
    foreach ($m in $t.Methods) {
      if (-not $m.HasBody) { continue }
      foreach ($ins in $m.Body.Instructions) {
        if (($ins.OpCode.Name -like "*call*" -or $ins.OpCode.Name -eq "newobj") -and $ins.Operand -and ("$($ins.Operand)" -like "*$target*")) {
          $res += "$($t.FullName)::$($m.Name)  ->  $($ins.Operand)"; break
        }
      }
    }
  }
  $res | Sort-Object -Unique
}
function FieldUse([string]$fname) {
  $res = @()
  foreach ($t in $global:TYPES) {
    foreach ($m in $t.Methods) {
      if (-not $m.HasBody) { continue }
      foreach ($ins in $m.Body.Instructions) {
        if ($ins.Operand -and $ins.Operand.GetType().Name -eq "FieldReference" -and $ins.Operand.Name -eq $fname) {
          $res += ("{0} {1}::{2}   [{3}]" -f $ins.OpCode.Name, $t.FullName, $m.Name, $ins.Operand.DeclaringType.Name)
        }
      }
    }
  }
  $res | Sort-Object -Unique
}
function StrUse([string]$needle) {
  $res = @()
  foreach ($t in $global:TYPES) {
    foreach ($m in $t.Methods) {
      if (-not $m.HasBody) { continue }
      foreach ($ins in $m.Body.Instructions) {
        if ($ins.OpCode.Name -eq "ldstr" -and "$($ins.Operand)" -like "*$needle*") {
          $res += "$($t.FullName)::$($m.Name)   `"$($ins.Operand)`""
        }
      }
    }
  }
  $res | Sort-Object -Unique
}
function Chain([string]$full) {
  $t = GetT $full; if (-not $t) { return }
  $c = @(); $x = $t
  while ($x) { $c += $x.FullName; $x = $x.BaseType }
  Write-Host "CHAIN: $($c -join '  <-  ')"
  Write-Host "ATTRS: $($t.Attributes)"
}
function Derived([string]$base) {
  $global:TYPES | Where-Object { $_.BaseType -and $_.BaseType.FullName -eq $base } | ForEach-Object { $_.FullName }
}
function Search([string]$kw) {
  Write-Host "== TYPES matching '$kw' =="
  $global:TYPES | Where-Object { $_.FullName -like "*$kw*" } | ForEach-Object { $_.FullName }
  Write-Host "== METHODS matching '$kw' =="
  foreach ($t in $global:TYPES) { foreach ($m in $t.Methods) { if ($m.Name -like "*$kw*") { "$($t.FullName)::$($m.Name)" } } }
}
