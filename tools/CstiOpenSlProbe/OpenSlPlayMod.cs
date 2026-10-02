using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using NL = System.Runtime.InteropServices.NativeLibrary;

[assembly: MelonInfo(typeof(CstiOpenSlProbe.OpenSlPlayMod), "CstiOpenSlProbe", "2.0.0", "dsh")]
[assembly: MelonGame(null, null)]

namespace CstiOpenSlProbe;

/// <summary>
/// v2：**随手触发**版 OpenSL ES 播放探针（**不碰 Unity 音频、不碰 APK**）。
///
/// 触发：点屏幕任意位置 → 播 0.8 秒 440Hz（峰值 0.35）。
///   · 泵 1（游戏内）：`CheatsManager.Update` **Postfix**（CstiQuickMenu 已验证可用）
///   · 泵 2（主菜单）：`FPSCounter.OnGUI` / `BasicSample.OnGUI` / `TestingCoroutines.OnGUI` 等 **Postfix**
///   · 兜底：启动后 12 秒自动播一次
/// 两次播放最短间隔 **1.5 秒**（防连点；也防 OnGUI 一帧多 pass 重复命中）。
///
/// ★ 线程纪律（上次 CstiICallFix 回归的教训）：
///   · **完全不用回调** —— 整段 PCM 一次性 Enqueue，原生音频线程永不回到托管侧；
///   · **不阻塞主线程** —— Enqueue + SetPlayState 都是微秒级调用，之后立即返回；
///     播放器**建一次复用**（不再每次 Destroy，也不 Sleep）。
/// </summary>
public class OpenSlPlayMod : MelonMod
{
    public override void OnInitializeMelon()
    {
        try { File.WriteAllText(OpenSlPlayer.LogPath, ""); } catch { }
        Write("== OpenSL v2（点屏即响）启动 ==");

        var h = new HarmonyLib.Harmony("csti.openslprobe.v2");
        Patch(h, "CheatsManager", "Update");
        Patch(h, "FPSCounter", "OnGUI");
        Patch(h, "BasicSample", "OnGUI");
        Patch(h, "TestingCoroutines", "OnGUI");
        Patch(h, "TestingPunch", "OnGUI");

        var t = new Thread(() => { try { Thread.Sleep(12000); } catch { } OpenSlPlayer.Play("启动兜底"); })
        { IsBackground = true, Name = "OpenSlAutoPlay" };
        t.Start();
        LoggerInstance.Msg("[OPENSL] v2 就绪：点屏幕即响（间隔>=1.5s）；12s 后兜底播一次");
    }

    public static void Write(string s)
    {
        try { File.AppendAllText(OpenSlPlayer.LogPath, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + "\n"); } catch { }
    }

    private static void Patch(HarmonyLib.Harmony h, string typeName, string method)
    {
        try
        {
            var t = AccessTools.TypeByName(typeName);
            if (t == null) { Write($"泵挂载: 找不到类型 {typeName}.{method}（跳过）"); return; }
            var m = AccessTools.Method(t, method);
            if (m == null) { Write($"泵挂载: 找不到方法 {typeName}.{method}（跳过）"); return; }
            var pf = typeof(OpenSlPlayMod).GetMethod(nameof(TickPostfix), BindingFlags.Static | BindingFlags.NonPublic);
            h.Patch(m, postfix: new HarmonyMethod(pf));
            Write($"泵挂载 OK: {typeName}.{method} Postfix");
            MelonLoader.MelonLogger.Msg("[OPENSL] 已挂 Postfix: " + typeName + "." + method);
        }
        catch (Exception e)
        {
            Write($"泵挂载异常 {typeName}.{method}: {e.GetType().Name} {e.Message}");
        }
    }

    /// <summary>所有泵共用：无参 Postfix，读输入 → 节流 → 播。</summary>
    private static void TickPostfix() => InputTicker.Tick("触屏");
}

/// <summary>输入节流。用 Environment.TickCount64 计时，零 Unity 依赖。</summary>
public static class InputTicker
{
    private static long _lastMs = -100000;
    private const int MinGapMs = 1500;

    public static void Tick(string src)
    {
        try
        {
            bool down = false;
            if (Input.touchCount > 0)
            {
                var t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Began) down = true;
            }
            if (!down && Input.GetMouseButtonDown(0)) down = true;
            if (!down) return;

            long now = Environment.TickCount64;
            if (now - _lastMs < MinGapMs) return;
            _lastMs = now;
            OpenSlPlayer.Play(src);
        }
        catch (Exception e)
        {
            OpenSlPlayer.Write("Tick 异常: " + e.GetType().Name + " " + e.Message);
        }
    }
}

/// <summary>OpenSL ES 播放器：建一次复用；一次性 Enqueue；不阻塞、无回调。</summary>
public static class OpenSlPlayer
{
    public static readonly string LogPath =
        "/sdcard/MelonLoader/com.winterspringgames.survivaljourney/opensl_play_probe.txt";
    public static readonly Action<string> Log = OpenSlPlayMod.Write;
    public static void Write(string s) => Log(s);

