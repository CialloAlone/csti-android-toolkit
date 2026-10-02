# CSTI Android v1.05o — 三条链路的数据流与 hook 点候选

> task-5 / flow-mapper / 只读分析
> 配套：`FLOW.md`（时序）、`OBJECTS.md`（对象/注册表）、`PLAN.md`（A/B/C 方案与推荐）
> 标注约定：`【签名事实】`= 来自程序集的方法/字段签名；`【真机】`= 真机日志实测；`【推断】`= 由命名/结构推断，附依据。
> ⚠️ 本文件**没有真实 IL** 可读（见 FLOW.md §1），所以"读/写方向"凡未标注为实测的，都是**签名级推断**，并给出可在真机 5 分钟内证伪的实验（§4）。

---

## 0. 先看这张总图（掉落为什么是整个问题的核心）

```
                        ┌──────────────────────── 数据层（UniqueIDScriptable，2858 个，进程级）────────────────────────┐
                        │ CardData.CardType / CardData.CardInteractions : CardOnCardAction[]                       │
                        │ CardData.DismantleActions : List<DismantleCardAction> / OnStatsChangeActions : …[]      │
                        │   （三者都继承 CardAction）                                                             │
                        │ CardAction.ProducedCards : CardsDropCollection[]   ← 「动作产出什么」（掉落唯一入口）    │
                        │ CardData.DroppedOnDestroy : CardsDropCollection[]  ← 「销毁时产出什么」                 │
                        │ CardData.BlueprintResult : CardDrop[]              ← 「蓝图产出什么」                   │
                        │ CardData.ExplorationResults : ExplorationResult[]  ← 「探索选项」(TriggerValue + CardAction)│
                        │ CardData.AllDrops : List<CardsDropCollection>      ← 「这张卡关联的全部掉落集合」(缓存)   │
                        │ Encounter.PlayerActions : GenericEncounterPlayerAction[] / EnemyActions : EnemyAction[] │
                        │ Encounter.*Effects : EncounterResultEffect { List<CardData> DroppedCards,              │
                        │                                              StatModifier[] StatChanges, ... }          │
                        │ CardData.WeatherEffects : WeatherSet { WeatherColors[], WeatherSpecialEffect[] }         │
                        └─────────────────────────────────────────────────────────────────────────────────────────┘
                                                     │ 查询/滚动
                                                     ▼
   GameManager.GetCollectionDropsReport(CardAction, InGameCardBase, bool) : CollectionDropReport   ★查询点★
        └─ CollectionDropInfo[] { CollectionName, BaseWeight, RangeUpTo, CardData[] Drops, ... }
             └─ CardsDropCollection.FillDropList(bool _CheckEnvironment, int _Multiplier)           ★滚动点★
                  └─ StatBasedDropChanceModifier / CardBasedDropChanceModifier /
                     DurabilityBasedDropChanceModifier （权重修正）
                                                     │ 结算/生成
                                                     ▼
   GameManager.StandardCardCollectionDrop(CardsDropCollection, InGameCardBase, bool, CardAction, int, Transform)  ★结算协程★
        └─ GameManager.ProduceCards(CardsDropCollection, InGameCardBase, bool, bool, bool)                          ★生成协程★
             └─ GameManager.AddCard(...)  → InGameCardBase 实例
                                                     │ 记账
                                                     ▼
   InGameCardBase.DroppedCollections : Dictionary<string, Vector2Int>   ← 每个实例「这个集合已经掉过几次」
   GameManager.OnCollectionDropsSelected : Action<CollectionDropReport> ← ★现成的 C# 事件钩子★
```

---

## 1. 链路一：遭遇 / 事件结算与掉落

### 1.1 数据模型（谁读谁写）

