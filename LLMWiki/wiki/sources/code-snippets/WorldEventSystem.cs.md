---
title: WorldEventSystem.cs
type: snippet
sources:
  - UnityProject/Assets/Scripts/Systems/WorldEventSystem.cs
related:
  - "[[concepts/world-events]]"
  - "[[concepts/data-pipeline]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [world-events, luban, randomness, tickable]
---

# WorldEventSystem.cs

> Triggers random world events from the Luban-generated event pool.
> The "AI director" of the game (very simple, weighted-random only).
> **File path**: `UnityProject/Assets/Scripts/Systems/WorldEventSystem.cs` (62 lines)

## Purpose

- Every 15 seconds (while not paused), pick a weighted-random event from the
  Luban event pool
- Pass event + choices to `TimeSystem.RaiseWorldEvent`
- Fire one event immediately on startup (no waiting 15s)
- Skip ticks while `TimeSystem.IsPaused` (a decision is outstanding)

## Public API

| Member | Returns | Description |
|---|---|---|
| `Tick()` | `void` | Called by VContainer every frame; respects pause |
| `RaiseInitialEvent()` | private | Fires one event in constructor |
| `RaiseFromPool()` | private | Pulls random event from pool, calls `TimeSystem.RaiseWorldEvent` |

## Dependencies

- `TimeSystem` (injected) — calls `RaiseWorldEvent` and checks `IsPaused`
- `LubanEventPool` (injected) — `GetRandomEvent()`, `GetChoices(eventId)`

## Constants

```csharp
private const float IntervalSeconds = 15f;  // Changed from 30s in lab 6
```

## Code

```csharp
using System.Linq;
using UnityEngine;
using VContainer.Unity;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    // Picks events from Luban-generated data (LubanEventPool) instead of a
    // hardcoded string array or hand-created ScriptableObject assets. See
    // Assets/Scripts/Data/LubanEventPool.cs and DataTables/ at the workspace
    // root for the actual event/choice source data.
    public class WorldEventSystem : ITickable
    {
        private const float IntervalSeconds = 15f;

        private readonly TimeSystem _timeSystem;
        private readonly LubanEventPool _eventPool;
        private float _timer;

        public WorldEventSystem(TimeSystem timeSystem, LubanEventPool eventPool)
        {
            _timeSystem = timeSystem;
            _eventPool = eventPool;

            // Fire one event immediately on startup instead of making the
            // first test always wait a full IntervalSeconds.
            RaiseInitialEvent();
        }

        private void RaiseInitialEvent()
        {
            if (_timeSystem.IsPaused) return;
            RaiseFromPool();
        }

        public void Tick()
        {
            // Already waiting on a decision (TimeSystem paused itself when
            // it last raised an event) - don't pile up more events. Nothing
            // fires again until execute_decision unpauses.
            if (_timeSystem.IsPaused) return;

            _timer += Time.deltaTime;
            if (_timer < IntervalSeconds) return;

            _timer = 0f;
            RaiseFromPool();
        }

        private void RaiseFromPool()
        {
            var eventRow = _eventPool.GetRandomEvent();
            if (eventRow == null) return; // LubanEventPool already logged why

            var choices = _eventPool.GetChoices(eventRow.Id)
                .Select(c => new EventChoiceInfo { ChoiceId = c.ChoiceId, Label = c.Label })
                .ToList();

            _timeSystem.RaiseWorldEvent(eventRow.Id, eventRow.Description, eventRow.RequiresDecision, choices);
        }
    }
}
```

## Key Patterns

1. **Event source = data, not code** — pulls from `LubanEventPool` (Excel→JSON→C#)
2. **Weighted random pick** — `LubanEventPool.GetRandomEvent()` uses `Weight` column
3. **Auto-pause respect** — `if (_timeSystem.IsPaused) return` prevents event pile-up
4. **Initial event on startup** — convenient for testing, no 15s wait
5. **Choices as `EventChoiceInfo` list** — projected from `EventChoiceRow` DTOs

## What's NOT in this file

- No state-conditional logic (e.g. low resources → specific event types)
- No event cooldowns
- No event chains
- No event priority by urgency

## Known Issues

- ⏳ Random weighted only — see [[sources/open-questions]] #9
- ⏳ Currently 4 events in the pool (from `Data/event.xlsx`); adding more
  requires editing the Excel file and re-running `gen.sh`

## Related Pages

- [[concepts/world-events|World Events]]
- [[concepts/data-pipeline|Data Pipeline (Luban)]]
- [[concepts/decision-pipeline|Decision Pipeline]]
- [[concepts/time-system|Time System]]
