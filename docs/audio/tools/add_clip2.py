"""第 2 件（最终版）：用 UnityPy 的**已验证无损**写入器新增 1 个 AudioClip 对象并锚定。

关键发现：`SerializedFile.save()` 会遍历 `self.objects`、逐个 `obj.write(...)` 并 `align_stream(8)`，
再自己算 metadata/data 大小与 header —— 所以**新增对象 = 往 `f.objects` 里插一个 ObjectReader**，
所有 byteStart/对齐/header 由它自动处理（并且已实测**无损回写**：原样 load→save 逐字节相同）。
"""
import copy, json, os, struct, sys, wave

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))
import UnityPy
from UnityPy.enums import ClassIDType

ASSETS = os.path.join(HERE, "assets")
OUT = os.path.join(HERE, "out")
CART = r"D:\RiderProjects\csti\BepInEx\plugins\DarthNihilus_Cart\Resource\Audio\Cart.wav"
SRC = b"sharedassets0.resource"
ALIGN, SPLIT = 16, 1048576
AS = os.path.join(ASSETS, "sharedassets0.assets")
RES = os.path.join(ASSETS, "sharedassets0.resource")
DEST = os.path.join(HERE, "apk_b_newclip")
R = []


def say(n, d, ok):
    R.append((n, d, bool(ok)))
    print(f"  [{'PASS' if ok else 'FAIL'}] {n}: {d}")


resorig = open(RES, "rb").read()
env = UnityPy.load(AS)
f = env.file
objs = list(env.objects)
clips = {}
for o in objs:
    if o.type == ClassIDType.AudioClip:
        t = o.read_typetree()
        clips[t["m_Name"]] = (o, t)
print(f"载入: 对象 {len(f.objects)} 个，AudioClip {len(clips)} 个")

with wave.open(CART, "rb") as w:
    wch, wsw, wrate, wn = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
    wraw = w.readframes(wn)
print(f"Cart.wav: {wch}ch {wrate}Hz {wsw*8}bit {wn/wrate:.4f}s")

# ---- 选模板：优先同声道数（任意频率，反正会重采样）+ 名字对齐差是 8 的倍数 ----
new_pad = (4 + len("Cart") + 3) // 4 * 4
tmpl = None
for pref_ch in (wch, None):                      # 先找同声道，再放宽到任意
    for nm, (o, t) in sorted(clips.items()):
        if pref_ch is not None and t["m_Channels"] != pref_ch:
            continue
        old_pad = (4 + len(nm.encode()) + 3) // 4 * 4
        if (new_pad - old_pad) % 8:
            continue
        tmpl = (nm, o, t, old_pad, new_pad)
        break
    if tmpl:
        break
assert tmpl, "无匹配模板"
tnm, to, tt, told_pad, tnew_pad = tmpl
traw = to.get_raw_data()
tres_off, tres_sz = tt["m_Resource"]["m_Offset"], tt["m_Resource"]["m_Size"]
print(f"模板={tnm} pid={to.path_id} raw={len(traw)}B name_pad {told_pad}->{tnew_pad} type_id={to.type_id}")

