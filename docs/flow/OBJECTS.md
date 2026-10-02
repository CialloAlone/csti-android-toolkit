# CSTI Android v1.05o — 对象来源与注册体系

> task-5 / flow-mapper / 只读分析
> 配套：`FLOW.md`（时序）、`INTERACTIONS.md`（三条链路）、`PLAN.md`（方案对比）
> 全部结论标注出处；`【事实】`= 类型/字段/方法签名或真机日志可证；`【推断】`= 由命名与结构推断，已注明依据。

---

## 1. 内容对象规模（谁有多少）

| 类型 | 基类 | 实例数 | 备注 |
|---|---|---|---|
| `CardData` | `UniqueIDScriptable` | **1906** | 卡牌/物品/环境/事件/天气/蓝图 都用它，靠 `CardData.CardType`（枚举 `CardTypes`）区分 |
| `GameStat` | `UniqueIDScriptable` | **361** | 数值状态 |
| `Objective` | `CompletableObject` → `UniqueIDScriptable` | **303** | 目标 |
| `SelfTriggeredAction` | `UniqueIDScriptable` | **138** | 自触发动作 |
| `CharacterPerk` | `CompletableObject` → `UniqueIDScriptable` | **89** | 特质 |
| `Encounter` | `UniqueIDScriptable` | **16** | 遭遇（战斗）模板 |
| `PerkGroup` | `UniqueIDScriptable` | **12** | 特质分组 |
| `PerkTabGroup` | `UniqueIDScriptable` | **4** | 特质页签（`PkTab0_All` / `PkTab1_Conditions` / `PkTab2_Traits` / `PkTab3_Background`） |
| 合计（`UniqueIDScriptable.AllUniqueObjects`） | — | **2858** | 含 `Gamemode` / `PlayerCharacter` / `CompletableObject` 派生 / `CardTabGroup` 等其余类型 |

- 上表实例数来自团队既有真机自检统计（`mods-06/CSTI-MiniLoader-06/TASK4-FINDINGS.md`、`Diag.cs` 的分类计数函数
  `Diag`（`Diag.cs:1530/1555/1675` 均通过 `UniqueIDScriptable.AllUniqueObjects` 遍历））。
- **基类关系（本次核实的）**：`UniqueIDScriptable : UnityEngine.ScriptableObject`；
  `CompletableObject : UniqueIDScriptable`（abstract）；`Objective : CompletableObject`；`CharacterPerk : CompletableObject`。
  所以"2858"不只是 14 个直接子类，还包含二级派生。
  直接派生自 `UniqueIDScriptable` 的类型共 14 个（`_dumps/02_uniqueid_derived.txt`）：
  `BookmarkGroup / CardData / CompletableObject / Encounter / EndgameLogCategory / ExclusiveCardOnBoardGroup /
   GameModifierPackage / Gamemode / GameStat / LocalTickCounter / PerkGroup / PerkTabGroup / PlayerCharacter /
   SelfTriggeredAction`。
- ⚠️ 注意：`CardTag`、`WeatherSet`、`GuideEntry`、`ContentPage`、`CardTabGroup`、`CardFilterGroup`、`StatListTab`
  等是**普通 ScriptableObject**（不进 `AllUniqueObjects`），它们靠别的注册表活着（见 §3）。

---

## 2. 对象从哪来：**Unity 场景引用型序列化资产**（不是代码构造、不是 AssetBundle/Resources）

### 2.1 判定与证据

