# CSTI mod 音效加载 —— 方案丙（APK 资产级替换）**已真机验证可行**

> 状态：**已真机验证**（2026-10-02 21:53，用户实测）
> 验证方式：把 `ButtonClick` 的音频数据换成一段 **74 ms 的 440 Hz 正弦**，
> 用户点任意按钮 → **听到了蜂鸣** → **FMOD 接受我们手工构造的 FSB5（`mode=2` PCM16）**。
> 也就是说：**"让 mod 播放自己的原声"这条路走通了。**

---

## 1. 结论与定位

| | |
|---|---|
| **能做** | 让游戏里某个 AudioClip **播放 mod 提供的任意音频**（真实原声，不是替代音效） |
| **不能做** | 在**运行期**新建 AudioClip 或把 PCM 喂进 AudioSource（16 条 AudioClip 创建 ICall + AudioSampleProvider 全线未注册） |
| **因此** | 这是**打包期（build-time）能力**，不是运行期能力 —— 见 §6，它决定用户怎么用 |

### 权威判据（全组通用，务必先记这条）
> **一个 ICall 能不能用，只看：`libil2cpp.so` 里有没有它的 wrapper 名字字符串。**
> 有 → 一定能用；没有 → **一定失败**（哪怕 `libunity.so` 里有实现、哪怕 interop 元数据里类还在）。
>
> 反例（都验证过）：`AudioClip::Construct_Internal`/`SetData`/`CreateUserSound`、
> `UnityEngine.Experimental.Audio.AudioSampleProvider::*`、
> `Resources::FindObjectsOfTypeAll`、`AudioSource::PlayOneShotHelper`、`GUIClip::GetMatrix_Injected`。
>
> 有效手段：① 真机 `il2cpp_resolve_icall`；② **PC 侧在 `gamedata\lib\arm64-v8a\libil2cpp.so` 里搜名字字符串**
> （离线即可预判，两者实测 1:1 吻合）。
> **不要**用 "名字在 `icall_inventory.txt` 里且不在 `icall_missing_real.txt` 里" 来推"已注册" ——
> 这两份文件不是同一次扫描的产物，推不出任何结论（此坑已踩过）。

---

## 2. 已验证的做法

### 2.1 目标资产结构（UnityPy 实测）
- 音频全部在 `assets/bin/Data/sharedassetsN.resource`（`N=0`: 43.5 MB / `N=2`: 4.3 MB）。
- **共 134 个 AudioClip**：`sharedassets0.assets` 127 个 + `sharedassets2.assets` 7 个。
- 全部用 `StreamedResource` 引用数据：`m_Resource = {m_Source, m_Offset, m_Size}`，**没有内联 `m_AudioData`**。
- `sharedassets0.resource` 被 127 个 blob **精确铺满、16 字节对齐、零空洞**。
- 容器格式 **FSB5（FMOD Sound Bank v1）**：

| 数量 | `m_LoadType` | `m_CompressionFormat` | FSB5 `mode` | `sampleHeadersSize` |
|---|---|---|---|---|
| 111 | 1 CompressedInMemory | 2 | **7 = IMA ADPCM** | 36 |
| 16 | 0 DecompressOnLoad | 1 | 15 = VORBIS | 68–484 |
| 7（sharedassets2）| 1 | 2 | 7 = IMA ADPCM | 36 |

### 2.2 FSB5 布局（逐字节反推，127 个 blob 全部成立）
```
0x00 "FSB5"   0x04 version=1   0x08 numSamples=1   0x0C sampleHeadersSize
0x10 nameTableSize=0           0x14 dataSize       0x18 mode
0x1C..0x37 少量字段 + 哈希      0x38 uint32 == 样本头首 4 字节（冗余摘要）
0x3C 样本头（IMA=36 B，只有前 8 B 非零，其余 0 填充）
数据起始 = 0x3C + sampleHeadersSize     （IMA 即 0x60）
恒等式：60 + sampleHeadersSize + nameTableSize + dataSize == blob 总长
```

### 2.3 本次验证用的做法（**最小改动原则**）
1. **复用目标 clip 原有的 96 字节前缀逐字节照抄**，只改 `mode`(0x18: 7→2) 与 `dataSize`(0x14) ——
   采样几何（声道/频率/样本头）与原件完全一致，**唯一变量是编码方式**。
2. 负载 = 重采样到目标频率/声道数的 int16 交织 PCM，不足部分补**真 0 静音**，
   输出 blob **严格等于**原 blob 字节数。
3. `.resource` **等长原地替换** → 文件长度不变 → **其余 126 个 clip 的 offset 全部无需重算**。
4. `.assets` 只补 **4 字节/个 `m_Length`**（必须写**实际写入的 PCM 时长**，不是源 WAV 时长 —— 音频可能被截断）。
5. 重新按 1 MB 分片回 `.assets.split0..111`（本次因等长，分片数不变）。
6. STORED 重打包（`repack_stored.py`）→ `zipalign` → `apksigner`。

