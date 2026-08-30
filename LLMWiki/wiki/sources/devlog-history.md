---
title: DevLog History
type: devlog
sources:
  - ../../project_summary.md
related:
  - "[[sources/architecture]]"
  - "[[sources/bug-log]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [devlog, history, labs]
---

# DevLog History

> Summary of 13 lab rounds (21-30 August 2026) from `project_summary.md`.
> Each lab was a focused work session with specific deliverables, bugs hit, and
> decisions made. For day-by-day work going forward, create new
> `devlog-YYYY-MM-DD.md` files.

## Lab Timeline

| # | Date | Topic | Key Outcome |
|---|---|---|---|
| 1 | ~Aug 21 | Proto schema + mock data | `economy.proto`, `SectEconomyState.cs`, `MockSectData.cs`, UI wireframe |
| 2 | Aug 21 | VContainer + MessagePipe wiring | `GameLifetimeScope.cs`, `TimeSystem.cs`, `McpBridge/` |
| 3 | Aug 21 | Debug round-trip | Found 5 real bugs (see [[sources/bug-log]]) |
| 4 | Aug 21 | Write path (bridge → Unity) | `ExecuteDecisionMessage`, `DecisionLogger` |
| 5 | Aug 22 | WorldEventSystem closes loop | Random event trigger every 30s, auto-pause |
| 6 | Aug 22 | Found library limit (TCP subscriber collision) | Switched `await_next_world_event` to request-response |
| 7 | Aug 22 | ExecuteDecision mutates state; **dropped protobuf** | Use MessagePack permanently |
| 8 | Aug 23 | DiscipleSystem real (recruit + gathering) | Passive loop, fractional accumulators |
| 9 | Aug 23 | ResourceCraftingSystem real | 2 recipes, ownership rules, all-or-nothing |
| 10 | Aug 24 | Purchase store MCP tool | `purchase_item`, atomic check-and-deduct |
| 11 | Aug 25 | UI basics (SectHud) | Top-bar HUD, personal wallet added |
| 12 | Aug 26 | EventData → Luban pipeline | Excel→JSON→C# replaces ScriptableObject |
| 13 | Aug 30 | Xianxia.UI.MVP Lite | EventPopup + ResourceHud, fixed silent-no-op bug |

## Lab 1 — Proto schema + mock data

**Files created**:
- `Shared/economy.proto` — `CurrencyWallet`, `InventoryItem` (with `OwnerScope` enum), `DiscipleState`, `SectStockpile` (`map<string,int32>` for extensibility), `SectEconomyState`
- `Shared/SectEconomyState.cs` — plain C# mirror
- `Shared/MockSectData.cs` — 3 disciples (outer/inner/elder) + stockpile
- `sect_ui_wireframe.html` — ink-paper-stamp aesthetic (avoiding AI-cliché palette)

**Decision**: ScriptableObject for design-time (RecipeDef, BuildingDef, ItemDef,
EventDef); Protobuf for runtime state (DiscipleState, SectEconomyState,
InventoryEntry). Runtime state references defs by ID, not by direct reference
(clean save/load + cross-process MCP).

## Lab 2 — VContainer + MessagePipe

**Files created**:
- `Shared/GameMessages.cs` — pub/sub DTOs (`DiscipleRecruitedMessage`, `WorldEventTriggeredMessage`) + request/response (`SectStateQuery`/`SectStateSnapshot`)
- `UnityProject/Assets/Scripts/Core/GameLifetimeScope.cs` — VContainer composition root
- `UnityProject/Assets/Scripts/Core/TimeSystem.cs` — example subsystem
- `McpBridge/Program.cs` — external .NET console app, stdio MCP server

**Workspace split**: 3 independent projects + 1 shared source folder, synced
via `sync-shared.sh`.

## Lab 3 — Debug round-trip (found 5 real bugs)

