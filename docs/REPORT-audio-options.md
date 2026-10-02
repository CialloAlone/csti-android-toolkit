# CSTI mod 音效加载 — 可行方案（audio-hunt 结题报告）

> ## ⚠️ 更正（v3 真机探针之后，2026-10-02 21:33）
> **运行时"造声音"这条路已经彻底堵死，包括我一度推荐的 `AudioSampleProvider`。**
> 真机实测：`UnityEngine.Experimental.Audio.AudioSampleProvider::*` **22 条全部 = 0x0**，
> `Internal_RegisterSampleProviderWithAudioSource` = 0x0。
> 原因与 `AudioClip::Construct_Internal` **完全相同**：`libil2cpp.so` 里搜不到这些 wrapper 名字
> （`InternalCreateSampleProvider` / `QueueSampleFrames` / `RegisterSampleProvider` / `Experimental.Audio`
> 全部 0 次命中），而 `libunity.so` 里有一堆实现 →
> **引擎有实现 ≠ 能解析；必须是"游戏托管代码引用过"的 ICall 才会生成 wrapper。**
>
> **判据修正（重要）**：`icall_missing_real.txt` 只覆盖它当时那份候选清单，
> **不能用来反推"已注册"**。唯一权威判据是：
> ① 真机 `il2cpp_resolve_icall`；② `libil2cpp.so` 里是否存在该名字字符串（存在 ⇔ 有 wrapper ⇔ 一定能用）。
>
> 因此：**能听到 mod 原声**只剩"APK 资产级替换"（方案丙）；
> **运行时能做的**只剩"映射到游戏已有 clip"（方案乙，降级）。

> 原始记录（下面方案甲一节）保留，以免后人重复踩坑。

---

## 0. 权威判据（全组通用，唯一有效）

> ### 判断一个 ICall 能不能用，只看一条：
> ### **`libil2cpp.so` 里有没有该 ICall 的 wrapper 名字字符串。**
> ### 有 → 一定能用。没有 → **一定失败**。
>
> **不成立的推论（我已犯过，勿重蹈）**：
> - ❌ "`libunity.so` 里有实现" ⇒ 能用 —— 否。`AudioClip::Construct_Internal`、
>   `AudioSampleProvider::*` 在 `libunity.so` 里都有实现，解析全部返回 0。
> - ❌ "interop 元数据里有这个类/方法" ⇒ 能用 —— 否。`AudioSampleProvider` 类在元数据里完好，
>   它的 ICall 一个都不注册。
> - ❌ "名字在 `icall_inventory.txt` 里且不在 `icall_missing_real.txt` 里" ⇒ 能用 —— 否。
>   这两份文件不是同一次扫描的产物，**逻辑上推不出任何结论**。
> - ❌ "换个名字变体（`_Injected`/`Impl`/…）试试" ⇒ 无用。没有 wrapper 就没有任何名字能命中。
>
> **两个有效手段（互为交叉验证）**：
> 1. 真机 `il2cpp_resolve_icall(name)`（权威，但需要设备）；
> 2. PC 侧在 `gamedata\lib\arm64-v8a\libil2cpp.so` 里搜这个名字的字符串（**不需要设备**，可离线预判）。
>    已验证：方法 2 的预测与真机实测 **1:1 吻合**（音频 15 条 HAVE 全部命中，其余全部 MISS）。
>
> 便捷清单：`temp\il2cpp_icall_names.txt` = libil2cpp.so 里全部 1132 条被引用的 ICall 名（带签名形式）。
> **它本身就是"能用"的白名单**（下界仍然不是全集，但命中即可用）。
> `icall_missing_real.txt`（234 条）只对该次扫描的候选清单有效，**不能反推"已注册"**。



---

## 1. 事实基础（全部有证据，不是推断）

### 1.1 引擎注册表的权威名单
| 来源 | 内容 |
|---|---|
| `icall_missing_real.txt`（真机实测） | 6051 条候选里 **234 条未注册** |
| `icall_inventory.txt`（随 CstiICallFix 打包） | 全部 6051 条候选名 |
| `temp\il2cpp_icall_names.txt`（PC 侧从 `libil2cpp.so` 提取） | 1132 条被引用的 icall 名 —— 注意这是**下界**，不是全集 |

判据：
- 名字**不在** 1132 清单里 → **一定**失败。
- 在清单里 → 必要不充分（反例：`UnityEngine.AudioSource::PlayOneShotHelper` 不在缺失名单里，
  但真机实测 = 0）。

