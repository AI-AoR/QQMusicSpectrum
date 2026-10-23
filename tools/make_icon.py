# 生成频谱图标 src/app.ico —— 纯标准库实现（不依赖 Pillow），4x 超采样抗锯齿。
# 小尺寸用 32bpp DIB 条目（兼容性最好），大尺寸用 PNG 条目（体积小）。
import os
import struct
import zlib

TOP = (0x00, 0xE5, 0xFF)
MID = (0x7C, 0x4D, 0xFF)
BOT = (0xFF, 0x3D, 0x9A)


def grad(t):
    if t < 0.5:
        return lerp(TOP, MID, t * 2.0)
    return lerp(MID, BOT, (t - 0.5) * 2.0)


def lerp(a, b, t):
    return (a[0] + (b[0] - a[0]) * t,
            a[1] + (b[1] - a[1]) * t,
            a[2] + (b[2] - a[2]) * t)


def pattern_for(size):
    if size <= 20:
        return 3, [0.55, 1.00, 0.70]
    if size <= 32:
        return 4, [0.45, 0.80, 1.00, 0.60]
    if size <= 64:
        return 5, [0.40, 0.68, 1.00, 0.74, 0.48]
    return 7, [0.34, 0.56, 0.80, 1.00, 0.78, 0.55, 0.36]


def render(size, ss=4):
    """返回 size*size 的 RGBA bytes（直通 alpha，非预乘）。"""
    W = size * ss
    n, pattern = pattern_for(size)

    bw = max(ss, int(round(W * 0.105)))
    gap = max(ss, int(round(W * 0.052)))
    total = n * bw + (n - 1) * gap
    x0 = (W - total) // 2
    usable = W * 0.80
    top_margin = (W - usable) / 2.0
    bottom = W - top_margin

    acc = {}

    for i in range(n):
        h = max(float(bw), usable * pattern[i])
        left = x0 + i * (bw + gap)
        right = left + bw
        top = bottom - h
        r = bw / 2.0
        cx = (left + right) / 2.0

        y_from = max(0, int(top))
        y_to = min(W, int(bottom) + 1)
        for py in range(y_from, y_to):
            pyc = py + 0.5
            if pyc < top or pyc > bottom:
                continue
            dy = (top + r) - pyc
            inset = 0.0
            if dy > 0.0:
                inset = r if dy >= r else r - (r * r - dy * dy) ** 0.5
            xa = max(0, int(left + inset))
            xb = min(W - 1, int(right - inset))
            if xb < xa:
                continue

            t = (pyc - top_margin) / usable
            t = 0.0 if t < 0.0 else (1.0 if t > 1.0 else t)
            cr, cg, cb = grad(t)

            by = py // ss
            base = by * size
            for px in range(xa, xb + 1):
                idx = base + (px // ss)
                e = acc.get(idx)
                if e is None:
                    acc[idx] = [1.0, cr, cg, cb]
                else:
                    e[0] += 1.0
                    e[1] += cr
                    e[2] += cg
                    e[3] += cb

    out = bytearray(size * size * 4)
    denom = float(ss * ss)
    for idx, e in acc.items():
        a = e[0] / denom
        if a <= 0.0:
            continue
        r_ = e[1] / e[0]
        g_ = e[2] / e[0]
        b_ = e[3] / e[0]
        o = idx * 4
        out[o] = int(round(r_)) if r_ < 255 else 255
        out[o + 1] = int(round(g_)) if g_ < 255 else 255
        out[o + 2] = int(round(b_)) if b_ < 255 else 255
        out[o + 3] = int(round(a * 255.0))
    return bytes(out)


def png_bytes(w, h, rgba):
    raw = bytearray()
    stride = w * 4
    for y in range(h):
        raw.append(0)
        raw += rgba[y * stride:(y + 1) * stride]

    def chunk(tag, data):
        return (struct.pack(">I", len(data)) + tag + data +
                struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    return (b"\x89PNG\r\n\x1a\n" +
            chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) +
            chunk(b"IDAT", zlib.compress(bytes(raw), 9)) +
            chunk(b"IEND", b""))


def dib_bytes(w, h, rgba):
    """32bpp BMP（自下而上）+ 全零 AND 掩码。"""
    hdr = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, w * h * 4, 0, 0, 0, 0)
    xor = bytearray()
    for y in range(h - 1, -1, -1):
        row = rgba[y * w * 4:(y + 1) * w * 4]
        for x in range(w):
            o = x * 4
            xor += bytes((row[o + 2], row[o + 1], row[o], row[o + 3]))
    and_stride = ((w + 31) // 32) * 4
    return hdr + bytes(xor) + bytes(and_stride * h)


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out = os.path.join(root, "src", "app.ico")
    # 只保留 Windows 实际会用到的档位：16/32/48 是经典图标尺寸，
    # 20/24 对应 125%/150% DPI，256 是资源管理器的「超大图标」。
    # 40/64/128 三档实测从不被选中，去掉可省 26KB（约 exe 的 24%）。
    sizes = [16, 20, 24, 32, 48, 256]

    blobs = []
    for s in sizes:
        rgba = render(s)
        data = png_bytes(s, s, rgba) if s >= 128 else dib_bytes(s, s, rgba)
        blobs.append((s, data))
        print("  %3dpx  %6d bytes" % (s, len(data)))

    header = struct.pack("<HHH", 0, 1, len(blobs))
    offset = 6 + 16 * len(blobs)
    entries = b""
    body = b""
    for s, data in blobs:
        dim = 0 if s >= 256 else s
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        body += data
        offset += len(data)

    with open(out, "wb") as f:
        f.write(header + entries + body)
    print("wrote %s  %d bytes  %d sizes" % (out, os.path.getsize(out), len(blobs)))


if __name__ == "__main__":
    main()
