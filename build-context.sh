#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"
OUT=".ai/context-pack.md"
mkdir -p .ai

BRANCH=$(git rev-parse --abbrev-ref HEAD)
COMMIT=$(git rev-parse --short HEAD)
DATE=$(date +%Y-%m-%d)

# --- Header + ground rules ---
cat > "$OUT" << EOF
# Cultivation-Together Context Pack

- Repository: Memphis004/Cultivation-Together
- Commit: $COMMIT
- Branch: $BRANCH
- Generated: $DATE

## Ground Rules (สำคัญมาก — บังคับ AI)

1. ใช้เฉพาะข้อมูลจากเอกสารนี้เท่านั้น ห้ามเดา
2. ห้ามสร้างชื่อไฟล์, class, method, API, หรือ behavior ที่ไม่มีใน context
3. เวลาอธิบายโค้ด ให้อ้างอิง path และ line range (เช่น \`Shared/GameMessages.cs:45-60\`)
4. ถ้าข้อมูลไม่พอ ให้ตอบเป็น JSON block แบบนี้ แล้วหยุด:

\`\`\`need_files
{
  "paths": ["path/to/file.cs"],
  "reason": "เหตุผลที่ต้องอ่านไฟล์นี้"
}
\`\`\`

5. โครงสร้าง repo:
   - \`Shared/\` = canonical source (แก้ตรงนี้เท่านั้น อย่าแก้ใน UnityProject/Shared/ หรือ McpBridge/Shared/)
   - \`UnityProject/\` = Unity game
   - \`McpBridge/\` = .NET AI GM bridge (TCP 127.0.0.1:3215)
   - \`LLMWiki/\` = wiki สำหรับ LLM
   - \`DataTables/\` = luban pipeline xlsx → csv

## Known Issues (จาก README)

- \`SectStateQueryHandler.InvokeAsync\` เรียก \`state.ToByteArray()\` ซึ่งยังไม่มีใน \`SectEconomyState\` (ต้อง run protoc หรือเขียน stub เอง)
- \`SectActionTools.ExecuteDecision\` ยังเป็น \`NotImplementedException\` (read path only)

EOF

# --- Key Symbols (C#) ---
cat >> "$OUT" << 'EOF'
## Key Symbols (C#)

EOF

for f in Shared/*.cs; do
  if [[ -f "$f" ]]; then
    echo "### $f" >> "$OUT"
    echo '```csharp' >> "$OUT"
    grep -E '^\s*(public|internal|private)\s+(class|struct|interface|record|enum)\s+\w+' "$f" | head -20 >> "$OUT" || true
    grep -E '^\s*(public|internal|private)\s+([\w<>\[\],\s]+\s+)?(\w+)\s*\(' "$f" | head -30 | sed 's/^/  /' >> "$OUT" || true
    echo '```' >> "$OUT"
    echo >> "$OUT"
  fi
done

# --- Selected Source Files ---
cat >> "$OUT" << 'EOF'
## Selected Source Files

EOF

MUST_INCLUDE=(
  "Shared/GameMessages.cs"
  "Shared/SectEconomyState.cs"
  "Shared/MessageTypes.cs"
  "Shared/MockSectData.cs"
  "McpBridge/Program.cs"
  "sync-shared.sh"
  "LLMWiki/README.md"
  "project_summary.md"
)

for f in "${MUST_INCLUDE[@]}"; do
  if [[ -f "$f" ]]; then
    echo "### $f" >> "$OUT"
    echo '```' >> "$OUT"
    cat "$f" >> "$OUT"
    echo '```' >> "$OUT"
    echo >> "$OUT"
  fi
done

# --- File Tree (filter ด้วย .aiignore) ---
# อยากให้ตัดรายการไฟล์สั้นลง เพราะรายการนี้กินบรรทัดเยอะมาก
cat >> "$OUT" << 'EOF'
## File Tree (filtered)

```
EOF

if command -v rg >/dev/null 2>&1; then
  rg --files --ignore-file .aiignore --glob '!.aiignore' . | sed 's|^\./||' | sort | head -200 | sed 's/^/  /' >> "$OUT"
else
  git ls-files | grep -v -E '(Library|Temp|Obj|Builds|\.psd|\.tga|\.safetensors|\.ckpt|\.dll|\.meta)' | head -200 | sed 's/^/  /' >> "$OUT"
fi

cat >> "$OUT" << 'EOF'
```

EOF

echo "✅ Generated $OUT ($(wc -l < "$OUT") lines, $(wc -c < "$OUT" | tr -d ' ') bytes)"
