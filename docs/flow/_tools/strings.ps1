param(
  [string]$Path = "D:\RiderProjects\ml-installer-06\gamedata\assets\bin\Data\Managed\Metadata\global-metadata.dat",
  [string]$Out = "D:\RiderProjects\ml-installer-06\docs\flow\_dumps\metadata_strings.txt",
  [int]$MinLen = 5
)
$bytes = [System.IO.File]::ReadAllBytes($Path)
$sb = New-Object System.Text.StringBuilder
$cur = New-Object System.Text.StringBuilder
$sw = New-Object System.IO.StreamWriter($Out, $false, [System.Text.Encoding]::UTF8)
for ($i = 0; $i -lt $bytes.Length; $i++) {
  $b = $bytes[$i]
  if ($b -ge 32 -and $b -lt 127) { [void]$cur.Append([char]$b) }
  else {
    if ($cur.Length -ge $MinLen) { $sw.WriteLine($cur.ToString()) }
    [void]$cur.Clear()
  }
}
if ($cur.Length -ge $MinLen) { $sw.WriteLine($cur.ToString()) }
$sw.Close()
Write-Host "wrote $Out  ($((Get-Content $Out).Count) lines)"