| 命题 | 判定 | 证据 |
|---|---|---|
| 内容对象是 Unity 序列化资产 | 【事实】 | APK 里 `assets/bin/Data/sharedassets0.assets`（**116.8 MB**，112 个分片）、`sharedassets1.assets`（20.0 MB）、`sharedassets2.assets`（1.27 MB）、`sharedassets3.assets`（4 KB）；每个文件头都紧跟 ASCII `2019.4.38f1`（真 Unity 序列化文件头） |
| 不是运行时 `AssetBundle` 加载 | 【事实】 | 游戏代码（`_dumps/01_interop_ALL_types.txt` 全量）里 **`UnityEngine.AssetBundle` 出现 0 次**；字面量表里 **无 `.bundle` / `StreamingAssets` / `resources.assets`** |
| 不是 `Resources.Load` 逐对象加载 | 【事实】 | 字面量表里唯一含 `Assets/` 的字符串是 TextMeshPro 内建的 `"Sprite Assets/Default Sprite Asset"`；APK 里也没有 `resources.assets` |
| 不是代码 `new`/`CreateInstance` 构造 | 【事实】 | 2858 个对象在 **`GameLoad.Awake()` 之前**就已注册完毕（真机日志：`Data dictionnary successfully loaded.` 的栈帧就是 `GameLoad:Awake()`），代码构造不可能早于场景激活完成 |
| 载体是"场景引用" | 【事实+推断】 | `GameDataBase : ScriptableObject { List<UniqueIDScriptable> AllData }`（`_dumps/00`），而 `GameLoad`（场景里的 MonoBehaviour）持有字段 `DataBase : GameDataBase`；一条场景引用即可把全部内容资产拉进 `sharedassets0.assets`。【推断】这正是 116 MB 集中在 sharedassets0 的原因 |

### 2.2 资产文件构成（`out/base.apk` 实测）

| 条目 | 大小 | 角色 |
|---|---|---|
| `level0` | 37 KB | 场景 0（启动场景，`GameLoad`） |
| `level1` | 731 KB | 场景 1（主菜单，`MainMenu`） |
| `level2.split0/1` | 1.02 MB + 262 KB | 场景 2（对局中，`GameManager`/`GraphicsManager`） |
| `level3` | 5 KB | 场景 3 |
| `sharedassets{0,1,2,3}.assets*` | 116.8 / 20.0 / 1.27 / 0.004 MB | 各场景组引用的资产（**内容对象主要在第 0 组**） |
| `sharedassets{0,1,2}.resource` | 43.6 / 0.007 / 4.4 MB | 资源流（贴图/音频等） |
| `globalgamemanagers(.assets)` | 67 / 97 KB | 场景列表 + 全局对象 |
| `Managed/Metadata/global-metadata.dat` | 3.1 MB | IL2CPP 元数据（version=24，4751 条字符串字面量） |

> 大文件被切成 `<name>.splitN`（每片 1 MB）是**这个 APK 自带的打包方式**，原版 `clone_stage/stock_signed.apk` 里也一样，
> 不是我们重打包引入的。

### 2.3 注册链路（时序）

```
Unity 激活场景 0 与其引用资产
   └─ 每个 UniqueIDScriptable 资产被实例化 → OnEnable()           [UniqueIDScriptable.OnEnable, protected virtual]
        └─ RegisterID()                                           [private]
             ├─ AllUniqueObjects[UniqueID] = this                 [static Dictionary<string,UniqueIDScriptable>]
             ├─ 重名 → Duplicates / "Some objects have the same IDs!" / " has the ID of registered item "
             └─ 空字典 → "No data base loaded!"
   之后 GameLoad.Awake() 打印 "Data dictionnary successfully loaded."
```
【事实】以上字符串与类型/方法来自：`_dumps/metadata_literals.txt:2210-2213`；
`_dumps/00_cpp_ALL_types.txt` 的 `UniqueIDScriptable` 段（`OnEnable` / `RegisterID` / `AllUniqueObjects` / `Duplicates` / `GetFromID`）。
【真机实测】`logs/unity_coldstart.txt:15-16`。

**引用解析**：`UniqueIDScriptable.GetFromID(string)`（静态，泛型重载 `GetFromID<T>(string)`）按 **GUID/UniqueID 字符串**查表；
另有 `SaveID/LoadID/RemoveNamesFromComplexID/AddNamesToComplexID/ListContains` 处理"复合 ID（带名字后缀）"。
→ mod 若要复用这套解析，必须让对象同时满足：① 在 `AllUniqueObjects` 里（键 = `UniqueID`），② 有非空 `UniqueID`。

---

## 3. 注册表 / 集合清单（"改哪里能让 mod 内容出现"）

以下是**游戏自己的**集合（不是 mod 的字典）。`GraphicsManager.Init()` / `MainMenu.Awake()` / `GameManager.Awake()` 之后
这些列表都已填充完毕，可以直接追加。

### 3.1 全局注册表

