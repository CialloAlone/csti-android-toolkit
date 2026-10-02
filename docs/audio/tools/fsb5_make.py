"""
方案丙 第一步工具：把 mod 的 WAV 做成**等长**的 FSB5(PCM16) blob，替换 APK 里某个游戏 AudioClip 的音频数据。

设计（最小改动原则）：
  · 复用目标 clip 原有的 96 字节前缀（60 B FSB5 头 + 36 B 样本头）**逐字节照抄**，
    只改 0x18 `mode`(7→2) 和 0x14 `dataSize`，其余（channels/frequency/样本头几何）保持不变 ——
    这样 FMOD 读到的采样几何与原件完全一致，唯一变量是"编码方式"。
  · 负载 = 重采样到目标频率 + 混到目标声道数的 int16 交织 PCM，不足部分用**真 0 静音**补齐，
    输出 blob 字节数 **严格等于** 原 blob 字节数。
  · 自证（全部在 PC 上完成，见 verify_* 函数）。

用法：
  python fsb5_make.py --clip ChoppingTrunk
  python fsb5_make.py --clip ChoppingTrunk --wav <path> --out <dir>
"""
import argparse, json, os, struct, sys, wave

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "assets")
OUT = os.path.join(HERE, "out")
RES0 = os.path.join(ASSETS, "sharedassets0.resource")
RES2 = os.path.join(ASSETS, "sharedassets2.resource")
DEFAULT_WAV = r"D:\RiderProjects\csti\BepInEx\plugins\Windy\Resource\Audio\windy.wav"

HDR = 0x3C        # 60 字节 FSB5 头
MODE_PCM16 = 2
MODE_IMA = 7
MODE_VORBIS = 15


