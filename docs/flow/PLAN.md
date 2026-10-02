# CSTI Android v1.05o — 插入方案对比与推荐路线（PLAN）

> task-5 / flow-mapper / 只读分析，**不写代码**
> 配套：`FLOW.md`（时序）、`OBJECTS.md`（对象/注册表）、`INTERACTIONS.md`（三条链路 hook 点）
> 前提（lead 已确认）：**这个 MiniLoader 从来没有在任何版本上成功跑通过手机版**，仓库最后一次提交是"最终宣告失败" → 本文按**从零设计插入点**写，不假设存在可借用的参考实现。

---

## 0. 结论（先给答案）

### 推荐路线 = **D：C 为主 · B 为接线 · A′ 仅在"必须有对象"时兜底**

| 层 | 用什么 | 一句话 |
|---|---|---|
| **逻辑/结算层**（掉落、遭遇结算、天气应用） | **C — 在查询点合并** | 不创建、不写任何游戏数据对象；只在游戏**读**数据的那几个函数上把 mod 数据并进返回值 |
| **可见性层**（特质页签、图鉴、统计页、蓝图页、掉落列表…） | **B — 只往游戏已有集合追加** | 已被真机验证可行（`PerkTabGroup.ContainedPerks` 等），仍是"改游戏对象"，但改的是**集合**不是**元素内容**，可逆、可审计 |
| **内容对象层**（必须有 `CardData` 实体才能进存档/被解析时） | **A′ — 最小化克隆 + 深拷贝中立化 + 立即注册** | 只在**必须**时用；把 A 的"浅拷贝污染"从根上换成"深拷贝"，并把数量压到个位数 |

**一句话给 lead**：不要再把力气花在"造出 2858 个对象那样的东西"上——
`GetCollectionDropsReport` / `FillDropList` / `ApplyEncounterResult` / `SetWeather` 这四个点能覆盖用户报的全部三个坏掉的功能（事件没反应、探索没掉落、改天气无效），
而且**它们全都允许"只读游戏对象、只改返回值"**。这就是我们要插的地方。

**但必须诚实地说清一个边界**（见 §4.3）：**C 不能凭空造出玩家可见的新物品**。
如果 mod 要求"掉出一件全新的道具/解锁一个全新特质"，那件道具必须存在一个 `CardData` 对象（否则 `GameManager.AddCard(CardData,...)`、
存档写回、`UniqueIDScriptable.GetFromID` 都无从下手）——这一步只能由 A′（或 B：复用游戏已有对象）承担。
所以 D 是**分层**的，不是"C 全包"。

---

## 1. 约束回顾（这些约束决定了 A 为什么走不通）

| # | 约束 | 证据 | 对方案的影响 |
|---|---|---|---|
| 1 | **没有真实 IL**（interop 是 `il2cpp_runtime_invoke` 跳板，Cpp2IL 输出是 dummy） | `FLOW.md §1`；本次实测 `OnEnable` 体 = 1 条 `ret` / 19 条跳板指令 | 所有"字段真实类型/偏移"都只能靠元数据 + 运行时探测 → **A 的 warp 引擎必须自己重建类型系统**（团队已为此修了 3 个上游 bug，TASK4-FINDINGS §2） |
| 2 | **234 个 ICall 被裁**，托管包装调用即崩 | `icall_missing_real.txt`；`RESULT.md §10.6`、`TASK4-FINDINGS §10.3` | A 依赖 `Sprite.Create`/`Texture2D.LoadRawTextureData`/`AudioClip.Construct_Internal` 等；音频**至今无解**（`TASK4-FINDINGS §10.7`） |
| 3 | **Unity 的克隆是浅拷贝** | `TASK4-FINDINGS §11`：mod 克隆游戏资产 → 引用类型字段与游戏共享 → 事件/掉落/天气被写坏 | **这是 A 的致命伤**，也是"根治需深拷贝"这句话的由来 |
| 4 | **`OnUpdate` / `MelonCoroutines` 在本环境从来没被调用** | `RESULT.md §3.5`（所有 `updateTicks=0`） | 任何"轮询式注入/延迟初始化"都不能用；必须挂在**同步回调**上（Harmony 或 C# 事件） |
| 5 | **Harmony 可用**，判据是"方法有没有 native 实体" | `RESULT.md §2.1/§5.3`（三个目标方法挂载成功、补丁体命中；`MethodInfo != 0`） | C 与 B 的挂载在技术上可行 |
| 6 | 游戏内容对象在 `GameLoad.Awake()` 之前就绪（2858 个） | 真机 `logs/unity_coldstart.txt:15-16` + `UniqueIDScriptable.OnEnable/RegisterID` | B/C 只需"读取"，不需要参与构建 |

