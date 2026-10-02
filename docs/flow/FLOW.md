# CSTI（卡牌生存-热带岛屿）Android v1.05o — 启动/加载时序测绘

> task-5 / owner: flow-mapper / 只读分析，不改工程代码、不占真机
> 目标：回答「我们该插在哪」，替代「照搬 PC 版 MiniLoader 新建对象」的思路。
> 配套文档：`OBJECTS.md`（对象来源与注册）、`INTERACTIONS.md`（三条链路 hook 点）、`PLAN.md`（A/B/C 方案与推荐）

---

## 0. 先读这一页：给 lead 的结论速览

| 问题 | 结论 | 出处 |
|---|---|---|
| 游戏内容对象什么时候可用？ | **第一个场景激活时（`GameLoad.Awake()` 之前）就已经全部注册完毕**。注册发生在 Unity 资产反序列化触发的 `UniqueIDScriptable.OnEnable()` 里 | 真机日志 `logs/unity_coldstart.txt:15-16`；`UniqueIDScriptable.OnEnable/RegisterID` |
| 我们最早能在哪插手？ | `LocalizationManager.LoadLanguage` / `GuideManager.Start` / `GraphicsManager.Init` 三个 Harmony 点（PC MiniLoader 已在用，**真机实测可挂且补丁体确实执行**） | `LoadPatchMain.cs:21,88,366`；`HarmonyProbe/RESULT.md §2.1` |
| 有没有"游戏自己的 mod 加载回调"？ | **没有**。没有 mod 目录扫描、没有 AssetBundle/Resources 加载入口（游戏代码里 `AssetBundle`/`Resources` 出现次数 = 0） | `_dumps/01_interop_ALL_types.txt` 全文搜索；`_dumps/metadata_literals.txt` |
| 轮询泵能用吗？ | **不能**。`OnUpdate` / `MelonCoroutines` 在本环境从来没被调用过（`updateTicks=0`） | `HarmonyProbe/RESULT.md §3.5` |
| 场景结构 | 4 个场景 `level0..level3`；资产在 `sharedassets0..3.assets`（116.8MB / 20.0MB / 1.27MB / 4KB） | APK 条目表 + 文件头 `2019.4.38f1` |

---

## 1. 证据来源与"能证到什么程度"

