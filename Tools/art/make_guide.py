#!/usr/bin/env python3
"""Build the silhouette guide images that Qwen-Image-2.1 edits (Phase 2.1 / Phase 4).

A guide is a flat, deliberately crude stand-in for the SHAPE of the artwork we
want: a solid `#FF00FF` background plus grey blocks describing the silhouette.
Qwen "redraws" it into finished art, and the magenta is chroma-keyed away by
`tools/art/postprocess.py`.

Hard rules honoured here (task iron rule 7):
  * NO pixel of `Artwork/Body_dizijm.png` is read, copied or resampled.
    Every guide is drawn from primitives only.
  * The reference asset is used for MEASUREMENTS ONLY; those measurements live
    in `art/disciple_list/manifest.json` and are cited per guide below.

Supersampling: guides are drawn at 4x and downsampled with a BOX (area-average)
filter so the silhouette edges are smoothly anti-aliased, matching the reference
asset's own anti-aliased edges (4.3% of its opaque pixels are semi-transparent).
BOX is used rather than Lanczos on purpose: Lanczos rings on a hard synthetic
edge, which produced off-tone pixels like (93,88,93) and (78,97,78) along the
spine boundary. Area-averaging cannot overshoot, so the only colours in the
output are the two intended greys, the magenta background, and true blends
between them.

Usage
-----
    python tools/art/make_guide.py scroll_paper      # implemented
    python tools/art/make_guide.py --list            # show what is implemented
"""

from __future__ import annotations

import argparse
import json
import pathlib
import sys

from PIL import Image, ImageDraw

REPO = pathlib.Path(__file__).resolve().parents[2]
MANIFEST_PATH = REPO / "art" / "disciple_list" / "manifest.json"
GUIDES_DIR = REPO / "art" / "disciple_list" / "guides"

MAGENTA = (255, 0, 255)


def load_manifest() -> dict:
    return json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))


def draw_scroll_paper(spec: dict) -> Image.Image:
    """Portrait parchment page: grey body, darker rolled spine on both tall edges.

    Spine widths come from the measured reference: 32/1134 and 34/1134 of the
    ornament width. Those two bands are the only part of the reference geometry
    that is not contaminated by the wooden rod, so they are reused verbatim.
    """
    w, h = spec["canvas"]
    ss = spec.get("supersample", 4)
    W, H = w * ss, h * ss

    img = Image.new("RGB", (W, H), MAGENTA)
    d = ImageDraw.Draw(img)

    m = spec["outer_margin_pct"]
    x0, y0 = round(W * m), round(H * m)
    x1, y1 = W - x0, H - y0
    r = spec["corner_radius_px"] * ss

    paper = spec["paper_grey"]
    spine = spec["spine_grey"]

    # The rolled spine sits at the outer edge of the page, so it is drawn first
    # as the full rounded body in the darker tone, then the paper is inset by
    # the spine width on the left and right.
    d.rounded_rectangle([x0, y0, x1 - 1, y1 - 1], radius=r, fill=(spine, spine, spine))

    left_px = round((x1 - x0) * spec["spine_pct"]["left"])
    right_px = round((x1 - x0) * spec["spine_pct"]["right"])
    d.rounded_rectangle(
        [x0 + left_px, y0, x1 - 1 - right_px, y1 - 1],
        radius=max(4, r - left_px),
        fill=(paper, paper, paper),
    )

    return img.resize((w, h), Image.BOX)


def draw_scroll_rod(spec: dict) -> Image.Image:
    """One long horizontal bar with slightly taller rounded end caps.

    Rendered horizontally because that is what the prompt describes; Unity
    rotates the sprite 90 degrees to serve as the left and right posts.
    """
    w, h = spec["canvas"]
    ss = spec.get("supersample", 4)
    W, H = w * ss, h * ss

    img = Image.new("RGB", (W, H), MAGENTA)
    d = ImageDraw.Draw(img)

    m = round(W * spec["outer_margin_pct"])
    x0, x1 = m, W - m
    bh = round(H * spec["bar_height_pct"])
    y0, y1 = (H - bh) // 2, (H - bh) // 2 + bh

    bar, end = spec["bar_grey"], spec["end_grey"]
    r = bh // 2

    d.rounded_rectangle([x0, y0, x1 - 1, y1 - 1], radius=r, fill=(bar, bar, bar))

    ew = round(W * spec["end_width_pct"])
    eh = round(bh * spec["end_height_scale"])
    ey0 = (H - eh) // 2
    for ex in (x0, x1 - 1 - ew):
        d.rounded_rectangle(
            [ex, ey0, ex + ew, ey0 + eh], radius=min(ew, eh) // 2, fill=(end, end, end)
        )

    return img.resize((w, h), Image.BOX)