---

## 2. 四个方案逐条拆解

### 2.1 A（现状）：克隆游戏对象当模板 + 新建 ScriptableObject + 包装资源

**机制（PC 版 MiniLoader 原样）**：`LoadResources.LoadEditorScriptableObject()` → `WarpFunc.JsonCommonWarpper(obj, json)`
把 JSON 逐字段写进一个"从游戏资产克隆出来的"对象；资源（图/音）另行包装。
出处：`D:\RiderProjects\CSTI-ModLoader\CSTI-MiniLoader\LoadUtil\LoadResources.cs:25-55`、`WarpperClassGen\WarpFunc.cs`。

| 维度 | 评估 |
|---|---|
| **可行性** | ⚠️ 部分可行但**永不收敛**。团队已为此实现：`MainGen` 字段真实类型探测、`MainGenTools` 写回修正、`TryResolveRef` 回查注册表、`NeutralizeClone`（中立化克隆体的引用字段）、数组用 `il2cpp_array_new` + `wbarrier_set_field` 写槽、`IconPack` 手工 DXT5→PNG→`LoadImage`→`CreateSprite_Injected` 图标管线、156 个 ICall 变体探测。**这些全部是在"A 这条路上"打的地基。** |
| **风险** | ① 浅拷贝污染（`TASK4-FINDINGS §11` 的现场事故：开局事件不响应 / 探索无掉落 / 改天气无效）；② `JsonUtility.FromJsonOverwrite(json, clone)` **仍会写进共享子对象**（TASK4 自己标注为"遗留"）；③ 音频无解；④ 每个新字段类型都要再修一次 warp 引擎。 |
| **对游戏原数据的影响** | **高**：只要漏掉一个引用类型字段没中立化，写 mod 数据 = 改游戏数据。`[INVARIANT]` 采样（136 个对象，签名和 900→900）是**事后检测**，不是预防。 |
| **工作量** | 已投入：数千行 + 多轮真机；剩余：深拷贝根治（未做，TASK4 标"风险较高"）、音频（无解）、更多 ICall | 
| **结论** | ❌ **不应作为主路线**。因为它的成本几乎全部花在"对抗引擎裁剪 + 对抗 Unity 浅拷贝"，而这两件事**与 mod 想做的事无关**。 |

### 2.2 A′（A 的修正版）：深拷贝克隆 + 中立化 + 立即注册（仅在必须造对象时使用）

**机制**：不用 `Object::Internal_CloneSingle` 的浅拷贝语义，改为
```
il2cpp_object_new(cls)                     // 造同类型空对象（不触发 Unity 资产语义）
   └─ 逐字段深拷贝：值类型直接复制；string 复制；数组/List/Dictionary 新建容器并递归深拷贝元素；
        UnityEngine.Object 派生字段 → 只复制引用（资产引用，不复制）
   └─ 立刻 UniqueID = 新 GUID；AllUniqueObjects[GUID] = obj；Init()
```
**证据支撑**：`TASK4-FINDINGS §11` 的表格已经把"哪些字段类型该怎么处理"列全了（数组/List/Dictionary/string/纯托管类/UnityEngine.Object 派生/值类型），
`Diag.NeutralizeClone` 已实现其中一半；缺的一半是"**递归深拷贝嵌套的纯托管类子对象**"（当前是"换成空实例"，会丢默认数据）。

