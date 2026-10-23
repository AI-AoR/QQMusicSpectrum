"""Compare two desktop screenshots (BMP 24/32bpp) and report the changed region.
Used to prove the overlay really gets composited, and that bars move with audio."""
import struct
import sys


def load_bmp(path):
    with open(path, "rb") as f:
        data = f.read()
    assert data[:2] == b"BM", "not a bmp: " + path
    off = struct.unpack_from("<I", data, 10)[0]
    w = struct.unpack_from("<i", data, 18)[0]
    h = struct.unpack_from("<i", data, 22)[0]
    bpp = struct.unpack_from("<H", data, 28)[0]
    assert bpp in (24, 32), bpp
    step = bpp // 8
    stride = (w * step + 3) & ~3
    px = []
    for y in range(h):
        row = off + (h - 1 - y) * stride
        line = []
        for x in range(w):
            o = row + x * step
            line.append((data[o + 2], data[o + 1], data[o]))
        px.append(line)
    return w, h, px


def diff(a_path, b_path, thresh=12):
    wa, ha, a = load_bmp(a_path)
    wb, hb, b = load_bmp(b_path)
    assert (wa, ha) == (wb, hb), "size mismatch"

    minx, miny, maxx, maxy = wa, ha, -1, -1
    count = 0
    sumr = sumg = sumb = 0
    for y in range(ha):
        ra, rb = a[y], b[y]
        for x in range(wa):
            ca, cb = ra[x], rb[x]
            d = abs(ca[0] - cb[0]) + abs(ca[1] - cb[1]) + abs(ca[2] - cb[2])
            if d > thresh:
                count += 1
                if x < minx: minx = x
                if x > maxx: maxx = x
                if y < miny: miny = y
                if y > maxy: maxy = y
                sumr += cb[0]; sumg += cb[1]; sumb += cb[2]

    print("  %s vs %s" % (a_path.split("\\")[-1], b_path.split("\\")[-1]))
    if count == 0:
        print("  changed pixels: 0  (identical)")
        return None
    print("  changed pixels: %d (%.2f%% of %dx%d)"
          % (count, 100.0 * count / (wa * ha), wa, ha))
    print("  bounding box : x %d..%d (w=%d)   y %d..%d (h=%d)"
          % (minx, maxx, maxx - minx + 1, miny, maxy, maxy - miny + 1))
    print("  mean colour  : rgb(%d,%d,%d)"
          % (sumr // count, sumg // count, sumb // count))
    return (minx, miny, maxx, maxy, count)


if __name__ == "__main__":
    if len(sys.argv) < 3:
        print("usage: diff_shot.py before.bmp after.bmp [after2.bmp ...]")
        sys.exit(2)
    base = sys.argv[1]
    for other in sys.argv[2:]:
        diff(base, other)
