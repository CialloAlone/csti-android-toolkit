using System;
using System.Collections.Generic;
using HarmonyLib;

namespace HarmonyProbe;

/// <summary>
/// 真正的「特性声明式」补丁类（走 Harmony.PatchAll 路径），用于对照
///   · 形式 N：typeof(X) + nameof(X.M)
///   · 形式 S：typeof(X) + "M"（方法名字符串）
/// 补丁体默认是**空**的；只有 ProbeMod.AttrVerbose=true 时才打一行日志。
/// </summary>
public static class AttrPatches
{
    // ================= 形式 N：nameof =================
    [HarmonyPatch(typeof(LocalizationManager), nameof(LocalizationManager.LoadLanguage))]
    public static class N_Loc_Pre { [HarmonyPrefix] public static void F() { if (ProbeMod.AttrVerbose) PLog.W("ATTR N loc pre"); } }

    [HarmonyPatch(typeof(LocalizationManager), nameof(LocalizationManager.LoadLanguage))]
    public static class N_Loc_Post { [HarmonyPostfix] public static void F() { if (ProbeMod.AttrVerbose) PLog.W("ATTR N loc post"); } }

    [HarmonyPatch(typeof(GuideManager), nameof(GuideManager.Start))]
    public static class N_Guide_Pre { [HarmonyPrefix] public static void F() { if (ProbeMod.AttrVerbose) PLog.W("ATTR N guide pre"); } }

    [HarmonyPatch(typeof(GuideManager), nameof(GuideManager.Start))]
    public static class N_Guide_Post { [HarmonyPostfix] public static void F() { if (ProbeMod.AttrVerbose) PLog.W("ATTR N guide post"); } }

    [HarmonyPatch(typeof(GraphicsManager), nameof(GraphicsManager.Init))]
    public static class N_Graphics_Pre { [HarmonyPrefix] public static void F() { if (ProbeMod.AttrVerbose) PLog.W("ATTR N graphics pre"); } }

    [HarmonyPatch(typeof(GraphicsManager), nameof(GraphicsManager.Init))]
    public static class N_Graphics_Post { [HarmonyPostfix] public static void F() { if (ProbeMod.AttrVerbose) PLog.W("ATTR N graphics post"); } }

    [HarmonyPatch(typeof(CheatsManager), nameof(CheatsManager.Update))]
    public static class N_CheatsUpd_Pre { [HarmonyPrefix] public static void F() { if (ProbeMod.AttrVerbose) PLog.W("ATTR N cheats.Update pre"); } }

    [HarmonyPatch(typeof(CheatsManager), nameof(CheatsManager.Update))]
    public static class N_CheatsUpd_Post { [HarmonyPostfix] public static void F() { if (ProbeMod.AttrVerbose) PLog.W("ATTR N cheats.Update post"); } }

    [HarmonyPatch(typeof(CheatsManager), nameof(CheatsManager.OnGUI))]
    public static class N_CheatsGui_Pre { [HarmonyPrefix] public static void F() { if (ProbeMod.AttrVerbose) PLog.W("ATTR N cheats.OnGUI pre"); } }

    [HarmonyPatch(typeof(CheatsManager), nameof(CheatsManager.OnGUI))]
    public static class N_CheatsGui_Post { [HarmonyPostfix] public static void F() { if (ProbeMod.AttrVerbose) PLog.W("ATTR N cheats.OnGUI post"); } }

    // ================= 形式 S：方法名字符串 =================
    [HarmonyPatch(typeof(LocalizationManager), "LoadLanguage")]
    public static class S_Loc_Pre { [HarmonyPrefix] public static void F() { if (ProbeMod.AttrVerbose) PLog.W("ATTR S loc pre"); } }

    [HarmonyPatch(typeof(LocalizationManager), "LoadLanguage")]
    public static class S_Loc_Post { [HarmonyPostfix] public static void F() { if (ProbeMod.AttrVerbose) PLog.W("ATTR S loc post"); } }

    [HarmonyPatch(typeof(GuideManager), "Start")]
    public static class S_Guide_Pre { [HarmonyPrefix] public static void F() { if (ProbeMod.AttrVerbose) PLog.W("ATTR S guide pre"); } }

    [HarmonyPatch(typeof(GuideManager), "Start")]
    public static class S_Guide_Post { [HarmonyPostfix] public static void F() { if (ProbeMod.AttrVerbose) PLog.W("ATTR S guide post"); } }

