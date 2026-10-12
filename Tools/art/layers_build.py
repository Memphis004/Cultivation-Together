#!/usr/bin/env python3
"""layers_trial: register base_bald to base_norm (G1/G2), pixel-cut head/body/hair,
run gates + QA images. Writes build_report.json in layers_trial/.

Usage: python Tools/art/layers_build.py [--seed 540601] [--scale S] [--tx X] [--ty Y]
"""
from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import sys

import numpy as np
import cv2
from PIL import Image
from scipy import ndimage

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import portrait_postprocess as pp  # noqa: E402

LT = ROOT / "art" / "portraits_v2" / "layers_trial"
N_PATH = LT / "base_norm.png"
RAW_SEL = pathlib.Path("C:/UnityProjects/Cultivation-Together/art/portraits_v2/layers_trial/round_hairA/raw")
REPORT = LT / "build_report.json"
FINAL = LT / "final"
QA = LT / "qa"

# face box (brows->chin) from layers_trial_measure.py, NOT modified here.
FACE_BOX = (430, 500, 600, 610)
HEAD_BOX = (360, 360, 660, 620)
DRIFT_TOL = 5
SCALE_RANGE = (0.85, 1.20)
NCC_MIN = 0.80
PHASE_CORR_MAX_PX = 2
G2_MAX = 0.03
SOFT_SCALP_EXPOSED_MAX = 150


def lum(rgb):
    return 0.299 * rgb[..., 0] + 0.587 * rgb[..., 1] + 0.114 * rgb[..., 2]


def blur(a, s=2.0):
    return ndimage.gaussian_filter(a, s)


def gas(x, lo, hi):
    return np.clip((x - lo) / (hi - lo), 0.0, 1.0)


def keyed(path):
    """keyed_rgba WITHOUT neutralize-canvas geometry, alpha float [0,1]."""
    src = Image.open(path).convert("RGB")
    rgb = np.array(src).astype(np.float64)
    alpha = pp.chroma_key(rgb, pp.KEY)
    rgb = pp.despill(rgb, alpha)
    rgb = pp.color_bleed(rgb, alpha)
    return rgb, alpha


def sha(p):
    return hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()


def save_img(img, p):
    p = pathlib.Path(p)
    p.parent.mkdir(parents=True, exist_ok=True)
    img.save(p)


# ---------------------------------------------------------------- registration
def register(rgbB, alB, seed, init, rot_max=3.0):
    """Nelder-Mead on blurred-lum SSD in the face box. Returns (transform, B')."""
    N_lum_blur = blur(lum(np.array(Image.open(N_PATH).convert("RGB")).astype(np.float64)))

    fx0, fy0, fx1, fy1 = FACE_BOX
    fcx = 512.4  # measurements.json norm_face_cx_px
    chin_ref = 611.0
    face_w_ref = 139.0

    def warped(scale, tx, ty, rot):
        h, w = rgbB.shape[:2]
        M = cv2.getRotationMatrix2D((fcx, fy0), rot, scale)
        M[:, 2] += [tx, ty]
        out_r = cv2.warpAffine(rgbB, M, (w, h), flags=cv2.INTER_LANCZOS4,
                               borderMode=cv2.BORDER_CONSTANT, borderValue=(255, 0, 255))
        out_a = cv2.warpAffine(alB, M, (w, h), flags=cv2.INTER_LANCZOS4,
                               borderMode=cv2.BORDER_CONSTANT, borderValue=0.0)
        return out_r, out_a

    def loss(p):
        s, tx, ty, rot = p
        if not (SCALE_RANGE[0] <= s <= SCALE_RANGE[1] or abs(rot) > rot_max):
            return 1e12
        r, _ = warped(s, tx, ty, rot)
        box = blur(lum(r))[fy0:fy1, fx0:fx1]
        ref = N_lum_blur[fy0:fy1, fx0:fx1]
        return float(((box - ref) ** 2).mean())

    from scipy.optimize import minimize
    res = minimize(loss, x0=list(init), method="Nelder-Mead",
                   options={"xatol": 0.01, "fatol": 0.05, "maxiter": 400})
    s, tx, ty, rot = res.x
    rB, aB = warped(s, tx, ty, rot)
    # warp border (magenta/black) pollutes feathered edge RGB -> kill the halo:
    # bleed nearest-opaque colour into edge/transparent px after the warp.
    rB = pp.despill(rB, aB)
    rB = pp.color_bleed(rB, aB)
    return {"scale": float(s), "tx": float(tx), "ty": float(ty), "rot_deg": float(rot),
            "loss": float(res.fun)}, rB, aB


