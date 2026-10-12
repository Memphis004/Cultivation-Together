#!/usr/bin/env python3
"""Phase 0 step 2: normalise the base portrait to the 1024x1536 head-geometry canvas.

Uses keyed_rgba from Tools/art/portrait_postprocess.py for alpha only; the subject is
composited on a pure #FF00FF canvas at source resolution, then resized (Lanczos).
color_bleed / process() are deliberately NOT used.
"""
import json
import pathlib
import sys

import numpy as np
from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "Tools" / "art"))
import portrait_postprocess as pp  # noqa: E402

HERE = pathlib.Path(__file__).resolve().parent
SRC = HERE / "base_src.png"
OUT = HERE / "base_norm.png"
MEAS = HERE / "measurements.json"

# Visual estimates from the 2x grid (grid origin x300,y40, 25px lines). +-5 px.
HAIR_TOP, CHIN, FACE_CX = 77.0, 300.0, 530.0
TOL_PX = 5

src_img = Image.open(SRC)
report = {"src_size": list(src_img.size), "src_mode": src_img.mode}
src_rgb = np.array(src_img.convert("RGB")).astype(np.float64)
dist = np.sqrt(((src_rgb - pp.KEY) ** 2).sum(axis=2))
report["src_near_magenta_frac_lt60"] = round(float((dist < 60).mean()), 4)
report["src_bbox_fg_y_x"] = None

keyed = pp.keyed_rgba(SRC)  # alpha from chroma key
sw, sh = keyed.size
head_h = CHIN - HAIR_TOP
cy = (CHIN + HAIR_TOP) / 2.0
s = pp.TARGET_HEAD_HEIGHT / head_h
off_x = int(round(pp.TARGET_HEAD_CENTER[0] - FACE_CX * s))
off_y = int(round(pp.TARGET_HEAD_CENTER[1] - cy * s))

# Composite on pure magenta at source resolution, then resize.
bg = Image.new("RGBA", (sw, sh), (255, 0, 255, 255))
bg.alpha_composite(keyed)
rgb_on_magenta = bg.convert("RGB").resize((round(sw * s), round(sh * s)), Image.LANCZOS)
canvas = Image.new("RGB", (pp.CANVAS_W, pp.CANVAS_H), (255, 0, 255))
canvas.paste(rgb_on_magenta, (off_x, off_y))
canvas.save(OUT)

arr = np.array(canvas).astype(np.float64)
d2 = np.sqrt(((arr - pp.KEY) ** 2).sum(axis=2))
# foreground rows/cols in normalised canvas (distance > 60 from magenta)
fg = d2 > 60
rows = np.where(fg.any(1))[0]
cols = np.where(fg.any(0))[0]

report.update({
    "method": {
        "hair_top_src": HAIR_TOP, "chin_src": CHIN, "face_cx_src": FACE_CX,
        "measure_note": "visual estimate from 25px labelled grid at 2x; +-5 px; hairpin/flowers excluded from hair_top",
        "head_height_src": head_h, "scale": round(s, 6),
        "paste_offset_px": [off_x, off_y],
        "target_head_center": list(pp.TARGET_HEAD_CENTER),
        "target_head_height": pp.TARGET_HEAD_HEIGHT,
        "compose": "keyed alpha -> composite on pure #FF00FF at src res -> Lanczos -> paste on pure magenta canvas; no color_bleed",
    },
    "norm_size": list(canvas.size),
    "norm_mode_saved": "RGB (canvas is opaque magenta; keyed in Phase 0 step 5)",
    "norm_head_center_px": [round(FACE_CX * s + off_x, 2), round(cy * s + off_y, 2)],
    "norm_head_height_px": round(head_h * s, 2),
    "norm_chin_y_px": round(CHIN * s + off_y, 2),
    "norm_face_cx_px": round(FACE_CX * s + off_x, 2),
    "norm_fg_bbox_y": [int(rows.min()), int(rows.max())],
    "norm_fg_bbox_x": [int(cols.min()), int(cols.max())],
    "frame_bottom_src_y_px": round((pp.CANVAS_H - pp.TARGET_HEAD_CENTER[1]) / s + cy, 1),
    "src_fg_bbox_y": None,
    "tolerance_px": TOL_PX,
})
MEAS.write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report, indent=2))
