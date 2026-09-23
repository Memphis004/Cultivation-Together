import struct, zlib
import sys
from collections import Counter

def read_png(path):
    data = open(path, "rb").read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", "not a png"
    pos, idat, w, h, bitd, ctype = 8, b"", 0, 0, 0, 0
    while pos < len(data):
        ln, typ = struct.unpack(">I4s", data[pos:pos+8]); pos += 8
        chunk = data[pos:pos+ln]; pos += ln + 4
        if typ == b"IHDR":
            w, h, bitd, ctype = struct.unpack(">IIBB", chunk[:10])
        elif typ == b"IDAT":
            idat += chunk
        elif typ == b"IEND":
            break
    raw = zlib.decompress(idat)
    ch = {0:1, 2:3, 3:1, 4:2, 6:4}[ctype]
    stride = w * ch
    out = bytearray(w * h * ch)
    prev = bytearray(stride)
    pos = 0
    for y in range(h):
        f = raw[pos]; pos += 1
        line = bytearray(raw[pos:pos+stride]); pos += stride
        if f == 1:
            for i in range(ch, stride): line[i] = (line[i] + line[i-ch]) & 0xFF
        elif f == 2:
            for i in range(stride): line[i] = (line[i] + prev[i]) & 0xFF
        elif f == 3:
            for i in range(stride):
                a = line[i-ch] if i >= ch else 0
                line[i] = (line[i] + ((a + prev[i]) >> 1)) & 0xFF
        elif f == 4:
            for i in range(stride):
                a = line[i-ch] if i >= ch else 0
                b = prev[i]
                c = prev[i-ch] if i >= ch else 0
                p = a + b - c
                pa, pb, pc = abs(p-a), abs(p-b), abs(p-c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 0xFF
        out[y*stride:(y+1)*stride] = line
        prev = line
    return w, h, ch, out

SHOT = sys.argv[1] if len(sys.argv) > 1 else "UnityProject/Library/demo_shot.png"
w, h, ch, px = read_png(SHOT)

def pix(x, y):
    o = (y * w + x) * ch
    return px[o], px[o+1], px[o+2]

corners = [pix(0,0), pix(w-1,0), pix(0,h-1), pix(w-1,h-1)]
bg = tuple(sum(c[i] for c in corners)//4 for i in range(3))

def diff(c):
    return abs(c[0]-bg[0]) + abs(c[1]-bg[1]) + abs(c[2]-bg[2])

# ASCII map: 52 cols x 46 rows
COLS, ROWS = 52, 46
cw, rh = w / COLS, h / ROWS
chars = " .:-=+*#%@"
print("ASCII fg-density map (%dx%d cells):" % (COLS, ROWS))
for ry in range(ROWS):
    line = ""
    for rx in range(COLS):
        cnt = tot = 0
        for y in range(int(ry*rh), int((ry+1)*rh), 2):
            for x in range(int(rx*cw), int((rx+1)*cw), 2):
                tot += 1
                if diff(pix(x, y)) > 45: cnt += 1
        d = cnt / max(tot, 1)
        line += chars[min(int(d * 10), 9)]
    print(line)

# Per-chibi window check from camera dump ground truth:
# camera: ortho size 4.5 at (0,1.20,-10), viewport full, aspect = w/h
aspect = w / h
half_w = 4.5 * aspect
def world_to_px(wx, wy):
    vx = 0.5 + (wx - 0.0) / (2 * half_w)
    vy = 0.5 + (wy - 1.20) / 9.0
    return int(vx * w), int((1 - vy) * h)

targets = {
    "d001 sprite top-left  (-1.20,1.70)": (-1.20, 1.70),
    "d002 sprite top-right ( 0.40,1.70)": (0.40, 1.70),
    "d000 spine bottom-left (-1.20,0.50)": (-1.20, 0.50),
    "d003 spine bottom-right(0.40,0.50)": (0.40, 0.50),
}
print("\nchibi windows (60x60px around expected centers):")
for name, (wx, wy) in targets.items():
    cx, cy = world_to_px(wx, wy)
    cnt = tot = 0
    colors = Counter()
    for y in range(max(0,cy-30), min(h,cy+30), 2):
        for x in range(max(0,cx-30), min(w,cx+30), 2):
            tot += 1
            c = pix(x, y)
            if diff(c) > 45:
                cnt += 1
                colors[((c[0]>>4)<<8)|((c[1]>>4)<<4)|(c[2]>>4)] += 1
    top = colors.most_common(3)
    topn = ", ".join("0x%03X x%d" % (k, v) for k, v in top)
    print("  %s -> px(%d,%d) fg=%d%% colors=%d top: %s" %
          (name, cx, cy, 100*cnt//max(tot,1), len(colors), topn))
