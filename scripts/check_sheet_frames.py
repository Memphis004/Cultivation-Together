import struct, zlib, glob, os

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
    return w, h, ch, ctype, out

BASE = "UnityProject/Assets/Resources/Data/Arts/Avatar/Chibi/"
sheets = [
    "body_body_robe_white", "head_head_female_01", "hair_hair_twin_tail",
    "accessory_acc_hairpin_silver", "face_marking_face_marking_red_dot",
    "body_body_robe_grey", "head_head_male_01", "hair_hair_short", "accessory_acc_none",
]

for name in sheets:
    path = BASE + name + ".png"
    if not os.path.exists(path):
        print(name, ": MISSING"); continue
    w, h, ch, ctype, px = read_png(path)
    cols, rows = 6, 2
    cw, chh = w // cols, h // rows
    # unity: Idle band rect.y=96 (top half in PNG coords), Walk rect.y=0 (bottom half)
    band_names = ["Idle(top)", "Walk(bottom)"]
    print("%s  %dx%d ctype=%d ch=%d cell=%dx%d" % (name, w, h, ctype, ch, cw, chh))
    for band in range(rows):
        counts = []
        for col in range(cols):
            opaque = tot = 0
            for y in range(band*chh, (band+1)*chh, 3):
                for x in range(col*cw, (col+1)*cw, 3):
                    o = (y*w + x) * ch
                    if ch == 4:
                        a = px[o+3]
                        if a > 32: opaque += 1
                    else:
                        # RGB: treat non-black-ish as content (baker may write alphaless)
                        r, g, b = px[o], px[o+1], px[o+2]
                        if r+g+b > 30: opaque += 1
                    tot += 1
            counts.append(opaque * 100 // max(tot, 1))
        print("   %s: " % band_names[band] + " ".join("%3d%%" % c for c in counts))