    [HarmonyPatch(typeof(GraphicsManager), "Init")]
    public static class S_Graphics_Pre { [HarmonyPrefix] public static void F() { if (ProbeMod.AttrVerbose) PLog.W("ATTR S graphics pre"); } }

    [HarmonyPatch(typeof(GraphicsManager), "Init")]
    public static class S_Graphics_Post { [HarmonyPostfix] public static void F() { if (ProbeMod.AttrVerbose) PLog.W("ATTR S graphics post"); } }

    [HarmonyPatch(typeof(CheatsManager), "Update")]
    public static class S_CheatsUpd_Pre { [HarmonyPrefix] public static void F() { if (ProbeMod.AttrVerbose) PLog.W("ATTR S cheats.Update pre"); } }

    [HarmonyPatch(typeof(CheatsManager), "Update")]
    public static class S_CheatsUpd_Post { [HarmonyPostfix] public static void F() { if (ProbeMod.AttrVerbose) PLog.W("ATTR S cheats.Update post"); } }

    [HarmonyPatch(typeof(CheatsManager), "OnGUI")]
    public static class S_CheatsGui_Pre { [HarmonyPrefix] public static void F() { if (ProbeMod.AttrVerbose) PLog.W("ATTR S cheats.OnGUI pre"); } }

    [HarmonyPatch(typeof(CheatsManager), "OnGUI")]
    public static class S_CheatsGui_Post { [HarmonyPostfix] public static void F() { if (ProbeMod.AttrVerbose) PLog.W("ATTR S cheats.OnGUI post"); } }

    // ================= 选择器 =================
    private static readonly Dictionary<string, Type> Map = new(StringComparer.OrdinalIgnoreCase);

    static AttrPatches()
    {
        void Add(string targetKey, string form, string kind, Type t) => Map[form + "|" + targetKey + "|" + kind] = t;

        Add("loc.LoadLanguage", "attr", "prefix", typeof(N_Loc_Pre));
        Add("loc.LoadLanguage", "attr", "postfix", typeof(N_Loc_Post));
        Add("guide.Start", "attr", "prefix", typeof(N_Guide_Pre));
        Add("guide.Start", "attr", "postfix", typeof(N_Guide_Post));
        Add("graphics.Init", "attr", "prefix", typeof(N_Graphics_Pre));
        Add("graphics.Init", "attr", "postfix", typeof(N_Graphics_Post));
        Add("cheats.Update", "attr", "prefix", typeof(N_CheatsUpd_Pre));
        Add("cheats.Update", "attr", "postfix", typeof(N_CheatsUpd_Post));
        Add("cheats.OnGUI", "attr", "prefix", typeof(N_CheatsGui_Pre));
        Add("cheats.OnGUI", "attr", "postfix", typeof(N_CheatsGui_Post));

        Add("loc.LoadLanguage", "attrname", "prefix", typeof(S_Loc_Pre));
        Add("loc.LoadLanguage", "attrname", "postfix", typeof(S_Loc_Post));
        Add("guide.Start", "attrname", "prefix", typeof(S_Guide_Pre));
        Add("guide.Start", "attrname", "postfix", typeof(S_Guide_Post));
        Add("graphics.Init", "attrname", "prefix", typeof(S_Graphics_Pre));
        Add("graphics.Init", "attrname", "postfix", typeof(S_Graphics_Post));
        Add("cheats.Update", "attrname", "prefix", typeof(S_CheatsUpd_Pre));
        Add("cheats.Update", "attrname", "postfix", typeof(S_CheatsUpd_Post));
        Add("cheats.OnGUI", "attrname", "prefix", typeof(S_CheatsGui_Pre));
        Add("cheats.OnGUI", "attrname", "postfix", typeof(S_CheatsGui_Post));
    }

    /// <summary>返回需要 PatchAll 的补丁类（kind=both 时返回两个）。</summary>
    public static Type[] Pick(string mode, string targetKey, string kind)
    {
        var list = new List<Type>();
        if (kind == "prefix" || kind == "both")
            if (Map.TryGetValue(mode + "|" + targetKey + "|prefix", out var a)) list.Add(a);
        if (kind == "postfix" || kind == "both")
            if (Map.TryGetValue(mode + "|" + targetKey + "|postfix", out var b)) list.Add(b);
        return list.Count == 0 ? null : list.ToArray();
    }
}
