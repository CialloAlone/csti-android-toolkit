# MiniLoader 机理说明书：JSON 如何变成「游戏认可的实体」

> 范围：通读 `D:\RiderProjects\ml-installer-06\mods-06\CSTI-MiniLoader-06\**`（移植版，Android/MelonLoader），
> 对照原仓库 `D:\RiderProjects\CSTI-ModLoader\` 的 `NoReflection` 分支（`CSTI-MiniLoader/` 目录），
> 并用 `D:\RiderProjects\csti\BepInEx\plugins\Windy\**`（作者侧数据）与
> `D:\RiderProjects\csti\mods\CSTI-ModEditor-master\CSTI-JsonData\**`（编辑器 schema）作为字段语义依据。
>
> 本文纯**只读分析**，不含业务代码改动。
>
> **代码快照**（写作时刻的磁盘版本，注意这份代码仍在被其他人改动）：
>
> | 文件 | 最后写入 | 说明 |
> |---|---|---|
> | `WarpperClassGen/WarpFunc.cs` | 21:03:09 | 嵌套 warp / List 原地 warp **已重新启用** |
> | `Diag.cs` | 21:03:24 | 新增 `DeepDetach` / `DumpEffectArray` / `[EFFECT2]` 探针 |
> | `LoadUtil/LoadResources.cs` | 21:03:27 | 新增 `[WARP]首见类型` / `[EVENTCARD]` 诊断 |
> | `LoadUtil/LoadArchMod.cs` | 20:52:55 | GSM inert、`SkipGameDataBaseAdd` 分流 |
> | `WarpperClassGen/MainGenTools.cs` | 20:11:05 | `TryResolveRef`、字段宿主修复、写回修复 |
> | `WarpperClassGen/MainGen.cs` | 20:07:38 | 真实字段类型 + il2cpp 值类型判定 |
> | `Patchers/LoadPatchMain.cs` | 20:45:34 | 步骤 0–9 与探针 |
> | `MiniLoader.cs` | 20:58:41 | 全局集合与开关 |
> | `HookFree.cs` | 20:14:57 | 免 hook 轮询注入 |
>
> **证据约定**：`【实测】`= 真机日志/离线解包的原始行；`【代码】`= 源码位置；`【推断】`= 由命名/签名推得，尚未直接验证。

---

## 0. 一句话总览

MiniLoader 干的事只有三件：

1. **把 `.modArch_V3` 里的字节解成 JSON 文本**，并按清单判断每个条目该变成哪种游戏类型；
2. **造一个该类型的实例**（本机是「克隆同类型的游戏现有资产」，不是 `ScriptableObject.CreateInstance`），
   把 JSON 灌进它的字段（`JsonUtility.FromJsonOverwrite`）；
3. **把 JSON 里那些「写不了字面量」的字段（引用类型）用 `XxxWarpType` + `XxxWarpData` 占位对二次回填**
   （`WarpFunc.JsonCommonWarpper` → `MainGenTools.CommonSet`），再把对象登记进各种字典、队列、页签。

第 3 步是 mod 内容「活不活」的分水岭：**JSON 里能写字面量的字段（数字/字符串/布尔/内联子对象）由第 2 步搞定；
凡是「要指向另一个游戏对象」的字段（卡、标签、声音、图标、状态、集合…）全部依赖第 3 步的 warp。**

而 Windy 这个包 **没有任何 Lua**（见 §1.1），所以「事件选项点了有没有反应」100% 由 warp 是否成功决定。

---

## 1. 材料与清单

### 1.1 `.modArch_V3` 的实际结构【实测】

离线解包结果（`mods-06/CSTI-MiniLoader-06/tools/_run/archdump.txt`）：

```
== modName = Windy
区块顺序 ==  ImgBLK → JsonsBLK → LocalBLK → AudioBLK → LuaBLK

ImgBLK    : 图集 2 个（8192×8192, fmt=12=DXT5），精灵 56 + 4
JsonsBLK  : CardData 172 / GameSourceModify 63 / ScriptableObject 19 / GameStat 17
            / CharacterPerk 6 / Encounter 4 / SelfTriggeredAction 4 / ModInfo.json 1 / PerkGroup 1
LocalBLK  : 1 条 SimpCn.csv（82,719 字符，键形如 Windy_Windy_CardDescription）
AudioBLK  : 1 个子块（解码后 1,705,832 B）
LuaBLK    : **0 条**
```

> **`LuaBLK` 条目数 = 0 → 这个 mod 包里没有任何 Lua 脚本。**
> 因此 mod 的全部行为只能由「JSON 字段 + `WarpData/WarpType` 占位对」表达；
> 一旦 warp 没把引用填进去，游戏侧就没有任何脚本层可以兜底 —— 症状就是「点了没反应」。

### 1.2 三层数据模型

```
.modArch_V3 字节
   │  LZ4 解码 → BinaryReader（LoadArchMod.LoadJsonsBLK_V3）
   ▼
MapperItem / MapperObject / MapperList / ObjInt / ObjString…（LoadUtil/MapperObject.cs）
   │  这是一棵「字符串用 StringMapper 字典压缩」的树；它同时是 KVProvider 的子类
   ▼
KVProvider 视图 + ToJson()（LoadUtil/KVProvider.cs）
   │  warp 读字段用 KVProvider 索引器；灌字段用 ToJson() 生成 JSON 文本
   ▼
JsonUtility.FromJsonOverwrite(jsonText, obj)   ← Unity 只认文本
   ▼
游戏对象的真实字段（il2cpp 内存布局）
```

要点（`LoadUtil/MapperObject.cs`）：

- `MapperItem.Read`（`MapperObject.cs:51`）按 1 字节类型标签读：1=对象、2=数组、3=bool、4=double、5=int、6=long、7=字符串。
- 字符串不存正文，存 `StringMapper` 的索引（`ObjString.WriteSelf/ReadSelf`，`MapperObject.cs:361-377`）；
  `ObjString.String` 通过 `Mapper.GetKey(Val)` 还原（`MapperObject.cs:211`）。
- `MapperItem.Keys` / `this[string]` / `this[int]`（`MapperObject.cs:171-240`）就是 warp 遍历 JSON 用的接口。
- `ToJson()`（`MapperObject.cs:244-249` + `MapperObject/MapperList/ObjString.ToJson`）手写序列化，
  负责把树还原成 Unity 能吃的 JSON 文本。**注意它只转义 `\n` 和 `"`**（`MapperObject.cs:384-385, 497-498`）。

---

## 2. 完整链路：从 `JsonsBLK` 到「游戏认可的实体」

### 2.1 谁在什么时候驱动

移植版**放弃了三个 Harmony 补丁**（原版挂在 `LocalizationManager.LoadLanguage` / `GuideManager.Start` / `GraphicsManager.Init`），
改为 `Pump`（挂在 `CheatsManager.Update` 上的每帧泵）→ `HookFree.Tick()`：

- `MiniLoader.OnInitializeMelon`（`MiniLoader.cs:156`）在 `SkipHarmonyPatchAll=true` 时只安装泵，
  并置 `DeferredInit=true`（`MiniLoader.cs:198`）；
- `HookFree.Tick`（`HookFree.cs:59`）每帧做：
  1. `LoadResources.LoadGameResourceFromRegistry()` 返回 true（游戏注册表有内容）后，
  2. `SelfCheck.CaptureBefore()` → `RealShims.PrimeTemplates()`（建克隆模板表）→
  3. `MiniLoader.RunDeferredInit()`（`MiniLoader.cs:127`）→ `LoadPatchMain.LoadAndInit()`。

**这一步的时序是整个 loader 的地基**：`PrimeTemplates` 必须在「游戏资源已就绪」之后，
否则 `CreateLike` 没有模板；`LoadAndInit` 必须在模板表建好之后，否则造不出对象。

### 2.2 九步主管道（`Patchers/LoadPatchMain.cs:LoadAndInit`，L314-387）

