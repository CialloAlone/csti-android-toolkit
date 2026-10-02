# RawTextureTest v3 —— 原生 IL2CPP 纹理通道（裸调 ICall 函数指针）

## 交付物

| 文件 | 作用 |
| --- | --- |
| `RawTexture.cs` | 可复用工具类 `RawTexture`（全局命名空间，移植到 MiniLoader 零改动引用） |
| `RawTextureTestMod.cs` | 测试 mod v3：CreateTexture2D → **裸调 `ImageConversion::LoadImage`** → EncodeToPNG 回读逐像素比对，含 VERIFY-A..F |
| `pc-verify/` | PC 侧独立验证工程：拿 Pillow 当参照物逐字节验证 PNG 自解码器（fallback 用） |
| 产物 | `bin\Release\RawTextureTest.dll`（单文件，直接丢 Mods；**不要**拷 bin 下其它 dll） |

构建：`dotnet build -c Release -p:ML06=D:\RiderProjects\ml-installer-06` → 0 warning / 0 error

## 固定 API

```csharp
public static IntPtr CreateTexture2D(int w, int h);                                  // RGBA32 + 无 mipmap
public static IntPtr CreateTexture2D(int w, int h, int textureFormat, bool mipChain);
public static bool   LoadImage(IntPtr tex, byte[] data);          // 主：裸调原生 ICall；失败才回退自解码+像素上传
public static bool   LoadRawTextureData(IntPtr tex, byte[] data); // 恒 false + LastError（ICall 被裁剪）
public static IntPtr CreateSprite(IntPtr tex, float x, float y, float w, float h);// 硬墙：优雅跳过，返回 0
public static object Wrap(IntPtr ptr, string assembly, string ns, string type);
public static IntPtr ResolveIcall(string name);
public static bool   SetPixels32(IntPtr tex, byte[] rgba32, int width, int height, bool flipRows); // fallback 通道
public static bool   Apply(IntPtr tex, bool updateMipmaps, bool makeNoLongerReadable);
public static bool   TryDecodeImage(byte[] data, out int w, out int h, out byte[] rgba32);
public static bool   TryReadImageSize(byte[] data, out int w, out int h);           // 只读 PNG IHDR
public static IntPtr CreateTexture2DFromImage(byte[] data);
// 诊断：Log / LastError / PinCreatedObjects / Describe() / DescribeMethod() / DescribeException()
//       GetTextureSize() / TryGetIntProp() / ProbeIcalls() / ArrayLength() / ArrayData() / Root()
```

## 真机实证的结论链（照抄，别再试错）

1. **托管包装 → SIGABRT；原生 ICall 实现是好的**。
   崩因是托管 `ImageConversion.LoadImage` 内部要经 il2cpp 解析 ICall；
   而 `il2cpp_resolve_icall("UnityEngine.ImageConversion::LoadImage")` 拿到的函数指针可以**直接裸调**：
   `delegate* unmanaged[Cdecl]<IntPtr,IntPtr,byte,IntPtr,byte>`，参数 `(tex, byteArray, markNonReadable=0, MethodInfo*=0)`，
   返回 1、不崩。本类主路径就用这个（编译产物已核对：`calli ... CallingConvention=C`）。
2. **Sprite 创建是硬墙**：`Sprite::Create` / `CreateSpriteWithoutTextureScripting` / `Internal_CreateSprite` /
   `CreateWithTexture` 全无注册 → 托管 `Sprite.Create` 必然转调它们 → invoke 即 SIGABRT。
   `CreateSprite()` 现在**只解析元数据并报告，绝不 invoke**，返回 0 + 明确 LastError。
3. **缺失 ICall**：`Texture2D::LoadRawTextureDataImpl(Array)`、`SetPixelDataImplArray`、`Internal_Create`，
   AssetBundle 的 `LoadFromMemory_Internal` / `LoadFromFile_Internal` / `LoadAsset_Internal` 也全缺 → 两条路都不通。
4. **已注册可用**：`ImageConversion::LoadImage`、`ImageConversion::EncodeToPNG`、`Texture2D::SetPixelsImpl`、
   `ApplyImpl`、`get_isReadable`。
5. `get_width` / `get_height` 虽然元数据里存在，但是**带托管方法体的虚属性**（内部再调 `Texture::GetDataWidth/GetDataHeight`），
   与本条 1 同类风险且未验证 → 本类一律不 invoke。纹理尺寸改用**指针登记表**：
   `CreateTexture2D` 时记录，原生 `LoadImage` 成功后用 `TryReadImageSize`（只读 IHDR）刷新（原生解码会 Resize 纹理）。