    const uint SL_BOOLEAN_FALSE = 0, SL_BOOLEAN_TRUE = 1;
    const uint SL_PLAYSTATE_PLAYING = 3;
    const uint SL_DATALOCATOR_ANDROIDSIMPLEBUFFERQUEUE = 0x800007BD;
    const uint SL_DATALOCATOR_OUTPUTMIX = 0x00000004;
    const uint SL_DATAFORMAT_PCM = 0x00000002;
    const uint SL_SAMPLINGRATE_48 = 48000000;
    const uint SL_PCMSAMPLEFORMAT_FIXED_16 = 16;
    const uint SL_SPEAKER_FRONT_LEFT = 0x1, SL_SPEAKER_FRONT_RIGHT = 0x2;
    const uint SL_BYTEORDER_LITTLEENDIAN = 0x1;

    const int RATE = 48000, CH = 2;
    const double SECONDS = 0.8, PEAK = 0.35;

    private static readonly object Gate = new object();
    private static bool _ready;
    private static IntPtr _pcm, _bufQ, _play, _player, _mix, _engine;
    private static int _bytes, _count;

    static IntPtr Vt(IntPtr itf) => Marshal.ReadIntPtr(itf);
    static IntPtr Slot(IntPtr itf, int i) => Marshal.ReadIntPtr(Vt(itf), i * IntPtr.Size);

    public static unsafe bool EnsureReady()
    {
        if (_ready) return true;
        var lib = NL.Load("libOpenSLES.so");
        Write("dlopen libOpenSLES.so -> 0x" + lib.ToInt64().ToString("X"));

        IntPtr Sym(string n)
        {
            try { return NL.GetExport(lib, n); }
            catch (Exception e) { Write("  GetExport(" + n + ") 失败: " + e.Message); return IntPtr.Zero; }
        }
        IntPtr Iid(string n)
        {
            var a = Sym(n);
            if (a == IntPtr.Zero) { Write("  !! IID 缺失: " + n); return IntPtr.Zero; }
            return Marshal.ReadIntPtr(a);
        }

        var pCreate = Sym("slCreateEngine");
        var iidEngine = Iid("SL_IID_ENGINE");
        var iidBufQ = Iid("SL_IID_ANDROIDSIMPLEBUFFERQUEUE");
        var iidPlay = Iid("SL_IID_PLAY");
        Write($"符号: slCreateEngine=0x{pCreate.ToInt64():X} IID_ENGINE=0x{iidEngine.ToInt64():X} "
              + $"IID_BUFFERQUEUE=0x{iidBufQ.ToInt64():X} IID_PLAY=0x{iidPlay.ToInt64():X}");
        if (pCreate == IntPtr.Zero || iidEngine == IntPtr.Zero || iidBufQ == IntPtr.Zero || iidPlay == IntPtr.Zero)
        { Write("!! 关键符号缺失"); return false; }

        var create = (delegate* unmanaged[Cdecl]<IntPtr*, uint, IntPtr, uint, IntPtr, IntPtr, int>)pCreate;
        IntPtr engine = IntPtr.Zero;
        int r = create(&engine, 0, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
        _engine = engine;
        Write($"slCreateEngine -> {r} engine=0x{_engine.ToInt64():X}");
        if (r != 0 || _engine == IntPtr.Zero) return false;

        Write($"engine->Realize -> {((delegate* unmanaged[Cdecl]<IntPtr, uint, int>)Slot(_engine, 0))(_engine, SL_BOOLEAN_FALSE)}");
        var getIfaceE = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr*, int>)Slot(_engine, 4);
        IntPtr eng = IntPtr.Zero;
        r = getIfaceE(_engine, iidEngine, &eng);
        Write($"engine->GetInterface(ENGINE) -> {r} eng=0x{eng.ToInt64():X}");
        if (r != 0 || eng == IntPtr.Zero) return false;

        var createMix = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr*, uint, IntPtr, IntPtr, int>)Slot(eng, 4);
        IntPtr mix = IntPtr.Zero;
        r = createMix(eng, &mix, 0, IntPtr.Zero, IntPtr.Zero);
        _mix = mix;
        Write($"eng->CreateOutputMix -> {r} mix=0x{_mix.ToInt64():X}");
        if (r != 0 || _mix == IntPtr.Zero) return false;
        Write($"mix->Realize -> {((delegate* unmanaged[Cdecl]<IntPtr, uint, int>)Slot(_mix, 0))(_mix, SL_BOOLEAN_FALSE)}");

