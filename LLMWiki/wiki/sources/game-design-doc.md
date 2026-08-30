---
title: Game Design Document
type: gdd
sources:
  - ../../project_summary.md
related:
  - "[[overview]]"
  - "[[sources/mechanics]]"
  - "[[sources/architecture]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [gdd, vision, design, xianxia]
---

# Game Design Document (GDD)

> High-level game design. Source of truth = `../../project_summary.md` (Thai).
> This wiki page is a structured summary to make it queryable.

## 1. Concept

**Xianxia sect-management simulator** on Unity designed for AI Game Master
play via MCP. AI VTuber (e.g. Open-LLM-VTuber) plays the Game Master role;
Twitch audience votes on choices (extension) — relieves streamer of having to
read aloud / control all mechanics.

> "ลดความเหนื่อยของสตรีมเมอร์ที่ต้องอ่านออกเสียง/คุมกลไกเกมเองตลอด"

## 2. Reference Games

| Game | What we borrow |
|---|---|
| 龙胤立志传 / The Scroll of Taiwu | World map, cities/villages/sects, time system, ingredient quality → result grade |
| Rimworld | Real-time with pause, speed control |
| Amazing Cultivation Simulator (ACS) | Combat pacing (resolves faster than Rimworld, good for AI decision loops) |

## 3. Core Loop (current MVP shape)

```
[Sect State] → [Event Trigger] → [Choices] → [Decision] →
[Consequences] → [Check End] → loop
```

**Status**: deliberately not finalized — waiting for gameplay systems to
solidify. Current implementation:
- Event triggered every 15s (weighted random from Luban pool)
- Auto-pause if `requiresDecision: true`
- Decision via UI or `execute_decision` MCP tool
- `DecisionExecutor` applies consequence
- TimeSystem unpauses

## 4. Design Pillars (inferred)

1. **AI-Playable First** — every mechanic must be queryable/controllable
   through MCP, no "human intuition" shortcuts
2. **Decisions Are Checkpoints** — auto-pause on important events
3. **Persistent, Compounding State** — choices have lasting consequences
4. **Modular Subsystems** — VContainer + MessagePipe

## 5. Two-Currency Economy

| Currency | Earned from | Used for |
|---|---|---|
| **Spirit Stones (หินวิญญาณ)** | Missions, adventures, demon-slaying, monthly stipend by rank, can be robbed | Item exchange, general spending |
| **Contribution (ค่าคุณูปการ)** | Output-based: gathering, crafting (higher grade = more), combat/missions | Buying from sect store (manuals, pills, items) |

**Raw resources** (provisions, herb, wood, ore) belong to the **sect** as
common pool. Disciples draw from it to craft.

## 6. Crafted Item Ownership Rule

| Disciple rank | Crafted item goes to |
|---|---|
| Outer / Inner Disciple | **Sect Stockpile** (other disciples buy with contribution) |
| Elder+ | **Personal inventory** (can be sold/traded privately) |

Implemented in `Assets/Scripts/Systems/SectStateProvider.cs:155` —
`TickCrafting()` checks `disciple.Rank >= DiscipleRank.Elder`.

## 7. World Design

- **Map**: cities / villages / sects, each with gathering nodes
- **Time**: real-time, pausable, speed-adjustable (Rimworld/ACS style),
  auto-pause on important events
- **Disciples**: applicants arrive → Outer Disciple → player assigns task →
  rank up (positions unlocked by buildings)
- **Buildings**: click-only construction, no placement on map
  (intentional: keeps AI loop simple)

## 8. Still To Decide

- Combat system (ACS-style vs Rimworld-style)
- Detailed stat system
- Techniques / manuals
- Items / weapons / armor / elixirs (detailed)
- Sect war (WIP)
- Spirit stone robbery between disciples (not in schema yet)

## Related Pages

- [[sources/mechanics|Mechanics]] — per-system design
- [[sources/architecture|Architecture]] — how the systems are wired
- [[sources/open-questions|Open Questions]] — what's not decided
- [[sources/devlog-history|DevLog History]] — what was built
