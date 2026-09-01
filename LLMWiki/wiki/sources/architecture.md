---
title: Architecture
type: architecture
sources:
  - ../../project_summary.md
  - ../../README.md
related:
  - "[[sources/game-design-doc]]"
  - "[[concepts/mcp-bridge]]"
  - "[[concepts/vcontainer-composition]]"
  - "[[concepts/message-pipe-bus]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [architecture, di, ipc, messagepipe]
---

# Architecture

> How the subsystems fit together. The single most important doc to read
> before touching any system.

## High-Level Diagram

```
┌─────────────────────────────────────────────────────────────┐
│  Unity (Host)                                               │
│                                                             │
│  ┌──────────────────┐    ┌──────────────────────────────┐   │
│  │  GameLifetimeScope│    │  MessagePipe Bus             │   │
│  │  (VContainer DI) │───▶│  - in-memory pub/sub         │   │
│  └──────────────────┘    │  - TCP interprocess to Bridge│   │
│           │              └──────────────────────────────┘   │
│           │                          ▲                      │
│           ▼                          │                      │
│  ┌──────────────────────────────────────────────┐           │
│  │  Subsystems (VContainer entry points)        │           │
│  │  - TimeSystem       (IStartable, ITickable)  │           │
│  │  - DiscipleSystem   (ITickable)              │           │
│  │  - ResourceCraftingSystem (ITickable)        │           │
│  │  - WorldEventSystem (ITickable)              │           │
│  │  - BuildingSystem   (ITickable, STUB)        │           │
│  │  - DecisionLogger   (IStartable)             │           │
│  │  - UIService        (Singleton)              │           │
│  └──────────────────────────────────────────────┘           │
│           │                                                  │
│           ▼                                                  │
│  ┌──────────────────────────────────────────────┐           │
│  │  ISectStateProvider (single live state)      │           │
│  │  - SectStateProvider                         │           │
│  │    - BuildSectEconomyState()                 │           │
│  │    - ApplyDecisionConsequence()              │           │
│  │    - TickGathering() / TickCrafting()        │           │
│  │    - RecruitOuterDisciple()                  │           │
│  │    - TryPurchaseItem()                       │           │
│  └──────────────────────────────────────────────┘           │
│           │                                                  │
│           ▼                                                  │
│  ┌──────────────────────────────────────────────┐           │
│  │  Data: LubanEventPool (Excel → JSON → C#)    │           │
│  │  Resources/DataTables/worldevent_*.json      │           │
│  └──────────────────────────────────────────────┘           │
│                                                             │
│  ┌──────────────────────────────────────────────┐           │
 │  │  UI (Xianxia.UI.MVP Lite — UGUI)             │           │
 │  │  - UIRoot (Canvas)                           │           │
 │  │  - UIPanelCatalog (ScriptableObject)         │           │
 │  │  - EventPopup  (View + Presenter)            │           │
 │  │  - ResourceHud (View + Presenter)            │           │
 │  │  - LogWindow   (View + Presenter)            │           │
 │  └──────────────────────────────────────────────┘           │
└─────────────────────────────────────────────────────────────┘
          │ TCP 127.0.0.1:3215 (MessagePipe.Interprocess)
          ▼
┌─────────────────────────────────────────────────────────────┐
│  McpBridge (.NET 8 console app)                             │
│  - IRemoteRequestHandler / IPublisher → MCP tools           │
│  - Tools: get_sect_state, await_next_world_event,           │
│           execute_decision, purchase_item                   │
│  - stdio → ModelContextProtocol SDK                         │
└─────────────────────────────────────────────────────────────┘
          │ stdio
          ▼
┌─────────────────────────────────────────────────────────────┐
│  AI GM Client (e.g. Open-LLM-VTuber)                        │
│  - Reads state, decides, calls tools                        │
└─────────────────────────────────────────────────────────────┘
```

## Composition Root

**File**: `Assets/Scripts/Core/GameLifetimeScope.cs`

Wires everything in `Configure(IContainerBuilder builder)`:

