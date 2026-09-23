"""Verify baked portrait placeholder PNGs: alpha coverage + head-center alignment.

INVARIANT (avatar-appearance §3.3 / PortraitPlaceholderBaker docstring):
every part is painted on the SAME 256x384 canvas with the SAME head-center
(x=128, y=262, y-up). Swapping eyes/nose/mouth must never shift anything.

This script decodes the PNGs (stdlib zlib, no PIL) and reports, per file:
  opaque px count, alpha-weighted centroid (converted to y-up coords),
  and bounding box. Face geometry must sit in the head region
  (x 90..166, y 206..310) for the facial-feature parts.
"""
import struct, sys, zlib, os

DIR = os.path.join(os.path.dirname(__file__), "..", "UnityProject", "Assets", "Resources", "Avatar")
W, H = 256, 384

def read_png(path):
    data = open(path, "rb").read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", "not a png: " + path
    pos, idat = 8, b""
    while pos < len(data):
        ln, typ = struct.unpack(">I4s", data[pos:pos+8]); pos += 8
        chunk = data[pos:pos+ln]; pos += ln + 4
        if typ == b"IHDR":
            w, h, bitd, ctype = struct.unpack(">IIBB", chunk[:10])
            assert (w, h, bitd, ctype) == (W, H, 8, 6), (path, w, h, bitd, ctype)
        elif typ == b"IDAT":
            idat += chunk
    raw = zlib.decompress(idat)
    stride, px = W * 4, [0] * (W * H)
    prev = bytearray(stride)
    for y in range(H):
        off, f = y * (stride + 1), raw[y * (stride + 1)]
        line = bytearray(raw[off + 1:off + 1 + stride])
        for x in range(stride):
            a = line[x - 4] if x >= 4 else 0
            b = prev[x]
            c = prev[x - 4] if x >= 4 else 0
            if f == 1: line[x] = (line[x] + a) & 255
            elif f == 2: line[x] = (line[x] + b) & 255
            elif f == 3: line[x] = (line[x] + (a + b) // 2) & 255
            elif f == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[x] = (line[x] + pr) & 255
        prev = line
        for x in range(W):
            px[y * W + x] = line[x * 4 + 3]
    return px

# (file, expect-opaque-min, strict-face-box?) — strict box only for face FEATURES;
# hair/accessory legitimately extend above the face box (bun, crown).
CHECKS = [
    ("eyes_round",           200, True),
    ("eyes_sharp",           150, True),
    ("brows_thick",          100, True),
    ("nose_tall",             30, True),
    ("mouth_smile",           40, True),
    ("eyeshadow_subtle",      80, True),
    ("face_marking_red_dot",  30, True),
    ("acc_jade_crown",       200, False),
    ("hair_topknot",         800, False),
    ("body_robe_azure",     4000, False),
    ("head_male_01",        4000, False),
    ("base_silhouette",     4000, False),
]

HEAD = (90, 166, 206, 310)  # x0,x1,y0,y1 (y-up)
fails = 0
for name, minpx, in_head in CHECKS:
    px = read_png(os.path.join(DIR, name + ".png"))
    n, sx, sy, x0, x1, y0, y1 = 0, 0, 0, W, -1, H, -1
    for y in range(H):
        yy = H - 1 - y  # y-up
        for x in range(W):
            if px[y * W + x] > 32:
                n += 1; sx += x; sy += yy
                if x < x0: x0 = x
                if x > x1: x1 = x
                if yy < y0: y0 = yy
                if yy > y1: y1 = yy
    cx, cy = (sx / n if n else -1), (sy / n if n else -1)
    ok = n >= minpx
    if in_head:
        ok = ok and HEAD[0] <= x0 and x1 <= HEAD[1] and HEAD[2] <= y0 and y1 <= HEAD[3]
    print("%-24s px=%-6d bbox=(%d..%d, %d..%d) centroid=(%.0f,%.0f) %s" %
          (name, n, x0, x1, y0, y1, cx, cy, "OK" if ok else "FAIL"))
    if not ok: fails += 1

print("FAILURES:", fails)
sys.exit(1 if fails else 0)