| # | 步骤 | 代码位置 | 读 | 写 |
|---|---|---|---|---|
| 0 | 探针（纹理/ICall/精灵/音频） | `LoadPatchMain.cs:320-338` | — | 日志 |
| 1 | `LoadResources.LoadGameResource()` | `LoadPatchMain.cs:322` → `LoadResources.cs:108` | `UniqueIDScriptable.AllUniqueObjects`（优先）/ `Object.FindObjectsOfType` | `AllItemDictionary`、`AllGUIDDict`、`AllScriptableObjectDict` |
| 2 | `LoadArchMod.LoadAllArchMod()` | `LoadPatchMain.cs:343` → `LoadArchMod.cs:174` | `Mods/**/*.modArch_V3` | 同上 + 各等待队列 |
| 3 | `LoadResources.LoadEditorScriptableObject()` | `LoadPatchMain.cs:345` → `LoadResources.cs:26` | `WaitForWarpperEditorNoGuidList` | 各 `WaitForAdd*` 队列 |
| 4 | `LoadResources.WarpperAllEditorMods()` | `LoadPatchMain.cs:356` → `LoadResources.cs:134` | `WaitForWarpperEditorGuidDict` | 对象字段本身 + `WaitForAdd*` |
| 5 | `LoadResources.WarpperAllEditorGameSrouces()` | `LoadPatchMain.cs:362` → `LoadResources.cs:376` | `WaitForWarpperEditorGameSourceGUIDList` | 游戏对象字段（当前 **inert**） |
| 6 | `LoadResources.MatchAndWarpperAllEditorGameSrouce()` | `LoadPatchMain.cs:364` → `LoadResources.cs:300` | `AllGUIDDict`、`AllCardTagGuidCardDataDict`、`MatchTagWarpData` | 游戏卡字段 |
| 7 | `AddPerkGroup()` | `LoadPatchMain.cs:389-447` | `WaitForAddPerkGroup`、`ItemDictionary(PerkGroup)`、`Diag.BuildPerkGroupIndex` | `PerkGroup.PerksList` |
| 7.5 | `Diag.RegisterModPerksIntoTabGroups()` | `LoadPatchMain.cs:374` | `ModPerks` | `PerkTabGroup.ContainedPerks` 等 |
| 8 | 遍历 `AllGUIDDict` 调 `Init()` | `LoadPatchMain.cs:377-380` | `AllGUIDDict` | 游戏内部注册（GUID→对象） |
| 9 | done | `LoadPatchMain.cs:381` | — | — |

原版同名步骤顺序**完全一致**（`git show NoReflection:CSTI-MiniLoader/Patchers/LoadPatchMain.cs` L306-328），
我们只是插入了探针、perk 页签登记与开关。

### 2.3 第 2 步内部：`JsonsBLK` 的三个分支（`LoadArchMod.LoadJsonsBLK_V3`，L480-642）

先做类型普查：

- `TypesInGameAssembly()`（`LoadArchMod.cs:45`）——**只枚举 `Assembly-CSharp`**，不用 `AccessTools.AllTypes()`
  （后者会物化 IL2CPP 代理程序集，Mono 断言直接 abort）。
- `allUniqueIDScriptableTypes` = `UniqueIDScriptable` 子类（`LoadArchMod.cs:485`）
- `allScriptableObjectTypes` = `ScriptableObject` 子类且不是 `UniqueIDScriptable`（`LoadArchMod.cs:489`）

然后对每个条目（`itemFlg==2` 是映射表，其余是数据）：

| 分支 | 判定 | 关键动作 | 代码 |
|---|---|---|---|
| `ModInfo.json` | `listStr[0]=="ModInfo.json"` | 什么都不做 | L522 |
| **ScriptableObject** | `listStr[0]=="ScriptableObject"` | 按 `listStr[1]`（类型名）找类型 → 造对象 → `NeutralizeClone` → `FromJsonOverwrite` → 入 `ItemDictionary` → 进 `WaitForWarpperEditorNoGuidList` → `RegObj(名字,…)` | L526-558 |
| **GameSourceModify** | `listStr[0]=="GameSourceModify"` | 只把 `Path.GetFileNameWithoutExtension(listStr.Last())` 当 key 入队（**Android 上反斜杠路径 → 永远查不到 → inert**） | L559-581 |
| **UniqueIDScriptable** | 其它 | 按 `listStr[0]` 找类型 → 校验 `UniqueID` 非空 → 造对象 → `NeutralizeClone` → `FromJsonOverwrite` → `name = "{modName}_{文件名}"` → `AllGUIDDict[guid]` → （可选）`GameLoad.Instance.DataBase.AllData.Add` → 进 `WaitForWarpperEditorGuidDict` → `RegObj(guid,…)` | L582-639 |

**「决定目标类型」这一步的全部依据**：
`listStr[0]`（= 清单里的分类字符串，如 `CardData`/`Encounter`/`GameSourceModify`/`ScriptableObject`）+
`listStr[1]`（ScriptableObject 分支才用，是**类型名**）+ `listStr.Last()`（文件路径，用来取对象名）。

### 2.4 第 2 步内部：创建对象实例（本机是「模板克隆」而不是「新建」）

```csharp
// LoadUtil/LoadArchMod.cs:536-537（ScriptableObject 分支）
var obj = CreateScriptableObjectViaShim(find_ScriptableObjectT) as ScriptableObject;
if (obj == null) obj = ScriptableObject.CreateInstance(Il2CppType.From(find_ScriptableObjectT));

// LoadUtil/LoadArchMod.cs:595-596（UniqueIDScriptable 分支）
var card = CreateScriptableObjectViaShim(type) as UniqueIDScriptable;
if (card == null) card = ScriptableObject.CreateInstance(Il2CppType.From(type)) as UniqueIDScriptable;
```

`CreateScriptableObjectViaShim`（`LoadArchMod.cs:223-241`）反射调用
`CstiICallFix.RealShims.CreateLike(Type)`（`mods-06/CstiICallFix/RealShims.cs:203-230`）：

1. `PrimeTemplates()`（`RealShims.cs:130-172`）遍历 `UniqueIDScriptable.AllUniqueObjects`，
   用 `il2cpp_object_get_class` + `il2cpp_class_get_name` 取**原生类名**，
   把「类名 → 该类型的一个现成实例指针」记进 `TemplateMap`；
2. `CreateLike` 用 **已注册的** `UnityEngine.Object::Internal_CloneSingle` 克隆这个模板，
   再用 `Activator.CreateInstance(type, clonePtr)` 包成托管代理。

> **这决定了后面所有麻烦**：`Internal_CloneSingle` 是 **Unity 的浅拷贝**。
> 克隆体身上所有引用类型字段（数组/List/字典/字符串/子对象）**与游戏资产共享同一个实例**。
> 因此「克隆之后、任何写入之前」必须先做 `NeutralizeClone`（§2.5），否则
> `FromJsonOverwrite` / warp / `FillDropsList` 会把 mod 数据写进游戏自带的容器里。

对照**原版**（`git show NoReflection:CSTI-MiniLoader/LoadUtil/LoadArchMod.cs` L243、L282）：

```csharp
var obj  = ScriptableObject.CreateInstance(Il2CppType.From(find_ScriptableObjectT));   // L243
var card = ScriptableObject.CreateInstance(Il2CppType.From(type)) as UniqueIDScriptable; // L282
```

原版走的是**真·新建**（引擎侧 `CreateScriptableObjectInstanceFromName`），
本机这个 ICall 被裁剪（`RealShims.cs:57` 就是给它做 shim 的），所以才换成克隆。
**代价是浅拷贝语义**；收益是对象能造出来。

### 2.5 `NeutralizeClone` / `DeepDetach`：把「共享」改成「私有」

`Diag.NeutralizeClone(ScriptableObject clone, string tag)`（`Diag.cs:709`）→ `DeepDetach(obj, tag, 0, stats)`（`Diag.cs:740`），
递归深度 ≤ 3（`Diag.cs:742`），对 `MainGen.GetOrGen(obj.GetType())` 里的每个引用类型字段：

| 字段种类 | 处理 | 代码 |
|---|---|---|
| `string` | 写 0（null） | `Diag.cs:756-761` |
| `List<T>` | 换成**新的空 List** | `Diag.cs:763-770` |
| `Dictionary<K,V>` | 换成**新的空字典** | `Diag.cs:772-780` |
| `Il2CppReferenceArray<T>`（`IsIl2CppArrayType`） | **等长**新数组；元素是 `UnityEngine.Object` → 保持共享；否则 `il2cpp_object_new` 造**同类型新实例**并递归 | `Diag.cs:782-831` |
| 纯托管类子对象（`LocalizedString`/`CardAction`/`DurabilityStat`…） | `il2cpp_object_new` 同类型新实例 + 递归 | `Diag.cs:833-852` |
| `UnityEngine.Object` 派生（Sprite/AudioClip/CardData/Gamemode…） | **不动**（资产引用，留给 warp 填） | `Diag.cs:752` |
| 值类型 | **不动** | `Diag.cs:751` |

> ★ 与「上一版」的关键差别（`Diag.cs:731-739` 的注释）：**数组保长度、元素建对象**，
> 而不是清成 0 长度。理由：`DismantleActions` 这类内联数组在清空后，
> 嵌套 warp 就「没有对象可写」了。
> 【实测】`[NEUTRAL] [ARCH] CardData 新建容器/实例=…`、`[NEUTRAL] 总计…=277276`（21:08 轮）。

