---
title: Mechanics
type: mechanics
sources:
  - ../../project_summary.md
related:
  - "[[concepts/gathering-system]]"
  - "[[concepts/crafting-system]]"
  - "[[concepts/purchase-store]]"
  - "[[concepts/world-events]]"
  - "[[concepts/time-system]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [mechanics, systems]
---

# Mechanics

> Per-system design. One section per implemented system.
> Status: ✅ = implemented in code, ⏳ = stubbed/deferred, ❓ = not yet decided

---

## 1. Time System — ✅

- **Real-time** with pause + speed control (Rimworld/ACS style)
- **Auto-pause** on `requiresDecision: true` world events
- Speed levels: 1x (default), can be increased
- Speed/pause changes published as `TimeSpeedChangedMessage` (in-memory only)

**Implementation**: `Assets/Scripts/Core/TimeSystem.cs`
- `SetPaused(bool)`, `SetSpeed(int)`
- `IsPaused` public so other systems (e.g. `WorldEventSystem`) can skip ticks

---

## 2. World Events — ✅ (weighted random, no state-conditional logic yet)

- Every **15 seconds** (configurable) while not paused
- Weighted random pick from `LubanEventPool.TbEvent` (Excel-driven)
- Currently 4 hardcoded events (see `LubanEventPool`):
  - `bandit_raid_001` — needs decision (-20 provisions)
  - `new_disciple_applicant` — needs decision (recruit or reject)
  - `herb_garden_bloom` — informational (+30 herb)
  - `wandering_merchant` — needs decision (trade)
- Initial event fires immediately on startup (no waiting 15s)
- After a `requiresDecision` event: `WorldEventSystem` skips ticks until
  `TimeSystem.IsPaused` is cleared by `DecisionLogger` after `execute_decision`

**Implementation**: `Assets/Scripts/Systems/WorldEventSystem.cs`

⏳ **TODO** (per project_summary.md): state-conditional logic (e.g. low-resource
sect gets more `wandering_merchant` events)

---

## 3. Decision Pipeline — ✅ (the most important mechanic)

The full bridge-driven decision loop:

```
WorldEventSystem.RaiseFromPool()
  → TimeSystem.RaiseWorldEvent(eventId, description, requiresDecision, choices)
    → If requiresDecision: TimeSystem.SetPaused(true)
    → _worldEventPublisher.Publish(WorldEventTriggeredMessage) [in-memory only]
    → _pendingEventSource.TrySetResult(response)  [completes await_next_world_event]
    → _cachedPendingEvent = response  [for late bridge callers]

UI side:
  WorldEventUISystem subscribes to WorldEventTriggeredMessage
    → if requiresDecision: UIService.Open(EventPopup)
      → User clicks choice → EventPopupPresenter.OnChoiceClicked(choiceId)
        → DecisionExecutor.Execute(eventId, choiceId)  [DIRECT call, not pub/sub]
          → ISectStateProvider.ApplyDecisionConsequence(eventId, choiceId)
          → TimeSystem.SetPaused(false)  [unpause]

Bridge side:
  await_next_world_event blocks on AwaitWorldEventResponse
  execute_decision publishes ExecuteDecisionMessage via interprocess
    → DecisionLogger.OnDecisionReceived (IDistributedSubscriber)
      → DecisionExecutor.Execute(...) [SAME call as UI path]
      → TimeSystem.SetPaused(false)
```

**Critical**: `DecisionExecutor` is the **single entry point** for decision
application. Both UI and bridge paths call it directly (NOT through pub/sub)
to avoid the silent-no-op bug fixed in lab 13.

**Implementation**:
- `Assets/Scripts/Core/DecisionExecutor.cs`
- `Assets/Scripts/Core/DecisionLogger.cs`
- `Assets/Scripts/UI/Presenters/EventPopupPresenter.cs`

---

## 4. Resource Gathering — ✅

- Disciples assigned a `gathering_*` `CurrentTask` passively produce raw
  resources every tick
- **Fractional accumulators** prevent losing sub-1 production between ticks
- Round-robin assignment when recruiting new outer disciples

| Task | Resource | Per second |
|---|---|---|
| `gathering_herb` | herb | 0.2 |
| `gathering_wood` | wood | 0.2 |
| `gathering_ore` | ore | 0.15 |
| `gathering_provisions` | provisions | 0.25 |

**Implementation**: `Assets/Scripts/Systems/DiscipleSystem.cs` + `SectStateProvider.TickGathering()`

---

## 5. Crafting — ✅

- 2 recipes implemented (placeholder balance, awaiting real design):

