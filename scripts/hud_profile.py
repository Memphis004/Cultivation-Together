import struct, zlib

def read_png(path):
    data = open(path, "rb").read()
    pos, idat, w, h, ctype = 8, b"", 0, 0, 0
    while pos < len(data):
        ln, typ = struct.unpack(">I4s", data[pos:pos+8]); pos += 8
        chunk = data[pos:pos+ln]; pos += ln + 4
        if typ == b"IHDR":
            w, h, _, ctype = struct.unpack(">IIBB", chunk[:10])
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
                b = prev[i]; c = prev[i-ch] if i >= ch else 0
                p = a + b - c
                pa, pb, pc = abs(p-a), abs(p-b), abs(p-c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 0xFF
        out[y*stride:(y+1)*stride] = line
        prev = line
    return w, h, ch, out

w, h, ch, px = read_png("UnityProject/Library/demo_shot.png")

def pix(x, y):
    o = (y*w + x) * ch
    return px[o], px[o+1], px[o+2]

# HUD region per VisualDemoHud.cs: Rect(10,10,460,420)
# Row profile: count columns with fg in x=[10,470]
bands = []
cur = None
for y in range(0, 445):
    cnt = 0
    for x in range(10, 470, 2):
        c = pix(x, y)
        if abs(c[0]-31) + abs(c[1]-36) + abs(c[2]-46) > 45:
            cnt += 1
    on = cnt > 8
    if on and cur is None:
        cur = [y, y]
    elif on:
        cur[1] = y
    elif cur is not None:
        bands.append(tuple(cur)); cur = None
if cur: bands.append(tuple(cur))

print("HUD bands (y-ranges with content), expected: 2 labels + 7 buttons + borders:")
merged = []
for b in bands:
    if merged and b[0] - merged[-1][1] <= 3:
        merged[-1] = (merged[-1][0], b[1])
    else:
        merged.append(b)
for b in merged:
    print("  y=%d..%d (h=%d)" % (b[0], b[1], b[1]-b[0]+1))
print("total bands:", len(merged))

# button-like bands = height >= 20 (IMGUI button ~28-30px, labels ~16px)
btn = [b for b in merged if b[1]-b[0]+1 >= 20]
lbl = [b for b in merged if 8 <= b[1]-b[0]+1 < 20]
print("button-height bands:", len(btn), " label-height bands:", len(lbl))
