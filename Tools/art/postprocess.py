#!/usr/bin/env python3
"""Turn a raw Qwen output into a transparent, trimmed, correctly sized sprite.

Pipeline (task Phase 3), fully deterministic — same input, same output:

    1. chroma key: colour distance to the guide's magenta. Below LOW  -> fully
       transparent. Above HIGH -> fully opaque. Between them -> a feathered
       alpha ramp, so anti-aliased edges survive instead of turning into a
       hard jagged cut.
    2. despill: pull the red/blue excess out of the partly-keyed edge pixels so
       the magenta cast does not linger as a pink or purple fringe.
    3. colour bleed: give every fully transparent pixel the RGB of its nearest
       opaque neighbour (one Euclidean distance transform). Alpha stays 0, so
       nothing becomes visible — this only stops the sprite showing dark, white
       or magenta halos once Unity samples it with bilinear filtering.
    4. trim the transparent border down to a small padding, then resize with
       Lanczos to the manifest's `target_px`. DOWNSCALE ONLY — if an asset
       would need upscaling, the script says so and leaves the size alone
       rather than inventing pixels.
    5. print a numeric report and write `art/disciple_list/final/<id>.png`.

Usage
-----
    python tools/art/postprocess.py art/disciple_list/raw/scroll_paper/seed101.png scroll_paper
    python tools/art/postprocess.py --all-runs scroll_paper   # every raw seed
"""

from __future__ import annotations

import argparse
import json
import pathlib
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

REPO = pathlib.Path(__file__).resolve().parents[2]
MANIFEST_PATH = REPO / "art" / "disciple_list" / "manifest.json"
RAW_DIR = REPO / "art" / "disciple_list" / "raw"
FINAL_DIR = REPO / "art" / "disciple_list" / "final"

# Distance from the key colour. < LOW is background, > HIGH is subject.
LOW = 60.0
HIGH = 160.0
PADDING = 4


def load_manifest() -> dict:
    return json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))


def key_color(manifest: dict) -> np.ndarray:
    return np.array(manifest["chroma_key"]["rgb"], dtype=np.float64)


def chroma_key(rgb: np.ndarray, key: np.ndarray) -> np.ndarray:
    """Return a float alpha in [0,1]: 0 = background, 1 = subject."""
    d = np.sqrt(((rgb.astype(np.float64) - key) ** 2).sum(axis=2))
    # LOW..HIGH maps to 1..0 (near the key = transparent)
    t = np.clip((d - LOW) / (HIGH - LOW), 0.0, 1.0)
    return t


def despill(rgb: np.ndarray, alpha: np.ndarray, key: np.ndarray) -> np.ndarray:
    """Reduce the key colour's channel bias on partially keyed pixels only."""
    out = rgb.astype(np.float64).copy()
    edge = (alpha > 0.0) & (alpha < 1.0)
    if not edge.any():
        return out
    # Magenta is a red+blue spike; on edge pixels cap red and blue at green
    # plus a small tolerance. Fully opaque pixels are left alone.
    g = out[..., 1]
    tol = 12.0
    out[..., 0] = np.where(edge, np.minimum(out[..., 0], g + tol), out[..., 0])
    out[..., 2] = np.where(edge, np.minimum(out[..., 2], g + tol), out[..., 2])
    return out


def color_bleed(
    rgb: np.ndarray, alpha: np.ndarray, key: np.ndarray
) -> tuple[np.ndarray, int, int]:
    """Give every transparent pixel the RGB of its NEAREST opaque pixel.

    Alpha is untouched, so this is invisible directly — it exists so a
    bilinear tap across the edge cannot drag the key colour, or a black or
    white fringe, into what you actually see.

    A single `distance_transform_edt` with `return_indices=True` computes the
    nearest opaque pixel for the whole image in one pass, which is both exact
    (a true Euclidean nearest neighbour, not a k-step dilation approximation)
    and O(n) instead of one full-image scan per iteration. That matters: the
    earlier iterative version took minutes on a 3840x288 canvas.

    Because alpha > 0 by construction implies distance-from-key > LOW, every
    opaque pixel is outside the key threshold, so after this fill no
    transparent pixel can still be key-coloured either. That makes the
    `near magenta = 0` guarantee structural rather than tuned.

    Returns (rgb, method, near_key_remaining).
    """
    solid = alpha > 0.0
    if not solid.any():
        d = np.sqrt(((rgb - key) ** 2).sum(axis=2))
        return rgb.copy(), 0, int((d < LOW).sum())

    nearest = ndimage.distance_transform_edt(
        ~solid, return_distances=False, return_indices=True
    )
    out = rgb[nearest[0], nearest[1]]

    d = np.sqrt(((out - key) ** 2).sum(axis=2))
    return out, 1, int((d < LOW).sum())