### 2.4 固定参数（**已验证可用，照抄即可**）
```
zipalign      : D:\devtools\HBuilderX\plugins\app-safe-pack\zipalign\zipalign.exe
                zipalign -f 16384 <in.apk> <out.apk>          # 16 KB 页设备必须；-p 老版本不支持
apksigner     : D:\devtools\HBuilderX\plugins\app-safe-pack\apksigner.jar   （用 java -jar 跑）
java          : D:\devtools\Java\jdk1.8.0_281\bin\java.exe   （jdk-11.0.13 也可）
keystore      : C:\Users\HelloAlone\AppData\Local\Temp\csti-diag\sign\test.jks
                口令 = 123456   （不是 android）   别名 = t
签名命令      : java -jar apksigner.jar sign --ks <ks> --ks-key-alias t \
                    --ks-pass pass:123456 --key-pass pass:123456 --out <out.apk> <aligned.apk>
验签命令      : java -jar apksigner.jar verify --print-certs <apk>
本机证书       : CN=CSTI Test, O=dsh
                SHA-256 79601b93a8cf80cbe751e173d7ed9e4f305a338fdef8782229ca2ef73315f604
```
⚠ **顺序不能反**：先 `zipalign` 再 `apksigner`。签名之后再动包会破坏对齐；在 16 KB 页设备上
`extractNativeLibs=false` 的 `.so` 会在启动 10~20 s 后 SIGSEGV（见 `mods-06\HarmonyProbe\RESULT.md` §4.2）。

### 2.5 ★ 装前必做的一道保险：**拉装机 APK 验签对比**
```bat
adb shell pm path com.winterspringgames.survivaljourney      :: 拿 base.apk 路径
adb pull <base.apk> logs\installed_base.apk
java -jar apksigner.jar verify --print-certs logs\installed_base.apk
```
**新包与装机包的证书 SHA-256 必须一致**，否则 `install -r` 会报 `INSTALL_FAILED_UPDATE_INCOMPATIBLE`，
而"卸载重装"= **丢存档**。本次两者 SHA-256 完全一致，所以 `install -r` 一次成功。
这道保险成本 ~10 秒，**建议固化成流程**；同时顺手把装机 APK 留一份做回滚包。

---

## 3. 已验证 / 未验证 边界（**务必按这个口径引用**）

| | 内容 |
|---|---|
| ✅ **已证** | **非压缩 PCM（FSB5 `mode=2`）可被 FMOD 接受并正常出声**；手工构造的 FSB5 头与样本头被接受；等长原地替换不破坏容器与邻居；`.resource` 长度不变时无需重算 offset；`m_Length` 单独补丁有效 |
| ✅ **已证** | 替换后的包**能正常启动**（MelonLoader 正常加载、跑到 `UnityPlayerActivity` 60 fps、无 SIGSEGV/SIGABRT）；16 KB 对齐 + 签名链路正确 |
| ⚠️ **未证** | **长音频**：本次只验证了 **74 ms**（`ButtonClick` 容量只有 0.0743 s）。`ChoppingTrunk ← windy.wav`（0.5486 s）**已经打进同一个 APK 但用户只点了按钮，还没砍树** → **0.55 s 是否正常尚未实测** |
| ⚠️ **未证** | **IMA ADPCM 原格式槽位**：本次是把 `mode 7 → 2` 改成 PCM16；**直接在 111 个 IMA 槽位里填 IMA 数据**未验证（那是路线 B 的备选，需要写编码器）|
| ⚠️ **未证** | **Vorbis 槽位**（16 个，`mode=15`）完全没试 |
| ⚠️ **未证** | 多 mod、多音效、同一 APK 里同时替换 10+ 个 clip 的实际表现 |
| ⚠️ **未证** | 长音频的**播放时长/循环行为**是否符合预期（`m_Length` 已改，但循环类 clip 的 `m_LoadType=0` 行为未验证）|

> 需要一次设备窗口把 **`ChoppingTrunk`（0.5486 s，砍棕榈树）** 验掉，才能把"长音频"从⚠️升到✅。

---

## 4. 通用化：两条路怎么选

### 背景：PCM16 的硬约束
PCM16 字节数 ≈ **IMA ADPCM 的 4 倍、Vorbis 的 8–10 倍**。
因此"**等长 + 等时长**"在 PCM16 下**数学上不可能**：同长度的 blob 装不下同时长的 PCM。
→ 每条被替换的 clip，其**可用时长** = `(blob 字节数 - 96) / (2 × 声道数 × 采样率)`。
例：`ButtonClick` 7232 B / 1ch / 48k → **0.0743 s**；`ChoppingTrunk` 115456 B / 2ch / 48k → **0.6008 s**。

---

