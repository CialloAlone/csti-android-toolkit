using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using MelonLoader;

[assembly: MelonInfo(typeof(RawTextureTest.RawTextureTestMod), "RawTextureTest", "3.0.0", "dsh")]
[assembly: MelonGame(null, null)]

namespace RawTextureTest;

/// <summary>
/// RawTexture 真机验证 mod（v3：主路径改成【裸调原生 ICall 函数指针】，自解码只作 fallback）。
///
/// 流程：
///   STEP1 CreateTexture2D(4,4)
///   STEP2a 参考自解码（仅用于后面逐像素比对）
///   STEP2b LoadImage 主路径 = il2cpp_resolve_icall("UnityEngine.ImageConversion::LoadImage") + delegate* unmanaged[Cdecl] 裸调
///   STEP2c/d 仅在主路径失败时显式跑 fallback（SetPixels32 + Apply）
///   STEP3 CreateSprite —— 真机确认 Sprite 创建 ICall 全缺，期望"优雅跳过 + 指针 0"，不算失败
///   VERIFY-A get_isReadable（已注册 ICall）
///   VERIFY-B EncodeToPNG（已注册 ICall）→ 再用自解码器回解 → **逐像素比对**
///   VERIFY-C fallback 通道独立可用
///   VERIFY-D 原生 LoadImage 的 Resize 能力（2x2 纹理装 4x4 图 → 期望成功且登记尺寸变 4x4）
///   VERIFY-E LoadRawTextureData 必须 false + 明确 LastError
///   VERIFY-F CreateTexture2DFromImage
/// 注意：本文件同样不得出现任何托管 UnityEngine 类型。
/// </summary>
public class RawTextureTestMod : MelonMod
{
    /// <summary>
    /// 4x4 / 8bit / RGBA(colortype=6) 的 PNG，108 字节，Pillow 生成并已通过严格结构校验。
    /// 图案是确定的（红/绿/蓝/黄 → 青/品红/白/黑 → 半透明行 → 递增值行），方便真机逐像素比对与判断行列翻转。
    /// 注意：早先任务里给的那串 base64（以 ...AElFTkSuQmCC 结尾的 78 字节版本）是**结构损坏**的：
    /// IDAT 长度字段写 20，实际块占 21+4 字节，且 zlib Adler-32 校验失败 —— 严格解码器会（也应该）拒绝它。
    /// </summary>
    const string Png4X4Base64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAYAAACp8Z5+AAAAM0lEQVR4nDWIsQkAIBDE8mBlbf04iqM5mptFFOVIAofgHapY4jg4N/E6kQEupUdtaWby2YK5FeHNPP51AAAAAElFTkSuQmCC";

    const string ImageConversionModule = "UnityEngine.ImageConversionModule.dll";

    /// <summary>
    /// ICall 注册表探测：逐条 resolve 并**立即打印**，这样即使后面某条导致 abort，前面的结果也已落盘。
    /// （只解析不调用，对已裁剪的 ICall 也安全。）
    /// </summary>
    void ProbeIcalls()
    {
        LoggerInstance.Msg("[RAW] ---- ICall 探测（非 0 = 已注册）----");
        var names = new[]
        {
            "UnityEngine.ImageConversion::LoadImage",
            "UnityEngine.ImageConversion::EncodeToPNG",
            "UnityEngine.Texture2D::LoadImage",
            "UnityEngine.Texture2D::LoadRawTextureDataImpl",
            "UnityEngine.Texture2D::LoadRawTextureDataImplArray",
            "UnityEngine.Texture2D::SetPixelDataImplArray",
            "UnityEngine.Texture2D::SetPixelsImpl",
            "UnityEngine.Texture2D::SetAllPixels32",
            "UnityEngine.Texture2D::SetBlockOfPixels32",
            "UnityEngine.Texture2D::ApplyImpl",
            "UnityEngine.Texture2D::Apply",
            "UnityEngine.Texture2D::get_width",
            "UnityEngine.Texture2D::get_height",
            "UnityEngine.Texture2D::get_format",
            "UnityEngine.Texture2D::get_isReadable",
            "UnityEngine.Texture2D::Internal_Create",
            "UnityEngine.Sprite::Create",
            "UnityEngine.Sprite::CreateSpriteWithoutTextureScripting",
        };
        foreach (var n in names)
        {
            try
            {
                var p = RawTexture.ResolveIcall(n);
                LoggerInstance.Msg("[RAW] ICALL " + (p != IntPtr.Zero ? "有" : "无") + "  0x" + p.ToInt64().ToString("X") + "  " + n);
            }
            catch (Exception e)
            {
                LoggerInstance.Msg("[RAW] ICALL 异常 " + n + " : " + e.GetType().Name + " " + e.Message);
            }
        }
        LoggerInstance.Msg("[RAW] ---- ICall 探测结束 ----");
    }