| 对象 | 关键字段 | 角色 | 出处 |
|---|---|---|---|
| `Encounter : UniqueIDScriptable` | `EncounterTitle`、`EnemyName`、`EnemyBodyTemplate`、`EnemyArmor`、`MeleeSkill/RangedSkill/Blood/Stamina/Morale/Value1..4 : EnemyValue`、`EnemyActions : EnemyAction[]`、`PlayerActions : GenericEncounterPlayerAction[]`、`DefaultPlayerWounds`、`EnemyDefeatedEffects/EnemyEscapedEffects/PlayerEscapedEffects/PlayerDemoralizedEffects/Special1..4Effects : EncounterResultEffect` | **遭遇模板**（16 个） | `_dumps/00` `Encounter` 段 |
| `EncounterResultEffect` | `ResultLog`、**`DroppedCards : List<CardData>`**、`StatChanges : StatModifier[]`、`TransferValuesToStats : EnemyValueToStatModifier[]`、`AmmoRecovery` | **遭遇掉落就来自这里**（注意：元素是 `CardData` **引用**，不是 `CardDrop`） | 同上 |
| `GenericEncounterPlayerAction` | `ActionName`、`ActionSuccessLog/ActionFailureLog`、`RequiredDistance`、`PreClashDistanceChange`、`ActionRange`、`Reach`、`IsEscapeAction`、`InitialDamage/InitialClashValue`、`EncounterResult` | 玩家选项模板（**非战斗选项也走这里**，如「逃跑/逼近/用道具」） | 同上 |
| `EnemyAction` | `ActionLog/SuccessLog/FailureLog`、`BaseWeight`、`DistanceWeightModifier`、`CloseRangeWeightModifier`、`CardsOnBoardWeightModifiers : CardBasedDropChanceModifier[]`、`StatsWeightModifiers : StatBasedDropChanceModifier[]`、`Damage`、`EncounterResult` | 敌人行动（用同一套权重修正器） | 同上 |
| `InGameEncounter : MonoBehaviour` | `EncounterModel : Encounter`、`CurrentRound`、`CurrentEnemyAction/CurrentPlayerAction`、`CurrentEnemy*`（12 个浮动值）、`EncounterResult`、`Popup : EncounterPopup`、`Distant` | **运行实例**，`Init(Encounter, EncounterPopup)` 建立 | 同上 |
| `EncounterPopup : MBSingleton<EncounterPopup>` | `CurrentEncounter : InGameEncounter`、`EncounterPlayerActions : List<...>`、`ActionButtons : List<EncounterOptionButton>`、`StatBasedEncounterResults : StatEncounterResult[]`、`StatEnforcedPlayerActions : StatEnforcedPlayerAction[]`、`PlayerEscapeAction/PlayerGetCloseAction : GenericEncounterPlayerAction`、`ForcedPlayerAction` | **UI 与结算都在这里**（全局单例，配置项也在它身上） | 同上 |

**非战斗"事件卡"走另一条**（用户报的「请给我药物 / 要压缩肉干没反应」就是这条）：
`CardData.CardType == CardTypes.Event` → 卡上的动作 = `CardData.CardInteractions : CardOnCardAction[]` +
`CardData.DismantleActions : List<DismantleCardAction>` + `CardData.OnStatsChangeActions : FromStatChangeAction[]`
（**三者都继承 `CardAction`**）→ `CardAction.ProducedCards : CardsDropCollection[]` → 进入下图的掉落管线。
运行时对应实例字段是 `InGameCardBase.DismantleActions : DismantleCardAction[]`；
每个按钮缓存了自己的报表：**`DismantleActionButton.DropReport : CollectionDropReport`**（PC 版 mod 正是从这里读的，见 `CstiDetailedCardProgress/Action.cs:20`）。
【签名事实】以上字段类型全部经 `_tools/verify_fields.ps1` 逐条核对；**注意本版本 `CardData` 上没有名为 `Actions` 的字段**，别按旧版/PC 记忆写。

### 1.2 结算调用链（`EncounterPopup` 方法表，名字即顺序）

```
EncounterPopup.StartEncounter(Encounter _Encounter, bool _Load)          ← 遭遇入口
   └─ InGameEncounter.Init(Encounter _Model, EncounterPopup _Popup)      ← 实例化运行态
        └─ EncounterPopup.RoundStart(bool _Loaded)
             ├─ CalculateCover() / CalculateStealthChecks()
             ├─ InGameEncounter.SelectAction() : EnemyAction            ← 敌人选招（权重滚动）
             ├─ EncounterPopup.SelectForcedPlayerAction()
             ├─ EncounterPopup.AddNonWeaponActions()                    ← ★组装玩家可选动作★
             ├─ EncounterPopup.DisplayPlayerActions()                   ← ★渲染按钮（PC mod 已挂此处）★
             ├─ EncounterPopup.DoPlayerAction(int _Action)              ← 玩家选择
             ├─ EncounterPopup.ResolveRound()
             ├─ EncounterPopup.ApplyEncounterResult() : bool            ← ★结算结果（含掉落）★
             │     └─ ResolveEncounterResultFromEnemyState()
             └─ EncounterPopup.WaitForAction(CardAction, InGameCardBase) : IEnumerator
```
【签名事实】以上全部来自 `_dumps/00_cpp_ALL_types.txt` 的 `EncounterPopup` / `InGameEncounter` 段。
【真机/PC 侧旁证】`mods-06/CstiDetailedCardProgress/Encounter.cs:37` 已在用 `[HarmonyPatch(typeof(EncounterPopup), "DisplayPlayerActions")]`，说明该点是**可挂且有数据可读**的。

### 1.3 hook 点候选（链路一）

