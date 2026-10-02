using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Il2CppInterop.Runtime;

/// <summary>
/// 纯原生（全程 IntPtr）的 UnityEngine 纹理 / 精灵工具。
///
/// 为什么必须这么写：
///   托管 <c>UnityEngine.Texture2D</c> 类型在本作（CSTI + Il2CppInterop 1.5.3 + .NET 8）上是致命的——
///   任何一次类型解析（<c>Il2CppType.Of&lt;Texture2D&gt;()</c> / <c>typeof(Texture2D)</c> / <c>new Texture2D(...)</c>）
///   都会走到 mono_class_from_mono_type_internal 的未实现分支，进程直接 SIGABRT。
///   本类不出现任何托管 Unity 类型，全部通过 <see cref="IL2CPP"/> 的原生导出函数操作，对象一律用 IntPtr 传递。
///
/// 真机结论（Lead 实测，勿再试错）：
///   * <b>托管包装会 SIGABRT，但原生 ICall 实现本身是好的</b>：用
///     <c>il2cpp_resolve_icall("UnityEngine.ImageConversion::LoadImage")</c> 拿函数指针，再用
///     <c>delegate* unmanaged[Cdecl]&lt;IntPtr,IntPtr,byte,IntPtr,byte&gt;</c> 裸调
///     <c>(tex, byteArray, markNonReadable=0, MethodInfo*=0)</c> → 返回 1、不崩（真机实证）。
///     故 <see cref="LoadImage"/> 主路径 = 裸调原生 ICall；自解码 + SetPixelsImpl 只作 fallback。
///   * 已注册可用 ICall：<c>ImageConversion::LoadImage</c>、<c>ImageConversion::EncodeToPNG</c>、
///     <c>Texture2D::SetPixelsImpl</c>、<c>Texture2D::ApplyImpl</c>、<c>Texture2D::get_isReadable</c>。
///   * 缺失 ICall：<c>Texture2D::LoadRawTextureDataImpl(Array)</c>、<c>SetPixelDataImplArray</c>、
///     <c>Texture2D::Internal_Create</c> —— 原始 GPU 字节（DXT5 等）上传无解，见 <see cref="LoadRawTextureData"/>。
///   * <b>Sprite 创建是硬墙</b>：<c>Sprite::Create</c> / <c>CreateSpriteWithoutTextureScripting</c> /
///     <c>Internal_CreateSprite</c> / <c>CreateWithTexture</c> 全无注册；托管 <c>Sprite.Create</c> 内部必然转调它们，
///     invoke 就是 SIGABRT。故 <see cref="CreateSprite"/> 只报告并优雅跳过，<b>绝不尝试 invoke</b>。
///   * AssetBundle 相关 ICall 亦全缺，不要走那条路。
///
/// 本机 interop_out 元数据核对出的事实（写代码时依赖这些结论）：
///   1) <c>il2cpp_runtime_invoke</c> 对值类型返回值返回<b>装箱对象</b>，必须 <c>il2cpp_object_unbox(res)</c> 之后再读。
///   2) <c>Texture2D::SetPixelsImpl</c> 真实签名 = <c>(int x, int y, int w, int h, UnityEngine.Color[] pixel, int miplevel, int frame)</c>
///      —— 7 个参数，像素数组是 <b>Color[]（每元素 4 个 float = 16 字节）</b>，<b>不是 Color32[]</b>。
///      证据：生成代码字段名 NativeMethodInfoPtr_SetPixelsImpl_Private_Void_Int32_Int32_Int32_Int32_Il2CppStructArray_1_Color_Int32_Int32_0
///   3) <c>Texture2D::ApplyImpl(bool updateMipmaps, bool makeNoLongerReadable)</c> —— 2 个参数。
///   4) <c>il2cpp_array_new</c> 收<b>元素类</b>指针；数据区偏移 = 4 * IntPtr.Size（64 位 = 0x20）。
///   5) <c>Sprite.Create</c> 同参数个数重载有歧义（还有 Create(Rect,Vector2,float)），必须按第一个参数类型名筛 Texture2D。
///   6) 静态方法 obj 传 IntPtr.Zero；实例方法（SetPixelsImpl / ApplyImpl）obj 传纹理对象指针。
/// </summary>
public static class RawTexture
{
    // ---------------------------------------------------------------- 常量

    /// <summary>引擎程序集名必须带 .dll 后缀，否则 IL2CPP.GetIl2CppClass 返回 0。</summary>
    public const string CoreModule = "UnityEngine.CoreModule.dll";
    public const string ImageConversionModule = "UnityEngine.ImageConversionModule.dll";
    public const string MscorlibModule = "Il2Cppmscorlib.dll";

    /// <summary>UnityEngine.TextureFormat.RGBA32 == 4</summary>
    public const int TextureFormatRGBA32 = 4;

    // ---------------------------------------------------------------- 诊断开关

    /// <summary>可选日志回调；为 null 时不输出。测试 mod 会接到 MelonLoader 的 LoggerInstance。</summary>
    public static Action<string> Log;

    /// <summary>
    /// 是否对创建出来的原生对象加一个强 GC 句柄（il2cpp_gchandle_new）。
    /// 我们只持有裸 IntPtr，IL2CPP 的保守 GC 有可能回收它，加句柄可根化对象。
    /// 关闭它不会影响 API 形状，只影响生命周期安全。
    /// </summary>
    public static bool PinCreatedObjects = true;

    /// <summary>最近一次失败原因；成功时为 null。</summary>
    public static string LastError { get; private set; }

    static readonly List<IntPtr> RootHandles = new List<IntPtr>();

    /// <summary>本工具创建的纹理 → (w&lt;&lt;32)|h。避免为了读尺寸去 invoke 未验证的 get_width/get_height。</summary>
    static readonly Dictionary<IntPtr, long> CreatedSizes = new Dictionary<IntPtr, long>();

    // ---------------------------------------------------------------- 缓存

    static IntPtr _clsTexture2D, _clsImageConversion, _clsSprite, _clsByte, _clsColor;
    static IntPtr _ctorTexture2D4, _ctorTexture2D2;
    static IntPtr _mLoadImage;                 // 仅供诊断打印，禁止 invoke
    static IntPtr _mSetPixelsImpl, _mApplyImpl, _mSpriteCreate;

    // 裸调 ICall 用的函数指针（resolve 一次缓存）
    static IntPtr _icallLoadImage;
    static bool _icallLoadImageProbed;

    static long Pack(int w, int h) => ((long)w << 32) | (uint)h;

    static void L(string msg)
    {
        try { Log?.Invoke(msg); } catch { /* 日志失败绝不能影响主流程 */ }
    }

    // ================================================================
    //  公共 API（签名固定，MiniLoader 移植按此编码）
    // ================================================================

    /// <summary>创建一个 RGBA32、无 mipmap 的 Texture2D，返回原生对象指针。</summary>
    public static IntPtr CreateTexture2D(int w, int h)
        => CreateTexture2D(w, h, TextureFormatRGBA32, false);

