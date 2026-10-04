#!/usr/bin/env python3
"""Portrait v2 Phase 4: deterministic postprocess for the user-chosen portraits.

Turns each chosen Qwen raw output into the final 1024x1536 RGBA portrait with
the canvas geometry the Unity side expects. Same input -> same output.

Pipeline (per image):

    1. chroma key on the magenta canvas: Euclidean RGB distance to (255,0,255).
       < LOW -> fully transparent, > HIGH -> fully opaque, feathered ramp in
       between so anti-aliased ink edges survive instead of a hard jagged cut.
    2. despill: cap red/blue on the partly-keyed edge pixels so the magenta
       cast does not linger as a pink fringe. Fully opaque pixels untouched.
    3. colour bleed: every fully transparent pixel takes the RGB of its nearest
       opaque neighbour (one Euclidean distance transform). Alpha stays 0.
       Stops halos when Unity samples the sprite with bilinear filtering.
       After this, no pixel can be key-coloured -> the "near magenta = 0"
       guarantee is structural, not tuned.
    4. geometry normalisation (uniform scale + translate, Lanczos):
       - equal head heights: every portrait's head box (hair-mass top -> chin)
         scales to exactly TARGET_HEAD_HEIGHT px
       - head centre lands exactly on TARGET_HEAD_CENTER = (512, 488)
         (the 50% x / 31.7708% from top invariant of the 256x384 canvas,
          x4 on 1024x1536)
       - frame bottom then lands near mid-thigh for these figures (recorded)
    5. numeric report + art/portraits_v2/manifest.json (before/after per image)
    6. review previews (NOT shipped): crosshair + icon-crop overlay, and the
       256x256 icon crop preview computed from AvatarIconCrop.NormalizedRect()
       (w=200/256, h=200/384, x=0.5-w/2, y_up=0.92-h -> px 112..912 x,
        124..924 y on 1024x1536). These previews are for human review only.

Head boxes below are VISUAL ESTIMATES measured from a labelled 25px grid view
of each head (hair-mass top excluding topknot/ornaments -> chin), accuracy
about +-5px. The transform math after that is exact.

Usage
-----
    python tools/art/portrait_postprocess.py            # write finals + manifest
    python tools/art/portrait_postprocess.py --check    # verify only
"""

from __future__ import annotations

import argparse
import json
import pathlib
import sys

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

REPO = pathlib.Path(__file__).resolve().parents[2]
FINAL_DIR = REPO / "art" / "portraits_v2" / "final"
MANIFEST_PATH = REPO / "art" / "portraits_v2" / "manifest.json"

KEY = np.array([255.0, 0.0, 255.0])
LOW = 60.0     # < LOW from key = fully background
HIGH = 160.0   # > HIGH from key = fully subject

CANVAS_W, CANVAS_H = 1024, 1536
TARGET_HEAD_HEIGHT = 240.0
TARGET_HEAD_CENTER = (512.0, 488.0)  # 50% x, 31.7708% from top (x4 of 256x384)

# AvatarIconCrop.NormalizedRect() in top-left-origin pixels on 1024x1536:
# w = 200/256 = 0.78125 -> 800px, h = 200/384 = 0.520833 -> 800px
# x = (0.5 - w/2)*1024 = 112 .. 912
# y = (1-0.92)*1536 = 122.88 .. 122.88+800 = 922.88  (rounded 124..924)
ICON_CROP_PX = (112, 124, 912, 924)

# Visual estimates from the labelled grid review (see module docstring).
# hair_top / chin: y of hair-mass top (topknot & hairpin excluded) and chin.
# face_cx: x centre of the face oval at eye level.
MEASUREMENTS = {
    "d000": {
        "src": "art/portraits_v2/anchor/1cf13e0c_000.png",
        "seed": 420103,
        "prompt_id": "1cf13e0c-816c-4337-a990-bc19f0ef09bb",
        "hair_top": 118,
        "chin": 290,
        "face_cx": 528,
    },
    "d002": {
        "src": "art/portraits_v2/phase3/d002/430202/acda02e0_000.png",
        "seed": 430202,
        "prompt_id": "acda02e0-7961-4a20-8421-2261f30a92de",
        "hair_top": 122,
        "chin": 292,
        "face_cx": 530,
    },
    "d003": {
        "src": "art/portraits_v2/phase3/d003/430302/7560e302_000.png",
        "seed": 430302,
        "prompt_id": "7560e302-ae90-4474-9ad5-ebf9a130164a",
        "hair_top": 132,
        "chin": 298,
        "face_cx": 528,
    },
}