| # | 目标方法（全签名） | 参数含义 | 为什么适合 | 风险 / 备注 |
|---|---|---|---|---|
| ① | `EncounterPopup.StartEncounter(Encounter _Encounter, bool _Load)`（inst, void） | `_Encounter`=模板数据对象；`_Load`=是否从存档恢复 | **遭遇的唯一入口**。Prefix 可替换 `_Encounter`（换成 mod 遭遇）或在 Postfix 里读取/记录；`_Load=true` 时可跳过 | Prefix 改参数需 `ref`（Harmony 支持 `ref Encounter`）；`_Load` 时 `CurrentEncounter` 可能还没赋值 |
| ② | `InGameEncounter.Init(Encounter _Model, EncounterPopup _Popup)`（inst, void） | 同上 + 宿主弹窗 | **运行实例建立点**，此时 `InGameEncounter` 已存在（`__instance`），适合做"给这个遭遇补 mod 数据"的最早时机 | 需先知道 `GameManager.StartEncounter` 的调用者；`EncounterPopup.StartEncounter` 是更外层的保险点 |
| ③ | `EncounterPopup.AddNonWeaponActions()`（inst, void） | 无参 | **组装玩家可选动作的地方**。Postfix = 往 `EncounterPlayerActions`(List) / `ActionButtons`(List) 追加 mod 选项，**不需要改任何 `Encounter` 数据对象** | 追加按钮需要 `ActionButtonPrefab : EncounterOptionButton` 实例化（`Object.Instantiate` 在真机可用）；顺序/索引需与 `DoPlayerAction(int _Action)` 对齐 |
| ④ | `EncounterPopup.ApplyEncounterResult() : bool`（inst） | 返回=是否应用了结果 | **结果结算点（含 `EncounterResultEffect.DroppedCards : List<CardData>`）**。Prefix 可往 `CurrentEncounter.EncounterModel.*Effects.DroppedCards` 追加 mod 掉落；Postfix 可读 `bool` 决定是否补结算 | 该 List 的元素是 **`CardData` 引用**：追加"指向 mod 自己对象的引用"是安全的；**危险动作是改被引用对象的内容**（那才会污染共享的 `CardData`） |
| ⑤ | `GameManager.OnCollectionDropsSelected : Action<CollectionDropReport>`（字段，非方法） | 报表含 `DropsInfo[]`/`SelectedDrop` | **纯 C# 事件，`+=` 即可，不用 Harmony、不碰 IL2CPP 补丁**。适合"知道自己掉到了什么" | 【推断】触发时机在玩家选定掉落之后；需要在拿到 `GameManager` 实例后订阅（`MBSingleton<GameManager>`） |
| ⑥ | `EncounterPopup.DisplayPlayerActions()`（inst, void） | 无参 | 只读展示点，PC mod 已验证可挂；适合**只做 UI 增强**（如显示 mod 选项的说明） | 在这里改数据太晚（UI 已按 `EncounterPlayerActions` 建好） |

---

## 2. 链路二：探索与掉落

### 2.1 数据模型

| 对象 | 关键字段 | 角色 |
|---|---|---|
| `CardData.ExplorationResults` | `ExplorationResult[]`，每项 `{ float TriggerValue; CardAction Action }` | **卡牌上的"探索选项"列表** |
| `CardAction.CountAsExploration` / `get_IsExploreAction()` | `bool` | 标哪些动作算"探索" |
| `CardAction.ProducedCards` | `CardsDropCollection[]` | **动作产出（掉落的唯一入口字段）** |
| `CardAction.DropsMultiplier` | `int` | 直接对应 `FillDropList(..., int _Multiplier)` 的第 2 个参数 |
| `CardsDropCollection` | `CollectionName`、`CollectionMessages : LocalizedString[]`、`CountsAsSuccess`、`RevealInventory`、`CollectionUses : Vector2Int`（可用次数）、`CollectionWeight`、`StatsDropChanceModifiers[]`、`CardDropChanceModifiers[]`、`DurabilitiesDropChanceModifier`、`CreatedLiquid : LiquidDrop`、**`DroppedCards : CardDrop[]`**、`DroppedEncounter : Encounter`、`StatModifications[]`、`DurabilityModifications` | **掉落集合（"掉落表"本体）** |
| `CardsDropCollection` 运行时属性 | **`CurrentDrop : List<CardData>`、`CurrentSaveDataDrop : List<CardData>`、`CurrentStatModifiers : List<StatModifier>`、`CurrentLiquidDrop : LiquidDrop`、`SaveDataKey : string`、`CurrentMessage : string`**（均为编译器生成的 `<X>k__BackingField`） | **滚动的结果容器**（"这一次会掉什么"） |
| `CollectionDropInfo`（struct） | `CollectionName`、`IsSuccess`、`RevealInventory`、`BaseWeight`、`StatWeightMods`、`CardWeightMods`、`DurabilitiesWeightMods`、**`RangeUpTo`**、**`Drops : CardData[]`**、`CollectionUses`、`StatMods` | **查询结果里"某个候选集合"的一行**（`RangeUpTo` = 权重区间的上界，加权随机用） |
| `CollectionDropReport`（struct） | `TickInfo`、`FromCard`、`FromData`、`FromAction`、**`DropsInfo : CollectionDropInfo[]`**、`TotalValue`、`BaseValue`、`RandomValue`、`SelectedDrop` | **一次掉落查询的完整结果**（UI 的成功率/掉落预览也用它） |
| `InGameCardBase.DroppedCollections` | `Dictionary<string, Vector2Int>` | **运行实例上的"这个集合已经掉了几次"账本**（键 = `CollectionName`） |
| `CardData.AllDrops` | **`List<CardsDropCollection>`** + `FillDropsList()` / `AddDropsFromAction(CardAction)` / `get_TotalDropWeight()` | 卡片级"这张卡关联的全部**掉落集合**"缓存 + 权重合计（**不是 `List<CardDrop>`**） |

