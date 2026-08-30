---
title: EventPopupPresenter.cs
type: snippet
sources:
  - UnityProject/Assets/Scripts/UI/Presenters/EventPopupPresenter.cs
related:
  - "[[concepts/mvp-ui]]"
  - "[[concepts/decision-pipeline]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [ui, event-popup, presenter, decision]
---

# EventPopupPresenter.cs

> Presenter for the EventPopup panel. The most important UI flow — choice
> clicks feed the decision pipeline.
> **File path**: `UnityProject/Assets/Scripts/UI/Presenters/EventPopupPresenter.cs` (61 lines)

## Purpose

- Receives `EventPopupOpenArgs` (event id, description, choices)
- Builds choice buttons on the view
- On click: calls `DecisionExecutor.Execute(eventId, choiceId)` **directly**
  (NOT through pub/sub — see lab 13 bug)

## Code (full)

```csharp
using System.Collections.Generic;
using System.Linq;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.UI
{
    public class EventPopupOpenArgs
    {
        public string EventId;
        public string Description;
        public List<EventChoiceInfo> Choices;
    }

    public class EventPopupPresenter : UIPresenter<EventPopupView>
    {
        // Fixed from the original spec draft: this must call DecisionExecutor
        // directly (in-process), not publish through IPublisher<ExecuteDecisionMessage>.
        // DecisionLogger only listens on IDistributedSubscriber<string,
        // ExecuteDecisionMessage> (the interprocess/bridge channel) - a plain
        // in-memory IPublisher<ExecuteDecisionMessage> publish would go to a
        // completely different, unlistened channel and silently do nothing.
        private readonly DecisionExecutor _decisionExecutor;

        private string _currentEventId;

        public EventPopupPresenter(DecisionExecutor decisionExecutor)
        {
            _decisionExecutor = decisionExecutor;
        }

        protected override void OnViewBound()
        {
            View.ChoiceClicked += OnChoiceClicked;
        }

        public override void OnOpen(object args)
        {
            var openArgs = args as EventPopupOpenArgs;
            if (openArgs == null) return;

            _currentEventId = openArgs.EventId;
            View.SetEvent(openArgs.EventId, openArgs.Description);

            var choices = (openArgs.Choices ?? new List<EventChoiceInfo>())
                .Select(c => new EventChoiceViewData { ChoiceId = c.ChoiceId, Label = c.Label })
                .ToList();
            View.SetChoices(choices);
        }

        private void OnChoiceClicked(string choiceId)
        {
            _decisionExecutor.Execute(_currentEventId, choiceId);
            View.Hide();
        }

        public override void Dispose()
        {
            View.ChoiceClicked -= OnChoiceClicked;
        }
    }
}
```

## Critical Pattern — Why Direct Call, Not Pub/Sub

From the file's comment block (lines 16-21):

> Fixed from the original spec draft: this must call DecisionExecutor
> directly (in-process), not publish through IPublisher<ExecuteDecisionMessage>.
> DecisionLogger only listens on IDistributedSubscriber<string,
> ExecuteDecisionMessage> (the interprocess/bridge channel) - a plain
> in-memory IPublisher<ExecuteDecisionMessage> publish would go to a
> completely different, unlistened channel and silently do nothing.

**Lesson**: in-process pub/sub is for events with no in-Unity listener
beyond the publisher. Anything that triggers another in-Unity system should
be a direct method call. Pub/sub is for cross-process.

## Flow

```
User clicks choice button
  → EventPopupView fires ChoiceClicked event
  → EventPopupPresenter.OnChoiceClicked(choiceId)
    → DecisionExecutor.Execute(eventId, choiceId)
      → ISectStateProvider.ApplyDecisionConsequence(eventId, choiceId)
      → TimeSystem.SetPaused(false)         ← unpause
    → View.Hide()                            ← close popup
```

## Why Hide() After Execute()

The decision is committed (state mutated) and the game is unpaused. The user
expects the popup to close. If `Execute` throws, the popup stays open
(uncaught exception bubbles up, no `Hide()` called) — user can try again.

## Related Pages

- [[concepts/mvp-ui|MVP UI Pattern]]
- [[concepts/decision-pipeline|Decision Pipeline]]
- [[sources/bug-log|Bug Log]] (BUG-L13-01)
