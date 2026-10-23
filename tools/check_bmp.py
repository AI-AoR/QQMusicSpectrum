"""Inspect selftest BMP output: bar structure, baseline, reflection falloff, gradient.
Pure stdlib. ASCII output to stay readable on a GBK console."""
import struct
import sys


def load_bmp(path):
    with open(path, "rb") as f:
        data = f.read()
    assert data[:2] == b"BM", "not a bmp"
    off = struct.unpack_from("<I", data, 10)[0]
    w = struct.unpack_from("<i", data, 18)[0]
    h = struct.unpack_from("<i", data, 22)[0]
    bpp = struct.unpack_from("<H", data, 28)[0]
    assert bpp == 24, bpp
    stride = (w * 3 + 3) & ~3
    px = []
    for y in range(h):
        row = off + (h - 1 - y) * stride
        line = []
        for x in range(w):
            o = row + x * 3
            line.append((data[o + 2], data[o + 1], data[o]))
        px.append(line)
    return w, h, px


def lum(c):
    return (c[0] * 299 + c[1] * 587 + c[2] * 114) // 1000


def analyze(path):
    w, h, px = load_bmp(path)
    corners = [px[0][0], px[0][w - 1], px[h - 1][0], px[h - 1][w - 1]]
    bg = tuple(sorted(c[i] for c in corners)[1] for i in range(3))

    def is_bg(c):
        return abs(c[0] - bg[0]) + abs(c[1] - bg[1]) + abs(c[2] - bg[2]) <= 6

    col_top = []
    for x in range(w):
        top = None
        for y in range(h):
            if not is_bg(px[y][x]):
                top = y
                break
        col_top.append(top)

    lit = [x for x in range(w) if col_top[x] is not None]
    print("  size %dx%d  bg rgb%s  lit columns %d/%d" % (w, h, bg, len(lit), w))
    if not lit:
        print("  !! blank image")
        return

    groups = []
    start = prev = lit[0]
    for x in lit[1:]:
        if x != prev + 1:
            groups.append((start, prev))
            start = x
        prev = x
    groups.append((start, prev))

    widths = [b - a + 1 for a, b in groups]
    gaps = [groups[i + 1][0] - groups[i][1] - 1 for i in range(len(groups) - 1)]
    print("  bars=%d  width=%d..%d  gap=%d..%d"
          % (len(groups), min(widths), max(widths), min(gaps) if gaps else 0, max(gaps) if gaps else 0))

    g = groups[len(groups) // 2]
    xm = (g[0] + g[1]) // 2
    rows = [y for y in range(h) if not is_bg(px[y][xm])]
    print("  middle bar x=%d  rows %d..%d" % (xm, rows[0], rows[-1]))

    # baseline = biggest single-row luminance drop below the vertical midpoint of the bar
    prof = [(y, lum(px[y][xm])) for y in rows]
    mid = (rows[0] + rows[-1]) // 2
    best = (0, None)
    for i in range(1, len(prof)):
        if prof[i][0] < mid:
            continue
        d = prof[i - 1][1] - prof[i][1]
        if d > best[0]:
            best = (d, prof[i][0])
    base = best[1]
    print("  baseline y=%s (drop %d)  bar span %d..%d  reflection rows=%s"
          % (base, best[0], rows[0], rows[-1], (rows[-1] - base + 1) if base else "?"))

    if base:
        main_l = [l for y, l in prof if y < base]
        refl_l = [l for y, l in prof if y >= base]
        if main_l and refl_l:
            a = sum(main_l) / len(main_l)
            b = sum(refl_l) / len(refl_l)
            print("  main avg lum=%.0f  reflection avg lum=%.0f (%.0f%%)" % (a, b, 100.0 * b / a))
            head = sum(refl_l[:2]) / len(refl_l[:2])
            tail = sum(refl_l[-2:]) / len(refl_l[-2:])
            print("  reflection falloff %.0f -> %.0f  %s"
                  % (head, tail, "OK" if tail < head else "!! not fading"))
        print("  bar bottom rgb%s -> bar top rgb%s" % (px[base - 2][xm], px[rows[0] + 2][xm]))

    tops = [min(x for x in col_top[a:b + 1] if x is not None) for a, b in groups]
    print("  bar tops y=%d..%d  (height variance %d px)" % (min(tops), max(tops), max(tops) - min(tops)))

    # every bar must sit on the same baseline: bottom-most lit row per bar
    bottoms = []
    for a, b in groups:
        x2 = (a + b) // 2
        r2 = [y for y in range(h) if not is_bg(px[y][x2])]
        bottoms.append(r2[-1])
    print("  bar bottoms y=%d..%d  %s"
          % (min(bottoms), max(bottoms), "aligned" if max(bottoms) - min(bottoms) <= 1 else "!! misaligned"))


if __name__ == "__main__":
    for p in sys.argv[1:]:
        print(p)
        analyze(p)