### 2.2 结算路径（名字即顺序）

```
ExplorationPopup.Setup(InGameCardBase _ExplorationCard)          ← 打开探索界面
   ├─ SetupExploration / SetupImprovements / SetupDamages
   ├─ StartExploration() → AddTickToExploration() : IEnumerator   ← 每次推进一格
   │     └─ AddActionToPerform(ExplorationResult _FromResult)     ← 把 ExplorationResult 变成待执行动作
   └─ ConfirmSelection() / ClickMainButton()

CardData.ExplorationResults[i].Action  (CardAction)
   ├─ CardAction.get_IsExploreAction() / CountAsExploration
   └─ CardAction.ProducedCards : CardsDropCollection[]            ← 掉落表入口

GameManager.GetCollectionDropsReport(CardAction _Action, InGameCardBase _FromCard, bool _CheckEnvironment)
        : CollectionDropReport                                   ★★ 纯查询点（无副作用）★★
   └─ 内部对每个候选集合算权重 → CollectionDropInfo[] DropsInfo

GameManager.StandardCardCollectionDrop(CardsDropCollection _Collection, InGameCardBase _FromCard,
        bool _TransformsIntoEnv, CardAction _FromAction, int _StartingTick, Transform _FeedbackSource)
        : IEnumerator                                            ★★ 结算协程（真正决定掉什么）★★
   └─ GameManager.ProduceCards(CardsDropCollection _Collection, InGameCardBase _FromCard,
              bool _TransformsIntoEnv, bool _ToExplorationSlots, bool _TravelToPrevEnv) : IEnumerator
        └─ GameManager.AddCard(...) → InGameCardBase 实例
```
【签名事实】全部来自 `_dumps/00_cpp_ALL_types.txt`；协程状态机编号可交叉验证：
`GameManager/<StandardCardCollectionDrop>d__364`、`<ProduceCards>d__365`。
【真机旁证】`mods-06/CSTI-MiniLoader-06/LoadUtil/LoadResources.cs:140/245/302` —— PC/移植版在 warp 完 `CardData` 后
**显式调用 `cardData.FillDropsList()`** 来重建 `AllDrops`，说明该方法是"掉落数据的重建点"。

### 2.3 "DropList / FillDropsList 到底谁读谁写"（本报告要回答的核心问题）

