# 批量导出器：把 interop 程序集 + Cpp2IL dummy 程序集的类型信息落盘成文本，便于引用
$ErrorActionPreference = "Stop"
$CecilDll = "D:\RiderProjects\MelonLoaderInstaller\MelonLoaderInstaller.ConsolePatcher\bin\Debug\net7.0\temp\lemon_data\core\Mono.Cecil.dll"
$CPP = "D:\RiderProjects\ml-installer-06\cpp2il_out\Assembly-CSharp.dll"
$ITP = "D:\RiderProjects\ml-installer-06\interop_out\Assembly-CSharp.dll"
$OUT = "D:\RiderProjects\ml-installer-06\docs\flow\_dumps"
New-Item -ItemType Directory -Force -Path $OUT | Out-Null
Add-Type -Path $CecilDll

function LoadAsm($p) {
  $a = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($p)
  $list = New-Object System.Collections.ArrayList
  function w($t) { [void]$list.Add($t); foreach ($n in $t.NestedTypes) { w $n } }
  foreach ($t in $a.MainModule.Types) { w $t }
  return @{ Asm = $a; Types = $list; Map = $(@{}) }
}
$cpp = LoadAsm $CPP
$itp = LoadAsm $ITP
foreach ($h in @($cpp, $itp)) { foreach ($t in $h.Types) { $h.Map[$t.FullName] = $t } }

function TN($t) { return $t.FullName }
function FmtFields($t) {
  $sb = New-Object System.Text.StringBuilder
  foreach ($f in $t.Fields) {
    if ($f.Name -like "NativeFieldInfoPtr_*" -or $f.Name -like "NativeMethodInfoPtr_*") { continue }
    [void]$sb.AppendLine(("    {0,-46} {1}" -f (TN $f.FieldType), $f.Name))
  }
  return $sb.ToString()
}
function FmtMethods($t) {
  $sb = New-Object System.Text.StringBuilder
  foreach ($m in $t.Methods) {
    if ($m.Name -like "get_NativeFieldInfoPtr_*" -or $m.Name -like "get_NativeMethodInfoPtr_*") { continue }
    $ps = ($m.Parameters | ForEach-Object { "$(TN $_.ParameterType) $($_.Name)" }) -join ", "
    [void]$sb.AppendLine(("    {0,-24} {1,-7} {2}({3})" -f (TN $m.ReturnType), $(if ($m.IsStatic) { "static" } else { "inst" }), $m.Name, $ps))
  }
  return $sb.ToString()
}
function DumpTypes($path, $h) {
  $sb = New-Object System.Text.StringBuilder
  foreach ($t in ($h.Types | Sort-Object FullName)) {
    if ($t.Name -eq "<Module>") { continue }
    [void]$sb.AppendLine("### $($t.FullName)")
    [void]$sb.AppendLine("    base = $($t.BaseType)   attrs = $($t.Attributes)")
    [void]$sb.AppendLine("    FIELDS:")
    [void]$sb.Append((FmtFields $t))
    [void]$sb.AppendLine("    METHODS:")
    [void]$sb.Append((FmtMethods $t))
    [void]$sb.AppendLine("")
  }
  Set-Content -Path $path -Value $sb.ToString() -Encoding UTF8
  Write-Host "wrote $path ($([math]::Round((Get-Item $path).Length/1024)) KB)"
}
DumpTypes "$OUT\00_cpp_ALL_types.txt" $cpp
DumpTypes "$OUT\01_interop_ALL_types.txt" $itp

# UniqueIDScriptable 派生类型
$sb = New-Object System.Text.StringBuilder
foreach ($t in ($cpp.Types | Sort-Object FullName)) {
  $b = $t.BaseType
  if ($b -and ($b.FullName -eq "UniqueIDScriptable" -or $b.FullName -like "*UniqueIDScriptable*")) {
    [void]$sb.AppendLine("$($t.FullName)   base=$($b.FullName)")
  }
}
Set-Content -Path "$OUT\02_uniqueid_derived.txt" -Value $sb.ToString() -Encoding UTF8
Write-Host "wrote 02"
