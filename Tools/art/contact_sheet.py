#!/usr/bin/env python3
"""Build a labelled contact sheet of an asset's seed variants.

Every variant is composited onto a checkerboard so transparency is obvious, and
labelled with its seed. This is a review aid for the checkpoint — it does not
judge the art, it only makes the four candidates comparable side by side.

Usage
-----
    python tools/art/contact_sheet.py scroll_paper
    python tools/art/contact_sheet.py scroll_paper --cols 2 --cell 380

    # compare two rounds in one sheet (each variant is labelled with its folder)
    python tools/art/contact_sheet.py scroll_paper \\
        --from art/disciple_list/final_round1 art/disciple_list/final \\
        --cols 4 --out scroll_paper_round1_vs_round2.png
"""

from __future__ import annotations

import argparse
import json
import pathlib
import re

from PIL import Image, ImageDraw, ImageFont

REPO = pathlib.Path(__file__).resolve().parents[2]
MANIFEST_PATH = REPO / "art" / "disciple_list" / "manifest.json"
FINAL_DIR = REPO / "art" / "disciple_list" / "final"
CONTACT_DIR = REPO / "art" / "disciple_list" / "contact"

CHECKER = 16
CHECKER_A = (150, 150, 150)
CHECKER_B = (200, 200, 200)
LABEL_BG = (24, 24, 24)
LABEL_FG = (245, 245, 245)

FONT_CANDIDATES = [
    r"C:\Windows\Fonts\segoeui.ttf",
    r"C:\Windows\Fonts\arial.ttf",
    "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
]


def load_font(size: int) -> ImageFont.ImageFont:
    for path in FONT_CANDIDATES:
        try:
            return ImageFont.truetype(path, size)
        except OSError:
            continue
    return ImageFont.load_default()


def checkerboard(w: int, h: int) -> Image.Image:
    img = Image.new("RGB", (w, h), CHECKER_A)
    d = ImageDraw.Draw(img)
    for y in range(0, h, CHECKER):
        for x in range(0, w, CHECKER):
            if ((x // CHECKER) + (y // CHECKER)) % 2:
                d.rectangle([x, y, x + CHECKER - 1, y + CHECKER - 1], fill=CHECKER_B)
    return img


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("asset_id")
    ap.add_argument("--cols", type=int, default=2)
    ap.add_argument("--cell", type=int, default=420, help="cell box size in px")
    ap.add_argument("--label-h", type=int, default=34)
    ap.add_argument(
        "--from",
        dest="from_dirs",
        nargs="*",
        default=None,
        help="one or more directories to collect variants from; a label tag is "
        "derived from each folder name. Defaults to art/disciple_list/final.",
    )
    ap.add_argument("--out", default=None, help="output file name")
    args = ap.parse_args()

    manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
    try:
        asset = next(a for a in manifest["assets"] if a["id"] == args.asset_id)
    except StopIteration:
        print(f"ERROR: no asset '{args.asset_id}' in manifest")
        return 2

    variants: list[tuple[str, pathlib.Path]] = []
    search_dirs = [pathlib.Path(d) for d in (args.from_dirs or [FINAL_DIR])]
    for d in search_dirs:
        tag = d.name
        found = sorted(d.glob(f"{args.asset_id}_seed*.png"))
        if not found:
            found = sorted(d.glob(f"{args.asset_id}.png"))
        for p in found:
            m = re.search(r"seed(\d+)", p.stem)
            bit = m.group(1) if m else p.stem
            label = f"{tag}:{bit}" if len(search_dirs) > 1 else bit
            variants.append((label, p))
    if not variants:
        print(f"ERROR: nothing to lay out for '{args.asset_id}' in {search_dirs}")
        return 2

    cell = args.cell
    label_h = args.label_h
    cols = max(1, args.cols)
    rows = (len(variants) + cols - 1) // cols

    sheet_w = cols * cell
    sheet_h = rows * (cell + label_h)
    sheet = checkerboard(sheet_w, sheet_h)
    d = ImageDraw.Draw(sheet)
    font = load_font(int(label_h * 0.55))

    for i, (tag, path) in enumerate(variants):
        cx = (i % cols) * cell
        cy = (i // cols) * (cell + label_h)
        # label strip
        d.rectangle([cx, cy, cx + cell - 1, cy + label_h - 1], fill=LABEL_BG)
        with Image.open(path) as sp:
            sprite = sp.convert("RGBA")
            sw, sh = sprite.size
            scale = min((cell - 12) / sw, (cell - 12) / sh)
            if scale > 1.0:
                scale = 1.0
            new = (max(1, int(sw * scale)), max(1, int(sh * scale)))
            sprite = sprite.resize(new, Image.LANCZOS)
            ox = cx + (cell - new[0]) // 2
            oy = cy + label_h + (cell - new[1]) // 2
            sheet.paste(sprite, (ox, oy), sprite)
        text = f"seed {tag}   {sw}x{sh}"
        d.text((cx + 10, cy + 7), text, fill=LABEL_FG, font=font)

    CONTACT_DIR.mkdir(parents=True, exist_ok=True)
    out = CONTACT_DIR / (args.out or f"{args.asset_id}.png")
    sheet.save(out)
    print(f"wrote {out.relative_to(REPO)}  {sheet.width}x{sheet.height}")
    print(f"  variants: {len(variants)}  ({', '.join(t for t, _ in variants)})")
    print(f"  sources : {', '.join(str(d) for d in search_dirs)}")
    print(f"  target_px: {asset['target_px']}   cells: {cols}x{rows} @ {cell}px")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