    public override void OnInitializeMelon()
    {
        RawTexture.Log = m => LoggerInstance.Msg("[RAW] " + m);
        LoggerInstance.Msg("=== RawTextureTest v2 开始（自解码 + SetPixelsImpl/ApplyImpl 通道）===");

        try
        {
            ProbeIcalls();

            var png = Convert.FromBase64String(Png4X4Base64);
            LoggerInstance.Msg("[RAW] 内嵌 PNG: " + png.Length + " 字节, 魔数=" + BitConverter.ToString(png, 0, 8));

            LoggerInstance.Msg("[RAW] ---- 解析结果 ----");
            foreach (var line in RawTexture.Describe().Split('\n'))
                if (line.Trim().Length > 0) LoggerInstance.Msg("[RAW] " + line.TrimEnd());
            LoggerInstance.Msg("[RAW] ------------------");

            // ---------- 步骤 1：创建 4x4 纹理 ----------
            var tex = RawTexture.CreateTexture2D(4, 4);
            LoggerInstance.Msg("[RAW] STEP1 CreateTexture2D(4,4) ptr=0x" + tex.ToInt64().ToString("X")
                               + (tex != IntPtr.Zero ? "  OK" : "  FAIL: " + RawTexture.LastError));
            if (tex == IntPtr.Zero) { LoggerInstance.Error("[RAW] 步骤 1 失败，终止"); return; }
            RawTexture.GetTextureSize(tex, out int rw, out int rh);
            LoggerInstance.Msg("[RAW] STEP1b 登记尺寸=" + rw + "x" + rh + " isReadable=" + GetIntProp(tex, "isReadable")
                               + "（尺寸走登记表，不 invoke get_width/get_height —— 那两个属性带托管方法体、内部 ICall 可能被裁剪）");

            // ---------- 步骤 2：主路径 = 裸调原生 ICall ImageConversion::LoadImage ----------
            // 参考像素（仅用于 VERIFY-B2 逐像素比对；解码器已在 PC 侧与 Pillow 逐字节对齐 13/13）
            if (!RawTexture.TryDecodeImage(png, out int iw, out int ih, out byte[] rgba))
            {
                LoggerInstance.Error("[RAW] STEP2a 参考自解码失败: " + RawTexture.LastError);
                return;
            }
            LoggerInstance.Msg("[RAW] STEP2a 参考自解码 " + iw + "x" + ih + " rgba=" + rgba.Length
                               + " 首像素=" + rgba[0] + "," + rgba[1] + "," + rgba[2] + "," + rgba[3]);

            bool native = RawTexture.LoadImage(tex, png);
            LoggerInstance.Msg("[RAW] STEP2b LoadImage(主路径=原生 ICall 裸调) -> " + native
                               + (native ? "  OK" : "  FAIL: " + RawTexture.LastError));

            // 若原生路径没成功，显式走一遍 fallback，确认备胎可用
            if (!native)
            {
                bool up = RawTexture.SetPixels32(tex, rgba, iw, ih, true);
                LoggerInstance.Msg("[RAW] STEP2c(fallback) SetPixels32 -> " + up + (up ? "  OK" : "  FAIL: " + RawTexture.LastError));
                bool ap = RawTexture.Apply(tex, true, false);
                LoggerInstance.Msg("[RAW] STEP2d(fallback) Apply -> " + ap + (ap ? "  OK" : "  FAIL: " + RawTexture.LastError));
            }

            // ---------- 步骤 3：建 Sprite（真机已确认是硬墙 → 期望优雅跳过，不算 FAIL） ----------
            var sprite = RawTexture.CreateSprite(tex, 0f, 0f, 4f, 4f);
            LoggerInstance.Msg("[RAW] STEP3 CreateSprite ptr=0x" + sprite.ToInt64().ToString("X")
                               + "  LastError=" + RawTexture.LastError
                               + " 结论: " + (sprite == IntPtr.Zero ? "通过（按预期优雅跳过，进程存活）" : "意外成功"));

            // ---------- 验证 A：isReadable ----------
            int readable = GetIntProp(tex, "isReadable");
            LoggerInstance.Msg("[RAW] VERIFY-A isReadable=" + readable + " 结论: " + (readable == 1 ? "通过（纹理可读，EncodeToPNG 才有意义）" : "未通过"));

            // ---------- 验证 B：EncodeToPNG 回编码 + 自解码回比 ----------
            var encoded = EncodeToPngNative(tex);
            if (encoded == null)
            {
                LoggerInstance.Warning("[RAW] VERIFY-B 失败：EncodeToPNG 没返回数据");
            }
            else
            {
                bool magic = encoded.Length >= 8 && encoded[0] == 0x89 && encoded[1] == 0x50 && encoded[2] == 0x4E && encoded[3] == 0x47;
                LoggerInstance.Msg("[RAW] VERIFY-B EncodeToPNG -> " + encoded.Length + " 字节, 头="
                                   + BitConverter.ToString(encoded, 0, Math.Min(8, encoded.Length))
                                   + " 结论: " + (magic ? "通过（PNG 魔数正确）" : "未通过"));

                if (RawTexture.TryDecodeImage(encoded, out int ew, out int eh, out byte[] ergba))
                {
                    bool same = PixelEquals(ergba, rgba, false, iw, ih);
                    bool flipped = PixelEquals(ergba, rgba, true, iw, ih);
                    LoggerInstance.Msg("[RAW] VERIFY-B2 回解码 " + ew + "x" + eh
                                       + " 首像素=" + ergba[0] + "," + ergba[1] + "," + ergba[2] + "," + ergba[3]
                                       + " | 原图首像素=" + rgba[0] + "," + rgba[1] + "," + rgba[2] + "," + rgba[3]);
                    LoggerInstance.Msg("[RAW] VERIFY-B2 逐像素比对: 同序一致=" + same + " 上下翻转一致=" + flipped
                                       + " 结论: " + ((same || flipped) ? "★通过 —— 图片像素确实进了纹理（Unity 行列约定已确认）" : "未通过 —— 像素对不上"));
                }
                else
                {
                    LoggerInstance.Warning("[RAW] VERIFY-B2 回解码 EncodeToPNG 结果失败: " + RawTexture.LastError);
                }
            }

            // ---------- 验证 C：fallback 通道（自解码 + SetPixelsImpl + ApplyImpl）独立可用 ----------
            var tex3 = RawTexture.CreateTexture2D(4, 4);
            bool c = tex3 != IntPtr.Zero && RawTexture.SetPixels32(tex3, rgba, iw, ih, true) && RawTexture.Apply(tex3, true, false);
            LoggerInstance.Msg("[RAW] VERIFY-C fallback 通道（新 4x4 纹理 + SetPixels32 + Apply）-> " + c
                               + (c ? "  OK" : "  FAIL: " + RawTexture.LastError));

            // ---------- 验证 D：原生 LoadImage 的 Resize 能力（2x2 纹理装 4x4 图） ----------
            var texSmall = RawTexture.CreateTexture2D(2, 2);
            RawTexture.GetTextureSize(texSmall, out int bw, out int bh);
            bool d = texSmall != IntPtr.Zero && RawTexture.LoadImage(texSmall, png);
            RawTexture.GetTextureSize(texSmall, out int aw, out int ah);
            LoggerInstance.Msg("[RAW] VERIFY-D LoadImage(2x2 纹理, 4x4 PNG) -> " + d
                               + "  登记尺寸 " + bw + "x" + bh + " -> " + aw + "x" + ah + "（期望 4x4：原生解码会 Resize）"
                               + " 结论: " + ((d && aw == 4 && ah == 4) ? "通过（原生 LoadImage 会重建纹理尺寸，登记表已同步）" : (d ? "成功但尺寸未同步" : "未通过: " + RawTexture.LastError)));

            // ---------- 验证 E：LoadRawTextureData 必须明确失败 ----------
            bool e = RawTexture.LoadRawTextureData(tex, png);
            LoggerInstance.Msg("[RAW] VERIFY-E LoadRawTextureData -> " + e + "  期望 False; LastError=" + RawTexture.LastError
                               + " 结论: " + ((!e && RawTexture.LastError == "纹理上传 ICall 被引擎裁剪") ? "通过" : "未通过"));

            // ---------- 验证 F：CreateTexture2DFromImage（按图片尺寸自动建纹理，走 fallback 通道） ----------
            var tex4 = RawTexture.CreateTexture2DFromImage(png);
            if (tex4 == IntPtr.Zero)
            {
                LoggerInstance.Warning("[RAW] VERIFY-F CreateTexture2DFromImage 失败: " + RawTexture.LastError);
            }
            else
            {
                RawTexture.GetTextureSize(tex4, out int fw, out int fh);
                var enc2 = EncodeToPngNative(tex4);
                bool fOk = enc2 != null && enc2.Length >= 8 && enc2[0] == 0x89 && enc2[1] == 0x50;
                LoggerInstance.Msg("[RAW] VERIFY-F CreateTexture2DFromImage ptr=0x" + tex4.ToInt64().ToString("X")
                                   + " 登记尺寸=" + fw + "x" + fh + " EncodeToPNG=" + (enc2?.Length ?? -1) + " 字节"
                                   + " 结论: " + (fOk ? "通过" : "未通过"));
            }

            LoggerInstance.Msg("=== RawTextureTest v3 结束：进程存活 ===");
        }
        catch (Exception ex)
        {
            LoggerInstance.Error("[RAW] 托管异常（未崩溃）: " + ex);
        }
    }

