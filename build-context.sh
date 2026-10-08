#!/usr/bin/env bash
# build-context.sh — สร้าง .ai/context-pack.md สำหรับส่งให้ AI
# ปรับได้ผ่าน env:  MAX_FILE_BYTES=40000  TREE_MAX_LINES=800  RECENT_LOG=15
set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"

OUT=".ai/context-pack.md"
MAX_FILE_BYTES="${MAX_FILE_BYTES:-40000}"
TREE_MAX_LINES="${TREE_MAX_LINES:-800}"
RECENT_LOG="${RECENT_LOG:-15}"
mkdir -p .ai

BRANCH=$(git rev-parse --abbrev-ref HEAD)
COMMIT=$(git rev-parse --short HEAD)
DATE=$(date +%Y-%m-%d)
DIRTY=$(git status --porcelain | wc -l | tr -d ' ')

# ---------------------------------------------------------------------------
# รายการไฟล์ที่ผ่านตัวกรอง .aiignore (ไวยากรณ์ gitignore, ไม่ต้องพึ่ง ripgrep)
# = ไฟล์ที่ git track ทั้งหมด ลบด้วยไฟล์ที่ match .aiignore
# ---------------------------------------------------------------------------
FILTERED="$(mktemp)"
trap 'rm -f "$FILTERED"' EXIT

ALL_TMP="$(mktemp)"; IGN_TMP="$(mktemp)"
git -c core.quotepath=false ls-files | sort > "$ALL_TMP"
if [[ -f .aiignore ]]; then
  git -c core.quotepath=false ls-files -c -i -X .aiignore | sort > "$IGN_TMP"
else
  : > "$IGN_TMP"
fi
comm -23 "$ALL_TMP" "$IGN_TMP" | grep -v -E '^\.ai/' > "$FILTERED" || true
rm -f "$ALL_TMP" "$IGN_TMP"

TOTAL_FILES=$(wc -l < "$FILTERED" | tr -d ' ')

# ---------------------------------------------------------------------------
# Header + Ground Rules
# ---------------------------------------------------------------------------
cat > "$OUT" << EOF
# Cultivation-Together Context Pack

- Repository: Memphis004/Cultivation-Together
- Commit: $COMMIT
- Branch: $BRANCH
- Generated: $DATE
- Uncommitted changes in working tree: $DIRTY file(s) (ไฟล์ที่แนบมาอ่านจาก working tree ไม่ใช่จาก commit — ถ้า > 0 อาจต่างจาก commit)
- Files after .aiignore filter: $TOTAL_FILES

## Ground Rules (สำคัญมาก — บังคับ AI)