### 2.6 `JsonUtility.FromJsonOverwrite`：把 JSON 灌进字段

`LoadArchMod.cs:547`（ScriptableObject）与 `LoadArchMod.cs:602`（UniqueIDScriptable），
参数是 `mapperObject.ToJson()`。**这一步负责所有「可以写字面量」的字段**，包括：

- 数字/布尔/字符串/枚举/struct；
- `LocalizedString` 这种内联小对象（`DefaultText` 里的中文就是这里进去的）；
- **内联的对象数组**，例如 `DismantleActions: [{...},{...},{...}]`、`ProducedCards: [{...}]`、
  `SpecialDurability1: {...}`。
  【实测】`[EFFECT] EVENTCARD Windy_Event_Gift …DismantleActions 条数=3`、
  `ProducedCards=1 首个集合名=Weather`（21:08）→ 这些**不是 warp 的功劳**。

Unity 的 `JsonUtility` 会**忽略未知键** —— 这正是 `XxxWarpData`/`XxxWarpType` 能安全地和真实字段同处一份 JSON 的原因。

### 2.7 注册：六个集合的键规则

`MiniLoader.cs:39-67` 定义全部集合；`MiniLoader.RegObj(id, o, type)`（`MiniLoader.cs:69`）：

```csharp
if (ItemDictionary(type).ContainsKey(id)) return;   // 先到先得，不覆盖
ItemDictionary(type)[id] = o;
if (type.IsSubclassOf(typeof(UniqueIDScriptable))) AllGUIDDict[id] = (UniqueIDScriptable)o;
if (type.IsSubclassOf(typeof(ScriptableObject)))   AllScriptableObjectDict[id] = (ScriptableObject)o;
```

| 集合 | 键 | 谁写 | 谁读 |
|---|---|---|---|
| `AllItemDictionary[Type][key]` | 类型 → (名或 GUID) | `RegObj` | `ItemDictionary()`、`TryResolveRef`、warp 的目标解析 |
| `AllGUIDDict[guid]` | UniqueID | `LoadArchMod.cs:614`、`RegObj` | `WarpperAllEditorGameSrouces`、`MatchAnd…`、步骤 8 `Init()` |
| `AllScriptableObjectDict[name]` | 名字 | `RegObj` | 诊断 |
| `WaitForWarpperEditorGuidDict[guid]` | GUID | `LoadArchMod.cs:630` | 步骤 4 |
| `WaitForWarpperEditorNoGuidList` | 列表 | `LoadArchMod.cs:555` | 步骤 3 |
| `WaitForWarpperEditorGameSourceGUIDList` | 列表 | `LoadArchMod.cs:579` | 步骤 5（当前 inert） |

**必须分清两种注册键**（这决定了 warp 能不能查到东西）：

- **UniqueIDScriptable** → 以 **GUID** 注册（`AllGUIDDict`），名字只在 `card.name` 里；
- **其它 ScriptableObject**（CardTag / EquipmentTag / ActionTag / WeatherSet / CardTabGroup / GuideEntry …）
  → 以**名字**注册；
- **Sprite / AudioClip** → 由 `ImgBLK`/`AudioBLK` 用 `CleanName` 后的**文件名**注册
  （`LoadArchMod.cs:334-335`、`422-423`、`701`、`708`）。

### 2.8 后处理：四个 `Warpper*` 各干什么

**`LoadEditorScriptableObject()`（`LoadResources.cs:26`）**
弹 `WaitForWarpperEditorNoGuidList` → `WarpFunc.JsonCommonWarpper(item.Obj, json)` → 分流：
`CardTabGroup`（名字以 `Tab_` 开头）→ `WaitForAddCardTabGroup`；
`ContentPage`（`*Default`/`*Main`）→ `WaitForAddDefaultContentPage`/`WaitForAddMainContentPage`；
`GuideEntry` → `WaitForAddGuideEntry`。

**`WarpperAllEditorMods()`（`LoadResources.cs:134`）** —— 主干：
`WarpFunc.ResetStats()` → 逐个弹 `WaitForWarpperEditorGuidDict` → `WarpFunc.JsonCommonWarpper(Obj, json)`，然后按类型分流：

- `CardData`：`BlueprintCardData*` → `WaitForAddBlueprintCard`；`ItemCardDataCardTabGpGroup` → 直接 `CardTabGroup.IncludedCards.Add`；
  `CardDataCardFilterGroup` → `WaitForAddCardFilterGroupCard`；最后 **`cardData.FillDropsList()`**（`LoadResources.cs:206`，
  游戏自己的方法；【实测】调用后 `CardData.AllDrops` 由 0 项变 3 项，故推断它把动作的 `ProducedCards` 汇总成 `AllDrops`，确切规则未验，见 §7.3）；
- `CharacterPerk`：收集进 `MiniLoader.ModPerks`；有 `CharacterPerkPerkGroup` 键 → `WaitForAddPerkGroup`（本版本该键已不存在，见 TASK4）；
- `GameStat`：`VisibleGameStatStatListTab` → `WaitForAddVisibleGameStat`；
- `PlayerCharacter`：把角色 `Resize` 进每个 `Gamemode.PlayableCharacters`，再进 `WaitForAddJournalPlayerCharacter`。
最后置 `LoadPatchMain.OnceWarp = true`（`LoadResources.cs:297`），`ContentDisplayer` 协程靠它同步。

**`WarpperAllEditorGameSrouces()`（`LoadResources.cs:376`）**
弹 `WaitForWarpperEditorGameSourceGUIDList`：`item.Obj==null` → `AllGUIDDict.TryGetValue(item.CardDirOrGuid)`，
查不到 `continue`；查到后若有 `MatchTagWarpData` → 转 `WaitForMatchAndWarpperEditorGameSourceList`；
若有 `ModLoaderSpecialOverwrite=true` → 先 `FromJsonOverwrite`；再 `JsonCommonWarpper`；若是 `CardData` → `FillDropsList()`。

**`MatchAndWarpperAllEditorGameSrouce()`（`LoadResources.cs:300`）**
先用 `AllGUIDDict` 里所有 `CardData.CardTags` 建「标签名 → {GUID→CardData}」索引，
再对 `MatchTagWarpData` 做交集筛选、`MatchTypeWarpData` 过滤 `CardType`，命中者 `JsonCommonWarpper` + `FillDropsList`。

> 【实测】本版 `AllGUIDDict` 里的 mod 卡 `CardTags=[0 项]`（21:08 日志），所以这张索引表基本是空的
> —— 见 §5 的「按名注册丢失」。

---

## 3. `WarpData` / `WarpType` 语义

### 3.1 命名约定

对任意字段 `Foo`：

| JSON 形态 | 含义 | 代码路径 |
|---|---|---|
| `Foo` | 字面量/内联对象/内联对象数组 → 由 `FromJsonOverwrite` 处理 | `LoadArchMod.cs:547/602` |
| `FooWarpType` + `FooWarpData`（字符串） | 单引用：`Foo = 解析(FooWarpData)` | `CommonSet` → `SetByWarpper<T>` |
| `FooWarpType` + `FooWarpData`（字符串数组） | 引用集合：解析每个字符串 | `SetArrByWarpper<T>` / `SetLiByWarpper<T>` |
| `FooWarpType` + `FooWarpData`（对象数组） | 造对象集合：为每个元素**新建 T 实例**再 warp 它 | `SetArrNoWarpper<T>` / `SetLiNoWarpper<T>` |

`FooWarpType` 缺 `FooWarpData` / 值不是 int → 整条跳过（`WarpFunc.cs:74-77`）；
遍历时遇到裸 `FooWarpData`（没有配对的 WarpType）→ 跳过（`WarpFunc.cs:89-90`）。

**实测分布**（对 `Windy/**` 全部 278 个 JSON 做正则统计）：

| WarpType | 出现次数 | 典型字段 |
|---|---|---|
| 3 | **3256** | `CardBackground/CardImage/CardTags/WhenCreatedSounds/DroppedCard/EquipmentTags/Stat/TriggerCards/ActionSounds/TransformInto/OverrideIcon/ActionTags …` |
| 4 | 85 | `DismantleActions/CardInteractions/NOTAffectedThings/ExplorationResults/ProducedCards/TimeOfDayMods/CardDropChanceModifiers …` |
| 5 | 21 | `ExtraDurabilityModifications/DismantleActions/OnZero/ProducedCards/SpoilageTime` |
| 6 | 3 | `SpawningBlockedBy` |