def smooth_mag(rgb):
    """Distance >= HIGH => subject (1); LOW..HIGH feathered."""
    d = np.sqrt(((rgb.astype(np.float64) - pp.KEY) ** 2).sum(axis=2))
    return np.clip((d - pp.LOW) / (pp.HIGH - pp.LOW), 0.0, 1.0)


def phase_corr(ref, mov, box):
    """cv2 phaseCorrelate on blurred luminance face box."""
    x0, y0, x1, y1 = box
    a = ref[y0:y1, x0:x1]
    b = mov[y0:y1, x0:x1]
    shift, resp = cv2.phaseCorrelate(np.float32(a), np.float32(b))
    return {"dx": float(shift[0]), "dy": float(shift[1]), "response": float(resp)}


# ---------------------------------------------------------------- gates
def gateG1(N_lum_blur, B_reg, a_reg, transform, face_w_ref=139.0):
    box_lum = blur(lum(B_reg))
    # NCC in face box
    fb = FACE_BOX
    a_ = N_lum_blur[fb[1]:fb[3], fb[0]:fb[2]].ravel()
    b_ = box_lum[fb[1]:fb[3], fb[0]:fb[2]].ravel()
    ncc = float((a_ * b_).sum() / (np.linalg.norm(a_) * np.linalg.norm(b_) + 1e-9))
    pc = phase_corr(N_lum_blur, box_lum, FACE_BOX)
    s = transform["scale"]
    chinB = detect_chin(B_reg)
    fw = face_width_eye(B_reg, a_reg)
    checks = {
        "ncc>=0.80": ncc >= NCC_MIN,
        "phase_corr<=2px": max(abs(pc["dx"]), abs(pc["dy"])) <= PHASE_CORR_MAX_PX,
        "scale_in_range": SCALE_RANGE[0] <= s <= SCALE_RANGE[1],
        "chinB_near_611": None if chinB is None else abs(chinB - 611.0) <= 5,
        "face_width_139_pct4": None if fw is None else abs(100.0 * (fw - face_w_ref) / face_w_ref) <= 4.0,
    }
    return {"ncc": round(ncc, 4), "phase": {k: round(v, 3) for k, v in pc.items()},
            "scale": round(s, 4), "chin": chinB, "face_width_eye": fw,
            "checks": checks, "pass": all(checks.values())}


def detect_chin(rgb):
    """Same band CHIN_X/CHIN_SEARCH convention as layers_trial_measure.py."""
    sk = skin_mask(rgb)
    frac = sk[:, 510:551].mean(axis=1)
    start, stop = 600, 660
    if frac[start] < 0.5:
        return None
    y = start
    while y + 1 < stop and frac[y + 1] >= 0.5:
        y += 1
    if y + 1 >= stop:
        return None
    return int(y)


def skin_mask(rgb):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    return (r > 150) & (g > 130) & (b > 110) & (np.abs(r - g) < 60)


def face_width_eye(rgb, alpha):
    """Face width in the registered image: skin at EYE_Y=533."""
    if alpha[533].max() < 0.5:
        return None
    sk = skin_mask(rgb)
    xs = np.where(sk[533])[0]
    if xs.size == 0:
        return None
    return int(xs.max() - xs.min())


def scalp_dark_frac(rgb, alpha, face_box):
    fy0, fx0, fy1, fx1 = face_box[1], face_box[0], face_box[3], face_box[2]
    face_cx = (fx0 + fx1) / 2.0
    half_w = (fx1 - fx0) / 2.0 + 10
    zone = np.zeros(alpha.shape, bool)
    zone[:max(0, fy0 - 5), :] = True
    zone &= np.abs(np.arange(alpha.shape[1])[None, :] - face_cx) <= half_w
    zone &= alpha > 0
    dark = lum(rgb) < 70
    return float((zone & dark).sum() / max(1, zone.sum()))


