#!/usr/bin/env python3
"""Build `comfy/qwen21_ui_asset.json` from the pristine source workflow.

Source (read-only, copied verbatim into Artwork/ by hand):
    Artwork/qwen21_hanfu_atlas_edit.json
    sha256 24712ac6a5cf3a74894e9a14c95809db7efc6c64f42aee31286b693bce97caad

Everything is copied byte-faithfully except the fields listed in EDITS below.
The graph stays a UI-format workflow (nodes/links), which is what the source is
and what `comfy workflow slots` / `set-slot` / `validate` all understand.

Node map (verified with `comfy workflow slots` on the source):
    1  UNETLoader            qwen_image_2.1_int8_convrot.safetensors   UNTOUCHED
    2  CLIPLoader            qwen3vl_8b_int8_convrot.safetensors       UNTOUCHED
    3  VAELoader             qwen_image_2.1_vae_bf16.safetensors       UNTOUCHED
    4  QwenImage21Cache      auto / default                            UNTOUCHED
    5  TextEncodeQwenImage21 clip + images.image_1 + vae               EDITED (prompt, resolution)
    6  KSampler              seed/control_after_generate/steps          EDITED
    7  VAEDecode                                                       UNTOUCHED
    8  LoadImage (image_1)   the guide silhouette                     EDITED (image)
    9  LoadImage (image_2)   optional style ref, stays MUTED (mode=2)  UNTOUCHED
    10 SaveImage             filename_prefix                           EDITED
    11 MarkdownNote          settings note                             EDITED (docs only)
    12 MarkdownNote          asset note                                EDITED (docs only)

Run:  python tools/art/make_ui_workflow.py
"""

from __future__ import annotations

import json
import pathlib
import sys

REPO = pathlib.Path(__file__).resolve().parents[2]
SOURCE = REPO / "Artwork" / "qwen21_hanfu_atlas_edit.json"
OUTPUT = REPO / "comfy" / "qwen21_ui_asset.json"
MANIFEST = REPO / "art" / "disciple_list" / "manifest.json"

SOURCE_SHA256 = "24712ac6a5cf3a74894e9a14c95809db7efc6c64f42aee31286b693bce97caad"


