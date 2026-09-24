---
title: Log Window
type: concept
sources:
  - UnityProject/Assets/Scripts/UI/Views/LogWindowView.cs
  - UnityProject/Assets/Scripts/UI/Views/LogWindowPresenter.cs
  - Shared/GameMessages.cs
  - UnityProject/Assets/Scripts/Core/DecisionExecutor.cs
related:
  - "[[concepts/mvp-ui]]"
  - "[[concepts/message-pipe-bus]]"
  - "[[concepts/decision-pipeline]]"
  - "[[sources/architecture]]"
created: 2026-09-01
updated: 2026-09-01
confidence: high
tags: [ui, log, mvp, real-time, event-display]
---

# Log Window

> Real-time scrolling event log — the "narrative feed" for stream viewers.
> Shows story-beat events only (recruits, world events, decisions),
> deliberately excluding high-frequency ticks like resource changes.

## Purpose

A lightweight in-game event stream for streamer audiences. The log surfaces
**narrative-worthy moments** — new disciples joining, world events firing,
decisions being made — without flooding with raw numbers from
every-tick resource changes.

## Architecture (MVP Pattern)

```
MessagePipe (ISubscriber<T>)
    │
    ▼
LogWindowPresenter  ──►  LogWindowView.AddLine(text)
    │                         │
    │  [subscribe to           │  [instantiate TMP_Text under ScrollRect]
    │   DiscipleRecruited]     │  [cap at maxEntries (50)]
    │  [subscribe to           │  [auto-scroll to bottom]
    │   WorldEventTriggered]   │
    │  [subscribe to           │
    │   DecisionExecuted]      │
    │                          │
    ▼                          ▼
Plain C# (testable)    MonoBehaviour (Unity lifecycle)
```

## Subscribed Messages

| Message | Log Line Format | Why |
|---|---|---|
| `DiscipleRecruitedMessage` | `"A new disciple, {DisplayName}, has joined the sect."` | Core narrative beat |
| `WorldEventTriggeredMessage` | `"Event: {Description}"` | Key game moment |
| `DecisionExecutedMessage` | `"Decision made for '{EventId}': {ChoiceId}"` | Resolution of event |

**Deliberately excluded**: `SectResourceChangedMessage` — fires on every
gathering/crafting tick, would flood log with numbers instead of story.

## View Implementation

`LogWindowView` (`Assets/Scripts/UI/Views/LogWindowView.cs`):

- `RectTransform content` — parent container for entries
- `TMP_Text logEntryPrefab` — prefab instantiated per entry
- `ScrollRect scrollRect` — auto-scrolls to bottom on new entry
- `int maxEntries = 50` — oldest entries destroyed beyond this cap
- `Queue<GameObject> _entries` — tracks entries for FIFO cleanup

Key behavior: `Canvas.ForceUpdateCanvases()` before setting
`scrollRect.verticalNormalizedPosition = 0f` to ensure layout rebuilds
before snapping to bottom.

## Presenter Implementation

`LogWindowPresenter` (`Assets/Scripts/UI/Views/LogWindowPresenter.cs`):

- Extends `UIPresenter<LogWindowView>` (typed to the View)
- Subscribes in `OnViewBound()` (called once by `UIService.Open`)
- All subscriptions disposed in `Dispose()` — deterministic cleanup
- Initial line: `"Sect log started."` on view bound

## Bootstrap

`UIBootstrap.Start()` calls `_uiService.Open("LogWindow")` at game start,
making the log always-visible alongside `WalletHud`.

## Layout

`UIRoot.ApplyLogWindowLayout()` positions the panel at bottom-left:
- Anchor: `(0, 0)` / `(0, 0)` — bottom-left corner
- Size: `400×300` px
- Offset: `10px` from edges

## Data Flow — End to End

```
WorldEventSystem fires WorldEventTriggeredMessage
         │
         ├──► LogWindowPresenter → View.AddLine("Event: ...")
         │
         └──► WorldEventUISystem → UIService.Open("EventPopup")
                    │
                    └──► User clicks choice → EventPopupPresenter
                                │
                                └──► DecisionExecutor.Execute()
                                        │
                                        ├──► Publishes DecisionExecutedMessage
                                        │       │
                                        │       └──► LogWindowPresenter
                                        │             → View.AddLine("Decision made for ...")
                                        │
                                        └──► TimeSystem.SetPaused(false)
```

## How DecisionExecutedMessage Flows

The `DecisionExecutor` is the single funnel point for all decisions
(regardless of origin — bridge or in-game UI click). It publishes
`DecisionExecutedMessage` in-memory so the log window can react
regardless of where the decision came from.

```csharp
// DecisionExecutor.Execute() — the single source of truth
_publication.Publish(new DecisionExecutedMessage {
    EventId = eventId,
    ChoiceId = choiceId
});
```

## Adding a New Log Event

To subscribe to a new message type:

1. Add constructor parameter for `ISubscriber<NewMessage>`
2. Store as private field
3. Subscribe in `OnViewBound()`
4. Format a log line in the handler
5. Dispose in `Dispose()`

```csharp
// Example: subscribing to ContributionEarnedMessage
private readonly ISubscriber<ContributionEarnedMessage> _contributionSub;

// In constructor:
_contributionSub = contributionSubscriber;

// In OnViewBound():
_contributionSub = _contributionSubscriber.Subscribe(OnContributionEarned);

private void OnContributionEarned(ContributionEarnedMessage msg)
    => View.AddLine($"{msg.DiscipleId} earned {msg.Amount} contribution: {msg.Reason}");
```

## Anti-Patterns

- ❌ Don't subscribe to high-frequency messages (resource ticks) — flood
- ❌ Don't put game logic in the View — only display
- ❌ Don't forget to dispose subscriptions — memory leak
- ❌ Don't use `Find`/`FindObjectOfType` — use VContainer DI

## Related Pages

- [[concepts/mvp-ui|MVP UI Pattern]] — base classes and panel lifecycle
- [[concepts/message-pipe-bus|MessagePipe Bus]] — the pub/sub backbone
- [[concepts/decision-pipeline|Decision Pipeline]] — how decisions reach the log
- [[sources/architecture|Architecture]] — system-level wiring
