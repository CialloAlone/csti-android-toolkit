# HarmonyProbe 真机实验跑批脚本
# 用法：
#   pwsh -File run.ps1 -Name e01-baseline-nopatch -Config "target=" -WaitSec 50
#   pwsh -File run.ps1 -Name e02-cheats-update -Config "target=cheats.Update`nkind=prefix`nbody=empty"
#
# 每次实验：构建 → 推 DLL/配置 → 隔离其它 mod → 清 logcat → 冷启动 → 等待 → 取证 → 存盘到 results\<Name>\
param(
    [Parameter(Mandatory = $true)][string]$Name,
    [string]$Config = "",
    [int]$WaitSec = 50,
    [switch]$SkipBuild,
    [switch]$KeepOthers,
    [switch]$Restore,
    [switch]$NoProbe,
    [string]$BuildArgs = "",
    [switch]$NoScreenshot
)

$ErrorActionPreference = 'Continue'
$Root      = 'D:\RiderProjects\ml-installer-06'
$Proj      = Join-Path $Root 'mods-06\HarmonyProbe'
$Adb       = 'D:\devtools\HBuilderX\plugins\launcher-tools\tools\adbs\adb.exe'
$Serial    = 'NZNBUC456PJN7HR4'
$Pkg       = 'com.winterspringgames.survivaljourney'
$Remote    = "/sdcard/MelonLoader/$Pkg"
$Mods      = "$Remote/Mods"
$Stash     = "$Remote/Mods.stash"
$ResDir    = Join-Path $Proj "results\$Name"

function Adb { & $Adb -s $Serial @args }

if (-not (Test-Path $ResDir)) { New-Item -ItemType Directory -Path $ResDir -Force | Out-Null }

# ---------- 1. 构建 ----------
if (-not $SkipBuild) {
    Write-Host "[1] build ..." -ForegroundColor Cyan
    $extra = @()
    if ($BuildArgs -ne "") { $extra = $BuildArgs.Split(' ') }
    $b = & dotnet build -c Release -p:ML06=$Root @extra (Join-Path $Proj 'HarmonyProbe.csproj') 2>&1
    $b | Set-Content (Join-Path $ResDir 'build.txt')
    if ($LASTEXITCODE -ne 0) { Write-Host ($b | Select-Object -Last 20 | Out-String); throw "build failed" }
    Write-Host ($b | Select-Object -Last 3 | Out-String)
}
$Dll = Join-Path $Proj 'bin\Release\HarmonyProbe.dll'
if (-not (Test-Path $Dll)) { throw "no dll: $Dll" }

# ---------- 2. 写配置并推送 ----------
Write-Host "[2] push dll + cfg ..." -ForegroundColor Cyan
$cfgLocal = Join-Path $ResDir 'HarmonyProbe.cfg'
Set-Content -Path $cfgLocal -Value $Config -Encoding ascii -NoNewline
if ($NoProbe) {
    Write-Host "    (NoProbe: 不推送探针，并把它从 Mods 里清掉)" -ForegroundColor Yellow
    Adb shell "rm -f $Mods/HarmonyProbe.dll $Mods/HarmonyProbe.cfg $Mods/HarmonyProbe.log" | Out-Null
} else {
    Adb push $cfgLocal "$Mods/HarmonyProbe.cfg" | Out-Null
    Adb push $Dll "$Mods/HarmonyProbe.dll" | Out-Null
    Adb shell "rm -f $Mods/HarmonyProbe.log" | Out-Null
}