| 维度 | 评估 |
|---|---|
| 可行性 | ✔ 高。目标明确、可增量验证；不需要新的 ICall（`il2cpp_object_new` 已在用） |
| 风险 | 中：递归深拷贝要处理循环引用（`CardData` 之间互相引用是常态）→ 必须用 `Dictionary<IntPtr,object>` 记忆化；`UnityEngine.Object` 派生字段必须**不**深拷贝（否则丢资产） |
| 对游戏原数据影响 | **低**：只要"新建容器 + 深拷贝"，写入永远落在 mod 私有对象上 |
| 工作量 | 中：约 1 个"递归深拷贝器" + 循环引用记忆化 + 用 `[INVARIANT]` 采样做门禁。**比继续打 A 的地基便宜得多** |
| 结论 | ✔ **作为 A 的替代品保留**，只在"必须有真实游戏对象"时用 |

### 2.3 B（只注入已有对象的集合）

**机制**：不造对象（或对象由 A′ 造好），只把它**追加进游戏自己的集合/数组**。

**已被验证可行的注入点**（真机或 PC 源码）：

| 注入点 | 证据 | 状态 |
|---|---|---|
| `PerkTabGroup.ContainedPerks : List<CharacterPerk>` | 真机 `[TAB] PkTab1..3` 各 +6（`TASK4-FINDINGS §4/§6`） | ✅ 真机 |
| `MainMenu.AllCharacterPerks / UnlockedPerks` | 真机 `[MENUFIX] AllCharacterPerks 89→95`（`§8`） | ✅ 真机 |
| `PerkGroup.PerksList : CharacterPerk[]`（数组，需 `Array.Resize`） | PC `LoadPatchMain.cs:341-362`；真机 `[DESC] PerksList=[17 项]` | ✅ 真机 |
| `GuideManager.AllEntries : List<GuideEntry>` | PC `LoadPatchMain.cs:293-303` | ✅ PC |
| `GraphicsManager.AllStatsList.Tabs[].ContainedStats` | PC `LoadPatchMain.cs:462-483` | ✅ PC |
| `BlueprintModelsScreen.BlueprintTabs` / `CardTabGroup.SubGroups` / `IncludedCards` / `ShopSortingList` | PC `LoadPatchMain.cs:428-537` | ✅ PC |
| `CardFilterGroup.IncludedCards` | PC `LoadPatchMain.cs:393-406` | ✅ PC |
| `Gamemode.PlayableCharacters : PlayerCharacter[]` | PC `LoadResources.cs:162-175` | ✅ PC |

| 维度 | 评估 |
|---|---|
| 可行性 | ✔✔ 最高（有真机成功记录） |
| 风险 | 低-中：① 数组型集合必须 `Array.Resize` 后**赋回属性**（PC 版踩过 `SetLiByWarpper` 忘写回，TASK4 §2-B）；② 有些集合会被游戏重写/清空 → 需要"周期维护"（团队已用 `[TABFIX]` 每 4 秒自愈，`§7`）；③ **前提是对象已存在** |
| 对游戏原数据影响 | 中：改的是游戏集合本身（元素被追加）。可逆、可审计、影响范围局限在"列表内容" |
| 工作量 | 低：每个注入点 10~30 行 |
| 结论 | ✔✔ **必用**。但注意它**不解决**"结算逻辑"问题（掉落/事件/天气坏掉不是集合问题） |

### 2.4 C（在查询点合并数据）★ 推荐主路线

**机制**：在游戏**读取**数据的函数上做 Postfix，把 mod 数据合并进**返回值**；游戏的数据对象一个字节都不写。

**候选查询点（详见 `INTERACTIONS.md`）**：