| 符号 | 写方（结论） | 读方（结论） | 置信度与依据 |
|---|---|---|---|
| `CardData.DroppedOnDestroy : CardsDropCollection[]` | **策划数据（资产序列化）**，运行时只读 | `CardData.FillDropsList()` / `get_HasOnDestroyDrops()` | 【签名事实】`get_HasOnDestroyDrops()` 存在；`DroppedOnDestroy` 是序列化数组 |
| `CardsDropCollection.DroppedCards : CardDrop[]` | **策划数据**（Inspector 配的"能掉什么"） | `SetDroppedCards(...)`、`GetAllPossibleCards(List<CardData>)`、`ContainsCard(CardData,bool)` | 【推断】依据：与 `StatBasedDropChanceModifier[]` 等策划字段同组；元素是 **struct `CardDrop`**，所以"往这里追加元素"是值拷贝、不产生共享引用 |
| `CardsDropCollection.CurrentDrop / CurrentSaveDataDrop / CurrentStatModifiers / CurrentMessage`（`<X>k__BackingField`） | **`FillDropList(bool _CheckEnvironment, int _Multiplier)`** 与 `SetDroppedCards(...)` | `CollectionDropInfo` 构造、UI 展示 | 【推断，强】依据：6 个自动属性都没有序列化痕迹（编译器 backing field），且 `FillDropList` 返回 void 必须写某处；`SaveDataKey` 与 `CardSaveData` 呼应 |
| `CardData.AllDrops : List<CardsDropCollection>` | **`CardData.FillDropsList()`**（+ `AddDropsFromAction(CardAction)`） | `get_TotalDropWeight()`、掉落预览/Tooltip、`CardData` 自身的"可掉落集合"查询 | 【推断，强】依据：方法名 + `AllDrops` 是全库唯一的"掉落集合聚合"字段（`List<CardsDropCollection>`）+ 移植版在 warp 后主动调用它重建（`LoadResources.cs:140/245/302`） |
| `CollectionDropReport` / `CollectionDropInfo` | **`GameManager.GetCollectionDropsReport(...)`**（返回 struct） | 掉落选择 UI、`GameManager.CurrentDismantleActions : List<CollectionDropReport>` | 【签名事实】返回类型 + 字段结构 |
| `InGameCardBase.DroppedCollections : Dictionary<string, Vector2Int>` | 结算时记账（键=`CollectionName`） | 判断集合是否用尽（`CollectionUses`） | 【推断，**高**】依据：① 字段名与 `CardsDropCollection.CollectionUses : Vector2Int` 值类型一致；② 真机存档实证 `CardSaveData.CollectionUses : List<CollectionDropsSaveData>{ CollectionName, CollectionDrops : Vector2Int }`，且 `CardsDropCollection.ToSaveData() : CollectionDropsSaveData` 存在；③ 全库唯一同名同类型字段。**未反汇编，故不标"事实"** |
| `GameManager.OnCollectionDropsSelected : Action<CollectionDropReport>` | 游戏在玩家确认掉落时 invoke | **mod 可订阅** | 【签名事实】字段类型就是 `Action<CollectionDropReport>` |

> ⚠️ **这就是 TASK4-FINDINGS §11 那次"掉落被写坏"事故的结构性解释**：
> mod 克隆 `CardData` 得到浅拷贝 → `CardsDropCollection` 是**引用类型子对象**、与游戏资产共享同一实例 →
> mod 侧调用 `FillDropsList()` / warp 写入 `CurrentDrop`(`List<CardData>`)、`CurrentMessage` 等**运行时容器**，
> 写的就是游戏自己那份 → 事件/掉落/天气一起被改写。
> 注意区分两种"追加"：
> - **安全**：往 `CardsDropCollection.DroppedCards : CardDrop[]`（**struct 数组**）追加元素 —— 值拷贝，不共享引用；
> - **安全**：往 `EncounterResultEffect.DroppedCards : List<CardData>` 追加**指向 mod 自己对象的引用**；
> - **不安全**：改**被引用的那个 `CardData`/子对象**的内容，或改共享 `CardsDropCollection` 的运行时字段（`CurrentDrop`/`CurrentMessage`/…）。
> 换句话说：**污染来自"写共享对象的内容"，不来自"把引用挂进列表"**。C 路线只做后者。

### 2.4 hook 点候选（链路二）

| # | 目标方法（全签名） | 参数含义 | 为什么适合 | 风险 / 备注 |
|---|---|---|---|---|
| ① | `GameManager.GetCollectionDropsReport(CardAction _Action, InGameCardBase _FromCard, bool _CheckEnvironment) : CollectionDropReport` | `_Action`=触发掉落的动作；`_FromCard`=来源卡实例；`_CheckEnvironment`=是否计入环境权重 | **纯查询、无副作用、返回值就是"候选掉落表"**。Postfix 里改 `__result.DropsInfo`（struct 数组）：追加 mod 集合、或按 `RangeUpTo` 重算区间 → **不动任何游戏数据对象** | `CollectionDropReport`/`CollectionDropInfo` 是 **struct**：Harmony Postfix 必须用 `ref CollectionDropReport __result` 才能改回；数组要新建（长度+1）再赋回 |
| ② | `CardsDropCollection.FillDropList(bool _CheckEnvironment, int _Multiplier)` | 环境检查开关、掉落倍数（来自 `CardAction.DropsMultiplier`） | 每次滚动"这次掉什么"的**唯一计算入口**。Postfix 可把 mod 掉落并进 `CurrentDrop` | ⚠️ **写的是被共享的集合对象**（同一 `CardsDropCollection` 被多次使用）；只能"本次有效"地加，且必须在结算后清掉，否则污染游戏数据（正是 TASK4 §11 的坑） |
| ③ | `GameManager.StandardCardCollectionDrop(CardsDropCollection _Collection, InGameCardBase _FromCard, bool _TransformsIntoEnv, CardAction _FromAction, int _StartingTick, Transform _FeedbackSource) : IEnumerator` | 集合、来源卡、是否转为环境、来源动作、起始 tick、反馈锚点 | **结算总入口**，参数里就带着 `_Collection` 与 `_FromAction` | ⚠️ 它是 `IEnumerator`：Harmony 的 Prefix/Postfix 在**迭代器创建时**执行，不是在结算完成时。要"结算后"必须 Prefix 返回 `false` 并返回**自己包装的 IEnumerator**（把原枚举器包一层）。工程量大、易错 |
| ④ | `CardData.FillDropsList()`（inst, void, 无参） | — | 重建卡片级 `AllDrops` 缓存；mod 若往 `DroppedOnDestroy`/`Actions` 追加了集合，可在这里重算 | 对**游戏对象**调用 = 修改游戏数据；只应对 mod 自己的对象调用 |
| ⑤ | `InGameCardBase.DroppedCollections : Dictionary<string, Vector2Int>`（字段） | 键=`CollectionName`，值=`Vector2Int` | **判断"这次是不是已经掉过该集合"的账本**，可用于实现"mod 掉落只在首次出现"这类规则 | 只读更安全；写它等于篡改存档语义 |
| ⑥ | `GameManager.OnCollectionDropsSelected : Action<CollectionDropReport>`（事件） | 报表 | **零补丁**（`+=`），适合记录/统计/触发后续逻辑 | 不改变掉落结果本身 |
| ⑦ | `ExplorationPopup.AddActionToPerform(ExplorationResult _FromResult)`（inst, void） | 一条探索结果 | 探索界面把结果**转成动作**的点；Postfix 可以追加/替换动作 | `ExplorationResult` 是**普通类**（引用类型，可改字段）；注意 UI 已按旧列表建好按钮 |

