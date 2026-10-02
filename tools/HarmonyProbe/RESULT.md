# HarmonyProbe —— 「Harmony 给这三个方法挂补丁就崩」根因调查报告

- 任务：**task-3**（owner: `harmony-probe`，真机独占 `NZNBUC456PJN7HR4`）
- 旧设备：Redmi matisse（22011211C），**Android 14**，`PAGE_SIZE=4096`，arm64-v8a
- 环境：CSTI v1.05o（Unity 2019.4.38f1 / IL2CPP）+ MelonLoader 0.6.5 Open-Beta + 自编译 Il2CppInterop 1.5.3
- 交付：本文件 + `run.ps1`（单次实验）+ `repeat.ps1`（重复启动实验）+ `results\`（每次实验的原始取证）

---

## 0. 结论摘要

1. **「Harmony 给这三个方法挂补丁导致崩溃」这个前提不成立。**（§2）
   - 三个目标方法**全部成功挂上**，Prefix/Postfix 补丁体**真实执行过**，无任何 HarmonyException。
   - 崩溃在**一个补丁都不挂**时同样发生（e02 / e03），也在**MelonLoader 装上之前**就发生过（12:32 / 12:45 的 tombstone）。

2. **跨设备真根因（lead 在新设备上确定）：APK 里 `.so` 的 zip 数据偏移没按 16 KB 对齐。**（§4）
   - Android 16 KB 页设备上，`extractNativeLibs=false` 的 `.so` 若只按 4 KB 对齐，会被错误映射 → 启动 10~20 s 后在 `libil2cpp.so` 段 SIGSEGV。
   - 我已在本机 APK 产物上**独立复算验证**了这一点，并发现一个坑（§4.2）。

3. **旧设备（4 KB 页）上的崩溃不能用对齐解释**，它是另一个（尚未定位的）现象：
   同一套 APK 在旧设备上**可活可死**，且被证明与「Mods 里有无 `HarmonyProbe.dll`」强相关（§3.3）。
   这一条对产品有实际含义，见 §5。

4. **产品级可用结论（本条最可直接使用）**：
   - Harmony 补丁这条路**可用**，MiniLoader 的三个补丁**没有必要改成免 hook 方案**。
   - 但 **`OnUpdate` / `MelonCoroutines` 泵在这套环境里是死的**（所有运行 `updateTicks=0`），
     `HookFree` 那套轮询注入**从来没有被执行过**（不是偶尔不跑，是从来没跑过）。

---

## 1. 三个目标方法

| # | 目标 | Harmony 角色 | 原用途 |
|---|---|---|---|
| ① | `LocalizationManager.LoadLanguage` | Postfix | 注入中文字符串 |
| ② | `GuideManager.Start` | Prefix | 加载引导 + 加玩家角色 |
| ③ | `GraphicsManager.Init` | Postfix | 注册卡牌页签/蓝图/状态/自定义 GameObject |

三个都是**无参实例方法**（`argc=0`），声明在 `Assembly-CSharp`（Il2CppInterop 互操作程序集）。

---

## 2. 直接推翻前提的两组实验

### 2.1 三个补丁挂得上、且真的执行了

`results\e04-allmods-3patches-log\HarmonyProbe.log`（全部 mod 在场 + ①②③ 都挂 `kind=both body=log`）：

```
18:20:40.991 --- target loc.LoadLanguage: LocalizationManager.LoadLanguage argc=0 declAsm=Assembly-CSharp
18:20:40.994   [BEFORE] MethodInfo=0xB400007751023620 methodPointer=0x7858BC4568 virtualMethodPointer=0x78589BA2F0
18:20:41.008 patch loc.LoadLanguage OK 用时=9ms
18:20:41.010 --- target guide.Start: GuideManager.Start argc=0 declAsm=Assembly-CSharp
18:20:41.020 patch guide.Start OK 用时=8ms
18:20:41.022 --- target graphics.Init: GraphicsManager.Init argc=0 declAsm=Assembly-CSharp
18:20:41.044 patch graphics.Init OK 用时=21ms
18:20:41.046 OnInitializeMelon 返回（未崩溃）
18:20:43.765 HIT prefix（补丁体真的执行了）
18:20:45.340 HIT postfix（补丁体真的执行了）
18:20:46.588 HIT prefix（补丁体真的执行了）
```

补充：`MethodInfo.methodPointer` / `virtualMethodPointer` 在 patch 前后**完全没变**
→ Il2CppInterop 1.5.3 不是改 `MethodInfo` 指针，而是在 native 代码层做 detour（MonoMod）。
这条可以排除「指针被改坏 / 调用约定不匹配」一类猜测。

### 2.2 一个补丁都不挂，照样崩

| 实验 | 补丁 | 其它 mod | 结果 |
|---|---|---|---|
| **e02** | **无（`kind=none`，连 Harmony 实例都不创建）** | 全部移走 | **DEAD** |
| **e03** | **无** | 全部在场 | **DEAD** |

### 2.3 MelonLoader 装上之前就有同源崩溃（决定性旁证）

设备 DropBox（`dumpsys dropbox --print`，原文 `results\dropbox.txt`）：

| 时间 | 进程存活 | 关键内容 |
|---|---|---|
| 2026-10-02 **12:32:04** | 3577 ms | SIGSEGV / `fault addr 0x4a` / `Cause: null pointer dereference` |
| 2026-10-02 **12:45:15** | 21635 ms | 同上 |
| 2026-10-02 **12:45:40** | 4479 ms | 同上 |

三次 native 栈**逐帧相同**：

```
signal 11 (SIGSEGV), code 1 (SEGV_MAPERR), fault addr 0x4a
Cause: null pointer dereference
#00 pc 0x4dbd4c  .../lib/arm64/libil2cpp.so   (BuildId 6036a7a42d55d734595008df720f284199bda2f4)
#01 pc 0x5d71d4  .../lib/arm64/libunity.so
...
x0 = 0  x20 = 0  x21 = 0  x23 = 0  x24 = 0        <-- this == null
```

而设备 `MelonLoader/Logs/` 里最早的一份日志是 **14-32-59**
→ **12:32 / 12:45 这两次崩溃时 MelonLoader 还没装上**。崩溃不是 MelonLoader/Harmony 引入的。

---

## 3. 完整实验矩阵（旧设备，全部为真机实测）

### 3.1 单次启动实验

| # | 配置 | 补丁 | mod 集合 | 结果 | 证据目录 |
|---|---|---|---|---|---|
| ref 18:09 | MiniLoader(`SkipHarmonyPatchAll=true`) + 6 mod | 无 | 7 个 | **ALIVE ≥5 min**（18:14:27 被我的 `force-stop` 杀掉，`am_kill` 有记录） | `results\ref-healthy-1809\` |
| e01 | 探针 | ③ postfix（空体） | 仅探针 | DEAD（进程活 13.8 s） | `results\e01-graphics-postfix-empty\` |
| e02 | 探针 | **无** | 仅探针 | DEAD（11.7 s） | `results\e02-control-nopatch\` |
| e03 | 探针 | **无** | 7+探针 | DEAD（14.0 s） | `results\e03-allmods-nopatch\` |
| e04 | 探针 | ①②③ 全挂（有日志体） | 7+探针 | DEAD（补丁体确已执行） | `results\e04-allmods-3patches-log\` |
| **e05** | **探针移除** | 无 | 7 个 | **ALIVE ≥90 s**，确认已到 `GameLoad=YES` | `results\e05-allmods-noprobe\` |
| e06 | 探针（无心跳、无补丁） | 无 | 7+探针 | DEAD | `results\e06-allmods-probe-hb0-nopatch\` |

> **e02 vs e05 是最重要的一对**：同一场景、同一份存档、同一时刻，
> **挂不挂 Harmony 补丁不改变结局**；改变结局的是 Mods 目录里有没有探针那个 DLL。

### 3.2 崩溃现场（每次逐行相同）

```
F libc  : FORTIFY: pthread_mutex_lock called on a destroyed mutex (0x78efd8c298)
```

崩溃前最后几行（`logcat` 的 `I Unity` 行 + MelonLoader）：

```
I Unity   : Data dictionnary successfully loaded.
I Unity   : GameLoad:Awake()
I MelonLoader: [DEBUG] ICall UnityEngine.Component::GetComponent not resolved
I MelonLoader: [DEBUG] ICall UnityEngine.Component::SendMessageUpwards not resolved
I MelonLoader: [DEBUG] ICall UnityEngine.Component::SendMessage not resolved
I MelonLoader: [DEBUG] ICall UnityEngine.Component::BroadcastMessage not resolved
I Unity   : Properly loaded main save data
I Unity   : GameLoad:LoadMainGameData()          <-- 自动读档
I Unity   : GameLoad:Awake()
（若干次）MelonLoader.Support.SceneHandler OnSceneLoad/OnSceneUnload 抛
        ICall UnityEngine.SceneManagement.Scene::GetBuildIndexInternal not resolved
