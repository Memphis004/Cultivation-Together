#!/usr/bin/env python3
"""
ถามคำถามกับ AiPASS Bridge โดยส่ง context pack ไปด้วย
ต้องรันจาก root ของ Cultivation-Together เสมอ
"""

import json
import os
import sys
from openai import OpenAI

REPO_ROOT = os.path.dirname(os.path.abspath(__file__))
CONTEXT_FILE = os.path.join(REPO_ROOT, ".ai", "context-pack.md")
# ปรับได้ตามโมเดลที่ AiPASS ให้ใช้
MODEL = os.environ.get("AIPASS_MODEL", "gemini-2.5-pro")
# AiPASS Bridge endpoint — ปรับ port ถ้าไม่ใช่ 8787
BASE_URL = os.environ.get("AIPASS_BASE_URL", "http://127.0.0.1:8787/v1")
API_KEY = os.environ.get("AIPASS_API_KEY", "sk-dummy")


def load_context() -> str:
    if not os.path.exists(CONTEXT_FILE):
        print(f"[ERROR] ไม่พบ {CONTEXT_FILE} — รัน `./build-context.sh` ก่อน", file=sys.stderr)
        sys.exit(1)
    with open(CONTEXT_FILE, "r", encoding="utf-8") as f:
        return f.read()


SYSTEM_PROMPT = """You are a senior Unity/.NET engineer working on Cultivation-Together.
You have access to a context pack that describes the project, its file tree,
key C# symbols, and selected source files.

<context>
{context}
</context>    Rules:
1. Use ONLY the context above. Do not bring in outside knowledge.
2. Do not invent file paths, classes, methods, or APIs.
3. Cite file paths when referencing code (e.g. `Shared/GameMessages.cs:45-60`).
4. If information is missing, respond with a need_files JSON block and stop:
   ```need_files
   {{
     "paths": ["path/to/file.cs"],
     "reason": "เหตุผลที่ต้องอ่านไฟล์นี้"
   }}
   ```
5. Be concise and technical. Thai or English — whichever the question uses.
"""


def ask(question: str, max_turns: int = 3) -> None:
    context = load_context()
    client = OpenAI(base_url=BASE_URL, api_key=API_KEY)

    system_content = SYSTEM_PROMPT.replace("{context}", context)
    messages = [
        {"role": "system", "content": system_content},
        {"role": "user", "content": question},
    ]

    turn = 0
    while turn < max_turns:
        turn += 1
        try:
            resp = client.chat.completions.create(model=MODEL, messages=messages)
        except Exception as e:
            print(f"[ERROR] เรียก AiPASS ล้มเหลว (turn {turn}): {e}", file=sys.stderr)
            sys.exit(1)

        content = resp.choices[0].message.content or ""
        print(f"\n--- Turn {turn} ---")
        print(content)

        # --- รอบสอง+: ถ้า AI ขอไฟล์เพิ่มในรอบก่อน ให้แนบไปให้ ---
        if "```need_files" in content:
            try:
                start = content.find("```need_files") + len("```need_files")
                end = content.find("```", start)
                json_str = content[start:end].strip()
                need = json.loads(json_str)
            except Exception as e:
                print(f"[WARN] parse need_files ล้มเหลว: {e}", file=sys.stderr)
                break

            paths = need.get("paths", [])
            reason = need.get("reason", "")
            if not paths:
                print("[INFO] AI ขอฟิลด์ paths เป็น empty — หยุดรอบนี้", file=sys.stderr)
                break

            print(f"\n[INFO] AI ขอไฟล์เพิ่ม: {paths} (เหตุผล: {reason})", file=sys.stderr)
            extra_lines = ["\n## Additional Requested Files (from AI)"]
            for p in paths:
                full = os.path.join(REPO_ROOT, p)
                if os.path.exists(full):
                    with open(full, "r", encoding="utf-8") as f:
                        body = f.read()
                    extra_lines.append(f"\n### {p}\n```\n{body}\n```")
                else:
                    extra_lines.append(f"\n### {p}\n[FILE NOT FOUND: {full}]")

            messages.append({"role": "assistant", "content": content})
            messages.append(
                {
                    "role": "user",
                    "content": "\n\n".join(extra_lines)
                    + "\n\nContinue answering the original question.",
                }
            )
        else:
            break


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python ask_aipass.py \"คำถามที่นี่\"", file=sys.stderr)
        sys.exit(1)
    ask(" ".join(sys.argv[1:]))
