---
title: Open Questions
type: design
sources:
  - ../../project_summary.md
related:
  - "[[sources/game-design-doc]]"
  - "[[sources/mechanics]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [open-questions, design, deferred]
---

# Open Questions

> Design decisions intentionally not yet made. When asked about these, do
> not assume — propose options with tradeoffs, let the human decide.

## 1. Final Core Loop

**Status**: deliberately not fixed (per project_summary.md)
**Why waiting**: need to finalize gameplay systems first
**Current MVP shape**:
```
[Sect State] → [Event Trigger] → [Choices] → [Decision] →
[Consequences] → [Check End] → loop
```
15s event interval, auto-pause on decisions, 1x default speed

**When asked**: ask what gameplay system they're working on first; core loop
will follow from those constraints

## 2. Combat System

**Status**: ❓ not decided
**Options on the table**:
- **ACS-style (Amazing Cultivation Simulator)**: faster resolve, fewer
  per-fight decisions, more encounters per session
- **Rimworld-style**: more granular, micro-positioning, slower resolve

**Project lean** (per project_summary.md): ACS-style — "resolve เร็วกว่า
เหมาะกับ AI ตัดสินใจ" (resolves faster, suitable for AI decision-making)

**When asked**: lean toward ACS-style, but ask if they want one round of
combat to be one decision or multiple

## 3. Stat System

**Status**: ❓ not decided
**Open**: what stats? cultivation-only (realm/HP/Qi), or RPG-style (STR/DEX/INT/etc.)?
**Lean**: keep it xianxia-flavored (realm + technique mastery, not D&D stats)

## 4. Techniques / Manuals

**Status**: ❓ not decided
**Open**: how does a disciple learn a technique? (item consumption, time-based study, teacher assignment?)

## 5. Items / Weapons / Armor / Elixirs (detailed)

**Status**: ❓ not decided
**Current**: only 2 placeholder items (`elixir_qi_gathering` grade 3,
`sword_azure_flame` grade 5)
**Open**: rarity tiers, set bonuses, durability, soul-binding, etc.

## 6. Sect War

**Status**: ⏳ WIP
**Open**: is this a real feature or aspirational? (not in current MVP scope)

## 7. Spirit Stone Robbery Between Disciples

**Status**: ❓ mentioned in design, not in schema
**Open**: is this gameplay-relevant or world-building? If gameplay, how is
it triggered (random event? betrayal arc?)

## 8. BuildingSystem

**Status**: ⏳ STUB (registered as entry point, no logic)
**Why deferred**: no game mechanic currently needs the unlock system
**When to resume**: when sect rank/position system needs to gate content

## 9. WorldEventSystem — State-Conditional Logic

**Status**: ⏳ random weighted pick only
**Open**: should events depend on current state? (low herb → more herb
discovery events, high money → more merchant events, etc.)
**When to resume**: when event variety feels low

## 10. Event Consequences & Shop Pricing — Balance

**Status**: ⏳ placeholders
**Current**:
- `bandit_raid_001` → -20 provisions
- `herb_garden_bloom` → +30 herb
- `wandering_merchant` → -20 ore, +40 provisions
- `new_disciple_applicant` (accept) → +1 outer disciple
- `purchase_item` → `grade × 50` Contribution

**When to resume**: after core loop is fixed and playtest starts

## 11. DiscipleList Panel

**Status**: ⏳ enum reserved, throws `NotImplementedException`
**Why deferred**: HUD was higher priority; roster view comes after
**When to resume**: after core gameplay feels complete

## 12. Dedicated WalletChangedMessage

**Status**: ⏳ `ResourceHudPresenter` piggybacks on `SectResourceChangedMessage`
**Tradeoff accepted**: works fine because all wallet-changing events also
change resources (e.g. `purchase_item` deducts contribution after checking
stock)

## 13. UIRoot.ApplyLayout() String Matching

**Status**: ⏳ works for 2 panels
**When to fix**: when panel count > 5; migrate to `IUIView.ApplyDefaultLayout()`

## 14. Save / Load System

**Status**: ❓ not designed
**Likely path**: serialize `SectEconomyState` via `ToByteArray()`
(MessagePack), write to `Application.persistentDataPath`
**No work on this until** the rest of the gameplay is stable

## When In Doubt

- **Don't assume** — these are deliberately unresolved
- **Propose with tradeoffs** when asked, e.g. "ACS-style would feel snappier
  for AI; Rimworld-style gives more player agency per fight. Which fits your
  design?"
- **Link to this page** when the question is in this list, so the user can
  remember they're tracked
