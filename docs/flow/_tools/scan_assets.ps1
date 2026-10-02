# 在 Unity 序列化资产文件里搜 MonoScript 类名，判定「内容对象」的物理归属（用 Latin1 字符串搜索，快）
Add-Type -AssemblyName System.IO.Compression.FileSystem
$apk = "D:\RiderProjects\ml-installer-06\out\base.apk"
$needles = @("CardData", "GameStat", "PerkTabGroup", "Encounter", "SelfTriggeredAction", "CardAction")
$enc = [System.Text.Encoding]::GetEncoding(28591)   # Latin1
$zip = [System.IO.Compression.ZipFile]::OpenRead($apk)
$entries = $zip.Entries | Where-Object { $_.FullName -like "assets/bin/Data/*" }
$results = @()
foreach ($e in $entries) {
  $s = $e.Open()
  $bufSize = 8MB
  $buf = New-Object byte[] $bufSize
  $counts = @{}; foreach ($n in $needles) { $counts[$n] = 0 }
  $carry = ""
  while ($true) {
    $read = $s.Read($buf, 0, $bufSize)
    if ($read -le 0) { break }
    $txt = $carry + $enc.GetString($buf, 0, $read)
    foreach ($n in $needles) {
      $idx = 0; $c = 0
      while (($idx = $txt.IndexOf($n, $idx, [System.StringComparison]::Ordinal)) -ge 0) { $c++; $idx += $n.Length }
      $counts[$n] += $c
    }
    $carry = if ($txt.Length -ge 32) { $txt.Substring($txt.Length - 32) } else { $txt }
  }
  $s.Close()
  $total = ($counts.Values | Measure-Object -Sum).Sum
  if ($total -gt 0) {
    $results += [pscustomobject]@{ File = ($e.FullName -replace '^assets/bin/Data/', ''); SizeMB = [math]::Round($e.Length / 1MB, 1); CardData = $counts["CardData"]; GameStat = $counts["GameStat"]; PerkTabGroup = $counts["PerkTabGroup"]; Encounter = $counts["Encounter"]; SelfTrg = $counts["SelfTriggeredAction"]; CardAction = $counts["CardAction"] }
  }
}
$zip.Dispose()
$results | Sort-Object -Property CardData -Descending | Format-Table -AutoSize | Out-String -Width 220
Write-Host "命中文件数: $($results.Count)"
