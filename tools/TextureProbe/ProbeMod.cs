using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using MelonLoader;

[assembly: MelonInfo(typeof(TextureProbe.ProbeMod), "TextureProbe", "2.0.0", "dsh")]
[assembly: MelonGame(null, null)]

namespace TextureProbe;

/// <summary>
/// v2：验证「按函数指针直接调 ICall」能绕开缺失的托管包装。
///  A. 造纹理（原生 ✔ 已证）→ 造 IL2CPP byte[] → 直接调 ImageConversion::LoadImage 指针
///  B. 再直接调 ImageConversion::EncodeToPNG 指针回读，检查 PNG 魔数与长度（证明像素真进去了）
///  C. 扫掉 Sprite / AssetBundle 的其余可能入口
/// 本文件不引用任何 UnityEngine 托管类型。
/// </summary>
public class ProbeMod : MelonMod
{
    const string Png4X4Base64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAYAAACp8Z5+AAAAFElEQVR42mNk+M9Qz0AEYBxVSF+FABJADveWkH6oAAAAAElFTkSuQmCC";

    public override void OnInitializeMelon()
    {
        LoggerInstance.Msg("=== TextureProbe v2 开始 ===");
        try
        {
            unsafe
            {
                LoggerInstance.Msg("[TP] ---- 扫 Sprite / AssetBundle 入口 ----");
                foreach (var n in new[]
                {
                    "UnityEngine.Sprite::Create",
                    "UnityEngine.Sprite::CreateSprite",
                    "UnityEngine.Sprite::CreateSpriteScripting",
                    "UnityEngine.Sprite::Internal_Create",
                    "UnityEngine.Sprite::Internal_CreateSprite",
                    "UnityEngine.Sprite::CreateWithTexture",
                    "UnityEngine.AssetBundle::LoadFromMemory_Internal",
                    "UnityEngine.AssetBundle::LoadFromFile_Internal",
                    "UnityEngine.AssetBundle::LoadAsset_Internal",
                    "UnityEngine.Resources::Load",
                    "UnityEngine.Resources::LoadAsyncInternal",
                    "UnityEngine.Texture2D::LoadImage",
                })
                {
                    var p = RawTexture.ResolveIcall(n);
                    LoggerInstance.Msg("[TP] ICALL " + (p != IntPtr.Zero ? "有" : "无") + " " + n);
                }

                // ---------- A. 直接按指针调 ImageConversion::LoadImage ----------
                var tex = RawTexture.CreateTexture2D(4, 4);
                LoggerInstance.Msg("[TP] A: 纹理 = 0x" + tex.ToInt64().ToString("X"));
                if (tex == IntPtr.Zero) { LoggerInstance.Msg("[TP] 结束：纹理创建失败"); return; }

                var png = Convert.FromBase64String(Png4X4Base64);
                var byteCls = ResolveByteClass();
                LoggerInstance.Msg("[TP] A: System.Byte 类 = 0x" + byteCls.ToInt64().ToString("X"));
                if (byteCls == IntPtr.Zero) { LoggerInstance.Msg("[TP] 结束：拿不到 Byte 类"); return; }

                var arr = IL2CPP.il2cpp_array_new(byteCls, (ulong)png.Length);
                if (arr == IntPtr.Zero) { LoggerInstance.Msg("[TP] 结束：数组创建失败"); return; }
                byte* dst = (byte*)arr + 4 * IntPtr.Size;
                Marshal.Copy(png, 0, (IntPtr)dst, png.Length);
                LoggerInstance.Msg("[TP] A: byte[] = 0x" + arr.ToInt64().ToString("X") + " len=" + IL2CPP.il2cpp_array_length(arr));

                var loadFn = RawTexture.ResolveIcall("UnityEngine.ImageConversion::LoadImage");
                LoggerInstance.Msg("[TP] A: LoadImage 指针 = 0x" + loadFn.ToInt64().ToString("X") + "，即将裸调 (tex, arr, false, null)");
                if (loadFn != IntPtr.Zero)
                {
                    var del = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte, IntPtr, byte>)loadFn;
                    var ok = del(tex, arr, 0, IntPtr.Zero);
                    LoggerInstance.Msg("[TP] A: ★ LoadImage 返回 = " + ok + "  ← 没崩！直接调 ICall 绕开成功");
                }

                // ---------- B. EncodeToPNG 回读，证明像素进去了 ----------
                var encFn = RawTexture.ResolveIcall("UnityEngine.ImageConversion::EncodeToPNG");
                LoggerInstance.Msg("[TP] B: EncodeToPNG 指针 = 0x" + encFn.ToInt64().ToString("X"));
                if (encFn != IntPtr.Zero)
                {
                    var enc = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr>)encFn;
                    var outArr = enc(tex, IntPtr.Zero);
                    LoggerInstance.Msg("[TP] B: 返回数组 = 0x" + outArr.ToInt64().ToString("X"));
                    if (outArr != IntPtr.Zero)
                    {
                        var len = IL2CPP.il2cpp_array_length(outArr);
                        byte* src = (byte*)outArr + 4 * IntPtr.Size;
                        var head = new byte[Math.Min(8, (int)len)];
                        Marshal.Copy((IntPtr)src, head, 0, head.Length);
                        LoggerInstance.Msg("[TP] B: ★ EncodeToPNG 长度=" + len + " 头=" + BitConverter.ToString(head)
                                           + "  ← 89-50-4E-47 = PNG，说明纹理里确实有图");
                    }
                }
            }
        }
        catch (Exception e)
        {
            LoggerInstance.Msg("[TP] 异常（非崩溃）: " + e.GetType().Name + " " + e.Message);
        }
        LoggerInstance.Msg("=== TextureProbe v2 结束 ===");
    }

    static IntPtr ResolveByteClass()
    {
        unsafe
        {
            var domain = IL2CPP.il2cpp_domain_get();
            uint count = 0;
            var asms = IL2CPP.il2cpp_domain_get_assemblies(domain, ref count);
            for (uint i = 0; i < count; i++)
            {
                var img = IL2CPP.il2cpp_assembly_get_image(*(asms + i));
                if (img == IntPtr.Zero) continue;
                var cls = IL2CPP.il2cpp_class_from_name(img, "System", "Byte");
                if (cls != IntPtr.Zero) return cls;
            }
        }
        return IntPtr.Zero;
    }
}
