---
title: Decision Pipeline
type: concept
sources:
  - UnityProject/Assets/Scripts/Core/DecisionExecutor.cs
  - UnityProject/Assets/Scripts/Core/DecisionLogger.cs
  - UnityProject/Assets/Scripts/UI/Presenters/EventPopupPresenter.cs
  - UnityProject/Assets/Scripts/Core/TimeSystem.cs
related:
  - "[[concepts/world-events]]"
  - "[[concepts/state-management]]"
  - "[[concepts/mcp-bridge]]"
  - "[[concepts/mvp-ui]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [decision, pipeline, executor, consequence]
---

# Decision Pipeline

> The full bridge ↔ Unity decision loop. The most important mechanic of the
> game. Auto-pause on important events, atomic decision application.

## Full Flow

```
┌─────────────────────────────────────────────────────────────────────┐
│ 1. EVENT FIRES                                                      │
│    WorldEventSystem.RaiseFromPool()                                 │
│      → TimeSystem.RaiseWorldEvent(eventId, desc, requiresDec, ...)  │
│        ├─ if requiresDecision: SetPaused(true)                      │
│        ├─ _worldEventPublisher.Publish(WorldEventTriggeredMessage) │
│        │    [in-memory only, Unity-side listeners only]             │
│        ├─ _pendingEventSource.TrySetResult(response)                │
│        │    [completes await_next_world_event RPC]                  │
│        └─ _cachedPendingEvent = response                           │
│             [for late bridge callers]                               │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 2. UI SIDE (in Unity)                                               │
│    WorldEventUISystem subscribes to WorldEventTriggeredMessage      │
│      → if requiresDecision: UIService.Open(EventPopup)              │
│        → EventPopupView.Show()                                      │
│        → User clicks choice button                                  │
│          → View fires ChoiceClicked event                           │
│            → EventPopupPresenter.OnChoiceClicked(choiceId)          │
│              → DecisionExecutor.Execute(eventId, choiceId)          │
│                ├─ ISectStateProvider.ApplyDecisionConsequence(...)  │
│                └─ TimeSystem.SetPaused(false)                       │
│              → View.Hide()                                          │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 3. BRIDGE SIDE                                                      │
│    Bridge subscribed to AwaitWorldEventResponse (RPC)               │
│      → AI GM receives event with choices                            │
│        → AI GM calls execute_decision(eventId, choiceId)            │
│          → Bridge publishes ExecuteDecisionMessage over TCP         │
│            → Unity: DecisionLogger.OnDecisionReceived                │
│              [IDistributedSubscriber, interprocess broker]          │
│              → DecisionExecutor.Execute(...) [SAME call as UI]      │
│                ├─ ISectStateProvider.ApplyDecisionConsequence(...)  │
│                └─ TimeSystem.SetPaused(false)                       │
└─────────────────────────────────────────────────────────────────────┘
```

## Why `DecisionExecutor` is the Single Entry Point

From `EventPopupPresenter.cs:16-21` (comment):

> Fixed from the original spec draft: this must call DecisionExecutor
> directly (in-process), not publish through IPublisher<ExecuteDecisionMessage>.
> DecisionLogger only listens on IDistributedSubscriber<string,
> ExecuteDecisionMessage> (the interprocess/bridge channel) - a plain
> in-memory IPublisher<ExecuteDecisionMessage> publish would go to a
> completely different, unlistened channel and silently do nothing.

**Two paths converge to the same executor**, ensuring identical state
mutation regardless of input source (UI vs bridge).

## `DecisionExecutor` (canonical)

```csharp
public class DecisionExecutor
{
    private readonly ISectStateProvider _stateProvider;
    private readonly TimeSystem _timeSystem;

    public DecisionExecutor(ISectStateProvider stateProvider, TimeSystem timeSystem)
    {
        _stateProvider = stateProvider;
        _timeSystem = timeSystem;
    }

    public void Execute(string eventId, string choiceId)
    {
        _stateProvider.ApplyDecisionConsequence(eventId, choiceId);
        _timeSystem.SetPaused(false);
    }
}
```

## `ApplyDecisionConsequence` — Placeholder Rules

From `SectStateProvider.cs:208-245`. Hardcoded for the 4 current events:

| Event | Consequence |
|---|---|
| `bandit_raid_001` | `-20 provisions` |
| `herb_garden_bloom` | `+30 herb` |
| `wandering_merchant` | `-20 ore, +40 provisions` |
| `new_disciple_applicant` (accept) | `RecruitOuterDisciple()` |
| `new_disciple_applicant` (reject) | No-op |
| *(unknown event)* | Log warning, no state change |

⏳ **TODO**: move to data-driven when events exceed ~10.

## Why Auto-Pause

From `TimeSystem.cs:91-92` (comment):

> requiresDecision auto-pauses - this is the "checkpoint" the AI GM / vote
> window waits on, and stays paused until execute_decision is called.

The pause creates a clear, observable point where:
- The game state is stable
- The event is visible (UI popup OR bridge response)
- The decision can be made deliberately
- All systems stop mutating state until the decision is in

## Anti-Patterns Fixed in Lab 13

❌ Original spec: `EventPopupPresenter` publishes via in-memory
`IPublisher<ExecuteDecisionMessage>`, `DecisionLogger` listens via
interprocess `IDistributedSubscriber`. **Different graphs** → silent no-op.

✅ Fix: both call `DecisionExecutor.Execute(...)` directly.

## Related Pages

- [[concepts/world-events|World Events]]
- [[concepts/state-management|State Management]]
- [[concepts/time-system|Time System]]
- [[concepts/mvp-ui|MVP UI Pattern]]
- [[sources/bug-log|Bug Log]] (BUG-L13-01)