    // ================================================================
    //  以下都是纯原生调试辅助
    // ================================================================

    /// <summary>沿父类链找方法（Texture2D.width 实际定义在 UnityEngine.Texture 上）。</summary>
    static IntPtr FindInHierarchy(IntPtr cls, string name, int argc)
    {
        for (var c = cls; c != IntPtr.Zero; c = IL2CPP.il2cpp_class_get_parent(c))
        {
            var m = IL2CPP.il2cpp_class_get_method_from_name(c, name, argc);
            if (m != IntPtr.Zero) return m;
        }
        return IntPtr.Zero;
    }

    /// <summary>读取原生对象的 int 属性（int 返回值同样是装箱的，需要 unbox）。</summary>
    static int GetIntProp(IntPtr obj, string prop)
    {
        if (obj == IntPtr.Zero) return int.MinValue;
        var cls = IL2CPP.il2cpp_object_get_class(obj);
        var m = FindInHierarchy(cls, "get_" + prop, 0);
        if (m == IntPtr.Zero)
        {
            MelonLogger.Warning("[RAW] 找不到属性 get_" + prop);
            return int.MinValue;
        }
        unsafe
        {
            IntPtr exc = IntPtr.Zero;
            var res = IL2CPP.il2cpp_runtime_invoke(m, obj, null, ref exc);
            if (exc != IntPtr.Zero) throw new Exception("get_" + prop + " 抛异常: " + RawTexture.DescribeException(exc));
            if (res == IntPtr.Zero) return int.MinValue;
            var p = IL2CPP.il2cpp_object_unbox(res);
            return p == IntPtr.Zero ? int.MinValue : *(int*)p;
        }
    }