### 1.2 音频 ICall 真机实测（`audio_probe.txt`，2026-10-02 21:28）
**已注册（可用）**
```
UnityEngine.AudioClip::get_length()
UnityEngine.AudioSource::Play(System.Double)
UnityEngine.AudioSource::PlayHelper(UnityEngine.AudioSource,System.UInt64)
UnityEngine.AudioSource::get_clip / set_clip
UnityEngine.AudioSource::Stop(System.Boolean)
UnityEngine.AudioSource::get_isPlaying / get_volume / set_volume
UnityEngine.AudioSource::SetPitch(UnityEngine.AudioSource,System.Single)
UnityEngine.GameObject::Internal_AddComponentWithType(System.Type)
UnityEngine.Object::FindObjectsOfType(System.Type)     ← 枚举对象请用这条
UnityEngine.Object::GetName(UnityEngine.Object)
```
**未注册（16 条 = AudioClip 的"创建/数据"全部能力）**
```
Construct_Internal / CreateUserSound / SetData / GetData / LoadAudioData / UnloadAudioData /
GetName / get_samples / get_channels / get_frequency / get_loadType / get_isReadyToPlay /
get_loadState / get_preloadAudioData / get_ambientic / get_loadInBackground
```
另外实测 **MISS** 的还有：`AudioSource::PlayOneShotHelper`、`Resources::FindObjectsOfTypeAll`。

> 为什么"猜名字"必败：`libil2cpp.so` 里为每个**被引用的** internal call 生成 wrapper，wrapper 内嵌
> 名字字面量。`AudioClip::Construct_Internal` 在这 19 MB 的 .so 里**连字符串都不存在**
> → 没有任何托管方法会去解析它 → 解析必然返回 0。变体名（`_Injected`/`Impl`/…）同理，全是空炮。

### 1.3 引擎二进制侧（解释"为什么引擎有实现却解析不到"）
- `libunity.so`（16 MB，从 stock APK 抽出）`.rodata` 里**确实有** `UnityEngine.AudioClip::Construct_Internal`、
  `CreateUserSound`、`SetData`、`PlayOneShotHelper` 等名字（0xC64B88–0xC68840，连续 NUL 分隔串池，约 301 条）。
- 但：`.dynsym` 只有 **346 符号 / 5 个导出函数 / 音频导出 0**；`.rela.dyn` 26621 条里
  **指向串池起点的重定位 = 0**。
→ **没有静态可读的 `{名字→函数指针}` 表**，`dlsym` 也不可能（未导出）。
→ `il2cpp_resolve_icall` 查的是**另一张表**（只覆盖该游戏实际引用的子集），所以"引擎里有实现" ≠ "能解析"。
→ 因此 `GUIClip::GetMatrix_Injected`（不在 1132 清单里）必然失败，画 IMGUI 的那个 mod 别在它上面耗时间。

### 1.4 游戏自己的音效是怎么来的
- `Assembly-CSharp` 音频类型：`SoundManager`(MBSingleton) / `MenuSoundManager` / `AudioSourcePool` /
  `RandomSoundPlay` / `AmbientSounds` / `ButtonSounds` / `RandomAmbientSound` / `SimpleSoundFade`；
  50 个 `AudioClip`/`AudioSource` **字段**（`CardData.WhenCreatedSounds`、`CardAction.ActionSounds`、
  `SoundManager.DefaultCookingComplete` …）。
- **全是序列化资产引用，游戏运行时从不"造" clip** —— 所以它只需要播放类 ICall，创建类全被裁掉。
- 资源形态：**没有 AssetBundle**（AssetBundleModule 的 icall 一条都没有）；
  音频是明文序列化文件 `assets/bin/Data/sharedassets0.resource` 里的
  **128 个 `FSB5`（FMOD Sound Bank）** + `sharedassets2.resource` 里 7 个 = 共 **135 个 clip**。

### 1.5 两条对全组都重要的现场事实
1. **`MelonMod.OnUpdate` 在这个构建里根本不触发。** 两次真机运行，`OnUpdate` 里的落盘一次都没执行
   （探针触发源始终是后台线程；`icall_intercepted.txt` / `icall_resolved_all.txt` 从未生成）。
   任何依赖 OnUpdate 的周期性逻辑都是死的。**线程可用（已实测）。**
