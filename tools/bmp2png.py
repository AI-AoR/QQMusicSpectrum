# 把自检输出的 32bpp BMP 转成 PNG（纯标准库，不依赖 Pillow）。
# 用法: python tools/bmp2png.py in.bmp out.png [scale]
import os
import struct
import sys
import zlib


def read_bmp(path):
    d = open(path, "rb").read()
    if d[:2] != b"BM":
        raise SystemExit("not a BMP: " + path)
    off = struct.unpack("<I", d[10:14])[0]
    hdr = struct.unpack("<IiiHH", d[14:30])
    width, height = hdr[1], hdr[2]
    bpp = hdr[4]
    if bpp not in (24, 32):
        raise SystemExit("only 24/32bpp supported, got %d" % bpp)
    bottom_up = height > 0
    height = abs(height)
    stride = ((width * bpp // 8) + 3) // 4 * 4   # BMP 每行按 4 字节对齐
    rows = []
    for y in range(height):
        src_y = (height - 1 - y) if bottom_up else y
        base = off + src_y * stride
        rows.append(d[base:base + stride])
    return width, height, rows, bpp


def to_rgba(width, height, rows, bpp, bg=(0x14, 0x14, 0x1C)):
    """BMP 是 BGRA / BGR；24bpp 视为完全不透明。"""
    step = bpp // 8
    out = bytearray()
    for y in range(height):
        row = rows[y]
        line = bytearray()
        for x in range(width):
            o = x * step
            b, g, r = row[o], row[o + 1], row[o + 2]
            a = row[o + 3] if step == 4 else 255
            if a == 255:
                line += bytes((r, g, b))
            elif a == 0:
                line += bytes(bg)
            else:
                line += bytes((
                    (r * a + bg[0] * (255 - a)) // 255,
                    (g * a + bg[1] * (255 - a)) // 255,
                    (b * a + bg[2] * (255 - a)) // 255,
                ))
        out += line
    return bytes(out)


def write_png(path, width, height, rgb):
    raw = bytearray()
    stride = width * 3
    for y in range(height):
        raw.append(0)
        raw += rgb[y * stride:(y + 1) * stride]

    def chunk(tag, data):
        return (struct.pack(">I", len(data)) + tag + data +
                struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    png = (b"\x89PNG\r\n\x1a\n" +
           chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)) +
           chunk(b"IDAT", zlib.compress(bytes(raw), 9)) +
           chunk(b"IEND", b""))
    open(path, "wb").write(png)
    return len(png)


def upscale(width, height, rgb, scale):
    if scale <= 1:
        return width, height, rgb
    stride = width * 3
    out = bytearray()
    for y in range(height):
        row = rgb[y * stride:(y + 1) * stride]
        wide = bytearray()
        for x in range(width):
            px = row[x * 3:x * 3 + 3]
            wide += px * scale
        for _ in range(scale):
            out += wide
    return width * scale, height * scale, bytes(out)


def main():
    if len(sys.argv) < 3:
        raise SystemExit("usage: bmp2png.py in.bmp out.png [scale]")
    src, dst = sys.argv[1], sys.argv[2]
    scale = int(sys.argv[3]) if len(sys.argv) > 3 else 1
    w, h, rows, bpp = read_bmp(src)
    rgb = to_rgba(w, h, rows, bpp)
    w, h, rgb = upscale(w, h, rgb, scale)
    size = write_png(dst, w, h, rgb)
    print("%s -> %s  %dx%d  %d bytes" % (os.path.basename(src), os.path.basename(dst), w, h, size))


if __name__ == "__main__":
    main()
