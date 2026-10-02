"""
路 B 产品化：把**整个 mod 作者目录**的 *.wav 一次性烘焙进 APK。
（不占设备；产出 APK + manifest + ROLLBACK.json + 全套自证）

用法：
  python bake_mod_audio.py --mod <mod目录> [--mod <另一个>] --out <APK> [--base <基础APK>] [--stage <暂存目录>]

行为：
  逐个 WAV → FSB5(PCM16) → **追加**到 sharedassets0.resource（16B 对齐）
           → **新增一个 AudioClip 对象**（m_Name = WAV 文件名主干，必须与 mod JSON 的音效名逐字一致）
           → **锚定**：把新 clip 的 PPtr 追加进一个**已有音效数组**（**每个音效用不同锚点对象**，分散；只追加、不顶掉任何已有音效）
"""
import argparse, copy, json, os, struct, subprocess, sys, wave

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))
import UnityPy
from UnityPy.enums import ClassIDType

ASSETS = os.path.join(HERE, "assets")
OUT = os.path.join(HERE, "out")
ROOT = r"D:\RiderProjects\ml-installer-06"
SRC = b"sharedassets0.resource"
ALIGN, SPLIT = 16, 1048576
ZA = r"D:\devtools\HBuilderX\plugins\app-safe-pack\zipalign\zipalign.exe"
JAVA = r"D:\devtools\Java\jdk1.8.0_281\bin\java.exe"
ASIGN = r"D:\devtools\HBuilderX\plugins\app-safe-pack\apksigner.jar"
KS = r"C:\Users\HelloAlone\AppData\Local\Temp\csti-diag\sign\test.jks"

ap = argparse.ArgumentParser()
ap.add_argument("--mod", action="append", required=True)
ap.add_argument("--out", required=True)
ap.add_argument("--base", default=os.path.join(ROOT, "clone_stage", "ml06.apk"))
ap.add_argument("--stage", default=os.path.join(HERE, "apk_batch"))
ap.add_argument("--skip-sign", action="store_true")
A = ap.parse_args()

R = []
def say(n, d, ok):
    R.append((n, d, bool(ok)))
    print(f"  [{'PASS' if ok else 'FAIL'}] {n}: {d}")

# ---------------------------------------------------------------- 收集 WAV
wavs = []
for m in A.mod:
    for dirpath, _, files in os.walk(m):
        for fn in sorted(files):
            if fn.lower().endswith(".wav"):
                wavs.append((os.path.splitext(fn)[0], os.path.join(dirpath, fn), m))
print(f"收集到 {len(wavs)} 个 WAV：")
for nm, p, m in wavs:
    print(f"  {nm:16} {os.path.getsize(p):>9} B   {p}")
assert wavs, "没有找到 WAV"

# ---------------------------------------------------------------- 载入
AS = os.path.join(ASSETS, "sharedassets0.assets")
RESP = os.path.join(ASSETS, "sharedassets0.resource")
resorig = open(RESP, "rb").read()
env = UnityPy.load(AS)
f = env.file
clips = {}
for o in env.objects:
    if o.type == ClassIDType.AudioClip:
        t = o.read_typetree()
        clips[t["m_Name"]] = (o, t)
print(f"\n载入: 对象 {len(f.objects)}，AudioClip {len(clips)}")

# ---------------------------------------------------------------- 锚点候选（数组容器，按 count 降序，取不同对象）
cont = json.load(open(os.path.join(OUT, "ref_containers.json"), encoding="utf-8"))
best = {}
for r in cont["arrays"]:
    k = r["referrer_pid"]
    if k not in best or r["count"] > best[k]["count"]:
        best[k] = r
anchors = sorted(best.values(), key=lambda r: (-r["count"], str(r["referrer"])))
print(f"锚点候选对象（音效数组）{len(anchors)} 个；本批需要 {len(wavs)} 个")
assert len(anchors) >= len(wavs), f"锚点对象不够：{len(anchors)} < {len(wavs)}"
chosen = anchors[:len(wavs)]