# ---------- 3. 隔离其它 mod ----------
if ($Restore) {
    Write-Host "[3] restore other mods from stash ..." -ForegroundColor Cyan
    Adb shell "mkdir -p $Stash" | Out-Null
    Adb shell "cd $Stash && for f in *; do case `"`$f`" in HarmonyProbe.*) ;; *) mv -f `"`$f`" $Mods/ ;; esac; done" | Out-Null
    $KeepOthers = $true
}
if (-not $KeepOthers) {
    Write-Host "[3] stash other mods ..." -ForegroundColor Cyan
    Adb shell "mkdir -p $Stash" | Out-Null
    # 把 Mods 下除 HarmonyProbe.* 以外的文件移到 stash
    Adb shell "cd $Mods && for f in *; do case `"`$f`" in HarmonyProbe.*) ;; *) mv -f `"`$f`" $Stash/ ;; esac; done" | Out-Null
}
Adb shell "ls -la $Mods" | Set-Content (Join-Path $ResDir 'mods_after.txt')
Adb shell "ls -la $Stash" | Set-Content (Join-Path $ResDir 'stash.txt')

# ---------- 4. 清日志 + 冷启动 ----------
Write-Host "[4] restart app ..." -ForegroundColor Cyan
Adb logcat -c 2>&1 | Out-Null
Adb shell "am force-stop $Pkg" | Out-Null
Start-Sleep -Milliseconds 800
$t0 = Get-Date
Adb shell "am start -n $Pkg/com.x.shell.JPolicyActivity" | Set-Content (Join-Path $ResDir 'am_start.txt')
Write-Host "    started at $($t0.ToString('HH:mm:ss.fff'))，等待 $WaitSec s ..."

# ---------- 5. 等待 + 取证 ----------
Start-Sleep -Seconds $WaitSec
$t1 = Get-Date

$pid_ = (Adb shell "pidof $Pkg") -join '' -replace '\s', ''
$resumed = (Adb shell "dumpsys activity activities | grep -E 'mResumedActivity|topResumedActivity'") -join "`n"
$verdict = if ($pid_) { 'ALIVE' } else { 'DEAD' }

$summary = @()
$summary += "experiment : $Name"
$summary += "started    : $($t0.ToString('yyyy-MM-dd HH:mm:ss.fff'))"
$summary += "checked    : $($t1.ToString('yyyy-MM-dd HH:mm:ss.fff'))  (waited ${WaitSec}s)"
$summary += "pidof      : '$pid_'"
$summary += "verdict    : $verdict"
$summary += "resumed    : $(($resumed -split "`n" | Select-Object -First 3) -join ' | ')"
$summary += ""
$summary += "----- CONFIG -----"
$summary += $Config
$summary | Set-Content (Join-Path $ResDir 'verdict.txt')
Write-Host ($summary -join "`n") -ForegroundColor $(if ($verdict -eq 'ALIVE') { 'Green' } else { 'Red' })

Write-Host "[5] collect evidence ..." -ForegroundColor Cyan
& $Adb -s $Serial logcat -b crash -d 2>&1 | Set-Content (Join-Path $ResDir 'logcat_crash.txt')
& $Adb -s $Serial logcat -d -v threadtime 2>&1 | Set-Content (Join-Path $ResDir 'logcat_all.txt')
Adb shell "tail -c 200000 $Remote/MelonLoader/Latest.log" | Set-Content (Join-Path $ResDir 'Latest_log_tail.txt')
Adb shell "cat $Mods/HarmonyProbe.log" | Set-Content (Join-Path $ResDir 'HarmonyProbe.log')
Adb shell "cat $Remote/MelonLoader/Latest-Bootstrap.log" | Set-Content (Join-Path $ResDir 'Latest-Bootstrap.log')
Adb shell "ls -la $Remote/MelonLoader/Logs/" | Set-Content (Join-Path $ResDir 'logs_dir.txt')
if (-not $NoScreenshot) {
    & $Adb -s $Serial exec-out screencap -p > (Join-Path $ResDir 'screen.png') 2>$null
}

# 关键片段提取
$keys = 'FATAL|SIGABRT|SIGSEGV|SIGBUS|abort message|backtrace|FORTIFY|libc  |il2cpp|mono|tombstone|DEBUG   |HarmonyException|Crash|destroyed mutex'
Select-String -Path (Join-Path $ResDir 'logcat_crash.txt') -Pattern $keys -AllMatches |
    ForEach-Object { $_.Line } | Set-Content (Join-Path $ResDir 'logcat_crash_keys.txt')
Write-Host "    -> $ResDir"