See [[sources/bug-log]] for details. Headlines:
1. `Duplicate 'Compile' items` (NETSDK1022) — SDK-style csproj auto-includes `.cs`
2. `AddMessagePipeTcpInterprocess` API name wrong — real API is on `IMessagePipeBuilder`
3. `RegisterTcpRemoteRequestHandler` must be called on **both** sides (even hub)
4. `ValueTask<T>` vs `UniTask<T>` — MessagePipe VContainer integration uses UniTask
5. `economy.proto` not yet compiled — added MessagePack serializer stub

**Round-trip verified 21 Aug 2026**: `get_sect_state` returns mock JSON
correctly through `Unity ↔ MessagePipe.Interprocess TCP ↔ McpBridge ↔ MCP
Inspector`, latency ~26-53ms.

## Lab 4 — Write path bridge → Unity

`execute_decision` was a stub. Now publishes `ExecuteDecisionMessage` via
interprocess, `DecisionLogger` subscribes (IDistributedSubscriber) and calls
`TimeSystem.SetPaused(false)`.

**Verified 22 Aug 2026**: `execute_decision(eventId=bandit_raid_001, choiceId=send_inner_disciples)` → log appears in Unity Console.

## Lab 5 — WorldEventSystem closes the loop

`await_next_world_event` was hanging (nothing called `RaiseWorldEvent`).
Added `WorldEventSystem` with random 30s interval, 4 hardcoded events,
respects `TimeSystem.IsPaused`.

## Lab 6 — Found library limit (TCP subscriber collision)

`await_next_world_event` failed with `SocketException 10048`. Read
`TcpDistributedSubscriber` source: it always binds its own listening socket,
ignoring `HostAsServer`. Bridge side can never safely use
`IDistributedSubscriber` over TCP — would collide with Unity's port.

**Fix**: Converted `await_next_world_event` to request-response (RPC),
keeps `WorldEventTriggeredMessage` for in-memory use only. Same pattern as
`get_sect_state` (already proven).

**Verified 22 Aug 2026**: `await_next_world_event` returns real events, no
hang. Full loop works.

**Gotcha**: After `await_next_world_event` returns a `requiresDecision: true`
event, `TimeSystem` stays paused. **Must** call `execute_decision` before
calling `await_next_world_event` again or it will time out.

## Lab 7 — Decision mutates state + dropped protobuf

`SectStateProvider.BuildSectEconomyState()` was calling
`MockSectData.Create()` fresh on every query — so mutations were invisible.

**Fix**: hold ONE live state instance for process lifetime. Added
`ApplyDecisionConsequence()` with placeholder rules per event id.

**Decision**: dropped protobuf permanently. Reasons:
- Original reason (cross-language bridge) moot since bridge is C# too
- MessagePack simpler, no codegen, already in dependency graph
- Renamed `EconomyStateProtobuf` → `EconomyStateBytes` to reflect reality

## Lab 8 — DiscipleSystem real

- `new_disciple_applicant` event: `RecruitOuterDisciple()` adds real outer
  disciple, round-robin `CurrentTask` and name, publishes
  `DiscipleRecruitedMessage`
- `TickGathering()`: passive resource production per task, fractional
  accumulators prevent losing sub-1 production

**Gotcha**: resource numbers jumped in bursts (not smooth) because **Unity
Editor doesn't have `Run In Background` enabled by default**. When switching
to MCP Inspector browser, `Update()`/`Tick()` stop. Enable in
`Edit > Project Settings > Player > Resolution and Presentation`.

## Lab 9 — ResourceCraftingSystem real

- 2 recipes (`refining_elixir`, `forging_artifact`)
- Ownership rule: Outer/Inner → Sect Stockpile (merge by item+grade), Elder+
  → Personal Inventory
- All-or-nothing consumption: progress holds at 100% if resources short

## Lab 10 — Purchase store

New MCP tool `purchase_item(discipleId, itemDefId, grade, quantity)`.
- Checks stock → checks contribution → deducts both → transfers item
- Placeholder pricing: `grade × 50` Contribution per unit
- Request-response pattern (atomic check)

## Lab 11 — UI basics