    /// <summary>调原生 ImageConversion.EncodeToPNG(tex)，把返回的 IL2CPP byte[] 拷成托管 byte[]。</summary>
    static byte[] EncodeToPngNative(IntPtr tex)
    {
        try
        {
            var cls = RawTexture.GetImageConversionClass();
            if (cls == IntPtr.Zero) { MelonLogger.Warning("[RAW] VERIFY-B 找不到 ImageConversion 类"); return null; }

            var m = IL2CPP.il2cpp_class_get_method_from_name(cls, "EncodeToPNG", 1);
            if (m == IntPtr.Zero) { MelonLogger.Warning("[RAW] VERIFY-B 找不到 EncodeToPNG/1"); return null; }

            unsafe
            {
                IntPtr texLocal = tex;
                var args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&texLocal);
                IntPtr exc = IntPtr.Zero;
                var arr = IL2CPP.il2cpp_runtime_invoke(m, IntPtr.Zero, (void**)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    MelonLogger.Warning("[RAW] VERIFY-B EncodeToPNG 抛异常: " + RawTexture.DescribeException(exc));
                    return null;
                }
                if (arr == IntPtr.Zero) { MelonLogger.Warning("[RAW] VERIFY-B EncodeToPNG 返回 0"); return null; }

                int len = (int)IL2CPP.il2cpp_array_length(arr);
                var buf = new byte[len];
                if (len > 0) Marshal.Copy(RawTexture.ArrayData(arr), buf, 0, len);
                return buf;
            }
        }
        catch (Exception e)
        {
            MelonLogger.Warning("[RAW] VERIFY-B 异常: " + e.Message);
            return null;
        }
    }

    /// <summary>比较两份 RGBA 缓冲；flip 时把 b 上下翻转后再比。</summary>
    static bool PixelEquals(byte[] a, byte[] b, bool flip, int width, int height)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int y = 0; y < height; y++)
        {
            int by = flip ? (height - 1 - y) : y;
            int ao = y * width * 4, bo = by * width * 4;
            for (int k = 0; k < width * 4; k++)
                if (a[ao + k] != b[bo + k]) return false;
        }
        return true;
    }
}
