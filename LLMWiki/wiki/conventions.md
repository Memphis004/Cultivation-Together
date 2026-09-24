---
title: Conventions
type: conventions
sources: []
related: []
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [workflow, preferences]
---

# Conventions

> Your workflow rules and preferences. The LLM agent reads this to understand
> how you like to work. Edit this freely — the agent will pick up changes.

## Communication Style

- **Language**: Mixed Thai/English (Thai for prose, English for technical terms)
- **Response format**: Concise, bulleted, technical
- **Code style**: Opinionated recommendations, not "it depends" — pick one
  approach and justify it
- **Tone**: Direct, no fluff. When uncertain, say so explicitly.

## Coding Preferences

- **C# version**: 8.0 compatible (Unity 6.3 default — no `record`/`init`/`global using`)
- **Naming**: PascalCase for public, `_camelCase` for private fields (Unity convention)
- **Architecture**:
  - Composition over inheritance
  - ScriptableObject for design-time data, plain C# for runtime state
  - MessagePack (NOT protobuf — dropped in lab 7)
  - Luban for bulk design-time data, not hand-created ScriptableObjects
- **Open-source first**: every dependency in the project is OSS or Unity built-in
  - Exception (approved 2026-09-25, S4): `Spine-Unity` runtime (Esoteric Software) เป็น dependency ของ visual system Tier-2 — license ยืนยันแล้วว่าครอบคลุม ([[decisions/visual-overrides-straight-alpha]]); example assets ของ Esoteric ห้ามตกค้างใน build จริง; asmdef แยกเพื่อสลับไป Unity 2D Animation ได้หากจำเป็น
- **No reflection** in UI panel resolution (enum→Type mapping instead)

## Wiki Usage

- **Always read `wiki/index.md` first** when answering queries
- **Always update `wiki/index.md` and `wiki/log.md`** on every operation
- **Discuss key takeaways** before mass-updating wiki pages
- **Cite sources** using `[[wikilinks]]` AND include the actual C# file path
  in backticks, e.g. `` `Assets/Scripts/Systems/SectStateProvider.cs:177` ``
- **Ask before** creating new top-level categories
- **Code is the source of truth** — if a design doc disagrees with code, code
  wins; flag the doc for update, don't change code silently

## Code Reference Convention

When explaining a system, link to the actual file with line number:

```markdown
Implemented in `Assets/Scripts/Systems/SectStateProvider.cs:177` —
`RecruitOuterDisciple()` publishes `DiscipleRecruitedMessage` on success.
```

## DevLog Cadence

- Write a devlog entry at the end of each work session in Thai
- Format: `devlog-YYYY-MM-DD.md` in `wiki/sources/`
- Include: what was done, what's next, any blockers, bugs found
- Round-up entries (`devlog-history.md`) summarize 2+ sessions at a time

## Bug Tracking

- Every bug → entry in `wiki/sources/bug-log.md`
- Format: date, repro, root cause, fix, related files, lessons learned
- After fix, agent should update the relevant concept page
- Bugs from the original lab rounds are preserved with `lab-N` prefix

## MCP / Bridge Debugging

When asking about MCP-related issues, include:
- Which MCP tool (`get_sect_state` / `await_next_world_event` / `execute_decision` / `purchase_item`)
- Unity console log
- McpBridge console log
- Whether Unity Editor's Pause button is engaged (separate from `TimeSystem.IsPaused`)
- Whether Unity's `Run In Background` is enabled in Player Settings

## Open Design Questions (don't assume, ask)

See `[[sources/open-questions]]` for the full list. When asked about:
- Combat → ask ACS-style vs Rimworld-style
- Stat system → propose something simple, don't import a complex framework
- Sect war → remind this is WIP, suggest deferring
- World event conditions → mention that current impl is weighted-random, no state logic
- Shop prices / event consequences → mention these are placeholders pending real balance