1. ใช้เฉพาะข้อมูลจากเอกสารนี้เท่านั้น ห้ามเดา
2. ห้ามสร้างชื่อไฟล์, class, method, API, หรือ behavior ที่ไม่มีใน context
3. เวลาอธิบายโค้ด ให้อ้างอิง path และ line range (ไฟล์ที่แนบมีเลขบรรทัดนำหน้าแล้ว เช่น \`Shared/GameMessages.cs:45-60\`)
4. ถ้าข้อมูลไม่พอ ให้ตอบเป็น JSON block แบบนี้ แล้วหยุด:

\`\`\`need_files
{
  "paths": ["path/to/file.cs"],
  "reason": "เหตุผลที่ต้องอ่านไฟล์นี้"
}
\`\`\`

5. โครงสร้าง repo:
   - \`Shared/\` = canonical source ของ DTO/state (แก้ตรงนี้เท่านั้น แล้วรัน \`./sync-shared.sh\` — ห้ามแก้ \`UnityProject/Assets/Scripts/Shared/\` หรือ \`McpBridge/Shared/\` ตรง ๆ; มี 3 สำเนา ถ้าไม่ sync จะ drift)
   - \`UnityProject/\` = Unity 6 game (VContainer + MessagePipe + UGUI/TMP)
   - \`McpBridge/\` = .NET 8 console app, MCP server (stdio) ↔ Unity (TCP 127.0.0.1:3215)
   - \`LLMWiki/\` = wiki (อ่าน \`wiki/index.md\` ก่อน) — **code คือ source of truth** ถ้า wiki ขัดกับ code ให้ code ชนะและแจ้งว่า wiki ล้าสมัย
   - \`DataTables/\` = Luban: \`Data/*.csv\` + \`Defines/schema.xml\` → \`gen.sh\`/\`gen.bat\` → \`UnityProject/Assets/Scripts/Data/Gen\` (namespace \`cfg.game\`, ห้ามแก้ไฟล์ใน Gen/) + JSON ใน \`Resources/DataTables\`

6. กฎโค้ดที่ผิดบ่อย (ยืนยันแล้วจาก bug log):
   - C# 8 เท่านั้น: ห้าม \`record\`, \`init\`, \`global using\`, target-typed \`new\`
   - ทุกการแก้ \`Stockpile.RawResources\` ต้องผ่าน \`AdjustAndNotify()\` ใน \`SectStateProvider\` เท่านั้น (ไม่งั้น HUD ไม่อัปเดต)
   - handler ที่มาจาก MessagePipe.Interprocess (TCP) ทำงานบน **background thread** — แตะ Unity API ต้อง \`await UniTask.SwitchToMainThread()\` ก่อน
   - in-process pub/sub ห้ามใช้แทนการเรียกข้าม process — ถ้า UI ต้องทำสิ่งเดียวกับ bridge ให้เรียก shared entry point ตรง ๆ (ตัวอย่าง: \`DecisionExecutor\`)
   - ฝั่ง visual/building/task message เป็น in-memory เท่านั้น ห้าม register บน interprocess broker
   - request-response ต้อง register \`RegisterTcpRemoteRequestHandler\` + \`RegisterAsyncRequestHandler\` ทั้งคู่ (ดู \`InterprocessInstaller.cs\`)
   - MessagePack \`[Key(N)]\` append-only ห้ามแก้เลขเดิม
   - ห้ามฟื้น outfit / \`TryApplyOutfit\` (decision record ใน \`LLMWiki/wiki/sources/avatar-appearance.md\` §1)
   - ห้ามใช้ \`FindObjectOfType\`/reflection หา service — ใช้ DI (constructor injection)
   - registration ใหม่ต้องไปอยู่ installer ที่ถูกชั้น (\`Core/Installers/*\`) หรือ \`SectSceneLifetimeScope\` ถ้าผูกฉาก — ห้ามยัดกลับ \`GameLifetimeScope.Configure()\`

## Known Issues / Open Items

ดูรายการล่าสุดในไฟล์ \`LLMWiki/wiki/sources/open-questions.md\` (แนบด้านล่าง) และ "Recent Wiki Log" ท้ายเอกสาร — script นี้ไม่ hardcode รายการที่ล้าสมัยง่ายอีกแล้ว

EOF

# ---------------------------------------------------------------------------
# Key Symbols — type declarations ของทุก .cs ที่ไม่ใช่ generated/test/editor/spike
# (แสดงชื่อ type ต่อไฟล์บรรทัดเดียว เพื่อประหยัด token)
# ---------------------------------------------------------------------------
{
  echo "## Key Symbols (C# types per file)"
  echo
  echo "(ตัด \`Gen/\`, \`Tests/\`, \`Editor/\`, \`Spikes/\` ออก — ถ้าต้องการให้ขอผ่าน need_files)"
  echo
} >> "$OUT"

TYPE_RE='^[[:space:]]*(public|internal)[[:space:]]+((static|sealed|abstract|partial)[[:space:]]+)*(class|struct|interface|enum)[[:space:]]+[A-Za-z_][A-Za-z0-9_]*'

while IFS= read -r f; do
  [[ -f "$f" ]] || continue
  syms=$(grep -hoE "$TYPE_RE" "$f" 2>/dev/null \
         | sed -E 's/^[[:space:]]*(public|internal)[[:space:]]+//; s/[[:space:]]+/ /g' \
         | paste -sd ',' - | sed 's/,/, /g' || true)
  if [[ -n "$syms" ]]; then
    echo "- \`$f\`: $syms" >> "$OUT"
  fi
done < <(grep -E '\.cs$' "$FILTERED" \
         | grep -E '^(Shared|McpBridge|UnityProject/Assets/Scripts)/' \
         | grep -v -E '/(Gen|Tests|Editor|Spikes|obj|bin)/' || true)

echo >> "$OUT"

# ---------------------------------------------------------------------------
# Selected Source Files (ใส่เลขบรรทัด + จำกัดขนาดต่อไฟล์)
# ---------------------------------------------------------------------------
echo "## Selected Source Files" >> "$OUT"
echo >> "$OUT"
echo "(แต่ละไฟล์จำกัด ${MAX_FILE_BYTES} bytes; เลขนำหน้าคือเลขบรรทัดจริงของไฟล์)" >> "$OUT"
echo >> "$OUT"

MUST_INCLUDE=(
  # --- repo/workflow ---
  "project_summary.md"
  "AGENTS.md"
  "sync-shared.sh"
  # --- shared contracts (canonical) ---
  "Shared/GameMessages.cs"
  "Shared/SectEconomyState.cs"
  "Shared/MockSectData.cs"
  # --- bridge ---
  "McpBridge/Program.cs"
  # --- composition root + installers ---
  "UnityProject/Assets/Scripts/Core/GameLifetimeScope.cs"
  "UnityProject/Assets/Scripts/Core/Installers/InterprocessInstaller.cs"
  "UnityProject/Assets/Scripts/Scenes/SectScene/SectSceneLifetimeScope.cs"
  # --- state + ISectStateProvider (interface อยู่ท้าย TimeSystem.cs) ---
  "UnityProject/Assets/Scripts/Core/TimeSystem.cs"
  "UnityProject/Assets/Scripts/Systems/SectStateProvider.cs"
  # --- data pipeline ---
  "DataTables/luban.conf"
  "DataTables/Defines/schema.xml"
  # --- small design data ---
  "UnityProject/Assets/Resources/Data/building_defs.json"
  "UnityProject/Assets/Resources/Data/visual_overrides.json"
  # --- wiki: สารบัญ + กฎ + งานค้าง ---
  "LLMWiki/wiki/index.md"
  "LLMWiki/wiki/conventions.md"
  "LLMWiki/wiki/sources/architecture.md"
  "LLMWiki/wiki/sources/open-questions.md"
)

MISSING=()
for f in "${MUST_INCLUDE[@]}"; do
  if [[ ! -f "$f" ]]; then
    MISSING+=("$f")
    continue
  fi
  size=$(wc -c < "$f" | tr -d ' ')
  {
    echo "### $f ($size bytes)"
    echo '````'
    if (( size > MAX_FILE_BYTES )); then
      head -c "$MAX_FILE_BYTES" "$f" | cat -n
      echo
      echo "... [TRUNCATED: แสดง $MAX_FILE_BYTES จาก $size bytes — ขอส่วนที่เหลือผ่าน need_files]"
    else
      cat -n "$f"
    fi
    echo '````'
    echo
  } >> "$OUT"
done

if (( ${#MISSING[@]} > 0 )); then
  {
    echo "### Missing (ไม่พบใน working tree)"
    for m in "${MISSING[@]}"; do echo "- $m"; done
    echo
  } >> "$OUT"
  echo "⚠️  Missing files: ${MISSING[*]}" >&2
fi

# ---------------------------------------------------------------------------
# File Tree: สรุปจำนวนไฟล์ต่อโฟลเดอร์ + รายชื่อไฟล์ที่สำคัญ (ผ่าน .aiignore แล้ว)
# ---------------------------------------------------------------------------
{
  echo "## File Tree (filtered by .aiignore)"
  echo
  echo "### Files per directory (depth ≤ 5)"
  echo '```'
  awk -v D=5 '{
    n = split($0, a, "/");
    if (n == 1) { d = "."; }
    else {
      m = (n - 1 < D) ? n - 1 : D;
      d = a[1];
      for (i = 2; i <= m; i++) d = d "/" a[i];
    }
    c[d]++
  } END { for (k in c) printf "%5d  %s\n", c[k], k }' "$FILTERED" | sort -k2
  echo '```'
  echo
  echo "### Source/doc files (.cs .md .sh .csv .xml .conf .asmdef .json)"
  echo '```'
} >> "$OUT"

