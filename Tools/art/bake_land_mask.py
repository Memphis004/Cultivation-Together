"""Bake a per-cell "land" (placeable) mask out of the painted terrain backdrop.

Source:  Assets/Resources/tilemap/map/mountain1_wx/mountain1.png
         4096x3328 px @ PPU 100 -> 40.96 x 33.28 world units, centred on origin
         (the sprite slice pivot is (0,0) and TerrainBackdropRenderer offsets it
         by half its bounds, so world (0,0) is the image centre).

Target:  Assets/Scripts/Building/PlaceableLandMask.cs  (generated C# data)
         consumed by GridOverlayRenderer (which cells to draw) and
         BuildingSystem (BuildingGrid.SetPlaceableMask -> CanPlace).

Why offline: the texture is imported with isReadable:0, so the runtime cannot
sample it. Baking keeps the runtime free of Texture2D.GetPixels and of any
editor-only dependency.

Colour model (measured on the real image):
  ground  flat warm beige rgb(222,214,200), 49.5% of the image
  fog     flat bright neutral (R == G, lum 228-250), ~15%
  rock    green-tinted, G > R + GREEN_MARGIN
  tree    autumn orange/red, R > G + TREE_MARGIN and bright

Which flat fill is the ground (confirmed with the artist, 2026-10-02): the warm
beige one - it carries the grass tufts, pebbles and stone stairs, and it is what
the four hand-authored placeable rects used to cover (52% beige vs 16% bright).
The brighter flat shapes are the drifting fog banks.

Two filters then trim the beige down to real building ground:

1. Islands. The beige also floods the plane *outside* the islands. Label it with
   8-connectivity: the huge component touching the image border is that outer
   plane (4.64M px), while the enclosed components are the plateau tops. Only the
   enclosed ones are kept (the artist's first cross-outs landed 93.6% on the
   border component).

2. Buildability. Some enclosed beige is still not ground: slivers of plateau
   hanging over a canyon, single cells in the mist, detached rocks. They are not
   a judgement call - a land region is only worth building on if it can host the
   largest building in the game (3x3 cells, `pill_hall` in building_defs.json).
   So the cell mask is labelled and a component survives only when it contains at
   least one all-land FOOTPRINT_W x FOOTPRINT_H block. On the current art that
   matches the artist's four remaining cross-outs exactly: the 17-cell top-right
   island (3 blocks) stays while the 9-cell strip, the 5-cell canyon ledge and
   the two single cells (0 blocks each) go.

Off-image pixels count as "not land" (fail-closed).

Usage:
  python Tools/art/bake_land_mask.py            # write the C# file
  python Tools/art/bake_land_mask.py --preview  # print ASCII + write preview PNG

The same mask can be produced (and hand-tuned) inside the Editor with
Xianxia > Land Mask Painter (Assets/Editor/LandMaskPainterWindow.cs), which runs the
identical classification in C# and writes the same file. Use this script for the
batch/CLI path and to study the art; use the window when you want to paint over the
result. Both emit the same C# shape, so either tool can overwrite the other's output
without the runtime noticing (verified 2026-10-02: the C# classifier reproduces this
script's 344 raw / 328 pruned cells bit-for-bit on the current art).
"""

import argparse
import os
import sys

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
IMAGE_PATH = os.path.join(ROOT, "UnityProject/Assets/Resources/tilemap/map/mountain1_wx/mountain1.png")
OUT_PATH = os.path.join(ROOT, "UnityProject/Assets/Scripts/Building/PlaceableLandMask.cs")
PREVIEW_PATH = os.path.join(ROOT, "Tools/art/out/land_mask_preview.png")

# --- geometry (must mirror CameraFramingConfig / IsometricCellMath) ---
PPU = 100.0
CELL_W = 1.28   # gridblock.png 128x66 @ PPU100
CELL_H = 0.66
BACKDROP_W = 40.96
BACKDROP_H = 33.28

# The backdrop is an axis-aligned rectangle in world space, but a cell range is a
# square in *cell* space - which is rotated 45 degrees against it. The image
# corners therefore reach out to |x|,|z| ~ 41, not ~20.
FULL_ORIGIN = -42
FULL_SIZE = 84

# Margin (cells) kept around the land bounding box when trimming the emitted mask.
MARGIN = 1

# --- classification thresholds ---
LAND_LUM = 222.0      # above the 212-216 mist spike (mist maxes ~217)
GREEN_MARGIN = 4      # G > R + margin  => rock / foliage
TREE_MARGIN = 25      # R > G + margin and bright => autumn tree
TREE_MIN_R = 140