| 链路 | 查询/应用点 | 签名 | 合并方式 |
|---|---|---|---|
| 掉落 | `GameManager.GetCollectionDropsReport` | `(CardAction, InGameCardBase, bool) → CollectionDropReport` | Postfix `ref __result`：把 `DropsInfo : CollectionDropInfo[]` 扩容后追加 mod 集合，并按 `RangeUpTo` 重排区间 |
| 掉落（滚动） | `CardsDropCollection.FillDropList` | `(bool _CheckEnvironment, int _Multiplier) → void` | Postfix：往本次的 `CurrentDrop` 追加；**必须在同一结算周期内可回滚**（否则就是 TASK4 §11 的坑） |
| 遭遇结算 | `EncounterPopup.ApplyEncounterResult` | `() → bool` | Prefix：往 `CurrentEncounter.EncounterModel.*Effects.DroppedCards : List<CardData>` 追加 mod 掉落（**挂引用安全**；危险的是改被引用对象的内容） |
| 遭遇选项 | `EncounterPopup.AddNonWeaponActions` | `() → void` | Postfix：往 `EncounterPlayerActions : List<...>` 追加 mod 选项 |
| 天气应用 | `AmbienceImageEffect.SetWeather` / `set_CurrentWeather` | `(WeatherSet) → void` | Postfix：把 mod 的 `WeatherSet` 合并/替换进去（**待真机 E3 确认写入点**，见 `INTERACTIONS.md §4`） |
| 事件 | `GameManager.OnCollectionDropsSelected`（C# 事件） | `Action<CollectionDropReport>` | 直接 `+=`，**零补丁** |

| 维度 | 评估 |
|---|---|
| 可行性 | ✔ 高，但**有一个必须先验证的前提**（§4.3） |
| 风险 | ① **必须确认"查询点"与"结算点"共用同一段计算**——否则只在 UI 预览里看到 mod 掉落、实际不掉（这是 C 的头号风险，用实验 E2 5 分钟可证伪）；② `CollectionDropReport`/`CollectionDropInfo` 是 **struct**，Postfix 必须 `ref __result`；③ `IEnumerator` 方法（`StandardCardCollectionDrop`/`ProduceCards`/`ChangeEnvironment`）的 Postfix **在迭代器创建时**触发，不是结算后 |
| 对游戏原数据影响 | **零**（不写游戏对象）——这是相对 A 的根本优势，直接消灭 `[INVARIANT]` 担心的那类事故 |
| 工作量 | 低-中：每个查询点 20~60 行（含 struct 重建）；难点在"算清 `RangeUpTo` 区间权重"（需要读 `CollectionDropInfo.FinalWeight/BonusWeight`，这些是现成的） |
| 结论 | ✔✔ **作为主路线**。它是唯一"改得越多、污染越少"的方案 |

---

## 3. 对比矩阵

| 维度 | A 克隆新建（现状） | A′ 深拷贝克隆 | B 注入已有集合 | **C 查询点合并** |
|---|---|---|---|---|
| 真机成功记录 | ❌（从未跑通） | ⚪（未实现） | ✅ 特质页签/菜单列表 | ⚪（本次提出，未实现） |
| 需要新建游戏对象 | 是（大量） | 是（少量） | 否（对象另供） | **否** |
| 写游戏数据对象 | **是（高危）** | 否（写私有对象） | 是（只改集合成员） | **否** |
| 受影响 ICall 依赖 | 高（Sprite/Texture/Audio） | 中 | 低 | **极低** |
| 浅拷贝污染风险 | **高（已发生）** | 低 | 无（不写元素内容） | **无** |
| 能否让"新卡牌/新道具"出现 | 能 | 能 | 复用已有对象才能 | **不能**（只能让已有对象出现在查询结果里） |
| 能否修好"事件/掉落/天气" | 副作用式（越修越坏） | 无关 | **不能** | **能** |
| 实现工作量 | 已投入巨大，仍未收敛 | 中（1 个深拷贝器） | 低（每点 10~30 行） | 低-中（每点 20~60 行） |
| 回归门禁 | `[INVARIANT]` 采样（事后） | 同左 | 同左 | 天然满足（不写游戏对象） |
| **推荐角色** | ❌ 弃用 | ⚪ 兜底 | ✅ 接线 | ✅✅ **主力** |

---

## 4. 推荐路线 D 的完整设计

### 4.1 分层职责