LIST_TMP="$(mktemp)"
grep -E '\.(cs|md|sh|csv|xml|conf|asmdef|json)$' "$FILTERED" | grep -v '/Gen/' > "$LIST_TMP" || true
LIST_TOTAL=$(wc -l < "$LIST_TMP" | tr -d ' ')
head -n "$TREE_MAX_LINES" "$LIST_TMP" | sed 's/^/  /' >> "$OUT"
if (( LIST_TOTAL > TREE_MAX_LINES )); then
  echo "  ... [$((LIST_TOTAL - TREE_MAX_LINES)) more files truncated — ปรับ TREE_MAX_LINES หรือ .aiignore]" >> "$OUT"
fi
rm -f "$LIST_TMP"

echo '```' >> "$OUT"
echo >> "$OUT"

# ---------------------------------------------------------------------------
# Recent Wiki Log
# ---------------------------------------------------------------------------
if [[ -f LLMWiki/wiki/log.md ]]; then
  {
    echo "## Recent Wiki Log (last $RECENT_LOG entries, each cut to 400 chars)"
    echo
    echo '```'
    grep '^## \[' LLMWiki/wiki/log.md | tail -n "$RECENT_LOG" | cut -c1-400
    echo '```'
    echo
  } >> "$OUT"
fi

BYTES=$(wc -c < "$OUT" | tr -d ' ')
LINES=$(wc -l < "$OUT" | tr -d ' ')
echo "✅ Generated $OUT ($LINES lines, $BYTES bytes, ~$((BYTES / 3)) tokens est.)"