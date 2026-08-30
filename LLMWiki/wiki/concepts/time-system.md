---
title: Time System
type: concept
sources:
  - UnityProject/Assets/Scripts/Core/TimeSystem.cs
related:
  - "[[concepts/decision-pipeline]]"
  - "[[concepts/world-events]]"
  - "[[concepts/state-management]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [time, pause, speed, request-response]
---

# Time System

> Game clock + the awaitable event hook. Hosts the interprocess request
> handlers for state query and world event await.

## Responsibilities

1. **Pause/speed control** — set by game logic, exposed via `TimeSpeedChangedMessage`
2. **Auto-pause on decision events** — `RaiseWorldEvent` triggers this
3. **Awaitable world event** — bridge's `await_next_world_event` blocks until
   next event via `UniTaskCompletionSource`

## Why `IsPaused` is Public

Other tickable systems need to know if a decision is outstanding so they
don't pile up work:

```csharp
// WorldEventSystem.Tick()
if (_timeSystem.IsPaused) return;  // skip while waiting for decision
```

## Awaitable World Event (the tricky part)

Bridge side calls `await_next_world_event`. Unity needs to block the
response until a world event fires. Implementation:

```csharp
private UniTaskCompletionSource<AwaitWorldEventResponse> _pendingEventSource =
    new UniTaskCompletionSource<AwaitWorldEventResponse>();

private AwaitWorldEventResponse _cachedPendingEvent;

public UniTask<AwaitWorldEventResponse> WaitForNextWorldEventAsync()
{
    if (_cachedPendingEvent != null)
    {
        var cached = _cachedPendingEvent;
        _cachedPendingEvent = null;
        return UniTask.FromResult(cached);
    }
    return _pendingEventSource.Task;
}

public void RaiseWorldEvent(string eventId, string description, bool requiresDecision, List<EventChoiceInfo> choices)
{
    if (requiresDecision) SetPaused(true);

    // ... build response ...

    if (requiresDecision) _cachedPendingEvent = response;

    var previous = _pendingEventSource;
    _pendingEventSource = new UniTaskCompletionSource<AwaitWorldEventResponse>();
    previous.TrySetResult(response);  // complete the previous awaiter
}
```

**Why the swap**: every event replaces the TCS so the next call has a fresh
slot. The previous awaiter gets the response.

**Why the cache**: if a `requiresDecision` event fires before the bridge
calls `await_next_world_event` (or before it connects), don't make it wait
for a brand new event. Hand out the cached one immediately.

## `SetPaused` and `SetSpeed` Publish

```csharp
public void SetPaused(bool paused)
{
    _paused = paused;
    _speedPublisher.Publish(new TimeSpeedChangedMessage { Speed = _speed, Paused = _paused });
}
```

UI listeners (none yet) can react. The current 2 panels don't care about
speed/pause state.

## What's NOT Done

- ⏳ `Tick()` is a no-op — no actual time advancement, just pause control
- ⏳ No "fast forward to next event" — speed control doesn't affect event
  interval (the WorldEventSystem has its own 15s timer)

## Common Mistake: Editor Pause vs TimeSystem.IsPaused

Unity Editor has its own Pause button. If left engaged, **all** `Update()` /
`Tick()` calls halt, including `WorldEventSystem.Tick()`. The game appears
"frozen" but `TimeSystem.IsPaused` is still false.

From `project_summary.md` (lab 6 gotcha):
> Pause ของ Unity Editor เอง (คนละตัวกับ `TimeSystem.IsPaused` ในโค้ด) ถ้า
> ติดค้างไว้จะทำให้ `Update()`/`Tick()` ทั้งหมดหยุดทำงาน

**Fix**: user must remember to unpause Editor.

## Related Pages

- [[concepts/decision-pipeline|Decision Pipeline]]
- [[concepts/world-events|World Events]]
- [[concepts/state-management|State Management]]
