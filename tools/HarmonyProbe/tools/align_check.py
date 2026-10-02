import zipfile, struct, sys
for path in sys.argv[1:]:
    print("="*70)
    print(path)
    try:
        z = zipfile.ZipFile(path)
    except Exception as e:
        print("  open failed:", e); continue
    names = [n for n in z.namelist() if n.startswith("lib/") and n.endswith(".so")]
    for n in sorted(names):
        i = z.getinfo(n)
        f = open(path, "rb")
        f.seek(i.header_offset)
        sig, ver, flg, meth, t, d, crc, csz, usz, nlen, elen = struct.unpack("<IHHHHHIIIHH", f.read(30))
        f.seek(i.header_offset + 30 + nlen)
        extra = f.read(elen)
        f.close()
        off = i.header_offset + 30 + nlen + elen
        # 处理 zip64 / padding extra field
        print("  %-46s meth=%-2d off=%-10d %%4096=%-5d %%16384=%-5d" % (n, meth, off, off % 4096, off % 16384))