（0/1/2 在本 mod 数据里**一次都没用**。）

### 3.2 数值 → 语义

枚举定义：`WarpperClassGen/MainGen.cs:14-23`（原版 `CSTI-MiniLoader/WarpperClassGen/MainGen.cs:12-21`，**数值完全一致**）

| 值 | 名字 | 意图 | **loader 实际行为** | 找不到目标时 |
|---|---|---|---|---|
| 0 | `NONE` | 不处理 | 无分支 → 与 3 同（仅单引用时有效） | 字段不写 |
| 1 | `COPY` | 覆盖 | 无分支；数组路径**不清空**（等于追加） | 静默丢元素 |
| 2 | `CUSTOM` | 自定义 | 无分支 | 同上 |
| 3 | `REFERENCE` | 引用已有对象 | 单引用：查表赋值；集合：逐元素解析后**整体替换** | 单引用：字段不写；集合：写回「只含解析成功项」的新容器 |
| 4 | `ADD` | 追加 | 集合：**保留原有元素**再追加（`warpType != MODIFY` ⇒ 不清空） | 同 3 |
| 5 | `MODIFY` | 原位修改 | 集合：**先 `Clear()` 再填**（`MainGenTools.cs:275`、`308`、`336`、`391`） | 同 3 |
| 6 | `ADD_REFERENCE` | 追加引用 | 与 4 相同（`IsString` 元素 ⇒ 走 `SetArrByWarpper`，不清空） | 同 3 |

> **代码里唯一一处真正看 `warpType` 的地方**就是那句 `if (warpType == WarpType.MODIFY) list.Clear();`
> （`MainGenTools.cs:275`、`308`、`336`、`391`）。其余 0/1/2/4/6 在数组路径上**行为完全等价**；
> 在单引用路径（`IsString`）上 `warpType` **根本不被读取**（`SetByWarpper` 签名里有参数但不用）。
> 也就是说：**4 与 5 的差别 = 有没有先清空；6 只是「元素是字符串的 4」；3 只对单引用有意义。**

### 3.3 查找规则（`MainGenTools.TryResolveRef<T>`，L123-159）

```csharp
// ① mod 自己注册的字典：类型 → 键（名字或 GUID）
if (AllItemDictionary.TryGetValue(typeof(T), out var typed) &&
    typed.TryGetValue(id, out var o) && o is T t0) return t0;
// ② 回查游戏注册表：GUID → UniqueIDScriptable，再 TryCast 成 T
var reg = UniqueIDScriptable.AllUniqueObjects;
if (reg.TryGetValue(id, out var uid) && Diag.CastOrNull<T>(uid) != null) return t1;
```

- 路径 ① 的键**可能是名字也可能是 GUID**，取决于对象是 2.7 里的哪一类；
- 路径 ② 是**移植版新增的补丁**，用来救「游戏自带对象」：游戏自带的 2858 个对象被 Il2CppInterop
  统一包成 `UniqueIDScriptable`，全部落在 `ItemDictionary(typeof(UniqueIDScriptable))` 里且**以 GUID 为键**
  （`Diag.cs:20-21`），所以按具体类型分的那张表里根本没有它们。
- **两条都查不到 → 返回 false → 字段保持原样（不改、不报错、不计数）。**

### 3.4 五个必须知道的语义陷阱

1. **继承字段在 warp 视野之外。**
   `MainGen.GetOrGen` 用 `AccessTools.GetDeclaredFields(type)`（`MainGen.cs:52`，`BindingFlags.DeclaredOnly`）——
   **只收本类声明的 `NativeFieldInfoPtr_*`**，基类字段不在表里。
   【实测】`[GEN] CharacterPerk 生成字段(19)`（= `CharacterPerk.json` 自有 19 个），
   而基类 `CompletableObject` 的 `ActionObjectives/CardsOnBoardObjectives/NestedObjectives/PlayedCharacter/RequiredDifficultyScore`
   全部报 `[WARP跳过] 字段未生成`（live.log 20:57:00.895-897）。
   【实测】`[EFFECT] 元素类型=DismantleCardAction 字段(6)`，而这 6 个正是
   `DismantleCardAction.json` 里**独有的** 6 个（`MinMaxExplorationDrops/ExplorationValue/PerformUponInspection/
   DontCloseInspectionWindow/AlwaysShow/VisibilityWithInventory`）——`ActionName`、`ProducedCards` 等 39 个
   继承自 `CardAction` 的字段**不在 gen 表里**。
2. **`Il2CppReferenceArray<T>` 不是 CLR 数组。**
   `Il2CppReferenceArray<T> : Il2CppArrayBase<T>` 是一个**类**（`il2cppinterop-fork/Il2CppInterop.Runtime/InteropTypes/Arrays/Il2CppReferenceArray.cs:7`），
   因此 `fldType.IsArray == false`。
   而 `WarpFunc.JsonCommonWarpper` 的内联数组下潜分支判断的正是 `tuple.fldType.IsArray`（`WarpFunc.cs:158`）——
   **对 `Il2CppReferenceArray<T>` 字段永远不成立**，只有 `List<T>` 字段能下潜（`WarpFunc.cs:142-143`）。
   `MainGenTools.CommonSet` 反而判得对（它用 `Il2CppArrayBase<>` 基类比较，`MainGenTools.cs:171-180`）——
   两处判定标准不一致，是同一个坑的两个面。
3. **集合解析失败会「静默缩水」。**
   `SetArrByWarpper`/`SetLiByWarpper` 先 `Clear`（MODIFY 时）或读旧值，然后把**解析成功**的元素塞进新容器再整字段写回。
   解析失败的项**被丢掉，字段仍被替换**。
   【实测】`[ARR] CardData.CardTags arr原有=有 请求=3 解析到=0` + `[ARR] 写回完成 CardTags`
   → `CardTags` 被替换成**空数组**（21:04 日志）。
4. **`SetLiByWarpper` 的写回是移植版新加的。**
   原版（`git show NoReflection:CSTI-MiniLoader/WarpperClassGen/MainGenTools.cs` L179-200）算完 `list` 就丢掉了，
   **从不写回字段** —— 也就是说原版的 List 型引用 warp 是**空操作**。现在补上了
   `il2cpp_gc_wbarrier_set_field`（`MainGenTools.cs:292-293`）。
5. **字段偏移必须按「宿主类型」查，不能按元素类型 `T` 查。**
   原版用 `MainGen.GetOrGen(typeof(T))`（`…/MainGenTools.cs:162`、`184`、`229`、`261`），
   宿主其实是 `baseObj` → 静默 return。移植版改成 `baseObj.GetType()`（`MainGenTools.cs:246`、269、302、330、386）。

---

## 4. 事件/遭遇是怎么被触发和执行的

### 4.1 `Event_Gift` 的真实数据形状（不是一层，是两层）

**第一层 `CardData/Event_Gift.json`**（`UniqueID=fb9aff8d…`，`CardType=3`=事件卡，名字「精灵的祝福」）：

| 选项 i | `ActionName.DefaultText` | `ProducedCards[0].CollectionName` | `ProducedCards[0].DroppedCards[0].DroppedCardWarpData` |
|---|---|---|---|
| 0 | 请改变天气吧！ | `Weather` | `a86af31aa56211ed9bc3902e162ca70e` → **Event_Gift_Weather**（Windy 自建事件卡） |
| 1 | 请给我药物吧！ | `Drug` | `30a05127a56d11ed8845902e162ca70e` → **Event_Gift_Drug**（Windy 自建事件卡） |
| 2 | 想要一份压缩肉干 | `Jerky` | `d28409ee8ec144b59748b5af5fd49a45` → **Jerky**（Windy 自建物品卡，CardType=0） |

全部 `DroppedCardWarpType = 3`（REFERENCE）、`Quantity=(1,1)`、`CollectionWeight=1`。
`DismantleActions` 本身**没有** `WarpData/WarpType` 配对 → 是**内联对象数组**。

**第二层 `Event_Gift_Weather.json` / `Event_Gift_Drug.json`**：这两张卡自己也是事件卡（`CardType=3`，名字都叫「自然之力」），
各自又有 3 个选项，形状与第一层完全相同：

| 文件 | 选项 | `CollectionName` | 掉落的卡（按编辑器 GUID 表反查） |
|---|---|---|---|
| `Event_Gift_Weather` | 希望天气下雨 / 希望天气放晴 / 希望狂风大作 | `LightRain` / `Clear` / `Storm` | **游戏自带天气卡**：`TropicalIsland_HeavyRainLong` / `TropicalIsland_Clear` / `TropicalIsland_Storm` |
| `Event_Gift_Drug` | 想要止痛药 / 想要抗生素 / 想要解毒的药 | `Painkillers` / `Antibiotics` / `Antidote` | 前两个是**游戏自带物品卡**，第三个是 Windy 的 `Antidote` |

