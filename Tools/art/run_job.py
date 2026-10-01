#!/usr/bin/env python3
"""Run Qwen-Image-2.1 jobs for the DiscipleList window kit, one job at a time.

What it does per asset (see the task's Phase 1.3):

    1. patch a THROWAWAY copy of `comfy/qwen21_ui_asset.json`
       (guide filename, prompt, seed, steps, resolution, output prefix)
    2. validate that copy with `comfy workflow validate`
    3. copy the guide silhouette into the ComfyUI input dir as
       `ui_<asset>_guide.png`
    4. run it and WAIT, one job at a time
    5. move the new outputs out of the ComfyUI output dir into
       `art/disciple_list/raw/<asset>/seed<N>.png`
    6. append a record (prompt, seed, steps, resolution, elapsed) to
       `art/disciple_list/run_log.json`

Nothing in ComfyUI's own folders is modified or deleted. Only files named
with the `ui_` prefix are written there, and they are copies of our own
guides/outputs.

Usage
-----
    python tools/art/run_job.py scroll_paper                 # all 4 manifest seeds
    python tools/art/run_job.py scroll_paper --seeds 101,202 # a subset
    python tools/art/run_job.py scroll_paper --steps 40      # the re-run at higher quality
    python tools/art/run_job.py scroll_paper --dry-run       # patch + validate + stage, run nothing
"""

from __future__ import annotations

import argparse
import json
import pathlib
import shutil
import subprocess
import sys
import time
import urllib.request

REPO = pathlib.Path(__file__).resolve().parents[2]
MANIFEST_PATH = REPO / "art" / "disciple_list" / "manifest.json"
BASELINE_WORKFLOW = REPO / "comfy" / "qwen21_ui_asset.json"
TMP_DIR = REPO / "art" / "disciple_list" / "_tmp"
GUIDES_DIR = REPO / "art" / "disciple_list" / "guides"
RAW_DIR = REPO / "art" / "disciple_list" / "raw"
LOG_PATH = REPO / "art" / "disciple_list" / "run_log.json"

COMFY_BASE = "http://127.0.0.1:8188"

# Fallbacks; the real values are read from the running server's argv at startup.
FALLBACK_INPUT = pathlib.Path(
    r"C:\Users\memph\AppData\Local\Comfy-Desktop\ComfyUI-Shared\input"
)
FALLBACK_OUTPUT = pathlib.Path(
    r"C:\Users\memph\AppData\Local\Comfy-Desktop\ComfyUI-Shared\output"
)

# Node ids in comfy/qwen21_ui_asset.json (verified with `comfy workflow slots`).
NODE_GUIDE = 8
NODE_ENCODE = 5
NODE_SAMPLER = 6
NODE_SAVE = 10


def server_dirs() -> tuple[pathlib.Path, pathlib.Path]:
    """Ask the running ComfyUI where its input/output dirs actually are."""
    try:
        with urllib.request.urlopen(f"{COMFY_BASE}/system_stats", timeout=10) as r:
            argv = json.load(r)["system"]["argv"]
    except Exception as exc:  # noqa: BLE001 - fall back rather than die
        print(f"  ! could not read /system_stats ({exc}); using fallback dirs")
        return FALLBACK_INPUT, FALLBACK_OUTPUT

    inp = out = None
    for i, tok in enumerate(argv):
        if tok == "--input-directory" and i + 1 < len(argv):
            inp = pathlib.Path(argv[i + 1])
        elif tok == "--output-directory" and i + 1 < len(argv):
            out = pathlib.Path(argv[i + 1])
    return inp or FALLBACK_INPUT, out or FALLBACK_OUTPUT


def load_manifest() -> dict:
    return json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))


