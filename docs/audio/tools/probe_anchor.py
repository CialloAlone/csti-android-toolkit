"""
验证「锚定」的可行性设计（路 B 第 3 步）。

lead 的前提是"挂到 type tree 内嵌的 ScriptableObject 字段" —— 但这个游戏里
**游戏自己的 ScriptableObject（含 CardData/SoundManager 等）在序列化文件里都是 classID 114 (MonoBehaviour)**，
它们的自定义字段**没有内嵌 type tree**（read_typetree 只能读出 m_GameObject/m_Enabled/m_Script/m_Name）。
真正内嵌 type tree 的是 Unity **内建类型**（AudioClip/PreloadData/Texture2D…）。

所以可行的锚定方式是：**在 MonoBehaviour 对象里用裸字节定位已有的"音效数组"，往数组里追加一项**。
本脚本验证这条路：找到引用某 clip 的 MonoBehaviour，检查其对象字节里是否存在
  [int32 count][count × (int32 fileID + int64 pathID)]  这种合法数组结构。
"""
import json, os, struct, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "pylibs"))
import UnityPy
from UnityPy.enums import ClassIDType

ASSETS = os.path.join(HERE, "assets")
OUT = os.path.join(HERE, "out")
refs = {r["name"]: r for r in json.load(open(os.path.join(OUT, "clip_refs.json"), encoding="utf-8"))}
inv = {c["name"]: c for c in json.load(open(os.path.join(OUT, "slot_table.json"), encoding="utf-8"))}

CAND = sys.argv[1:] or ["BeeLoop", "SadTune", "BabyCryLoop", "DynamiteFuse"]

env = UnityPy.load(os.path.join(ASSETS, "sharedassets0.assets"))
raw = open(os.path.join(ASSETS, "sharedassets0.assets"), "rb").read()
by_path = {o.path_id: o for o in env.objects}

print("检查候选锚点：对象里是否存在可直接追加的「音效数组」结构\n")
ok_any = False
for clip in CAND:
    if clip not in refs:
        print(f"[{clip}] 无引用记录，跳过"); continue
    pid = inv[clip]["path_id"]
    print(f"=== {clip} (pathID={pid}) 被 {refs[clip]['ref_count']} 个对象引用 ===")
    for r in refs[clip]["referrers"][:4]:
        if r["file"] != "sharedassets0.assets" or r["type"] != "MonoBehaviour":
            continue
        o = by_path.get(r["path_id"])
        if o is None:
            continue
        d = o.get_raw_data()
        pat = struct.pack("<i", 0) + struct.pack("<q", pid)
        found = []
        p = 0
        while True:
            i = d.find(pat, p)
            if i < 0: break
            p = i + 1
            # 尝试把 i 当成数组第 k 项，回溯找 count
            for back in range(1, 9):
                cpos = i - back * 12
                if cpos - 4 < 0: break
                cnt = struct.unpack_from("<i", d, cpos - 4)[0]
                if not (1 <= cnt <= 64): continue
                # 校验 cpos 起连续 cnt 项都是合法 PPtr（fileID 小、pathID 在对象表内）
                good = True
                for k in range(cnt):
                    fid, ppid = struct.unpack_from("<iq", d, cpos + k * 12)
                    if fid < 0 or fid > 8 or ppid not in by_path:
                        good = False; break
                # 严格条件：i 处的 PPtr 必须是这个数组的**元素之一**，且就是我们的 pid
                if not (back < cnt):
                    continue
                if struct.unpack_from("<q", d, cpos + back * 12 + 4)[0] != pid:
                    continue
                if good:
                    found.append((cpos, cnt, back, i))
                    break
        print(f"  参照对象 pid={r['path_id']} name={r.get('name')!r} 对象大小={len(d)}")
        if found:
            for cpos, cnt, back, i in found[:2]:
                items = [struct.unpack_from("<iq", d, cpos + k * 12) for k in range(cnt)]
                names = [next((n for n, c in inv.items() if c["path_id"] == pp and c["resource_file"] == "sharedassets0.resource"), f"pid{pp}") for _, pp in items]
                print(f"    ★ 找到合法数组: 位于对象内 +0x{cpos:X}，count={cnt}，元素={names}")
                print(f"      → 锚定方案：把 count 改成 {cnt+1}，并在 +0x{cpos+cnt*12:X} 插入 12 字节 PPtr{{0, 新clip pathID}}")
                print(f"      → 该对象会增长 12 字节 → 需搬到数据区末尾并更新对象表条目（与新增对象同一套重写机制）")
                ok_any = True
        else:
            print("    （未找到可识别的数组结构 —— 该引用可能是单值 PPtr 而非数组）")
    print()
print("结论:", "锚定方案在 PC 侧**结构可验证** —— 可用裸字节定位数组并追加" if ok_any else "★ 未找到可验证的数组结构，需换候选或改设计")