def draw_close_medallion(spec: dict) -> Image.Image:
    """Round disc at the top with a darker centre, narrow tassel hanging below."""
    w, h = spec["canvas"]
    ss = spec.get("supersample", 4)
    W, H = w * ss, h * ss

    img = Image.new("RGB", (W, H), MAGENTA)
    d = ImageDraw.Draw(img)

    m = round(W * spec["outer_margin_pct"])
    disc = round(W * spec["disc_diameter_pct"])
    cx = W // 2
    d0 = disc // 2
    dg, ig, tg = spec["disc_grey"], spec["inner_grey"], spec["tassel_grey"]

    d.ellipse([cx - d0, m, cx + d0, m + disc], fill=(dg, dg, dg))
    inner = round(disc * spec["inner_diameter_pct"])
    i0 = inner // 2
    icy = m + disc // 2
    d.ellipse([cx - i0, icy - i0, cx + i0, icy + i0], fill=(ig, ig, ig))

    tw = round(W * spec["tassel_width_pct"])
    ty0 = m + disc
    ty1 = H - m
    d.rounded_rectangle(
        [cx - tw // 2, ty0, cx + tw // 2, ty1], radius=tw // 2, fill=(tg, tg, tg)
    )

    return img.resize((w, h), Image.BOX)


def draw_cloud_corner(spec: dict) -> Image.Image:
    """Overlapping lobes for a ruyi cloud mass, plus a rising lobe and a curl."""
    w, h = spec["canvas"]
    ss = spec.get("supersample", 4)
    W, H = w * ss, h * ss

    img = Image.new("RGB", (W, H), MAGENTA)
    d = ImageDraw.Draw(img)

    cg, ug = spec["cloud_grey"], spec["curl_grey"]

    for fx, fy, fr in spec["lobes"]:
        cx, cy, r = fx * W, fy * H, fr * W
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(cg, cg, cg))

    sx, sy, sr = spec["spiral"]
    cx, cy, r = sx * W, sy * H, sr * W
    rx, ry = r, r * 1.6  # a taller-than-wide curl reads better as a cloud hook
    d.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=(cg, cg, cg))
    d.ellipse(
        [cx - rx * 0.45, cy - ry * 0.45, cx + rx * 0.45, cy + ry * 0.45],
        fill=(ug, ug, ug),
    )

    return img.resize((w, h), Image.BOX)


def draw_title_plate(spec: dict) -> Image.Image:
    """Long horizontal plate with rounded ends and slightly taller end caps."""
    w, h = spec["canvas"]
    ss = spec.get("supersample", 4)
    W, H = w * ss, h * ss

    img = Image.new("RGB", (W, H), MAGENTA)
    d = ImageDraw.Draw(img)

    m = round(W * spec["outer_margin_pct"])
    x0, x1 = m, W - m
    ph = round(H * spec["plate_height_pct"])
    y0 = (H - ph) // 2
    y1 = y0 + ph

    pg, cg = spec["plate_grey"], spec["cap_grey"]
    d.rounded_rectangle(
        [x0, y0, x1 - 1, y1 - 1], radius=ph // 3, fill=(pg, pg, pg)
    )

    ew = round(W * spec["cap_width_pct"])
    eh = round(ph * spec["cap_height_scale"])
    ey0 = (H - eh) // 2
    for ex in (x0, x1 - 1 - ew):
        d.rounded_rectangle(
            [ex, ey0, ex + ew, ey0 + eh], radius=min(ew, eh) // 2, fill=(cg, cg, cg)
        )

    return img.resize((w, h), Image.BOX)


BUILDERS = {
    "scroll_paper": draw_scroll_paper,
    "scroll_rod": draw_scroll_rod,
    "close_medallion": draw_close_medallion,
    "cloud_corner": draw_cloud_corner,
    "title_plate": draw_title_plate,
}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("asset_id", nargs="?")
    ap.add_argument("--list", action="store_true", help="list implemented guides")
    args = ap.parse_args()

    if args.list or not args.asset_id:
        manifest = load_manifest()
        print("implemented :", ", ".join(sorted(BUILDERS)))
        print(
            "pending     :",
            ", ".join(
                a["id"] for a in manifest["assets"] if a["id"] not in BUILDERS
            ),
        )
        return 0

    manifest = load_manifest()
    try:
        asset = next(a for a in manifest["assets"] if a["id"] == args.asset_id)
    except StopIteration:
        print(f"ERROR: no asset '{args.asset_id}'", file=sys.stderr)
        return 2

    builder = BUILDERS.get(args.asset_id)
    if builder is None:
        print(
            f"ERROR: no guide builder for '{args.asset_id}' yet "
            "(Phase 4 implements the remaining four)",
            file=sys.stderr,
        )
        return 2

    spec = asset.get("guide")
    if spec is None:
        print(f"ERROR: manifest has no `guide` block for '{args.asset_id}'", file=sys.stderr)
        return 2

    GUIDES_DIR.mkdir(parents=True, exist_ok=True)
    out = GUIDES_DIR / f"{args.asset_id}_guide.png"
    img = builder(spec)
    img.save(out)

    # Report only what is verifiable from the file itself.
    px = img.load()
    w, h = img.size
    exact_bg = sum(
        1 for y in range(0, h, 7) for x in range(0, w, 7) if px[x, y] == MAGENTA
    )
    sampled = len(range(0, w, 7)) * len(range(0, h, 7))
    greys = sorted({px[x, y][0] for y in range(0, h, 3) for x in range(0, w, 3)
                    if px[x, y][0] == px[x, y][1] == px[x, y][2]})
    print(f"wrote {out.relative_to(REPO)}")
    print(f"  size            : {w}x{h}  (aspect {w / h:.4f})")
    print(f"  mode            : {img.mode}")
    print(f"  exact #FF00FF   : {exact_bg}/{sampled} sampled px ({exact_bg / sampled * 100:.1f}%)")
    print(f"  exact greys seen: {greys}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
