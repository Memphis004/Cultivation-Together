---
title: Events
type: entity
sources:
  - DataTables/Data/event.xlsx
  - UnityProject/Assets/Scripts/Systems/WorldEventSystem.cs
related:
  - "[[concepts/world-events]]"
  - "[[entities/world-events|World Events (catalog)]]"
  - "[[concepts/decision-pipeline]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [events, game-events, world-events, timeline]
---

# Events

> Game event timeline. Includes world events (random), player actions
> (decisions, purchases, recruitments), and system events (resource ticks).

## Event Categories

### 1. World Events (random, NPC-driven)

See [[entities/world-events|World Events]] for the catalog. Fired by
`WorldEventSystem` every 15s.

| Event                    | Cadence  | Decision? |
| ------------------------ | -------- | --------- |
| `bandit_raid_001`        | weighted | yes       |
| `new_disciple_applicant` | weighted | yes       |
| `herb_garden_bloom`      | weighted | no        |
| `wandering_merchant`     | weighted | yes       |

### 2. Player Decision Events (input-driven)

Triggered by UI click or MCP `execute_decision`:

| Action | Source | Effect |
|---|---|---|
| `execute_decision(eventId, choiceId)` | Bridge | Calls `DecisionExecutor.Execute` |
| UI choice click | `EventPopupPresenter` | Same `DecisionExecutor.Execute` |

### 3. Player Purchase Events

| Action | Source | Effect |
|---|---|---|
| `purchase_item(...)` | Bridge | `SectStateProvider.TryPurchaseItem` |

### 4. System Tick Events (passive)

| System | Frequency | Effect |
|---|---|---|
| `DiscipleSystem.Tick` | Every frame | `TickGathering` — adds raw resources |
| `ResourceCraftingSystem.Tick` | Every frame | `TickCrafting` — produces items |
| `WorldEventSystem.Tick` | Every 15s | Triggers next world event |

## Event Sequence (Current MVP Loop)

```
t=0:     WorldEventSystem.RaiseInitialEvent()
         → first world event fires (weighted random)

t=event: User/AI picks choice
         → DecisionExecutor.Execute
         → State mutated
         → TimeSystem.SetPaused(false)
         → loop continues

t=event+15s: Next world event fires
         (only if previous was resolved)
```

## What Triggers What

```
User/AI choice click
  → EventPopupPresenter or DecisionLogger
  → DecisionExecutor.Execute
  → ISectStateProvider.ApplyDecisionConsequence
  → AdjustAndNotify (for resource changes)
  → ResourceHudPresenter refreshes on SectResourceChangedMessage
  → TimeSystem.SetPaused(false) (unpause)
  → WorldEventSystem.Tick resumes normal interval
```

```
purchase_item(discipleId, itemDefId, grade, qty)
  → McpBridge SectActionTools
  → PurchaseItemRequest (TCP)
  → PurchaseItemHandler (Unity, request handler)
  → SectStateProvider.TryPurchaseItem
  → Mutate: wallet -cost, stockpile -qty, inventory +item
  → Return PurchaseItemResponse (success/failure)
```

```
Every frame (VContainer ITickable)
  DiscipleSystem.Tick → TickGathering → AdjustAndNotify (per gathered unit)
  ResourceCraftingSystem.Tick → TickCrafting → consume resources, produce item
```

## State Events (publish but don't mutate)

| Event | Published when | Subscribers |
|---|---|---|
| `SectResourceChangedMessage` | Resource change | `ResourceHudPresenter` |
| `DiscipleRecruitedMessage` | New disciple added | (none yet, but available) |
| `DiscipleRankChangedMessage` | Rank change | (none yet) |
| `ContributionEarnedMessage` | Contribution gain | (none yet) |
| `WorldEventTriggeredMessage` | World event fires | `WorldEventUISystem` (opens EventPopup) |
| `TimeSpeedChangedMessage` | Speed/pause change | (none yet) |
| `ExecuteDecisionMessage` | Bridge → Unity decision | `DecisionLogger` |

## ⏳ Future Events (Planned)

- **Combat events**: `combat_started`, `combat_ended`, `combatant_killed`
- **Building events**: `building_constructed`, `position_unlocked`
- **Quest events**: `quest_started`, `quest_completed`, `quest_failed`
- **Sect war events**: `war_declared`, `battle_resolved`, `territory_lost`

## Related Pages

- [[concepts/world-events|World Events (concept)]]
- [[entities/world-events|World Events (catalog)]]
- [[concepts/decision-pipeline|Decision Pipeline]]
- [[concepts/state-management|State Management]]
