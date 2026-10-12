#!/usr/bin/env python3
"""Measure layers_trial outputs against base_norm.png (deterministic, numbers only).

For each raw output: size/mode, near-magenta visible px, alpha at top corners, keyed RGBA
(keyed_rgba + neutralize_canvas, no geometry), drift (dx,dy) via cross-correlation of the
luminance of a face box against base_norm within +-40px, chin y (centre-band skin run,
None when not found), eye-level face width, foreground_ratio and below_chin_px vs base.

Phase 0 pass/fail (only these decide the verdict):
  |dx|,|dy| <= 5 ; |chin_y - chin_y_base| <= 5 ; |face_width delta| <= 3 %.
mismatch_fraction and dark_frac_head_box are REPORTED ONLY (0.02 belongs to Phase 1).
A check whose value could not be measured is None; the verdict is UNMEASURABLE when no
check failed but at least one is None.
"""
import argparse
import json
import pathlib
import sys

import numpy as np
from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import portrait_postprocess as pp  # noqa: E402

LT = ROOT / "art" / "portraits_v2" / "layers_trial"
BASE = LT / "base_norm.png"
MEAS = LT / "measurements.json"

# Reference boxes on the 1024x1536 normalised canvas (from measurements.json geometry).
FACE_BOX = (430, 500, 600, 610)   # brows -> chin, x range around face
HEAD_BOX = (360, 360, 660, 620)   # hair top -> chin, used for dark-pixel fraction
DRIFT_R = 40
EYE_Y = 533
FG_DIST = 60                      # distance from #FF00FF above which a pixel is foreground

# Chin detection: centre band, scan down from a row known to be inside the face.
CHIN_X = (510, 551)
CHIN_SEARCH = (600, 660)          # start row must be skin; run must end before stop row
CHIN_THR = 0.5                    # fraction of skin pixels per row that counts as "face row"

# Phase 0 tolerances.
DRIFT_TOL_PX = 5
CHIN_TOL_PX = 5
FACE_WIDTH_TOL_PCT = 3.0


def lum(rgb):
    return 0.299 * rgb[..., 0] + 0.587 * rgb[..., 1] + 0.114 * rgb[..., 2]


def skin_mask(rgb):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    return (r > 150) & (g > 130) & (b > 110) & (np.abs(r - g) < 60)


def fg_mask(rgb):
    return np.sqrt(((rgb - pp.KEY) ** 2).sum(axis=2)) > FG_DIST


def shift_crop(arr, x0, y0, x1, y1, dx, dy):
    return arr[y0 + dy:y1 + dy, x0 + dx:x1 + dx]


def best_drift(ref, out, box):
    x0, y0, x1, y1 = box
    best = (-1e9, 0, 0)
    ref_c = ref[y0:y1, x0:x1]
    rc = ref_c - ref_c.mean()
    for dy in range(-DRIFT_R, DRIFT_R + 1):
        for dx in range(-DRIFT_R, DRIFT_R + 1):
            oc = shift_crop(out, x0, y0, x1, y1, dx, dy)
            if oc.shape != ref_c.shape:
                continue
            oc2 = oc - oc.mean()
            den = np.sqrt((rc * rc).sum() * (oc2 * oc2).sum())
            if den == 0:
                continue
            score = float((rc * oc2).sum() / den)
            if score > best[0]:
                best = (score, dx, dy)
    return {"dx": best[1], "dy": best[2], "ncc": round(best[0], 4)}


def chin_y(rgb):
    """Bottom row of the face in the centre band, or None.

    Starts at CHIN_SEARCH[0] (must be face skin) and walks down while the skin fraction
    of the band stays >= CHIN_THR. Returns None if the start row is not skin or if the run
    reaches CHIN_SEARCH[1] (no end found, e.g. the body/skin continues below the face).
    """
    frac = skin_mask(rgb)[:, CHIN_X[0]:CHIN_X[1]].mean(axis=1)
    start, stop = CHIN_SEARCH
    if frac[start] < CHIN_THR:
        return None
    y = start
    while y + 1 < stop and frac[y + 1] >= CHIN_THR:
        y += 1
    if y + 1 >= stop:
        return None
    return int(y)


def face_width_at_eye(rgb):
    row = rgb[EYE_Y]
    skin = skin_mask(row[None, ...])[0]
    xs = np.where(skin[380:700])[0] + 380
    if xs.size == 0:
        return None
    return int(xs.max() - xs.min())


def base_reference():
    base_rgb = np.array(Image.open(BASE).convert("RGB")).astype(np.float64)
    geo = json.loads(MEAS.read_text(encoding="utf-8"))
    chin_base = chin_y(base_rgb)
    chin_geo = geo["norm_chin_y_px"]
    return {
        "rgb": base_rgb,
        "lum": lum(base_rgb),
        "chin_y": chin_base,
        "chin_geo": chin_geo,
        "chin_diff_vs_geo": None if chin_base is None else round(chin_base - chin_geo, 2),
        "chin_within_tol_of_geo": None if chin_base is None else abs(chin_base - chin_geo) <= CHIN_TOL_PX,
        "face_width_eye": face_width_at_eye(base_rgb),
        "face_width_eye_geo": geo.get("norm_face_width_at_eye_level_px"),
        "fg_px": int(fg_mask(base_rgb).sum()),
        "below_chin_px": None if chin_base is None else int(fg_mask(base_rgb)[chin_base + 1:].sum()),
        "dark_frac_head_box": round(float((lum(base_rgb)[HEAD_BOX[1]:HEAD_BOX[3], HEAD_BOX[0]:HEAD_BOX[2]] < 50).mean()), 4),
    }