Added `SectHudView` (top bar HUD) showing stockpile with delta indicators
+ personal wallet. Added `MockSectData` entry for player as Sect Master
(d000, "Liu YiFeng", rank `SectMaster`) — original design had no player
entry.

**Gotcha**: `RegisterComponentInHierarchy` (not `RegisterEntryPoint`) for
components already in scene. Must create Canvas/TMP Text/wire references
in Editor — can't create scene objects from code.

## Lab 12 — Luban pipeline replaces ScriptableObject

Moved event definitions from `.asset` files to Excel → JSON → C#.

**Bugs hit**:
1. `<module name="event">` — `event` is C# reserved keyword, compiler
   exploded. Renamed module to `worldevent`.
2. `Luban.SimpleJSON` namespace, not `SimpleJSON` (had to verify by reading
   package source).
3. Luban gen script looks like it hangs on cold start (C# scripting engine
   warmup) — wait longer, not a bug.

Deleted: `EventData.cs`, `EventPool.cs` (ScriptableObject version)
Created: `LubanEventPool.cs` (plain C# wrapper around `cfg.Tables`),
`DataTables/Data/event.xlsx`, `DataTables/Defines/worldevent.xml`,
`DataTables/gen.sh`/`gen.bat`

## Lab 13 — Xianxia.UI.MVP Lite

Implemented per `XIANXIA_UI_MVP_LITE_SPEC.md`:
- View = MonoBehaviour (`UIViewBase`)
- Presenter = plain C# (`UIPresenter<TView>`)
- Service = `UIService` (resolves presenter by kind, instantiates prefab)
- Explicit enum→Type mapping (no assembly reflection)
- Disposable subscriptions

**Bug fixed in spec before implementation**:
`EventPopupPresenter` original spec used `IPublisher<ExecuteDecisionMessage>`
(in-memory), but `DecisionLogger` only listens on
`IDistributedSubscriber<string, ExecuteDecisionMessage>` (interprocess).
**Different graphs** — silent no-op. Fixed by extracting `DecisionExecutor`
that both call directly. → This is the canonical decision-application entry
point.

**Side changes outside `UI/`**:
- `GameMessages.cs` — added `Description`/`Choices` to `WorldEventTriggeredMessage`
- `TimeSystem.cs` — publish message with new fields
- `SectStateProvider.cs` — `AdjustAndNotify()` actually publishes
  `SectResourceChangedMessage` (was defined but never published)

**Decision**: `ResourceHud` panel REPLACES `SectHudView` (deleted). Delta
indicators via `SectResourceChangedMessage` subscription (post-clamp delta
included in message — no client-side diff needed).

**Gotcha hit at runtime**: All UI landed center-screen, not at intended
positions. Root cause: (1) `UIRoot` GameObject not stretched to Canvas,
(2) `ContentSizeFitter` set to `PreferredSize` recomputes every frame
overriding anchor stretch. Fixed: `UIRoot.Awake()` forces stretch,
`ContentSizeFitter` set to `Unconstrained`, `UIRoot.ApplyLayout()` called
from `UIService.Open()`.

**Verified 30 Aug 2026**: ResourceHud works first try. EventPopup needed
prefab/catalog wiring fix (try-catch in `WorldEventUISystem` caught it
cleanly). Full loop: `WorldEventSystem` → `EventPopup` opens → click
choice → `DecisionExecutor` → `SectStateProvider` logs "Recruited outer
disciple: Jiang Yu". **Proves lab 13 bug fix is correct, not just theory.**

## Where To Next (Decided Order)

1. ~~ResourceCraftingSystem~~ ✅ lab 9
2. ~~Purchase store~~ ✅ lab 10
3. ~~EventData → Luban~~ ✅ lab 12
4. ⏳ BuildingSystem (still stub)
5. ⏳ Combat system
6. ⏳ Inner/Elder promotion
7. ⏳ Save/load

## Related Pages

- [[sources/architecture|Architecture]]
- [[sources/bug-log|Bug Log]]
- [[sources/open-questions|Open Questions]]