==> 崩
```

- 崩溃点**固定在「自动读档完成 → 场景切换」那一刻**；
- 只有 bionic 的 FORTIFY 一行，**没有 SIGSEGV 报告、没有 tombstone、没有 `am_crash`**，
  AMS 只留 `am_proc_died: [0,<pid>,...,0,2]`。
  → 推断：native 崩溃被 Unity 崩溃处理器（`Crash-Handler: com.unity3d.player.p`）接管，
  处理器自身在已销毁的 mutex 上再次加锁被 FORTIFY 打死，进程直接消失。

### 3.3 重复启动协议（`repeat.ps1 -Launch 3 -WaitSec 30`）

**这是本次调查最关键的一组数据。** 每个配置连续冷启动 3 次，每次都确认 `GameLoad=YES`。

| # | Mods 里有什么 | 3 次结果 | 证据 |
|---|---|---|---|
| r01 | 7 个原 mod | **ALIVE 3/3** | `results\r01-allmods-noprobe\` |
| r02 | 7 个 + **空探针**（`-p:ProbeNothing=1`：`OnInitializeMelon` 第一行 `return`，零日志/零文件/零线程/零补丁） | **DEAD 3/3** | `results\r02-allmods-emptymod\` |
| r03 | 同上，但**不推送**、等 20 s（排除 adb push / 媒体扫描） | **DEAD 3/3** | `results\r03-probe-present-nopush\` |
| r04 | 再删掉探针 | **ALIVE 3/3** | `results\r04-allmods-noprobe-again\` |
| r05 | **只留 `HarmonyProbe.cfg`，删掉 dll** | **ALIVE 3/3** | `results\r05-cfgonly\` |
| r06 | 删探针，改推**另一个全新编译的极简空 mod `NoopMod.dll`**（只引用 MelonLoader，不引用任何游戏程序集） | **ALIVE 3/3** | `results\r06-noopmod\` |
| r07 | 探针在场，但重推 `CSTI-MiniLoader.dll` 让**探针不再排在枚举第一位** | 见 `results\r07-probe-second\summary.txt` | `results\r07-probe-second\` |

聚合：**探针 dll 在场 = 崩 11/11；不在场 = 活 13/13**（含 ref/e05/r01×3/r04×3/r05×3/r06×3）。

其它已被排除的变量：
- **不是 `adb push` / 媒体扫描**：r03 不推送 + 静置 20 s，仍 3/3 崩；r05 只推 cfg 反而 3/3 活。
- **不是 `.cfg` / `.log` 这类非 dll 文件**：r05（只有 cfg）3/3 活。
- **不是「多一个 mod」本身**：r06（换成另一个全新空 mod `NoopMod.dll`）3/3 活。
- **不是时间漂移 / 设备状态变化**：r01 与 r04 相隔 6 分钟，都是 3/3 活；r02/r03 夹在中间 3/3 崩。
- **不是 mod 里写了什么代码**：r02 的探针 `OnInitializeMelon` 第一行就 return（`Latest.log` 里确认没有任何 `[PROBE]` 行）。

**矛盾点（诚实记录）**：目前只有 `HarmonyProbe.dll` 这一个特定装配会稳定触发；
换 `NoopMod.dll` 不会。r07 用来判定「是不是因为探针被 FUSE 目录枚举排在第一位」。
**这一条与产品无关**（产品里不含探针），但它证明了：

> 这台设备上这个崩溃处在**临界点**上，
> **任何用「单次启动」做的 A/B 二分（包括最初那次 PatchAll 二分）都不可靠。**

### 3.4 时序数据（`Latest.log`「Loading Mods」→ `Component::GetComponent` ICall 批）

| 运行 | 结局 | 到读档前一刻耗时 |
|---|---|---|
| r01 L1 / L2 / L3 | ALIVE | 6.19 / 7.53 / 8.36 s |
| r02 L1 / L2 / L3 | DEAD | 10.71 / 9.61 / 8.87 s |

存活组普遍早 1.5~4 s 到达读档点，但两组有重叠 → 支持「临界窗口 / 竞态」而非「确定性 bug」。

### 3.5 更新泵（OnUpdate）实测是死的

所有运行里探针心跳都打印 `updateTicks=0`（`OnUpdate` 从未被调用），
与「支持模块 `MelonLoader.Support.SM_Component.Create()` 被改成 `ret`」一致。

**含义（对产品很重要）：**
- `HookFree.Tick()`（靠 `OnUpdate` 轮询的免 hook 注入）**从来没有被执行过** ——
  之前看到「免 hook 模式运行中」的日志只是 OnInitializeMelon 里打的那一行，不是 Tick 真的在跑。
- 任何依赖 `OnUpdate` / `MelonCoroutines` / Unity 协程的 mod 逻辑在本环境里**静默失效**。
- mod 想被驱动只能走 **Harmony detour**（本报告证明它可用），或者自己起线程 / 挂原生回调。

---

## 4. 真根因：APK 内 `.so` 未按 16 KB 对齐（lead 在新设备上定位）

新设备 `YHRSHMAYE6UOL7YP`（Android 17，16 KB 页）上的四臂对照（lead 实测，同协议 3×40 s）：

| 臂 | APK | 结果 |
|---|---|---|
| 1 | 原始签名原版（未重打包） | **3/3 存活** ✔ |
| 2 | 我们重打包、4 KB 对齐 | **3/3 崩** ✗ |
| 3 | 我们重打包 + MelonLoader + 7 mods、4 KB 对齐 | **3/3 崩** ✗ |
| 4 | **同 3，改用 16 KB 对齐重排** | **3/3 存活** ✔（7 Mods loaded / Windy 成功 / `[STEP] 9 done`） |

机制：打包流程 `repack_stored.py`（把 `lib/**/*.so` + `resources.arsc` 改成 STORED，
因为 MelonLoader 清单里 `extractNativeLibs=false` 不能压缩）+ `zipalign -p -f 4`（**只对齐 4 KB**）+ apksigner。
16 KB 页设备上 `.so` 数据偏移不是 16 KB 倍数 → 原生库被错误映射 → 启动 10~20 s 后
在 `libil2cpp.so` 段 SIGSEGV（正好对上 §2.3 抓到的 `libil2cpp.so+0x4dbd4c`）。

附：游戏自带库 ELF `p_align`：`libil2cpp/libunity/libcrypto/libssl = 0x10000`（16 KB 兼容 ✔）；
`libmain.so = 0x1000`（4 KB ✗）；MelonLoader 注入的 `libBootstrap.so` / `libdobby.so = 0x1000`。
系统会弹 `VRI-PageSizeMismatchDialog`（兼容性警告，不致命；原版也弹，因为 `libmain.so` 就是 4 KB）。

### 4.1 我在本机 APK 产物上的独立复算（验证方法：解析 ZIP 本地头 + extra 字段长度，得数据偏移）

| APK | `.so` 数据偏移 % 4096 | % 16384 | 判定 |
|---|---|---|---|
| `out\base_stored.apk`（zipalign 之前） | 非 0（libil2cpp=213） | 非 0 | ✗ |
| `out\base_aligned.apk`（`zipalign -p -f 4`） | **全 0** | 有的 0 有的非 0（libil2cpp=0，libssl=4096，libmain=12288） | 4 KB ✔ / 16 KB ✗ |
| `out\base_aligned16k.apk`（`zipalign -f 16384`） | 全 0 | **全 0** | ✔✔ |
| `out\base_signed.apk` | 全 0 | 部分非 0 | 4 KB ✔ / 16 KB ✗ |
| `clone_stage\ml06.apk` | 全 0 | 部分非 0 | 4 KB ✔ / 16 KB ✗ |
| `clone_stage\stock_signed.apk`（原版） | 非 0 | 非 0 | 但 `meth=8`（**压缩**），走的是解压路径，不受影响 |

复算脚本：`%TEMP%\align_check.py`（逻辑已写入本目录 `tools\align_check.py`，见 §6）。

### 4.2 ⚠️ 发现一个坑：`out\ml06_16k.apk` 其实**不是** 16 KB 对齐

```
out\ml06_16k.apk   (18:33, 181,653,719 B)
  lib/arm64-v8a/libil2cpp.so   off=108212224   %4096=0   %16384=12288   <-- 不是 16 KB 倍数
  ...所有 .so 一律 %16384 = 12288