def trim_to_content(alpha: np.ndarray, padding: int) -> tuple[int, int, int, int]:
    ys, xs = np.where(alpha > 0)
    if len(ys) == 0:
        return 0, 0, alpha.shape[1], alpha.shape[0]
    h, w = alpha.shape
    x0 = max(0, int(xs.min()) - padding)
    y0 = max(0, int(ys.min()) - padding)
    x1 = min(w, int(xs.max()) + 1 + padding)
    y1 = min(h, int(ys.max()) + 1 + padding)
    return x0, y0, x1, y1


def process(raw_path: pathlib.Path, asset: dict, manifest: dict, write: bool = True):
    key = key_color(manifest)
    src = Image.open(raw_path).convert("RGB")
    rgb = np.array(src)
    src_h, src_w = src.height, src.width

    alpha = chroma_key(rgb, key)
    rgb2 = despill(rgb, alpha, key)
    rgb2, bleed_rounds, bleed_left = color_bleed(rgb2, alpha, key)

    rgba = np.dstack([np.clip(rgb2, 0, 255), alpha * 255.0]).astype(np.uint8)
    img = Image.fromarray(rgba, "RGBA")

    # trim, then downscale-only to target
    a0 = alpha
    x0, y0, x1, y1 = trim_to_content(a0, PADDING)
    img = img.crop((x0, y0, x1, y1))
    trimmed = img.size

    target = tuple(asset["target_px"])
    tw, th = target
    cw, ch = img.size
    scale = min(tw / cw, th / ch)
    upscaled = scale > 1.0
    if upscaled:
        new_size = (cw, ch)  # refuse to upscale
    else:
        # preserve the trimmed aspect, never exceed the target box
        new_size = (max(1, round(cw * scale)), max(1, round(ch * scale)))
    if new_size != img.size:
        img = img.resize(new_size, Image.LANCZOS)

    # Post-resize safety pass. Lanczos can pull a faint key tint inward from
    # any transparent pixel the bleed could not reach, so re-check on the
    # FINAL raster and neutralise whatever is left. These pixels are deep in
    # the transparent interior, so recolouring them cannot change what is seen.
    final_arr = np.array(img).astype(np.float64)
    fd = np.sqrt(((final_arr[..., :3] - key) ** 2).sum(axis=2))
    leftover = fd < LOW
    safety_fixed = int(leftover.sum())
    if safety_fixed:
        solid_final = final_arr[..., 3] > 0
        if solid_final.any():
            neutral = final_arr[..., :3][solid_final].mean(axis=0)
        else:
            neutral = np.array([128.0, 128.0, 128.0])
        final_arr[..., 0] = np.where(leftover, neutral[0], final_arr[..., 0])
        final_arr[..., 1] = np.where(leftover, neutral[1], final_arr[..., 1])
        final_arr[..., 2] = np.where(leftover, neutral[2], final_arr[..., 2])
        img = Image.fromarray(final_arr.astype(np.uint8), "RGBA")

    out_path = FINAL_DIR / f"{asset['id']}.png"
    if write:
        FINAL_DIR.mkdir(parents=True, exist_ok=True)
        img.save(out_path)

    # ── numeric report ──
    arr = np.array(img)
    al = arr[..., 3]
    total = al.size
    n0 = int((al == 0).sum())
    nfull = int((al == 255).sum())
    nsemi = int(((al > 0) & (al < 255)).sum())
    near_key = 0
    near_key_visible = 0
    if arr[..., :3].size:
        dd = np.sqrt(
            ((arr[..., :3].astype(np.float64) - key) ** 2).sum(axis=2)
        )
        near_key = int((dd < LOW).sum())
        near_key_visible = int(((dd < LOW) & (al > 0)).sum())

    report = {
        "asset_id": asset["id"],
        "raw": str(raw_path.relative_to(REPO)),
        "raw_size": [src_w, src_h],
        "trimmed_size": list(trimmed),
        "final_size": list(img.size),
        "target_px": list(target),
        "upscale_needed": bool(upscaled),
        "has_alpha": bool((al < 255).any()),
        "alpha_zero_pct": round(n0 / total * 100, 2),
        "alpha_full_pct": round(nfull / total * 100, 2),
        "alpha_semi_pct": round(nsemi / total * 100, 2),
        "semi_pct_of_opaque": round(nsemi / max(1, nfull + nsemi) * 100, 2),
        "px_near_key_color": near_key,
        "px_near_key_color_visible": near_key_visible,
        "bleed_passes": bleed_rounds,
        "bleed_left_over": bleed_left,
        "post_resize_safety_fixed": safety_fixed,
        "bytes": out_path.stat().st_size if out_path.exists() else 0,
        "out": str(out_path.relative_to(REPO)),
    }
    return report


