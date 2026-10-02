param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Names)
$f = "D:\RiderProjects\ml-installer-06\docs\flow\_dumps\00_cpp_ALL_types.txt"
$lines = Get-Content $f
$idx = @{}
for ($i = 0; $i -lt $lines.Count; $i++) {
  if ($lines[$i] -like "### *") { $n = $lines[$i].Substring(4).Trim(); if (-not $idx.ContainsKey($n)) { $idx[$n] = $i } }
}
$order = $idx.Keys | Sort-Object { $idx[$_] }
foreach ($name in $Names) {
  if (-not $idx.ContainsKey($name)) { Write-Host "### $name  <NOT FOUND>"; continue }
  $start = $idx[$name]
  $end = $lines.Count
  foreach ($k in $order) { if ($idx[$k] -gt $start) { $end = $idx[$k]; break } }
  $lines[$start..($end - 1)] | ForEach-Object { $_ }
  Write-Host ""
}