### 槽位挑选依据：已离线扫出 **每个 clip 被谁引用**（`out\slot_table.json` / `clip_refs.json`）
> 为什么要"裸字节扫"：MonoBehaviour 的自定义字段**不在** SerializedFile 内嵌的 type tree 里
> （字段定义在程序集里），所以 `read_typetree()` 对 MonoBehaviour 只能读出 `m_GameObject/m_Enabled/m_Script/m_Name`，
> **看不到它引用了哪些 AudioClip** —— 这就是"引用遍历抓到 0 条"的原因。
>
> 做法：PPtr 在序列化文件里是 `int32 m_FileID + int64 m_PathID`（12 字节小端），
> 直接在各对象的字节区间里搜这个 12 字节模式，并用 `externals` 解析跨文件 FileID。
> **必须只扫 MonoBehaviour**：游戏自己的数据（含脚本型 ScriptableObject）在文件里都是 classID 114；
> 而 `Texture2D/Mesh/Shader/Font` 是裸字节块，搜出来全是巧合
> （不过滤时 `Drink` 会被"543 个 Texture2D 引用"这种假数据淹没）。
>
> **判据**：`引用数 = 0` → **游戏里没有任何对象引用它 → 永远不可能被播放 → 牺牲它绝不影响任何游戏音效**。

**实测结果（135 个 AudioClip，跨 3 个 `.assets`；扫描 7447 个 MonoBehaviour，命中 2409 条 PPtr）**

| 档位 | 数量 | 说明 |
|---|---|---|
| **引用数 = 0** | **6** | **最安全**，见下表 |
| 引用数 1 | 36 | 次安全，影响面 = 那 1 个资产 |
| 引用数 2–3 | 23 | 谨慎 |
| 引用数 ≥ 30 | **21** | **绝对不要动**（`SticksAppear` 256、`LargeObject` 202、`Eating` 167、`ButtonClick` 60 …）|

**引用数 = 0 的 6 个槽位（可作 mod 音效专属容器）**
| clip | 文件 | 声道/频率 | 原 blob | 原时长 | 等长替换只能放 |
|---|---|---|---|---|---|
| `HeartbeatLoop` | sharedassets2 | 2ch/48k | 740928 | 13.72 s | 3.000 s |
| `Test2_Note4_Looping` | sharedassets2 | 2ch/44.1k | 728608 | 14.68 s | 4.130 s |
| `Test2_Note3_Looping` | sharedassets2 | 2ch/44.1k | 720960 | 14.53 s | 4.087 s |
| `Test2_Chord1_Looping` | sharedassets2 | 2ch/44.1k | 717088 | 14.45 s | 4.065 s |
| `Test2_Note1_Looping` | sharedassets2 | 2ch/44.1k | 714848 | 14.41 s | 4.052 s |
| `Typing` | sharedassets1 | 1ch/44.1k | 7232 | 0.29 s | 0.081 s |

→ 有了**路 A+**，"等长替换只能放多少秒"这一列**不再是限制**；这 6 个槽位的真正价值是
**牺牲它们不可能删掉任何玩家能听到的游戏音效**。

**引用数 = 1 的 36 个（次选，附引用者便于判断影响面）** —— 例：
`BabyCryLoop ← Baby`、`SadTune ← Event_FluteTunes1`、`BeeLoop ← BeeSkep`、`DogBite ← DogFriend`、
`CaveTune ← Event_FluteTunes3`、`MuffledJungleLoop ← Env_CrashedPlane`、`DarkCaveLoop ← Env_CaveDark`、
`DynamiteFuse ← DynamiteOff`、`AsteroidImpact ← Event_AlienCrater` …（完整见 `out\slot_recommendation.txt`）

---

### 路 A：牺牲槽位映射（等长原地替换）—— **最小改动版（已真机验证）**
从 135 个 clip 里挑一批"低价值/罕见"的槽位当 mod 音效容器，逐个把 WAV 编码进去。

- **可行性**：✅ **已真机验证**（就是本次通过的那条机制）。
- **关键难点**：~~判断哪些槽位"低价值"~~ → **已解决**（见上面的引用数扫描）。
- **工作量**：小–中（工具已就绪；需补"槽位挑选 + 清单/manifest + 批量编码"）。
- **风险**：**低**（同已证机制）。主要风险是**挑错槽位 → 玩家听到某处游戏音效被换掉**，属产品风险非技术风险。
- **代价**：**会覆盖游戏原声**（该槽位原声音效消失）；**数量/时长受限**（受槽位大小约束）。

### ★★ 路 A+：**追加 blob + 重指向** —— **推荐产品化路线**
不覆盖原数据，而是：
1. 把新音频的 FSB5 blob **追加**到 `.resource` 末尾（16 字节对齐）；
2. 把某个槽位 clip 的 `m_Resource.m_Offset/m_Size` **重指向**这个新 blob；
3. 补 `m_Length`（必要时连 `m_Channels`/`m_Frequency`/`m_CompressionFormat` 一起补，反正都在同一个对象里）。

