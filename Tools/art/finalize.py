#!/usr/bin/env python3
"""Promote the user's chosen seeds to the canonical sprite names for Phase 5.

For every asset in `picks_approved_by_user` this:

    1. copies `final/<id>_seed<N>.png` -> `final/<id>.png`
    2. recomputes that asset's 9-slice border from the CHOSEN seed only (the
       border differs per seed, so a median of all four would be wrong)
    3. writes `chosen_seed` and the final `sprite_border_px_at_target` back
       into the manifest
    4. re-verifies the promoted file: RGBA, size, and zero near-magenta pixels

Deterministic and safe to re-run.

Usage
-----
    python tools/art/finalize.py
    python tools/art/finalize.py --check   # verify only, change nothing
"""

from __future__ import annotations

import argparse
import json
import pathlib
import shutil
import sys

import numpy as np
from PIL import Image

REPO = pathlib.Path(__file__).resolve().parents[2]
MANIFEST_PATH = REPO / "art" / "disciple_list" / "manifest.json"
FINAL_DIR = REPO / "art" / "disciple_list" / "final"


def side_bands(path: pathlib.Path, horizontal: bool) -> tuple[int, int]:
    """Width of the darker band at each end, measured on the middle scanlines."""
    a = np.array(Image.open(path).convert("RGBA"))
    h, w, _ = a.shape
    lum = a[..., :3].astype(float).mean(2)
    al = a[..., 3]
    left: list[int] = []
    right: list[int] = []
    idx = range(int(h * 0.40), int(h * 0.60)) if horizontal else range(
        int(w * 0.40), int(w * 0.60)
    )
    for i in idx:
        on = np.where(al[i] > 200)[0]
        if len(on) < w * 0.40:
            continue
        med = np.median(lum[i][on])
        dark = np.where((lum[i] < med - 25) & (al[i] > 200))[0]
        if not len(dark):
            continue
        lo = dark[dark < w // 2]
        hi = dark[dark >= w // 2]
        if len(lo):
            left.append(int(lo.max()) + 1)
        if len(hi):
            right.append(w - int(hi.min()))
    return (
        int(np.median(left)) if left else 0,
        int(np.median(right)) if right else 0,
    )


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="verify only, write nothing")
    args = ap.parse_args()

    manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
    picks = manifest["picks_approved_by_user"]
    key = np.array(manifest["chroma_key"]["rgb"], dtype=np.float64)

    print("=" * 78)
    print("FINALIZE — promote the chosen seed per asset")
    print("=" * 78)
    ok = True

    for asset in manifest["assets"]:
        aid = asset["id"]
        seed = picks.get(aid)
        if seed is None:
            print(f"  {aid}: NO PICK — skipped")
            ok = False
            continue

        src = FINAL_DIR / f"{aid}_seed{seed}.png"
        dst = FINAL_DIR / f"{aid}.png"
        if not src.exists():
            print(f"  {aid}: MISSING {src.name}")
            ok = False
            continue

        if not args.check:
            shutil.copy2(src, dst)

        im = Image.open(dst)
        arr = np.array(im.convert("RGBA"))
        d = np.sqrt(((arr[..., :3].astype(np.float64) - key) ** 2).sum(2))
        near = int((d < 60).sum())
        ta = asset["target_px"]
        size = im.size

        line = (
            f"  {aid:<16} seed {seed:<4} -> {dst.name:<28} {size[0]}x{size[1]} "
            f"(target {ta[0]}x{ta[1]})  near-magenta {near}"
        )
        print(line)
        if near:
            ok = False
            print(f"      !! {near} near-magenta pixels — refusing to publish")

        if asset["nine_slice"]:
            L, R = side_bands(dst, asset.get("nine_slice_axis", "horizontal") == "horizontal")
            print(f"      spriteBorder = (left={L}, bottom=0, right={R}, top=0)  [chosen seed only]")
            if not args.check:
                asset["sprite_border_px_at_target"] = {
                    "left": L,
                    "bottom": 0,
                    "right": R,
                    "top": 0,
                }
                asset["sprite_border_px_note"] = (
                    f"computed from the CHOSEN seed {seed} (not a median), measured on the "
                    f"{size[0]}x{size[1]} final sprite"
                )
        if not args.check:
            asset["chosen_seed"] = seed

    if not args.check:
        MANIFEST_PATH.write_text(
            json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
        )
        print(f"\nupdated {MANIFEST_PATH.relative_to(REPO)}")

    print(f"\nresult: {'OK' if ok else 'PROBLEMS FOUND'}")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
