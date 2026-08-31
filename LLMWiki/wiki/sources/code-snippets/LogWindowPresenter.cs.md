---
title: LogWindowPresenter.cs
type: snippet
source: UnityProject/Assets/Scripts/UI/Views/LogWindowPresenter.cs
related:
  - "[[concepts/log-window]]"
  - "[[concepts/mvp-ui]]"
  - "[[concepts/decision-pipeline]]"
  - "[[concepts/message-pipe-bus]]"
created: 2026-09-01
updated: 2026-09-01
confidence: high
tags: [ui, presenter, log, messagepipe, mvp]
---

# LogWindowPresenter.cs

> Presenter for the scrolling log window. Subscribes to narrative-worthy
> MessagePipe events and formats each into a log line.

**Path**: `UnityProject/Assets/Scripts/UI/Views/LogWindowPresenter.cs`
**Namespace**: `Xianxia.Sect.UI`

## Public API

| Member | Signature | Notes |
|---|---|---|
| Constructor | `LogWindowPresenter(ISubscriber<DiscipleRecruitedMessage>, ISubscriber<WorldEventTriggeredMessage>, ISubscriber<DecisionExecutedMessage>)` | All three MessagePipe subscribers injected via VContainer |
| `Bind` | inherited from `UIPresenter<LogWindowView>` | Called once by `UIService.Open()` |
| `OnViewBound` | `protected override void OnViewBound()` | Subscribes to all three message types |
| `Dispose` | `public override void Dispose()` | Unsubscribes all three message subscriptions |

## Dependencies

| Dependency | Purpose |
|---|---|
| `ISubscriber<DiscipleRecruitedMessage>` | New disciple joined |
| `ISubscriber<WorldEventTriggeredMessage>` | Random world event fired |
| `ISubscriber<DecisionExecutedMessage>` | Decision finalized (any source) |
| `LogWindowView` | The View (scrolling TMP text log) |

## Code

```csharp
using System;
using MessagePipe;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.UI
{
    // Scope for v1: narrative-worthy events only (recruit, world event,
    // decision made) - deliberately NOT SectResourceChangedMessage, which
    // fires on every gathering/crafting tick and would flood the log with
    // numbers instead of the story beats a stream audience actually cares
    // about. Add a subscription here later if resource-level detail turns
    // out to be wanted too.
    public class LogWindowPresenter : UIPresenter<LogWindowView>
    {
        private readonly ISubscriber<DiscipleRecruitedMessage> _discipleRecruitedSubscriber;
        private readonly ISubscriber<WorldEventTriggeredMessage> _worldEventSubscriber;
        private readonly ISubscriber<DecisionExecutedMessage> _decisionExecutedSubscriber;

        private IDisposable _discipleSub;
        private IDisposable _eventSub;
        private IDisposable _decisionSub;

        public LogWindowPresenter(
            ISubscriber<DiscipleRecruitedMessage> discipleRecruitedSubscriber,
            ISubscriber<WorldEventTriggeredMessage> worldEventSubscriber,
            ISubscriber<DecisionExecutedMessage> decisionExecutedSubscriber)
        {
            _discipleRecruitedSubscriber = discipleRecruitedSubscriber;
            _worldEventSubscriber = worldEventSubscriber;
            _decisionExecutedSubscriber = decisionExecutedSubscriber;
        }

        protected override void OnViewBound()
        {
            _discipleSub = _discipleRecruitedSubscriber.Subscribe(OnDiscipleRecruited);
            _eventSub = _worldEventSubscriber.Subscribe(OnWorldEvent);
            _decisionSub = _decisionExecutedSubscriber.Subscribe(OnDecisionExecuted);

            View.AddLine("Sect log started.");
        }

        private void OnDiscipleRecruited(DiscipleRecruitedMessage message)
        {
            View.AddLine($"A new disciple, {message.DisplayName}, has joined the sect.");
        }

        private void OnWorldEvent(WorldEventTriggeredMessage message)
        {
            View.AddLine($"Event: {message.Description}");
        }

        private void OnDecisionExecuted(DecisionExecutedMessage message)
        {
            View.AddLine($"Decision made for '{message.EventId}': {message.ChoiceId}");
        }

        public override void Dispose()
        {
            _discipleSub?.Dispose();
            _eventSub?.Dispose();
            _decisionSub?.Dispose();
        }
    }
}
```

## Known Issues

- None currently. See [[sources/bug-log]] for historical bugs.