---

## 3. 链路三：天气

### 3.1 数据模型（天气是"一张卡"驱动的，不是全局枚举）

| 对象 | 关键字段 | 角色 |
|---|---|---|
| `Gamemode.Weather : CardData` | 模式默认天气 | 开局种子 |
| `PlayerCharacter.Weather : CardData` | 角色默认天气 | 开局种子 |
| **`CharacterPerk.OverrideWeather : CardData`** | **特质覆盖天气** | ★ 已被真机验证可改特质 → **改天气的最省事入口** |
| `GameManager.StartingWeather : CardData` / `CurrentWeatherCard : InGameCardBase` / `WeatherCardPrefab : InGameCardBase` | 起始数据 / **当前天气卡实例** / 生成用 prefab | 运行态 |
| `GameSaveData.CurrentWeatherCard : CardSaveData` | 存档 | 天气会被存档 |
| `CardData.CardType == CardTypes.Weather` / `CardData.WeatherEffects : WeatherSet` / `CardData.VisualEffects : WeatherSpecialEffect[]` | 天气卡的**表现数据** | 天气可视化 |
| `WeatherSet : ScriptableObject` | `ColorsOfTheDay : WeatherColors[]`、`EffectsToSpawn : WeatherSpecialEffect[]`、`get_CurrentColors()` | 一组天气表现 |
| `WeatherColors`（struct） | `ColorName`、`TimeRange : Vector2Int`、`MainColor`、`LightSourceAllowed` | 一天内的配色分段 |
| `AmbienceImageEffect : MBSingleton<AmbienceImageEffect>` | `CurrentWeather : WeatherSet`（属性）、`CurrentColor/PrevColor : WeatherColors`、`CurrentWeatherEffects : List<WeatherSpecialEffect>`、`CurrentLightSources : List<CardData>`、`LightStat : GameStat` | **真正"把天气画出来/生效"的地方** |
| `SoundManager` | `WeatherAmbience : AmbientSounds[]`、`WeatherAmbiencePrefab`、`SetWeatherAmbience(CardData _Weather)`、`SetWeatherVolume(float)`、`PauseWeatherAmbience()/ResumeWeatherAmbience()` | 天气音效 |
| `GraphicsManager` | `WeatherSlotSettings`、`WeatherSlotObject`、`WeatherSlot` | 天气卡 UI 槽位 |
| `CardVisualsManager` / `CardPooling` | `WeatherCardVisualsPrefab`、`WeatherCardsQueue` | 天气卡视觉池 |

### 3.2 驱动关系（"改变天气由谁驱动"）