    /// <summary>完整参数的创建入口（textureFormat 为 UnityEngine.TextureFormat 的整数值）。</summary>
    public static IntPtr CreateTexture2D(int w, int h, int textureFormat, bool mipChain)
    {
        LastError = null;
        if (w <= 0 || h <= 0) { LastError = "尺寸非法: " + w + "x" + h; return IntPtr.Zero; }

        var cls = GetTexture2DClass();
        if (cls == IntPtr.Zero) { LastError = "找不到 Texture2D 类（" + CoreModule + "）"; return IntPtr.Zero; }

        EnsureTextureCtors(cls);
        if (_ctorTexture2D4 == IntPtr.Zero && _ctorTexture2D2 == IntPtr.Zero)
        {
            LastError = "找不到 Texture2D 构造函数（.ctor/4 与 .ctor/2 均为 0）";
            return IntPtr.Zero;
        }

        var obj = IL2CPP.il2cpp_object_new(cls);
        if (obj == IntPtr.Zero) { LastError = "il2cpp_object_new 返回 0"; return IntPtr.Zero; }

        unsafe
        {
            int width = w, height = h, fmt = textureFormat;
            bool mip = mipChain;
            IntPtr exc = IntPtr.Zero;

            if (_ctorTexture2D4 != IntPtr.Zero)
            {
                var args = stackalloc IntPtr[4];
                args[0] = (IntPtr)(&width);
                args[1] = (IntPtr)(&height);
                args[2] = (IntPtr)(&fmt);
                args[3] = (IntPtr)(&mip);
                _ = IL2CPP.il2cpp_runtime_invoke(_ctorTexture2D4, obj, (void**)args, ref exc);
            }
            else
            {
                // 兜底：只有 .ctor(int,int) 时用它（默认 RGBA32 + 无 mipmap）
                L("CreateTexture2D: .ctor/4 缺失，退化使用 .ctor/2");
                var args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&width);
                args[1] = (IntPtr)(&height);
                _ = IL2CPP.il2cpp_runtime_invoke(_ctorTexture2D2, obj, (void**)args, ref exc);
            }

            if (exc != IntPtr.Zero)
            {
                LastError = "Texture2D..ctor 抛异常: " + DescribeException(exc);
                return IntPtr.Zero;
            }
        }

