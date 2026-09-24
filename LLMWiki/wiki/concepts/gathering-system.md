---
title: Gathering System
type: concept
sources:
  - UnityProject/Assets/Scripts/Systems/DiscipleSystem.cs
  - UnityProject/Assets/Scripts/Systems/SectStateProvider.cs
related:
  - "[[concepts/state-management]]"
  - "[[concepts/crafting-system]]"
  - "[[entities/disciples]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [gathering, resources, passive, fractional-accumulator]
---

# Gathering System

> Passive resource production. Every disciple assigned a `gathering_*`
> CurrentTask produces raw resources every tick.

## Rates (placeholders)

| Task | Resource | Per second |
|---|---|---|
| `gathering_herb` | herb | 0.2 |
| `gathering_wood` | wood | 0.2 |
| `gathering_ore` | ore | 0.15 |
| `gathering_provisions` | provisions | 0.25 |

Defined in `SectStateProvider.GatheringRates` dict.

## Loop

```
DiscipleSystem.Tick() [every frame, VContainer ITickable]
  → _stateProvider.TickGathering(Time.deltaTime)
    → for each disciple:
      if CurrentTask is gathering_*: accumulate rate * dt into per-task accumulator
      if accumulator >= 1: AdjustAndNotify(whole units), keep fractional remainder
```

**Fractional accumulator per task** — multiple disciples on the same task
share one accumulator. This prevents losing sub-1 production between ticks.

Example: with 1 disciple on `gathering_herb` at 0.2/s:
- t=0.0: acc = 0
- t=4.99s: acc = 0.998 → no increment
- t=5.0s: acc = 1.0 → +1 herb, acc = 0
- t=10.0s: +1 herb, acc = 0
- (continuous, not bursts)

## Recruitment Auto-Assignment

`RecruitOuterDisciple()` assigns the new disciple to a gathering task using
round-robin:

```csharp
var index = _state.Disciples.Count;
var task = GatheringTasks[index % GatheringTasks.Length];
```

So disciples are spread across all 4 gathering tasks evenly. No task gets
starved of new recruits.

## What It Doesn't Do

- No skill levels (all disciples same rate)
- No tool bonuses (e.g. better sickle → faster herb gathering)
- No diminishing returns at high stockpile
- No time-of-day effects

These are all future work. The current implementation is intentionally
simple.

## How UI Sees Gathering

Every `+1 herb` etc. fires `SectResourceChangedMessage`. `WalletHudPresenter`
piggybacks a wallet refresh on it. The HUD no longer shows per-resource
stockpile values or delta indicators (wallet-only since 25 Sep 2026).

## Related Pages

- [[concepts/crafting-system|Crafting System]]
- [[concepts/state-management|State Management]]
- [[entities/disciples|Disciples]]
- [[entities/resources|Resources]]