```
┌─ 内容供给层（只有"必须有真实对象"时才用）──────────────────────────────┐
│  A′：深拷贝克隆 + NeutralizeClone + 立刻 UniqueID/注册 + Init()        │
│  产物：少量游戏类型对象（CardData / CharacterPerk / Encounter …）      │
│  约束：必须可枚举、可审计；数量压到个位数；每次创建后立刻跑 [INVARIANT] │
└───────────────────────────────────────────────────────────────────────┘
                    │ 对象交给 ▼
┌─ 可见性层（B）────────────────────────────────────────────────────────┐
│  追加进游戏自己的集合：PerkTabGroup.ContainedPerks / MainMenu.* /     │
│  PerkGroup.PerksList / CardTabGroup.IncludedCards / StatListTab.      │
│  ContainedStats / BlueprintTabs / GuideManager.AllEntries / …          │
│  时机：GraphicsManager.Init (Postfix) / GuideManager.Start (Prefix)    │
│  自愈：周期校正（沿用 [TABFIX] 思路）                                  │
└───────────────────────────────────────────────────────────────────────┘
                    │ 逻辑交给 ▼
┌─ 逻辑层（C）★核心────────────────────────────────────────────────────┐
│  遭遇：ApplyEncounterResult(Prefix) + AddNonWeaponActions(Postfix)    │
│  掉落：GetCollectionDropsReport(Postfix, ref __result)                │
│        + FillDropList(Postfix, 当期可回滚)                            │
│  天气：SetWeather / set_CurrentWeather（待 E3 定）                    │
│  事件：GameManager.OnCollectionDropsSelected += …（零补丁）            │
│  铁律：**不写任何游戏数据对象的字段**                                  │
└───────────────────────────────────────────────────────────────────────┘
```

### 4.2 分阶段落地（建议顺序，每阶段都有独立验收）

| 阶段 | 目标 | 交付 | 验收判据 |
|---|---|---|---|
| **P0**（0.5 天） | 用实验 E1~E5（`INTERACTIONS.md §4`）确认 4 个查询点的真实调用顺序与"查询/结算是否同源" | 一份 hook 命中日志 | 每条链路都能指出"唯一入口"，且**不掉帧、不崩**（重复冷启动 3 次，参照 `RESULT.md §3.3` 协议） |
| **P1**（1~2 天） | **C-掉落**：`GetCollectionDropsReport` Postfix 合并 mod 候选 | mod 的掉落出现在成功率/预览里 | 预览里能看到 mod 掉落 **且** 实际也能掉出来（若只能看到不能掉 → 触发 P1' 预案） |
| **P1'**（预案） | 若查询/结算不同源：改挂 `CardsDropCollection.FillDropList` Postfix，或对 `StandardCardCollectionDrop` 做**枚举器包装**（Prefix 返回 `false` 并返回自己的 `IEnumerator`） | 同上 | 同上 + 结算后游戏对象 `[INVARIANT]` 零变化 |
| **P2**（1 天） | **C-遭遇**：`ApplyEncounterResult` Prefix 追加 `EncounterResultEffect.DroppedCards`；`AddNonWeaponActions` Postfix 追加 mod 选项 | 开局事件选项有反应、遭遇能掉 mod 物品 | 事件选项点击后行为正确；**追加前后 `[INVARIANT]` 采样零变化**（只挂引用、不改被引用对象） |
| **P3**（1 天） | **B-接线**：把 mod 对象接进 `PerkTabGroup.ContainedPerks` / `MainMenu.AllCharacterPerks` / `UnlockedPerks` / `PerkGroup.PerksList`（沿用已验证的经验） | 特质/内容可见 | 真机 `[TAB]`/`[MENUFIX]` 计数正确且稳定 16 分钟不自失（沿用现有自愈） |
| **P4**（待定） | **C-天气**：按 E3 结论在唯一写入点合并 | 改天气生效 | 用户手动验证 |
| **P5**（按需） | **A′**：仅在"必须新对象"时启用深拷贝克隆 | 深拷贝器 + 深拷贝回归用例 | `[INVARIANT]` 采样：mod 加载前后游戏自带对象**完全不变**（沿用 §11 的 136 对象签名法） |

### 4.3 C 的边界（必须让 lead 知道，避免期望错位）

