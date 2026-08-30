---
title: TimeSystem.cs
type: snippet
sources:
  - UnityProject/Assets/Scripts/Core/TimeSystem.cs
related:
  - "[[concepts/time-system]]"
  - "[[concepts/decision-pipeline]]"
  - "[[concepts/state-management]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [time, pause, request-response, await]
---

# TimeSystem.cs

> Game clock + the awaitable event hook. Hosts `AwaitWorldEventHandler` and
> `SectStateQueryHandler` for the interprocess bus.
> **File path**: `UnityProject/Assets/Scripts/Core/TimeSystem.cs` (188 lines)

## Purpose

- Game clock: pause + speed (no real time advancement yet — `Tick()` is a no-op)
- Auto-pause on `requiresDecision` events
- Holds a `UniTaskCompletionSource` for `await_next_world_event` RPC
- Hosts 2 of the 3 interprocess request handlers (`AwaitWorldEventHandler`,
  `SectStateQueryHandler`)

## Public API

| Member | Returns | Description |
|---|---|---|
| `SetPaused(bool)` | `void` | Pause/resume, publishes `TimeSpeedChangedMessage` |
| `SetSpeed(int)` | `void` | Change speed, publishes `TimeSpeedChangedMessage` |
| `IsPaused` | `bool` | Public so other systems (e.g. `WorldEventSystem`) can skip ticks |
| `Tick()` | `void` | Called every frame (VContainer `ITickable`); no-op if paused |
| `RaiseWorldEvent(eventId, description, requiresDecision, choices)` | `void` | Called by `WorldEventSystem`; auto-pauses if decision needed |
| `WaitForNextWorldEventAsync()` | `UniTask<AwaitWorldEventResponse>` | Called by `AwaitWorldEventHandler` to bridge async event source |

## Code — Key Sections

### Auto-pause on decision event

```csharp
public void RaiseWorldEvent(string eventId, string description, bool requiresDecision, List<EventChoiceInfo> choices)
{
    Debug.Log($"[TimeSystem] World event raised: {eventId} (requiresDecision={requiresDecision})");

    if (requiresDecision) SetPaused(true);

    _worldEventPublisher.Publish(new WorldEventTriggeredMessage {
        EventId = eventId,
        RequiresDecision = requiresDecision,
        Description = description,
        Choices = choices ?? new List<EventChoiceInfo>(),
    });

    var response = new AwaitWorldEventResponse {
        EventId = eventId,
        RequiresDecision = requiresDecision,
        Description = description,
        Choices = choices ?? new List<EventChoiceInfo>(),
    };

    if (requiresDecision) {
        _cachedPendingEvent = response;  // For late bridge callers
    }

    // Complete the previous awaiter (if any) and create a new TCS
    var previous = _pendingEventSource;
    _pendingEventSource = new UniTaskCompletionSource<AwaitWorldEventResponse>();
    previous.TrySetResult(response);
}
```

**Two outputs of one event**:
1. `_worldEventPublisher.Publish(...)` — in-memory for any Unity-side listener
   (e.g. `WorldEventUISystem` → opens `EventPopup`)
2. `previous.TrySetResult(response)` — completes the request-response for
   bridge-side `await_next_world_event`

### Cached pending event for late callers

```csharp
public UniTask<AwaitWorldEventResponse> WaitForNextWorldEventAsync()
{
    if (_cachedPendingEvent != null) {
        var cached = _cachedPendingEvent;
        _cachedPendingEvent = null;
        return UniTask.FromResult(cached);
    }
    return _pendingEventSource.Task;
}
```

**Why**: If a `requiresDecision` event fires before the bridge connects (or
before it calls `await_next_world_event`), don't make it wait for a brand new
event. Cache once, hand out on next call, then clear.

### Request handlers (separate classes in same file)

```csharp
public class AwaitWorldEventHandler : IAsyncRequestHandler<AwaitWorldEventRequest, AwaitWorldEventResponse>
{
    private readonly TimeSystem _timeSystem;
    public AwaitWorldEventHandler(TimeSystem timeSystem) { _timeSystem = timeSystem; }
    public UniTask<AwaitWorldEventResponse> InvokeAsync(AwaitWorldEventRequest request, CancellationToken cancellationToken = default)
    {
        return _timeSystem.WaitForNextWorldEventAsync();
    }
}

public class SectStateQueryHandler : IAsyncRequestHandler<SectStateQuery, SectStateSnapshot>
{
    private readonly ISectStateProvider _stateProvider;
    public SectStateQueryHandler(ISectStateProvider stateProvider) { _stateProvider = stateProvider; }
    public UniTask<SectStateSnapshot> InvokeAsync(SectStateQuery request, CancellationToken cancellationToken = default)
    {
        var state = _stateProvider.BuildSectEconomyState();
        var snapshot = new SectStateSnapshot {
            RequestId = request.RequestId,
            EconomyStateBytes = state.ToByteArray()  // MessagePack
        };
        return UniTask.FromResult(snapshot);
    }
}

public interface ISectStateProvider
{
    SectEconomyState BuildSectEconomyState();
    void ApplyDecisionConsequence(string eventId, string choiceId);
    void TickGathering(float deltaTimeSeconds);
    void TickCrafting(float deltaTimeSeconds);
    void RecruitOuterDisciple();
    PurchaseItemResponse TryPurchaseItem(string discipleId, string itemDefId, int grade, int quantity);
}
```

**Pattern**: thin handler that delegates to the actual system. Handler doesn't
know about state structure — it just calls the right method on the right system.

## Key Patterns

1. **`UniTaskCompletionSource`** — bridges callback-based event sources to
   `async/await` for the interprocess RPC
2. **Auto-pause on decision** — `RaiseWorldEvent` decides; no caller
   responsibility
3. **Cached pending event** — handles race between event firing and bridge
   connecting
4. **`IsPaused` is public** — other tickable systems check it to skip work

## Known Issues

- ⏳ `Tick()` does nothing — no actual time advancement, just pause control
- ⏳ No "fast forward to next event" — speed control doesn't affect event interval

## Related Pages

- [[concepts/time-system|Time System]]
- [[concepts/decision-pipeline|Decision Pipeline]]
- [[concepts/mcp-bridge|MCP Bridge]]
- [[sources/architecture|Architecture]]