# Largest building footprint in the game (Assets/Resources/Data/building_defs.json,
# `pill_hall` 3x3). A land region that cannot host one is not building ground.
FOOTPRINT_W = 3
FOOTPRINT_H = 3

# --- cell sampling ---
# Fraction of sample points inside the cell's diamond that must be land. A
# fraction below 1 erodes the mask slightly, so a drawn diamond (and any
# building footprint snapped to it) stays visually on the plateau instead of
# hanging over the cliff edge. Sampling a lattice of points instead of a few
# vertices keeps a stray grass/rock pixel from flipping a whole cell.
SAMPLE_INSET = 0.9
LAND_FRACTION = 0.85


def flat_beige(arr):
    r, g, b = arr[..., 0], arr[..., 1], arr[..., 2]
    return (np.abs(r - 222) <= 6) & (np.abs(g - 214) <= 6) & (np.abs(b - 200) <= 8)


def enclosed_beige(beige, min_px=2000):
    """Beige that belongs to a component which does NOT touch the image border.

    Those are the plateau tops: the cliffs around them cut them off from the
    outer plane, which is one huge component that does reach the border.
    Returns (keep mask, component ids of the kept pixels, 0 elsewhere).
    """
    from scipy import ndimage

    lab, n = ndimage.label(beige, structure=np.ones((3, 3), dtype=int))
    sizes = ndimage.sum(beige, lab, range(1, n + 1))
    keep = np.zeros_like(beige)
    for idx in np.nonzero(sizes >= min_px)[0] + 1:
        m = lab == idx
        if m[0].any() or m[-1].any() or m[:, 0].any() or m[:, -1].any():
            continue
        keep |= m
    return keep, np.where(keep, lab, 0)


def load_land_pixels(path, land_lum=LAND_LUM, mode="islands"):
    """-> (land bool array, component id array). Ids are 0 where not land."""
    from scipy import ndimage

    img = Image.open(path).convert("RGB")
    arr = np.asarray(img).astype(np.int16)
    r, g, b = arr[..., 0], arr[..., 1], arr[..., 2]
    lum = (r + g + b) / 3.0

    if mode == "beige":
        beige = flat_beige(arr)
        lab, _ = ndimage.label(beige, structure=np.ones((3, 3), dtype=int))
        return beige, np.where(beige, lab, 0)

    if mode == "islands":
        return enclosed_beige(flat_beige(arr))

    # legacy reading: the brighter flat shapes are the plateau tops
    land = lum >= land_lum
    land &= g <= r + GREEN_MARGIN          # not rock / green foliage
    land &= ~((r > g + TREE_MARGIN) & (r > TREE_MIN_R))   # not autumn tree
    return land, np.zeros_like(arr[..., 0])


def world_to_pixel(wx, wy, img_h):
    """World -> PNG (row, col). Image is centred on the origin, y up in Unity."""
    col = (wx + BACKDROP_W / 2) * PPU
    py_bottom = (wy + BACKDROP_H / 2) * PPU
    row = (img_h - 1) - py_bottom
    return row, col


def sample_land(land, wx, wy):
    h, w = land.shape
    row, col = world_to_pixel(wx, wy, h)
    r = int(round(row))
    c = int(round(col))
    if r < 0 or c < 0 or r >= h or c >= w:
        return False
    return bool(land[r, c])


def cell_center(x, y):
    return (x - y) * (CELL_W * 0.5), (x + y) * (CELL_H * 0.5)


# Points of the diamond sampling lattice, built once (relative to the cell centre).
SAMPLE_STEPS = 5
SAMPLE_OFFSETS = tuple(
    (dx * CELL_W * 0.5 * SAMPLE_INSET, dy * CELL_H * 0.5 * SAMPLE_INSET)
    for dx in [2.0 * i / (SAMPLE_STEPS - 1) - 1.0 for i in range(SAMPLE_STEPS)]
    for dy in [2.0 * j / (SAMPLE_STEPS - 1) - 1.0 for j in range(SAMPLE_STEPS)]
    if abs(dx) + abs(dy) <= 1.0
)