| 想做的效果 | C 能否单独完成 | 说明 |
|---|---|---|
| 让**已有**游戏物品出现在 mod 的掉落表里 | ✅ 能 | 这是 C 的主场 |
| 让 mod **新道具**掉出来 | ⚠️ 需要对象存在 | `GameManager.AddCard(CardData _Data, ...)` 的第一个参数必须是 `CardData`；没有对象就无从生成。→ 需要 B（复用已有 CardData）或 A′（造一个） |
| 想让 mod 内容**在存档里活下来** | ⚠️ 需要对象存在 | **但这件事已经被真机证明可行**：`logs/save_backup_SaveData.json` 里 `UnlockedPerks` 已含 6 个 Windy 特质，格式 `"<UniqueID>(<名字>)"`（如 `8adb7a05993748ba8827f2b91128c96d(Windy_air)`）。存档只存 ID 字符串 → 下次启动时该 ID 必须在 `AllUniqueObjects` 里 |
| 让 mod 特质出现在角色创建界面 | ❌ 不能 | 那是**集合成员**问题 → B（`PerkTabGroup.ContainedPerks` 等，已验证） |
| 让 mod 内容进存档 | ⚠️ 需要对象存在 | 同上：先有对象 + GUID，再由游戏自己的 `SaveID` 写 `GUID(名字)` |
| 改天气表现 | ✅ 能 | `AmbienceImageEffect.SetWeather` / `set_CurrentWeather` |
| 改遭遇结算（加掉落/改数值） | ✅ 能 | `ApplyEncounterResult` / `AddNonWeaponActions` |

> **一句话**：**B 负责"让东西看得见"，C 负责"让逻辑算得对"，A′ 只在"必须有个对象"时兜底。**

### 4.4 为什么不是"纯 C"

因为用户的实际 mod（如 Windy）要"新特质 + 新物品 + 图标"。这些**必然**需要对象存在。
但请注意：**对象存在 ≠ 对象要参与逻辑**。最好的组合是：
用 A′ 造少量对象（把风险圈在一个受控的、可深拷贝的范围内），
用 B 把它们接进游戏集合（已验证），
用 C 保证**结算逻辑永远读的是"游戏数据 + mod 数据的合并视图"，而不是被 mod 改过的游戏对象**。
这样，TASK4 §11 那类"越修越坏"的事故从结构上不可能再发生。

---

## 5. 决策速查表（按"mod 想做什么"查）

| mod 想做的 | 推荐 | 插入点 |
|---|---|---|
| 加掉落/改掉落概率 | **C** | `GameManager.GetCollectionDropsReport` Postfix（`ref __result`） |
| 加遭遇选项/改结算 | **C** | `EncounterPopup.AddNonWeaponActions` / `ApplyEncounterResult` |
| 改天气 | **C** | `AmbienceImageEffect.SetWeather` / `set_CurrentWeather`（待 E3） |
| 加特质到页签 | **B** | `PerkTabGroup.ContainedPerks` + `MainMenu.AllCharacterPerks/UnlockedPerks`（真机已验证） |
| 加图鉴/统计页/蓝图页条目 | **B** | `GuideManager.AllEntries` / `StatListTab.ContainedStats` / `BlueprintModelsScreen.BlueprintTabs` |
| 加新道具（必须有对象） | **A′ + B** | 深拷贝克隆 → 注册 → 接进 `CardTabGroup.IncludedCards` 等 |
| 改数值/平衡（不新增内容） | **C**（读时改） | 对应查询点；若必须持久化则用 A′ 造私有副本 |
| 加音频 | ❌ 暂无解 | `TASK4-FINDINGS §10.7`（ICall 全被裁且无符号可绑） |

---

## 6. 风险与回退

| 风险 | 触发条件 | 缓解 | 回退 |
|---|---|---|---|
| 查询点≠结算点（C 的最大风险） | P1 验收发现"预览有、实际不掉" | 实验 E2 提前判定；P1' 预案 | 改挂 `FillDropList`；再不行对枚举器做包装 |
| struct 返回值改不回 | `CollectionDropReport` 是 `ValueType` | Postfix 用 `ref CollectionDropReport __result` 并整份重建 `DropsInfo` 数组 | 改为"只读展示 + 在结算点合并" |
| `IEnumerator` hook 时机错 | 挂 `StandardCardCollectionDrop` 等 | 不用它们；改用同步查询/应用点 | 枚举器包装 |
| 游戏重写集合（元素被清） | `ContainedPerks` 之类被游戏重建 | 周期自愈（`[TABFIX]` 思路，4s 一次） | 挂在"界面打开"的同步方法上即时补齐 |
| 补丁体本身崩 | 补丁里调了缺失 ICall 的托管包装 | 铁律：补丁体内只做"读字段 + 改返回值"，**不调 Unity 托管包装**（`RESULT.md §5.4`） | 移除该补丁，退化到 B |
| 结论不可信（单次启动） | 该环境崩溃呈"可活可死" | 验收一律**重复冷启动 3 次**（`RESULT.md §3.3`） | — |

