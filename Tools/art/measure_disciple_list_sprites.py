"""Measure the alpha edges of Assets/Resources/ui/disciple_list/*.png.

Purpose: derive 9-slice sprite borders and content insets from the ACTUAL
pixels of the shipped art (per the layout-pass task), not by eye.

Run:  python Tools/art/measure_disciple_list_sprites.py
"""
import os

from PIL import Image

FOLDER = os.path.join(os.path.dirname(__file__), "..", "..",
                      "UnityProject", "Assets", "Resources", "ui", "disciple_list")
ALPHA_THRESHOLD = 16  # anything below this reads as transparent at UI scale


def edges(im):
    """Return (left, top, right, bottom) = thickness of fully-transparent
    margins on each side, measured with ALPHA_THRESHOLD."""
    w, h = im.size
    px = im.load()

    def col_opaque(x):
        return any(px[x, y][3] >= ALPHA_THRESHOLD for y in range(h))

    def row_opaque(y):
        return any(px[x, y][3] >= ALPHA_THRESHOLD for x in range(w))

    left = next((x for x in range(w) if col_opaque(x)), w)
    right = next((x for x in range(w - 1, -1, -1) if col_opaque(x)), -1)
    top = next((y for y in range(h) if row_opaque(y)), h)
    bottom = next((y for y in range(h - 1, -1, -1) if row_opaque(y)), -1)
    return left, top, w - 1 - right, h - 1 - bottom


def paper_inner_box(im):
    """For scroll_paper: find the bright paper rectangle inside the darker
    rolled edges, by looking for columns/rows whose mean luminance jumps.

    Returns (left, top, right, bottom) margins in px from the sprite edge to
    the bright interior — the region that is safe to fill with content.
    """
    w, h = im.size
    px = im.load()

    def col_lum(x):
        vals = [lum(px[x, y]) for y in range(h) if px[x, y][3] >= ALPHA_THRESHOLD]
        return sum(vals) / len(vals) if vals else 0

    def row_lum(y):
        vals = [lum(px[x, y]) for x in range(w) if px[x, y][3] >= ALPHA_THRESHOLD]
        return sum(vals) / len(vals) if vals else 0

    mid_lum_col = col_lum(w // 2)
    mid_lum_row = row_lum(h // 2)

    def first_bright(means, threshold):
        return next((i for i, v in enumerate(means) if v >= threshold), len(means))

    cols = [col_lum(x) for x in range(w)]
    rows = [row_lum(y) for y in range(h)]
    # bright = within 8% of the middle-of-paper luminance
    t_col = mid_lum_col * 0.92
    t_row = mid_lum_row * 0.92
    left = first_bright(cols, t_col)
    right = w - 1 - first_bright(cols[::-1], t_col)
    top = first_bright(rows, t_row)
    bottom = h - 1 - first_bright(rows[::-1], t_row)
    return left, top, w - 1 - right, h - 1 - bottom


def lum(p):
    r, g, b, a = p
    return 0.299 * r + 0.587 * g + 0.114 * b


for fn in sorted(os.listdir(FOLDER)):
    if not fn.endswith(".png"):
        continue
    im = Image.open(os.path.join(FOLDER, fn)).convert("RGBA")
    w, h = im.size
    l, t, r, b = edges(im)
    line = f"{fn:24s} {w:4d}x{h:<4d} transparent margins L={l:<4d} T={t:<4d} R={r:<4d} B={b:<4d}"
    if fn == "scroll_paper.png":
        pi = paper_inner_box(im)
        line += f"  |  bright-paper inset L={pi[0]} T={pi[1]} R={pi[2]} B={pi[3]}"
    print(line)