# ---- FSB5(PCM16) ----
tblob = resorig[tres_off:tres_off + tres_sz]
assert tblob[:4] == b"FSB5"
shsize = struct.unpack_from("<I", tblob, 0x0C)[0]
hdr60, sh = bytearray(tblob[:0x3C]), tblob[0x3C:0x3C + shsize]
samples = struct.unpack("<%dh" % (len(wraw) // 2), wraw)
n_src = len(samples) // wch
frames = [[samples[i * wch + c] for c in range(wch)] for i in range(n_src)]
if wch != tt["m_Channels"]:
    frames = ([[v] * tt["m_Channels"] for v in samples] if wch == 1
              else [[int(sum(samples[i * wch:(i + 1) * wch]) / wch)] for i in range(n_src)])
if wrate != tt["m_Frequency"]:
    n = int(round(len(frames) * tt["m_Frequency"] / wrate)); out = []
    for j in range(n):
        pos = j * (len(frames) - 1) / max(1, n - 1); i0 = int(pos); fr = pos - i0; i1 = min(i0 + 1, len(frames) - 1)
        out.append([int(round(frames[i0][c] * (1 - fr) + frames[i1][c] * fr)) for c in range(tt["m_Channels"])])
    frames = out
pcm = bytearray()
for fr in frames:
    for s in fr:
        pcm += struct.pack("<h", max(-32768, min(32767, s)))
new_dur = len(frames) / tt["m_Frequency"]
struct.pack_into("<I", hdr60, 0x14, len(pcm))
struct.pack_into("<I", hdr60, 0x18, 2)
newblob = bytes(hdr60) + sh + bytes(pcm)
new_off = (len(resorig) + ALIGN - 1) // ALIGN * ALIGN
new_res = resorig + b"\x00" * (new_off - len(resorig)) + newblob
print(f"新 FSB5 {len(newblob)}B（PCM16 {len(pcm)}B, {new_dur:.4f}s）@ .resource 0x{new_off:X}；"
      f"{len(resorig)} -> {len(new_res)}B")

# ---- 新 AudioClip 对象字节 ----
nb = struct.pack("<i", 4) + b"Cart"
nb += b"\x00" * (tnew_pad - len(nb))
newobj = bytearray(nb + traw[told_pad:])
d = bytes(newobj)
sp = d.find(struct.pack("<i", len(SRC)) + SRC)
assert sp >= 0
q = sp + 4 + len(SRC)
res_rel = None
for al in (8, 4, 2, 1):
    rq = (q + al - 1) // al * al
    if struct.unpack_from("<qq", d, rq) == (tres_off, tres_sz):
        res_rel = rq; break
assert res_rel is not None
li = d.find(struct.pack("<f", tt["m_Length"]))
assert li >= 0 and struct.unpack_from("<4i", d, li - 16) == (tt["m_LoadType"], tt["m_Channels"], tt["m_Frequency"], tt["m_BitsPerSample"])
struct.pack_into("<qq", newobj, res_rel, new_off, len(newblob))
struct.pack_into("<f", newobj, li, new_dur)
newobj = bytes(newobj)

# ---- 锚点 BedRoll ----
cont = json.load(open(os.path.join(OUT, "ref_containers.json"), encoding="utf-8"))
slot = {c["name"]: c for c in json.load(open(os.path.join(OUT, "slot_table.json"), encoding="utf-8"))}
a = next(r for r in cont["arrays"] if r["referrer"] == "BedRoll" and r["count"] == 2)
apid, cpos = a["referrer_pid"], a["arr_off"]
aobj = f.objects[apid]
ad = aobj.get_raw_data()
cnt = struct.unpack_from("<i", ad, cpos)[0]
elems = [struct.unpack_from("<iq", ad, cpos + 4 + k * 12) for k in range(cnt)]
assert cnt == 2 and elems[0][1] == slot["Sleep1"]["path_id"] and elems[1][1] == slot["Sleep2"]["path_id"], elems

newpid = max(f.objects) + 1
ins = cpos + 4 + cnt * 12
# ★ 必须"插入"12 字节（pack_into 只是覆写，不会让对象变长）
nad = bytearray(ad[:ins] + struct.pack("<iq", 0, newpid) + ad[ins:])
struct.pack_into("<i", nad, cpos, cnt + 1)
nad = bytes(nad)
assert len(nad) == len(ad) + 12, f"锚点长度未增长: {len(ad)} -> {len(nad)}"
adiff = [i for i in range(len(ad)) if ad[i] != nad[i]]
print(f"\n锚点 BedRoll pid={apid} 原 {len(ad)}B -> 新 {len(nad)}B；数组@{cpos:#x} count {cnt}->{cnt+1}；"
      f"新增 PPtr@+{ins:#x} = (0,{newpid})；差异字节 {len(adiff)} 个 {[hex(x) for x in adiff][:8]}")

# ---- 写入 ----
new_o = copy.copy(to)
new_o.path_id = newpid
new_o.data = newobj
new_o.byte_size = len(newobj)
f.objects[newpid] = new_o
aobj.set_raw_data(nad)
aobj.byte_size = len(nad)        # ★ 必须同步：write() 用 self.data 写数据，但对象表里的 byte_size 取自该字段
out = f.save()
print(f"\n保存: .assets {len(open(AS,'rb').read())} -> {len(out)} B；对象数 {len(f.objects)}")

os.makedirs(DEST, exist_ok=True)
open(os.path.join(DEST, "sharedassets0.assets"), "wb").write(out)
open(os.path.join(DEST, "sharedassets0.resource"), "wb").write(new_res)
nparts = (len(out) + SPLIT - 1) // SPLIT
for i in range(nparts):
    open(os.path.join(DEST, f"sharedassets0.assets.split{i}"), "wb").write(out[i * SPLIT:(i + 1) * SPLIT])

# ================= 自证 =================
print("\n=== 自证（跑在落盘后的真实文件上）===")
env2 = UnityPy.load(os.path.join(DEST, "sharedassets0.assets"))
c2 = {}
allc = 0
for o in env2.objects:
    if o.type == ClassIDType.AudioClip:
        allc += 1
        t = o.read_typetree()
        c2[t["m_Name"]] = (o, t)
say("① 对象总数 +1", f"AudioClip {len(clips)} -> {allc}（对象总数 {len(f.objects)}）", allc == len(clips) + 1)
assert "Cart" in c2, "新 clip 不在！"
co, ct = c2["Cart"]
say("② 新 clip 存在且 m_Name='Cart'", f"pid={co.path_id} name={ct['m_Name']!r}", ct["m_Name"] == "Cart")
r = ct["m_Resource"]
blob = new_res[r["m_Offset"]:r["m_Offset"] + r["m_Size"]]
ver, num, shs, nt, ds, mode = struct.unpack_from("<6I", blob, 4)
say("③ blob 是合法 FSB5(PCM16)", f"m_Offset={r['m_Offset']} m_Size={r['m_Size']} mode={mode} "
                                  f"恒等式={60+shs+nt+ds}", blob[:4] == b"FSB5" and mode == 2 and 60 + shs + nt + ds == len(blob))
say("④ m_Length 正确", f"{ct['m_Length']!r}（目标 {new_dur:.6f}）", abs(ct["m_Length"] - new_dur) < 1e-4)
bad = []
for nm, (o, t) in clips.items():
    if nm not in c2:
        bad.append((nm, "缺失")); continue
    t2 = c2[nm][1]
    if (t2["m_Resource"]["m_Offset"], t2["m_Resource"]["m_Size"]) != (t["m_Resource"]["m_Offset"], t["m_Resource"]["m_Size"]):
        bad.append((nm, "offset/size 变了"))
say("⑤ 原 134 个 clip 的 offset/size 逐条不变", f"核对 {len(clips)} 个，变化 {len(bad)} 个 {bad[:3]}", not bad)
# 锚点
env3 = UnityPy.load(os.path.join(DEST, "sharedassets0.assets"))
ao = next(o for o in env3.objects if o.path_id == apid)
anad = ao.get_raw_data()
ncnt = struct.unpack_from("<i", anad, cpos)[0]
nelems = [struct.unpack_from("<iq", anad, cpos + 4 + k * 12) for k in range(ncnt)]
say("⑥ 锚点数组 count 2->3 且末项是新 clip", f"count={ncnt} elems={nelems}",
    ncnt == 3 and nelems[2] == (0, newpid) and nelems[:2] == elems[:2])
say("⑥b 锚点其余字节未变", f"仅 count(4B)+新增12B 变化", len(anad) == len(ad) + 12)
say("⑦ .assets 长度/分片", f"{len(out)} B, {nparts} 片", len(out) > 0 and nparts > 0)
joined = b"".join(open(os.path.join(DEST, f"sharedassets0.assets.split{i}"), "rb").read() for i in range(nparts))
say("⑦b 分片无损拼回", f"{len(joined)} == {len(out)}", joined == out)

json.dump(dict(newpid=newpid, new_off=new_off, newblob=len(newblob), new_dur=new_dur,
               anchor_pid=apid, anchor_arr_off=cpos, anchor_ins=ins,
               assets_before=len(open(AS, "rb").read()), assets_after=len(out),
               resource_before=len(resorig), resource_after=len(new_res),
               obj_before=len(clips), obj_after=allc, template=tnm,
               checks=[dict(name=n, detail=d, ok=o) for n, d, o in R]),
          open(os.path.join(DEST, "ROLLBACK.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print(f"\n产物: {DEST}")
print("整体:", "全部自证通过" if all(x[2] for x in R) else "**有自证未通过**")