---

## 7. 立即行动项（按优先级）

1. **P0/E1~E5 实验**（`INTERACTIONS.md §4`）：只打日志、不改数据。**这是 C 路线唯一的前置条件。**
   特别是 **E3（天气写入点）** 与 **E2（查询/结算是否同源）** —— 前者是本报告的已知缺口，后者决定 P1 还是 P1'。
2. 把 `INTERACTIONS.md §2.4①`（`GetCollectionDropsReport` Postfix）做成第一个可运行原型：
   目标最小 = "往某个已有 `CardsDropCollection` 的候选表里并进一条已有游戏的 `CardData`"，然后**看它能不能真的掉出来**。
3. 每次改动后跑一次 `[INVARIANT]` 采样（游戏自带对象指纹），**"零变化"作为合入门禁**。
4. **不要**再往 A 的 warp 引擎上继续加功能，除非某个具体效果被 §5 决策表判定为"必须有对象"。

---

## 附：本文所有关键论断的出处索引

| 论断 | 出处 |
|---|---|
| 游戏内容对象 2858 个、各类型规模 | `mods-06/CSTI-MiniLoader-06/TASK4-FINDINGS.md`；`Diag.cs` 注册表统计函数 |
| 注册在 `OnEnable→RegisterID`、`AllUniqueObjects` 是 GUID 字典 | `_dumps/00_cpp_ALL_types.txt` `UniqueIDScriptable` 段；`_dumps/metadata_literals.txt:2210-2213` |
| 注册早于 `GameLoad.Awake()` | 真机 `logs/unity_coldstart.txt:15-16`（消息 + 栈帧） |
| 掉落查询/结算函数签名 | `_dumps/00_cpp_ALL_types.txt` `GameManager` 段（含 `d__364`/`d__365`） |
| `CollectionDropReport`/`CollectionDropInfo` 是 struct 及其字段 | 同上 |
| 遭遇结算与选项组装方法 | 同上 `EncounterPopup` / `InGameEncounter` 段；`mods-06/CstiDetailedCardProgress/Encounter.cs:37` |
| 天气驱动链 | 同上 `AmbienceImageEffect` / `WeatherSet` / `GameManager` / `CharacterPerk.OverrideWeather` 段；字面量 4441/4442 |
| A 的浅拷贝污染与已投入的地基 | `mods-06/CSTI-MiniLoader-06/TASK4-FINDINGS.md §2/§3/§10/§11` |
| B 的真机成功记录 | `TASK4-FINDINGS.md §4/§6/§7/§8` |
| PC MiniLoader 的注入点清单 | `D:\RiderProjects\CSTI-ModLoader\CSTI-MiniLoader\Patchers\LoadPatchMain.cs`、`LoadUtil\LoadResources.cs` |
| Harmony 可用 / `OnUpdate` 死 / 不要调缺失 ICall 的托管包装 | `mods-06/HarmonyProbe/RESULT.md §2.1/§3.5/§5` |
| 单次启动结论不可信（须重复冷启动） | `RESULT.md §3.3` |
| mod 对象**确实会落盘**（`GUID(名字)` 复合 ID、Windy 6 个特质已在存档里） | `logs/save_backup_SaveData.json`（真机存档，2026-10-02 20:50）+ `UniqueIDScriptable.SaveID/RemoveNamesFromComplexID` |
| 掉落记账会被存档（`CollectionUses`） | `logs/save_backup_Slot_1.json`：`"CollectionUses":[{"CollectionName":"Coral","CollectionDrops":{"x":0,"y":2}}]` + `CardSaveData.CollectionUses : List<CollectionDropsSaveData>` |
| 天气卡是真实卡实例、`SlotType=5(Weather)`、`Spoilage` 在走 | 同上存档 + `SlotsTypes.Weather` 枚举序 |