MEASURE_METHOD = (
    "visual estimate from labelled 25px grid view of each head "
    "(hair-mass top, topknot excluded, -> chin; face_cx at eye level); +-5px"
)


def chroma_key(rgb: np.ndarray, key: np.ndarray) -> np.ndarray:
    """Float alpha in [0,1]: 0 = background, 1 = subject, feathered in between."""
    d = np.sqrt(((rgb.astype(np.float64) - key) ** 2).sum(axis=2))
    return np.clip((d - LOW) / (HIGH - LOW), 0.0, 1.0)


def despill(rgb: np.ndarray, alpha: np.ndarray) -> np.ndarray:
    """ลดอคติสี key (ม่วง/แดงน้ำเงิน) โดยไม่แตะสีอื่น

    เดิมทำแค่พิกเซล alpha 0..1 → พิกเซลทึบที่โมเดลวาดหมึกม่วงไว้ยังโผล่
    ตอนนี้ hue-neutralise พิกเซลที่ "ทั้ง R และ B สูงกว่า G" (เฉดม่วงเท่านั้น)
    โดยบังคับ R,B ไม่ให้เกิน G+tol — สีผิว (B ต่ำกว่า G), งานขาวดำ
    (R≈G≈B) และเส้นหมึกบางๆ ไม่โดน เพราะเงื่อนไขสองช่องพร้อมกัน
    """
    out = rgb.astype(np.float64).copy()
    visible = alpha > 0.0
    g = out[..., 1]
    tol = 8.0
    hue = visible & (out[..., 0] > g + tol) & (out[..., 2] > g + tol)
    if hue.any():
        out[..., 0] = np.where(hue, np.minimum(out[..., 0], g + tol), out[..., 0])
        out[..., 2] = np.where(hue, np.minimum(out[..., 2], g + tol), out[..., 2])
    # ขอบ feathered ได้ cap ที่เข้มกว่าเล็กน้อย
    edge = visible & (alpha < 1.0)
    if edge.any():
        edge_tol = 4.0
        out[..., 0] = np.where(edge, np.minimum(out[..., 0], g + edge_tol), out[..., 0])
        out[..., 2] = np.where(edge, np.minimum(out[..., 2], g + edge_tol), out[..., 2])
    return out


def color_bleed(rgb: np.ndarray, alpha: np.ndarray) -> np.ndarray:
    """Transparent pixels take the RGB of their nearest opaque neighbour."""
    solid = alpha > 0.0
    if not solid.any():
        return rgb.copy()
    nearest = ndimage.distance_transform_edt(
        ~solid, return_distances=False, return_indices=True
    )
    return rgb[nearest[0], nearest[1]]


def neutralize_canvas(canvas: Image.Image, tol: float = 8.0) -> Image.Image:
    """ผ่าน hue-neutralise ซ้ำหลัง resize — Lanczos ผสมสีขอบแล้วเด้งสีม่วงกลับมาได้"""
    arr = np.array(canvas).astype(np.float64)
    rgb, a = arr[..., :3], arr[..., 3]
    g = rgb[..., 1]
    hue = (a > 0) & (rgb[..., 0] > g + tol) & (rgb[..., 2] > g + tol)
    if not hue.any():
        return canvas
    rgb[..., 0] = np.where(hue, np.minimum(rgb[..., 0], g + tol), rgb[..., 0])
    rgb[..., 2] = np.where(hue, np.minimum(rgb[..., 2], g + tol), rgb[..., 2])
    return Image.fromarray(np.dstack([np.clip(rgb, 0, 255), a]).astype(np.uint8), "RGBA")


def magenta_cast_pixels(canvas: Image.Image) -> dict:
    """นับพิกเซลที่ยังเหลือเฉดม่วง (R>G+25 และ B>G+25) แยก solid/edge"""
    arr = np.array(canvas).astype(int)
    R, G, B, A = arr[..., 0], arr[..., 1], arr[..., 2], arr[..., 3]
    cast = (R > G + 25) & (B > G + 25) & (R > 70) & (B > 70)
    solid, edge = A >= 200, (A > 0) & (A < 200)
    return {
        "solid_px": int(solid.sum()),
        "solid_cast_px": int((solid & cast).sum()),
        "edge_px": int(edge.sum()),
        "edge_cast_px": int((edge & cast).sum()),
    }


