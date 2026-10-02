"""
路 A+（推荐路线）：**追加 blob + 重指向**。

和路 A（等长原地替换）的区别：
  · 路 A ：把新 blob 覆盖在**原槽位**上，长度必须完全相等 → **时长受槽位大小限制**。
  · 路 A+：把新 blob **追加**到 `.resource` 末尾（16 字节对齐），再把槽位 clip 的
           `m_Resource.m_Offset/m_Size` **重指向**它 → **时长不受限**，且
           · 追加不移动任何已有数据 → 现有 offset 全部继续有效，**不需要重排**
           · 原始音频仍留在文件里 → **可一条命令回滚**
           · `.assets` 仍是**等长就地修改** → **不需要重分片**

用法：
  python patch_aplus.py --clip ChoppingTrunk --wav <wav> [--codec pcm16] [--dest apk_test_aplus]

产出（dest 目录）：
  sharedassets0.resource / sharedassets0.assets + .split0..N
  ROLLBACK.json     ← 原 offset/size/名称/时长，用于还原
  APLUS_REPORT.txt/json
"""
import argparse, json, os, struct, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))
OUT = os.path.join(HERE, "out")
ASSETS = os.path.join(HERE, "assets")
HDR = 0x3C
SPLIT = 1048576
ALIGN = 16


from fsb5_make import read_wav, convert, parse_fsb5   # 复用已验证的 FSB5 构造

REPORT = []


def say(name, detail, ok):
    REPORT.append((name, detail, bool(ok)))
    print(f"  [{'PASS' if ok else 'FAIL'}] {name}: {detail}")


def build_blob(template_blob, frames, ch, rate):
    """按路 A 的最小改动原则构造 FSB5(PCM16)：照抄模板 60B 头 + 样本头，只改 mode/dataSize。"""
    o = parse_fsb5(template_blob)
    sh = o["shSize"]
    pcm = bytearray()
    for fr in frames:
        for s in fr:
            pcm += struct.pack("<h", max(-32768, min(32767, s)))
    hdr = bytearray(o["prefix60"])
    struct.pack_into("<I", hdr, 0x14, len(pcm))      # dataSize
    struct.pack_into("<I", hdr, 0x18, 2)             # mode = PCM16
    blob = bytes(hdr) + o["sampleHdr"] + bytes(pcm)
    # 注意：convert() 返回的 frames 是"逐帧"列表（每帧 ch 个样本），帧数 = len(frames)，
    # 不能再除以 ch。用两条互相独立的式子交叉校验，防止写错 m_Length。
    n_frames = len(frames)
    seconds = n_frames / rate
    seconds_check = len(pcm) / (2 * ch * rate)
    assert abs(seconds - seconds_check) < 1e-9, f"时长自校验失败 {seconds} vs {seconds_check}"
    info = dict(shSize=sh, pcm_bytes=len(pcm), frames=n_frames,
                seconds=seconds, seconds_check=seconds_check, blob_len=len(blob),
                orig_dataSize=o["dataSize"], orig_mode=o["mode"])
    return blob, info


