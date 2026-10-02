"""
把每个「clip 引用点」的**容器形态**分类：单值 PPtr vs 数组元素。
方法（lead 建议，比猜形状靠谱）：用**精确值**定位所有引用点，再向前回溯容器。

参考点 = 目标 clip 的精确 pathID（int64 LE），前置 4 字节是其 m_FileID。
"""
import collections, json, os, re, struct, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))
import UnityPy
from UnityPy.enums import ClassIDType

ASSETS = os.path.join(HERE, "assets")
OUT = os.path.join(HERE, "out")
slot = json.load(open(os.path.join(OUT, "slot_table.json"), encoding="utf-8"))

FILES = ["sharedassets0.assets", "sharedassets1.assets", "sharedassets2.assets", "sharedassets3.assets"]
envs, raws, externals = {}, {}, {}
clip_of = {}      # (file, pid) -> name
for fn in FILES:
    p = os.path.join(ASSETS, fn)
    if not os.path.exists(p):
        continue
    envs[fn] = UnityPy.load(p)
    raws[fn] = open(p, "rb").read()
    try:
        externals[fn] = [os.path.basename(getattr(e, "path", str(e))) for e in envs[fn].file.externals]
    except Exception:
        externals[fn] = []
    for o in envs[fn].objects:
        if o.type == ClassIDType.AudioClip:
            try:
                clip_of[(fn, o.path_id)] = o.read_typetree().get("m_Name")
            except Exception:
                pass
print(f"环境: {[ (f, len(externals[f])) for f in envs ]}")

# 每个文件的 clip pathID 集合
pids_by_file = collections.defaultdict(set)
for (fn, pid) in clip_of:
    pids_by_file[fn].add(pid)
pid_name = {pid: clip_of[(fn, pid)] for (fn, pid) in clip_of}


def plausible(fid, pid):
    return 0 <= fid <= 8 and 0 < pid < (1 << 40)


def classify(d, hit):
    """hit = pathID 的起始字节位置；返回 (kind, info)"""
    # 单值：紧跟其后的字节不像另一个 PPtr 的元素起点，且前面 count 不成立
    # 数组布局 = [int32 count][ (int32 fileID + int64 pathID) × count ]
    # hit 指向 pathID，所以第 j 个元素的 pathID 在 count+4+j*12  →  count 在 hit - j*12 - 8
    for j in range(0, 64):
        cpos = hit - j * 12 - 8
        if cpos < 0:
            break
        cnt = struct.unpack_from("<i", d, cpos)[0]
        if not (1 <= cnt <= 64) or j >= cnt:
            continue
        if cpos + 4 + cnt * 12 > len(d):
            continue
        ok = True
        for k in range(cnt):
            f2, p2 = struct.unpack_from("<iq", d, cpos + 4 + k * 12)
            if not plausible(f2, p2):
                ok = False; break
        if ok:
            elems = [struct.unpack_from("<iq", d, cpos + 4 + k * 12) for k in range(cnt)]
            return "array", dict(count=cnt, index=j, arr_off=cpos, elems=elems)
    return "single", {}


stats = collections.Counter()
per_clip = {}
array_containers = []
single_containers = []

for fn, env in envs.items():
    raw = raws[fn]
    pids = sorted(pids_by_file.get(fn, set()))
    if not pids:
        continue
    pat = re.compile(b"|".join(re.escape(struct.pack("<q", q)) for q in pids))
    for o in env.objects:
        if o.type != ClassIDType.MonoBehaviour:
            continue
        s, sz = o.byte_start, o.byte_size
        if sz <= 0 or s + sz > len(raw):
            continue
        d = raw[s:s + sz]
        for m in pat.finditer(d):
            hit = m.start()
            if hit < 4:
                continue
            fid = struct.unpack_from("<i", d, hit - 4)[0]
            pid = struct.unpack_from("<q", d, hit)[0]
            if fid != 0:                       # 只看同文件引用（跨文件另计）
                continue
            nm = pid_name.get(pid)
            if not nm:
                continue
            kind, info = classify(d, hit)
            stats[kind] += 1
            c = per_clip.setdefault(nm, collections.Counter())
            c[kind] += 1
            try:
                rn = o.peek_name()
            except Exception:
                rn = None
            rec = dict(clip=nm, referrer=rn, referrer_pid=o.path_id, obj_size=len(d), **info)
            (array_containers if kind == "array" else single_containers).append(rec)

print("=" * 96)
print(f"引用点分类统计（同文件 fileID=0）：数组元素 {stats['array']} 处；单值 PPtr {stats['single']} 处")
print(f"涉及 MonoBehaviour 对象：数组 {len({(r['referrer_pid']) for r in array_containers})} 个，"
      f"单值 {len({(r['referrer_pid']) for r in single_containers})} 个")
print("=" * 96)

print("\n### 数组容器 Top 20（count 最大的 = 最像'音效列表'的字段，= 锚定首选目标）")
byobj = collections.defaultdict(list)
for r in array_containers:
    byobj[(r["referrer"], r["referrer_pid"])].append(r)
top = sorted(byobj.items(), key=lambda kv: -max(x["count"] for x in kv[1]))[:20]
for (rn, rpid), lst in top:
    best = max(lst, key=lambda x: x["count"])
    print(f"  {str(rn):28} pid={rpid:<6} 数组count={best['count']:<3} 位于对象内+0x{best['arr_off']:X}")
    print(f"        该数组元素: {[pid_name.get(p, f'pid{p}') for _, p in best['elems']][:8]}")

print("\n### 单值 PPtr 容器 Top 12（= 顶掉式锚定的目标）")
cnt_obj = collections.Counter((r["referrer"], r["referrer_pid"]) for r in single_containers)
for (rn, rpid), n in cnt_obj.most_common(12):
    names = sorted({r["clip"] for r in single_containers if r["referrer_pid"] == rpid})
    print(f"  {str(rn):28} pid={rpid:<6} 单值音效字段 {n} 个: {names[:6]}")

print("\n### 每个 clip 的容器的分布（前 25）")
for nm, c in sorted(per_clip.items(), key=lambda kv: -(kv[1]['array'] + kv[1]['single']))[:25]:
    print(f"  {nm:28} 数组 {c['array']:3}  单值 {c['single']:3}")

allarr = stats['array']; allsingle = stats['single']
print("\n" + "=" * 96)
if allarr == 0:
    print("★ 结论：**全部都是单值 PPtr** → 锚定 = 顶掉某个已有音效字段（与牺牲槽位同性质）")
elif allsingle == 0:
    print("★ 结论：**全部都是数组元素** → 锚定 = 往数组追加一项（可无痛扩展）")
else:
    print(f"★ 结论：**两种都存在** —— 数组 {allarr} 处（可追加，首选）/ 单值 {allsingle} 处（只能顶掉）")
print("=" * 96)

json.dump(dict(stats=dict(stats), arrays=array_containers, singles=single_containers),
          open(os.path.join(OUT, "ref_containers.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print("落盘: out\\ref_containers.json")
