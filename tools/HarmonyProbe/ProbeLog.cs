using System;
using System.IO;
using MelonLoader;

namespace HarmonyProbe;

/// <summary>
/// 双通道日志：MelonLoader Latest.log（带时间戳） + 自己维护的追加式文本文件（每次 open/close，
/// 保证崩溃前最后一行一定已落盘）。里程碑与补丁体命中都用它。
/// </summary>
public static class PLog
{
    private static string _path;
    private static readonly object Gate = new();

    public static string Path => _path;

    public static void Init(string path)
    {
        _path = path;
#if !PROBE_NOFILEIO
        try
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(path, "==================== HarmonyProbe run " +
                                      DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====================\n");
        }
        catch { }
#endif
    }

    public static void W(string msg)
    {
        var line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg;
        try { MelonLogger.Msg("[PROBE] " + msg); } catch { }
#if !PROBE_NOFILEIO
        lock (Gate)
        {
            try { if (_path != null) File.AppendAllText(_path, line + "\n"); } catch { }
        }
#endif
    }

    /// <summary>只写自己的文件，不走 MelonLogger（用于隔离“从后台线程调用 MelonLoader 日志器”这一变量）。</summary>
    public static void Wf(string msg)
    {
        var line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg;
        lock (Gate)
        {
            try { if (_path != null) File.AppendAllText(_path, line + "\n"); } catch { }
        }
    }

    public static void E(string msg)
    {
        var line = DateTime.Now.ToString("HH:mm:ss.fff") + " !! " + msg;
        try { MelonLogger.Error("[PROBE] " + msg); } catch { }
        lock (Gate)
        {
            try { if (_path != null) File.AppendAllText(_path, line + "\n"); } catch { }
        }
    }
}