- **可行性**：✅ **机制与已验证的路 A 同源**（同为"改一个已有对象 + 改资源引用"），
  且 **PC 侧 9 项自证已全部通过**（见 §3 与 `apk_test_aplus\APLUS_REPORT.txt`）。
- **比 A 强在哪**：
  - **追加不会移动任何已有数据** → 现有 offset 依然全部有效，**不需要重算/重排**；
  - **时长不受槽位大小限制**（需要多长就追加多长）—— 直接回答"mod 声音长度不一定有等长资产"这个质疑；
  - **原始音频数据仍在文件里**（只是那个 clip 不再指向它）→ **可一条命令回滚**；
  - `.assets` 仍是**等长就地修改**（16 + 4 = 20 字节）→ **不需要重分片**。
- **关键难点**：~~槽位挑选~~ → **已解决**（引用数扫描）；其余与 A 相同。
- **工作量**：小–中（`patch_aplus.py` 已可用，见下）。
- **风险**：**低**。唯一语义代价和 A 一样：**那个槽位的游戏原声被顶掉了**（但原数据还在，可回滚）。

#### 工具：`temp\audiohunt\patch_aplus.py`
```
python patch_aplus.py --clip <槽位名> --wav <wav> [--codec pcm16] [--dest apk_test_aplus]
```
产出：`sharedassetsN.resource`（已追加）、`sharedassetsN.assets` + `.split0..N`（已重指向）、
**`ROLLBACK.json`**（原 `offset/size/m_Length` + 新值 + 字段在文件里的绝对偏移）、`APLUS_REPORT.txt/json`。

自证 9 项（全部跑在落盘后的真实文件上）：
```
[PASS] ① .assets 差异字节数符合预期（= m_Resource 16 B + m_Length 4 B）
[PASS] ② .resource 新长度 = 原长 + 对齐填充 + blob
[PASS] ③ 追加区 16 字节对齐
[PASS] ④ 原文件区间逐字节未变（前 len(原) 字节与原件完全一致）
[PASS] ⑤ 追加区回读为 FSB5(PCM16)（恒等式 60+shSize+nameTab+dataSize == blob 总长）
[PASS] ⑥ **原槽位数据仍完好**（A+ 的关键优势）
[PASS] ⑦ 重指向生效（m_Offset/m_Size/m_Length 三处都核对）
[PASS] ⑧ 其余 clip 的 offset 全部未变
[PASS] ⑨ .assets 长度不变 / 分片数不变
```
**实测样例**：`HeartbeatLoop`（引用数 0，sharedassets2，2ch/48k，原 13.72 s）← `windy.wav` 完整 0.5486 s
→ 追加 105420 B @offset 4373024，`m_Offset 2881504→4373024`、`m_Size 740928→105420`、`m_Length 13.7187→0.5486`；
与 WAV 真实时长 0.548571 s 的偏差 **8.9 µs**（float32 精度）。
超长文件改名 105324 B（PCM16）。

> **关于 `--codec ima`**：**当前未实现，且故意不实现**。路 A+ 已经消除了容量瓶颈，
> IMA 没有容量收益；而 FSB5 的 IMA 样本头语义（字段 4..7）与 IMA 块布局**我们没有逐字节反推过**，
> 盲写只会产出未经验证的代码。真要做请先补一次布局反推 + 真机验证。
> 传 `--codec ima` 会明确报错说明这一点，不会静默出错。

### 路 B：真正新增 AudioClip 对象 —— ✅ **已解决并验证（见 §8）**
> **本节下面的"不可解"判断已作废，保留以免重复踩坑。**
> 当时的结论是"运行期无法发现无人引用的新资产"——**这个观察是对的，但结论下错了**：
> 我们的按名解析**不依赖运行期枚举**，靠的是"**游戏对象字段里引用到的 clip**"建索引。
> 所以只要新 clip **被引用一次**，它就会进名字索引 —— 而"被引用一次"只需
> **往某个已有 MonoBehaviour 的音效数组里追加一项 PPtr**（不需要 type tree，用裸字节定位即可，见 §9）。
> **实测结果：§8，PC 侧 5 步全通，原 127 个 clip offset 变化 0 个。**

<details><summary>原（已作废的）"不可解"分析</summary>
往 `.resource` 追加新 blob **+ 在 `.assets` 里新增 AudioClip 对象**，不碰任何现有 clip。

