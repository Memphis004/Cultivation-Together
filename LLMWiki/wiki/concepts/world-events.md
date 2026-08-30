---
title: World Events
type: concept
sources:
  - UnityProject/Assets/Scripts/Systems/WorldEventSystem.cs
  - UnityProject/Assets/Scripts/Data/LubanEventPool.cs
  - DataTables/Data/event.xlsx
related:
  - "[[concepts/decision-pipeline]]"
  - "[[concepts/data-pipeline]]"
  - "[[concepts/time-system]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [events, world, luban, randomness, ai-director]
---

# World Events

> Random events that interrupt the game loop. Currently weighted-random from
> a Luban-generated pool — the simplest possible "AI director".

## Event Flow

```
WorldEventSystem.Tick() (every frame)
  → if TimeSystem.IsPaused: skip (decision outstanding)
  → accumulate dt; every 15s: RaiseFromPool()
    → LubanEventPool.GetRandomEvent() (weighted random from Excel data)
    → LubanEventPool.GetChoices(eventId)
    → TimeSystem.RaiseWorldEvent(id, desc, requiresDecision, choices)
      → if requiresDecision: SetPaused(true)
      → in-memory Publish(WorldEventTriggeredMessage) [Unity-side listeners]
      → TrySetResult(AwaitWorldEventResponse) [bridge RPC completes]
      → if requiresDecision: cache for late bridge callers
```

## Current Events (4)

| Event | Decision? | Consequence |
|---|---|---|
| `bandit_raid_001` | yes | -20 provisions |
| `new_disciple_applicant` | yes | recruit or reject |
| `herb_garden_bloom` | no (informational) | +30 herb |
| `wandering_merchant` | yes | -20 ore, +40 provisions |

Data lives in `DataTables/Data/event.xlsx` and
`DataTables/Data/event_choice.xlsx`. See [[concepts/data-pipeline]].

## Adding a New Event

1. Open `DataTables/Data/event.xlsx`, add a row (id, description, weight, requiresDecision)
2. Open `DataTables/Data/event_choice.xlsx`, add choice rows (eventId, choiceId, label, ...)
3. Run `DataTables/gen.sh` (or `gen.bat` on Windows) to regenerate JSON + C# classes
4. Add a consequence rule in `SectStateProvider.ApplyDecisionConsequence()`:
   ```csharp
   case "my_new_event":
       AdjustAndNotify(resources, "herb", 50);
       break;
   ```
5. (Optional) If the event has new consequence types (e.g. disciple
   recruitment beyond outer), add the logic

## ⏳ What's Missing

From `[[sources/open-questions]]` #9:

- No state-conditional logic (e.g. low herb → more `herb_garden_bloom`)
- No event cooldowns
- No event chains
- No event priority/urgency

These can be added when event variety feels low or when AI GM needs more
sophisticated event selection.

## Event Source Evolution

| Lab | Source | Notes |
|---|---|---|
| 5 | Hardcoded string array in `WorldEventSystem` | 4 events baked in |
| 12 | Luban-generated (Excel → JSON → C#) | One source of truth, easy to extend |

The current implementation is **fully data-driven** — no code change needed
to add a new event, only the consequence rule needs code.

## Related Pages

- [[concepts/decision-pipeline|Decision Pipeline]]
- [[concepts/data-pipeline|Data Pipeline (Luban)]]
- [[concepts/time-system|Time System]]
- [[entities/world-events|World Events]]
