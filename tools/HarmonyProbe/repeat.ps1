# HarmonyProbe 重复启动跑批脚本（每次实验连跑 N 次冷启动，记录「第几次崩」）
#
# 用法：
#   pwsh -File repeat.ps1 -Name r01-allmods-noprobe -Launch 3 -WaitSec 30 -NoProbe -KeepOthers
#   pwsh -File repeat.ps1 -Name r02-probe-minimal -Config "target=`nheartbeat=0" -Launch 3 -WaitSec 30 -KeepOthers
param(
    [Parameter(Mandatory = $true)][string]$Name,
    [string]$Config = "",
    [int]$Launch = 3,
    [int]$WaitSec = 30,
    [switch]$SkipBuild,
    [switch]$KeepOthers,
    [switch]$Restore,
    [switch]$NoProbe,
    [switch]$NoPush,
    [switch]$CfgOnly,
    [switch]$NoopMod,
    [switch]$Reorder,
    [switch]$ProbeNothing,
    [switch]$NoFileIO,
    [string]$BuildArgs = "",
    [switch]$NoScreenshot
)

$ErrorActionPreference = 'Continue'
$Root   = 'D:\RiderProjects\ml-installer-06'
$Proj   = Join-Path $Root 'mods-06\HarmonyProbe'
$Adb    = 'D:\devtools\HBuilderX\plugins\launcher-tools\tools\adbs\adb.exe'
$Serial = 'NZNBUC456PJN7HR4'
$Pkg    = 'com.winterspringgames.survivaljourney'
$Remote = "/sdcard/MelonLoader/$Pkg"
$Mods   = "$Remote/Mods"
$Stash  = "$Remote/Mods.stash"
$ResDir = Join-Path $Proj "results\$Name"
$Act    = "$Pkg/com.x.shell.JPolicyActivity"

function Adb { & $Adb -s $Serial @args }

if (-not (Test-Path $ResDir)) { New-Item -ItemType Directory -Path $ResDir -Force | Out-Null }

# ---------- 构建 ----------
if (-not $SkipBuild) {
    Write-Host "[1] build $BuildArgs ..." -ForegroundColor Cyan
    $extra = @(); if ($BuildArgs -ne "") { $extra += $BuildArgs.Split(' ') }
    if ($ProbeNothing) { $extra += '-p:ProbeNothing=1' }
    if ($NoFileIO)     { $extra += '-p:ProbeNoFileIO=1' }
    $b = & dotnet build -c Release -p:ML06=$Root @extra (Join-Path $Proj 'HarmonyProbe.csproj') 2>&1
    $b | Set-Content (Join-Path $ResDir 'build.txt')
    if ($LASTEXITCODE -ne 0) { Write-Host ($b | Select-Object -Last 20 | Out-String); throw "build failed" }
}
$Dll = Join-Path $Proj 'bin\Release\HarmonyProbe.dll'

# ---------- mod 目录准备 ----------
if ($Restore) {
    Write-Host "[2] restore mods from stash ..." -ForegroundColor Cyan
    Adb shell "mkdir -p $Stash" | Out-Null
    Adb shell "cd $Stash && for f in *; do case `"`$f`" in HarmonyProbe.*) ;; *) mv -f `"`$f`" $Mods/ ;; esac; done" | Out-Null
    $KeepOthers = $true
}
if (-not $KeepOthers) {
    Write-Host "[2] stash other mods ..." -ForegroundColor Cyan
    Adb shell "mkdir -p $Stash" | Out-Null
    Adb shell "cd $Mods && for f in *; do case `"`$f`" in HarmonyProbe.*) ;; *) mv -f `"`$f`" $Stash/ ;; esac; done" | Out-Null
}
$cfgLocal = Join-Path $ResDir 'HarmonyProbe.cfg'
Set-Content -Path $cfgLocal -Value $Config -Encoding ascii -NoNewline
if ($NoProbe) {
    Write-Host "    (NoProbe)" -ForegroundColor Yellow
    Adb shell "rm -f $Mods/HarmonyProbe.dll $Mods/HarmonyProbe.cfg $Mods/HarmonyProbe.log" | Out-Null
} elseif ($CfgOnly) {
    Write-Host "    (CfgOnly: 只留 HarmonyProbe.cfg，删掉 dll)" -ForegroundColor Yellow
    Adb push $cfgLocal "$Mods/HarmonyProbe.cfg" | Out-Null
    Adb shell "rm -f $Mods/HarmonyProbe.dll $Mods/HarmonyProbe.log" | Out-Null
    Start-Sleep -Seconds 15
} elseif ($NoopMod) {
    Write-Host "    (NoopMod: 删掉探针，改推一个完全空的 NoopMod.dll)" -ForegroundColor Yellow
    $noop = Join-Path $Proj 'NoopMod\bin\Release\NoopMod.dll'
    $nb = & dotnet build -c Release -p:ML06=$Root (Join-Path $Proj 'NoopMod\NoopMod.csproj') 2>&1
    $nb | Set-Content (Join-Path $ResDir 'build-noop.txt')
    if (-not (Test-Path $noop)) { throw "NoopMod build failed" }
    Adb shell "rm -f $Mods/HarmonyProbe.dll $Mods/HarmonyProbe.cfg $Mods/HarmonyProbe.log" | Out-Null
    Adb push $noop "$Mods/NoopMod.dll" | Out-Null
    Start-Sleep -Seconds 15
} elseif ($NoPush) {
    Write-Host "    (NoPush: 不推送，用 Mods 里现有的探针；等待 20s 让媒体扫描器安静下来)" -ForegroundColor Yellow
    Start-Sleep -Seconds 20
} else {
    Adb push $cfgLocal "$Mods/HarmonyProbe.cfg" | Out-Null
    Adb push $Dll "$Mods/HarmonyProbe.dll" | Out-Null
    Adb shell "rm -f $Mods/HarmonyProbe.log" | Out-Null
    if ($Reorder) {
        # FUSE 目录里「最后创建的文件排在最前」。把 CSTI-MiniLoader 再推一次，
        # 让它重新成为最新文件 → MelonLoader 优先枚举它，探针排第二。
        $ml = Join-Path $Proj 'results\ref-healthy-1809\CSTI-MiniLoader.deployed.dll'
        if (-not (Test-Path $ml)) { throw "缺少 CSTI-MiniLoader 副本: $ml" }
        Adb push $ml "$Mods/CSTI-MiniLoader.dll" | Out-Null
        Write-Host "    (Reorder: 已重推 CSTI-MiniLoader.dll，使它成为枚举第一个；探针排第二)" -ForegroundColor Yellow
        Start-Sleep -Seconds 15
    }
}
Adb shell "ls -la $Mods" | Set-Content (Join-Path $ResDir 'mods_after.txt')

