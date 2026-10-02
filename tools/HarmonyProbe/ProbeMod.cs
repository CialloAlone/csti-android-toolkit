using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(HarmonyProbe.ProbeMod), "HarmonyProbe", "0.1.0", "dsh")]
[assembly: MelonGame(null, null)]

namespace HarmonyProbe;

/// <summary>一个可 patch 目标的描述。</summary>
public sealed class TargetDef
{
    public string Key;
    public Type T;
    public string Method;
    public int Argc;

    public MethodInfo Resolve()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic |
                                 BindingFlags.Instance | BindingFlags.Static;
        try
        {
            var m = T.GetMethod(Method, all | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            if (m != null) return m;
        }
        catch { }
        try
        {
            var m = T.GetMethod(Method, all, null, Type.EmptyTypes, null);
            if (m != null) return m;
        }
        catch { }
        try
        {
            foreach (var m in T.GetMethods(all))
                if (m.Name == Method && m.GetParameters().Length == Argc)
                    return m;
        }
        catch { }
        return null;
    }
}

public class ProbeMod : MelonMod
{
    public const string HarmonyId = "dsh.HarmonyProbe";

    internal static readonly Dictionary<string, string> Cfg = new(StringComparer.OrdinalIgnoreCase);
    private static Thread _hb;
    private static volatile bool _hbStop;
    private static int _hbN;
    private static int _hbMs = 1000;
    private static int _hbMode;   // 0=不开线程 1=线程+MelonLogger+文件 2=线程+只写文件

    /// <summary>OnUpdate 被调用的次数（用于验证更新泵是否还活着）。</summary>
    public static int UpdateTicks;

    /// <summary>特性式补丁类里的补丁体是否打日志（默认 false = 空体）。</summary>
    public static bool AttrVerbose;

    public static string Get(string k, string def)
        => Cfg.TryGetValue(k, out var v) ? v.Trim() : def;

