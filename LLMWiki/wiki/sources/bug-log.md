---
title: Bug Log
type: bug-log
sources:
  - ../../project_summary.md
related:
  - "[[sources/devlog-history]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [bug, fix, debugging, mcp, messagepipe]
---

# Bug Log

> Real bugs found and fixed during 13 lab rounds (21-30 Aug 2026).
> Format: lab round, severity, symptom, root cause, fix, lesson.

---

## Lab 3 — Round-trip setup (5 bugs)

### [2026-08-21] BUG-L3-01: Duplicate 'Compile' items (NETSDK1022)

- **Severity**: critical (blocks build)
- **Symptom**: `NETSDK1022` build error
- **Root cause**: SDK-style csproj auto-includes all `.cs` files in the folder; manual `<Compile Include="Shared/*.cs"/>` was redundant
- **Fix**: Remove manual `<Compile Include>` entries; let SDK-style auto-include
- **Lesson**: Trust SDK-style csproj auto-include; don't manually list `.cs` files

### [2026-08-21] BUG-L3-02: `AddMessagePipeTcpInterprocess` doesn't exist

- **Severity**: critical
- **Symptom**: Compilation fails on the extension method
- **Root cause**: Real API is `services.AddMessagePipe()` (returns `IMessagePipeBuilder`), then call `.AddTcpInterprocess(...)` on the builder
- **Fix**: Chain on `IMessagePipeBuilder` instead of `IServiceCollection`
- **Lesson**: When a library has a builder pattern, use the builder — don't try to call leaf extensions directly

### [2026-08-21] BUG-L3-03: `RegisterTcpRemoteRequestHandler` missing on hub side