```csharp
// Data (Luban migration replacing ScriptableObject)
builder.RegisterInstance(new LubanEventPool());

// UI (Xianxia.UI.MVP Lite)
builder.RegisterInstance(uiRoot);
builder.RegisterInstance(uiPanelCatalog);
builder.Register<Xianxia.Sect.UI.UIService>(Lifetime.Singleton);
builder.Register<DecisionExecutor>(Lifetime.Singleton);
builder.Register<Xianxia.Sect.UI.EventPopupPresenter>(Lifetime.Transient);
builder.Register<Xianxia.Sect.UI.ResourceHudPresenter>(Lifetime.Transient);
builder.Register<Xianxia.Sect.UI.LogWindowPresenter>(Lifetime.Transient);
builder.Register<Xianxia.Sect.UI.AvatarCustomizationPresenter>(Lifetime.Transient);
builder.RegisterEntryPoint<Xianxia.Sect.UI.WorldEventUISystem>(Lifetime.Singleton);
builder.RegisterEntryPoint<Xianxia.Sect.UI.UIBootstrap>(Lifetime.Singleton);

// MessagePipe bus + TCP interprocess
var options = builder.RegisterMessagePipe(pipeOptions => { ... });
var messagePipeBuilder = builder.ToMessagePipeBuilder();
var interprocess = messagePipeBuilder.AddTcpInterprocess("127.0.0.1", 3215, ...);

// Interprocess-registered message brokers (pub/sub across process)
messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, DiscipleRecruitedMessage>(interprocess);
messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, SectResourceChangedMessage>(interprocess);
// ...

// Interprocess-registered request/response pairs
messagePipeBuilder.RegisterTcpRemoteRequestHandler<SectStateQuery, SectStateSnapshot>(interprocess);
messagePipeBuilder.RegisterTcpRemoteRequestHandler<AwaitWorldEventRequest, AwaitWorldEventResponse>(interprocess);

// Subsystems (entry points)
builder.RegisterEntryPoint<TimeSystem>(Lifetime.Singleton).AsSelf();
builder.RegisterEntryPoint<DiscipleSystem>(Lifetime.Singleton).AsSelf();
builder.RegisterEntryPoint<ResourceCraftingSystem>(Lifetime.Singleton).AsSelf();
builder.RegisterEntryPoint<WorldEventSystem>(Lifetime.Singleton).AsSelf();
builder.RegisterEntryPoint<DecisionLogger>(Lifetime.Singleton).AsSelf();

// State aggregator
builder.Register<ISectStateProvider, SectStateProvider>(Lifetime.Singleton);
```
> 📎 Source: Assets/Scripts/Core/GameLifetimeScope.cs

## Key Architectural Decisions (with rationale)

### Why VContainer (not Zenject)
- Lighter, faster, modern API
- Direct support for entry points (`IStartable`, `ITickable`) without writing bootstrap code

### Why MessagePipe (not UnityEvents / native C# events)
- Zero-alloc pub/sub
- Type-safe (no string-based event names)
- Built-in request-response
- Same API for in-memory and interprocess (TCP)
- VContainer integration built-in

### Why TCP Interprocess (not named pipes / shared memory)
- Standard, well-tested by MessagePipe team
- Cross-platform (Windows / macOS / Linux)
- Easy to debug with Wireshark if needed
- Unity = server, McpBridge = client (clear ownership)

### Why MessagePack (not Protobuf) — decided in lab 7
- Protobuf assumed "state must cross language boundaries" — never materialized
  since bridge is C# too
- MessagePack simpler, faster, no codegen pipeline
- Already in dependency graph (for other reasons)
- **Reversed**: dropped protobuf permanently, also dropped from Luban pipeline

### Why Luban (not ScriptableObject) — for event data
- Events are tabular (id, description, weight, requiresDecision) — perfect
  for Excel
- No clicking to create `.asset` files
- One source of truth in `DataTables/Data/event.xlsx`
- Version-controllable (Excel diffs in git work fine)

### Why UGUI (not UI Toolkit)
- Faster to wire up for MVP
- More mature ecosystem / tutorials
- UI Toolkit still in transition
- Future-migration possible if needed

### Why MVP-Lite (not MVVM, not pure View-MonoBehaviour)
- Clean separation: View = MonoBehaviour (Unity lifecycle), Presenter = plain
  C# (testable, fast)
- No reflection for panel resolution (explicit enum→Type mapping)
- Disposable subscriptions prevent memory leaks
- Reference design: CycloneGames UIFramework

### Why Per-Process Pub/Sub is BANNED inside Unity
**Critical lesson** (lab 13 bug fix): `EventPopupPresenter` originally
published via in-memory `IPublisher<ExecuteDecisionMessage>`, but
`DecisionLogger` only listened on `IDistributedSubscriber<string,
ExecuteDecisionMessage>` (the interprocess channel). Different graphs = silent
no-op when clicking choice buttons. **Fixed** by extracting `DecisionExecutor`
and having both paths call it directly.

```csharp
public class DecisionExecutor
{
    public void Execute(string eventId, string choiceId)
    {
        _stateProvider.ApplyDecisionConsequence(eventId, choiceId);
        _timeSystem.SetPaused(false);

        // Both DecisionLogger (bridge) and EventPopupPresenter (UI)
        // funnel through here
        _decisionExecutedPublisher.Publish(new DecisionExecutedMessage { EventId = eventId, ChoiceId = choiceId });
    }
}
```
> 📎 Source: Assets/Scripts/Core/DecisionExecutor.cs

