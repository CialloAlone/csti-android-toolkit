# CSTI Android Modkit — 卡牌生存：热带岛屿 · Android + MelonLoader 0.6 移植与逆向工具集

把 PC 版《Card Survival: Tropical Island》的 mod 生态搬到 **Android arm64 + MelonLoader 0.6.5（.NET 8 / Il2CppInterop）**
的过程中沉淀下来的**工具、探针与逆向文档**。mod 本体在各自的仓库（见下方"相关仓库"）。

> 目标设备实测：`com.winterspringgames.survivaljourney`（arm64-v8a，游戏数据裁剪过，`libunity.so` 被 strip）。

---

## 这个仓库里有什么

```
docs/
  LOADER.md                 loader 逆向总纲：游戏加载顺序、ICall 表、warp 机制、插入点
  flow/FLOW.md              游戏启动/读档/事件结算流程测绘
  flow/INTERACTIONS.md      卡牌交互与掉落结算路径
  flow/OBJECTS.md           对象图：ScriptableObject 注册表、AssetBundle、prefab
  flow/PLAN.md              路线规划（A/A′/B/C/D 五条注入路线与取舍）
  flow/_tools/*.ps1         测绘用的 Cecil/strings/xref 脚本
  TASK4-FINDINGS.md         移植与修复的全部结论（§1–§17，含每一条真机判据）
  icall_missing_real.txt    本机缺失的 234 条引擎 ICall 清单
  icall_inventory.txt       引擎 ICall 全量盘点（含 _Injected/Impl 等价命名）
  REPORT-audio-options.md   音效加载方案调研（含 dead-end 结论与替代路径）
  il2cpp_icall_names.txt    从 libil2cpp wrapper 里抽出的 1132 个 icall 名（交叉验证用）

tools/
  RawTextureTest/   原生 IL2CPP 工具库（Texture2D/Sprite 直通调用）+ PC 侧校验工程
  HarmonyProbe/     Harmony "挂补丁即崩" 定位工程（免 hook 注入原型）
  ICallSweep/       ICall 探测/扫描
  TextureProbe/     纹理与精灵 API 探针
  TypeProbe/        类型/注册表探针
```

## 核心结论（详见 `docs/TASK4-FINDINGS.md`）

1. **裁剪构建的 ICall 缺口**：缺失的 234 条里，很多只是"托管可见名"被删掉，等价实现仍以
   `X_Injected` / `XImpl` 形式注册。**不要**直接调用托管包装（`Sprite.Create`、`ImageConversion.LoadImage`
   会 SIGSEGV），要么按 `_Injected` 直调，要么用自实现 shim 补表（见 `csti-icall-fix` 仓库）。
2. **warp 才是引用回填的唯一途径**：mod JSON 用 `"X": {"m_FileID":0,"m_PathID":0}` + `"XWarpData"/"XWarpType"`
   表达引用；`JsonUtility.FromJsonOverwrite` 不认识 `WarpData`。事件选项的真正产物在第 5 层：
   `卡 → DismantleActions → ProducedCards → DroppedCards → DroppedCard`，任何一层断了都会"选了没反应"。
3. **三个必踩的坑**：
   - `MainGen.GetOrGen` 只收 `GetDeclaredFields` → 继承字段（如 `CardAction.ProducedCards`）永远读不到；
   - `Il2CppReferenceArray<T>` 在 interop 里是**类**（`IsArray == false`），按 `IsArray` 判断会漏掉数组下潜；
   - 从数组里取出的元素常被包成 `Il2CppSystem.Object`（gen 表为空）→ 必须按 il2cpp 真实类名重建代理再下潜。
4. **防污染 = 深拷贝式"去共享"**：克隆模板对象后，把数组**等长重建**、元素换同类型新实例并递归；
   之后 warp 写入只落在 mod 私有副本上，游戏自带对象 `[INVARIANT]` 尺寸/内容变化保持 0。
5. **原版资产按名字引用**：`CardTag/EquipmentTag/ActionTag/Sprite/AudioClip` 在 JSON 里用名字
   （`tag_Decoration`、`BG_SandTop`…），而注册表只有 GUID 键 → 需要"类型+名字"二级索引。

## 使用方法

```powershell
# 依赖：MelonLoader 0.6.5 的 net8 托管 dll 与 Il2CppInterop 生成的游戏代理程序集
dotnet build -c Release -p:ML06=<你的 ml-installer-06 目录>
```

真机脚本（短轮询、不阻塞）在 loader 仓库的 `CSTI-MiniLoader/tools/`：
`csti_run.ps1`（推送并等待关键日志）、`archdump.py`（离线解包 `.modArch_V3`，含 GameSourceModify 分析）。

## 相关仓库（同一套移植工程）

| 仓库 | 内容 |
|---|---|
| [`csti-icall-fix`](https://github.com/CialloAlone/csti-icall-fix) | 缺失 ICall 的接管与 shim |
| [`csti-quickmenu`](https://github.com/CialloAlone/csti-quickmenu) | 悬浮窗快捷菜单 |
| [`CSTI-ModLoader` @ `android-06-port`](https://github.com/CialloAlone/CSTI-ModLoader/tree/android-06-port) | 移植版 MiniLoader（本轮主体） |
| [`CstiCheatConsoleMobile` @ `ml06-port`](https://github.com/CialloAlone/CstiCheatConsoleMobile/tree/ml06-port) | 作弊控制台 ML 0.6 移植 |
| [`CstiDetailedCardProgress` @ `ml06-port`](https://github.com/CialloAlone/CstiDetailedCardProgress/tree/ml06-port) | 详细卡面 ML 0.6 移植 |
| [`MelonLoaderInstaller` @ `android-pack-fixes`](https://github.com/CialloAlone/MelonLoaderInstaller/tree/android-pack-fixes) | APK 打补丁/重签名修复 |
| [`Il2CppInterop` @ `csti-fixes` / `csti-fixes-runtime`](https://github.com/CialloAlone/Il2CppInterop/branches) | 生成器与注入器兼容修复 |

## 免责声明

仅供学习与个人存档使用。所有原游戏资源与上游项目版权归各自作者；本仓库只包含我们自己编写的
工具、文档与补丁，**不含**游戏本体、APK、签名密钥或任何上游二进制。