def sha256(path: pathlib.Path) -> str:
    import hashlib

    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    if not SOURCE.exists():
        print(f"ERROR: source workflow missing: {SOURCE}", file=sys.stderr)
        return 1

    digest = sha256(SOURCE)
    if digest != SOURCE_SHA256:
        print(
            f"WARNING: {SOURCE.name} sha256 is {digest}, expected {SOURCE_SHA256}.\n"
            "         Proceeding, but the baseline may have drifted from what was measured.",
            file=sys.stderr,
        )

    graph = json.loads(SOURCE.read_text(encoding="utf-8"))
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    target = next(a for a in manifest["assets"] if a["id"] == "scroll_paper")

    nodes = {n["id"]: n for n in graph["nodes"]}

    # ── node 8: guide silhouette (default = scroll_paper; run_job.py repoints it) ──
    n8 = nodes[8]
    n8["widgets_values"][0] = "ui_scroll_paper_guide.png"
    n8["widgets_values_named"]["image"] = "ui_scroll_paper_guide.png"
    n8["title"] = "LoadImage (guide silhouette = ui_<id>_guide.png)"

    # ── node 5: prompt + resolution budget (negative_prompt left as-is; inert at cfg=1) ──
    n5 = nodes[5]
    n5["widgets_values"][0] = target["prompt"]
    n5["widgets_values"][2] = manifest["generation"]["resolution_budget_fixed"]
    n5["widgets_values_named"]["prompt"] = target["prompt"]
    n5["widgets_values_named"]["resolution"] = manifest["generation"]["resolution_budget_fixed"]

    # ── node 6: fixed seed, fixed control_after_generate, 25 steps, cfg 1 ──
    n6 = nodes[6]
    n6["widgets_values"] = [target["seeds"][0], "fixed", 25, 1, "euler", "simple", 1]
    n6["widgets_values_named"] = {
        "seed": target["seeds"][0],
        "control_after_generate": "fixed",
        "steps": 25,
        "cfg": 1,
        "sampler_name": "euler",
        "scheduler": "simple",
        "denoise": 1,
    }

    # ── node 10: output prefix ──
    n10 = nodes[10]
    n10["widgets_values"][0] = "ui_scroll_paper_"
    n10["widgets_values_named"]["filename_prefix"] = "ui_scroll_paper_"

    # ── notes: replace the hanfu-atlas documentation with this kit's docs ──
    nodes[11]["title"] = "Note: settings (do not change)"
    nodes[11]["widgets_values"] = [
        "## Qwen-Image-2.1 edit — DiscipleList window kit\n"
        "\n"
        "**Sampler** (do NOT use classic img2img values here)\n"
        "- denoise: 1 — instruction-edit model. Edit strength comes from the\n"
        "  *prompt*, not from denoise.\n"
        "- cfg: 1 — keep it. **The negative prompt is ONLY read when cfg > 1.**\n"
        "  At cfg=1 every prohibition must live in the POSITIVE prompt.\n"
        "- euler / simple / 25 steps.\n"
        "- control_after_generate: **fixed** — every seed is explicit so a job\n"
        "  can be reproduced exactly.\n"
        "\n"
        "**resolution** (widget on node 5, TextEncodeQwenImage21)\n"
        "- a total PIXEL BUDGET, not a side length.\n"
        "- the canvas keeps image_1's aspect ratio and is snapped to a\n"
        "  multiple of 32, so the long side can EXCEED the widget value.\n"
        "  Measured precedent on this machine: a 253x142 source at budget\n"
        "  1024 produced 1376x768 (~1.06 MP).\n"
        "- 0 = keep the reference's own size.\n"
        "- This machine has 15.9 GB VRAM and the weights alone are ~15.8 GB.\n"
        "  Do NOT raise the budget. Run ONE job at a time.\n"
        "\n"
        "**image_1** = the guide silhouette (flat magenta background).\n"
        "**image_2** = optional style reference; its LoadImage stays MUTED\n"
        "node 9 / mode=2). Unmute it, upload a file, and it is read from\n"
        "<image2> in the prompt. Reference images are addressed as\n"
        "<image1>, <image2>, ... — note the angle brackets and no space.\n"
        "\n"
        "**Pixels of Body_dizijm.png must never be fed to this graph.**\n"
        "That asset is for measuring structure and proportions only; the\n"
        "guide is a synthesized flat silhouette.\n"
    ]
    nodes[12]["title"] = "Note: this kit's paths + how to run"
    nodes[12]["widgets_values"] = [
        "## DiscipleList window kit\n"
        "\n"
        "Built by `tools/art/make_ui_workflow.py` from\n"
        "`Artwork/qwen21_hanfu_atlas_edit.json`, which is an unmodified copy\n"
        "of the source workflow. Do not edit this file by hand — re-run the\n"
        "builder instead.\n"
        "\n"
        "## Model files\n"
        "diffusion_models/qwen_image_2.1_int8_convrot.safetensors\n"
        "text_encoders/qwen3vl_8b_int8_convrot.safetensors\n"
        "vae/qwen_image_2.1_vae_bf16.safetensors\n"
        "huggingface.co/Comfy-Org/Qwen-Image-2.1\n"
        "\n"
        "## Pipeline\n"
        "1. `python tools/art/make_guide.py <asset_id>`\n"
        "2. `comfy workflow validate comfy/qwen21_ui_asset.json`\n"
        "3. `python tools/art/run_job.py <asset_id>`\n"
        "   raw output lands in `art/disciple_list/raw/<id>/seed<N>.png`\n"
        "4. use `comfy/workflow set-slot ... --stdout` to preview a tweak\n"
        "   without writing the baseline.\n"
        "\n"
        "## Assets in this kit\n"
        "scroll_paper, scroll_rod, close_medallion, cloud_corner,\n"
        "title_plate — see `art/disciple_list/manifest.json`.\n"
    ]

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(
        json.dumps(graph, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )

    print(f"wrote {OUTPUT.relative_to(REPO)}")
    print(f"  nodes: {len(graph['nodes'])}  links: {len(graph.get('links', []))}")
    print(f"  node 8 guide  : {n8['widgets_values'][0]}")
    print(f"  node 5 res    : {n5['widgets_values'][2]}")
    print(f"  node 5 prompt : {n5['widgets_values'][0][:70]}...")
    print(f"  node 6 widgets: {n6['widgets_values']}")
    print(f"  node 10 prefix: {n10['widgets_values'][0]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
