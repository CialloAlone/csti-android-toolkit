"""生成 PNG 解码器测试矩阵：多张不同位深/颜色类型的 PNG + 用 Pillow 解码出的 RGBA 参考。

Pillow 是独立实现，用它当参照物可以证明我们手写的解码器（反滤波 / zlib / 调色板 / 位深扩展）是对的。

注意：本 session 里 python 进程被沙箱限成只读，所以本脚本**不写任何文件**，
而是把结果以 JSON（含 base64）打到 stdout，由 PowerShell 落盘。
用法: python gen_testdata.py  > 由 materialize.ps1 解析
"""
import base64
import io
import json
import sys

import numpy as np
from PIL import Image

EMBEDDED = ("iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAYAAACp8Z5+AAAAFElEQVR42mNk+M9Qz0AEYBxVSF+F"
            "ABJADveWkH6oAAAAAElFTkSuQmCC")

cases = []


def ihdr(data: bytes):
    w = int.from_bytes(data[16:20], "big")
    h = int.from_bytes(data[20:24], "big")
    return w, h, data[24], data[25], data[28]


def add(name: str, png_bytes: bytes, raw_rgba: bytes, w: int, h: int):
    _, _, bit, color, inter = ihdr(png_bytes)
    cases.append({
        "name": name, "w": w, "h": h, "bit": bit, "color": color, "interlace": inter,
        "png_b64": base64.b64encode(png_bytes).decode("ascii"),
        "raw_b64": base64.b64encode(raw_rgba).decode("ascii"),
    })


def png_of(img, **kw) -> bytes:
    b = io.BytesIO()
    img.save(b, "PNG", **kw)
    return b.getvalue()


def add_img(name, img, **kw):
    data = png_of(img, **kw)
    w, h, _, _, _ = ihdr(data)
    ref = Image.open(io.BytesIO(data)).convert("RGBA")
    if ref.size != (w, h):
        ref = ref.resize((w, h))
    add(name, data, ref.tobytes(), w, h)


rng = np.random.default_rng(20261002)

# 1) 与 mod 内嵌完全相同的 4x4 RGBA PNG（保证真机/PC 同源）
d = base64.b64decode(EMBEDDED)
w, h, _, _, _ = ihdr(d)
add("embedded4x4", d, Image.open(io.BytesIO(d)).convert("RGBA").tobytes(), w, h)

# 2) 8 位 RGBA 大图：覆盖多种滤波类型
add_img("rgba8_37x53", Image.fromarray(rng.integers(0, 256, size=(37, 53, 4), dtype=np.uint8), "RGBA"))

# 3) 8 位 RGB
add_img("rgb8_16x16", Image.fromarray(rng.integers(0, 256, size=(16, 16, 3), dtype=np.uint8), "RGB"))

# 4) 8 位灰度
add_img("gray8_9x11", Image.fromarray(rng.integers(0, 256, size=(9, 11), dtype=np.uint8), "L"))

# 5) 8 位灰度 + Alpha
add_img("graya8_8x8", Image.fromarray(rng.integers(0, 256, size=(8, 8, 2), dtype=np.uint8), "LA"))

# 6) 8 位调色板（quantize 会带 tRNS）
add_img("pal8_12x12", Image.fromarray(rng.integers(0, 256, size=(12, 12, 3), dtype=np.uint8), "RGB").quantize(colors=64))

# 7) 4 位调色板
pal4 = Image.new("P", (10, 10))
pal4.putpalette(sum([[i * 16, 255 - i * 16, (i * 7) % 256] for i in range(16)], []))
pal4.putdata([(x + y) % 16 for y in range(10) for x in range(10)])
add_img("pal4_10x10", pal4, bits=4)

# 8) 1 位灰度
add_img("gray1_7x7", Image.fromarray((rng.integers(0, 2, size=(7, 7)) * 255).astype(np.uint8), "L").convert("1"))

# 9) 2 位灰度（值必须是 0/85/170/255）
add_img("gray2_6x9", Image.fromarray((rng.integers(0, 4, size=(6, 9)) * 85).astype(np.uint8), "L"), bits=2)

# 10) 4 位灰度
add_img("gray4_6x9", Image.fromarray((rng.integers(0, 16, size=(6, 9)) * 17).astype(np.uint8), "L"), bits=4)

# 11) 16 位灰度（我们的解码器取高字节）
a16 = rng.integers(0, 65536, size=(5, 7)).astype(">u2")
img16 = Image.fromarray(a16, "I;16")
data16 = png_of(img16)
w, h, _, _, _ = ihdr(data16)
arr = np.array(Image.open(io.BytesIO(data16)))
rgba = np.zeros((h, w, 4), dtype=np.uint8)
rgba[..., 0] = rgba[..., 1] = rgba[..., 2] = (arr >> 8).astype(np.uint8)
rgba[..., 3] = 255
add("gray16_5x7", data16, rgba.tobytes(), w, h)

# 12) 极限：1x1
add_img("rgba8_1x1", Image.new("RGBA", (1, 1), (7, 200, 30, 128)))

json.dump(cases, sys.stdout, ensure_ascii=True)