def cell_land(land, blobs, x, y, land_fraction=LAND_FRACTION):
    """-> (is_land_cell, set of source component ids the cell's samples touch)."""
    h, w = land.shape
    cx, cy = cell_center(x, y)
    hits = 0
    ids = set()
    for dx, dy in SAMPLE_OFFSETS:
        row, col = world_to_pixel(cx + dx, cy + dy, h)
        r = int(round(row))
        c = int(round(col))
        if r < 0 or c < 0 or r >= h or c >= w or not land[r, c]:
            continue
        hits += 1
        if blobs is not None and blobs[r, c]:
            ids.add(int(blobs[r, c]))
    return hits >= land_fraction * len(SAMPLE_OFFSETS) - 1e-9, ids


def build_full_mask(land, blobs, land_fraction=LAND_FRACTION):
    """Land flags over every cell the backdrop can reach (84x84, origin -42).

    Also returns, per cell, the set of source pixel-component ids it came from -
    so a component that gets dropped can drop its pixels too.
    """
    full = [[False] * FULL_SIZE for _ in range(FULL_SIZE)]
    src = [[frozenset()] * FULL_SIZE for _ in range(FULL_SIZE)]
    for iz in range(FULL_SIZE):
        for ix in range(FULL_SIZE):
            ok, ids = cell_land(land, blobs, FULL_ORIGIN + ix, FULL_ORIGIN + iz, land_fraction)
            full[iz][ix] = ok
            src[iz][ix] = frozenset(ids)
    return full, src


def keep_buildable(full, fw=FOOTPRINT_W, fh=FOOTPRINT_H, min_cells=1):
    """Drop connected land regions that cannot host the largest footprint.

    A region survives only if it contains at least one all-land fw x fh block
    (and has at least `min_cells` cells). Returns (kept grid, dropped cells).
    """
    from scipy import ndimage

    grid = np.array(full, dtype=bool)
    lab, n = ndimage.label(grid, structure=np.ones((3, 3), dtype=int))
    sizes = ndimage.sum(grid, lab, range(1, n + 1))
    keep = np.zeros_like(grid)
    for i in range(1, n + 1):
        if sizes[i - 1] < min_cells:
            continue
        m = lab == i
        rows_, cols = m.shape
        fit = False
        for iz in range(rows_ - fh + 1):
            if fit:
                break
            for ix in range(cols - fw + 1):
                if m[iz:iz + fh, ix:ix + fw].all():
                    fit = True
                    break
        if fit:
            keep |= m
    dropped = grid & ~keep
    return [[bool(v) for v in row] for row in keep], int(dropped.sum())