（反查依据：`CSTI-ModEditor-master/CSTI-JsonData/UniqueIDScriptableGUID/CardData/Weather.json`、
`…/Item.json` 是 GUID→名字表；`912b2a05…` = `TropicalIsland_HeavyRainLong(大雨)`、
`dc4523f5…` = `TropicalIsland_Clear(晴朗)`、`6e196596…` = `TropicalIsland_Storm(风暴)`。）

> **所以「天气」和「药物/肉干」是同一个机制**：选项 = `CardAction` → `ProducedCards`（一个 `CardsDropCollection`）
> → `DroppedCards[*]`（`CardDrop`）→ `DroppedCard`（`CardData`）。
> 差别只在**指向谁**（游戏天气卡 vs 物品卡）以及**第二层还要再走一遍**。
> `CollectionName` 只是集合的**展示/日志名**（作者文档：`CardsDropCollection.txt` "Collection name"），
> 它**不是**查表键 —— 真正决定产出的是 `DroppedCards`。
> `CollectionWeight` 才是概率：作者文档写「final drop rate of the collection = weight of the collection / sum of all collection weights」。
> 【推断】下雨/放晴/狂风 = 把对应的游戏天气卡放进天气位；这一步是否等价于「改天气」需在游戏侧确认。

### 4.2 这张卡怎么进游戏、玩家怎么点得到

1. `PerkGroup/start.json` + `CharacterPerk/bless.json`：`bless` 的
   `AddedCardsWarpData = ["fb9aff8da55e11edb99e902e162ca70e"]`、`AddedCardsWarpType = 3`
   → warp 后 `CharacterPerk.AddedCards = [Windy_Event_Gift]`。
   【实测】`[MP] Windy_bless GUID=cf0ffee5… AddedCards=[1 项] Windy_Event_Gift/CardData`（21:04）。
2. 该 `CharacterPerk` 被挂进 `PerkGroup.start.PerksList`（步骤 7 `AddPerkGroup`）与
   `PerkTabGroup.ContainedPerks`（步骤 7.5），玩家在开局/角色界面选到这个「精灵的祝福」。
3. 开局时游戏把 perk 的 `AddedCards` 塞进起始卡组 / 事件队列 → 事件卡出现在场上。
4. 玩家点开事件卡 → 游戏枚举 `CardData.DismantleActions`（`List<DismantleCardAction>`）生成按钮；
   按钮文案读 `ActionName`（`LocalizedString.DefaultText` 里直接有中文，所以**不依赖 CSV**）。

### 4.3 玩家点某个选项后，游戏读什么

已知的读点（E2 探针实测签名，TASK4-FINDINGS §12.4 / `E2Probe.cs`）：

```
GameManager.GetCollectionDropsReport(CardAction _Action, InGameCardBase _FromCard, Boolean _CheckEnvironment) : CollectionDropReport
CardsDropCollection.FillDropList(Boolean _CheckEnvironment, Int32 _Multiplier) : Void

CardsDropCollection(class): CollectionName:String, CountsAsSuccess:Boolean, RevealInventory:Boolean, CollectionUses:Vector2Int,
                            CollectionWeight:Int32, StatsDropChanceModifiers:…, CardDropChanceModifiers:…,
                            DurabilitiesDropChanceModifier:DurabilityBasedDropChanceModifier, CreatedLiquid:LiquidDrop,
                            DroppedCards:Il2CppReferenceArray`1, DroppedEncounter:Encounter,
                            StatModifications:…, DurabilityModifications:AddedDurabilityModifier, …
CardDrop(class):            DroppedCard:CardData, Quantity:Vector2Int
CollectionDropReport(class): TickInfo, FromCard, FromData, FromAction, DropsInfo:Il2CppReferenceArray`1, TotalValue, BaseValue, RandomValue, SelectedDrop
CollectionDropInfo(class):   CollectionName, IsSuccess, RevealInventory, BaseWeight, …, Drops:Il2CppReferenceArray`1, CollectionUses, StatMods
```

执行顺序（**【代码】**=loader/游戏接口可证，**【推断】**=按名字与作者文档推出，待游戏侧确认）：

1. 【代码】`WarpperAllEditorMods` 在 warp 后调用 **`CardData.FillDropsList()`**（`LoadResources.cs:206`）。
   【实测】结果是 `CardData.AllDrops` 变成 `[3 项] <not-unity-object>/CardsDropCollection`（21:08 日志）
   —— 3 个选项各一个集合，正是「动作 → 产出集合」的汇总表。
2. 【推断】点击动作后，游戏按 `AllDrops`/`ProducedCards` 里的集合做**加权抽取**（权重 = `CollectionWeight`；
   `CollectionUses` 限制次数），命中某个 `CardsDropCollection`。
3. 【推断】对该集合调 `FillDropList`，把 `DroppedCards[*]` 展开成待生成的卡：
   **每个 `CardDrop` 生成 `DroppedCard`（`CardData`）× `Quantity`**；`DroppedEncounter` 非空则改为触发遭遇。
4. 【**必须已解析**】`CardDrop.DroppedCard` 必须是**非空的有效 `CardData` 指针**。
   `Quantity` 是值类型，由 `FromJsonOverwrite` 就填好了（【实测】`Quantity=(1,1)`）；
   **`DroppedCard` 只能由 warp 填**：JSON 里它是 `{"m_FileID":0,"m_PathID":0}`（Unity 资产引用占位 = null），
   真正的值在 `DroppedCardWarpData` + `DroppedCardWarpType=3` 里。
5. 【代码】如果 `DroppedCard` 为 null，`FillDropList` 没有任何对象可产出 → 玩家看到「什么都没发生」。

### 4.4 现在卡在哪：`DroppedCard = <null>`（实测）

21:08 那一轮（嵌套 warp / List 原地 warp 已重新启用、`DeepDetach` 已改成「数组保长度」）的日志：

```
[EFFECT2] 选项"请改变天气吧！"   → ProducedCards[0].DroppedCards 长度=1; [0] DroppedCard=<null> Quantity=(1,1)
[EFFECT2] 选项"请给我药物吧！"   → ProducedCards[0].DroppedCards 长度=1; [0] DroppedCard=<null> Quantity=(1,1)
[EFFECT2] 选项"想要一份压缩肉干" → ProducedCards[0].DroppedCards 长度=1; [0] DroppedCard=<null> Quantity=(1,1)
[EFFECT2] 选项"想要止痛药。"     → ProducedCards[0].DroppedCards 长度=1; [0] DroppedCard=<null> Quantity=(1,1)
```

也就是说：**集合有了、集合名对了、数量对了，唯独「要产出的那张卡」是空的**。
warp 根本没走到 `DroppedCards[i]` 这一层。有两处独立的原因，**两处都要修**：

**阻塞点 A（最可能，先修这个）：`ProducedCards` 是 `CardAction` 的继承字段，不在 `DismantleCardAction` 的 gen 表里。**
- 下潜链路：`CardData` →（`DismantleActions` 是集合，元素 `DismantleCardAction`）→ 元素对象 →
  它的 JSON 键 `ProducedCards` → `genInfos.TryGetValue("ProducedCards")`。
- 【实测】`[EFFECT] 元素类型=DismantleCardAction 字段(6)`，只有**自有** 6 个字段；
  `ProducedCards` 在 `CardAction.json`（自有 40 字段，含 `ProducedCards`）里。
- 【代码】`MainGen.cs:52` 用 `AccessTools.GetDeclaredFields`（DeclaredOnly）→ 继承字段全丢。
- 同样的现象在 `CharacterPerk` 上被独立证实（`字段未生成: CharacterPerk.ActionObjectives` 等 5 条）。
- 后果：warp 在 `DismantleCardAction` 这一层**看不到 `ProducedCards`**，于是不会下潜，
  里面的 `DroppedCardWarpType/WarpData` 永远没机会执行。
  `[WARP统计] 跳过(无字段)=185799` 里相当一部分就是这类继承字段。

**阻塞点 B（修完 A 之后会立刻撞上）：`CardsDropCollection.DroppedCards` 是 `Il2CppReferenceArray<CardDrop>`，不是 CLR 数组。**
- 【实测】`[E2SIG] CardsDropCollection(class) gen字段(20): … DroppedCards:Il2CppReferenceArray`1 …`。
- 【代码】`Il2CppReferenceArray<T>` 是类（`Il2CppReferenceArray.cs:7`），`Type.IsArray == false`；
  而 `WarpFunc.cs:158` 的下潜条件是 `tuple.fldType.IsArray`。