def locate_resource_fields(raw, known_off, known_size, src_name):
    """在对象原始字节里定位 m_Resource.m_Offset / m_Size，并用已知值自校验。"""
    s = raw.find(struct.pack("<i", len(src_name)) + src_name)
    if s < 0:
        return None
    p = s + 4 + len(src_name)
    for align in (8, 4, 2, 1):
        q = (p + align - 1) // align * align
        if q + 16 > len(raw):
            continue
        off, size = struct.unpack_from("<qq", raw, q)
        if off == known_off and size == known_size:
            return q, p
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--clip", required=True)
    ap.add_argument("--wav", required=True)
    ap.add_argument("--codec", default="pcm16", choices=["pcm16", "ima"])
    ap.add_argument("--dest", default=os.path.join(HERE, "apk_test_aplus"))
    a = ap.parse_args()

    if a.codec != "pcm16":
        print("!! --codec ima 未实现。原因见 docs\\audio\\MOD-AUDIO.md §4：")
        print("   路 A+ 已经消除了容量瓶颈，IMA 编码器没有容量收益；")
        print("   而 FSB5 的 IMA 样本头语义（字段 4..7）与 IMA 块布局我们**没有逐字节反推过**，")
        print("   盲写会产出未经真机验证的代码。真要做请先补一次布局反推 + 真机验证。")
        return 3

    # 槽位表优先（跨 3 个 .assets 的 135 个 clip + 引用数）；没有再退回旧 inventory（只有 sharedassets0）
    st_path = os.path.join(OUT, "slot_table.json")
    if os.path.exists(st_path):
        inv = json.load(open(st_path, encoding="utf-8"))
        for r in inv:
            r.setdefault("source", r.get("resource_file"))
            r.setdefault("channels", r.get("ch"))
            r.setdefault("frequency", r.get("freq"))
    else:
        inv = json.load(open(os.path.join(OUT, "csti_audioclip_inventory.json"), encoding="utf-8"))
        for r in inv:
            r.setdefault("source", r.get("resource_file", r.get("source")))
    cand = [c for c in inv if c["name"] == a.clip]
    if not cand:
        print("找不到 clip:", a.clip); return 1
    c = cand[0]
    print(f"目标槽位: {c['name']}  {c['file'] if 'file' in c else c['source']}  "
          f"ch={c['channels']} freq={c['frequency']} loadType={c['loadType']} fmt={c['fmt']} "
          f"off={c['offset']} size={c['size']} 原时长={c['length']}s")

    res_path = os.path.join(ASSETS, c["source"])
    as_name = c["source"].replace(".resource", ".assets")
    as_path = os.path.join(ASSETS, as_name)
    res0 = open(res_path, "rb").read()
    raw0 = open(as_path, "rb").read()
    src_name = c["source"]

    samples, wch, wrate = read_wav(a.wav)
    src_dur = len(samples) / wch / wrate
    frames = convert(samples, wch, wrate, c["channels"], c["frequency"])
    new_dur = len(frames) / c["frequency"]
    print(f"WAV: {wch}ch {wrate}Hz  {src_dur:.4f}s  ->  转换后 {c['channels']}ch {c['frequency']}Hz "
          f"{len(frames)} frames {new_dur:.4f}s")

    template = res0[c["offset"]:c["offset"] + c["size"]]
    blob, bi = build_blob(template, frames, c["channels"], c["frequency"])
    print(f"新 blob: {bi['blob_len']} B（含 {bi['pcm_bytes']} B PCM16）；原 blob {c['size']} B；"
          f"原槽位若等长替换只能放 {(c['size']-96)/(2*c['channels']*c['frequency']):.4f}s，"
          f"现在放了 {bi['seconds']:.4f}s → **时长不再受限**")

    # ---------------- 追加（16 字节对齐）----------------
    new_off = (len(res0) + ALIGN - 1) // ALIGN * ALIGN
    pad = new_off - len(res0)
    new_res = bytearray(res0) + b"\x00" * pad + blob
    print(f"追加: 原 .resource {len(res0)} B --(+{pad} 对齐)--> offset={new_off} (0x{new_off:X}) "
          f"-> 新 {len(new_res)} B")

    # ---------------- 定位并改写 .assets ----------------
    import UnityPy
    from UnityPy.enums import ClassIDType
    env = UnityPy.load(as_path)
    obj = None
    for o in env.objects:
        if o.type == ClassIDType.AudioClip:
            try:
                if o.read_typetree().get("m_Name") == c["name"]:
                    obj = o; break
            except Exception:
                pass
    if obj is None:
        print("找不到对象"); return 1
    t = obj.read_typetree()
    oraw = obj.get_raw_data()
    pos = raw0.find(oraw)
    if pos < 0 or raw0.find(oraw, pos + 1) >= 0:
        print("对象字节在文件里不唯一/找不到"); return 1

    loc = locate_resource_fields(oraw, c["offset"], c["size"], src_name.encode())
    if not loc:
        print("定位 m_Resource.m_Offset/m_Size 失败（自校验不过）"); return 1
    res_rel, after_str = loc
    print(f"定位: 对象@0x{pos:X}  m_Resource 字符串@{after_str}  ->  m_Offset/m_Size@对象内+0x{res_rel:X}")

    patched = bytearray(raw0)
    len_off = None
    pat = struct.pack("<f", t["m_Length"])
    inner = oraw.find(pat)
    if inner >= 0:
        ctx = struct.unpack_from("<4i", oraw, inner - 16)
        if ctx == (t["m_LoadType"], t["m_Channels"], t["m_Frequency"], t["m_BitsPerSample"]):
            len_off = pos + inner
    if len_off is None:
        print("定位 m_Length 失败"); return 1

    struct.pack_into("<qq", patched, pos + res_rel, new_off, bi["blob_len"])
    struct.pack_into("<f", patched, len_off, bi["seconds"])

    diff = sorted(set(i for i in range(len(raw0)) if raw0[i] != patched[i]))
    expect = set(range(pos + res_rel, pos + res_rel + 16)) | set(range(len_off, len_off + 4))
    say("① .assets 差异字节数符合预期", f"{len(diff)} 个，范围 = m_Resource(16B) + m_Length(4B)", set(diff) <= expect and len(diff) > 0)

    os.makedirs(a.dest, exist_ok=True)
    open(os.path.join(a.dest, os.path.basename(res_path)), "wb").write(bytes(new_res))
    open(os.path.join(a.dest, as_name), "wb").write(bytes(patched))
    diska = bytes(patched)
    nparts = (len(diska) + SPLIT - 1) // SPLIT
    for i in range(nparts):
        open(os.path.join(a.dest, f"{as_name}.split{i}"), "wb").write(diska[i * SPLIT:(i + 1) * SPLIT])

    # ---------------- 自证 ----------------
    print("\n=== A+ 自证 ===")
    disk_res = open(os.path.join(a.dest, os.path.basename(res_path)), "rb").read()
    say("② .resource 新长度 = 原长 + 对齐填充 + blob", f"{len(disk_res)} == {len(res0)} + {pad} + {bi['blob_len']}",
        len(disk_res) == len(res0) + pad + bi["blob_len"])
    say("③ 追加区 16 字节对齐", f"new_off={new_off} % 16 = {new_off % 16}", new_off % 16 == 0)
    say("④ 原文件区间逐字节未变", f"前 {len(res0)} 字节与原件完全一致", disk_res[:len(res0)] == res0)
    ap = parse_fsb5(disk_res[new_off:new_off + bi["blob_len"]])
    say("⑤ 追加区回读为 FSB5(PCM16)", f"mode={ap['mode']} dataSize={ap['dataSize']} 总长={bi['blob_len']}",
        ap["mode"] == 2 and HDR + ap["shSize"] + ap["nameTableSize"] + ap["dataSize"] == bi["blob_len"])
    say("⑥ 原槽位数据仍完好（A+ 的关键优势）", f"原 blob @{c['offset']} 前 4 字节 = {res0[c['offset']:c['offset']+4]!r}",
        disk_res[c["offset"]:c["offset"] + 4] == b"FSB5")

    env2 = UnityPy.load(os.path.join(a.dest, as_name))
    changed, ok_other = 0, True
    for o in env2.objects:
        if o.type != ClassIDType.AudioClip:
            continue
        tt = o.read_typetree()
        if tt.get("m_Name") == c["name"]:
            changed += 1
            r = tt["m_Resource"]
            say("⑦ 重指向生效", f"m_Offset {c['offset']} -> {r['m_Offset']}, m_Size {c['size']} -> {r['m_Size']}, "
                                f"m_Length {c['length']:.4f} -> {tt['m_Length']:.4f}",
                r["m_Offset"] == new_off and r["m_Size"] == bi["blob_len"] and abs(tt["m_Length"] - bi["seconds"]) < 1e-4)
        else:
            if tt["m_Resource"]["m_Offset"] != next(x["offset"] for x in inv if x["name"] == tt["m_Name"]):
                ok_other = False
    say("⑧ 其余 clip 的 offset 全部未变", f"核对通过（共 {len([o for o in env2.objects if o.type==ClassIDType.AudioClip])-1} 个）", ok_other)
    say("⑨ .assets 长度不变 / 分片数不变", f"{len(diska)} == {len(raw0)}，{nparts} 片", len(diska) == len(raw0))

    rb = dict(clip=c["name"], file=c["source"], as_file=as_name,
              orig_offset=c["offset"], orig_size=c["size"], orig_m_Length=c["length"],
              new_offset=new_off, new_size=bi["blob_len"], new_m_Length=bi["seconds"],
              resource_len_before=len(res0), resource_len_after=len(disk_res),
              assets_len=len(raw0), len_off_file=len_off, res_off_file=pos + res_rel)
    json.dump(rb, open(os.path.join(a.dest, "ROLLBACK.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    with open(os.path.join(a.dest, "APLUS_REPORT.txt"), "w", encoding="utf-8") as f:
        for n, d, o in REPORT:
            f.write(f"[{'PASS' if o else 'FAIL'}] {n}: {d}\n")

    allok = all(r[2] for r in REPORT)
    print(f"\n产物: {a.dest}")
    print(f"回滚信息: ROLLBACK.json  （原 offset={c['offset']} size={c['size']} m_Length={c['length']}）")
    print("整体:", "全部自证通过" if allok else "**有自证未通过**")
    return 0 if allok else 2


if __name__ == "__main__":
    sys.exit(main())