> **Rule**: in-process pub/sub in Unity is for events that have no Unity-side
> listener beyond the publisher itself. Anything that needs to actually
> trigger another in-Unity system should be a direct method call.

## What Crosses the TCP Boundary

**Pub/Sub (fire-and-forget, bridge can listen but doesn't have to):**
- `DiscipleRecruitedMessage`
- `DiscipleRankChangedMessage`
- `SectResourceChangedMessage`
- `ContributionEarnedMessage`
- `ExecuteDecisionMessage` (bridge → Unity only)

**Request/Response (bridge asks, Unity answers):**
- `SectStateQuery` → `SectStateSnapshot` (full state snapshot)
- `AwaitWorldEventRequest` → `AwaitWorldEventResponse` (block until next event)
- `PurchaseItemRequest` → `PurchaseItemResponse` (atomic buy)

**Not registered on interprocess (in-memory only):**
- `TimeSpeedChangedMessage` (UI internal)
- `WorldEventTriggeredMessage` (UI internal — bridge uses request-response instead)

### Why `WorldEventTriggeredMessage` is NOT interprocess

From `GameMessages.cs:89-102` — confirmed by reading `TcpDistributedSubscriber`
source: it calls `worker.StartReceiver()` (binds its own listening socket)
**regardless of `HostAsServer`** value. So a non-hub process (bridge) can
never safely `IDistributedSubscriber` over this TCP transport — it would
collide with the hub's already-bound port. Solution: use request-response
(`AwaitWorldEventRequest`) which only ever uses the client connection
(`Connect`, not `Listen`).

## Implementation Details for AI Agent
- **Namespace:** `Xianxia.Sect` (สำหรับ Data Model), `Xianxia.Sect.UI` (สำหรับ Renderer/View)
- **File Location:** 
  - Data Model: `UnityProject/Assets/Scripts/Shared/SectEconomyState.cs` (แก้ไขไฟล์เดิม)
  - Renderer: `UnityProject/Assets/Scripts/UI/Views/AvatarRenderer.cs` (สร้างใหม่)
  - Def Loader: `UnityProject/Assets/Scripts/Data/AvatarPartPool.cs` (สร้างใหม่)

 │  ┌──────────────────────────────────────────────┐           │
 │  │  Data: AvatarPartPool (JSON → C#)            │           │
 │  │  Resources/DataTables/avatar_parts.json      │           │
 │  └──────────────────────────────────────────────┘           │
 │           │                                                  │
 │           ▼                                                  │
 │  ┌──────────────────────────────────────────────┐           │
 │  │  UI: AvatarRenderer (Per-Disciple GameObject)│           │
 │  │  - Resolves AvatarAppearance → Sprite Layers │           │
 │  └──────────────────────────────────────────────┘           │

## Workspace Layout (multi-project monorepo)

```
Cultivation Together/              ← workspace root
├── Shared/                        ← canonical source for shared types
│   ├── GameMessages.cs            ←   sync to Unity + Bridge
│   ├── SectEconomyState.cs
│   └── MockSectData.cs
├── UnityProject/                  ← open in Unity Hub
│   ├── Assets/Scripts/            ← all game C# logic
│   └── ...
├── McpBridge/                     ← .NET 8 console app
│   └── Program.cs                 ← MCP server
├── DataTables/                    ← Luban Excel sources
│   ├── Data/event.xlsx
│   ├── Data/event_choice.xlsx
│   └── Defines/worldevent.xml
├── Tools/Luban/                   ← Luban binary
├── LLMWiki/                       ← this folder
├── project_summary.md             ← THE design log (Thai)
├── README.md                      ← workspace overview
└── XIANXIA_UI_MVP_LITE_SPEC.md    ← UI spec
```

**Rule**: edit files in top-level `Shared/`, then run `./sync-shared.sh` to
copy into both `UnityProject/Assets/Scripts/Shared/` and `McpBridge/Shared/`.
Never edit the copies directly.


## Related Pages

- [[concepts/vcontainer-composition|VContainer Composition Root]]
- [[concepts/message-pipe-bus|MessagePipe Bus]]
- [[concepts/mcp-bridge|MCP Bridge]]
- [[concepts/decision-pipeline|Decision Pipeline]]
- [[concepts/mvp-ui|Xianxia.UI.MVP Lite]]
- [[concepts/data-pipeline|Luban Data Pipeline]]