| 集合 | 声明 | 宿主 | 谁来填 |
|---|---|---|---|
| `AllUniqueObjects` | `static Dictionary<string, UniqueIDScriptable>` | `UniqueIDScriptable` | 资产 `OnEnable → RegisterID`（2858 项） |
| `LoadedIDs` | `static Dictionary<string,string>` | `UniqueIDScriptable` | `SaveID/LoadID` |
| `Duplicates` | `static List<UniqueIDScriptable>` | `UniqueIDScriptable` | ID 冲突时 |
| `AllData` | `List<UniqueIDScriptable>` | `GameDataBase`（`GameLoad.DataBase`） | 场景/资产序列化 |
| `CurrentTexts` | `Dictionary<string,string>` | `LocalizationManager` | `LoadLanguage()`（PC MiniLoader 的 Postfix 注入点） |
| `Languages` | `LanguageSetting[]` | `LocalizationManager` | 场景 |

### 3.2 主菜单（角色/特质界面，`MainMenu`）

| 集合 | 声明 | 用途 | 已被验证可注入？ |
|---|---|---|---|
| `AllCharacterPerks` | `List<CharacterPerk>` | 角色创建界面**全量**特质 | ✅ 真机 `[MENUFIX] AllCharacterPerks 89→95`（`TASK4-FINDINGS.md §8`） |
| `UnlockedPerks` | `List<CharacterPerk>` | 已解锁特质 | ✅ 同上（26→32 起） |
| `AllPerkGroups` | `List<PerkGroup>` | 特质组 | — |
| `AllPerkTabs` | `List<PerkTabGroup>` | 特质页签（4 个） | — |
| `PerkTabs` | `List<IndexButton>` | 页签按钮（UI 实例） | — |
| `AllPerkButtons` | `List<MenuPerkButton>` | 特质按钮（UI 实例） | — |
| `CurrentlyEquippedPerks` | `List<CharacterPerk>` | 当前装备 | — |
| `CreatedCharacters` / `UnlockedCharacters` | `List<PlayerCharacter>` | 角色 | — |
| `AvailableGamemodes` | `Gamemode[]` | 模式 | — |
| 页签内容 | `PerkTabGroup.ContainedPerks : List<CharacterPerk>` | **特质显示列表**（真机证实） | ✅ 真机 `[TAB] PkTab2_Traits 29→35`（`TASK4-FINDINGS.md §1/§4`） |
| 组内容 | `PerkGroup.PerksList : CharacterPerk[]` | 组内特质（**数组**，追加需 `Array.Resize`） | ✅ PC `AddPerkGroup()`；真机 `[DESC] PerksList=[17 项]` |

### 3.3 游玩中（`GameManager`，`MBSingleton<GameManager>`）

| 集合 | 声明 | 用途 |
|---|---|---|
| `AllCards` / `AllVisibleCards` | `List<InGameCardBase>` | 场上全部卡实例 |
| `LocationCards` / `BaseCards` / `ItemCards` / `LiquidCards` / `ImprovementCards` / `EnvDamageCards` / `WeaponCards` / `AmmoCards` / `ArmorCards` / `CoverCards` | `List<InGameCardBase>` | 分类卡实例 |
| `CurrentHandCard` / `CurrentEnvironmentCard` / `CurrentWeatherCard` / `CurrentEventCard` / `CurrentExplorableCard` | `InGameCardBase` | **当前天气卡/事件卡/探索卡在这里** |
| `AllStats` / `StatsDict` | `List<InGameStat>` / `Dictionary<GameStat,InGameStat>` | 状态实例 |
| `AllSelfActions` | `List<SelfTriggeredAction>` | 自触发动作（mod 内容主要落点之一） |
| `AllObjectives` / `HiddenObjectives` | `List<Objective>` | 目标 |
| `AllPerks` | `List<CharacterPerk>` | 游玩中的特质全量 |
| `AllBlueprintModels` / `AllBlueprintResults` / `BlueprintModelStates` / `PurchasableBlueprintCards` / `StartingBlueprints` | `List<CardData>` / `Dictionary<CardData,CardData>` / `Dictionary<CardData,BlueprintModelState>` | 蓝图 |
| `EncounteredEvents` / `EventCardQueue` / `ExplorationDroppedEvents` | `List<CardData>` | 事件队列 |
| `CurrentDismantleActions` | `List<CollectionDropReport>` | 当前可拆解/掉落报表缓存 |
| `StartingItems` / `StartingLocations` / `StartingBaseStructures` / `StartingEnvironment` / `StartingWeather` | `List<CardData>` / `CardData` | 开局种子 |
| `UnlockableCards` / `CardsAboutToBeUnlocked` / `UnlockedImprovements` | `List<CardUnlockConditions>` / `List<CardData>` | 解锁 |