    public static bool Flag(string k, bool def = false)
    {
        var v = Get(k, null);
        if (v == null) return def;
        return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    public static string[] List(string k)
    {
        var v = Get(k, "");
        if (string.IsNullOrWhiteSpace(v)) return Array.Empty<string>();
        return v.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string FindCfg()
    {
        var cands = new List<string>();
        try
        {
            var asmDir = Path.GetDirectoryName(typeof(ProbeMod).Assembly.Location);
            if (!string.IsNullOrEmpty(asmDir))
            {
                cands.Add(Path.Combine(asmDir, "HarmonyProbe.cfg"));
                var parent = Path.GetDirectoryName(asmDir);
                if (!string.IsNullOrEmpty(parent)) cands.Add(Path.Combine(parent, "HarmonyProbe.cfg"));
            }
        }
        catch { }
        cands.Add("/sdcard/MelonLoader/com.winterspringgames.survivaljourney/HarmonyProbe.cfg");
        cands.Add("/sdcard/HarmonyProbe.cfg");
        foreach (var c in cands)
            try { if (File.Exists(c)) return c; } catch { }
        return null;
    }

    public override void OnInitializeMelon()
    {
#if PROBE_NOTHING
        // -p:ProbeNothing=1：什么都不做，用于判断「多一个 mod」本身是否有影响
        return;
#else
        // ---------- 0. 尽早开一个落盘日志 ----------
        var asmDir = "";
        try { asmDir = Path.GetDirectoryName(typeof(ProbeMod).Assembly.Location) ?? ""; } catch { }
        var logPath = asmDir.Length > 0 ? Path.Combine(asmDir, "HarmonyProbe.log")
                                        : "/sdcard/MelonLoader/com.winterspringgames.survivaljourney/HarmonyProbe.log";
        PLog.Init(logPath);

        PLog.W("assemblies: " + (asmDir.Length > 0 ? asmDir : "?"));
        if (!Flag("skipver"))
        {
            try { PLog.W("MelonLoader=" + typeof(MelonMod).Assembly.GetName().Version); } catch { }
            try { PLog.W("0Harmony=" + typeof(HarmonyLib.Harmony).Assembly.GetName().Version); } catch { }
            try { PLog.W("Il2CppInterop.Runtime=" + typeof(IL2CPP).Assembly.GetName().Version); } catch { }
            try { PLog.W("HarmonyLib.Harmony.DEBUG=" + HarmonyLib.Harmony.DEBUG); } catch { }
        }
        else
        {
            PLog.W("skipver=1：不触碰 Harmony/Il2CppInterop 类型");
        }

        // ---------- 1. 配置 ----------
        var cfgFile = FindCfg();
        if (cfgFile == null)
        {
            PLog.W("no HarmonyProbe.cfg found -> 默认不挂任何补丁（安全）");
        }
        else
        {
            PLog.W("cfg = " + cfgFile);
            try
            {
                foreach (var raw in File.ReadAllLines(cfgFile))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                    var i = line.IndexOf('=');
                    if (i <= 0) continue;
                    Cfg[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                    PLog.W("  cfg." + line.Substring(0, i).Trim() + " = " + line.Substring(i + 1).Trim());
                }
            }
            catch (Exception e) { PLog.E("读配置失败: " + e.Message); }
        }

        if (Flag("debug"))
        {
            try { HarmonyLib.Harmony.DEBUG = true; PLog.W("已打开 HarmonyLib.Harmony.DEBUG（补丁过程会写 Harmony.log）"); } catch (Exception e) { PLog.E("HarmonyLib.Harmony.DEBUG 打开失败 " + e.Message); }
        }

        AttrVerbose = Flag("attrbodylog");
        PLog.W("AttrVerbose(特性式补丁体打日志) = " + AttrVerbose);

        // ---------- 2. 心跳线程：崩溃时刻可以被精确定位 ----------
        _hbMode = int.TryParse(Get("heartbeat", "1"), out var hbm) ? hbm : 1;
        PLog.W("heartbeat 模式 = " + _hbMode + "（0=不开线程 1=线程+MelonLogger 2=线程+只写文件）");
        if (_hbMode > 0)
        {
            _hbMs = int.TryParse(Get("heartbeat_ms", "1000"), out var ms) ? ms : 1000;
            _hb = new Thread(HbLoop) { IsBackground = true, Name = "HarmonyProbe.HB" };
            _hb.Start();
        }

        // ---------- 3. 执行实验 ----------
        try
        {
            RunExperiment();
        }
        catch (Exception e)
        {
            PLog.E("RunExperiment 异常: " + e);
        }

        PLog.W("OnInitializeMelon 返回（未崩溃）");
#endif
    }

    private static void HbLoop()
    {
        while (!_hbStop)
        {
            try { Thread.Sleep(_hbMs); } catch { return; }
            if (_hbStop) return;
            _hbN++;
            if (_hbMode == 2) PLog.Wf("HB " + _hbN + "  updateTicks=" + Volatile.Read(ref UpdateTicks));
            else PLog.W("HB " + _hbN + "  updateTicks=" + Volatile.Read(ref UpdateTicks));
        }
    }

    public override void OnUpdate()
    {
        Interlocked.Increment(ref UpdateTicks);
    }

    // ==================================================================
    //  实验主体
    // ==================================================================
    private static void RunExperiment()
    {
        var targets = List("target");
        var kind = Get("kind", "none").ToLowerInvariant();
        var body = Get("body", "empty").ToLowerInvariant();
        var mode = Get("mode", "manual").ToLowerInvariant();
        var dump = Flag("dump", true);

        PLog.W($"实验: mode={mode} kind={kind} body={body} targets=[{string.Join(",", targets)}] dump={dump}");

        if (targets.Length == 0 || kind == "none")
        {
            PLog.W("无补丁（对照：只加载探针，不挂 Harmony）");
            return;
        }

        var h = new HarmonyLib.Harmony(HarmonyId);
        PLog.W("独立 Harmony 实例 id = " + h.Id);

        foreach (var key in targets)
        {
            var def = Registry.Find(key);
            if (def == null) { PLog.E("未知 target: " + key); continue; }

            MethodInfo orig;
            try { orig = def.Resolve(); }
            catch (Exception e) { PLog.E("解析 " + key + " 失败: " + e); continue; }
            if (orig == null) { PLog.E("找不到方法: " + key); continue; }

            PLog.W($"--- target {key}: {orig.DeclaringType?.FullName}.{orig.Name} argc={orig.GetParameters().Length} " +
                   $"declAsm={orig.DeclaringType?.Assembly.GetName().Name}");

            var host = Il2CppPointer.Dump(orig, "BEFORE");

            if (mode == "manual")
            {
                HarmonyMethod pre = null, post = null;
                if (kind == "prefix" || kind == "both") pre = new HarmonyMethod(Bodies.Pick(body, true));
                if (kind == "postfix" || kind == "both") post = new HarmonyMethod(Bodies.Pick(body, false));
                PLog.W($"patch {key}: pre={(pre == null ? "-" : pre.method.Name)} post={(post == null ? "-" : post.method.Name)}");
                var sw = DateTime.Now;
                h.Patch(orig, pre, post);
                PLog.W($"patch {key} OK 用时={(DateTime.Now - sw).TotalMilliseconds:F0}ms");
            }
            else
            {
                var types = AttrPatches.Pick(mode, key, kind);
                if (types == null) { PLog.E("attr 模式不支持: mode=" + mode + " target=" + key + " kind=" + kind); continue; }
                foreach (var t in types)
                {
                    PLog.W($"PatchAll({t.FullName}) 开始");
                    var sw = DateTime.Now;
                    HarmonyLib.Harmony.CreateAndPatchAll(t, HarmonyId + "." + t.Name);
                    PLog.W($"PatchAll({t.FullName}) OK 用时={(DateTime.Now - sw).TotalMilliseconds:F0}ms");
                }
            }

            if (dump) Il2CppPointer.Dump(orig, "AFTER ");
        }
    }

    // ==================================================================
    //  目标表
    // ==================================================================
    public static class Registry
    {
        private static readonly List<TargetDef> All = new()
        {
            new TargetDef { Key = "loc.LoadLanguage",  T = typeof(LocalizationManager), Method = "LoadLanguage", Argc = 0 },
            new TargetDef { Key = "guide.Start",       T = typeof(GuideManager),        Method = "Start",        Argc = 0 },
            new TargetDef { Key = "graphics.Init",     T = typeof(GraphicsManager),     Method = "Init",         Argc = 0 },
            new TargetDef { Key = "cheats.Update",     T = typeof(CheatsManager),       Method = "Update",       Argc = 0 },
            new TargetDef { Key = "cheats.OnGUI",      T = typeof(CheatsManager),       Method = "OnGUI",        Argc = 0 },
        };

        public static TargetDef Find(string key)
        {
            foreach (var d in All)
                if (string.Equals(d.Key, key.Trim(), StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }
    }

    // ==================================================================
    //  补丁体
    // ==================================================================
    public static class Bodies
    {
        public static void PreEmpty() { }
        public static void PostEmpty() { }

        public static void PreLog() => PLog.W("HIT prefix（补丁体真的执行了）");
        public static void PostLog() => PLog.W("HIT postfix（补丁体真的执行了）");

        public static void PreInstance(object __instance)
            => PLog.W("HIT prefix __instance=" + (__instance == null ? "null" : __instance.GetType().FullName));
        public static void PostInstance(object __instance)
            => PLog.W("HIT postfix __instance=" + (__instance == null ? "null" : __instance.GetType().FullName));

        public static void PreResult(object __result)
            => PLog.W("HIT prefix（实参 __result 类型=" + (__result == null ? "null" : __result.GetType().FullName) + "）");

        public static MethodInfo Pick(string body, bool isPrefix)
        {
            var n = body switch
            {
                "log" => isPrefix ? nameof(PreLog) : nameof(PostLog),
                "instance" => isPrefix ? nameof(PreInstance) : nameof(PostInstance),
                _ => isPrefix ? nameof(PreEmpty) : nameof(PostEmpty),
            };
            return typeof(Bodies).GetMethod(n, BindingFlags.Public | BindingFlags.Static);
        }
    }
}

/// <summary>读 il2cpp MethodInfo 里的 methodPointer / virtualMethodPointer，用于验证 detour 是否真的改写了指针。</summary>
public static class Il2CppPointer
{
    public static unsafe IntPtr MethodInfoOf(MethodInfo m)
    {
        try
        {
            var t = Il2CppType.From(m.DeclaringType);
            var cls = IL2CPP.il2cpp_class_from_system_type(t.Pointer);
            if (cls == IntPtr.Zero) return IntPtr.Zero;
            return IL2CPP.il2cpp_class_get_method_from_name(cls, m.Name, m.GetParameters().Length);
        }
        catch { return IntPtr.Zero; }
    }

    public static unsafe IntPtr Dump(MethodInfo m, string tag)
    {
        try
        {
            var mi = MethodInfoOf(m);
            if (mi == IntPtr.Zero) { PLog.W($"  [{tag}] MethodInfo=0x0（拿不到）"); return IntPtr.Zero; }
            var p0 = *(IntPtr*)mi;
            var p1 = ((IntPtr*)mi)[1];
            PLog.W($"  [{tag}] MethodInfo=0x{mi.ToInt64():X} methodPointer=0x{p0.ToInt64():X} virtualMethodPointer=0x{p1.ToInt64():X}");
            return mi;
        }
        catch (Exception e)
        {
            PLog.W($"  [{tag}] 读指针失败: {e.GetType().Name} {e.Message}");
            return IntPtr.Zero;
        }
    }
}