- 后果：即使能下潜到 `ProducedCards`，也**进不去 `DroppedCards` 的元素**，
  `CardDrop.DroppedCard` 依然为 null。

**判定哪个在起作用的探针（一行即可）**：在 `WarpFunc.JsonCommonWarpper` 里对
`DismantleActions`/`ProducedCards`/`DroppedCards` 三个键打印
`ft.Name + "/gtd=" + (IsGenericType ? GetGenericTypeDefinition().Name : "-") + "/IsArray=" + IsArray`
以及 `genInfos.ContainsKey(fieldName)`；或对 `DismantleCardAction` 打印 `genInfos.Count` 与 `Keys`。

### 4.5 天气链路「额外」多出来的两个依赖

`Event_Gift_Weather` 的选项要真的改天气，除了 §4.4 的通用条件外还必须：

1. `TryResolveRef<CardData>` 能找到**游戏自带的天气卡**（GUID 路径）。
   【代码】走 `TryResolveRef` 路径 ②（`UniqueIDScriptable.AllUniqueObjects` + `TryCast<CardData>`），
   前提是步骤 1 的注册表导入成功 —— 【实测】`[HOOKFREE] 资源注册表导入完成`，可认为成立。
2. 作者为「风暴」卡写的 **GameSourceModify 必须生效**：
   `GameSourceModify/weather/6e196596764a19442b72b2368656552e.json`（= `TropicalIsland_Storm`）内容是

   ```json
   { "SpoilageTimeWarpType": 5,
     "SpoilageTimeWarpData": {
       "OnZeroWarpType": 5,
       "OnZeroWarpData": {
         "ProducedCardsWarpType": 4,
         "ProducedCardsWarpData": [ { "CollectionName": "Storm", "CollectionWeight": 0, "DroppedCards": [] } ] } } }
   ```

   即：**对游戏自带风暴卡做 `MODIFY`，在 `SpoilageTime.OnZero.ProducedCards` 上 `ADD` 一个 `CollectionName="Storm"` 的集合**
   —— 让风暴能自我延续/再触发。
   这条要生效需要三层条件同时成立：**(a)** GameSourceModify 条目能解析到游戏对象；
   **(b)** 嵌套对象 warp（`SpoilageTime` → `OnZero`）能下潜；**(c)** 集合的 `MODIFY`/`ADD` 语义正确。
   【实测】当前 `(a)` 直接不成立：`[GSM] … 本会改到游戏对象=0 保持inert=63`（63 条全部 inert，见 §5.6）。

---

## 5. 我们改过的偏差：原版 → 移植版 → 破坏哪一环

> 基准 = `git show NoReflection:CSTI-MiniLoader/<path>`（该目录**只存在于 `NoReflection`**；
> `git diff NoReflection master --stat -- CSTI-MiniLoader` 显示 master 里这 18 个文件全被删除，
> 所以「原版 MiniLoader」= NoReflection 的提交版本。注意工作区里 `LoadUtil/LoadArchMod.cs`、
> `MiniLoader.cs`、`Patchers/LoadPatchMain.cs` 有未提交改动，本文一律以 **HEAD 提交版**为准。）