> **GameManager 还暴露了一批公开 C# 事件（`Action<T>`）**，可以 `+=` 订阅，**完全不需要 Harmony**：
> `OnCardSpawned` / `OnCardLoaded` / `OnCardDestroyed`（`Action<InGameCardBase>`）、
> `OnCollectionDropsSelected`（`Action<CollectionDropReport>`）、
> `OnActionPerformed` / `OnActionStarted`（`Action<ActionReport>`）、
> `OnStatModified`（`Action<StatModifierReport>`）、`OnStatsListReady`（`Action`）、
> `OnBeginDragItem` / `OnEndDragItem`（`Action<InGameDraggableCard>`）、`OnDismantleActionHovered`（`Action`）。
> 出处：`_dumps/00_cpp_ALL_types.txt` 的 `GameManager` 段。

### 3.4 UI/图鉴/引导（`GraphicsManager` / `GuideManager`，PC MiniLoader 已用的注入面）

| 集合 | 声明 | 宿主 | PC MiniLoader 用法 |
|---|---|---|---|
| `AllStatsList` → `DetailedStatList.Tabs : StatListTab[]` → `StatListTab.ContainedStats : List<GameStat>` | — | `GraphicsManager` | `AddVisibleGameStat()` 追加（`LoadPatchMain.cs:462-483`） |
| `BlueprintModelsPopup : BlueprintModelsScreen` → `BlueprintTabs : CardTabGroup[]` → `CardTabGroup.SubGroups : List<CardTabGroup>` / `IncludedCards : List<CardData>` / `ShopSortingList` | — | `GraphicsManager` | `AddCardTabGroup()` / `AddBlueprintCardData()`（`LoadPatchMain.cs:428-537`） |
| `CurrentFilterTags : List<CardFilterGroup>`（+ `CardFilterGroup.IncludedCards : List<CardData>`） | — | `GraphicsManager` | `AddCardFilterGroupOnce()`（`LoadPatchMain.cs:393-406`） |
| `UnlockedPerksQueue : List<CharacterPerk>` | — | `GraphicsManager` | 特理解锁弹窗队列 |
| `AllEntries : List<GuideEntry>` | — | `GuideManager` | `LoadGuideEntry()`（`LoadPatchMain.cs:293-303`） |
| `GuidePages` / `PagesDict` / `CardsToEntriesDict` / `StatsToEntriesDict` | `List<ContentPage>` / `Dictionary<...>` | `GuideManager` | 图鉴页 |
| `Gamemode.PlayableCharacters : PlayerCharacter[]` | 数组 | `Gamemode` | `WarpperAllEditorMods()` 追加角色（`LoadResources.cs:162-175`） |
| `CardTabGroup.IncludedCards` / `BookmarkGroup.IncludedCards` | `List<CardData>` | — | 卡牌页签归类 |
| `SoundManager.WeatherAmbience : AmbientSounds[]` | 数组 | `SoundManager` | 天气环境音 |

---

## 4. 数据对象 vs 运行实例（两条完全不同的路）

| | 数据对象（模板） | 运行实例 |
|---|---|---|
| 代表类型 | `CardData` / `Encounter` / `GameStat` / `CharacterPerk` / `Objective` / `SelfTriggeredAction` | `InGameCardBase` / `InGameEncounter` / `InGameStat` / `InGameTickCounter` |
| 基类 | `UniqueIDScriptable`（ScriptableObject） | `MonoBehaviour` |
| 数量级 | 全局 2858，生命周期 = 整个进程 | 每局动态生成/回收，随存档 |
| 生成方式 | 场景资产（§2） | `GameManager.AddCard(...)` / `LoadCard(...)` / `LoadCardSet(...)` / `CreateCardAsSaveData(...)`，**从 prefab 池化**（`ItemCardPrefab` / `EnvironmentCardPrefab` / `WeatherCardPrefab` / `EventCardPrefab` / `ExplorableCardPrefab` / …，见 `GameManager` 字段） |
| 引用数据的方式 | — | `InGameCardBase` 里持有 `CardData`（模型）；`InGameEncounter.EncounterModel : Encounter`、`InGameEncounter.Init(Encounter, EncounterPopup)` |
| mod 该改哪层 | **优先改这一层**（改一次，全局生效；存档只存实例的 GUID 引用） | 改这层 = 只影响当前局，且要处理存档 |