```
Gamemode.Weather / PlayerCharacter.Weather / CharacterPerk.OverrideWeather   (CardData)
        └─► GameManager.StartingWeather : CardData
              └─► 生成 InGameCardBase（prefab = GameManager.WeatherCardPrefab，类型 CardTypes.Weather）
                    └─► GameManager.CurrentWeatherCard : InGameCardBase        ★状态字段★
                          └─► CardData.WeatherEffects : WeatherSet
                                ├─► AmbienceImageEffect.SetWeather(WeatherSet _Weather)   [static] ★应用点★
                                │     ├─ ApplyColors(WeatherColors _Colors)
                                │     ├─ SpawnCardVisualEffect(WeatherSpecialEffect, InGameCardBase) [static]
                                │     └─ RemoveWeatherEffects() / (static) HideWeatherEffects() / ShowWeatherEffects()
                                └─► SoundManager.SetWeatherAmbience(CardData _Weather)
```
【签名事实】全部字段/方法来自 `_dumps/00_cpp_ALL_types.txt`。
【真机旁证】冷启动日志里 `GameManager:Awake()` 打印 `Current Gamemode: CharacterList` / `Current Character: `（字面量 2252-2255），
说明这两个种子在 `Awake` 阶段就已确定。
【字面量旁证】`"Trying to pin a weather card, this is not supported"` + `"_Weather"`（字面量 4441/4442）→ 存在 `CardTypes.Weather` 专用分支。
【推断】"改变天气"= 换掉 `GameManager.CurrentWeatherCard`（或它的 `CardData`），随后由 `AmbienceImageEffect` 应用。
**但我无法从签名证明"哪段代码写了 `CurrentWeatherCard`"** —— 这是本报告唯一需要真机确认的关键点（§4-E3）。

**【新证据·真机存档】天气卡是一张会被"消耗"的真实卡**（`logs/save_backup_Slot_1.json`）：
```json
"CurrentWeatherCard":{"CardID":"24c3c1920ab17a44f8d0879ab93da943(TropicalIsland_ClearStart)",
                      "SlotInformation":{"SlotType":5,"SlotIndex":0},   // 5 = SlotsTypes.Weather
                      "Spoilage":43.0, "Usage":0.0, …}
```
- 天气卡落在 `SlotsTypes.Weather` 槽位（`GraphicsManager.WeatherSlotSettings/WeatherSlotObject/WeatherSlot` 就是它的 UI）；
- 它的 `Spoilage` 在走（存档值 43.0），`CardData` 侧对应 `BaseSpoilageRate` 等速率字段 →
  **【推断，强】天气变化的机制 = 天气卡随时间被"消耗/到期"，然后有新的天气卡被生成/换上**。
- 由此得到比 E3 更省事的钩子：**Hook `GameManager.AddCard(...)` / `ProduceCards(...)` 并按
  `CardData.CardType == CardTypes.Weather` 过滤**，即可捕获"天气被换上"的瞬间（见 §3.3 ⑦）。

### 3.3 hook 点候选（链路三）

| # | 目标方法（全签名） | 参数含义 | 为什么适合 | 风险 / 备注 |
|---|---|---|---|---|
| ① | `AmbienceImageEffect.SetWeather(WeatherSet _Weather)`（**static**, void） | 要应用的天气表现集 | **"天气生效"的唯一应用点**：Postfix 里 `__instance`/静态参数都能拿到；可在此把 mod 的 `WeatherSet` 合并/替换 | 静态方法：Harmony 静态补丁可以挂（本环境已验证 `CheatsManager.*` 这类可挂，见 RESULT.md §5.3）；**只改视觉/表现，不改逻辑状态** |
| ② | `AmbienceImageEffect.get_CurrentWeather()` / `set_CurrentWeather(WeatherSet)`（属性） | `WeatherSet` | 属性 setter 是**可挂的实例方法**（`set_CurrentWeather(WeatherSet value)`），比字段直写更安全 | 属性可能是内联的（若被内联，Harmony 可能挂不到实体）；用 `MethodInfo != 0` 判据先验证 |
| ③ | `GameManager.ChangeEnvironment() : IEnumerator`（d__391） | 无参 | **环境切换**（天气通常随环境变）。是"世界状态大幅变化"的钩子 | 同样是 `IEnumerator`，Postfix 只在创建时触发（同链路二③的坑） |
| ④ | `GameManager.CurrentWeatherCard : InGameCardBase`（字段） | — | 直接改这个字段 = 换天气；也是**读取当前天气**最直接的路径 | 需要 il2cpp 字段偏移写入（团队已有 `GetOrGen`/`wbarrier` 工具链）；写字段不会触发 UI 刷新，需配合 ① 或 `GraphicsManager` 刷新 |
| ⑤ | `SoundManager.SetWeatherAmbience(CardData _Weather)`（inst, void） | 天气卡数据 | 天气切换的**伴随调用**，可作为"天气真的换了"的可靠信号（Prefix 即可记录） | 只处理音频；若某次切换不调它则不触发 |
| ⑥ | `CharacterPerk.OverrideWeather : CardData` | — | **最省事的"注入天气"路径**：给一个已装备的 mod 特质设 `OverrideWeather` | 需真机确认游戏在哪一步读它（开局？换装时？） |
| ⑦ | `GameManager.AddCard(CardData _Data, InGameCardBase _FromCard, bool _InCurrentEnv, TransferedDurabilities, bool _UseDefaultInventory, SpawningLiquid, Vector2Int, bool _MoveView) : IEnumerator`（另有 29 参重载） | `_Data` = 要生成的卡的数据 | **捕获"天气卡被换上"的通用钩子**：Prefix 里判 `_Data.CardType == CardTypes.Weather` 即可；比找 `CurrentWeatherCard` 的写入点更可靠 | `IEnumerator`（同③的时机坑）；29 参重载签名很长，需精确匹配。**建议先用 E3-b 验证是否 100% 经过这里** |
| ⑧ | `InGameCardBase.CurrentSpoilage : float`（存档字段 `CardSaveData.Spoilage`） | 天气卡的"剩余时长" | 存档实证：天气卡 `Spoilage=43.0` 在走 → **天气到期换卡必然先走 spoilage 归零**，可作为"天气即将改变"的预报点 | 需要每帧/每次结算读，代价高；不建议作为主钩子 |
| ⑨ | `DismantleActionButton.DropReport : CollectionDropReport`（字段） | 该按钮**已经算好的**掉落报表 | **零成本读取点**：说明 `GetCollectionDropsReport` 的返回值直达 UI（PC mod `Action.cs:20` 就是这么读的）。可用来**验证 E2**：若这里能看到 mod 数据，说明查询点注入生效 | 是"缓存副本"，改它不影响结算；只适合读/校验 |