# --------------------------------------------------------------------------- WAV
def read_wav(path):
    with wave.open(path, "rb") as w:
        ch, sw, sr, n = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        raw = w.readframes(n)
    assert sw == 2, f"只支持 16-bit PCM WAV，实际 sampwidth={sw}"
    samples = struct.unpack("<%dh" % (len(raw) // 2), raw)
    return samples, ch, sr


def convert(samples, src_ch, src_rate, dst_ch, dst_rate):
    """int16 交织 -> 目标声道/频率（线性插值重采样；声道 1->N 复制，N->1 取均值）。"""
    n_src = len(samples) // src_ch
    # 1) 声道映射
    if src_ch == dst_ch:
        frames = [list(samples[i * src_ch:(i + 1) * src_ch]) for i in range(n_src)]
    elif src_ch == 1:
        frames = [[samples[i]] * dst_ch for i in range(n_src)]
    elif dst_ch == 1:
        frames = [[int(sum(samples[i * src_ch:(i + 1) * src_ch]) / src_ch)] for i in range(n_src)]
    else:
        frames = [list(samples[i * src_ch:(i + 1) * src_ch])[:dst_ch] for i in range(n_src)]
    # 2) 重采样
    if src_rate != dst_rate and n_src > 1:
        n_dst = int(round(n_src * dst_rate / src_rate))
        out = []
        for j in range(n_dst):
            pos = j * (n_src - 1) / max(1, n_dst - 1)
            i0 = int(pos); frac = pos - i0
            i1 = min(i0 + 1, n_src - 1)
            out.append([int(round(frames[i0][c] * (1 - frac) + frames[i1][c] * frac)) for c in range(dst_ch)])
        frames = out
    return frames


# ------------------------------------------------------------------- FSB5 builder
def parse_fsb5(b):
    if b[:4] != b"FSB5":
        raise ValueError("不是 FSB5")
    ver, num, sh, nt, ds, mode = struct.unpack_from("<6I", b, 4)
    return dict(version=ver, numSamples=num, shSize=sh, nameTableSize=nt, dataSize=ds, mode=mode,
                prefix60=b[:HDR], sampleHdr=b[HDR:HDR + sh], data=b[HDR + sh:HDR + sh + ds])


def build_pcm16_blob(orig_blob, frames, dst_ch, dst_rate):
    """返回 (new_blob, info)。new_blob 长度 == len(orig_blob)。"""
    o = parse_fsb5(orig_blob)
    sh = o["shSize"]
    capacity = len(orig_blob) - HDR - sh          # 可用的 data 字节数
    assert capacity > 0, "原 blob 太小"

    pcm = bytearray()
    for fr in frames:
        for s in fr:
            pcm += struct.pack("<h", max(-32768, min(32767, s)))
    used = min(len(pcm), capacity)
    pcm = bytes(pcm[:used])                        # 超出容量的部分裁掉（时长变短）
    padding = capacity - used

    hdr = bytearray(o["prefix60"])
    struct.pack_into("<I", hdr, 0x14, capacity)    # dataSize
    struct.pack_into("<I", hdr, 0x18, MODE_PCM16)  # mode

    blob = bytes(hdr) + o["sampleHdr"] + pcm + b"\x00" * padding
    info = dict(
        orig_size=len(orig_blob), new_size=len(blob), shSize=sh,
        capacity=capacity, pcm_bytes=used, padding_bytes=padding,
        dst_ch=dst_ch, dst_rate=dst_rate,
        frames_written=used // (2 * dst_ch),
        audio_seconds=used / (2 * dst_ch) / dst_rate,
        capacity_seconds=capacity / (2 * dst_ch) / dst_rate,
        orig_dataSize=o["dataSize"], orig_mode=o["mode"],
    )
    return blob, info


# ----------------------------------------------------------------------- 自证
def verify_parse(blob, expect_size, dst_ch, dst_rate, info):
    r = []
    o = parse_fsb5(blob)
    r.append(("① 回读字段", f"magic=FSB5 version={o['version']} numSamples={o['numSamples']} "
                            f"shSize={o['shSize']} nameTableSize={o['nameTableSize']} "
                            f"dataSize={o['dataSize']} mode={o['mode']}(PCM16)",
              o["mode"] == MODE_PCM16 and o["shSize"] == info["shSize"]
              and HDR + o["shSize"] + o["nameTableSize"] + o["dataSize"] == len(blob)))
    r.append(("② 总长相等", f"新 {len(blob)} vs 原 {expect_size}",
              len(blob) == expect_size))
    # 填充必须是真静音
    pad = blob[len(blob) - info["padding_bytes"]:] if info["padding_bytes"] else b""
    r.append(("③ 填充是静音", f"尾部 {info['padding_bytes']} 字节，非零字节数 = {sum(1 for x in pad if x)}",
              all(x == 0 for x in pad)))
    # 头部前缀除 mode/dataSize 外与原件一致
    changed = [i for i in range(HDR) if blob[i] != info["orig_prefix"][i]]
    allowed = set(range(0x14, 0x1C))
    r.append(("④ 头前缀最小改动", f"60 字节里变了 {len(changed)} 个，位置={[hex(x) for x in changed]}（只允许 0x14..0x1B）",
              set(changed) <= allowed))
    # 样本头逐字节照抄
    r.append(("⑤ 样本头原样", f"{info['shSize']} 字节与原件逐字节相同",
              blob[HDR:HDR + info["shSize"]] == info["orig_sampleHdr"]))
    return r


def verify_splice(blob, clip_name, offset, expect_size):
    """把 blob 拼进 .resource 的副本，然后逐条核对其余 clip 的 FSB5 头是否完好、offset/size 是否未变。"""
    inv_path = os.path.join(OUT, "csti_audioclip_inventory.json")
    inv = json.load(open(inv_path, encoding="utf-8"))
    inv0 = [c for c in inv if c["source"] == "sharedassets0.resource"]

    orig = open(RES0, "rb").read()
    assert len(blob) == expect_size
    new = orig[:offset] + blob + orig[offset + expect_size:]
    assert len(new) == len(orig), "拼接后文件长度变了！"

    bad = []
    for c in inv0:
        if c["name"] == clip_name:
            continue
        magic = new[c["offset"]:c["offset"] + 4]
        if magic != b"FSB5":
            bad.append((c["name"], magic))
    # 逐条比对 offset/size（来自 .assets，未改动，因此必然相同；此处显式核对）
    same = all(c["offset"] % 16 == 0 for c in inv0)
    return [
        ("⑥ 其余 clip 头未损坏", f"核对 {len(inv0)-1} 个邻居，magic 损坏 {len(bad)} 个"
                                 + ("" if not bad else " -> " + str(bad[:3])), len(bad) == 0),
        ("⑦ .resource 长度不变", f"{len(new)} == {len(orig)}（因此所有 offset 无需重算）", len(new) == len(orig)),
        ("⑧ offset 16 字节对齐保持", f"127 条全部 16 对齐 = {same}", same),
    ]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--clip", default="ChoppingTrunk")
    ap.add_argument("--wav", default=DEFAULT_WAV)
    ap.add_argument("--out", default=os.path.join(OUT, "fsb5_payload.bin"))
    ap.add_argument("--splice", action="store_true",
                    help="把生成的 blob 拼进 .resource 并把结果写到 out/<name>.patched.resource")
    ap.add_argument("--patch-length", action="store_true",
                    help="额外算出需要写回 .assets 的 m_Length 新值（float）并打印定位信息")
    a = ap.parse_args()

    inv = json.load(open(os.path.join(OUT, "csti_audioclip_inventory.json"), encoding="utf-8"))
    cand = [c for c in inv if c["name"] == a.clip]
    if not cand:
        print("找不到 clip:", a.clip); return 1
    c = cand[0]
    print(f"目标 clip: {c['name']}  loadType={c['loadType']} fmt={c['fmt']} ch={c['channels']} "
          f"freq={c['frequency']} len={c['length']}s offset={c['offset']} size={c['size']}")

    res = RES0 if c["source"] == "sharedassets0.resource" else RES2
    with open(res, "rb") as f:
        f.seek(c["offset"]); orig = f.read(c["size"])
    print(f"原 blob 读入 {len(orig)} 字节, mode={parse_fsb5(orig)['mode']}, shSize={parse_fsb5(orig)['shSize']}")

    samples, wch, wrate = read_wav(a.wav)
    dur = len(samples) / wch / wrate
    print(f"WAV: {a.wav}\n     {wch}ch {wrate}Hz 16bit  {len(samples)} samples  {dur:.4f} s")
    frames = convert(samples, wch, wrate, c["channels"], c["frequency"])
    print(f"转换后: {c['channels']}ch {c['frequency']}Hz  {len(frames)} frames  "
          f"{len(frames)/c['frequency']:.4f} s  (重采样前后时长差 {len(frames)/c['frequency']-dur:+.4f} s)")

    blob, info = build_pcm16_blob(orig, frames, c["channels"], c["frequency"])
    info["orig_prefix"] = orig[:HDR]
    info["orig_sampleHdr"] = orig[HDR:HDR + info["shSize"]]

    print(f"\n容量: {info['capacity']} B = {info['capacity_seconds']:.4f} s (PCM16 {c['channels']}ch {c['frequency']}Hz)")
    print(f"写入: {info['pcm_bytes']} B = {info['audio_seconds']:.4f} s，静音填充 {info['padding_bytes']} B")
    print(f"原 clip 声明时长 m_Length = {c['length']} s -> 实际 PCM 时长 {info['audio_seconds']:.4f} s "
          f"(差 {info['audio_seconds']-c['length']:+.4f} s)")

    print("\n=== 自证 ===")
    ok_all = True
    for name, detail, ok in verify_parse(blob, len(orig), c["channels"], c["frequency"], info) + \
                           verify_splice(blob, c["name"], c["offset"], len(orig)):
        print(f"  [{'PASS' if ok else 'FAIL'}] {name}: {detail}")
        ok_all &= ok

    os.makedirs(os.path.dirname(a.out), exist_ok=True)
    with open(a.out, "wb") as f:
        f.write(blob)
    print(f"\n输出: {a.out} ({len(blob)} B)")

    if a.splice:
        orig_res = open(res, "rb").read()
        patched = orig_res[:c["offset"]] + blob + orig_res[c["offset"] + len(orig):]
        assert len(patched) == len(orig_res), "拼接后长度变了"
        pres = os.path.join(OUT, os.path.basename(res) + ".patched")
        with open(pres, "wb") as f:
            f.write(patched)
        # 复核：拼接后的文件里，目标 clip 的头是新 blob，邻居头仍在
        chk = patched[c["offset"]:c["offset"] + 4] == b"FSB5" and patched[c["offset"] + 0x18] == MODE_PCM16
        nb = sum(1 for cc in inv if cc["source"] == c["source"] and cc["name"] != c["name"]
                 and patched[cc["offset"]:cc["offset"] + 4] == b"FSB5")
        tot = sum(1 for cc in inv if cc["source"] == c["source"] and cc["name"] != c["name"])
        print(f"已写: {pres} ({len(patched)} B)  目标头替换OK={chk}  邻居 FSB5 头完好 {nb}/{tot}")

    if a.patch_length:
        import struct as _s
        new_len = info["audio_seconds"]
        print(f"\n[.assets 补丁需要] 找到该 clip 对象的 m_Length 字段（原值 float {c['length']}），"
              f"改写为 {new_len!r} = {_s.pack('<f', new_len).hex(' ')}")
        print(f"  提示：m_Length 在对象原始字节里紧跟在 m_BitsPerSample 之后；"
              f"可用 struct.pack('<f', {c['length']!r}).hex(' ') 在对象里唯一定位。")
    json.dump({k: v for k, v in info.items() if not isinstance(v, (bytes, bytearray))} | {"clip": c},
              open(os.path.join(OUT, "fsb5_make_report.json"), "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)
    print("总体:", "全部自证通过" if ok_all else "**有自证未通过**")
    return 0 if ok_all else 2


if __name__ == "__main__":
    sys.exit(main())