- **可行性**：⚠️ 中低。**技术上可做，但有一个不是"offset"的硬障碍。**
- **关键难点（按严重程度排序）**：
  1. **★ 运行期"发现"问题（真正的拦路虎，目前判为不可解）**：
     新增了一个 AudioClip 对象，**谁来把它交给 MiniLoader？** 这个裁剪版引擎里：
     `Resources::FindObjectsOfTypeAll` **未注册**、`Object::FindObjectsOfType` **只扫活动场景**（资产不在场景里，实测返回 0 条）、
     `Resources::Load` 只搜 `resources.assets`（**本游戏没有这个文件**）、AssetBundle 模块**整条被裁**。
     → **没有任何已注册的 ICall 能在运行期枚举/加载一个"没人引用的新资产"**。
     绕法只有：把新 clip 挂到某个**已有游戏对象字段**上（改 MonoBehaviour 的 PPtr），
     这又要求拿到 MonoBehaviour 的字段布局（其 type tree **不内嵌**，实测 `read_typetree` 读不出自定义字段）——
     **难度陡增，且脆弱**。
  2. `.assets` 里插入对象要改对象表（在数据段**之前**），**其后所有对象的 `byteStart` 都要重写**；
     还要管 `m_Name` 字符串、对象表项、文件头 `fileSize`。
  3. `.assets` 变长 → **112 个分片要重分**（可做，但整条链都要重跑）。
  4. 新对象要能被 Unity 加载：SerializedFile 的对象是**按引用惰性物化**的，没人引用就不会被实例化 ——
     与难点 1 是同一个问题的两面。
- **工作量**：**大**（序列化文件写回 + 对象表重写 + PPtr 锚定 + 全链重跑）。
- **风险**：**高**（写坏 `.assets` 会整包起不来；PPtr 改错会崩；且难点 1 大概率绕不过去）。
- **收益**：**不覆盖任何游戏原声、容量不受限** —— 这是它唯一但很实在的优势。
- **结论**：**仅当"绝对不能覆盖任何游戏原声"成为硬需求时再研究**，且必须先解决难点 1。

</details>

### 对比与推荐

| | 路 A（等长替换） | **★ 路 A+（追加+重指向）** | 路 B（新增对象） |
|---|---|---|---|
| 可行性 | ✅ 已真机验证 | ✅ 高（同源机制） | ⚠️ 中低（被"运行期发现"卡住） |
| 覆盖游戏原声 | 是 | 是（但原数据保留可回滚） | **否** |
| 容量/时长限制 | 受槽位大小限制 | **不受限** | 不受限 |
| 重算 offset | 不需要 | **不需要** | 需要（对象表全部重写） |
| 重分片 `.assets` | 不需要（等长） | **不需要**（等长就地改） | 需要 |
| 工作量 | 小–中 | 小–中 | 大 |
| 风险 | 低 | 低 | 高 |

> **推荐：路 A+ 先做**（在已证机制上稍加扩展就能拿到"容量不受限 + 可回滚"），
> **路 B 只在"绝对不能覆盖任何游戏原声"成为硬需求时再考虑**，且要先解决"运行期发现"这一关。

---

## 5. 两条路共同的运行期配套（都还没做）
无论 A/A+/B，游戏里那个 AudioClip **要在运行期被 MiniLoader 拿到并按 mod 的音效名注册**：
- 已注册可用的播放 API：`AudioSource::set_clip` + `Play()`（**注意 `PlayOneShotHelper` 实测 MISS，别用 `PlayOneShot`**）、
  `Stop`、`get/set_volume`、`SetPitch`、`GameObject::Internal_AddComponentWithType`。
- 槽位 clip 可以从游戏自己的数据里取（`SoundManager` 的命名字段、`CardData.*Sounds` 等），
  也可以走 MiniLoader 现有的 `ItemDictionary(typeof(AudioClip))` / `AllGUIDDict`。
- ⚠ 取游戏引用必须**等游戏初始化完成**（实测 t+20 s 时 `SoundManager.Instance` 仍是 `null`），
  且**不能依赖 `MelonMod.OnUpdate`**（该构建里它根本不触发；用后台线程或已有的泵）。
- **方案乙（映射到游戏已有 clip）与 A/A+ 是同一套运行期配套**，只是音频内容不同。

---

## 6. ★ 关键定位：**这是"打包期"能力，不是"运行期"能力**

这一点直接决定用户怎么用：

- **运行期什么都改不了**：不能加载新音频、不能新建 AudioClip、不能替换某个 clip 的数据。
  已注册的 ICall 只够"**播放已有的 clip**"。
- **所以每个音效都要走一遍**：
  `选槽位 → 编码成 FSB5 → 打补丁 → 重打包 APK → zipalign → 签名 → 重装`。
- **对用户的实际影响**：
  - **装 mod 音效 ≠ 放个文件进 Mods 目录**；需要**重新生成并安装一个 APK**（≈ 每次换音效一次重装）。
  - 好在**不需要卸载**（签名一致 + `install -r`），**存档不受影响**。
  - 一个 mod 包里的多个音效可以**一次性批量**打进同一个 APK（槽位够的话）。
  - 这也意味着**音效与 mod 的其余部分（JSON/图片/Lua，那些是运行期加载的）解耦**：
    音效要单独出"带音效的 APK"，其余内容仍走 Mods 目录。