| # | 位置 | 原版怎么写 | 我们改成什么 | 可能破坏哪一环 |
|---|---|---|---|---|
| 1 | `LoadArchMod.cs:536/595` | `ScriptableObject.CreateInstance(Il2CppType.From(type))`（真·新建） | `RealShims.CreateLike`（模板**克隆**）+ 失败回退 CreateInstance | 浅拷贝共享引用 → 必须靠 #2 兜底；且**只对游戏里已有实例的类型有效**（无模板类型无回退保障） |
| 2 | `Diag.cs:709` | **不存在** | `NeutralizeClone`/`DeepDetach`：数组**等长**换新元素、List/字典换空、string 置 null、托管子对象换新实例并递归（深度≤3） | 是 #1 的必要配套；但 List 被清空后**只能靠 `FromJsonOverwrite` 重建**（若该字段在 JSON 里是"只有 WarpData 的对象数组"就没人建了）；数组等长依赖「模板长度 = JSON 长度」 |
| 3 | `WarpFunc.cs:93-127` | 嵌套对象：`CommonGet` → `JsonCommonWarpper(subObj)` → `CommonSetFld` **总是**下潜 | 20:35 起**停用**（`StatObjSkipped++`），21:03 起**重新启用**（注释见 `WarpFunc.cs:111-116`） | 停用期 = 事件产出链断；启用后靠 #2 保证不污染游戏资产 |
| 4 | `WarpFunc.cs:138-157` | List 元素：`Cast<IList>()` → `list[i]` → warp → `list[i]=ele` **总是**执行 | 同上，先停用后启用（21:03 注释 `WarpFunc.cs:145-146`） | 同上 |
| 5 | `WarpFunc.cs:158-191` | 内联数组：`tuple.fld.FieldType.IsArray`（因为 `fld` 是 `IntPtr` 静态字段 → 恒 false，**是死代码**） | 仍是 `tuple.fldType.IsArray`（类型修对了，但 `Il2CppReferenceArray<T>.IsArray==false` 让它**仍然是死代码**） | **阻塞点 B**：`DroppedCards` 这类引用数组永远进不去 |
| 6 | `MainGen.cs:52-71` | 存 `NativeFieldInfoPtr_*` 的 `FieldInfo` → `fld.FieldType` 恒为 `System.IntPtr` → `isValueType` 恒 true、`GetGenericTypeDefinition()` 抛异常 | 真实类型从代理类**同名属性**取；`isValueType` 用 `il2cpp_class_is_valuetype` 判 | 修好了顶层引用字段；但 `GetDeclaredFields` 仍是 DeclaredOnly → **继承字段仍不可见（阻塞点 A）** |
| 7 | `MainGenTools.cs:161-163/246` 等 | `MainGen.GetOrGen(typeof(T))`（元素类型）查字段偏移；`SetLiByWarpper` 算完 list **不写回** | 改 `baseObj.GetType()`；补写回 | 修的是"没写进去"；副作用是原本静默失败的路径现在会真的改内存（所以 #2 必须先行） |
| 8 | `MainGenTools.cs:123` | 只查 `AllItemDictionary[typeof(T)]` | 新增 `TryResolveRef`：再回查 `UniqueIDScriptable.AllUniqueObjects`（GUID→TryCast） | 让"游戏自带对象"的 GUID 引用可解析；但**按名字引用游戏 ScriptableObject 仍然解析不到**（见 #9） |
| 9 | `LoadResources.cs:83-111` | `LoadGameResource()` 只走 `Object.FindObjectsOfType(Il2CppType.Of<ScriptableObject>())` + `WithGameDataFinder()` → **非 UniqueIDScriptable 按 `.name` 注册**、UniqueIDScriptable 按 GUID 注册 | 新增 `LoadGameResourceFromRegistry()` 优先执行：只导入 `AllUniqueObjects`（**全部以 GUID 为键、包装类型统一成 `UniqueIDScriptable`**），成功即 `return`，旧路径不再执行 | **按名注册的整类对象消失**（CardTag/ActionTag/EquipmentTag/WeatherSet/CardTabGroup…）。【实测】`[ARR] CardData.CardTags 请求=3 解析到=0`、`WhenCreatedSounds 解析到=0`、`ActionSounds 解析到=0` → `CardTags` 被写成**空数组**，`MatchAndWarpperAllEditorGameSrouce` 的标签索引表因而是空的 |
| 10 | `LoadArchMod.cs:566` | `var Guid = Path.GetFileNameWithoutExtension(listStr.Last())` 后用 `AllGUIDDict.TryGetValue(Guid)` **解析并传给包装队列** | 保留 `Path.GetFileNameWithoutExtension`（Android 不认 `\` → 整条路径当文件名），并且**故意**把 `Obj` 留 null、不做 `CleanName` | 63 条 GameSourceModify **全部 inert** → 所有"改游戏原版卡/天气/树/战斗"的效果全失效（§4.5 条件 a） |
| 11 | `LoadArchMod.cs:618-627` | `GameLoad.Instance.DataBase.AllData.Add(card)` 无条件执行 | `if (MiniLoader.SkipGameDataBaseAdd)` 默认 **true** → 跳过（`MiniLoader.cs:119`） | mod 卡不进游戏主数据表。事件/掉落若遍历 `AllData` 就看不到它们；`Fill()`/存档/`Find()` 也可能少一层。**当前是以"避免遍历撞上空壳卡抛异常"为目的的二分开关** |
| 12 | `MiniLoader.cs:94-100` | `OnInitializeMelon` = `throw new NotImplementedException("这不现实")` 之后才 `PatchAll` + `LoadAndInit()`（**永远到不了**） | `SkipHarmonyPatchAll=true`：不挂三个补丁，改 `Pump`+`HookFree.Tick` 轮询；`DeferredInit=true` 延后到注册表就绪 | 时序整体重排：perk 页签登记、`ContentDisplayer` 协程都依赖 `OnceWarp`/`InitDone`；三个补丁对应的注入点改由轮询驱动 |
| 13 | `LoadArchMod.cs:45` | `AccessTools.AllTypes()` | `TypesInGameAssembly()`（只 `Assembly-CSharp`） | 修 Mono 断言 abort；副作用是**拿不到别的程序集里的类型**（本 mod 用不到） |
| 14 | `LoadArchMod.cs:29` | `Path.GetFileNameWithoutExtension`（真机不认 `\`） | `CleanName()` 统一取干净名（精灵名/obj_name/CardName） | 修字典键；但**唯独 GameSourceModify 不用它**（见 #10） |
| 15 | `MainGenTools.cs:357-377` | `Array.CreateInstance(Il2CppType.Of<T>(), n)` + 托管索引器 `SetValue` | `il2cpp_array_new(字段自身类型)` + 手算元素基址 `+0x20/+0x10` 直写槽位 | 修真机 SIGSEGV；代价是**绕过了类型检查**，写错元素类型不会报错 |
| 16 | `WarpFunc.cs:183-188` | `catch (Exception) { }` 完全静默 | 空 catch 保留 + 计数/取样（`StatEx/StatSkipSample`，`DumpSamples()`） | 只增可观测性；**失败仍然是静默的**（不写字段、不抛） |
| 17 | `MainGenTools.cs:299-322` | `SetLiNoWarpper` 造对象后不写回 | 补写回 | 同上 |
| 18 | `LoadResources.cs:236-238` 等 | `CharacterPerk` 只入 `WaitForAddPerkGroup` | 额外收集 `MiniLoader.ModPerks` + `Diag.RegisterModPerksIntoTabGroups()` + 周期维护 | 本版本 `PerkTabGroup.ContainedPerks` 才是显示源，属**新增修法**，不是破坏 |

### 5.1 关于 #1/#2 的因果链（为什么必须成对）

克隆 = 浅拷贝 ⇒ 若不先 `DeepDetach`，`JsonUtility.FromJsonOverwrite` 和 `FillDropsList()` 会写进
**游戏自带资产**的共享容器 → 20:35 用户实测的「开局选药物没给东西 / 探索没掉落 / 改天气无效」。
反过来，`DeepDetach` 一旦把 **List 清空**，就要求该字段必须能被 JSON 重建；
`DismantleActions` 恰好可以（内联数组），但**只有 WarpData 的字段就不行**。
这正是「21:03 把数组改成保长度」的动因。

### 5.2 关于 #3/#4 的反复

- 20:35–20:43：停用（原因：共享实例被写坏）。
- 21:03:09：**重新启用**（原因：`DeepDetach` 已经给了私有副本，写入只落自己那份，
  而"事件选项能否产出"恰恰依赖嵌套下潜 —— 见 `WarpFunc.cs:111-116` 的注释）。
- 【实测】21:08 轮启用后：`[INVARIANT] warp 后 游戏对象（尺寸/内容/丢失）: 尺寸变化=0 内容变化=0 丢失=0 / 共 76 ✓`
  → 重新启用**没有**污染游戏资产；而 `写入` 从 567/8273 涨到 **95844**。
- 但 `[EFFECT2]` 证明**产出链仍未通**（`DroppedCard=<null>`），原因不在 #3/#4，而在 #5/#6。

### 5.3 关于 #9（按名注册丢失）——目前最容易被忽视的一环

`CardTags`/`ActionTags`/`ActionSounds`/`WhenCreatedSounds` 这些字段在 mod JSON 里
**全部用名字**（`"tag_FeedRich"`、`"Eating"`、`"ExploreBushes"`），而不是 GUID。
原版靠 `FindObjectsOfType` 把这些**普通 ScriptableObject 按 `.name`** 注册进 `ItemDictionary(typeof(T))`；
本机这条链被裁，于是只剩 GUID 一条路，**名字全部查不到**。
【实测】`CardTags 请求=3 解析到=0`、`ActionSounds 请求=1 解析到=0`、`WhenCreatedSounds 请求=1 解析到=0`。
后果：
- 卡的标签为空 → `MatchAndWarpperAllEditorGameSrouce` 的标签索引空转、依赖标签的判定失效；
- 动作音效为空（听觉层面）；
- 依赖 `CardTags` 的 `ItemCardDataCardTabGpGroup` 等分类逻辑退化。

**修法方向**（供 Lead 决策，不是本文的实现）：导入注册表时**同时按「原生类名 + 对象名」建一份名字索引**，
或把 `Object.FindObjectsOfType` 换成「遍历 `Resources` 里能拿到的东西 + 名字索引」的等价路径。

### 5.4 关于 #11（`AllData.Add` 跳过）

它被跳过之后，mod 卡仍进 `AllGUIDDict` / `ItemDictionary` / 步骤 8 的 `Init()`。
风险面：游戏若通过 `GameLoad.Instance.DataBase.AllData` 做**事件抽取/掉落结算/存档序列化**，
mod 卡就不在候选集里。当前没有证据说明它是本次症状的直接原因（E2/二分 2a 只是"保住基线"），
但它和「玩家点选项后游戏能不能找到那张要产出的卡」是**同一个问题域**，建议在 §4.4 修完后一并复测。

### 5.5 `GameSourceModify` 的完整失效链（63 条）

```
arch 里存 Windows 路径 ds_save\Windy\GameSourceModify\weather\6e196596….json
  → LoadArchMod.cs:566  Path.GetFileNameWithoutExtension 在 Android 上不认 '\'
  → rawKey = 整条路径
  → WarpperAllEditorGameSrouces（LoadResources.cs:387） AllGUIDDict.TryGetValue(rawKey) → miss
  → continue   （且 LoadArchMod.cs:567 的只读探针也证实：解析到 0 条）