2. `Resources::FindObjectsOfTypeAll` 未注册 —— `MiniLoader\Diag.cs:2071` 用它，必然失败；
   改用 `Object::FindObjectsOfType(System.Type)`。

---

## 2. 方案

### ❌ 方案甲（已实测否决 —— 2026-10-02 21:33 v3 探针）
**用 `UnityEngine.Experimental.Audio.AudioSampleProvider` 把 PCM 直喂 AudioSource。**

真机结果：**22 条 ICall 全部 = 0x0**，链路测试根本没跑起来。
```
[1] 小结: 已注册 8 / 缺失 22 / 共 30
    InternalCreateSampleProvider / InternalQueueSampleFrames / InternalGetFreeSampleFrameCount /
    InternalIsValid / InternalSetEnableSilencePadding / InternalGetScriptingPtr / … 全部 0x0
    AudioSourceExtensionsInternal::Internal_RegisterSampleProviderWithAudioSource = 0x0
```
二进制侧印证：`libil2cpp.so` 中 `InternalCreateSampleProvider` / `QueueSampleFrames` /
`RegisterSampleProvider` / `Experimental.Audio` **命中 0 次**（`AudioSampleProvider` 只出现 2 次，是类型名）；
`libunity.so` 中 `AudioSampleProvider` 31 次、`InternalCreateSampleProvider` 1 次。
→ 引擎有实现，但游戏托管代码从不引用 → **不生成 wrapper → 解析恒为 0**。

**教训**：我曾把"名字在 `icall_inventory.txt` 里且不在 `icall_missing_real.txt` 里"当成"已注册"——
这两份文件不是同一次扫描的产物，**逻辑上推不出任何结论**。已作废。

<details><summary>原（已被否决的）方案甲设计，仅作记录</summary>

链路：
```
mod WAV → NAudio 纯托管解码成交织 float32
        → AudioSampleProvider::InternalCreateSampleProvider(ch, rate)   → providerId
        → InternalQueueSampleFrames(providerId, float* , frameCount)     ← 裸 IntPtr，不需要 NativeArray
        → AudioSourceExtensionsInternal::Internal_RegisterSampleProviderWithAudioSource(audioSource, providerId)
        → AudioSource::Play(0)
        → 后台线程按 InternalGetFreeSampleFrameCount 持续补帧
```

**可行性（有证据）**
- 这条支线的 **ICall 全部已注册**（真机 `il2cpp_resolve_icall` 逐条实测 HAVE）：
  `InternalCreateSampleProvider`、`InternalQueueSampleFrames`、`InternalGetFreeSampleFrameCount`、
  `InternalGetMaxSampleFrameCount`、`InternalIsValid`、`InternalSetEnableSilencePadding`、
  `InternalSetEnableSampleFramesAvailableEvents`、`InternalGetScriptingPtr`、
  `Internal_RegisterSampleProviderWithAudioSource`、`Internal_UnregisterSampleProviderFromAudioSource`。
- interop 元数据里 `UnityEngine.Experimental.Audio.AudioSampleProvider` 类**存在且未裁剪**
  （`public static AudioSampleProvider Create(ushort, uint)`、`QueueSampleFrames`、
  `freeSampleFrameCount`、`sampleFramesAvailable` 事件、`AudioSourceExtensionsInternal.RegisterSampleProvider` 都在）。
- **完全不碰** 那 16 条缺失 ICall。

**工作量**：中等偏小。约 1 个新文件（~250 行）+ `LoadAudioBLK` 两行接线。
草稿已写好：`temp\audiohunt\ModAudioPlayer.cs.draft`。

**风险**
- ⚠ 上机前必须验证：`AudioSampleProvider` 需要音频输出在跑、且 Android 上这条 Mesa/AAudio 支线在
  裁剪引擎里是否真的被链接。**目前只有"ICall 已注册 + 元数据存在"两级证据，缺第三级"真机播放成功"。**
  → 探针已写好（`CstiICallFix\AudioProbe.cs` 第 [3] 段：造 1 秒 440 Hz 正弦→播放→读 `isPlaying`），
  **等设备窗口，90 秒出结果**。
- 需要持续的补帧线程；本构建 OnUpdate 不可用，所以必须用线程（已实测线程可用）。
- 长音频的缓冲管理要自己写（`maxSampleFrameCount` 有限）。

---

### 方案乙（降级，今天就能上）
**不创建 clip，把 mod 音效名映射到游戏已有 AudioClip，用已注册的播放路径播。**