> 这条区分是 PLAN.md 的核心：**"新建 CardData 对象"= 在数据层造对象**（贵、且 Android 上被 ICall 裁剪卡住）；
> **"在查询点合并"= 不动数据层，只在读取时把 mod 数据并进去**（便宜）。

---

## 5. 参考：`CardData`（1906 个对象共用的一张表）的关键字段

| 字段 | 类型 | 含义 |
|---|---|---|
| `UniqueID` | `string`（继承） | 注册表键 |
| `CardType` | `CardTypes` | `Item/Base/Location/Event/Environment/Weather/Hand/Blueprint/Explorable/Liquid/EnvImprovement/EnvDamage` |
| `CardTags` | `CardTag[]` | 归类标签 |
| `CardInteractions` | `CardOnCardAction[]` | 卡对卡动作（**继承 `CardAction`**） |
| `DismantleActions` | `List<DismantleCardAction>` | 卡自身的可执行动作（**掉落的主入口**） |
| `OnStatsChangeActions` | `FromStatChangeAction[]` | 状态变化触发动作 |
| `ExplorationResults` | `ExplorationResult[]` | 探索选项（`{ TriggerValue, CardAction Action }`） |
| `DroppedOnDestroy` | `CardsDropCollection[]` | 销毁时掉落 |
| `BlueprintResult` | `CardDrop[]` | 蓝图产物 |
| `AllDrops` | **`List<CardsDropCollection>`** | **运行时缓存**（由 `FillDropsList()` 填充；注意是"集合列表"不是"卡列表"） |
| `WeatherEffects` | `WeatherSet` | 天气视觉/颜色集（**天气链路的落点**） |
| `VisualEffects` | `WeatherSpecialEffect[]` | 天气特效 prefab 列表 |
| `DefaultLiquidContained` | `LiquidDrop` | 默认液体 |
| `DismantleActions` | `List<DismantleCardAction>` | 拆解动作 |

> ⚠️ 本版本 `CardData` **没有**名为 `Actions` 的字段（旧版/PC 记忆里可能有），
> 动作全部挂在上面三个派生自 `CardAction` 的集合上。字段类型逐条经 `_tools/verify_fields.ps1` 核对。
| `ShowInInventory` 等 | — | UI 行为 |

方法与派生：`get_HasOnDestroyDrops()`、`get_HasExplorationAction()`、`GetExplorationResult(string)`、
`FillDropsList()`、`AddDropsFromAction(CardAction)`。
出处：`_dumps/00_cpp_ALL_types.txt` 的 `CardData` 段（第 1435-1629 行）；完整字段表见该文件。

---

## 6. 存档格式：mod 内容**确实**会落盘（新证据，2026-10-02 20:50 取到的真机存档）

素材：`logs/save_backup_SaveData.json`（全局档，1.4KB）、`logs/save_backup_Slot_1.json`（槽位档，251KB）。

### 6.1 全局档 `GlobalSaveData`

```json
{"Games":[],"Checkpoints":[],"IsValid":true,"DontShowEasyPopup":false,
 "UnlockedPerks":["907ff762686447d45995bb671e8863a9(Pk_1_LifeRaft)", …,
                  "8adb7a05993748ba8827f2b91128c96d(Windy_air)",
                  "cf0ffee55b4647159cac6407db619905(Windy_bless)",
                  "d6347dcc760241389d32b4e5b4064443(Windy_bow)",
                  "c244f793141b45d2abee606d6d70058b(Windy_desert)",
                  "53273323feb741bf8c20d661932c4ac2(Windy_Forest)",
                  "6bde4535a56f11ed906e902e162ca70e(Windy_Windy)"],
 "CreatedCharacters":[{"CharacterName":"Windy", "CharacterPerks":["6bde4535…(Windy_Windy)", …]}],
 "GlobalObjectives":["b89611e112d87434a94f8198e863ef94"], "Suns":0,"Moons":0,
 "PerkUnlockFixVersion":2}
```