        IntPtr locBufQ = Marshal.AllocHGlobal(8);
        Marshal.WriteInt32(locBufQ, 0, unchecked((int)SL_DATALOCATOR_ANDROIDSIMPLEBUFFERQUEUE));
        Marshal.WriteInt32(locBufQ, 4, 2);
        IntPtr fmt = Marshal.AllocHGlobal(28);
        Marshal.WriteInt32(fmt, 0, (int)SL_DATAFORMAT_PCM);
        Marshal.WriteInt32(fmt, 4, CH);
        Marshal.WriteInt32(fmt, 8, (int)SL_SAMPLINGRATE_48);
        Marshal.WriteInt32(fmt, 12, (int)SL_PCMSAMPLEFORMAT_FIXED_16);
        Marshal.WriteInt32(fmt, 16, (int)SL_PCMSAMPLEFORMAT_FIXED_16);
        Marshal.WriteInt32(fmt, 20, (int)(SL_SPEAKER_FRONT_LEFT | SL_SPEAKER_FRONT_RIGHT));
        Marshal.WriteInt32(fmt, 24, (int)SL_BYTEORDER_LITTLEENDIAN);
        IntPtr src = Marshal.AllocHGlobal(16);
        Marshal.WriteIntPtr(src, 0, locBufQ); Marshal.WriteIntPtr(src, 8, fmt);
        IntPtr locMix = Marshal.AllocHGlobal(16);
        Marshal.WriteInt32(locMix, 0, (int)SL_DATALOCATOR_OUTPUTMIX);
        Marshal.WriteIntPtr(locMix, 8, _mix);
        IntPtr sink = Marshal.AllocHGlobal(16);
        Marshal.WriteIntPtr(sink, 0, locMix); Marshal.WriteIntPtr(sink, 8, IntPtr.Zero);

        IntPtr ids = Marshal.AllocHGlobal(IntPtr.Size);
        Marshal.WriteIntPtr(ids, iidBufQ);
        IntPtr req = Marshal.AllocHGlobal(8);
        Marshal.WriteInt32(req, 0, (int)SL_BOOLEAN_TRUE);
        var createPlayer = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr*, IntPtr, IntPtr, uint, IntPtr, IntPtr, int>)Slot(eng, 2);
        IntPtr player = IntPtr.Zero;
        r = createPlayer(eng, &player, src, sink, 1, ids, req);
        _player = player;
        Write($"eng->CreateAudioPlayer -> {r} player=0x{_player.ToInt64():X}");
        if (r != 0 || _player == IntPtr.Zero) return false;
        Write($"player->Realize -> {((delegate* unmanaged[Cdecl]<IntPtr, uint, int>)Slot(_player, 0))(_player, SL_BOOLEAN_FALSE)}");
        var getIfaceP = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr*, int>)Slot(_player, 4);
        IntPtr bq = IntPtr.Zero, pl = IntPtr.Zero;
        int rq = getIfaceP(_player, iidBufQ, &bq), rp = getIfaceP(_player, iidPlay, &pl);
        _bufQ = bq; _play = pl;
        Write($"player->GetInterface(BUFFERQUEUE) -> {rq} 0x{bq.ToInt64():X} ; (PLAY) -> {rp} 0x{pl.ToInt64():X}");
        if (rq != 0 || rp != 0 || bq == IntPtr.Zero || pl == IntPtr.Zero) return false;

        int frames = (int)(RATE * SECONDS);
        _bytes = frames * CH * 2;
        _pcm = Marshal.AllocHGlobal(_bytes);
        short* p = (short*)_pcm;
        for (int i = 0; i < frames; i++)
        {
            short v = (short)(32767.0 * PEAK * Math.Sin(2.0 * Math.PI * 440.0 * i / RATE));
            p[i * 2] = v; p[i * 2 + 1] = v;
        }
        Write($"PCM 就绪: {_bytes} B ({SECONDS}s {RATE}Hz 立体声 16bit 峰值 {PEAK})");
        _ready = true;
        return true;
    }

    /// <summary>入队 + 播放 + 记日志。**微秒级返回，不阻塞主线程、无回调、不 Destroy。**</summary>
    public static unsafe void Play(string trigger)
    {
        lock (Gate)
        {
            try
            {
                _count++;
                Write($"--- 第 {_count} 次播放  触发源={trigger} ---");
                if (!EnsureReady()) { Write("  引擎未就绪，放弃"); return; }

                var enqueue = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, int>)Slot(_bufQ, 0);
                int re = enqueue(_bufQ, _pcm, (uint)_bytes);
                var setState = (delegate* unmanaged[Cdecl]<IntPtr, uint, int>)Slot(_play, 0);
                int rs = setState(_play, SL_PLAYSTATE_PLAYING);
                var getState = (delegate* unmanaged[Cdecl]<IntPtr, uint*, int>)Slot(_play, 1);
                uint st = 0; getState(_play, &st);
                Write($"  Enqueue({_bytes} B) -> {re}   ★SetPlayState(PLAYING) -> {rs}   GetPlayState -> {st} (3=PLAYING)");
            }
            catch (Exception e)
            {
                Write($"  !! 播放异常: {e.GetType().Name} {e.Message}");
                Write("  " + (e.StackTrace ?? ""));
            }
        }
    }
}