```
set_clip(游戏clip) → Play()        // 两者都已注册
```
- **可行性**：播放链 100% 已注册（真机实测）。
- **工作量**：小。`LoadAudioBLK` 里把"解码→建 clip"换成"查映射表→取游戏 clip"。
- **代价**：**用户听到的是另一个游戏音效，不是 mod 原声**。
- **需要的数据**：游戏 clip 的名字清单 → 用 `Object::FindObjectsOfType(typeof(AudioClip))` +
  反射 `SoundManager` 的命名 `Default*` 字段拿。探针第 [2] 段已实现。
- **风险**：低。注意**不要用 `PlayOneShot`**（`PlayOneShotHelper` 实测 MISS），要用 `set_clip` + `Play()`。

---

### ✅ 方案丙（已 PC 侧验证 —— 唯一能听到 mod 原声的路）
**直接替换 APK 资产里某个游戏 AudioClip 的音频数据；运行时用方案乙的映射把 mod 音效名绑到它。
运行时零新增 ICall 依赖，整个注册表问题被绕开。**

#### 已验证的资产结构（UnityPy 读 `sharedassets0.assets` / `sharedassets2.assets`，type tree 内嵌，字段全部可读）
- **共 134 个 AudioClip**：`sharedassets0.assets` **127 个** + `sharedassets2.assets` **7 个**（`sharedassets3` 0 个）。
- 全部走 **`StreamedResource`** 引用音频数据：`m_Resource = {m_Source:"sharedassetsN.resource", m_Offset, m_Size}`，
  **没有任何 `m_AudioData` 内联数据**。
- **127 个 blob 的大小之和 = 43,577,312 = `sharedassets0.resource` 文件大小**（16 字节对齐，**精确铺满、无空洞**）。
- 容器格式：**FSB5（FMOD Sound Bank v1）**，`numSamples=1`，`nameTableSize=0`：
  | 数量 | `m_LoadType` | `m_CompressionFormat` | FSB5 `mode` | `sampleHeadersSize` |
  |---|---|---|---|---|
  | **111** | 1 (CompressedInMemory) | 2 | **7 = IMA ADPCM** | 36 |
  | **16** | 0 (DecompressOnLoad) | 1 | **15 = VORBIS** | 68–484 |
  | 7 (sharedassets2) | 1 | 2 | 7 = IMA ADPCM | 36 |
- 完整清单（名字/时长/声道/频率/offset/size）已落盘：
  **`temp\audiohunt\out\csti_audioclip_inventory.json`** —— 这就是方案乙/丙要的映射表基础，
  **不需要真机**。例：`ButtonClick`、`ButtonHover`、`ChoppingTrunk`、`ChoppingTree_Shorter`、`WaveCrash`、
  `PalmTree`、`Eating`、`Drink`、`CameraShutter`、`Sleep1`、`DjangoTune`…

#### 推荐实现路径：**等长原地替换 `.resource`，`.assets` 一个字节都不用改**
1. 挑一个目标 clip，把 mod 的 WAV 编码成 FSB5，**补齐到与原 blob 完全相同的字节数**（多出的部分填静音）。
   - 优先选 **IMA ADPCM(mode 7)** 的 clip（111 个，`sampleHeadersSize=36`，是最简单的 FSB5 变体）；
     需要写一个 IMA ADPCM 编码器（~80 行，格式公开）。
   - 备选：直接用 **`mode=2 (PCM16)`** 的 FSB5（**零编码器**，只要包一层 36 字节头 + 原始 PCM）。
     FMOD 是按 FSB5 头里的 `mode` 解码的，所以理论上不必动 `m_CompressionFormat`；
     **这一条需要真机确认**（见风险）。
2. 保持 `channels / frequency / duration` 与原 clip **一致**（把 mod 音频重采样 + 裁剪/补静音到原时长），
   这样 `m_Length / m_Channels / m_Frequency / m_BitsPerSample` 全部不用改 →
   **`.assets` 完全不改，只 patch `.resource`**，且**文件长度不变 → 所有其它 clip 的 offset 都不受影响**。
3. 运行时（MiniLoader）：把 mod 音效名映射到该 clip，走已注册的 `set_clip` + `Play()`。

**可行性**：高。资产结构、容器格式、字段布局、精确铺满的对齐都已实测确认；
剩下的只有"FSB5 blob 构造正确性"这一个未验证点。