# ---------------------------------------------------------------- layer cut
def cut_layers(B_rgb, B_al, N_rgb, N_al, y_cut, hair_params):
    H, W = B_al.shape
    rows = np.arange(H)[:, None]
    # head: B alpha rows <= y_cut
    head_al = np.where(rows <= y_cut, B_al, 0.0)
    # body: B alpha rows >= y_cut-25
    body_al = np.where(rows >= y_cut - 25, B_al, 0.0)

    # hair from N — darker-than-B pixels local to N (size/perimeter diff),
    # not a global smoothstep (N body cloth is also black and would flood the cut).
    lumN = lum(N_rgb)
    lumB = lum(B_rgb)
    b_opaque = B_al > 0.5
    lumB_safe = np.where(b_opaque, lumB, 255.0)
    darker_local = np.clip((lumB_safe - lumN - hair_params["delta"])
                           / hair_params["range"], 0, 1)
    raw = darker_local * N_al
    # hair zone (spec F2): x face_cx±200, y hair_top-30 .. chin+120 — long strands
    # below chin+120 are cut (known defect: "ตัดปอยยาวทิ้ง")
    zone = np.zeros_like(raw, bool)
    fy_top = 337   # hair_top(367) - 30
    fy_bot = 731   # chin(611) + 120
    zone[fy_top:fy_bot, 312:712] = True
    raw[~zone] = 0

    fx0, fy0, fx1, fy1 = FACE_BOX
    keepout = np.zeros_like(raw, bool)
    kx0 = int(fx0 + 0.12 * (fx1 - fx0)); kx1 = int(fx1 - 0.12 * (fx1 - fx0))
    ky0 = int(fy0 + 0.12 * (fy1 - fy0)); ky1 = int(fy1 - 0.12 * (fy1 - fy0))
    keepout[ky0:ky1, kx0:kx1] = True
    raw[keepout] = 0

    thr = hair_params["threshold"] / 255.0  # arg is 0..255 luminance-delta, raw is [0,1]
    m = raw > thr
    # keep components touching main hair mass (or area >=80 after dilate3)
    main = m.copy()
    main[ky0:ky1, :] = False
    lab, n = ndimage.label(m)
    if n:
        # main mass = largest component intersecting the head zone (y<fy1)
        sizes = ndimage.sum(m, lab, range(1, n + 1))
        head_zone = lab.copy().astype(bool)
        head_zone[ky1:, :] = False
        best, bestsize = 0, 0
        for i in range(1, n + 1):
            sz = int((lab[head_zone & (lab == i)] > 0).sum())
            if sz > bestsize:
                best, bestsize = i, sz
        keep = np.zeros_like(m)
        if best:
            keep |= lab[best] if False else (lab == best)
        # dilate main mass by 3, any comp with area>=80 within dilated zone kept
        dil = ndimage.binary_dilation(keep, ndimage.generate_binary_structure(2, 2), iterations=3)
        for i in range(1, n + 1):
            comp = lab == i
            sz = int(comp.sum())
            if sz >= 80 and (comp & dil).any():
                keep |= comp
        m = keep

    # fill holes (pin/flower/highlights) radius ~10 in hair zone
    if hair_params.get("fill_holes", True):
        filled = ndimage.binary_fill_holes(m)
        m = filled

    # close small gaps
    m = ndimage.binary_closing(m, np.ones((hair_params["close"], hair_params["close"])))

    # soft alpha = raw where mask, 0 elsewhere
    hair_al = np.where(m, raw, 0.0)
    hair_rgb = N_rgb  # colour from N

    return head_al, body_al, hair_al, hair_rgb, keepout


def assemble(rgb, alpha, colour, neutralize=True):
    rgba = np.dstack([np.clip(colour, 0, 255), alpha * 255.0]).astype(np.uint8)
    img = Image.fromarray(rgba, "RGBA")
    if neutralize:
        img = pp.neutralize_canvas(img)
    return img


def layer_checks(img, name, report, keepout=None, y_cut=None, which=None):
    arr = np.array(img.convert("RGBA")).astype(np.float64)
    rgb, al = arr[..., :3], arr[..., 3]
    d = np.sqrt(((rgb - pp.KEY) ** 2).sum(axis=2))
    vis_near_mag = int(((d < pp.LOW) & (al >= 64)).sum())
    corners = [int(al[0, 0]), int(al[0, -1]), int(al[-1, 0]), int(al[-1, -1])]
    ok_corners = all(c == 0 for c in corners)
    size_mode = img.size == (1024, 1536) and img.mode == "RGBA"
    r = {"size_mode": size_mode, "corners_zero": ok_corners, "near_magenta_visible": vis_near_mag}
    if which == "hair":
        r["alpha_in_keepout"] = int(((al > 0) & keepout).sum())
    if which == "head":
        r["alpha_below_ycut"] = int(((al > 0) & (np.arange(al.shape[0])[:, None] > y_cut)).sum())
    if which == "body":
        r["alpha_above_cut"] = int(((al > 0) & (np.arange(al.shape[0])[:, None] < y_cut - 25)).count if False else
                                   ((al > 0) & (np.arange(al.shape[0])[:, None] < y_cut - 25)).sum())
    r["pass_basic"] = size_mode and ok_corners and vis_near_mag == 0
    report[name] = r
    return r