def keyed_rgba(src_path: pathlib.Path) -> Image.Image:
    src = Image.open(src_path).convert("RGB")
    rgb = np.array(src)
    alpha = chroma_key(rgb, KEY)
    rgb2 = despill(rgb, alpha)
    rgb2 = color_bleed(rgb2, alpha)
    rgba = np.dstack([np.clip(rgb2, 0, 255), alpha * 255.0]).astype(np.uint8)
    return Image.fromarray(rgba, "RGBA")


def process(disciple_id: str, m: dict, write: bool) -> dict:
    src_path = REPO / m["src"]
    head_h = float(m["chin"] - m["hair_top"])
    cy = (m["chin"] + m["hair_top"]) / 2.0
    ax, ay = float(m["face_cx"]), cy
    s = TARGET_HEAD_HEIGHT / head_h

    img = keyed_rgba(src_path)
    sw, sh = img.size
    scaled = img.resize((round(sw * s), round(sh * s)), Image.LANCZOS)
    off_x = int(round(TARGET_HEAD_CENTER[0] - ax * s))
    off_y = int(round(TARGET_HEAD_CENTER[1] - ay * s))
    canvas = Image.new("RGBA", (CANVAS_W, CANVAS_H), (0, 0, 0, 0))
    canvas.paste(scaled, (off_x, off_y))
    canvas = neutralize_canvas(canvas)

    arr = np.array(canvas).astype(np.float64)
    rgb, alpha8 = arr[..., :3], arr[..., 3]
    d = np.sqrt(((rgb - KEY) ** 2).sum(axis=2))
    near_magenta_solid = int(((d < LOW) & (alpha8 >= 64)).sum())
    near_magenta_edge = int(((d < LOW) & (alpha8 > 0) & (alpha8 < 64)).sum())
    coverage = float((alpha8 > 0).sum()) / (CANVAS_W * CANVAS_H)

    achieved_cx = ax * s + off_x
    achieved_cy = ay * s + off_y
    out_h = head_h * s
    frame_bottom_src = (CANVAS_H - TARGET_HEAD_CENTER[1]) / s + ay

    out_path = FINAL_DIR / f"portrait_{disciple_id}.png"
    if write:
        canvas.save(out_path)
    size_bytes = out_path.stat().st_size if out_path.exists() else 0

    checks = {
        "size_1024x1536": canvas.size == (CANVAS_W, CANVAS_H),
        "mode_rgba": canvas.mode == "RGBA",
        "near_magenta_solid_is_zero": near_magenta_solid == 0,
        "alpha_coverage_in_range": 0.05 <= coverage <= 0.60,
        "file_size_over_100kb": size_bytes > 100_000,
    }

    if write:
        # Review-only overlays (never shipped to Unity).
        review = Image.new("RGB", canvas.size, (32, 34, 38))
        review.paste(canvas, (0, 0), canvas)
        dr = ImageDraw.Draw(review)
        cx, cy2 = TARGET_HEAD_CENTER
        dr.line([(cx - 60, cy2), (cx + 60, cy2)], fill=(255, 60, 0), width=3)
        dr.line([(cx, cy2 - 60), (cx, cy2 + 60)], fill=(255, 60, 0), width=3)
        x0, y0, x1, y1 = ICON_CROP_PX
        dr.rectangle([x0, y0, x1, y1], outline=(0, 200, 255), width=3)
        dr.text((10, 10), f"{disciple_id} seed {m['seed']}  s={s:.4f}  "
                          f"head_h={out_h:.1f}px", fill=(255, 255, 0))
        dr.text((10, 30), "orange= head-center target (512,488); "
                          "cyan= AvatarIconCrop rect (review only)",
                fill=(255, 255, 0))
        review.save(FINAL_DIR / f"review_{disciple_id}.png")
        canvas.crop(ICON_CROP_PX).resize((256, 256), Image.LANCZOS).save(
            FINAL_DIR / f"icon_preview_{disciple_id}.png"
        )

    return {
        "magenta_cast": magenta_cast_pixels(canvas),
        "src": m["src"],
        "seed": m["seed"],
        "prompt_id": m["prompt_id"],
        "measured_head_box_src_px": {
            "hair_top": m["hair_top"],
            "chin": m["chin"],
            "face_cx": m["face_cx"],
            "method": MEASURE_METHOD,
        },
        "head_height_src_px": head_h,
        "scale_uniform": round(s, 6),
        "paste_offset_px": [off_x, off_y],
        "achieved_head_center_px": [round(achieved_cx, 2), round(achieved_cy, 2)],
        "target_head_center_px": list(TARGET_HEAD_CENTER),
        "achieved_head_height_px": round(out_h, 2),
        "frame_bottom_src_y_px": round(frame_bottom_src, 1),
        "output": f"art/portraits_v2/final/portrait_{disciple_id}.png",
        "checks": checks,
        "near_magenta_pixels": {
            "alpha_ge_64": near_magenta_solid,
            "alpha_1_to_63": near_magenta_edge,
        },
        "alpha_coverage": round(coverage, 4),
        "file_size_bytes": size_bytes,
    }