**结论（事实）**：
1. **mod 对象会被存档**，格式 = `"<UniqueID>(<对象名>)"` —— 这正是 `UniqueIDScriptable.SaveID/LoadID/RemoveNamesFromComplexID/AddNamesToComplexID`
   那一组方法处理的"复合 ID"。【事实】存档实文 + 方法签名。
2. 存档只存 **ID 字符串**，不存对象内容 → **下次进游戏时该 ID 必须在 `AllUniqueObjects` 里，否则解析失败**。
   这解释了为什么 mod 对象"造出来"以后还得留在注册表里（也是 A′ 之后必须立即注册的原因）。
3. `UnlockedPerks` 里已经是 89 个原版 + 6 个 Windy → **mod 特质的持久化链路是通的**（真机实测数据）。

### 6.2 槽位档 `CardSaveData`（`logs/save_backup_Slot_1.json#MainData`）

```json
"CurrentWeatherCard":{"CardID":"24c3c1920ab17a44f8d0879ab93da943(TropicalIsland_ClearStart)",
                      "EnvironmentID":"0699edf0fe179cc41a6a2c924871d28e(Env_Bay)",
                      "SlotInformation":{"SlotType":5,"SlotIndex":0},
                      "Spoilage":43.0, …}
"CurrentEnvironmentCard":{"CardID":"0699edf0fe179cc41a6a2c924871d28e(Env_Bay)",
                      "SlotInformation":{"SlotType":4,"SlotIndex":0}, …}
"CollectionUses":[{"CollectionName":"Coral","CollectionDrops":{"x":0,"y":2}}]
```

**结论（事实）**：
1. `CardSaveData.CollectionUses : List<CollectionDropsSaveData>`（`{ CollectionName : string, CollectionDrops : Vector2Int }`）
   与 `CardsDropCollection.ToSaveData() : CollectionDropsSaveData`、`InGameCardBase.DroppedCollections : Dictionary<string, Vector2Int>`
   三者名字/类型严格对应，且存档里确有 `Coral` 已用 `y=2` 的记录
   → `INTERACTIONS.md §2.3` 里那条推断从"中置信度"**升级为高置信度**（存档实证 + 全库唯一的同名同类型字段；
   仍未反汇编验证赋值点，故不标为"事实"）。
2. `SlotType: 5` = `SlotsTypes.Weather`（枚举序：`Item=0,Base=1,Location=2,Event=3,Environment=4,Weather=5,Hand=6,…`），
   `SlotType: 4` = `Environment` → 与枚举定义**逐项吻合**，验证了对象模型的读法。
3. **天气卡是一张真实卡实例**（`CurrentWeatherCard`），它的 `Spoilage: 43.0` 在走 → 天气变化很可能是
   "天气卡被消耗/替换"。【事实：存档字段 + 数值在变】【推断：机制】
4. 卡牌引用一律用 `GUID(名字)` 复合 ID（`Env_Bay` / `SandSource` / `TropicalIsland_ClearStart`），
   `ModelID` 只在少数对象上出现（存档里唯一一处是 `StormLocalCounter`，对应 `LocalTickCounter`）。

---

## 7. 给 PLAN 的三条硬结论

1. **数据对象不需要"造"，只需要"接"**：2858 个对象在 `GameLoad.Awake()` 之前就绪（§2.3），
   任何时刻（`GraphicsManager.Init` / `GuideManager.Start` / `GameManager.Awake` 之后）都能取到它们。
2. **"让 mod 内容出现"的最小动作是往一个已有 `List`/数组里追加**（§3.2/§3.3/§3.4 已列出全部候选，
   且特质页签/特质组/角色/图鉴/统计页/蓝图页这几条**在真机或 PC 上已被验证可行**）。
3. **运行时实例层不建议新建**：实例由 prefab 池化 + 存档 GUID 引用，自造实例要么进不了存档，
   要么在 `AddCard` 的复杂签名（29 个参数）上出错；而 `InGameCardBase` 一旦生成，
   **只要它引用的 `CardData` 在注册表里，游戏就能正常显示/存档它**（这是"数据层注入"能生效的根本原因）。
