#!/usr/bin/env python3
"""TEMP runner for layers_trial (portrait_edit_round.py is missing from the skill).

Mirrors run() of Tools/art/portrait_female_candidates.py: validate -> persist intent ->
comfy run -> jobs wait -> download. Writes only under art/portraits_v2/layers_trial/.
Modes:  --preview  (default) build graphs + validate, submits nothing
        --run      submit the jobs in prompts.json, one at a time
"""
import copy
import json
import pathlib
import shutil
import subprocess
import sys
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[3]
T = ROOT / "art" / "portraits_v2" / "layers_trial"
LOG = T / "generation_log.json"
SENTINEL = T / "pause_requested"
TEMPLATE = ROOT / "art" / "portraits_v2" / "workflows" / "d001_seed430101.json"
PROMPTS = T / "prompts.json"
BASE = T / "base_norm.png"
ANCHOR = ROOT / "art" / "portraits_v2" / "anchor" / "1cf13e0c_000.png"
NODE_BASE, NODE_ANCHOR, NODE_PROMPT, NODE_SEED, NODE_SAVE, NODE_REF = "4", "5", "6", "8", "10", "11"
TMP_BASE_NAME = "lt_base_norm.png"
TMP_ANCHOR_NAME = "lt_anchor_1cf13e0c_000.png"


def server_dirs():
    fallback_in = pathlib.Path(r"C:\Users\memph\AppData\Local\Comfy-Desktop\ComfyUI-Shared\input")
    fallback_out = pathlib.Path(r"C:\Users\memph\AppData\Local\Comfy-Desktop\ComfyUI-Shared\output")
    try:
        with urllib.request.urlopen("http://127.0.0.1:8188/system_stats", timeout=10) as r:
            argv = json.load(r)["system"]["argv"]
    except Exception as exc:  # noqa: BLE001
        print(f"! system_stats failed ({exc}); fallback dirs")
        return fallback_in, fallback_out
    inp = out = None
    for i, tok in enumerate(argv):
        if tok == "--input-directory" and i + 1 < len(argv):
            inp = pathlib.Path(argv[i + 1])
        elif tok == "--output-directory" and i + 1 < len(argv):
            out = pathlib.Path(argv[i + 1])
    return inp or fallback_in, out or fallback_out


def cli(*args):
    proc = subprocess.run(["comfy", *args], capture_output=True, text=True, encoding="utf-8", errors="replace")
    if proc.returncode:
        raise RuntimeError(f"comfy {args}: {proc.stdout}\n{proc.stderr}")
    payload = json.loads(proc.stdout)
    if payload.get("type") == "envelope":
        if not payload.get("ok"):
            raise RuntimeError(payload)
        return payload["data"]
    return payload


def save_log(log):
    tmp = LOG.with_suffix(".tmp")
    tmp.write_text(json.dumps(log, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    tmp.replace(LOG)


def build_graphs(items):
    template = json.loads(TEMPLATE.read_text(encoding="utf-8"))
    out = []
    for item in items:
        g = copy.deepcopy(template)
        g[NODE_BASE]["inputs"]["image"] = TMP_BASE_NAME
        g[NODE_ANCHOR]["inputs"]["image"] = TMP_ANCHOR_NAME
        g[NODE_PROMPT]["inputs"]["prompt"] = item["prompt"]
        g[NODE_PROMPT]["inputs"]["negative_prompt"] = g[NODE_PROMPT]["inputs"]["negative_prompt"]
        g[NODE_SEED]["inputs"]["seed"] = item["seed"]
        g[NODE_SAVE]["inputs"]["filename_prefix"] = f"layers_trial_head_seed{item['seed']}"
        out.append((item["seed"], g))
    return out


def stage_inputs(inp_dir):
    inp_dir.mkdir(parents=True, exist_ok=True)
    shutil.copy2(BASE, inp_dir / TMP_BASE_NAME)
    shutil.copy2(ANCHOR, inp_dir / TMP_ANCHOR_NAME)


def preview(items, inp_dir):
    stage_inputs(inp_dir)
    wdir = T / "workflows"
    wdir.mkdir(exist_ok=True)
    report = []
    for seed, g in build_graphs(items):
        path = wdir / f"head_seed{seed}.json"
        path.write_text(json.dumps(g, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        v = cli("workflow", "validate", "--workflow", str(path))
        report.append({"seed": seed, "workflow": str(path.relative_to(ROOT)).replace("\\", "/"), "validate": v})
        print(seed, "valid=", v.get("valid"), "errors=", v.get("error_count"), "warnings=", v.get("warning_count"),
              "spends=", v.get("spends_credits"))
    return report


def run(items, inp_dir, out_dir):
    if SENTINEL.exists():
        print("PAUSED: pause_requested sentinel present; no submission.")
        return 2
    stage_inputs(inp_dir)
    log = json.loads(LOG.read_text(encoding="utf-8")) if LOG.exists() else {
        "generation_requests_submitted": 0, "jobs": []}
    for seed, g in build_graphs(items):
        if SENTINEL.exists():
            print("PAUSED before next submit (sentinel).")
            break
        if any(j["seed"] == seed and j.get("status") in ("submitted", "submitting", "completed") for j in log["jobs"]):
            print(f"skip seed {seed}: already recorded in log")
            continue
        path = T / "workflows" / f"head_seed{seed}.json"
        path.write_text(json.dumps(g, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        v = cli("workflow", "validate", "--workflow", str(path))
        if not v.get("valid") or v.get("error_count") or v.get("warning_count") or v.get("spends_credits"):
            raise RuntimeError(f"not cleared: {v}")
        job = {"seed": seed, "workflow": str(path.relative_to(ROOT)).replace("\\", "/"), "status": "submitting"}
        log["jobs"].append(job)
        log["generation_requests_submitted_before_submit"] = log["generation_requests_submitted"]
        save_log(log)  # persist intent BEFORE submit
        sub = cli("run", "--workflow", str(path), "--where", "local", "--host", "127.0.0.1", "--port", "8188",
                  "--no-notify", "--no-watch")
        job["prompt_id"] = sub["prompt_id"]
        job["status"] = "submitted"
        log["generation_requests_submitted"] += 1
        save_log(log)
        cli("jobs", "wait", job["prompt_id"], "--host", "127.0.0.1", "--port", "8188", "--timeout", "900",
            "--poll-interval", "10")
        st = cli("jobs", "status", job["prompt_id"], "--host", "127.0.0.1", "--port", "8188")
        if st.get("status") != "completed" or st.get("execution_error") or st.get("error"):
            job["status"] = "failed_or_ambiguous"
            save_log(log)
            raise RuntimeError(f"job failed/ambiguous, do not resubmit: {st}")
        folder = T / "raw" / str(seed)
        dl = cli("download", job["prompt_id"], "--where", "local", "-o", str(folder))
        files = dl.get("files", [])
        if len(files) != 1:
            job["status"] = "download_ambiguous"
            save_log(log)
            raise RuntimeError(f"expected 1 output: {dl}")
        job["status"] = "completed"
        job["raw"] = files[0]["path"]
        log["images_completed"] = sum(1 for j in log["jobs"] if j.get("status") == "completed")
        save_log(log)
        print("completed", seed, files[0]["path"])
    return 0


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "--preview"
    items = json.loads(PROMPTS.read_text(encoding="utf-8"))
    inp, outd = server_dirs()
    print("input dir:", inp, "| output dir:", outd)
    if mode == "--preview":
        preview(items, inp)
    elif mode == "--run":
        sys.exit(run(items, inp, outd))
