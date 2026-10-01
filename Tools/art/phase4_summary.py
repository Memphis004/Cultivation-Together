#!/usr/bin/env python3
"""Summarise the generated sprite kit: sizes, key-colour safety, and 9-slice borders.

Everything printed here is measured from the files on disk.

Usage
-----
    python tools/art/phase4_summary.py
"""

from __future__ import annotations

import glob
import json
import os
import pathlib
import sys

import numpy as np
from PIL import Image

REPO = pathlib.Path(__file__).resolve().parents[2]
MANIFEST = REPO / "art" / "disciple_list" / "manifest.json"
FINAL = REPO / "art" / "disciple_list" / "final"
LOG = REPO / "art" / "disciple_list" / "run_log.json"


def key_of(manifest: dict) -> np.ndarray:
    return np.array(manifest["chroma_key"]["rgb"], dtype=np.float64)


def side_bands(path: pathlib.Path, horizontal: bool) -> tuple[int, int]:
    """Width of the darker band at each end, measured on the middle scanlines."""
    a = np.array(Image.open(path).convert("RGBA"))
    h, w, _ = a.shape
    rgb = a[..., :3].astype(float)
    al = a[..., 3]
    lum = rgb.mean(2)
    left, right = [], []
    span = range(int(h * 0.40), int(h * 0.60)) if horizontal else range(
        int(w * 0.40), int(w * 0.60)
    )
    for i in span:
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
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    log = json.loads(LOG.read_text(encoding="utf-8"))
    key = key_of(manifest)

    print("=" * 112)
    print("FINAL SPRITE INVENTORY")
    print("=" * 112)
    hdr = ("file", "final", "target", "w off", "h off", "nearMag", "upscale", "bytes")
    print("%-30s%12s%12s%8s%7s%9s%8s%8s" % hdr)

    gaps = []
    for a in manifest["assets"]:
        ta = a["target_px"]
        for f in sorted(FINAL.glob(f"{a['id']}_seed*.png")):
            im = Image.open(f)
            arr = np.array(im.convert("RGBA"))
            d = np.sqrt(((arr[..., :3].astype(np.float64) - key) ** 2).sum(2))
            s = im.size
            dw = abs(s[0] - ta[0]) / ta[0] * 100
            dh = abs(s[1] - ta[1]) / ta[1] * 100
            up = s[0] > ta[0] or s[1] > ta[1]
            print(
                "%-30s%12s%12s%7.1f%%%6.1f%%%9d%8s%8d"
                % (
                    f.name,
                    f"{s[0]}x{s[1]}",
                    f"{ta[0]}x{ta[1]}",
                    dw,
                    dh,
                    int((d < 60).sum()),
                    up,
                    os.path.getsize(f),
                )
            )
            if max(dw, dh) > 5:
                gaps.append((a["id"], f.name, f"{s[0]}x{s[1]}", f"{ta[0]}x{ta[1]}", max(dw, dh)))

    print("\nSEEDS THAT MISS THE TARGET BOX BY MORE THAN 5% (avoid these):")
    if gaps:
        for aid, name, got, want, off in gaps:
            print("   %-16s %-30s %-11s vs %-11s off %.0f%%" % (aid, name, got, want, off))
    else:
        print("   none")

    print("\n" + "=" * 112)
    print("9-SLICE BORDER PROPOSAL (horizontal slice; left/right dark band per seed)")
    for a in manifest["assets"]:
        if not a["nine_slice"]:
            continue
        files = sorted(FINAL.glob(f"{a['id']}_seed*.png"))
        if not files:
            continue
        bands = [side_bands(f, a.get("nine_slice_axis", "horizontal") == "horizontal") for f in files]
        L = int(np.median([b[0] for b in bands]))
        R = int(np.median([b[1] for b in bands]))
        print("   %-16s per-seed %s" % (a["id"], bands))
        print(
            "   %-16s proposed spriteBorder = (left=%d, bottom=0, right=%d, top=0)"
            % ("", L, R)
        )

    print("\n" + "=" * 112)
    print("ACTUAL GENERATED CANVAS PER ASSET")
    print("   the resolution widget is a PIXEL BUDGET, so the canvas keeps each")
    print("   guide's aspect and is snapped to /32 — these sizes are NOT all 1024^2")
    for a in manifest["assets"]:
        sizes = {
            tuple(e["actual_png_size"])
            for e in log
            if e["asset_id"] == a["id"]
            and e.get("actual_png_size")
            and e.get("prompt") == a["prompt"]
        }
        for s in sorted(sizes):
            print(
                "   %-17s %4dx%-5d = %.3f MP   (manifest predicted %s)"
                % (a["id"], s[0], s[1], s[0] * s[1] / 1e6, a.get("gen_size_expected"))
            )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