| Task | Item | Grade | Time | Cost |
|---|---|---|---|---|
| `refining_elixir` | elixir_qi_gathering | 3 | 20s | herb × 10 |
| `forging_artifact` | sword_azure_flame | 5 | 30s | ore × 15, wood × 10 |

- **All-or-nothing** consumption: if resources short, progress holds at 100%
  and waits (no time lost)
- **Ownership rule** (from economy design):
  - Outer/Inner Disciple → items go to **Sect Stockpile** (merge by item+grade)
  - Elder+ → items go to **disciple's Personal Inventory**

**Implementation**: `Assets/Scripts/Systems/ResourceCraftingSystem.cs` + `SectStateProvider.TickCrafting()`

---

## 6. Recruitment — ✅

- Triggered by `new_disciple_applicant` world event → `execute_decision` with
  `choiceId` containing "accept" (case-insensitive)
- Always creates an **Outer Disciple** (Inner/Elder assignment deferred)
- Round-robin `CurrentTask` from `GatheringTasks` list
- Round-robin name from `RecruitNamePool` placeholder
- ID format: `d001`, `d002`, ... (continues from existing roster)
- Publishes `DiscipleRecruitedMessage` (interprocess, so bridge can react)

**Implementation**: `Assets/Scripts/Systems/SectStateProvider.cs:177` — `RecruitOuterDisciple()`

⏳ **TODO**: Inner Disciple / Elder / Sect Master promotion flow

---

## 7. Purchase Store — ✅

- MCP tool: `purchase_item(discipleId, itemDefId, grade, quantity)`
- Disciple buys from `Stockpile.CraftedGoods` using their own Contribution
- Atomic: checks stock, checks contribution, deducts both, transfers item
- **Placeholder pricing**: `grade × 50` Contribution per unit

**Implementation**: `Assets/Scripts/Core/PurchaseItemHandler.cs` + `SectStateProvider.TryPurchaseItem()`

⏳ **TODO**: real pricing model (rarity, sect reputation, etc.)

---

## 8. Buildings — ⏳ STUB

`BuildingSystem` is registered as an entry point but has no gameplay logic.
Per project_summary.md: "deferred until needed" — building positions/permissions
unlocked by buildings currently have no in-game effect.

---

## 9. Combat — ❓

Per project_summary.md: "ยังไม่ตัดสินใจ" — debate is ACS-style (faster resolve)
vs Rimworld-style (more granular). Will decide after other systems solidify.

---

## 10. Saving / Persistence — ❓ (no dedicated system)

Runtime state held in memory by `SectStateProvider`. No save/load to disk yet.

⏳ **TODO** before launch: persistence layer (likely JSON serialization of
`SectEconomyState` via existing `ToByteArray()` MessagePack helper)

---

## 11. UI Panels — ✅ (MVP-Lite)

| Panel | View | Presenter | Subscribes to |
|---|---|---|---|
| EventPopup | `EventPopupView` | `EventPopupPresenter` | n/a (passes args directly) |
| WalletHud | `WalletHudView` | `WalletHudPresenter` | `SectResourceChangedMessage` |

⏳ **TODO**: DiscipleList panel (enum reserved, throws `NotImplementedException`)

---

## Cross-Cutting Concerns

### State Changes Trigger UI via Pub/Sub

Every state mutation goes through `AdjustAndNotify()` which publishes
`SectResourceChangedMessage` with the **actual applied delta** (post-clamp),
not the requested delta. UI panels subscribe and refresh on the event.

**File**: `Assets/Scripts/Systems/SectStateProvider.cs:259`

### Wallet Changes Piggyback on Resource Changes

No dedicated `WalletChangedMessage` exists. `WalletHudPresenter` refreshes
the wallet piggyback on every `SectResourceChangedMessage`. Acceptable
tradeoff until there's a real reason to add a dedicated message.

⏳ **TODO**: dedicated wallet-changed message when wallet-only events become common

### All-or-Nothing Resource Consumption

`TryConsume()` only deducts if every cost can be fully paid. Prevents partial
crafting that would leave the system in an inconsistent state.

**File**: `Assets/Scripts/Systems/SectStateProvider.cs:292`

## Related Pages

- [[sources/architecture|Architecture]]
- [[concepts/world-events|World Events]]
- [[concepts/decision-pipeline|Decision Pipeline]]
- [[concepts/gathering-system|Gathering System]]
- [[concepts/crafting-system|Crafting System]]
- [[entities/disciples|Disciples]]
- [[entities/world-events|World Events]]
- [[entities/resources|Resources]]
- [[entities/items|Items]]