- **若将来能接受"听不到原声"**，则方案乙（映射到游戏已有 clip）是**纯运行期**的，不用重打包 —— 这是它唯一的优势。

---

## 8. ✅ 路 B（新增 AudioClip 对象）**已验证**（2026-10-02，PC 侧 5 步全通）

`temp\audiohunt\add_clip2.py` 一次性完成：追加 blob → 新增 AudioClip 对象 → 锚定到 `BedRoll` 的音效数组 →
重分片 → 覆盖进 APK → STORED 复核 → zipalign → 签名。产物 `temp\audiohunt\apk_test\WindyCart.apk`。

| 判据 | 结果 |
|---|---|
| 对象总数 | 6857 → **6858**；AudioClip 127 → **128** |
| 新 clip | `m_Name='Cart'`，pid=6858，blob 合法 FSB5(PCM16)，`m_Length`=1.2867120（与 Cart.wav 真实时长偏差 4.6e-8） |
| **原 127 个 clip 的 offset/size** | **逐条不变，变化 0 个** |
| 锚点 `BedRoll` | 数组 count 2→3，末项=(0,6858)，前两项仍是 Sleep1/Sleep2；**除 count+1 与插入的 12 字节外其余字节逐字节不变**（16544→16556 B） |
| 文件 | `.resource` 43,577,312→43,804,384；`.assets` 116,827,648→116,827,768（112 分片，无损拼回） |
| APK | `.so` 7 个全 STORED、`resources.arsc` STORED、zipalign/verify 通过、**证书 SHA-256 与装机包一致** |

### 8.1 ★★ 六条 SerializedFile 改写通用经验（后续任何 `.assets` 改动都适用）

1. **`count` 在 `pathID` 前 8 字节，不是 4**。
   数组布局 = `[int32 count][ (int32 m_FileID + int64 m_PathID) × count ]`。
   所以对"pathID 所在位置 `hit`"，第 j 个元素的 `count` 在 `hit − j×12 − 8`。
   ⚠ 我第一版按 `hit − j×12 − 4` 算 → 把 `m_FileID`(0) 当成了 count → **得出"0 个数组"的假结论**。
   改正后结果立刻语义自洽（鼓声列表=`Practice1..5`、笛声=`FlutePractice1..3`、床=`Sleep1,Sleep2`），这就是它正确的证据。

2. **序列化文件头是"大端 4 个 u32 + 小端其余"**。
   `metadataSize / fileSize / version / dataOffset` **永远按大端读**（Unity 故意如此，便于工具判字节序）；
   `0x10` 的 `endianness` 字节（本文件 = 0）才决定后面各表的字节序。
   ⚠ 用小端读头会得到 `version=0x15000000` 这种垃圾值。

3. **UnityPy 的 `SerializedFile.save()` 是逐字节无损的**（实测：原样 `load` → `save()` 与原件完全相同）。
   而且它**遍历 `f.objects`、逐个 `write()` 并 `align_stream(8)`，再自己算 metadata/data 大小与 header** ——
   所以 **"新增对象"就退化成"往 `f.objects` 插一个 `ObjectReader` 副本"**，
   所有 `byteStart` / 对齐 / `dataOffset` / `fileSize` 自动处理。
   → **不需要**手写"特征串定位对象表 + 手算 +N 平移"那套（我原先的设计整段作废，风险大降）。

4. **必须显式同步 `obj.byte_size`**。
   `get_raw_data()` 是按 `self.byte_size` 从 reader 读的、**不看 `self.data`**；
   而 `write()` 用 `self.data` 写数据、对象表里的长度却取自 `self.byte_size`。
   → 只 `set_raw_data(new)` 而不 `obj.byte_size = len(new)`，**表里长度仍是旧值**，
   Unity 只会读旧长度 → **追加的字节读不到 → 锚定静默失效**。
   （这一条正是自证 ⑥b 抓出来的。）

5. **`struct.pack_into` 是覆写，不是插入**。
   要让对象变长必须切片拼接：`new = old[:ins] + bytes12 + old[ins:]`。

6. **改写后必须"从最终 APK 回读"再验一遍**（而不是只验中间文件）。
   本轮全部判据都跑在落盘后的真实文件 + 从 `WindyCart.apk` 内解出的 `.assets` 上。

### 8.2 ★ 扩展性估算 —— 回答用户"槽位不够"

**每个 mod 音效的资产开销**（实测 + 计算）：

| 组成 | 字节 |
|---|---|
| 新增 AudioClip 对象 | **≈ 88 B**（实测：模板 96 B − 名字对齐差 8；随名字长度在 88–120 B 间浮动） |
| 锚点数组新增一项 PPtr | **12 B** |
| FSB5 头 + 样本头 | **96 B**（60 + 36） |
| PCM16 音频数据 | `2 × 声道数 × 采样率 × 秒数` |
| **合计** | **≈ 196 B + PCM16 数据** |

