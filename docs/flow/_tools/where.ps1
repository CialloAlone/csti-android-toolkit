param([Parameter(ValueFromRemainingArguments = $true)][int[]]$Lines)
$f = "D:\RiderProjects\ml-installer-06\docs\flow\_dumps\00_cpp_ALL_types.txt"
$all = Get-Content $f
$heads = @()
for ($i = 0; $i -lt $all.Count; $i++) { if ($all[$i] -like "### *") { $heads += [pscustomobject]@{ Line = $i + 1; Name = $all[$i].Substring(4).Trim() } } }
foreach ($ln in $Lines) {
  $h = $heads | Where-Object { $_.Line -le $ln } | Select-Object -Last 1
  Write-Host ("{0,6}  [{1}]  {2}" -f $ln, $h.Name, $all[$ln - 1].Trim())
}
