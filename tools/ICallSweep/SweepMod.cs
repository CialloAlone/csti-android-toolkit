using System;
using Il2CppInterop.Runtime;
using MelonLoader;

[assembly: MelonInfo(typeof(ICallSweep.SweepMod), "ICallSweep", "1.0.0", "dsh")]
[assembly: MelonGame(null, null)]

namespace ICallSweep;

/// <summary>
/// 纯 ICall 名字扫描：只调 il2cpp_resolve_icall（解析名字，不调用函数），用于找出
/// 「原始 GPU 纹理上传」「Sprite 创建」这类被裁剪/改名后仍可用的原生入口。
/// 本文件不引用任何 UnityEngine 托管类型。
/// </summary>
public class SweepMod : MelonMod
{
    public override void OnInitializeMelon()
    {
        LoggerInstance.Msg("=== ICallSweep 开始 ===");
        var names = new[]
        {
            // 纹理上传（各种可能的原生名字）
            "UnityEngine.Texture2D::LoadRawTextureData",
            "UnityEngine.Texture2D::LoadRawTextureDataImpl",
            "UnityEngine.Texture2D::LoadRawTextureDataImplArray",
            "UnityEngine.Texture2D::SetPixelsImpl",
            "UnityEngine.Texture2D::SetPixels32",
            "UnityEngine.Texture2D::SetPixelData",
            "UnityEngine.Texture2D::SetPixelDataImplArray",
            "UnityEngine.Texture2D::ApplyImpl",
            "UnityEngine.Texture2D::Apply",
            "UnityEngine.Texture2D::Internal_Create",
            "UnityEngine.Texture2D::get_isReadable",
            "UnityEngine.Texture2D::GetPixelsImpl",
            "UnityEngine.Texture2D::GetRawTextureData",
            // 编码图解码
            "UnityEngine.ImageConversion::LoadImage",
            "UnityEngine.ImageConversion::EncodeToPNG",
            "UnityEngine.ImageConversion::EncodeToJPG",
            // Sprite 创建
            "UnityEngine.Sprite::Create",
            "UnityEngine.Sprite::CreateSprite",
            "UnityEngine.Sprite::CreateSpriteWithoutTextureScripting",
            "UnityEngine.Sprite::Internal_CreateSprite",
            "UnityEngine.Sprite::get_texture",
            "UnityEngine.Sprite::get_rect",
            "UnityEngine.Sprite::get_pixelsPerUnit",
            // 音频（MiniLoader 的 wav 块也断在这条）
            "UnityEngine.AudioClip::Construct_Internal",
            "UnityEngine.AudioClip::Init_Internal",
            // 其它可能用到的
            "UnityEngine.Object::Internal_CloneSingle",
            "UnityEngine.Object::Destroy",
            "UnityEngine.Object::DontDestroyOnLoad",
        };

        foreach (var n in names)
        {
            IntPtr p = IntPtr.Zero;
            try { p = IL2CPP.il2cpp_resolve_icall(n); }
            catch (Exception e) { LoggerInstance.Msg("[ICALL] 异常 " + n + " : " + e.GetType().Name); continue; }
            LoggerInstance.Msg("[ICALL] " + (p != IntPtr.Zero ? "有" : "无") + "  0x" + p.ToInt64().ToString("X16") + "  " + n);
        }
        LoggerInstance.Msg("=== ICallSweep 结束 ===");
    }
}