**PCM16 数据量**：单声道 48 kHz = **96 KB/s**；立体声 44.1 kHz = **176 KB/s**；立体声 48 kHz = **192 KB/s**。

**一个 APK 能装多少个**（增量全部落在 `.resource`，在 APK 里是 STORED、不再压缩）：

| 音效预算 | 1 秒单声道 48 kHz | 1 秒立体声 44.1 kHz |
|---|---|---|
| 50 MB | **≈ 546 个** | ≈ 297 个 |
| 200 MB | **≈ 2184 个** | ≈ 1190 个 |
| 1 GB | ≈ 11000 个 | ≈ 5900 个 |

> **结论：容量上限 = 手机剩余空间 / APK 安装上限，"134 个槽位"这个瓶颈彻底消失。**
> 真实约束只剩两条：
> ① **每次改动都要重签 + 重装** → 应当**批量**打进一个 APK，而不是一个音效打一次；
> ② 每个音效都要吃掉一个**锚点数组位**（12 B）—— 一个锚点可以放很多项（count 是 int32），也可以分散到多个锚点对象。

**关于 IMA ADPCM（原"路 A2"）的新定位**：PCM16 是 16 bit/样本，IMA ADPCM 是 4 bit/样本 → **小 4 倍**
（单声道 48 kHz 从 96 KB/s 降到 24 KB/s）。
所以 A2 现在的价值**不再是"塞进槽位"**（路 B/A+ 已无容量瓶颈），而是**把 APK 体积压小 4 倍**：
同样 50 MB 预算能从 ≈546 个变成 ≈2180 个。**需要装很多音效时才值得写这个编码器。**

### 8.3 安装 / 回滚（各一条命令）
```bat
:: 安装（装前必须用户点头；会 force-stop 当前会话）
adb -s NZNBUC456PJN7HR4 install -r -i com.miui.packageinstaller ^
  D:\RiderProjects\ml-installer-06\temp\audiohunt\apk_test\WindyCart.apk

:: 回滚（改动只在 APK 资源内，不涉及存档；这份是装前从设备拉下来的装机包，签名必然匹配）
adb -s NZNBUC456PJN7HR4 install -r -i com.miui.packageinstaller ^
  D:\RiderProjects\ml-installer-06\logs\installed_base.apk
```
更精细的还原信息（新对象 pid/offset、锚点 pid 与数组内偏移、改动前后文件长度、全部自证清单）：
`temp\audiohunt\apk_b_newclip\ROLLBACK.json`。

**真机判据**：`[NAMEIDX] ✓ 按名解析 AudioClip "Cart"` + `[INVARIANT]` 0 变化 + 用户**睡一觉**（`BedRoll`）听到 `Cart.wav`（1.2867 s）。

---

## 9. 容器分类结论（锚定设计的依据）

扫 4 个 `.assets` 的全部 MonoBehaviour（同文件 `fileID=0`）：

| 容器类型 | 引用点 | 涉及对象 |
|---|---|---|
| **数组元素** | **1944** | 1378 个 MonoBehaviour |
| 单值 PPtr | 395 | 250 个 MonoBehaviour |

- **数组侧可信**：1944 处全部语义自洽（见 §8.1 第 1 条的例子）。
- ⚠ **单值侧 395 是上界**：`LiberationSans SDF`（一个字体对象）报了 22 个"单值音效字段"，
  里面是一堆互不相关的 clip —— 那是**字体图集数据里的巧合字节**，不是真字段。
  **不影响我们的方案**：我们走数组路线。
- 明细落盘 `temp\audiohunt\out\ref_containers.json`（可直接查任意 clip 的容器形态）。

---

---

## 11. ⛔ 用户硬要求：**禁止改 APK 资产** → 路 A/A+/B 全部作废；运行期通道**已穷举关闭**

> 用户明确要求**纯 mod（运行期）**方案。因此本文件 §2–§10 的资产改写路线**全部停用**（仅作技术记录）。

### 11.1 穷举结论：**运行期没有任何办法创建 AudioClip**
对 `libunity` 的 **853 条注册表** 与 `libil2cpp` 的 **1132 条 wrapper 名** 做全量枚举，
**两侧的音频面完全相同，各 17 条，且没有一条能创建 clip**：
```
AudioMixer::{GetFloat, SetFloat}
AudioClip::get_length                              ← 只读时长
AudioSettings::{StartAudioOutput, StopAudioOutput}
AudioSource::{get_clip, set_clip, Play, PlayHelper, Stop, get_isPlaying,
              get_volume, set_volume, GetPitch, SetPitch,
              get_playOnAwake, set_playOnAwake}
```
全部含 `AudioClip` 的名字只有 3 条：`AudioClip::get_length`、`AudioClip::get_length()`、
`AudioSource::set_clip(UnityEngine.AudioClip)` —— **读长度 + 绑定已有 clip**。