# ---------- 重复启动 ----------
$rows = @()
for ($i = 1; $i -le $Launch; $i++) {
    Write-Host "[launch $i/$Launch] ..." -ForegroundColor Cyan
    Adb logcat -c 2>&1 | Out-Null
    Adb shell "am force-stop $Pkg" | Out-Null
    Start-Sleep -Milliseconds 1200
    $t0 = Get-Date
    Adb logcat -c 2>&1 | Out-Null
    Adb shell "am start -n $Act" | Out-Null
    Start-Sleep -Seconds $WaitSec
    $t1 = Get-Date

    $pidStr = ((Adb shell "pidof $Pkg") -join '') -replace '\s', ''
    $alive = if ($pidStr) { 'ALIVE' } else { 'DEAD' }

    $lc = (& $Adb -s $Serial logcat -d -v brief 2>&1)
    $crashBuf = (& $Adb -s $Serial logcat -b crash -d -v brief 2>&1)
    $gamed = if (($lc | Select-String -Pattern 'GameLoad:LoadMainGameData' -Quiet)) { 'GameLoad=YES' } else { 'GameLoad=NO' }
    $fortify = if (($crashBuf | Select-String -Pattern 'FORTIFY|SIGABRT|SIGSEGV|FATAL' -Quiet)) { ($crashBuf | Select-String -Pattern 'FORTIFY|SIGABRT|SIGSEGV|FATAL' | Select-Object -First 1).Line } else { '' }

    $lc      | Set-Content (Join-Path $ResDir "launch$i-logcat.txt")
    $crashBuf| Set-Content (Join-Path $ResDir "launch$i-crashbuf.txt")
    Adb shell "tail -c 120000 $Remote/MelonLoader/Latest.log" | Set-Content (Join-Path $ResDir "launch$i-Latest.log")
    Adb shell "cat $Mods/HarmonyProbe.log" | Set-Content (Join-Path $ResDir "launch$i-probe.log")
    if (-not $NoScreenshot) { & $Adb -s $Serial exec-out screencap -p > (Join-Path $ResDir "launch$i.png") 2>$null }

    $rows += [pscustomobject]@{ Launch = $i; Verdict = $alive; Pid = $pidStr; GameLoad = $gamed; CrashLine = $fortify; StartedAt = $t0.ToString('HH:mm:ss') }
    Write-Host ("    -> {0} pid={1} {2} {3}" -f $alive, $pidStr, $gamed, $fortify) -ForegroundColor $(if ($alive -eq 'ALIVE') { 'Green' } else { 'Red' })
}

$sum = @()
$sum += "experiment : $Name"
$sum += "config     : " + ($Config -replace "`n", ' | ')
$sum += "buildargs  : $BuildArgs   noprobe=$NoProbe  wait=${WaitSec}s"
$sum += ""
$rows | Format-Table -AutoSize | Out-String -Width 200 | Add-Content -Path (Join-Path $ResDir 'summary.txt')
($sum + ((Format-Table -InputObject $rows -AutoSize | Out-String -Width 200))) | Set-Content (Join-Path $ResDir 'summary.txt')
Write-Host "`n===== SUMMARY $Name =====" -ForegroundColor Yellow
Get-Content (Join-Path $ResDir 'summary.txt') | Write-Host