def print_report(r: dict) -> None:
    print(f"[{r['asset_id']}] {r['out']}")
    print(f"  raw         : {r['raw']}  {r['raw_size'][0]}x{r['raw_size'][1]}")
    print(f"  trimmed     : {r['trimmed_size'][0]}x{r['trimmed_size'][1]}  (+{PADDING}px padding)")
    print(f"  final       : {r['final_size'][0]}x{r['final_size'][1]}  target={r['target_px']}")
    print(f"  has alpha   : {r['has_alpha']}")
    print(
        f"  alpha mix   : {r['alpha_zero_pct']}% fully transparent / "
        f"{r['alpha_full_pct']}% fully opaque / {r['alpha_semi_pct']}% semi "
        f"({r['semi_pct_of_opaque']}% of covered px)"
    )
    print(
        f"  near magenta: {r['px_near_key_color']} px total / "
        f"{r['px_near_key_color_visible']} px visible  (both MUST be 0)"
    )
    print(
        f"  bleed       : {r['bleed_passes']} pass (nearest-neighbour), "
        f"{r['bleed_left_over']} left, "
        f"{r['post_resize_safety_fixed']} neutralised after resize"
    )
    print(f"  bytes       : {r['bytes']}")
    if r["upscale_needed"]:
        print("  !! would need UPSCALING — left at source size instead (rule: never upscale)")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("raw_png", nargs="?")
    ap.add_argument("asset_id", nargs="?")
    ap.add_argument(
        "--all-runs",
        metavar="ASSET_ID",
        help="process every raw PNG under raw/<ASSET_ID>/",
    )
    ap.add_argument(
        "--all-seeds-as",
        metavar="ASSET_ID",
        help="process every raw PNG and write each as final/<ASSET_ID>_seed<N>.png "
        "so a contact sheet can compare them",
    )
    args = ap.parse_args()

    manifest = load_manifest()

    def asset_of(aid: str) -> dict:
        try:
            return next(a for a in manifest["assets"] if a["id"] == aid)
        except StopIteration:
            print(f"ERROR: no asset '{aid}' in manifest", file=sys.stderr)
            raise SystemExit(2)

    if args.all_runs or args.all_seeds_as:
        aid = args.all_runs or args.all_seeds_as
        asset = asset_of(aid)
        raws = sorted((RAW_DIR / aid).glob("*.png"))
        if not raws:
            print(f"ERROR: no raw PNGs under {RAW_DIR / aid}", file=sys.stderr)
            return 2
        per_seed = bool(args.all_seeds_as)
        reports = []
        for raw in raws:
            tmp_asset = dict(asset)
            if per_seed:
                tag = raw.stem  # e.g. seed101
                tmp_asset["id"] = f"{aid}_{tag}"
            rep = process(raw, tmp_asset, manifest)
            print_report(rep)
            reports.append(rep)
        out = REPO / "art" / "disciple_list" / f"postprocess_report_{aid}.json"
        out.write_text(json.dumps(reports, indent=2) + "\n", encoding="utf-8")
        print(f"\nwrote {out.relative_to(REPO)}")
        return 0

    if not args.raw_png or not args.asset_id:
        ap.print_help()
        return 2

    asset = asset_of(args.asset_id)
    rep = process(pathlib.Path(args.raw_png), asset, manifest)
    print_report(rep)
    out = REPO / "art" / "disciple_list" / f"postprocess_report_{args.asset_id}.json"
    out.write_text(json.dumps(rep, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
