using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(TypeProbe.ProbeMod), "TypeProbe", "0.4.0", "dsh")]
[assembly: MelonGame(null, null)]

namespace TypeProbe;

/// <summary>第四版：正确枚举 IL2CPP image 名，用真实名字取 Texture2D 类指针并创建实例。</summary>
public class ProbeMod : MelonMod
{
    public override void OnInitializeMelon()
    {
        LoggerInstance.Msg("=== TypeProbe v4 开始 ===");

        try
        {
            unsafe
            {
                var domain = IL2CPP.il2cpp_domain_get();
                uint size = 0;
                var asms = IL2CPP.il2cpp_domain_get_assemblies(domain, ref size);
                LoggerInstance.Msg("[RAW] 程序集数量 = " + size);

                string coreName = null;
                for (uint i = 0; i < size; i++)
                {
                    var img = IL2CPP.il2cpp_assembly_get_image(asms[i]);
                    var nm = Marshal.PtrToStringAnsi(IL2CPP.il2cpp_image_get_name(img));
                    if (i < 30) LoggerInstance.Msg("[RAW] image[" + i + "] = " + nm);
                    if (nm != null && (nm.StartsWith("UnityEngine.CoreModule") || nm == "UnityEngine"))
                        coreName ??= nm;
                }
                LoggerInstance.Msg("[RAW] 选中引擎程序集名 = " + (coreName ?? "<无>"));

                foreach (var cand in new[] { coreName, "UnityEngine.CoreModule", "UnityEngine.CoreModule.dll", "UnityEngine" })
                {
                    if (cand == null) continue;
                    var cls = IL2CPP.GetIl2CppClass(cand, "UnityEngine", "Texture2D");
                    LoggerInstance.Msg("[RAW] GetIl2CppClass(\"" + cand + "\") = 0x" + cls.ToString("X"));
                    if (cls == IntPtr.Zero) continue;

                    var obj = IL2CPP.il2cpp_object_new(cls);
                    var ctor = IL2CPP.il2cpp_class_get_method_from_name(cls, ".ctor", 4);
                    LoggerInstance.Msg("[RAW]   ★ object_new=0x" + obj.ToString("X") + "  .ctor(4)=0x" + ctor.ToString("X"));
                    if (obj != IntPtr.Zero && ctor != IntPtr.Zero)
                    {
                        int w = 4, h = 4, fmt = 4; bool mip = false;
                        var args = stackalloc IntPtr[4];
                        args[0] = (IntPtr)(&w); args[1] = (IntPtr)(&h);
                        args[2] = (IntPtr)(&fmt); args[3] = (IntPtr)(&mip);
                        IntPtr exc = IntPtr.Zero;
                        var res = IL2CPP.il2cpp_runtime_invoke(ctor, obj, (void**)args, ref exc);
                        LoggerInstance.Msg("[RAW]   ★ ctor res=0x" + res.ToString("X") + " exc=0x" + exc.ToString("X"));

                        var imgConv = IL2CPP.GetIl2CppClass("UnityEngine.ImageConversionModule", "UnityEngine", "ImageConversion");
                        var loadImage = imgConv == IntPtr.Zero ? IntPtr.Zero : IL2CPP.il2cpp_class_get_method_from_name(imgConv, "LoadImage", 2);
                        LoggerInstance.Msg("[RAW]   ImageConversion=0x" + imgConv.ToString("X") + " LoadImage(2)=0x" + loadImage.ToString("X"));
                    }
                    break;
                }

                LoggerInstance.Msg("=== TypeProbe v4 结束（没崩 = 原生路径可用）===");
            }
        }
        catch (Exception e)
        {
            LoggerInstance.Error("[RAW] 异常: " + e);
        }
    }
}