| 素材 | 能给什么 | 不能给什么 |
|---|---|---|
| `interop_out/Assembly-CSharp.dll`（2.4MB，Il2CppInterop 代理） | **完整方法/属性/字段名 + 参数类型**；`NativeFieldInfoPtr_*` 静态字段 | ❌ 方法体全是 `il2cpp_runtime_invoke` 跳板，**没有真实 IL，不能做静态调用图** |
| `cpp2il_out/Assembly-CSharp.dll`（1.1MB，Cpp2IL dummy） | **字段真实类型**（如 `CardData.AllDrops : List<CardsDropCollection>`、`InGameCardBase.DroppedCollections : Dictionary<string,Vector2Int>`）、泛型实参、继承链、编译器生成的状态机类名（`GameManager/<FinishInitializing>d__319`） | ❌ 方法体是 `ret` |
| `gamedata/.../global-metadata.dat`（3.1MB，version=24） | **4751 条 ldstr 字符串字面量**（游戏自己的日志/提示文本，精确可引用）；全部类型名/字段名/方法名 | ❌ 没有跨方法引用关系 |
| `gamedata/lib/arm64-v8a/libil2cpp.so`（19MB） | 引擎与游戏的原生代码 | 本次**未做反汇编**（无 IL 的情况下成本过高，见 §6） |
| 真机日志 `logs/unity_coldstart.txt` | **游戏 `Debug.Log` + Unity 自动附带的托管栈帧** → 直接读出调用链！ | 只在 `adb logcat \| grep 'I Unity'` 里，不在 `Latest.log` |
| `mods-06/HarmonyProbe/RESULT.md` | 真机实测：哪些方法能挂 Harmony、`OnUpdate` 是死的、崩溃根因 | — |
| `D:\RiderProjects\CSTI-ModLoader\CSTI-MiniLoader\`（PC 版 MiniLoader 源码） | 现状方案 A 的全部机制、以及它已经在用的集合注入点 | PC 是 Mono/IL2CPP 均可跑，Android 从未跑通（lead 已确认） |

> 关键方法论：**真机日志里的 `Debug.Log(msg)` 后面紧跟的若干 `I Unity` 行就是 Unity 打印的托管栈帧**（`(Filename: ./Runtime/Export/Debug/Debug.bindings.h Line: 39)` 是分隔）。所以「msg 是哪段代码打的」是**实测事实**，不是猜的。本文件所有调用链都以此为准。

---

## 2. 启动/加载时序（文字版时序图）

时间戳取自真机冷启动 `logs/unity_coldstart.txt`（2026-10-02 20:47:44 → 20:48:42，pid 5513）。

```
[t=0.00s] ── S0 引擎/IL2CPP 初始化 ─────────────────────────────────────────────
   入口：libmain.so → JNI_OnLoad → il2cpp_init（引擎侧，非游戏代码）
   产物：IL2CPP 运行时 + global-metadata（version=24）+ Cpp2IL/Il2CppInterop 代理程序集
   证据：日志 L1-L5
         "Built from '2019.4/staging' branch, Version '2019.4.38f1', Scripting Backend 'il2cpp',
          CPU 'arm64-v8a', Stripping 'Enabled'"
         "ApplicationInfo com.winterspringgames.survivaljourney version v1.05o"
   ── [t=+5.2s] MelonLoader 打印"本游戏已被 MelonLoader 修改"横幅（真机 L6-L14，20:47:49.914）
      并在其后加载/初始化 Mods/*.dll（Harmony 可用性见 RESULT.md §2）──

[t=+9.7s] ── S1 boot 场景激活 + 数据字典注册 ★插入点的"零点" ─────────────────
   入口：Unity 反序列化 level0 场景 + 其引用的 sharedassets0.assets
         → 每个 UniqueIDScriptable 资产被激活 → UniqueIDScriptable.OnEnable() → RegisterID()
   产物：static Dictionary<string, UniqueIDScriptable> UniqueIDScriptable.AllUniqueObjects（2858 项）
         static Dictionary<string,string> LoadedIDs、List<UniqueIDScriptable> Duplicates
   证据：真机 L15 "Data dictionnary successfully loaded."   ← 该消息的栈帧是 "GameLoad:Awake()"
         （L16）→ 证明这行是在 GameLoad.Awake() 里打的，**即 Awake 时字典已经建好**
         字面量：2210 "No data base loaded!" / 2211 "Some objects have the same IDs!" /
                 2212 " has the ID of registered item " / 2213 "Data dictionnary successfully loaded."
         类型：UniqueIDScriptable : UnityEngine.ScriptableObject，OnEnable() 保护虚方法，
               RegisterID() 私有方法，GetFromID(string) 静态

[t=+11.7s] ── S2 全局存档 / 选项 ─────────────────────────────────────────────
   入口：GameLoad.Awake() → LoadMainGameData() → LoadGameFilesData() → LoadGameFile(path)
                          → LoadOptions() / LoadDefaultOptions()
   产物：GlobalSaveData GameLoad.SaveData、GameOptions DefaultOptions/CurrentGameOptions、
         List<GameSaveFile> GameLoad.Games
   证据：真机 L20-22
         "Properly loaded main save data"
         "GameLoad:LoadMainGameData()"      ← 栈帧 1
         "GameLoad:Awake()"                 ← 栈帧 2
         字面量 2230 "Properly loaded main save data" / 2214 "Slot_{0}.json" /
               2247 "{0}/Games" / 2248 "{0}/SaveData.json"

[t=+12.8s] ── S3 主菜单场景 ──────────────────────────────────────────────────
   入口：MainMenu.Awake()
   产物：MainMenu 的各 List（全量特质/已解锁特质/特质页签/特质组/按钮…）
         LocalizationManager.CurrentTexts（Dictionary<string,string>）
         GraphicsManager 的全部 UI 引用 + AllStatsList(DetailedStatList) 等
   证据：真机 L26-40
         "Achievement: CREATE_CUSTOM_CHARACTER"
         "Objective:OnComplete(Boolean)"
         "CompletableObject:ForceComplete(Boolean)"
         "GameLoad:CheckSteamAchievements()"
         "MainMenu:Awake()"                 ← MainMenu.Awake → GameLoad.CheckSteamAchievements
                                                 → Objective.OnComplete(bool) → CompletableObject.ForceComplete(bool)
         "MainMenu:RefreshCreatedCharacters()" + "MainMenu:Awake()"   ← Awake 内调 RefreshCreatedCharacters
   同一阶段的两条 mod 注入线（PC MiniLoader 已用，真机实测可挂）：
         LocalizationManager.Awake() → LoadLanguage()      ← Postfix 注入 mod 文本
         GraphicsManager.Init()                            ← Postfix 注入卡牌页签/蓝图/状态

[t=+42.0s] ── S3.5 玩家在菜单里点角色页（UI 事件链，实测） ────────────────────
   证据：真机 L48-60
         "MainMenu:SetupCharacterSelectionScreen()"
         "System.Action`1:Invoke(T)"
         "UnityEngine.Events.UnityAction:Invoke()"
         "UnityEngine.Events.UnityEvent:Invoke()"
         "UnityEngine.EventSystems.ExecuteEvents:Execute(GameObject, BaseEventData, EventFunction`1)"
         "UnityEngine.EventSystems.StandaloneInputModule:ProcessTouchPress(PointerEventData, Boolean, Boolean)"
         → MainMenu.SetupCharacterSelectionScreen() 由按钮点击触发（不是 Awake 触发）

[t=+47.7s] ── S4 进游戏（开局/读档）★ 三条业务链路开始活跃 ────────────────────
   入口：MainMenu.StartGame() → GameLoad.StartNewGame(int,bool,bool) 或 GameLoad.LoadGame(int)
         → GameLoad.GameSceneIndex → AsyncLoading.LoadScene(int) → LoadSceneRoutine(int)
   产物：GameManager 单例（MBSingleton<GameManager>）、InGameCardBase 实例群
   证据：真机 L64-72
         "Current Gamemode: CharacterList"  + 栈帧 "GameManager:Awake()"
         "Current Character: "              + 栈帧 "GameManager:Awake()"
         字面量 2252/2253 "Current Gamemode: " / "Current Gamemode: NULL"、
               2254/2255 "Current Character: " / "Current Character: NULL"

[t=+48.6s] ── S4.1 世界构建 + 首次自动存档（协程实测） ──────────────────────
   入口：GameManager.FinishInitializing()（协程 GameManager/<FinishInitializing>d__319）
         → GameLoad.SaveGame(int,bool)
   产物：首份存档；环境卡 GameManager.CurrentEnvironmentCard；天气卡 GameManager.CurrentWeatherCard
   证据：真机 L74-77
         "saving checkpoint"
         "GameLoad:SaveGame(Int32, Boolean)"            ← 栈帧 1
         "<FinishInitializing>d__319:MoveNext()"        ← 栈帧 2
         "UnityEngine.SetupCoroutine:InvokeMoveNext(IEnumerator, IntPtr)"
         交叉验证：Android 构建里确实存在 `GameManager/<FinishInitializing>d__319`
                 （`_dumps/00_cpp_ALL_types.txt`），与日志里的 d__319 **编号一致**
   世界构建 API（`GameManager`，签名见 OBJECTS.md §5）：
         LoadCardSet(...) / LoadCard(...) / LoadInventoryCard(...) / AddCard(...) /
         CreateCardAsSaveData(...) / ChangeEnvironment()

[t=+57.7s] ── S5 退出对局回主菜单 ───────────────────────────────────────────
   证据：真机 L81-93（同一组 5 行栈帧再次出现：CheckSteamAchievements → MainMenu:Awake）
         → 场景被重新加载，MainMenu 全流程重跑
```

**【观测】mod 自身（CSTI-MiniLoader）在这条时间轴上的位置**（同一份真机日志 L42-46）：
```
20:48:04.562  加载模组Windy中的图片总用时为: 0:00:05.6849427      ← MainMenu.Awake 之后 ~7s
20:48:06.705  加载模组 Windy 中的json用时:0:00:02.1415818
```
即：**mod 的资源/JSON 加载发生在主菜单场景起来之后**（不是进程启动时）。
本报告不对"它由谁触发"下结论（可能是 MelonLoader 的 Mods 初始化线程，也可能是 mod 自己在某个补丁里触发）；
但它说明"**主菜单阶段（S3）时 mod 尚未完成加载**"——凡是依赖 mod 数据的注入，最早也要等到 `GraphicsManager.Init` /
`GuideManager.Start` 这类**由游戏主动调用**的时机，不能指望 `OnInitializeMelon` 一返回数据就绪。

### 2.1 每个阶段"能不能插"的判定

| 阶段 | 可插？ | 用什么 | 备注 |
|---|---|---|---|
| S0 | ✖ 不建议 | — | IL2CPP 还没起来，Harmony 挂不上游戏方法 |
| S1 数据注册 | △ 只能"事后追加" | `UniqueIDScriptable.AllUniqueObjects` 是 `Dictionary`，可写 | 但 `RegisterID()` 是私有方法、且它是"注册表"；mod 若要注册新 GUID 对象，正确做法是写字典 + 给对象设 `UniqueID`（见 PLAN.md B/C） |
| S2 存档 | ✖ | — | 与内容注入无关 |
| S3 主菜单 | ✔✔ **最佳** | `LocalizationManager.LoadLanguage`(Postfix) / `GraphicsManager.Init`(Postfix) | PC MiniLoader 已验证；此时全部 2858 个数据对象可用 |
| S4 进游戏 | ✔✔ | `GuideManager.Start`(Prefix) | 世界对象（`InGameCardBase`）在此之后产生；三条业务链路的 hook 必须挂在这里之后才有意义 |
| 任意时刻轮询 | ✖✖ | `OnUpdate` / `MelonCoroutines` | **本环境从来没被调用过**（RESULT.md §3.5），团队现有 `HookFree` 路线等于死代码 |

---

## 3. 场景与资产布局（决定"对象从哪来"）

APK 内 `assets/bin/Data/`（`out/base.apk` 实测条目）：

| 文件 | 大小 | 说明 |
|---|---|---|
| `level0` | 37 KB | 场景 0（启动场景；`GameLoad` 所在） |
| `level1` | 731 KB | 场景 1（主菜单；`MainMenu` 所在） |
| `level2.split0/1` | 1.02 MB + 262 KB | 场景 2（游戏内；`GameManager` 所在） |
| `level3` | 5 KB | 场景 3 |
| `sharedassets0.assets.split0..111` | **116.8 MB** | 场景 0 组资产（**游戏内容对象主要在这里**） |
| `sharedassets1.assets.split0..19` | 20.0 MB | 场景 1 组资产 |
| `sharedassets2.assets.split0..1` | 1.27 MB | 场景 2 组资产 |
| `sharedassets3.assets` | 4 KB | 场景 3 组资产 |
| `sharedassets{0,1,2}.resource` | 43.6 / 0.007 / 4.4 MB | 资源流（贴图/音频等大对象） |
| `globalgamemanagers(.assets)` | 67 / 97 KB | 场景列表 + 全局单例资产 |
| `<32 位 hash 名>` × 32 | 合计 35.5 MB | 额外的 Unity 序列化文件（非 Resources） |
| `Managed/Metadata/global-metadata.dat` | 3.1 MB | IL2CPP 元数据 |

判定（有证据）：

1. **这些是真的 Unity 2019.4.38f1 序列化文件**，不是自定义容器。
   证据：文件头前 21 字节后紧跟 ASCII `2019.4.38f1`（`sharedassets0.assets.split0`：
   `00 02 25 a5 06 f6 a6 00 00 00 00 15 00 02 25 c0 00 00 00 00 "2019.4.38f1"`）。
2. **内容对象是"场景引用型"资产，不是运行时加载的 AssetBundle/Resources。**
   证据 a：游戏代码里 `UnityEngine.AssetBundle` 相关调用/字段 **0 处**（`_dumps/01_interop_ALL_types.txt` 全文搜索）。
   证据 b：没有任何 `Assets/...` 形式的 `Resources.Load` 路径字面量（`_dumps/metadata_literals.txt` 唯一命中是 TextMeshPro 内建的 `"Sprite Assets/Default Sprite Asset"`）。
   证据 c：`GameDataBase : ScriptableObject { List<UniqueIDScriptable> AllData }` 是**被场景里的 `GameLoad.DataBase` 字段引用**的（`GameLoad.DataBase : GameDataBase`），一条场景引用把 2858 个资产全部拉进构建。
   证据 d：PC MiniLoader 自己的导入代码就是"遍历场景里的 ScriptableObject + 遍历 `GameLoad.Instance.DataBase.AllData`"两条路（`LoadResources.cs:74-94`）。
3. **注册时机 = Unity 资产激活（`OnEnable`），早于 `GameLoad.Awake()`**。见 §2 S1。

---

## 4. 时序之外：三个已实测的环境约束（写代码前必须知道）

| 约束 | 事实 | 出处 |
|---|---|---|
| Harmony 可用 | 三个目标方法（`LocalizationManager.LoadLanguage` / `GuideManager.Start` / `GraphicsManager.Init`）在真机上 Prefix/Postfix 都挂成功（8~21ms），补丁体真实命中；`MethodInfo.methodPointer` patch 前后不变（MonoMod detour） | `HarmonyProbe/RESULT.md §2.1` |
| `OnUpdate` 死 | 所有运行 `updateTicks=0`；`MelonLoader.Support.SM_Component.Create()` 被改成 `ret` | 同上 §3.5 |
| 缺 ICall 不能调托管包装 | `Sprite.Create` / `ImageConversion.LoadImage` / `Texture2D.LoadRawTextureData` / `AudioClip.Construct_Internal` 等托管包装内部解析的是被裁掉的名字 → **调用即 SIGSEGV/SIGABRT**；必须直接调已注册的 `*_Injected`/`*Impl` ICall | `TASK4-FINDINGS.md §10`；`HarmonyProbe/RESULT.md §5.4` |

> 对插入点选择的含义：**能被 Harmony 挂上的方法 = 可以作为 hook 点**。判断标准是"有没有 native 实体"（`MethodInfo != 0`），与参数个数无关（无参实例方法同样能挂）。

---

## 5. 时序图的可复现命令

```powershell
# 冷启动游戏自身的日志（含托管栈帧）—— 这是本文件时序列的唯一真机证据
adb logcat -d | grep -F 'I Unity'          # 已落盘：logs/unity_coldstart.txt

# 静态元数据（本文件所有类型/字段/字面量引用的来源）
pwsh -File docs\flow\_tools\dump.ps1                 # → _dumps/00_cpp_ALL_types.txt, 01_interop_ALL_types.txt
pwsh -File docs\flow\_tools\literals.ps1             # → _dumps/metadata_literals.txt（4751 条精确字面量）
pwsh -File docs\flow\_tools\scan_assets.ps1          # → APK 资产文件扫描
```

---

## 6. 本次未做、以及为什么（留给下一轮的可选项）

- **未反汇编 `libil2cpp.so`**。要做"精确的谁读谁写"必须反汇编 ARM64 + 解析 IL2CPP 的 `FieldInfo*` 表，
  以 `interop_out/MethodAddressToToken.db`（MelonLoader UMTM 格式，magic `UMTM` v1）+ APK 内 `capstone.dll`
  为起点可做，但工作量是"天"级；而 §7 的那些 hook 点**不需要**它就能决定下一步（见 PLAN.md）。
- **未用 Il2CppDumper/Cpp2IL 重跑分析**：APK 内的 Cpp2IL 只产出 dummy（无 IL），重跑不会带来新信息。
- **PC 版真实 IL 在本机不存在**：全盘搜到的 `Assembly-CSharp.dll` 全部是 Cpp2IL dummy、Il2CppInterop 代理
  或 reference-assembly（`OnEnable` 体只有 `ret` / `il2cpp_runtime_invoke`）。
  → 因此本报告的"调用链"一律以**真机日志栈帧**或**类型/字段/方法签名**为证，凡推断处均标注【推断】。