def patch_workflow(
    baseline: dict, asset: dict, guide_name: str, seed: int, steps: int, resolution: int
) -> dict:
    """Return a patched copy. Only these nodes are touched; everything else is verbatim."""
    graph = json.loads(json.dumps(baseline))  # deep copy
    nodes = {n["id"]: n for n in graph["nodes"]}

    n8 = nodes[NODE_GUIDE]
    n8["widgets_values"][0] = guide_name
    n8["widgets_values_named"]["image"] = guide_name

    n5 = nodes[NODE_ENCODE]
    n5["widgets_values"][0] = asset["prompt"]
    n5["widgets_values"][2] = resolution
    n5["widgets_values_named"]["prompt"] = asset["prompt"]
    n5["widgets_values_named"]["resolution"] = resolution

    n6 = nodes[NODE_SAMPLER]
    n6["widgets_values"] = [seed, "fixed", steps, 1, "euler", "simple", 1]
    n6["widgets_values_named"] = {
        "seed": seed,
        "control_after_generate": "fixed",
        "steps": steps,
        "cfg": 1,
        "sampler_name": "euler",
        "scheduler": "simple",
        "denoise": 1,
    }

    n10 = nodes[NODE_SAVE]
    n10["widgets_values"][0] = f"ui_{asset['id']}_"
    n10["widgets_values_named"]["filename_prefix"] = f"ui_{asset['id']}_"

    return graph