**工作量**：中。PC 侧工具链 ~400 行 Python/C#（FSB5 头写入 + IMA ADPCM 编码 + 重采样/补齐 + 打补丁）。
项目已有 `AssetsTools.NET.dll`，但本报告用的是 UnityPy（更省事，已装到 `temp\audiohunt\pylibs`）。

**风险**
- ⚠ **FSB5 blob 必须真机验证**：PCM16 子方案若被 Unity/FMOD 拒绝，就退回 IMA ADPCM 编码（确定性更高但要多写编码器）。
- 每次换音效 = 重打 APK + 重签；我们的 `out\base_signed.apk` / STORED 对齐流程已具备。
- **不需要改 `.assets`，所以不需要重算资源偏移、不需要重打包 `.resource`**（这点显著降低了风险）。
- **是否需要真机验证**：需要（最终验收；且 FSB5 子方案二选一要真机拍板）。

#### 附带收获
- **134 个游戏音效的名字清单在 PC 侧就能拿到**（`csti_audioclip_inventory.json`），
  方案乙的映射表不用真机、不用等 `SoundManager.Instance` 初始化。
- "谁引用了 AudioClip"的遍历这次没抓到（MonoBehaviour 的 type tree 里引用字段未按预期呈现），
  但对方案丙不构成阻塞 —— 我们不需要知道谁引用它，只需要知道 **clip 名字**（映射用）和 **blob 位置**（替换用）。


---

### 方案丁（判定不可行，记录以免重复踩坑）
- **运行时创建 AudioClip**：不可行。`Construct_Internal` / `SetData` / `CreateUserSound` 等 16 条
  全部未注册，且 `libil2cpp.so` 里连名字字面量都没有（没有 wrapper 生成）。变体名探测 114 个全 0。
- **dlsym 引擎实现**：不可行。`libunity.so` 音频符号导出 0。
- **AssetBundle 装载**：不可行。AssetBundleModule 的 icall 一条都没被引用（全裁）。
- **`DownloadHandlerAudioClip` 下载音频**：不可行（相关 icall 未注册）。
- **`Microphone.Start`**：不可行（未注册）。
- **直接读 `{名字→函数指针}` 表**：不存在（`.rela.dyn` 无指向串池的重定位）。

---

## 3. 建议的执行顺序

1. **拿 90 秒设备窗口，跑 `AudioProbe` 第二版** —— 一次性判定方案甲是否真能出声。
   （探针已编译好：`mods-06\CstiICallFix\bin\Release\CstiICallFix.dll`，21:30:36）
2. 若 `isPlaying=true` → 把 `temp\audiohunt\ModAudioPlayer.cs.draft` 落到
   `CSTI-MiniLoader-06\LoadUtil\ModAudioPlayer.cs`，改 `LoadAudioBLK` 两行，重新编译上机。
   **这是能听到 mod 原声的完整方案。**
3. 若方案甲不成立 → 走方案乙（今天可交付，降级），方案丙作为后续。

## 4. 未决 / 需要设备的验证点
- [ ] **方案甲是否真能出声**（唯一的关键未知，探针已就绪）
- [ ] 游戏 AudioClip 的名字清单（映射表数据，探针第 [2] 段）
- [ ] `SoundManager` 命名 clip 字段是否可在运行时反射读到（探针第 [2] 段）

## 5. 产物索引
| 文件 | 说明 |
|---|---|
| `temp\audiohunt\out\libunity_elf.txt` | libunity.so 段表 / 导出符号 / 串池定位 |
| `temp\audiohunt\out\libunity_table.txt` | `.rela.dyn` 重定位分析（证明无指针表） |
| `temp\audiohunt\out\cecil_audio.txt` | 游戏音频类型 / 50 个音频字段 / AudioModule 托管面 |
| `temp\audiohunt\out\surface_audio.txt` | `AudioSampleProvider` 等类的完整托管签名 |
| `temp\il2cpp_icall_names.txt` | libil2cpp.so 里 1132 条被引用的 icall 名 |
| `temp\unity_icall_candidates.txt` | libunity.so 里 5220 条 `UnityEngine.*` icall 候选名 |
| `temp\audiohunt\device\audio_probe.txt` | 第一轮真机探针原始输出 |
| `temp\audiohunt\ModAudioPlayer.cs.draft` | 方案甲的落地代码草稿 |
| `mods-06\CstiICallFix\AudioProbe.cs` | 第二轮探针（验证方案甲 + 枚举 clip） |