def verdict(checks):
    vals = list(checks.values())
    if any(v is False for v in vals):
        return "FAIL"
    if any(v is None for v in vals):
        return "UNMEASURABLE"
    return "PASS"


def measure(raw_path, seed, out_root, base):
    im = Image.open(raw_path)
    res = {"seed": seed, "raw": str(raw_path.relative_to(ROOT)).replace("\\", "/"),
           "size": list(im.size), "mode": im.mode}
    a = np.array(im.convert("RGBA")).astype(np.float64)
    rgb, al = a[..., :3], a[..., 3]
    d = np.sqrt(((rgb - pp.KEY) ** 2).sum(axis=2))
    res["alpha_top_left_corner"] = int(al[0, 0])
    res["alpha_top_right_corner"] = int(al[0, -1])
    res["near_magenta_visible_px_lt60"] = int(((d < 60) & (al > 0)).sum())

    keyed = pp.neutralize_canvas(pp.keyed_rgba(raw_path))
    keyed_path = out_root / "layers" / f"head_seed{seed}.png"
    keyed_path.parent.mkdir(exist_ok=True)
    keyed.save(keyed_path)
    res["keyed_png"] = str(keyed_path.relative_to(ROOT)).replace("\\", "/")
    res["keyed_size"] = list(keyed.size)
    res["keyed_mode"] = keyed.mode

    out_lum = lum(rgb)
    res["drift_face_box"] = best_drift(base["lum"], out_lum, FACE_BOX)
    chin_out = chin_y(rgb)
    res["chin_y_out"] = chin_out
    res["chin_y_base"] = base["chin_y"]
    fo = face_width_at_eye(rgb)
    res["face_width_eye_out"] = fo
    res["face_width_eye_base"] = base["face_width_eye"]
    fb = base["face_width_eye"]
    res["face_width_delta_pct"] = None if (fo is None or not fb) else round(100.0 * (fo - fb) / fb, 2)

    # Isolation metrics (report + diagnose "not isolated" vs "isolated").
    fg = fg_mask(rgb)
    res["foreground_px"] = int(fg.sum())
    res["foreground_ratio"] = round(float(fg.sum()) / base["fg_px"], 4)
    res["below_chin_px"] = None if chin_out is None else int(fg[chin_out + 1:].sum())

    # Reported only, not part of the verdict.
    x0, y0, x1, y1 = FACE_BOX
    diff = np.abs(base["lum"][y0:y1, x0:x1] - out_lum[y0:y1, x0:x1])
    res["mismatch_fraction_face_box_diff48"] = round(float((diff > 48).mean()), 4)
    x0, y0, x1, y1 = HEAD_BOX
    res["dark_frac_head_box_out"] = round(float((out_lum[y0:y1, x0:x1] < 50).mean()), 4)
    res["dark_frac_head_box_base"] = base["dark_frac_head_box"]

    checks = {
        "drift_le_5px": abs(res["drift_face_box"]["dx"]) <= DRIFT_TOL_PX and abs(res["drift_face_box"]["dy"]) <= DRIFT_TOL_PX,
        "chin_within_5px_of_base": None if (chin_out is None or base["chin_y"] is None)
        else abs(chin_out - base["chin_y"]) <= CHIN_TOL_PX,
        "face_width_within_3pct": None if res["face_width_delta_pct"] is None
        else abs(res["face_width_delta_pct"]) <= FACE_WIDTH_TOL_PCT,
    }
    res["phase0_checks"] = checks
    res["phase0_verdict"] = verdict(checks)
    return res


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seeds", nargs="+", type=int, default=[540101, 540102, 540103])
    ap.add_argument("--round-dir", default=str(LT), help="folder with generation_log.json; outputs go here")
    args = ap.parse_args()
    out_root = pathlib.Path(args.round_dir).resolve()
    log = json.loads((out_root / "generation_log.json").read_text(encoding="utf-8"))
    raw_by_seed = {j["seed"]: pathlib.Path(j["raw"]) for j in log["jobs"] if j.get("status") == "completed"}
    base = base_reference()
    results = [measure(raw_by_seed[s], s, out_root, base) for s in args.seeds]
    out = {
        "base": {
            "file": "base_norm.png",
            "chin_y_base_detector": base["chin_y"],
            "chin_y_norm_measurements_json": base["chin_geo"],
            "chin_base_diff_vs_measurements_px": base["chin_diff_vs_geo"],
            "chin_base_within_5px_of_measurements": base["chin_within_tol_of_geo"],
            "face_width_eye_base_detector": base["face_width_eye"],
            "face_width_eye_norm_measurements_json": base["face_width_eye_geo"],
            "foreground_px_base": base["fg_px"],
            "below_chin_px_base": base["below_chin_px"],
            "dark_frac_head_box_base": base["dark_frac_head_box"],
        },
        "boxes": {"face": FACE_BOX, "head": HEAD_BOX, "drift_range_px": DRIFT_R, "chin_x": CHIN_X,
                  "chin_search": CHIN_SEARCH, "fg_distance": FG_DIST},
        "tolerances": {"drift_px": DRIFT_TOL_PX, "chin_px": CHIN_TOL_PX, "face_width_pct": FACE_WIDTH_TOL_PCT},
        "results": results,
    }
    (out_root / "measure_results.json").write_text(json.dumps(out, indent=2), encoding="utf-8")
    print(json.dumps(out, indent=2))


if __name__ == "__main__":
    main()