def trim(full):
    """Land bounding box + MARGIN, rounded out to a centred even square."""
    cells = [(FULL_ORIGIN + ix, FULL_ORIGIN + iz)
             for iz in range(FULL_SIZE) for ix in range(FULL_SIZE) if full[iz][ix]]
    if not cells:
        raise SystemExit("no land cells found - check the classification thresholds")
    xs = [c[0] for c in cells]
    zs = [c[1] for c in cells]
    min_x, max_x = min(xs) - MARGIN, max(xs) + MARGIN
    min_z, max_z = min(zs) - MARGIN, max(zs) + MARGIN
    # a centred even square grid (GridOverlayRenderer.GridExtent), origin -extent/2
    extent = 2 * int(max(abs(min_x), abs(max_x), abs(min_z), abs(max_z)))
    print("land cell bbox      : x %d..%d  z %d..%d" % (min(xs), max(xs), min(zs), max(zs)))
    print("trimmed grid extent : %d  (origin %d,%d, cells %d..%d)" % (
        extent, -extent // 2, -extent // 2, -extent // 2, extent // 2 - 1))
    return extent


def build_mask(full, extent):
    half = extent // 2
    rows = []
    for iz in range(extent):
        z = -half + iz
        fz = z - FULL_ORIGIN
        row = ""
        for ix in range(extent):
            x = -half + ix
            fx = x - FULL_ORIGIN
            inside = 0 <= fx < FULL_SIZE and 0 <= fz < FULL_SIZE
            row += "#" if (inside and full[fz][fx]) else "."
        rows.append(row)
    return rows


def preview(tint, blobs, rows, preview_path=PREVIEW_PATH):
    n_land = sum(r.count("#") for r in rows)
    print("tinted pixels       : %.1f%% of the image (sample points per cell: %d, threshold %.2f)" % (
        100.0 * tint.mean(), len(SAMPLE_OFFSETS), LAND_FRACTION))
    print("emitted mask %dx%d centred on origin: %d/%d cells land (%.1f%%), "
          "largest-block rule %dx%d" % (
              len(rows), len(rows), n_land, len(rows) * len(rows),
              100.0 * n_land / (len(rows) * len(rows)), FOOTPRINT_W, FOOTPRINT_H))

    half = len(rows) // 2
    print("\nmask (col = x from %d, row = z from %d; '#' = land, '.' = mist/rock/tree):"
          % (-half, -half))
    print("      " + "".join(str(abs((-half + i) // 10) % 10) for i in range(len(rows))))
    print("      " + "".join(str(abs(-half + i) % 10) for i in range(len(rows))))
    for iz, row in enumerate(rows):
        print("%5d %s" % (-half + iz, row))

    # which parts of the backdrop the mask reaches, in world units
    xs = [(-half + ix, -half + iz)
          for iz in range(len(rows)) for ix in range(len(rows)) if rows[iz][ix] == "#"]
    ws = [cell_center(x, z) for x, z in xs]
    print("\nland cells world bbox: X %6.2f..%6.2f  Y %6.2f..%6.2f  (backdrop +-%0.2f, +-%0.2f)" % (
        min(w[0] for w in ws), max(w[0] for w in ws),
        min(w[1] for w in ws), max(w[1] for w in ws), BACKDROP_W / 2, BACKDROP_H / 2))
    off = sum(1 for w in ws if abs(w[0]) > BACKDROP_W / 2 or abs(w[1]) > BACKDROP_H / 2)
    print("land cells outside the backdrop rectangle: %d" % off)

    os.makedirs(os.path.dirname(preview_path), exist_ok=True)
    h, w = tint.shape
    rgba = np.zeros((h, w, 4), dtype=np.uint8)
    rgba[..., 0] = 255
    rgba[..., 1] = 40
    rgba[..., 2] = 40
    rgba[..., 3] = np.where(tint, 70, 0).astype(np.uint8)
    # mark every accepted cell with a blue dot so the erosion is visible
    for iz in range(len(rows)):
        for ix in range(len(rows)):
            if rows[iz][ix] != "#":
                continue
            cx, cy = cell_center(-half + ix, -half + iz)
            row, col = world_to_pixel(cx, cy, h)
            r, c = int(round(row)), int(round(col))
            if 0 <= r < h and 0 <= c < w:
                rgba[max(0, r - 4):r + 5, max(0, c - 4):c + 5] = (0, 160, 255, 255)
    base = Image.open(IMAGE_PATH).convert("RGB")
    Image.alpha_composite(base.convert("RGBA"), Image.fromarray(rgba)).convert("RGB").save(preview_path)
    print("preview written: %s" % os.path.relpath(preview_path, ROOT))


def emit_cs(rows):
    extent = len(rows)
    half = extent // 2
    land_count = sum(r.count("#") for r in rows)
    body = "\n".join('            "%s",' % r for r in rows)
    return '''// <auto-generated>
//   Generated by Tools/art/bake_land_mask.py - do not edit by hand.
//   Regenerate after the terrain art changes:  python Tools/art/bake_land_mask.py
//
//   Per-cell "land" mask of the painted backdrop
//   {source_rel} ({w}x{h} px @ PPU {ppu} = {ww:.2f}x{wh:.2f} world
//   units, centred on the origin).
//
//   Land = the flat warm beige ground (rgb 222,214,200) of the plateau tops.
//   Fog banks, rock, trees, the beige plane *outside* the islands and everything
//   off-image are NOT land, so both the build-mode overlay and BuildingGrid.CanPlace
//   stay on the islands instead of spilling over the fog.
//
//   Two filters, both checked against the artist's cross-outs on the preview:
//     * "on an island" - the beige is labelled with 8-connectivity and only the
//       components that do NOT touch the image border are kept (the outer plane is
//       one huge border-touching component).
//     * "worth building on" - the cell mask is labelled again and a region is kept
//       only if it can host the largest footprint in the game, {fw}x{fh} cells
//       (pill_hall in building_defs.json). Slivers hanging over a canyon and single
//       cells floating in the mist are dropped with it.
//
//   Cell size / mapping mirror CameraFramingConfig + IsometricCellMath:
//     centre(x,z) = ((x - z) * {cw}/2, (x + z) * {ch}/2)
// </auto-generated>
namespace Xianxia.Sect.Building
{{
    /// <summary>
    /// Baked placeable-land mask - {count} of {total} cells are land.
    /// Row index = z - <see cref="OriginY"/>, column index = x - <see cref="OriginX"/>.
    /// The rect matches GridOverlayRenderer.GridExtent and the production BuildingGrid.
    /// </summary>
    public static class PlaceableLandMask
    {{
        public const string SourceImage = "{source_rel}";

        /// <summary>Logical cell of Rows[0][0] (matches the isometric cell grid).</summary>
        public const int OriginX = {ox};
        public const int OriginY = {oy};

        public const int Width = {mw};
        public const int Height = {mh};

        public const char LandChar = '#';

        /// <summary>Number of land cells in the whole mask.</summary>
        public const int LandCellCount = {count};

        /// <summary>
        /// Largest building footprint the bake required a land region to fit
        /// (pill_hall in building_defs.json). Kept in sync by the generator.
        /// </summary>
        public const int MinFootprintWidth = {fw};
        public const int MinFootprintHeight = {fh};

        private static readonly string[] Rows =
        {{
{body}
        }};

        /// <summary>Cell is plateau top surface (mist / rock / tree / off-image = false).</summary>
        public static bool IsLand(int x, int z)
        {{
            int ix = x - OriginX;
            int iz = z - OriginY;
            if (ix < 0 || iz < 0 || ix >= Width || iz >= Height) return false;
            return Rows[iz][ix] == LandChar;
        }}

        /// <summary>
        /// Row-major (index = (z - originZ) * width + (x - originX)) bool block for
        /// an arbitrary rect of logical cells - the shape BuildingGrid.SetPlaceableMask
        /// takes. Cells outside the mask bounds are false (fail-closed).
        /// </summary>
        public static bool[] BuildCells(int originX, int originZ, int width, int height)
        {{
            var cells = new bool[width * height];
            for (int iz = 0; iz < height; iz++)
            {{
                for (int ix = 0; ix < width; ix++)
                {{
                    cells[iz * width + ix] = IsLand(originX + ix, originZ + iz);
                }}
            }}
            return cells;
        }}
    }}
}}
'''.format(
        source_rel="Assets/Resources/tilemap/map/mountain1_wx/mountain1.png",
        w=4096, h=3328, ppu=int(PPU), ww=BACKDROP_W, wh=BACKDROP_H,
        cw="%.2f" % CELL_W, ch="%.2f" % CELL_H,
        count=land_count, total=extent * extent,
        ox=-half, oy=-half, mw=extent, mh=extent,
        fw=FOOTPRINT_W, fh=FOOTPRINT_H,
        body=body)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--preview", action="store_true", help="print ASCII maps + write preview PNG")
    ap.add_argument("--land-lum", type=float, default=LAND_LUM)
    ap.add_argument("--land-fraction", type=float, default=LAND_FRACTION)
    ap.add_argument("--footprint-w", type=int, default=FOOTPRINT_W,
                    help="min all-land block width a region must host to stay buildable")
    ap.add_argument("--footprint-h", type=int, default=FOOTPRINT_H)
    ap.add_argument("--mode", choices=("islands", "beige", "bright"), default="islands",
                    help="'islands' (default) = the beige plane on the plateau tops only; "
                         "'beige' = the whole beige plane incl. outside the islands; "
                         "'bright' = legacy reading where the cream shapes are the ground")
    ap.add_argument("--preview-out", default=PREVIEW_PATH)
    args = ap.parse_args()

    if not os.path.exists(IMAGE_PATH):
        print("missing image: %s" % IMAGE_PATH, file=sys.stderr)
        return 1

    land, blobs = load_land_pixels(IMAGE_PATH, args.land_lum, args.mode)
    full, src = build_full_mask(land, blobs, args.land_fraction)
    raw = sum(r.count(True) for r in full)
    full, dropped = keep_buildable(full, args.footprint_w, args.footprint_h)
    print("cells before buildability filter: %d (dropped %d in regions too small to "
          "fit %dx%d)" % (raw, dropped, args.footprint_w, args.footprint_h))
    extent = trim(full)
    rows = build_mask(full, extent)

    if args.preview:
        # tint only the beige that survived, so the preview shows what is buildable
        kept_ids = set()
        for iz in range(FULL_SIZE):
            for ix in range(FULL_SIZE):
                if full[iz][ix]:
                    kept_ids |= src[iz][ix]
        tint = np.isin(blobs, list(kept_ids)) if kept_ids else np.zeros_like(land)
        preview(tint, blobs, rows, args.preview_out)
        return 0

    os.makedirs(os.path.dirname(OUT_PATH), exist_ok=True)
    with open(OUT_PATH, "w", encoding="utf-8", newline="\n") as f:
        f.write(emit_cs(rows))
    print("wrote %s (extent %d, %d land cells)" % (
        os.path.relpath(OUT_PATH, ROOT), len(rows), sum(r.count("#") for r in rows)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