def run_validate(workflow_path: pathlib.Path) -> bool:
    proc = subprocess.run(
        ["comfy", "workflow", "validate", "--workflow", str(workflow_path)],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    raw = proc.stdout.strip()
    try:
        env = json.loads(raw)
    except json.JSONDecodeError:
        print(f"  ! validate returned non-JSON:\n{raw[:800]}")
        return False
    data = env.get("data") or {}
    ok = bool(data.get("valid"))
    print(
        f"  validate: valid={ok} errors={data.get('error_count')} "
        f"warnings={data.get('warning_count')} "
        f"spends_credits={data.get('spends_credits')} "
        f"converted_from_ui={data.get('converted_from_ui')}"
    )
    for e in data.get("errors") or []:
        print(f"    ERROR {json.dumps(e, ensure_ascii=False)[:300]}")
    for w in data.get("warnings") or []:
        print(f"    WARN  {json.dumps(w, ensure_ascii=False)[:300]}")
    return ok


def run_one(
    workflow_path: pathlib.Path, out_dir: pathlib.Path, timeout_s: int
) -> tuple[bool, str]:
    """Run one job to completion. Returns (ok, stderr-ish text)."""
    before = {p.name for p in out_dir.glob("*.png")}
    proc = subprocess.run(
        [
            "comfy",
            "run",
            "--workflow",
            str(workflow_path),
            "--wait",
            "--where",
            "local",
            "--no-notify",
        ],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout_s,
    )
    after = {p.name for p in out_dir.glob("*.png")}
    new = sorted(after - before)
    if proc.returncode != 0:
        return False, (proc.stdout or "")[-1500:] + "\n" + (proc.stderr or "")[-1500:]
    if not new:
        return False, "run reported success but produced no new PNG in the output dir"
    # stash the new names on the module so the caller can collect them
    run_one.last_new = new  # type: ignore[attr-defined]
    return True, ""


def append_log(record: dict) -> None:
    log = []
    if LOG_PATH.exists():
        try:
            log = json.loads(LOG_PATH.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            log = []
    log.append(record)
    LOG_PATH.write_text(
        json.dumps(log, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("asset_id")
    ap.add_argument(
        "--seeds",
        default=None,
        help="comma-separated seeds; default = the 4 seeds in the manifest",
    )
    ap.add_argument("--steps", type=int, default=25)
    ap.add_argument("--resolution", type=int, default=None)
    ap.add_argument("--timeout", type=int, default=1800, help="per-job wall clock (s)")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    manifest = load_manifest()
    gen = manifest["generation"]
    try:
        asset = next(a for a in manifest["assets"] if a["id"] == args.asset_id)
    except StopIteration:
        print(f"ERROR: no asset '{args.asset_id}' in the manifest", file=sys.stderr)
        print("       known: " + ", ".join(a["id"] for a in manifest["assets"]))
        return 2

    seeds = (
        [int(s) for s in args.seeds.split(",")]
        if args.seeds
        else list(asset["seeds"])
    )
    resolution = args.resolution or gen["resolution_budget_fixed"]
    steps = args.steps

    guide_src = GUIDES_DIR / f"{asset['id']}_guide.png"
    if not guide_src.exists():
        print(f"ERROR: guide missing: {guide_src}", file=sys.stderr)
        print("       build it first (Phase 2.1) — refusing to run without a guide")
        return 2

    in_dir, out_dir = server_dirs()
    print(f"comfy input : {in_dir}")
    print(f"comfy output: {out_dir}")
    print(f"asset       : {asset['id']}  seeds={seeds}  steps={steps}  resolution={resolution}")
    print(f"guide       : {guide_src}")

    TMP_DIR.mkdir(parents=True, exist_ok=True)
    (RAW_DIR / asset["id"]).mkdir(parents=True, exist_ok=True)

    baseline = json.loads(BASELINE_WORKFLOW.read_text(encoding="utf-8"))
    guide_name = f"ui_{asset['id']}_guide.png"

    if not args.dry_run:
        shutil.copy2(guide_src, in_dir / guide_name)
        print(f"  staged guide -> {in_dir / guide_name}")

    failures = 0
    for n, seed in enumerate(seeds, 1):
        print(f"\n--- job {n}/{len(seeds)}  seed={seed} ---")
        graph = patch_workflow(baseline, asset, guide_name, seed, steps, resolution)
        tmp_workflow = TMP_DIR / f"ui_{asset['id']}_seed{seed}.json"
        tmp_workflow.write_text(
            json.dumps(graph, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
        )
        print(f"  patched workflow -> {tmp_workflow.relative_to(REPO)}")

        if not run_validate(tmp_workflow):
            print("  ABORT: validation failed")
            failures += 1
            continue

        if args.dry_run:
            print("  dry-run: not submitting")
            continue

        started = time.time()
        try:
            ok, err = run_one(tmp_workflow, out_dir, args.timeout)
        except subprocess.TimeoutExpired:
            ok, err = False, f"timed out after {args.timeout}s"
        elapsed = time.time() - started

        if not ok:
            print(f"  FAILED after {elapsed:.1f}s:\n{err}")
            failures += 1
            continue

        new_names = run_one.last_new  # type: ignore[attr-defined]
        dests = []
        for i, name in enumerate(new_names):
            suffix = f"seed{seed}" if len(new_names) == 1 else f"seed{seed}_{i}"
            dest = RAW_DIR / asset["id"] / f"{suffix}.png"
            shutil.move(str(out_dir / name), str(dest))
            dests.append(dest)
            print(f"  output {name} -> {dest.relative_to(REPO)}")

        try:
            from PIL import Image  # noqa: PLC0415

            with Image.open(dests[0]) as im:
                actual = list(im.size)
        except Exception:  # noqa: BLE001
            actual = None

        append_log(
            {
                "asset_id": asset["id"],
                "seed": seed,
                "steps": steps,
                "resolution_budget": resolution,
                "cfg": gen["cfg"],
                "sampler": gen["sampler"],
                "scheduler": gen["scheduler"],
                "denoise": gen["denoise"],
                "control_after_generate": "fixed",
                "guide": str(guide_src.relative_to(REPO)),
                "prompt": asset["prompt"],
                "outputs": [str(d.relative_to(REPO)) for d in dests],
                "actual_png_size": actual,
                "elapsed_s": round(elapsed, 1),
                "finished_at": time.strftime("%Y-%m-%dT%H:%M:%S"),
            }
        )
        print(f"  done in {elapsed:.1f}s  actual png size={actual}")

    print(f"\n{len(seeds) - failures}/{len(seeds)} jobs ok")
    if failures:
        print("NOTE: the FIRST job also loads ~15.8 GB of weights — slow is expected, not a bug")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