```

名字里带 `16k`，但 7 个 `.so` 的数据偏移全部 `% 16384 = 12288`。
**请确认新设备上跑通第 4 臂用的到底是哪个文件** —— 只有 `out\base_aligned16k.apk` 是真正 `%16384=0` 的。
如果第 4 臂用的是 `ml06_16k.apk`，那「16 KB 对齐」这个结论需要重新核对（或者是 18:33 之后又重排过一次）。

> 复现命令（老版 zipalign 不支持 `-P`，直接给对齐值即可）：
> `zipalign -f 16384 in.apk out.apk`，然后 apksigner 签名。
> **注意：任何在 zipalign 之后再做「重打包 / 加文件」的步骤都会破坏对齐** —— `ml06_16k.apk` 很可能就是踩了这个。

### 4.3 旧设备（Android 14 / 4 KB 页）不适用该解释

旧设备 `getconf PAGE_SIZE = 4096`，4 KB 对齐的 `.so` 在 4 KB 页设备上映射是正确的。
但旧设备上：
- 崩溃确实发生（§3.1、§3.3）；
- 崩溃点、FORITFY 表现、与 12:32/12:45 tombstone 的 native 签名**都和新设备一致**；
- 且呈现「与 Mods 内容强相关 + 时序临界」的特征（§3.3、§3.4）。

→ **旧设备上还有一个独立于对齐的、尚未定位的诱因。**
它有可能是同一个重打包 APK 在 4 KB 页设备上的另一种脆弱表现，也可能是别的东西。
建议在旧设备上做一次对齐版本对照（`zipalign -f 16384` 重打包后装到旧设备）来一刀切开。

---

## 5. 「哪些方法能安全 patch」的可操作准则

基于本次实测（本条是给产品用的）：

1. **这三个方法可以直接用 Harmony 补丁，不需要替代方案。**
   实测：① Postfix ② Prefix ③ Postfix 都安装成功（8~21 ms），补丁体真实命中，无异常。

2. **不要用 `OnUpdate` / `MelonCoroutines` / 协程作为驱动泵** —— 本环境里它们从来没被调用过
   （§3.5）。`HookFree` 这条路线在真机上等于死代码。

3. **判断某个 il2cpp 方法能不能 patch，本环境里看的是「方法有没有 native 实体」而不是「有没有参数」**：
   - 能挂的：无参实例方法同样能挂（①②③ 就是），跨程序集方法也能挂（`CheatsManager.*` 是反证）；
   - 挂不上的典型：`Il2CppInterop` 找不到对应 `MethodInfo`（`AccessTools`/`GetMethod` 返回 null）。
     **先把解析结果打日志**（探针里的 `dump=1` 就会打印 `MethodInfo` / `methodPointer` / `virtualMethodPointer`），
     `MethodInfo=0x0` 就别挂了。

4. **凡是「方法带缺失 ICall 的托管包装」都不要调用**（团队已踩过：`Texture2D.LoadRawTextureData` /
   `Sprite.Create` / `AudioClip.Construct_Internal` → 调用即 SIGABRT）。这与 Harmony 无关，
   但会让「补丁体里干了什么」变成崩因。

5. **验收一律用重复启动 + 记录第几次崩**（§3.3 证明单次启动结论不可信）。命令见 §6。

---

## 6. 复现工具

```powershell
# 单次实验：构建 → 推送 → 隔离其它 mod → 冷启动 → 取证
pwsh -File mods-06\HarmonyProbe\run.ps1 -Name eXX `
     -Config "target=graphics.Init`nkind=postfix`nbody=empty" -WaitSec 60

# 重复启动实验：连跑 N 次冷启动，记录第几次崩 + 是否走到 GameLoad
pwsh -File mods-06\HarmonyProbe\repeat.ps1 -Name rXX -Launch 3 -WaitSec 30 -KeepOthers -NoProbe
pwsh -File mods-06\HarmonyProbe\repeat.ps1 -Name rXX -Launch 3 -WaitSec 30 -KeepOthers -NoopMod
pwsh -File mods-06\HarmonyProbe\repeat.ps1 -Name rXX -Launch 3 -WaitSec 30 -KeepOthers -CfgOnly
pwsh -File mods-06\HarmonyProbe\repeat.ps1 -Name rXX -Launch 3 -WaitSec 30 -KeepOthers -ProbeNothing   # 空 mod 变体

# APK 内 .so 对齐复算
python mods-06\HarmonyProbe\tools\align_check.py out\*.apk

# 取证
adb shell dumpsys dropbox --print                 # 原生 tombstone（不需要 root）
adb shell logcat -b crash -d                      # bionic FORTIFY / abort
adb shell logcat -b events -d | grep am_crash     # AMS 记录的崩溃
adb logcat -d | grep 'I Unity'                    # 游戏自己的 Debug.Log
```