        Root(obj);
        CreatedSizes[obj] = Pack(w, h);
        L("CreateTexture2D(" + w + ", " + h + ", fmt=" + textureFormat + ", mip=" + mipChain + ") -> 0x" + Hex(obj));
        return obj;
    }

    /// <summary>
    /// 把编码图片（PNG）加载进纹理：<b>托管侧自解码</b> → 原生 <c>Texture2D.SetPixelsImpl</c> 上传 → <c>ApplyImpl</c>。
    /// 不再调用 ImageConversion.LoadImage（其内部 ICall 被裁剪，会 SIGABRT）。
    /// 注意：自解码路径<b>不能改变纹理尺寸</b>，图片尺寸必须与纹理一致；否则返回 false 并说明两者尺寸。
    /// 需要按图片尺寸新建纹理时用 <see cref="CreateTexture2DFromImage"/>。
    /// </summary>
    public static bool LoadImage(IntPtr tex, byte[] data)
    {
        LastError = null;
        if (tex == IntPtr.Zero) { LastError = "纹理指针为 0"; return false; }
        if (data == null || data.Length == 0) { LastError = "图片数据为空"; return false; }

        // ---------- 主路径：裸调原生 ICall ImageConversion::LoadImage ----------
        // 真机已实证：托管包装会 SIGABRT，但 resolve 出来的原生实现可以直接按指针调用。
        // 优点：PNG/JPG 都能解，内存只占原图字节（不像 Color[] 路径要 16B/像素）。
        string nativeErr;
        if (TryLoadImageNative(tex, data, out nativeErr))
        {
            // 原生 LoadImage 会按图片尺寸重建纹理（可能 Resize），登记表要跟着更新，
            // 否则后续 SetPixels32 的尺寸校验会用到过期尺寸。只解析文件头，不重解码。
            if (TryReadImageSize(data, out int nw, out int nh)) CreatedSizes[tex] = Pack(nw, nh);
            else CreatedSizes.Remove(tex);
            L("LoadImage 成功（原生 ICall 裸调）: 0x" + Hex(tex) + " <- " + data.Length + " 字节");
            return true;
        }

        L("原生 LoadImage 不可用/失败（" + nativeErr + "）→ 回退到自解码 + SetPixelsImpl");
        return LoadImageFallback(tex, data);
    }

    /// <summary>fallback：托管自解码 + SetPixelsImpl + ApplyImpl（不能 Resize，尺寸必须一致）。</summary>
    static bool LoadImageFallback(IntPtr tex, byte[] data)
    {
        L("  L1 自解码开始 bytes=" + data.Length);
        if (!TryDecodeImage(data, out int iw, out int ih, out byte[] rgba)) return false;
        L("  L2 解码成功 " + iw + "x" + ih + " RGBA=" + rgba.Length + " 字节");

        if (GetTextureSize(tex, out int tw, out int th))
        {
            if (tw != iw || th != ih)
            {
                LastError = "纹理尺寸 " + tw + "x" + th + " 与图片尺寸 " + iw + "x" + ih
                            + " 不一致；像素上传路径无法自动 Resize，请先按图片尺寸 CreateTexture2D（或用 CreateTexture2DFromImage）";
                L("LoadImage 失败: " + LastError);
                return false;
            }
        }
        else
        {
            L("提示: 0x" + Hex(tex) + " 不是本工具创建的纹理，跳过尺寸校验（按图片 " + iw + "x" + ih + " 上传）");
        }

        // L3/L4 的 invoke 追踪在 SetPixels32 / Apply 里打印
        if (!SetPixels32(tex, rgba, iw, ih, true)) return false;
        if (!Apply(tex, true, false)) return false;
        L("LoadImage 成功: 0x" + Hex(tex) + " <- " + iw + "x" + ih + "（自解码 + SetPixelsImpl + ApplyImpl）");
        return true;
    }

    /// <summary>
    /// 裸调原生 <c>UnityEngine.ImageConversion::LoadImage(Texture2D, byte[], bool markNonReadable, MethodInfo*)</c>。
    /// 用 <c>il2cpp_resolve_icall</c> 拿函数指针 + <c>delegate* unmanaged[Cdecl]</c> 直调（真机实证可行、不崩）。
    /// </summary>
    static bool TryLoadImageNative(IntPtr tex, byte[] data, out string err)
    {
        err = null;
        var fn = GetNativeLoadImage();
        if (fn == IntPtr.Zero)
        {
            err = "ICall \"UnityEngine.ImageConversion::LoadImage\" 未注册";
            return false;
        }

        var arr = NewIl2CppByteArray(data, out string arrErr);
        if (arr == IntPtr.Zero) { err = "构造 IL2CPP byte[] 失败: " + arrErr; return false; }

        var handle = IL2CPP.il2cpp_gchandle_new(arr, false);
        try
        {
            unsafe
            {
                L("  N1 裸调 ImageConversion::LoadImage fn=0x" + Hex(fn) + " tex=0x" + Hex(tex)
                  + " bytes=" + data.Length + " arr=0x" + Hex(arr));
                var del = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte, IntPtr, byte>)fn;
                byte ok = del(tex, arr, 0, IntPtr.Zero);   // markNonReadable=0, MethodInfo*=0
                L("  N2 裸调返回 " + ok);
                if (ok == 0)
                {
                    err = "原生 LoadImage 返回 0（Unity 认为这不是合法图片数据）";
                    return false;
                }
                return true;
            }
        }
        finally
        {
            if (handle != IntPtr.Zero) IL2CPP.il2cpp_gchandle_free(handle);
        }
    }

    static IntPtr GetNativeLoadImage()
    {
        if (!_icallLoadImageProbed)
        {
            _icallLoadImageProbed = true;
            _icallLoadImage = ResolveIcall("UnityEngine.ImageConversion::LoadImage");
            if (_icallLoadImage == IntPtr.Zero)
                L("警告: ICall \"UnityEngine.ImageConversion::LoadImage\" 解析为 0，将只走自解码 fallback");
        }
        return _icallLoadImage;
    }

    /// <summary>
    /// 只解析图片头部拿尺寸（PNG 的 IHDR），不解压像素 —— 用于原生 LoadImage 之后刷新尺寸登记表。
    /// 非 PNG 或头不完整时返回 false（调用方会把登记表标记为未知，后续跳过尺寸校验）。
    /// </summary>
    public static bool TryReadImageSize(byte[] data, out int width, out int height)
    {
        width = 0; height = 0;
        if (data == null || data.Length < 24) return false;
        if (data[0] != 0x89 || data[1] != 0x50 || data[2] != 0x4E || data[3] != 0x47) return false;
        if (data[12] != 'I' || data[13] != 'H' || data[14] != 'D' || data[15] != 'R') return false;
        width = ReadBE32(data, 16);
        height = ReadBE32(data, 20);
        return width > 0 && height > 0;
    }

    /// <summary>
    /// 上传原始 GPU 纹理字节（UnityEngine.Texture2D.LoadRawTextureData 语义）。
    /// 本作 libil2cpp 未注册 <c>Texture2D::LoadRawTextureDataImpl</c> / <c>LoadRawTextureDataImplArray</c> /
    /// <c>SetPixelDataImplArray</c>，因此恒返回 false 并设置 LastError —— <b>不崩溃、不静默</b>。
    /// 原始 DXT5 等 GPU 字节目前无解（8192x8192 DXT5 图集走不了这条路）。
    /// </summary>
    public static bool LoadRawTextureData(IntPtr tex, byte[] data)
    {
        LastError = null;
        if (tex == IntPtr.Zero) { LastError = "纹理指针为 0"; return false; }
        if (data == null || data.Length == 0) { LastError = "原始数据为空"; return false; }

        LastError = "纹理上传 ICall 被引擎裁剪";
        L("LoadRawTextureData(0x" + Hex(tex) + ", " + data.Length + " 字节) -> false: " + LastError
          + "（libil2cpp 未注册 Texture2D::LoadRawTextureDataImpl/ImplArray/SetPixelDataImplArray）");
        return false;
    }

    /// <summary>
    /// 把 RGBA32 像素（每像素 4 字节 R,G,B,A）上传到纹理：原生 <c>Texture2D.SetPixelsImpl</c>（已注册 ICall）。
    /// 像素尺寸必须与纹理当前尺寸完全一致（本路径不能 Resize）。
    /// <paramref name="flipRows"/> = true 时把输入第 0 行当作图片顶行，翻成 Unity 的自下而上顺序（LoadImage 走这个）。
    /// 注意：SetPixelsImpl 收的是 <c>Color[]</c>（16 字节/像素），不是 Color32[]，故峰值内存 = 像素数 x 16B。
    /// </summary>
    public static bool SetPixels32(IntPtr tex, byte[] rgba32, int width, int height, bool flipRows)
    {
        LastError = null;
        if (tex == IntPtr.Zero) { LastError = "纹理指针为 0"; return false; }
        if (rgba32 == null || width <= 0 || height <= 0) { LastError = "像素数据或尺寸非法"; return false; }
        if (rgba32.Length != width * height * 4)
        {
            LastError = "像素字节数 " + rgba32.Length + " != " + width + "x" + height + "x4";
            return false;
        }

        if (GetTextureSize(tex, out int tw, out int th))
        {
            if (tw != width || th != height)
            {
                LastError = "像素块 " + width + "x" + height + " 与纹理 " + tw + "x" + th + " 不一致（当前只支持整图上传）";
                return false;
            }
        }
        else
        {
            L("提示: 0x" + Hex(tex) + " 不是本工具创建的纹理，跳过尺寸校验，按传入的 " + width + "x" + height + " 上传");
        }

        var cls = GetTexture2DClass();
        if (cls == IntPtr.Zero) { LastError = "找不到 Texture2D 类"; return false; }
        var method = GetSetPixelsImpl(cls);
        if (method == IntPtr.Zero) { LastError = "找不到 Texture2D.SetPixelsImpl/7"; return false; }

        L("  L3 构造 Color[" + (width * height) + "]（每元素 16 字节）");
        string arrErr;
        var colors = NewColorArray(width * height, out arrErr);
        if (colors == IntPtr.Zero) { LastError = "构造 Color[] 失败: " + arrErr; return false; }

        FillColorArray(colors, rgba32, width, height, flipRows);
        L("  L4 即将 il2cpp_runtime_invoke(SetPixelsImpl) tex=0x" + Hex(tex) + " colors=0x" + Hex(colors)
          + " 方法=" + DescribeMethod(method));

        var handle = IL2CPP.il2cpp_gchandle_new(colors, false);
        try
        {
            unsafe
            {
                IntPtr colorsLocal = colors;
                int x = 0, y = 0, w = width, h = height, miplevel = 0, frame = 0;
                var args = stackalloc IntPtr[7];
                args[0] = (IntPtr)(&x);
                args[1] = (IntPtr)(&y);
                args[2] = (IntPtr)(&w);
                args[3] = (IntPtr)(&h);
                args[4] = (IntPtr)(&colorsLocal);   // 引用类型：指向"保存对象指针的槽"
                args[5] = (IntPtr)(&miplevel);
                args[6] = (IntPtr)(&frame);

                IntPtr exc = IntPtr.Zero;
                _ = IL2CPP.il2cpp_runtime_invoke(method, tex, (void**)args, ref exc);   // 实例方法：obj = 纹理
                L("  L5 SetPixelsImpl 返回 exc=0x" + Hex(exc));
                if (exc != IntPtr.Zero)
                {
                    LastError = "SetPixelsImpl 抛异常: " + DescribeException(exc);
                    return false;
                }
            }
        }
        finally
        {
            if (handle != IntPtr.Zero) IL2CPP.il2cpp_gchandle_free(handle);
        }

        L("SetPixels32(tex=0x" + Hex(tex) + ", " + width + "x" + height + ", flipRows=" + flipRows + ") -> ok");
        return true;
    }

    /// <summary>原生 <c>Texture2D.ApplyImpl(updateMipmaps, makeNoLongerReadable)</c>：把 CPU 像素真正提交到 GPU。</summary>
    public static bool Apply(IntPtr tex, bool updateMipmaps, bool makeNoLongerReadable)
    {
        LastError = null;
        if (tex == IntPtr.Zero) { LastError = "纹理指针为 0"; return false; }

        var cls = GetTexture2DClass();
        if (cls == IntPtr.Zero) { LastError = "找不到 Texture2D 类"; return false; }
        var method = GetApplyImpl(cls);
        if (method == IntPtr.Zero) { LastError = "找不到 Texture2D.ApplyImpl/2"; return false; }

        L("  L6 即将 il2cpp_runtime_invoke(ApplyImpl) tex=0x" + Hex(tex) + " 方法=" + DescribeMethod(method));
        unsafe
        {
            bool up = updateMipmaps, noLongerReadable = makeNoLongerReadable;
            var args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&up);
            args[1] = (IntPtr)(&noLongerReadable);

            IntPtr exc = IntPtr.Zero;
            _ = IL2CPP.il2cpp_runtime_invoke(method, tex, (void**)args, ref exc);
            L("  L7 ApplyImpl 返回 exc=0x" + Hex(exc));
            if (exc != IntPtr.Zero)
            {
                LastError = "ApplyImpl 抛异常: " + DescribeException(exc);
                return false;
            }
        }

        L("Apply(tex=0x" + Hex(tex) + ", updateMipmaps=" + updateMipmaps + ", makeNoLongerReadable=" + makeNoLongerReadable + ") -> ok");
        return true;
    }

    /// <summary>解码图片为 RGBA32 像素（每像素 R,G,B,A，<b>行序为图片顶行在前</b>）。当前实现支持 PNG。</summary>
    public static bool TryDecodeImage(byte[] data, out int width, out int height, out byte[] rgba32)
    {
        width = 0; height = 0; rgba32 = null;
        LastError = null;
        if (data == null || data.Length < 8) { LastError = "图片数据为空或过短"; return false; }

        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return DecodePng(data, out width, out height, out rgba32);

        LastError = "只实现了 PNG 自解码（JPEG 暂不支持）";
        return false;
    }

    /// <summary>解码 + 按图片尺寸新建纹理 + 上传 + Apply，返回纹理指针（失败返回 0，原因见 LastError）。</summary>
    public static IntPtr CreateTexture2DFromImage(byte[] data)
    {
        LastError = null;
        if (!TryDecodeImage(data, out int w, out int h, out byte[] rgba)) return IntPtr.Zero;

        var tex = CreateTexture2D(w, h);
        if (tex == IntPtr.Zero) return IntPtr.Zero;

        if (!SetPixels32(tex, rgba, w, h, true)) return IntPtr.Zero;
        if (!Apply(tex, true, false)) return IntPtr.Zero;

        L("CreateTexture2DFromImage -> 0x" + Hex(tex) + " (" + w + "x" + h + ")");
        return tex;
    }

    /// <summary>
    /// 用纹理的 (x, y, w, h) 像素区域建一个 Sprite（pivot 取 0.5,0.5），返回原生 Sprite 指针。
    /// 等价于 <c>Sprite.Create(tex, new Rect(x, y, w, h), new Vector2(0.5f, 0.5f))</c>。
    /// </summary>
    public static IntPtr CreateSprite(IntPtr tex, float x, float y, float w, float h)
    {
        LastError = null;
        if (tex == IntPtr.Zero) { LastError = "纹理指针为 0"; return IntPtr.Zero; }

        // 真机实证：Sprite 创建是硬墙 —— Sprite::Create / CreateSpriteWithoutTextureScripting /
        // Internal_CreateSprite / CreateWithTexture 等 ICall 全部未注册；托管 Sprite.Create 内部必然转调它们，
        // 一旦 invoke 就是 SIGABRT。所以这里【只报告、不调用】，签名保持不变供上层优雅降级。
        // 仍然把解析结果打出来（只读元数据，安全），方便后人确认墙还在。
        var cls = GetSpriteClass();
        var method = cls == IntPtr.Zero ? IntPtr.Zero : GetSpriteCreateMethod(cls);
        LastError = "Sprite 创建 ICall 被引擎裁剪（Create/CreateSpriteWithoutTextureScripting/Internal_CreateSprite 全无注册），已优雅跳过";
        L("CreateSprite 跳过: " + LastError
          + "  [tex=0x" + Hex(tex) + " rect=(" + x + "," + y + "," + w + "," + h + ")"
          + " Sprite类=0x" + Hex(cls) + " Create方法=0x" + Hex(method) + "]");
        return IntPtr.Zero;
    }

    /// <summary>
    /// 把原生指针包装成托管 interop 对象（如 "UnityEngine.Sprite, UnityEngine.CoreModule"）。
    /// 严禁用于 Texture2D —— 托管 Texture2D 类型解析会 SIGABRT，这里直接拒绝。
    /// </summary>
    public static object Wrap(IntPtr ptr, string assembly, string ns, string type)
    {
        LastError = null;
        if (ptr == IntPtr.Zero) return null;
        if (string.IsNullOrEmpty(type)) { LastError = "类型名为空"; return null; }
        if (string.Equals(type, "Texture2D", StringComparison.Ordinal))
        {
            LastError = "拒绝包装 Texture2D：托管类型解析会触发 Mono 缺陷（SIGABRT）";
            L("Wrap 拒绝: " + LastError);
            return null;
        }

        try
        {
            string full = (string.IsNullOrEmpty(ns) ? type : ns + "." + type) + ", " + assembly;
            var t = Type.GetType(full, throwOnError: false);
            if (t == null) { LastError = "找不到托管类型: " + full; return null; }
            if (t.FullName == "UnityEngine.Texture2D") { LastError = "拒绝包装 Texture2D"; return null; }
            return Activator.CreateInstance(t, new object[] { ptr });
        }
        catch (Exception e)
        {
            LastError = "Wrap 失败: " + e.GetType().Name + ": " + e.Message;
            return null;
        }
    }

    // ================================================================
    //  ICall 探测（只解析不调用，因此对已裁剪的 ICall 也安全）
    // ================================================================

    /// <summary>解析一条 IL2CPP ICall 的函数指针（0 = 该 ICall 未注册）。只解析，不会调用。</summary>
    public static IntPtr ResolveIcall(string name)
    {
        LastError = null;
        if (string.IsNullOrEmpty(name)) { LastError = "ICall 签名为空"; return IntPtr.Zero; }
        try { return IL2CPP.il2cpp_resolve_icall(name); }
        catch (Exception e)
        {
            LastError = "il2cpp_resolve_icall 抛出: " + e.GetType().Name + ": " + e.Message;
            return IntPtr.Zero;
        }
    }

    /// <summary>逐个尝试多个候选签名，返回多行报告（命中的给出指针）。仅用于日志。</summary>
    public static string ProbeIcalls(params string[] candidates)
    {
        var sb = new StringBuilder();
        if (candidates == null) return sb.ToString();
        foreach (var c in candidates)
        {
            IntPtr p;
            try { p = IL2CPP.il2cpp_resolve_icall(c); } catch { p = IntPtr.Zero; }
            sb.Append(p != IntPtr.Zero ? "[有] 0x" + Hex(p) : "[无]            ").Append("  ").AppendLine(c);
        }
        return sb.ToString();
    }

    // ================================================================
    //  诊断辅助（附加 API，不影响固定签名）
    // ================================================================

    /// <summary>把 IL2CPP 异常指针格式化成可读文本。</summary>
    public static string DescribeException(IntPtr exc)
    {
        if (exc == IntPtr.Zero) return "<null>";
        unsafe
        {
            const int Size = 1024;
            byte* buf = stackalloc byte[Size];
            buf[0] = 0;
            IL2CPP.il2cpp_format_exception(exc, buf, Size - 1);
            var s = Marshal.PtrToStringAnsi((IntPtr)buf);
            return string.IsNullOrEmpty(s) ? ("0x" + Hex(exc)) : s;
        }
    }

    /// <summary>打印一个 MethodInfo 的签名（名称 + 参数类型），用于确认真机上取到的是哪个重载。</summary>
    public static string DescribeMethod(IntPtr method)
    {
        if (method == IntPtr.Zero) return "<null>";
        unsafe
        {
            var name = Marshal.PtrToStringAnsi(IL2CPP.il2cpp_method_get_name(method)) ?? "?";
            int pc = (int)IL2CPP.il2cpp_method_get_param_count(method);
            var sb = new StringBuilder();
            sb.Append(name).Append('(');
            for (int i = 0; i < pc; i++)
            {
                if (i > 0) sb.Append(", ");
                var pt = IL2CPP.il2cpp_method_get_param(method, (uint)i);
                sb.Append(pt == IntPtr.Zero ? "?" : (Marshal.PtrToStringAnsi(IL2CPP.il2cpp_type_get_name(pt)) ?? "?"));
            }
            return sb.Append(')').ToString();
        }
    }

    /// <summary>
    /// 读取原生对象的 int 属性（返回值是装箱对象，需要 unbox）。
    /// ⚠️ 只对**已确认为已注册 ICall** 的属性安全，目前真机验证过的只有 <c>get_isReadable</c>。
    /// 不要用它读 width/height：那两个属性带托管方法体，内部调的 ICall 可能已被裁剪 → SIGABRT。
    /// </summary>
    public static bool TryGetIntProp(IntPtr obj, string prop, out int value)
    {
        value = 0;
        if (obj == IntPtr.Zero) { LastError = "对象指针为 0"; return false; }
        var cls = IL2CPP.il2cpp_object_get_class(obj);
        var m = FindInHierarchy(cls, "get_" + prop, 0);
        if (m == IntPtr.Zero) { LastError = "找不到属性 get_" + prop; return false; }
        return InvokeInt(m, obj, out value);
    }

    /// <summary>
    /// 读取纹理尺寸：**只认本工具 <see cref="CreateTexture2D"/> 创建并登记过的纹理**（指针查表，零原生调用）。
    ///
    /// 为什么不去 invoke get_width/get_height：这两个属性带托管方法体（内部再调 Texture::GetDataWidth /
    /// GetDataHeight ICall），而本作引擎裁剪过 ICall 表 —— 与 ImageConversion.LoadImage 同类的 SIGABRT 风险。
    /// 真机只验证过 <c>get_isReadable</c>（它本身是已注册 ICall，可安全 invoke），width/height 未验证，一律不碰。
    /// </summary>
    public static bool GetTextureSize(IntPtr tex, out int width, out int height)
    {
        width = 0; height = 0;
        if (tex == IntPtr.Zero) return false;
        if (!CreatedSizes.TryGetValue(tex, out long packed)) return false;
        width = (int)(packed >> 32);
        height = (int)(packed & 0xFFFFFFFF);
        return true;
    }

    /// <summary>一键解析所有需要的类/方法并返回多行报告，便于真机日志确认。</summary>
    public static string Describe()
    {
        var sb = new StringBuilder();
        try
        {
            var tex = GetTexture2DClass();
            EnsureTextureCtors(tex);
            GetSetPixelsImpl(tex);
            GetApplyImpl(tex);
            var img = GetImageConversionClass();
            GetLoadImageMethod(img);           // 仅为了打印，确认它存在但不可用
            var spr = GetSpriteClass();
            GetSpriteCreateMethod(spr);
            var byt = GetByteClass();
            var col = GetColorClass();

            sb.AppendLine("Texture2D 类        = 0x" + Hex(tex) + "  (" + CoreModule + ")");
            sb.AppendLine("  .ctor/4           = 0x" + Hex(_ctorTexture2D4) + "  " + DescribeMethod(_ctorTexture2D4));
            sb.AppendLine("  .ctor/2           = 0x" + Hex(_ctorTexture2D2) + "  " + DescribeMethod(_ctorTexture2D2));
            sb.AppendLine("  SetPixelsImpl     = 0x" + Hex(_mSetPixelsImpl) + "  " + DescribeMethod(_mSetPixelsImpl) + "   <- 上传通道");
            sb.AppendLine("  ApplyImpl         = 0x" + Hex(_mApplyImpl) + "  " + DescribeMethod(_mApplyImpl) + "   <- 提交 GPU");
            sb.AppendLine("ImageConversion 类  = 0x" + Hex(img) + "  (" + ImageConversionModule + ")");
            sb.AppendLine("  托管 LoadImage     = 0x" + Hex(_mLoadImage) + "  " + DescribeMethod(_mLoadImage) + "   <- 禁用！托管包装内部解析 ICall，invoke 即 SIGABRT");
            sb.AppendLine("  原生 ICall LoadImage = 0x" + Hex(GetNativeLoadImage()) + "   <- 走这个（裸调函数指针）");
            sb.AppendLine("Sprite 类           = 0x" + Hex(spr) + "  (" + CoreModule + ")");
            sb.AppendLine("  Create            = 0x" + Hex(_mSpriteCreate) + "  " + DescribeMethod(_mSpriteCreate) + "   <- 禁用！Sprite 创建 ICall 全缺，invoke 即 SIGABRT");
            sb.AppendLine("System.Byte 类      = 0x" + Hex(byt) + "  (" + MscorlibModule + ")");
            sb.AppendLine("UnityEngine.Color   = 0x" + Hex(col) + "  数组元素大小 = "
                          + (col == IntPtr.Zero ? -1 : IL2CPP.il2cpp_class_array_element_size(col)) + " 字节（期望 16）");
            sb.Append("数组数据偏移        = " + (4 * IntPtr.Size) + " 字节 (4 * IntPtr.Size)");
        }
        catch (Exception e)
        {
            sb.Append("Describe 失败: ").Append(e);
        }
        return sb.ToString();
    }

    /// <summary>读 IL2CPP 数组的长度（元素个数）。</summary>
    public static uint ArrayLength(IntPtr arr) => arr == IntPtr.Zero ? 0u : IL2CPP.il2cpp_array_length(arr);

    /// <summary>取 IL2CPP 数组的数据区起始指针（偏移 4 * IntPtr.Size）。</summary>
    public static IntPtr ArrayData(IntPtr arr)
    {
        if (arr == IntPtr.Zero) return IntPtr.Zero;
        unsafe { return (IntPtr)((byte*)arr + 4 * IntPtr.Size); }
    }

    /// <summary>在原生对象上加一个强 GC 句柄，避免只持裸指针时被 GC 回收。</summary>
    public static void Root(IntPtr obj)
    {
        if (!PinCreatedObjects || obj == IntPtr.Zero) return;
        try
        {
            var handle = IL2CPP.il2cpp_gchandle_new(obj, false);
            if (handle != IntPtr.Zero) RootHandles.Add(handle);
        }
        catch (Exception e)
        {
            L("Root(0x" + Hex(obj) + ") 失败（忽略）: " + e.Message);
        }
    }

    /// <summary>ImageConversion 类（仅用于 EncodeToPNG 之类的原生辅助，LoadImage 已禁用）。</summary>
    public static IntPtr GetImageConversionClass()
    {
        if (_clsImageConversion == IntPtr.Zero) _clsImageConversion = ResolveClass(ImageConversionModule, "UnityEngine", "ImageConversion");
        return _clsImageConversion;
    }

    // ================================================================
    //  内部实现
    // ================================================================

    static string Hex(IntPtr p) => p.ToInt64().ToString("X");

    static IntPtr GetTexture2DClass()
    {
        if (_clsTexture2D == IntPtr.Zero) _clsTexture2D = ResolveClass(CoreModule, "UnityEngine", "Texture2D");
        return _clsTexture2D;
    }

    static IntPtr GetSpriteClass()
    {
        if (_clsSprite == IntPtr.Zero) _clsSprite = ResolveClass(CoreModule, "UnityEngine", "Sprite");
        return _clsSprite;
    }

    static IntPtr GetByteClass()
    {
        if (_clsByte == IntPtr.Zero) _clsByte = ResolveClass(MscorlibModule, "System", "Byte");
        return _clsByte;
    }

    static IntPtr GetColorClass()
    {
        if (_clsColor == IntPtr.Zero) _clsColor = ResolveClass(CoreModule, "UnityEngine", "Color");
        return _clsColor;
    }

    static void EnsureTextureCtors(IntPtr cls)
    {
        if (cls == IntPtr.Zero) return;
        if (_ctorTexture2D4 == IntPtr.Zero) _ctorTexture2D4 = IL2CPP.il2cpp_class_get_method_from_name(cls, ".ctor", 4);
        if (_ctorTexture2D2 == IntPtr.Zero) _ctorTexture2D2 = IL2CPP.il2cpp_class_get_method_from_name(cls, ".ctor", 2);
    }

    /// <summary>仅诊断用：确认 ImageConversion.LoadImage 存在。<b>禁止调用</b>（内部 ICall 被裁剪）。</summary>
    static IntPtr GetLoadImageMethod(IntPtr cls)
    {
        if (_mLoadImage != IntPtr.Zero || cls == IntPtr.Zero) return _mLoadImage;
        _mLoadImage = IL2CPP.il2cpp_class_get_method_from_name(cls, "LoadImage", 3);
        if (_mLoadImage == IntPtr.Zero) _mLoadImage = IL2CPP.il2cpp_class_get_method_from_name(cls, "LoadImage", 2);
        return _mLoadImage;
    }

    static IntPtr GetSetPixelsImpl(IntPtr cls)
    {
        if (_mSetPixelsImpl != IntPtr.Zero || cls == IntPtr.Zero) return _mSetPixelsImpl;
        // 7 个参数：(x, y, w, h, Color[] pixel, miplevel, frame)；第 5 个参数类型必须是 Color，防重载歧义
        _mSetPixelsImpl = FindMethodByParamTypeName(cls, "SetPixelsImpl", 7, 4, "Color");
        if (_mSetPixelsImpl == IntPtr.Zero)
            _mSetPixelsImpl = IL2CPP.il2cpp_class_get_method_from_name(cls, "SetPixelsImpl", 7);
        if (_mSetPixelsImpl == IntPtr.Zero) L("警告: 找不到 Texture2D.SetPixelsImpl/7");
        return _mSetPixelsImpl;
    }

    static IntPtr GetApplyImpl(IntPtr cls)
    {
        if (_mApplyImpl != IntPtr.Zero || cls == IntPtr.Zero) return _mApplyImpl;
        _mApplyImpl = IL2CPP.il2cpp_class_get_method_from_name(cls, "ApplyImpl", 2);
        if (_mApplyImpl == IntPtr.Zero) L("警告: 找不到 Texture2D.ApplyImpl/2");
        return _mApplyImpl;
    }

    static IntPtr GetSpriteCreateMethod(IntPtr cls)
    {
        if (_mSpriteCreate != IntPtr.Zero || cls == IntPtr.Zero) return _mSpriteCreate;
        // 3 参数里同时存在 Create(Rect,Vector2,float)，必须校验第一个参数是 Texture2D
        _mSpriteCreate = FindMethodByParamTypeName(cls, "Create", 3, 0, "Texture2D");
        if (_mSpriteCreate == IntPtr.Zero)
        {
            L("Sprite.Create/3(Texture2D,...) 未找到，退化为 Create/8");
            _mSpriteCreate = IL2CPP.il2cpp_class_get_method_from_name(cls, "Create", 8);
        }
        return _mSpriteCreate;
    }

    /// <summary>按 名称 + 参数个数 + 指定下标参数的类型名包含子串 精确挑方法（重载消歧）。</summary>
    static IntPtr FindMethodByParamTypeName(IntPtr cls, string name, int paramCount, int paramIndex, string typeNameContains)
    {
        if (cls == IntPtr.Zero) return IntPtr.Zero;
        unsafe
        {
            IntPtr iter = IntPtr.Zero;
            IntPtr m;
            while ((m = IL2CPP.il2cpp_class_get_methods(cls, ref iter)) != IntPtr.Zero)
            {
                var mn = Marshal.PtrToStringAnsi(IL2CPP.il2cpp_method_get_name(m));
                if (mn != name) continue;
                if ((int)IL2CPP.il2cpp_method_get_param_count(m) != paramCount) continue;
                var pt = IL2CPP.il2cpp_method_get_param(m, (uint)paramIndex);
                var tn = pt == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(IL2CPP.il2cpp_type_get_name(pt));
                if (tn != null && tn.IndexOf(typeNameContains, StringComparison.Ordinal) >= 0) return m;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>沿父类链找方法（Texture.width/height 定义在 UnityEngine.Texture 上）。</summary>
    static IntPtr FindInHierarchy(IntPtr cls, string name, int argc)
    {
        for (var c = cls; c != IntPtr.Zero; c = IL2CPP.il2cpp_class_get_parent(c))
        {
            var m = IL2CPP.il2cpp_class_get_method_from_name(c, name, argc);
            if (m != IntPtr.Zero) return m;
        }
        return IntPtr.Zero;
    }

    /// <summary>调用返回 int 的原生方法（值类型返回值是装箱对象，需要 unbox）。</summary>
    static bool InvokeInt(IntPtr method, IntPtr obj, out int value)
    {
        value = 0;
        unsafe
        {
            IntPtr exc = IntPtr.Zero;
            var res = IL2CPP.il2cpp_runtime_invoke(method, obj, null, ref exc);
            if (exc != IntPtr.Zero) { LastError = "调用抛异常: " + DescribeException(exc); return false; }
            if (res == IntPtr.Zero) { LastError = "返回 0"; return false; }
            var p = IL2CPP.il2cpp_object_unbox(res);
            if (p == IntPtr.Zero) { LastError = "unbox 返回 0"; return false; }
            value = *(int*)p;
            return true;
        }
    }

    /// <summary>构造 IL2CPP System.Byte[] 并写入托管字节（保留给未来的 ICall 通道使用）。</summary>
    static IntPtr NewIl2CppByteArray(byte[] data, out string err)
    {
        err = null;
        var byteCls = GetByteClass();
        if (byteCls == IntPtr.Zero) { err = "找不到 System.Byte 类"; return IntPtr.Zero; }

        var arr = IL2CPP.il2cpp_array_new(byteCls, (ulong)data.Length);
        if (arr == IntPtr.Zero)
        {
            var arrayCls = IL2CPP.il2cpp_array_class_get(byteCls, 1);
            if (arrayCls != IntPtr.Zero) arr = IL2CPP.il2cpp_array_new_specific(arrayCls, (ulong)data.Length);
        }
        if (arr == IntPtr.Zero) { err = "il2cpp_array_new 返回 0"; return IntPtr.Zero; }

        unsafe
        {
            byte* dst = (byte*)arr + 4 * IntPtr.Size;
            Marshal.Copy(data, 0, (IntPtr)dst, data.Length);
        }

        uint len = IL2CPP.il2cpp_array_length(arr);
        uint byteLen = IL2CPP.il2cpp_array_get_byte_length(arr);
        if (len != (uint)data.Length || byteLen != (uint)data.Length)
        {
            err = "IL2CPP byte[] 自检失败: length=" + len + " byteLength=" + byteLen + " 期望=" + data.Length;
            return IntPtr.Zero;
        }
        return arr;
    }

    /// <summary>构造 IL2CPP UnityEngine.Color[]（每元素 16 字节 = 4 个 float）。</summary>
    static IntPtr NewColorArray(int pixelCount, out string err)
    {
        err = null;
        var colorCls = GetColorClass();
        if (colorCls == IntPtr.Zero) { err = "找不到 UnityEngine.Color 类"; return IntPtr.Zero; }

        int elemSize = IL2CPP.il2cpp_class_array_element_size(colorCls);
        if (elemSize != 16) { err = "Color 数组元素大小 = " + elemSize + "，期望 16"; return IntPtr.Zero; }

        var arr = IL2CPP.il2cpp_array_new(colorCls, (ulong)pixelCount);
        if (arr == IntPtr.Zero)
        {
            var arrayCls = IL2CPP.il2cpp_array_class_get(colorCls, 1);
            if (arrayCls != IntPtr.Zero) arr = IL2CPP.il2cpp_array_new_specific(arrayCls, (ulong)pixelCount);
        }
        if (arr == IntPtr.Zero) { err = "il2cpp_array_new 返回 0"; return IntPtr.Zero; }

        uint byteLen = IL2CPP.il2cpp_array_get_byte_length(arr);
        if (byteLen != (uint)(pixelCount * 16))
        {
            err = "Color[] 自检失败: byteLength=" + byteLen + " 期望=" + pixelCount * 16;
            return IntPtr.Zero;
        }
        return arr;
    }

    /// <summary>把 RGBA32 字节填进 IL2CPP Color[]（每个 Color = 4 个 little-endian float，取值 0..1）。</summary>
    static unsafe void FillColorArray(IntPtr colors, byte[] rgba, int width, int height, bool flipRows)
    {
        float* dst = (float*)((byte*)colors + 4 * IntPtr.Size);
        const float Inv255 = 1f / 255f;
        for (int row = 0; row < height; row++)
        {
            int srcRow = flipRows ? (height - 1 - row) : row;
            int srcByte = srcRow * width * 4;
            int dstFloat = row * width * 4;
            for (int k = 0; k < width * 4; k++)
                dst[dstFloat + k] = rgba[srcByte + k] * Inv255;
        }
    }

    /// <summary>按 ".dll" 后缀 / 无后缀 / 枚举 domain image 三种方式解析 IL2CPP 类。</summary>
    static IntPtr ResolveClass(string assembly, string ns, string type)
    {
        var cls = IL2CPP.GetIl2CppClass(assembly, ns, type);
        if (cls != IntPtr.Zero) return cls;

        string alt = assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? assembly.Substring(0, assembly.Length - 4)
            : assembly + ".dll";
        cls = IL2CPP.GetIl2CppClass(alt, ns, type);
        if (cls != IntPtr.Zero) return cls;

        // 兜底：自己枚举 domain 里的 image，不依赖 Il2CppInterop 的 ourImagesMap 注册表
        unsafe
        {
            var domain = IL2CPP.il2cpp_domain_get();
            uint count = 0;
            var asms = IL2CPP.il2cpp_domain_get_assemblies(domain, ref count);
            if (asms != null)
            {
                for (uint i = 0; i < count; i++)
                {
                    var img = IL2CPP.il2cpp_assembly_get_image(*(asms + i));
                    if (img == IntPtr.Zero) continue;
                    var name = Marshal.PtrToStringAnsi(IL2CPP.il2cpp_image_get_name(img));
                    if (!ImageNameMatches(name, assembly)) continue;
                    cls = IL2CPP.il2cpp_class_from_name(img, ns, type);
                    if (cls != IntPtr.Zero)
                    {
                        L("ResolveClass(" + assembly + ", " + ns + "." + type + ") 经 image 枚举命中: " + name);
                        return cls;
                    }
                }

                // 最后一层兜底：完全忽略程序集名，在**所有** image 里找这个全名类。
                // 真机上 mscorlib 的 image 名并不叫 "Il2Cppmscorlib.dll"（System.Byte 就是这样找不到的）。
                for (uint i = 0; i < count; i++)
                {
                    var img = IL2CPP.il2cpp_assembly_get_image(*(asms + i));
                    if (img == IntPtr.Zero) continue;
                    cls = IL2CPP.il2cpp_class_from_name(img, ns, type);
                    if (cls != IntPtr.Zero)
                    {
                        var name = Marshal.PtrToStringAnsi(IL2CPP.il2cpp_image_get_name(img));
                        L("ResolveClass(" + assembly + ", " + ns + "." + type + ") 经全量扫描命中: " + name);
                        return cls;
                    }
                }
            }
        }

        L("ResolveClass 失败: " + assembly + " :: " + ns + "." + type);
        return IntPtr.Zero;
    }

    static bool ImageNameMatches(string imageName, string wanted)
    {
        if (imageName == null || wanted == null) return false;
        if (string.Equals(imageName, wanted, StringComparison.OrdinalIgnoreCase)) return true;
        string a = imageName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? imageName.Substring(0, imageName.Length - 4) : imageName;
        string b = wanted.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? wanted.Substring(0, wanted.Length - 4) : wanted;
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================
    //  PNG 自解码（System.IO.Compression.ZLibStream + 手工反滤波）
    // ================================================================

    static int ReadBE32(byte[] d, int off)
        => (d[off] << 24) | (d[off + 1] << 16) | (d[off + 2] << 8) | d[off + 3];

    /// <summary>最小 PNG 解码器：8 位/16 位/1-2-4 位，灰度/RGB/调色板/灰度+Alpha/RGBA，非隔行。</summary>
    static bool DecodePng(byte[] d, out int width, out int height, out byte[] rgba)
    {
        width = 0; height = 0; rgba = null;

        int pos = 8;
        int bitDepth = 0, colorType = 0;
        byte[] palette = null, trns = null;
        var idat = new MemoryStream();
        bool sawIhdr = false, sawIend = false;

        while (pos + 8 <= d.Length)
        {
            int len = ReadBE32(d, pos);
            if (len < 0 || pos + 12 + len > d.Length) { LastError = "PNG 块长度越界 @" + pos; return false; }
            string type = Encoding.ASCII.GetString(d, pos + 4, 4);
            int ds = pos + 8;

            if (type == "IHDR")
            {
                if (len < 13) { LastError = "IHDR 长度异常"; return false; }
                width = ReadBE32(d, ds);
                height = ReadBE32(d, ds + 4);
                bitDepth = d[ds + 8];
                colorType = d[ds + 9];
                int comp = d[ds + 10], filt = d[ds + 11];
                int interlace = d[ds + 12];
                if (width <= 0 || height <= 0) { LastError = "PNG 尺寸非法 " + width + "x" + height; return false; }
                if (comp != 0 || filt != 0) { LastError = "PNG 压缩/滤波方法不支持 comp=" + comp + " filt=" + filt; return false; }
                if (interlace != 0) { LastError = "不支持隔行(Adam7) PNG"; return false; }
                sawIhdr = true;
            }
            else if (type == "PLTE") { palette = new byte[len]; Array.Copy(d, ds, palette, 0, len); }
            else if (type == "tRNS") { trns = new byte[len]; Array.Copy(d, ds, trns, 0, len); }
            else if (type == "IDAT") { idat.Write(d, ds, len); }
            else if (type == "IEND") { sawIend = true; }

            pos = ds + len + 4;   // 跳过数据 + CRC
            if (sawIend) break;
        }

        if (!sawIhdr) { LastError = "PNG 缺少 IHDR"; return false; }
        if (idat.Length == 0) { LastError = "PNG 缺少 IDAT"; return false; }

        int channels = colorType == 0 ? 1 : colorType == 2 ? 3 : colorType == 3 ? 1 : colorType == 4 ? 2 : colorType == 6 ? 4 : -1;
        if (channels < 0) { LastError = "不支持的 PNG 颜色类型 " + colorType; return false; }
        if (colorType == 3 && palette == null) { LastError = "调色板 PNG 缺少 PLTE"; return false; }
        if (bitDepth != 1 && bitDepth != 2 && bitDepth != 4 && bitDepth != 8 && bitDepth != 16)
        { LastError = "不支持的 PNG 位深 " + bitDepth; return false; }
        if (bitDepth < 8 && colorType != 0 && colorType != 3)
        { LastError = "1/2/4 位深仅支持灰度与调色板（colorType=" + colorType + "）"; return false; }

        int bitsPerPixel = channels * bitDepth;
        int bytesPerRow = (width * bitsPerPixel + 7) / 8;
        int bpp = Math.Max(1, bitsPerPixel / 8);          // 反滤波像素步长（字节）
        long expected = (long)(bytesPerRow + 1) * height;

        byte[] raw;
        try
        {
            idat.Position = 0;
            using (var z = new ZLibStream(idat, CompressionMode.Decompress))
            using (var outMs = new MemoryStream())
            {
                z.CopyTo(outMs);
                raw = outMs.ToArray();
            }
        }
        catch (Exception e)
        {
            LastError = "IDAT zlib 解压失败: " + e.GetType().Name + ": " + e.Message;
            return false;
        }

        if (raw.Length < expected)
        {
            LastError = "IDAT 解压后数据不足: " + raw.Length + " < " + expected;
            return false;
        }

        // ---- 反滤波 ----
        var img = new byte[bytesPerRow * height];
        for (int y = 0; y < height; y++)
        {
            int ft = raw[y * (bytesPerRow + 1)];
            int src = y * (bytesPerRow + 1) + 1;
            int cur = y * bytesPerRow;
            int prev = cur - bytesPerRow;
            for (int x = 0; x < bytesPerRow; x++)
            {
                int v = raw[src + x];
                int a = x >= bpp ? img[cur + x - bpp] : 0;                       // 左
                int b = y > 0 ? img[prev + x] : 0;                               // 上
                int c = (x >= bpp && y > 0) ? img[prev + x - bpp] : 0;           // 左上
                switch (ft)
                {
                    case 0: break;
                    case 1: v += a; break;
                    case 2: v += b; break;
                    case 3: v += (a + b) >> 1; break;
                    case 4: v += Paeth(a, b, c); break;
                    default: LastError = "未知 PNG 滤波类型 " + ft; return false;
                }
                img[cur + x] = (byte)v;
            }
        }

        // ---- 转 RGBA32（行序：图片顶行在前）----
        var outRgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int rowOff = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int o = rowOff + x * 4;
                switch (colorType)
                {
                    case 0:   // 灰度
                    {
                        int g = Sample(img, bytesPerRow, y, x, bitDepth);
                        if (bitDepth < 8) g = g * 255 / ((1 << bitDepth) - 1);
                        outRgba[o] = outRgba[o + 1] = outRgba[o + 2] = (byte)g;
                        outRgba[o + 3] = 255;
                        break;
                    }
                    case 2:   // RGB
                    {
                        outRgba[o] = (byte)Sample(img, bytesPerRow, y, x * 3, bitDepth);
                        outRgba[o + 1] = (byte)Sample(img, bytesPerRow, y, x * 3 + 1, bitDepth);
                        outRgba[o + 2] = (byte)Sample(img, bytesPerRow, y, x * 3 + 2, bitDepth);
                        outRgba[o + 3] = 255;
                        break;
                    }
                    case 3:   // 调色板
                    {
                        int idx = Sample(img, bytesPerRow, y, x, bitDepth);
                        int p = idx * 3;
                        if (p + 2 < palette.Length)
                        {
                            outRgba[o] = palette[p];
                            outRgba[o + 1] = palette[p + 1];
                            outRgba[o + 2] = palette[p + 2];
                        }
                        outRgba[o + 3] = (trns != null && idx < trns.Length) ? trns[idx] : (byte)255;
                        break;
                    }
                    case 4:   // 灰度 + Alpha
                    {
                        int g = Sample(img, bytesPerRow, y, x * 2, bitDepth);
                        outRgba[o] = outRgba[o + 1] = outRgba[o + 2] = (byte)g;
                        outRgba[o + 3] = (byte)Sample(img, bytesPerRow, y, x * 2 + 1, bitDepth);
                        break;
                    }
                    default:  // 6 = RGBA
                    {
                        outRgba[o] = (byte)Sample(img, bytesPerRow, y, x * 4, bitDepth);
                        outRgba[o + 1] = (byte)Sample(img, bytesPerRow, y, x * 4 + 1, bitDepth);
                        outRgba[o + 2] = (byte)Sample(img, bytesPerRow, y, x * 4 + 2, bitDepth);
                        outRgba[o + 3] = (byte)Sample(img, bytesPerRow, y, x * 4 + 3, bitDepth);
                        break;
                    }
                }
            }
        }

        rgba = outRgba;
        return true;
    }

    /// <summary>取一个采样值：8 位取字节；16 位取高字节（PNG 大端）；1/2/4 位按位取。</summary>
    static int Sample(byte[] img, int rowBytes, int y, int sampleIndex, int bitDepth)
    {
        int row = y * rowBytes;
        switch (bitDepth)
        {
            case 8: return img[row + sampleIndex];
            case 16: return img[row + sampleIndex * 2];
            case 1: return (img[row + (sampleIndex >> 3)] >> (7 - (sampleIndex & 7))) & 0x1;
            case 2: return (img[row + (sampleIndex >> 2)] >> (6 - ((sampleIndex & 3) << 1))) & 0x3;
            case 4: return (img[row + (sampleIndex >> 1)] >> (((sampleIndex & 1) == 0) ? 4 : 0)) & 0xF;
            default: return 0;
        }
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }
}
