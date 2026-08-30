---
title: Cultivation Together — Project Overview
type: overview
sources:
  - ../../project_summary.md
  - ../../README.md
related:
  - "[[sources/game-design-doc]]"
  - "[[sources/architecture]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [project, vision, xianxia]
---

# Cultivation Together

> A xianxia sect-management simulator designed for AI Game Master play via MCP.
> Goal: let AI VTuber (e.g. Open-LLM-VTuber) play the Game Master role, so
> streamers can let the audience vote on sect decisions while AI handles narration.

## One-paragraph Pitch

Single-player xianxia (cultivation) simulator where you manage a sect of
disciples: gather resources, craft elixirs and artifacts, recruit and rank up
disciples, and respond to random world events. Designed from day one to be
playable by an AI agent via MCP — every game state is queryable, every
decision is an MCP tool call, so an AI VTuber can act as Game Master and a
Twitch audience can vote on what to do next.

## Core Pillars (inferred from design history)

1. **AI-Playable First** — every mechanic must be queryable and controllable
   through MCP, no "human intuition" shortcuts the AI can't reach
2. **Decisions Are Checkpoints** — auto-pause on important events so the AI
   GM / vote window has a clean decision point
3. **Persistent, Compounding State** — disciples, resources, ranks persist
   across sessions; choices have lasting consequences
4. **Modular Subsystems** — VContainer + MessagePipe = pluggable, testable,
   swappable

## Tech Stack (locked, see [[conventions]])

| Layer | Technology |
|---|---|
| Engine | Unity 6.3 (C# 8.0) |
| DI | VContainer |
| Internal bus | MessagePipe |
| IPC to MCP | MessagePipe.Interprocess over TCP |
| MCP bridge | .NET 8 console app (`McpBridge/`) |
| State serialization | MessagePack (protobuf dropped in lab 7) |
| Design-time data | Luban (Excel → JSON → C#) |
| UI | UGUI + TextMeshPro |
| Editor AI | Ivan Murzak Unity MCP |

## Game Loop (current MVP shape)

```
World tick (TimeSystem)
  → Every 15s: WorldEventSystem rolls an event from Luban pool
  → If event requires decision: auto-pause
  → Player/AI picks choice via UI or execute_decision MCP tool
  → DecisionExecutor applies consequence (mutates state)
  → TimeSystem unpauses
  → loop

Disciple subsystems (every frame):
  DiscipleSystem.Tick → TickGathering (adds raw resources)
  ResourceCraftingSystem.Tick → TickCrafting (consumes raw, produces items)
```

The final core loop is **deliberately not fixed yet** — waiting for gameplay
systems to solidify (see [[sources/open-questions]]).

## What's Implemented (v0.1 MVP)

- ✅ VContainer composition root + MessagePipe bus + TCP interprocess
- ✅ TimeSystem with pause/speed + auto-pause on decision events
- ✅ `get_sect_state` / `await_next_world_event` / `execute_decision` / `purchase_item` MCP tools (round-trip verified)
- ✅ `SectStateProvider` holds live state instance, mutated by decisions
- ✅ DiscipleSystem — passive gathering (4 gathering tasks, fractional accumulators)
- ✅ ResourceCraftingSystem — 2 recipes (refining_elixir, forging_artifact) with ownership rules
- ✅ `purchase_item` — disciples buy from sect stockpile with contribution
- ✅ WorldEventSystem — weighted random from Luban-generated pool
- ✅ MVP-Lite UI (EventPopup + ResourceHud) with proper subscribe-driven delta updates
- ✅ Luban pipeline for event/choice data (replaced ScriptableObject)

## What's Stubbed / Deferred

- ⏳ `BuildingSystem` — empty, deferred
- ⏳ `DiscipleListPresenter` panel — enum reserved, throws NotImplemented
- ⏳ Combat — system not picked (ACS-style vs Rimworld-style TBD)
- ⏳ Stat system, techniques, items detail
- ⏳ Sect wars (WIP)
- ⏳ World events are still weighted-random (no state-conditional logic)

## Key Files to Know

| Purpose | Path |
|---|---|
| Composition root | `Assets/Scripts/Core/GameLifetimeScope.cs` |
| Game clock + pause | `Assets/Scripts/Core/TimeSystem.cs` |
| State aggregator | `Assets/Scripts/Systems/SectStateProvider.cs` |
| World events | `Assets/Scripts/Systems/WorldEventSystem.cs` |
| Gathering loop | `Assets/Scripts/Systems/DiscipleSystem.cs` |
| Crafting loop | `Assets/Scripts/Systems/ResourceCraftingSystem.cs` |
| UI MVP base | `Assets/Scripts/UI/Core/UIPresenter.cs` + `UI/Views/UIViewBase.cs` |
| Event data | `Assets/Scripts/Data/LubanEventPool.cs` |
| Messages DTOs | `Assets/Scripts/Shared/GameMessages.cs` |
| Runtime state | `Assets/Scripts/Shared/SectEconomyState.cs` |
| MCP bridge | `McpBridge/Program.cs` |
| Design log | `../../project_summary.md` |

## Related Pages

- [[sources/game-design-doc|Game Design Document]] — full GDD
- [[sources/architecture|Architecture]] — how the subsystems fit together
- [[conventions|Conventions]] — workflow rules
- [[sources/devlog-history|DevLog History]] — 13 lab rounds, 21-30 Aug 2026