### 探针配置项（`Mods\HarmonyProbe.cfg`，`key=value`，`#` 注释）

| key | 含义 |
|---|---|
| `target` | `loc.LoadLanguage` / `guide.Start` / `graphics.Init` / `cheats.Update` / `cheats.OnGUI`，逗号分隔 |
| `kind` | `none` / `prefix` / `postfix` / `both` |
| `body` | `empty` / `log` / `instance` |
| `mode` | `manual`（HarmonyMethod 手工挂）/ `attr`（`typeof+nameof` 特性式）/ `attrname`（方法名字符串特性式） |
| `heartbeat` | `0` 不开线程 / `1` 线程+MelonLogger+文件 / `2` 线程+只写文件 |
| `dump` | 是否打印 `MethodInfo` / `methodPointer` / `virtualMethodPointer` |
| `skipver` | 跳过 Harmony/Il2CppInterop 类型查找 |

编译期开关（MSBuild 属性）：
`-p:ProbeNothing=1`（`OnInitializeMelon` 第一行 return）、`-p:ProbeNoFileIO=1`（不写自己的日志文件）。

---

## 7. 重要陷阱与教训（写给后续做真机实验的人）

1. **MelonLoader 的 `Latest.log` 不包含 Unity 的 `Debug.Log` 输出。**
   判断「游戏有没有走到 `GameLoad:LoadMainGameData()`」必须 `adb logcat | grep 'I Unity'`。
2. **单次启动的 A/B 结论无效**（§3.3）。这个崩溃可活可死，必须重复启动统计。
3. **`dumpsys dropbox --print` 不需要 root 就能拿到 native tombstone**（本报告 12:32/12:45 的证据就是这么来的）。
   但 FORTIFY abort 那一类**不会**进 dropbox、也不会有 `am_crash`，只能靠 `logcat -b crash`。
4. **`am_proc_died` 不能区分「崩」和「被 force-stop」**，要配合日志/心跳判断。
5. **`zipalign` 之后任何再打包动作都会破坏对齐**（§4.2）。