---

## 4. 需要真机在 5 分钟内证伪/确认的 5 个实验（给持机方）

| 编号 | 目的 | 做法（Harmony Postfix，只打日志） | 判据 |
|---|---|---|---|
| **E1** | 确认链路的实际入口 | 挂 `EncounterPopup.StartEncounter` / `InGameEncounter.Init` / `EncounterPopup.ApplyEncounterResult`，打印 `__instance` 与参数 | 触发一次遭遇后，三者是否都被调用、顺序如何 |
| **E2** | 确认掉落查询点被谁调用 | 挂 `GameManager.GetCollectionDropsReport` + `CardsDropCollection.FillDropList`，打印 `_Action.ActionName` / `_CheckEnvironment` / `CollectionName` | 探索一次，看谁先被调用、`_Collection` 是哪个、`CurrentDrop` 计数 |
| **E3** | **确认天气写入点**（本报告缺口） | 同时挂 `AmbienceImageEffect.SetWeather`、`set_CurrentWeather`、`SoundManager.SetWeatherAmbience`、`GameManager.ChangeEnvironment`，每个只打一行日志 | 用户改一次天气，看**哪一个**被调用 → 那就是唯一的注入点 |
| **E3-b** | 天气换卡的通用入口 | 挂 `GameManager.AddCard`（8 参重载）Prefix，判 `_Data.CardType == CardTypes.Weather` 时打日志（含 `_Data.name`、`Spoilage`） | 存档实证天气卡有 `Spoilage=43.0` 在走 → 若换天气时经 `AddCard` 传入 Weather 卡，则**这就是最稳的钩子**（不用管 `CurrentWeatherCard` 谁写的） |
| **E4** | 确认 `FillDropsList` 的读写方向 | 挂 `CardData.FillDropsList`，Prefix 打印 `__instance.UniqueID` + `AllDrops.Count`，Postfix 再打印一次 | 计数是否变化 → 证明它写 `AllDrops` |
| **E5** | 确认 `GameManager` 事件可用 | 不做 Harmony，直接 `GameManager.Instance.OnCollectionDropsSelected += r => log(r.TotalValue)`（在 `GuideManager.Start` Prefix 里订阅） | 事件是否触发 → 若可用，**链路二/一的部分需求可以完全不挂补丁** |

> 这 5 个实验都只打日志、不改数据，风险与 `HarmonyProbe` 同级（该工程已证明此环境补丁可挂、补丁体确实执行）。

---

## 5. 三条链路的注入点小结（供 PLAN.md 直接引用）

| 链路 | 首选注入点 | 类型 | 为什么 |
|---|---|---|---|
| 遭遇/事件 | `EncounterPopup.ApplyEncounterResult()`（Prefix）+ `EncounterPopup.AddNonWeaponActions()`（Postfix） | 同步方法，可挂 | 结算与选项组装都在同步方法里；只往集合里**挂指向 mod 自己对象的引用**，不改被引用对象的内容 → 不产生共享数据污染 |
| 探索掉落 | `GameManager.GetCollectionDropsReport(...)`（Postfix, `ref __result`） | 纯查询，无副作用 | 返回的就是候选掉落表；不改游戏数据对象 |
| 天气 | **待 E3 确认**；目前最可能是 `AmbienceImageEffect.SetWeather(WeatherSet)` / `set_CurrentWeather` | 静态/属性 | "应用点"确定；"写入点"待实测 |