### 11.2 `UnityWebRequestMultimedia` / `DownloadHandlerAudioClip` 通道：**不可用**
| 检查项 | 结果 |
|---|---|
| interop 元数据里有类型吗 | **有**：`UnityEngine.UnityWebRequestAudioModule.dll` 内 `DownloadHandlerAudioClip`、`UnityWebRequestMultimedia`、`GetAudioClip`、`InternalCreateAudioClip`、`audioClip`、`streamAudio` 全在 |
| 它需要的 ICall 在 853/1132 里吗 | **全部未命中**：`DownloadHandlerAudioClip::{InternalCreateAudioClip, CreateAudioClip, InternalGetAudioClip, GetContent}`、`UnityWebRequestMultimedia::GetAudioClip` 逐条 **0 命中** |
| 旁证（同族其它成员） | **Texture 版可用**：`DownloadHandlerTexture::Create` ✔、`DownloadHandlerTexture::InternalGetTextureNative` ✔、`ImageConversion::LoadImage` ✔、`DownloadHandler::InternalGetByteArray` ✔ |
| 其它音频入口 | `PlayClipAtPoint` **0**、`Microphone::*` **0**、`AudioType` **0**、`Multimedia` **0** |

→ **结论**：该 module 的类型进了元数据，但**整条 ICall 被裁**（与 `AudioClip::Construct_Internal` 同一机制）。
"用 Unity 自己在 native 侧造 clip"这个设想**不成立**。

### 11.3 纯 mod 唯一可行的是什么
`AudioSource::set_clip` + `Play()` 播放**游戏已有的 134 个音效**（按名 / 语义挑相近的）。
**能出声、能无限扩展、零资产改动 —— 但听到的不是 mod 原声。**
`AudioClip::get_length` 可用，所以还能拿到真实时长做淡入淡出 / 排队。

---

## 10. 工具与产物索引

| 文件 | 说明 |
|---|---|
| `temp\audiohunt\fsb5_layout.py` | FSB5 布局反推 |
| `temp\audiohunt\fsb5_make.py` | 单个 blob 构造 + 自证（`--clip/--wav/--out/--splice/--patch-length`） |
| `temp\audiohunt\patch_assets.py` | 两个 clip 的 `.resource` + `.assets` 落盘打补丁 + 全套自证 |
| `temp\audiohunt\build_test_apk.py` | 覆盖进 APK + STORED 重打包 + `.so` STORED 自证 |
| `temp\audiohunt\apk_test\README-安装与回滚.md` | 安装/判读/回滚 + 全部自证清单 |
| `temp\audiohunt\apk_test\WindyAudioTest.apk` | **本次已验证的签名 APK**（181674199 B） |
| `logs\installed_base.apk` | 装前的装机 APK（回滚用，签名必然匹配） |
| `logs\save_before_audiotest\` | 存档备份（15 文件 / 3.3 MB，含 4 个存档槽） |
| `temp\audiohunt\out\csti_audioclip_inventory.json` | **134 个游戏音效清单**（名字/时长/声道/频率/offset/size） |
| `temp\audiohunt\REPORT-audio-options.md` | 完整方案对比与证据链 |

| `temp\audiohunt\add_clip2.py` | **★ 路 B 实现**（新增 AudioClip + 锚定 + 全自证），也是 §8.1 六条经验的参考实现 |
| `temp\audiohunt\scan_thunks.py` | 枚举 libunity 的 853 条注册 thunk → `out\libunity_thunks.txt` |
| `temp\audiohunt\classify_refs.py` | 引用点容器分类 → `out\ref_containers.json` |
| `temp\audiohunt\out\libunity_thunks.txt` | **libunity 注册表 853 条**（名字 + 实现地址） |
| `docs\ICALL-AVAILABLE.md` | **引擎/游戏侧可用 ICall 权威清单**（1154 条，分类 + 我们依赖项） |
| `docs\audio\MOD-AUDIO.md` | 本文档 |

### 待办
- [ ] **真机验证 `WindyCart.apk`**（等用户点头）：`[NAMEIDX] ✓ 按名解析 AudioClip "Cart"` + `[INVARIANT]` 0 变化 + 睡觉听到 `Cart.wav`
- [ ] 验 `ChoppingTrunk ← windy.wav`（0.5488 s，砍棕榈树）→ 把"长音频"从未证升为已证
- [ ] MiniLoader 侧：把新 clip 按 mod 音效名注册 + 用 `set_clip`/`Play` 播放（**需 Lead 明确交接 MiniLoader 归属后再动**）
- [ ] 批量工具化：一次把 mod 的**全部** wav 打进一个 APK（现在是一次一个）
- [ ] （可选）IMA ADPCM 编码器 —— 仅在"要装很多音效、需要把 APK 压小 4 倍"时才做（见 §8.2）