def verify_only() -> int:
    ok = True
    for did in MEASUREMENTS:
        p = FINAL_DIR / f"portrait_{did}.png"
        if not p.exists():
            print(f"MISSING {p}")
            ok = False
            continue
        im = Image.open(p)
        arr = np.array(im.convert("RGBA")).astype(np.float64)
        d = np.sqrt(((arr[..., :3] - KEY) ** 2).sum(axis=2))
        solid_mag = int(((d < LOW) & (arr[..., 3] >= 64)).sum())
        cov = float((arr[..., 3] > 0).sum()) / (CANVAS_W * CANVAS_H)
        good = (im.size == (CANVAS_W, CANVAS_H) and im.mode == "RGBA"
                and solid_mag == 0 and 0.05 <= cov <= 0.60
                and p.stat().st_size > 100_000)
        ok = ok and good
        print(f"{did}: {im.size} {im.mode} near-magenta={solid_mag} "
              f"coverage={cov:.4f} bytes={p.stat().st_size} "
              f"{'OK' if good else 'FAIL'}")
    return 0 if ok else 1


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="verify outputs only")
    args = ap.parse_args()
    if args.check:
        return verify_only()

    FINAL_DIR.mkdir(parents=True, exist_ok=True)
    results = {}
    all_ok = True
    for did, m in MEASUREMENTS.items():
        r = process(did, m, write=True)
        results[did] = r
        ok = all(r["checks"].values())
        all_ok = all_ok and ok
        print(f"{did}: cast(solid/edge)="
              f"{r['magenta_cast']['solid_cast_px']}/{r['magenta_cast']['edge_cast_px']} "
              f"s={r['scale_uniform']:.4f} "
              f"head_h={r['achieved_head_height_px']:.1f}px "
              f"head_center={r['achieved_head_center_px']} "
              f"frame_bottom_src_y={r['frame_bottom_src_y_px']} "
              f"near-magenta={r['near_magenta_pixels']['alpha_ge_64']} "
              f"coverage={r['alpha_coverage']} "
              f"{'OK' if ok else 'FAIL'}")

    heights = [results[d]["achieved_head_height_px"] for d in MEASUREMENTS]
    manifest = {
        "task": "portrait_v2",
        "canvas": [CANVAS_W, CANVAS_H],
        "chroma_key_rgb": KEY.astype(int).tolist(),
        "chroma_thresholds": {"low": LOW, "high": HIGH},
        "target_head_height_px": TARGET_HEAD_HEIGHT,
        "target_head_center_px": list(TARGET_HEAD_CENTER),
        "head_center_invariant": "50% x / 31.7708% from top (256x384 x4)",
        "head_height_spread_px": round(max(heights) - min(heights), 3),
        "icon_crop_px_topleft": list(ICON_CROP_PX),
        "icon_crop_source": "AvatarIconCrop.NormalizedRect() values x4 (locked consts)",
        "images": results,
        "all_checks_passed": all_ok,
        "estimates_note": MEASURE_METHOD,
    }
    MANIFEST_PATH.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(f"head-height spread across portraits: "
          f"{manifest['head_height_spread_px']}px")
    print(f"manifest -> {MANIFEST_PATH.relative_to(REPO)}")
    return 0 if all_ok else 1


if __name__ == "__main__":
    sys.exit(main())