# ---------------------------------------------------------------- QA images
def qa_images(head, body, hair, composite, N_rgb, outdir):
    outdir.mkdir(parents=True, exist_ok=True)
    mag = Image.new("RGB", composite.size, (255, 0, 255))
    mag.paste(composite, (0, 0), composite)
    save_img(mag.convert("RGB"), outdir / "composite_on_magenta.jpg")
    grey = Image.new("RGB", composite.size, (128, 128, 128))
    grey.paste(composite, (0, 0), composite)
    save_img(grey.convert("RGB"), outdir / "composite_on_grey.jpg")
    side = Image.new("RGB", (1024 * 2, 1536), (30, 30, 30))
    side.paste(Image.fromarray(np.clip(N_rgb, 0, 255).astype(np.uint8)), (0, 0))
    side.paste(mag, (1024, 0))
    save_img(side.convert("RGB").resize((768, 576)), outdir / "N_vs_composite.jpg")

    strip = Image.new("RGB", (1024 * 3, 1536), (128, 128, 128))
    for i, l in enumerate([head, body, hair]):
        bg = Image.new("RGB", (1024, 1536), (128, 128, 128))
        bg.paste(l, (0, 0), l)
        strip.paste(bg, (1024 * i, 0))
    save_img(strip.convert("RGB").resize((1152, 576)), outdir / "layers_strip.jpg")

    # head-neck crop at 3x: chin area (fixed band covering chin+neck seam)
    box = (380, 560, 660, 720)
    crop = mag.crop(box).resize(((box[2] - box[0]) * 3, (box[3] - box[1]) * 3), Image.LANCZOS)
    save_img(crop, outdir / "head_neck_crop_3x.jpg")

    ov = Image.fromarray(np.clip(N_rgb, 0, 255).astype(np.uint8)).convert("RGBA")
    hair_al = np.array(hair)[..., 3]
    red = np.zeros_like(np.array(ov), dtype=np.uint8)
    red[..., 0] = 255
    red[..., 3] = (hair_al > 0).astype(np.uint8) * 128
    ov2 = Image.alpha_composite(ov, Image.fromarray(red, "RGBA"))
    save_img(ov2.convert("RGB").resize((768, 1152)), outdir / "hair_mask_overlay.jpg")