6. `SetPixelsImpl` 真实签名 = `(int x, int y, int w, int h, UnityEngine.Color[] pixel, int miplevel, int frame)`，
   像素数组是 **Color[]（16 字节/元素）**，不是 Color32[]（证据：生成代码字段名 `..._Il2CppStructArray_1_Color_Int32_Int32_0`）。
   → **fallback 路径峰值内存 = 像素数 x 16B**（1024² ≈ 16MB，2048² ≈ 67MB）；主路径只占原图字节，没有这个问题。
7. 值类型返回值是装箱对象 → `il2cpp_object_unbox(res)` 再读；`il2cpp_array_new` 收元素类指针、数据区偏移 `4*IntPtr.Size`；
   静态方法 obj=`IntPtr.Zero`，实例方法 obj=纹理指针；裸 IntPtr 用 `il2cpp_gchandle_new` 根化。

## PC 侧已验证

`dotnet run --project pc-verify -- pc-verify/testdata` → **13/13 与 Pillow 逐字节一致**
（位深 1/4/8/16，颜色类型 0/2/3 含 tRNS/4/6，尺寸 1x1~53x37，覆盖 5 种滤波）。
静态扫描产物：AssemblyRefs 含 `System.IO.Compression`（ZLibStream），**零 UnityEngine 引用、零 ldtoken 托管类型句柄**。

**顺带抓到的真问题**：任务里给的那串 78 字节 base64 PNG 结构是坏的 —— IDAT 长度字段写 20、实际块占 21+4 字节，
规范 IEND 在偏移 66，zlib Adler-32 校验失败。Pillow 宽容放行，严格解码器拒绝。已换成 Pillow 生成校验过的
108 字节 4x4 PNG（图案确定：红/绿/蓝/黄 → 青/品红/白/黑 → 半透明行 → 递增灰阶行）。

## 真机验证清单（由 Lead 执行）

1. `adb push bin\Release\RawTextureTest.dll` → 游戏 `Mods\`，启动。
2. 期望关键日志：
   - `ICALL 有 ... UnityEngine.ImageConversion::LoadImage` / `EncodeToPNG` / `SetPixelsImpl` / `ApplyImpl` / `get_isReadable`；
     Sprite 相关那几条应全是 `无`
   - `原生 ICall LoadImage = 0x...`（0 = 未解析到，会退回 fallback）
   - `STEP1 OK` → `STEP2a 参考自解码 4x4` → `STEP2b LoadImage(主路径=原生 ICall 裸调) -> True`（日志里有 `N1 裸调 ...` / `N2 裸调返回 1`）
   - `STEP3 CreateSprite ptr=0x0 ... 结论: 通过（按预期优雅跳过，进程存活）`
   - `VERIFY-A isReadable=1`
   - `VERIFY-B EncodeToPNG -> NN 字节, 头=89-50-4E-47` + `VERIFY-B2 逐像素比对: 同序一致=True` ← **决定性证据**
   - `VERIFY-C fallback 通道 ... -> True`、`VERIFY-D ... 4x4 通过`、`VERIFY-E ... 通过`、`VERIFY-F ... 通过`
   - `=== RawTextureTest v3 结束：进程存活 ===`
3. 崩点定位：主路径崩的话最后一条日志是 `N1 裸调 ...`；fallback 崩的话最后一条是 `L4 即将 il2cpp_runtime_invoke(SetPixelsImpl) ...`。

## 已知边界

- 原始 GPU 字节（DXT5 等）上传无解；`LoadRawTextureData` 恒 false + 明确 LastError。
- Sprite 创建无解（ICall 全缺）；AssetBundle 无解。
- fallback 自解码只支持 PNG（非隔行；灰度/RGB/调色板/灰度+A/RGBA，8/16 位与 1/2/4 位子字节），JPEG 明确报不支持；
  但主路径用的是引擎原生解码器，PNG/JPG 都能吃。
- fallback 不能 Resize（尺寸必须一致）；主路径可以（Unity 原生语义）。
- fallback 依赖 .NET 8 框架自带的 `System.IO.Compression.ZLibStream`；若真机运行时把它裁了，会报可捕获的
  `IDAT zlib 解压失败: FileNotFoundException`（不崩），届时再考虑纯托管 inflate。