# ---------------------------------------------------------------- 逐个烘焙
res_new = bytearray(resorig)
manifest = []
used_pids = set(f.objects)
obj_edits = {}          # anchor_pid -> (new_bytes, old_bytes, arr_off, old_count)
for i, (name, path, mod) in enumerate(wavs):
    with wave.open(path, "rb") as w:
        wch, wsw, wrate, wn = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        wraw = w.readframes(wn)
    want_dur = wn / wrate

    # 模板：优先同声道 + 名字对齐差是 8 的倍数
    new_pad = (4 + len(name.encode()) + 3) // 4 * 4
    tmpl = None
    for pref in (wch, None):
        for nm, (o, t) in sorted(clips.items()):
            if pref is not None and t["m_Channels"] != pref:
                continue
            old_pad = (4 + len(nm.encode()) + 3) // 4 * 4
            if (new_pad - old_pad) % 8:
                continue
            tmpl = (nm, o, t, old_pad)
            break
        if tmpl: break
    assert tmpl, f"{name}: 无匹配模板"
    tnm, to, tt, told_pad = tmpl
    traw = to.get_raw_data()
    tres_off, tres_sz = tt["m_Resource"]["m_Offset"], tt["m_Resource"]["m_Size"]

    # FSB5(PCM16)：用模板自己的头做骨架
    tblob = resorig[tres_off:tres_off + tres_sz]
    shsize = struct.unpack_from("<I", tblob, 0x0C)[0]
    hdr60, sh = bytearray(tblob[:0x3C]), tblob[0x3C:0x3C + shsize]
    samples = struct.unpack("<%dh" % (len(wraw) // 2), wraw)
    n_src = len(samples) // wch
    frames = [[samples[j * wch + c] for c in range(wch)] for j in range(n_src)]
    if wch != tt["m_Channels"]:
        frames = ([[v] * tt["m_Channels"] for v in samples] if wch == 1
                  else [[int(sum(samples[j * wch:(j + 1) * wch]) / wch)] for j in range(n_src)])
    if wrate != tt["m_Frequency"]:
        n = int(round(len(frames) * tt["m_Frequency"] / wrate)); out = []
        for j in range(n):
            p = j * (len(frames) - 1) / max(1, n - 1); i0 = int(p); fr = p - i0; i1 = min(i0 + 1, len(frames) - 1)
            out.append([int(round(frames[i0][c] * (1 - fr) + frames[i1][c] * fr)) for c in range(tt["m_Channels"])])
        frames = out
    pcm = bytearray()
    for fr_ in frames:
        for s in fr_:
            pcm += struct.pack("<h", max(-32768, min(32767, s)))
    struct.pack_into("<I", hdr60, 0x14, len(pcm))
    struct.pack_into("<I", hdr60, 0x18, 2)
    blob = bytes(hdr60) + sh + bytes(pcm)
    off = (len(res_new) + ALIGN - 1) // ALIGN * ALIGN
    res_new += b"\x00" * (off - len(res_new)) + blob

    # 新 AudioClip 对象
    nb = struct.pack("<i", len(name.encode())) + name.encode()
    nb += b"\x00" * ((4 + len(name.encode()) + 3) // 4 * 4 - len(nb))
    nobj = bytearray(nb + traw[told_pad:])
    d = bytes(nobj)
    sp = d.find(struct.pack("<i", len(SRC)) + SRC)
    assert sp >= 0, f"{name}: 找不到 m_Resource 字符串"
    q = sp + 4 + len(SRC)
    res_rel = None
    for al in (8, 4, 2, 1):
        rq = (q + al - 1) // al * al
        if struct.unpack_from("<qq", d, rq) == (tres_off, tres_sz):
            res_rel = rq; break
    assert res_rel is not None
    li = d.find(struct.pack("<f", tt["m_Length"]))
    assert li >= 0 and struct.unpack_from("<4i", d, li - 16) == (tt["m_LoadType"], tt["m_Channels"], tt["m_Frequency"], tt["m_BitsPerSample"])
    struct.pack_into("<qq", nobj, res_rel, off, len(blob))
    struct.pack_into("<f", nobj, li, want_dur)
    nobj = bytes(nobj)

    newpid = max(used_pids) + 1
    used_pids.add(newpid)
    new_o = copy.copy(to)
    new_o.path_id = newpid
    new_o.data = nobj
    new_o.byte_size = len(nobj)
    f.objects[newpid] = new_o

    # 锚定：追加到该锚点对象的音效数组末尾
    a = chosen[i]
    apid, cpos = a["referrer_pid"], a["arr_off"]
    aobj = f.objects[apid]
    ad = obj_edits[apid][1] if apid in obj_edits else aobj.get_raw_data()
    cnt = struct.unpack_from("<i", ad, cpos)[0]
    ins = cpos + 4 + cnt * 12
    nad = bytearray(ad[:ins] + struct.pack("<iq", 0, newpid) + ad[ins:])
    struct.pack_into("<i", nad, cpos, cnt + 1)
    obj_edits[apid] = (aobj, bytes(nad), cpos, cnt, ad)

    manifest.append(dict(name=name, wav=path, mod=mod, pid=newpid, offset=off, size=len(blob),
                         duration=want_dur, ch=tt["m_Channels"], freq=tt["m_Frequency"],
                         template=tnm, anchor=chosen[i]["referrer"], anchor_pid=apid,
                         anchor_arr_off=cpos, anchor_index=cnt, src_ch=wch, src_freq=wrate))
    print(f"  #{i} {name:16} pid={newpid} blob={len(blob):>7}B @0x{off:X} dur={want_dur:.4f}s "
          f"锚点={chosen[i]['referrer']}(pid={apid}) 第 {cnt} 项")

for apid, (aobj, nad, cpos, cnt, ad) in obj_edits.items():
    aobj.set_raw_data(nad)
    aobj.byte_size = len(nad)

out_assets = f.save()
print(f"\n保存 .assets: {len(open(AS,'rb').read())} -> {len(out_assets)} B；对象数 {len(f.objects)}")

# ---------------------------------------------------------------- 落盘 + 打包
stage = A.stage
os.makedirs(stage, exist_ok=True)
open(os.path.join(stage, "sharedassets0.assets"), "wb").write(out_assets)
open(os.path.join(stage, "sharedassets0.resource"), "wb").write(bytes(res_new))
nparts = (len(out_assets) + SPLIT - 1) // SPLIT
for i in range(nparts):
    open(os.path.join(stage, f"sharedassets0.assets.split{i}"), "wb").write(out_assets[i * SPLIT:(i + 1) * SPLIT])
json.dump(manifest, open(os.path.join(stage, "manifest.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
json.dump(dict(base_apk=A.base, assets_before=len(open(AS, "rb").read()), assets_after=len(out_assets),
               resource_before=len(resorig), resource_after=len(res_new), split_parts=nparts,
               clips_before=len(clips), clips_after=len(clips) + len(wavs),
               anchors={str(k): dict(arr_off=v[2], old_count=v[3], new_count=v[3] + 1) for k, v in obj_edits.items()},
               sounds=[m["name"] for m in manifest], rollback="装回 logs\\installed_base.apk 即可完全还原"),
          open(os.path.join(stage, "ROLLBACK.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)

subprocess.run([sys.executable, os.path.join(HERE, "build_apk.py"), "--src", stage, "--out", A.out, "--base", A.base],
               check=True)

# ---------------------------------------------------------------- 自证
print("\n=== 自证 ===")
env2 = UnityPy.load(os.path.join(stage, "sharedassets0.assets"))
c2, allc = {}, 0
for o in env2.objects:
    if o.type == ClassIDType.AudioClip:
        allc += 1
        t = o.read_typetree()
        c2[t["m_Name"]] = (o, t)
say("① 新 clip 全部存在", f"AudioClip {len(clips)} -> {allc}（期望 {len(clips)+len(wavs)}）", allc == len(clips) + len(wavs))
bad = [m["name"] for m in manifest if m["name"] not in c2]
say("② 每个新 clip 的 m_Name 与 manifest 一致", f"缺失 {len(bad)} {bad[:5]}", not bad)
bad = [(m["name"], c2[m["name"]][1]["m_Length"], m["duration"]) for m in manifest
       if abs(c2[m["name"]][1]["m_Length"] - m["duration"]) > 1e-5]
say("③ m_Length 与 WAV 时长一致（<1e-5）", f"超差 {len(bad)} 个 {bad[:3]}", not bad)
bad = []
for nm, (o, t) in clips.items():
    if nm in c2:
        t2 = c2[nm][1]
        if (t2["m_Resource"]["m_Offset"], t2["m_Resource"]["m_Size"]) != (t["m_Resource"]["m_Offset"], t["m_Resource"]["m_Size"]):
            bad.append(nm)
say("④ 原 127 个 clip 的 offset/size 逐条不变", f"变化 {len(bad)} 个 {bad[:3]}", not bad)
bad = []
for apid, (aobj, nad, cpos, cnt, ad) in obj_edits.items():
    ao = next(o for o in env2.objects if o.path_id == apid)
    nd = ao.get_raw_data()
    ncnt = struct.unpack_from("<i", nd, cpos)[0]
    exp = cnt + sum(1 for m in manifest if m["anchor_pid"] == apid)
    if len(nd) != len(ad) + (exp - cnt) * 12 or ncnt != exp:
        bad.append((apid, len(ad), len(nd), cnt, ncnt, exp))
say("⑤ 每个锚点只增长「新增项×12」字节", f"异常 {len(bad)} 个 {bad[:3]}", not bad)
nb = []
for apid, (aobj, nad, cpos, cnt, ad) in obj_edits.items():
    ao = next(o for o in env2.objects if o.path_id == apid)
    nd = ao.get_raw_data()
    ins = cpos + 4
    if nd[:cpos] != ad[:cpos] or nd[cpos + 4 + (len(nd) - len(ad)):] != ad[cpos + 4:]:
        nb.append(apid)
say("⑤b 锚点除 count 与新增项外其余字节不变", f"异常 {len(nb)} 个", not nb)
joined = b"".join(open(os.path.join(stage, f"sharedassets0.assets.split{i}"), "rb").read() for i in range(nparts))
say("⑥ 分片无损拼回", f"{nparts} 片 -> {len(joined)}", joined == out_assets)

if not A.skip_sign:
    pre = A.out.replace(".apk", ".prestored.apk")
    import shutil
    shutil.move(A.out, pre) if False else None
    # build_apk.py 已产出 <out>（覆盖后未 STORED）与 <out>.prestored.apk
    pre = A.out.replace(".apk", ".prestored.apk")
    alg = A.out.replace(".apk", ".aligned.apk")
    subprocess.run([ZA, "-f", "16384", pre, alg], check=True)
    subprocess.run([JAVA, "-jar", ASIGN, "sign", "--ks", KS, "--ks-key-alias", "t",
                    "--ks-pass", "pass:123456", "--key-pass", "pass:123456", "--out", A.out, alg], check=True)
    os.remove(pre); os.remove(alg)
    import zipfile
    z = zipfile.ZipFile(A.out)
    infos = z.infolist()
    so = [x for x in infos if x.filename.startswith("lib/") and x.filename.endswith(".so")]
    say("⑦ lib/**/*.so 全 STORED", f"{len(so)} 个，非 STORED {sum(1 for x in so if x.compress_type != 0)}",
        all(x.compress_type == 0 for x in so))
    say("⑦b resources.arsc STORED", f"{[x.compress_type for x in infos if x.filename=='resources.arsc']}",
        [x.compress_type for x in infos if x.filename == "resources.arsc"] == [0])
    r = subprocess.run([JAVA, "-jar", ASIGN, "verify", "--print-certs", A.out], capture_output=True, text=True)
    say("⑦c apksigner verify", "exit=%d" % r.returncode, r.returncode == 0)
    import re
    sha = re.search(r"SHA-256 digest: (\w+)", r.stdout)
    ins = subprocess.run([JAVA, "-jar", ASIGN, "verify", "--print-certs", os.path.join(ROOT, "logs", "installed_base.apk")],
                         capture_output=True, text=True)
    sha2 = re.search(r"SHA-256 digest: (\w+)", ins.stdout)
    say("⑦d 证书与装机包一致", f"{sha.group(1)[:16] if sha else '?'} vs {sha2.group(1)[:16] if sha2 else '?'}",
        bool(sha and sha2 and sha.group(1) == sha2.group(1)))
    # 从最终 APK 回读新 clip
    z2 = zipfile.ZipFile(A.out)
    parts = [z2.read(f"assets/bin/Data/sharedassets0.assets.split{i}") for i in range(nparts)]
    tmp = os.path.join(stage, "_apk_readback.bin")
    open(tmp, "wb").write(b"".join(parts))
    env3 = UnityPy.load(tmp)
    c3 = {o.read_typetree().get("m_Name") for o in env3.objects if o.type == ClassIDType.AudioClip}
    miss = [m["name"] for m in manifest if m["name"] not in c3]
    say("⑧ 从最终 APK 回读全部新 clip", f"缺失 {len(miss)} {miss[:5]}", not miss)
    os.remove(tmp)

json.dump([dict(name=n, detail=d, ok=o) for n, d, o in R],
          open(os.path.join(stage, "VERIFY.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print(f"\n产物: {A.out}")
print(f"manifest: {os.path.join(stage,'manifest.json')}   ROLLBACK: {os.path.join(stage,'ROLLBACK.json')}")
print("整体:", "全部自证通过" if all(x[2] for x in R) else "**有自证未通过**")