# ---------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seed", type=int, default=540602)
    ap.add_argument("--scale", type=float, default=None)
    ap.add_argument("--tx", type=float, default=None)
    ap.add_argument("--ty", type=float, default=None)
    ap.add_argument("--y-cut", type=int, default=None, help="override chin+20")
    ap.add_argument("--color-threshold", type=float, default=128.0)
    ap.add_argument("--max-y-tries", type=int, default=3)
    ap.best = None
    args = ap.parse_args()

    raw = RAW_SEL / str(args.seed)
    cands = sorted(raw.glob("*_000.png"))
    if not cands:
        print(f"FATAL no raw for seed {args.seed}"); return 1
    src = cands[0]

    report = {"seed": args.seed, "source_raw": str(src), "source_sha256": sha(src)}
    N_rgb_f = np.array(Image.open(N_PATH).convert("RGB")).astype(np.float64)
    N_al, _ = keyed(N_PATH)  # keyed response of N (for hair extraction)
    N_al = N_al[1] if isinstance(N_al, tuple) else N_al

    print(f"[1/6] keying {src}")
    rgbB, alB = keyed(src)
    rgbB_init, alB_init = keyed(src)

    print("[2/6] ECC/NM registration")
    # initial transform from drift + scale from face width ratio
    chin_b0 = detect_chin(rgbB)
    fw_b0 = face_width_eye(rgbB, alB)
    s0 = args.scale or 139.0 / max(1, fw_b0 or 126)  # widen B to N face width
    # ty from chin: target 611; B chin is chin_b0
    ty0 = args.ty if args.ty is not None else (611.0 - (chin_b0 or 601))
    tx0 = args.tx if args.tx is not None else 0.0
    print(f"      init s={s0:.4f} tx={tx0} ty={ty0}")
    transform, B_rgb, B_al = register(rgbB, alB, args.seed, init=[s0, tx0, ty0, 0.0])
    report["transform"] = transform
    print(f"      transform {transform}")

    N_lum_blur = blur(lum(N_rgb_f))
    B_reg_lum = blur(lum(B_rgb))
    g1 = gateG1(N_lum_blur, B_rgb, B_al, transform)
    report["G1"] = g1
    print(f"      G1 {g1['checks']} pass={g1['pass']}")

    # warp B into registered canvas
    Image.fromarray(np.dstack([np.clip(B_rgb, 0, 255), B_al * 255]).astype(np.uint8), "RGBA").save(LT / "base_bald_reg.png")

    g2 = scalp_dark_frac(B_rgb, B_al, FACE_BOX)
    g2 >= G2_MAX and report.setdefault("warnings", []).append("G2 above threshold")
    report["G2"] = {"dark_frac_scalp": round(g2, 4), "pass": g2 <= G2_MAX}
    print(f"      G2 dark_frac={g2:.4f} pass={g2 <= G2_MAX}")

    N_al_hair = keyed(N_PATH)
    N_al_hair = N_al_hair[1] if isinstance(N_al_hair, tuple) else N_al_hair

    print("[3/6] cutting layers")
    chin = detect_chin(B_rgb) or 611
    y_cut = args.y_cut or chin + 20
    for attempt in range(3):
        head_al, body_al, hair_al, hair_rgb, keepout = cut_layers(
            B_rgb, B_al, N_rgb_f, N_al_hair, y_cut, {"delta": 25.0, "range": 60.0, "threshold": args.color_threshold, "close": 9, "fill_holes": True})
        head = assemble(B_rgb, head_al, B_rgb)
        body = assemble(B_rgb, body_al, B_rgb)
        hair = assemble(hair_rgb, hair_al, hair_rgb)
        print(f"      debug: head_px={int((head_al>0).sum())} body_px={int((body_al>0).sum())} hair_px={int((hair_al>0).sum())}")
        layer_checks(head, "head", report, y_cut=y_cut)
        layer_checks(body, "body", report, y_cut=y_cut)
        layer_checks(hair, "hair", report, keepout=keepout)
        # G3: keepout alpha and floating components
        hair_idx = (hair_al > 0)
        G3_keepout = int((hair_idx & keepout).sum())
        lab, n = ndimage.label(hair_idx)
        floating = 0
        for i in range(1, n + 1):
            comp = lab == i
            if 60 <= int(comp.sum()):
                if not (comp & ndimage.binary_dilation(hair_idx & (lab == 0) - (lab == i), iterations=0)).any():
                    pass
        report["G3"] = {"alpha_in_keepout": G3_keepout, "hair_components": n,
                        "detect_chin": chin, "y_cut": int(y_cut),
                        "pass_keepout": G3_keepout == 0}
        print(f"      y_cut={y_cut} keepout_alpha={G3_keepout}")
        if G3_keepout == 0:
            break
        dy = 8 if attempt == 0 else 12
        y_cut += dy
        report["warnings"] = report.get("warnings", []) + [f"y_cut adjusted to {y_cut} (attempt {attempt})"]

    # composite: hair over B, B hard under (hair wins where its alpha high, B under else)
    comp_al = np.clip(B_al + hair_al - B_al * hair_al, 0, 1)
    hair_hot = (hair_al >= 0.5)
    b_hot = (B_al > 0.02) & ~hair_hot
    comp_rgb = np.where(hair_hot[..., None], hair_rgb,
                        np.where(b_hot[..., None], B_rgb, hair_rgb))
    comp = assemble(comp_rgb, comp_al, comp_rgb)
    report["G5"] = {"y_cut": int(y_cut),
                    "pass": int(((np.array(body)[..., 3] > 0) & (np.arange(1536)[:, None] < y_cut - 25)[:, None]).sum()) == 0}

    qa_images(head, body, hair, comp, N_rgb_f, QA)
    save_img(head, FINAL / "head.png"); save_img(body, FINAL / "body.png"); save_img(hair, FINAL / "hair.png")
    transform_path = LT / "transform.json"
    transform_path.write_text(json.dumps({"transform": transform}, indent=2), encoding="utf-8")

    report["sha256_final"] = {k: sha(FINAL / f"{k}.png") for k in ["head", "body", "hair"]}
    REPORT.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("[4/6] gates+QA done ->", QA)
    print("[6] report ->", REPORT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