```
作者把它们当成"改原版内容"的唯一手段（天气、树、战斗、环境），全部失效。

---

## 6. 结论：要让那张事件卡的三个选项真的给东西，哪些环节必须成功

### 6.1 依赖清单（按必须成立的顺序）

| # | 环节 | 代码位置 | 依赖 | 当前状态 |
|---|---|---|---|---|
| 1 | 游戏注册表就绪 + 模板表就绪 | `HookFree.cs:67-77`、`RealShims.cs:130` | 每帧泵 + `AllUniqueObjects` 非空 | ✅ 实测通过 |
| 2 | 造出对象（模板克隆） | `LoadArchMod.cs:536/595` → `RealShims.CreateLike:203` | 模板表里有该类型；`Internal_CloneSingle` 已注册 | ✅（`[SHIM] CreateLike 克隆成功`） |
| 3 | 断开与游戏资产的共享 | `Diag.cs:709/740` | 字段真实类型（#6 的修复） | ✅（`[INVARIANT] 变化=0`） |
| 4 | JSON 灌进字段（含 `DismantleActions`/`ProducedCards`/`Quantity`） | `LoadArchMod.cs:547/602` | `MapperObject.ToJson()` + Unity JsonUtility | ✅ 实测 `条数=3`、`ProducedCards=1`、`Quantity=(1,1)` |
| 5 | 卡的注册与可见（GUID + perk 挂载 + 页签） | `MiniLoader.RegObj:69`、`LoadPatchMain.cs:377`、`:389`、`:374` | `AllGUIDDict`、`AddedCardsWarpType=3` 解析、`PerkGroup`/`PerkTabGroup` | ✅ 实测 `AddedCards=[1 项]`、页签 +6 |
| 6 | **warp 能下潜到 `DroppedCards[i]` 并执行 `DroppedCardWarpType/WarpData`** | `WarpFunc.cs:128-194` + `MainGen.GetOrGen:46` | ①`DismantleCardAction` 的 gen 表要能看见继承字段 `ProducedCards`；②`DroppedCards:Il2CppReferenceArray` 也要能进 | ❌ **阻塞点 A + B** |
| 7 | `TryResolveRef<CardData>` 能查到目标卡 | `MainGenTools.cs:123` | mod 卡走 `AllItemDictionary[CardData][GUID]`；游戏卡走 `AllUniqueObjects[GUID] + TryCast` | ✅（前提是 #6 能走到它） |
| 8 | `CardData.FillDropsList()` 汇总 `AllDrops` | `LoadResources.cs:206` | #6 成功（否则汇总出的是空 `DroppedCard` 的集合） | ⚠️ 汇总了但内容为空 |
| 9 | 天气链额外：GameSourceModify 生效 | `LoadArchMod.cs:559` + `LoadResources.cs:387` | `CleanName` 后能查到 GUID + 嵌套 warp + ADD/MODIFY 语义 | ❌ inert |
| 10 | 文本/图标 | `LoadArchMod.cs:334/422`、`LocalizedString.DefaultText` | Sprite 名索引 ✓；CSV ✓（但 `DefaultText` 自带中文，不强依赖） | ✅ |

**一句话**：**只差第 6 环（以及天气还差第 9 环）。**
证据是那条最直白的日志：`[EFFECT2] … DroppedCard=<null> Quantity=(1,1)`。

### 6.2 最小改动建议（两处，都在 warp 侧）

1. **让 gen 表包含继承字段**：`MainGen.GetOrGen` 除了自身，还要沿 `BaseType` 链收集
   `NativeFieldInfoPtr_*`（或对属性查找做同样的事）。这直接解开阻塞点 A，
   并顺手修掉 `CharacterPerk.ActionObjectives` 等一批 `字段未生成`。
2. **统一容器判定**：把 `WarpFunc` 内联数组分支的 `fldType.IsArray` 换成与
   `MainGenTools.CommonSet` 一致的 `Il2CppArrayBase<>` 判定（含 `IsIl2CppArrayType` 的基类遍历写法，`Diag.cs:809` 已有现成实现）。这解开阻塞点 B。

改完后用同一条 `[EFFECT2]` 判据验收：三个选项 + `Event_Gift_Weather/Drug` 的 6 个选项都应打印
`DroppedCard=<名字>/0x…` 而不是 `<null>`；且 `[INVARIANT]` 必须继续保持 0 变化。

### 6.3 天气的验收要更严一格

天气选项多两层前置：① `DroppedCard` 指向的是**游戏自带天气卡**，必须走 `TryResolveRef` 路径 ②；
② 作者真正想让天气「延续/触发」的那段逻辑写在 **GameSourceModify** 里（`SpoilageTime.OnZero.ProducedCards += Storm`），
需要先把 `CleanName` 补到 GameSourceModify 分支（`LoadArchMod.cs:559-581`），
再确认嵌套 `MODIFY`/`ADD` 语义正确（`warpType==MODIFY` 会**清空**游戏卡原有集合 —— 这正是当前被当成"污染游戏数据"而关掉的路径，
放开前必须先确认 `DeepDetach`/GameSourceModify 的写入目标是**游戏资产本体**，此时没有任何私有副本可保护 → 这是**唯一必须谨慎设计的一环**）。

---

## 7. 附录

### 7.1 关键代码位置速查

| 概念 | 位置 |
|---|---|
| 区块分发 | `LoadUtil/LoadArchMod.cs:LoadMod`(L201) / `LoadModArchBLK`(L243) |
| JSON 清单解析三分类 | `LoadUtil/LoadArchMod.cs:LoadJsonsBLK_V3`(L480-642) |
| 类型普查（只 Assembly-CSharp） | `LoadUtil/LoadArchMod.cs:TypesInGameAssembly`(L45) |
| 造对象（shim 克隆） | `LoadUtil/LoadArchMod.cs:CreateScriptableObjectViaShim`(L223) → `CstiICallFix/RealShims.cs:CreateLike`(L203) |
| 模板表 | `CstiICallFix/RealShims.cs:PrimeTemplates`(L130) |
| 去共享 | `Diag.cs:NeutralizeClone`(L709) / `DeepDetach`(L740) |
| JSON→字段 | `LoadUtil/LoadArchMod.cs:547`、`602`（`JsonUtility.FromJsonOverwrite`） |
| 注册 | `MiniLoader.cs:RegObj`(L69) / `ItemDictionary`(L84) |
| warp 入口 | `WarpperClassGen/WarpFunc.cs:JsonCommonWarpper`(L55) |
| warp 赋值/建对象 | `WarpperClassGen/MainGenTools.cs:CommonSet`(L161)、`SetByWarpper`(L239)、`SetLiByWarpper`(L263)、`SetLiNoWarpper`(L299)、`SetArrByWarpper`(L324)、`SetArrNoWarpper`(L382) |
| 引用解析 | `WarpperClassGen/MainGenTools.cs:TryResolveRef`(L123) |
| 字段表 | `WarpperClassGen/MainGen.cs:GetOrGen`(L46)、`WarpType`(L14) |
| 后处理 | `LoadUtil/LoadResources.cs:26 / 134 / 300 / 376` |
| 页签/蓝图/统计注入 | `Patchers/LoadPatchMain.cs:389(id=7) / 571 / 600 / 548` |
| 免 hook 驱动 | `HookFree.cs:Tick`(L59)、`Pump.cs` |

### 7.2 证据索引

| 结论 | 证据 |
|---|---|
| 区块顺序 / 各分类条目数 / **LuaBLK=0** | `tools/_run/archdump.txt` |
| `CardDrop`/`CardsDropCollection` 字段与类型 | `tools/_run/live.log` 20:56:46.170 / 46.175（`[E2SIG]`） |
| 继承字段不在 gen 表 | `live.log` 20:57:00.895-897（`[WARP跳过] 字段未生成: CharacterPerk.*`）+ `latest.log` 21:03:55.739（`[GEN] CharacterPerk 生成字段(19)`） |
| `DismantleCardAction` 自有 6 字段 | `latest.log` 21:04:12.978（`[EFFECT] 元素类型=DismantleCardAction 字段(6)`） |
| 三个选项的产出集合非空、`DroppedCard` 为空 | `latest.log` 21:08:13.494-13.496（`[EFFECT2]`） |
| `AllDrops` 已汇总 3 个集合 | `latest.log` 21:08:13.502（`[EVENTCARD] AllDrops=[3 项]`） |
| 按名引用解析失败 | `latest.log` 21:04:12.828（`[ARR] CardData.CardTags 请求=3 解析到=0`） |
| GSM 全 inert | `latest.log` 21:08:12.694（`[GSM] 本会改到游戏对象=0 保持inert=63`） |
| 游戏资产未被污染 | `latest.log` 21:08:23.498（`[INVARIANT] 尺寸变化=0 内容变化=0 丢失=0 / 共 76`） |
| `AllData.Add` 被跳过 | `latest.log`（`[DB] 二分 2a：跳过 GameLoad.Instance.DataBase.AllData.Add(card)`） |
| `Il2CppReferenceArray<T>` 是类 | `il2cppinterop-fork/Il2CppInterop.Runtime/InteropTypes/Arrays/Il2CppReferenceArray.cs:7` |
| 作者侧字段语义 | `CSTI-ModEditor-master/CSTI-JsonData/Notes-En/{CardAction,DismantleCardAction,CardsDropCollection,CardDrop}.txt` |
| 类型自有/继承字段划分 | `…/UniqueIDScriptableBaseJsonData/CharacterPerk.json`(35) vs `CompletableObject.json`(16)；`…/ScriptableObjectTypeJsonData/CardAction.json`(40, 含 `ProducedCards`) vs `DismantleCardAction.json`(45) |
| 天气卡 GUID→名字 | `…/UniqueIDScriptableGUID/CardData/Weather.json`、`Item.json` |
| 原版对照 | `git show NoReflection:CSTI-MiniLoader/**`（master 无此目录） |

### 7.3 尚未验证（在动代码前应补测）

1. `DismantleActions` 到底是 `List<DismantleCardAction>` 还是 `Il2CppReferenceArray<DismantleCardAction>`
   （决定阻塞点 A 的表述；本文按「List」解释 `[SET] CardAction.ActionSounds` 的出现，但**两种假设都与现有日志相容**，
   一行探针即可定论）。
2. 「掉落天气卡 = 改变天气」的游戏侧语义（本文标为【推断】）。
3. `SkipGameDataBaseAdd=true` 对**事件抽取**是否有影响（二分 2a 只验了基线不崩）。
4. `CardData.FillDropsList()` 的确切汇总规则（是汇总 `DismantleActions[*].ProducedCards` 还是别的）——
   目前只有「`AllDrops` 从 0 项变 3 项」这一条观测。
5. 本文件写作期间 `WarpperClassGen/WarpFunc.cs`、`Diag.cs`、`LoadUtil/LoadResources.cs` 正在被其他协作者改动，
   引用行号以 §0 表格的快照时间为准；若文件继续变化，请以「方法名」定位而不是行号。