- **Severity**: critical (request-response silently broken)
- **Symptom**: `get_sect_state` returns nothing
- **Root cause**: `RegisterTcpRemoteRequestHandler` must be called on **both** sides, even the hub (`HostAsServer = true`) — it wires the TCP worker to the registered handler, not just a caller-side proxy
- **Fix**: Add to `GameLifetimeScope.Configure` (Unity side)
- **Lesson**: Read the actual reference code (Wanxiang.Guanxiangtai's `FrontendIpcServer.cs`) instead of guessing; symmetry assumed wrong
- **Related**: `RegisterTcpInterprocessMessageBroker` has same requirement (both sides)

### [2026-08-21] BUG-L3-04: `ValueTask<T>` vs `UniTask<T>` type mismatch

- **Severity**: critical
- **Symptom**: Compilation fails — handler return type doesn't match
- **Root cause**: MessagePipe VContainer integration uses `UniTask<T>` (Cysharp.Threading.Tasks), not standard `ValueTask<T>`
- **Fix**: All `IAsyncRequestHandler<,>` implementations return `UniTask<T>`
- **Lesson**: When using a custom async library, check the signature contract; don't assume BCL types

### [2026-08-21] BUG-L3-05: `economy.proto` not yet compiled

- **Severity**: critical (blocks `SectStateQueryHandler`)
- **Symptom**: `state.ToByteArray()` doesn't exist
- **Root cause**: `SectEconomyState.cs` is hand-written mirror, not protoc output
- **Fix (temporary)**: Added `[MessagePackObject]`/`[Key]` to `SectEconomyState`, hand-wrote `ToByteArray()`/`FromByteArray()` using MessagePack
- **Lesson**: When a pipeline isn't ready, use the simplest bridge that works; revisit later
- **Status**: Made permanent in lab 7 — dropped protobuf entirely, MessagePack is the real serializer

---

## Lab 6 — Library limit found

### [2026-08-22] BUG-L6-01: SocketException 10048 — TCP subscriber collision

- **Severity**: critical (blocks `await_next_world_event`)
- **Symptom**: `SocketException 10048 (Only one usage of each socket address is normally permitted)`
- **Root cause**: `TcpDistributedSubscriber<TKey,TMessage>` constructor unconditionally calls `worker.StartReceiver()` → `SocketTcpServer.Listen(host, port)`, **regardless of `HostAsServer` value**
  - Unity side: has guard (`Interlocked.Increment == 1`), safe because it IS the hub
  - Bridge side: tries to bind the same port Unity is already using → collision
- **Fix**: Converted `await_next_world_event` from pub/sub to request-response. Only `IPublisher`/`IRemoteRequestHandler` use `client.Value.Connect()` (not `Listen`), so no collision
- **Lesson**: Read the source of any networking library you depend on. Don't trust the API contract until you've verified the implementation behavior at boundaries you care about

### [2026-08-22] BUG-L6-02: Gotcha — `await_next_world_event` timeout after decision

- **Severity**: documentation, not a code bug
- **Symptom**: 60s timeout when calling `await_next_world_event` repeatedly without `execute_decision` in between
- **Root cause**: After `requiresDecision: true` event, `TimeSystem.IsPaused = true`. `WorldEventSystem.Tick()` skips while paused. So no new event fires until `execute_decision` → `DecisionLogger` → `SetPaused(false)`
- **Status**: By design, but needs to be documented in `get_sect_state` / `await_next_world_event` tool description
- **Lesson**: "By design" ≠ "no documentation needed"; document the contract explicitly

### [2026-08-22] BUG-L6-03: Gotcha — Unity Editor Pause button halts game

- **Severity**: documentation
- **Symptom**: 60s timeout on first `await_next_world_event` test
- **Root cause**: Unity Editor's Pause button (separate from `TimeSystem.IsPaused`) halts `Update()`/`Tick()`. If left engaged from a prior session, all tickable systems freeze
- **Fix**: User must remember to unpause Editor; in future may add a guard that warns on startup
- **Lesson**: User-facing tools (Editor) can simulate "no bug" symptoms; check all layers of state

---

## Lab 7 — Decision mutates state

### [2026-08-22] BUG-L7-01: State mutations invisible to next query

- **Severity**: critical (core game loop broken)
- **Symptom**: `execute_decision` logs that it ran, but `get_sect_state` immediately after shows unchanged state
- **Root cause**: `SectStateProvider.BuildSectEconomyState()` called `MockSectData.Create()` fresh on every query — built a brand new state each time
- **Fix**: Hold ONE live state instance for process lifetime (`private readonly SectEconomyState _state = MockSectData.Create();`)
- **Lesson**: When a system "owns" state, it must hold a reference, not build it. This is a classic factory vs. instance confusion

---

## Lab 8 — Gathering system

### [2026-08-23] BUG-L8-01: Resource numbers jump in bursts

- **Severity**: documentation/environment
- **Symptom**: Resources increment by ~5 every several seconds instead of smoothly
- **Root cause**: Unity Editor doesn't have `Run In Background` enabled by default. When switching to MCP Inspector browser, `Update()`/`Tick()` stop
- **Fix**: Enable `Edit > Project Settings > Player > Resolution and Presentation > Run In Background`
- **Lesson**: Editor settings that affect runtime behavior should be checked during initial setup, not at symptom time

---

## Lab 12 — Luban pipeline

### [2026-08-26] BUG-L12-01: `<module name="event">` — C# reserved keyword

- **Severity**: critical (compilation fails with chain of errors)
- **Symptom**: Cascading compilation errors after running Luban codegen
- **Root cause**: `event` is a C# reserved keyword; generated code uses `cfg.event.X` which compiler can't parse
- **Fix**: Rename module to `worldevent` (`cfg.worldevent.X`)
- **Lesson**: When picking names for any codegen target, check the target language's reserved words. Luban schema names propagate to C# namespaces

### [2026-08-26] BUG-L12-02: SimpleJSON namespace wrong

- **Severity**: medium (one-line fix, but user had to discover it)
- **Symptom**: `SimpleJSON` namespace not found
- **Root cause**: Actual namespace is `Luban.SimpleJSON`, not `SimpleJSON`
- **Fix**: Import `Luban.SimpleJSON`; use `JSON.Parse` (not `SimpleJSON.JSON.Parse`)
- **Lesson**: When a package bundles its own JSON library, the namespace is usually prefixed with the package name; don't assume the library's public name

### [2026-08-26] BUG-L12-03: Luban gen script "hangs" on first run

- **Severity**: perception, not a real bug
- **Symptom**: Only the banner shows for 30+ seconds, no other output
- **Root cause**: Cold start of Luban's embedded C# scripting engine is slow
- **Fix**: Wait longer; output will appear
- **Lesson**: When debugging "is it hung?", add a heartbeat log or measure baseline first run timing

---

## Lab 13 — Xianxia.UI.MVP Lite

### [2026-08-30] BUG-L13-01: EventPopup choice click is silent no-op

- **Severity**: critical (decision pipeline broken from UI side)
- **Symptom**: Click choice button in EventPopup → nothing happens (no log, no error, no state change)
- **Root cause**: `EventPopupPresenter` published via in-memory `IPublisher<ExecuteDecisionMessage>`, but `DecisionLogger` only subscribed on `IDistributedSubscriber<string, ExecuteDecisionMessage>` (interprocess channel). **Different graphs, no connection**
- **Fix**: Extracted `DecisionExecutor` (plain C# static helper). Both `EventPopupPresenter` (UI) and `DecisionLogger` (bridge) call it directly
- **Lesson**: **In-process pub/sub should only be used when the publisher has no in-Unity listener beyond itself.** Anything that needs to trigger another in-Unity system should be a direct method call. Pub/sub is for cross-process communication, not for in-process events that have an in-Unity consumer
- **Related concept**: [[concepts/decision-pipeline|Decision Pipeline]]

### [2026-08-30] BUG-L13-02: All UI lands at center-screen

- **Severity**: cosmetic
- **Symptom**: ResourceHud and EventPopup both appear at center-screen instead of top-bar / popup position
- **Root cause**: Two issues combined:
  1. `UIRoot` GameObject wasn't stretched to fill Canvas initially
  2. `ContentSizeFitter` set to `PreferredSize` — recomputes size every frame, overriding anchor stretch
- **Fix**:
  - `UIRoot.Awake()` enforces `RectTransform` stretch to full Canvas
  - `ContentSizeFitter` set to `Unconstrained`
  - `UIRoot.ApplyLayout()` (static helper) called from `UIService.Open()` for runtime-instantiated panels
- **Lesson**: uGUI layout has two competing forces (anchor stretch vs. size fitter). When you want "stretch to parent", explicitly set `ContentSizeFitter` to `Unconstrained`. The default `PreferredSize` will silently override

---

## Future Bug Watch List (Known Risks)

These are patterns that haven't broken yet but are likely to:

1. **WalletChangedMessage missing** — `WalletHudPresenter` refreshes wallet
   piggyback on `SectResourceChangedMessage`. Works only because
   `AdjustAndNotify()` fires on every relevant change. Adding a code path that
   mutates wallet without going through it will silently desync UI
2. ~~**`UIRoot.ApplyLayout()` string-matches prefab names**~~ — ✅ resolved
   25 Sep 2026: migrated to `UIViewBase.ApplyDefaultLayout()` overrides
   (lab 20); adding a panel no longer touches UIRoot
3. **Consequence rules hardcoded in `SectStateProvider.ApplyDecisionConsequence()`**
   — adding a new event requires code change. Move to data-driven when events
   exceed ~10
4. **`WorldEventSystem` random weighted pick** — no state-conditional logic.
   Adding condition (e.g. "low herb → more gathering events") requires
   touching the system

## Related Pages

- [[sources/devlog-history|DevLog History]]
- [[concepts/decision-pipeline|Decision Pipeline]]
- [[concepts/mcp-bridge|MCP Bridge]]
