---
title: World Events
type: entity
sources:
  - DataTables/Data/event.xlsx
  - DataTables/Data/event_choice.xlsx
  - DataTables/Defines/worldevent.xml
related:
  - "[[concepts/world-events]]"
  - "[[concepts/decision-pipeline]]"
  - "[[concepts/data-pipeline]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [event, world-event, luban, design]
---

# World Events (Entity Catalog)

> The 4 currently-defined world events. Data lives in `DataTables/Data/`.
> See [[concepts/data-pipeline]] for how the data flows.

## Event Schema (Excel)

| Column | Type | Description |
|---|---|---|
| `id` | string | Unique event id, e.g. `bandit_raid_001` |
| `description` | string | Shown in UI popup and to AI GM |
| `weight` | float | Random selection weight (relative) |
| `requiresDecision` | bool | If true, auto-pause + present choices |

## Choice Schema (Excel)

| Column | Type | Description |
|---|---|---|
| `eventId` | string | FK to event id |
| `choiceId` | string | Unique per event, e.g. `send_inner_disciples` |
| `label` | string | Display text |

## Current Events (4)

### `bandit_raid_001`

- **Description**: (from Excel) Bandits raid the sect stores
- **Requires decision**: yes
- **Choices**:
  - `send_inner_disciples` — Send inner disciples to drive them off
  - `pay_them_off` — Pay them 20 provisions to leave
- **Consequence** (in `ApplyDecisionConsequence`):
  - Currently: `-20 provisions` regardless of choice
  - ⏳ TODO: differentiate by choice (should be `-5` for send, `-20` for pay, plus reputation)

### `new_disciple_applicant`

- **Description**: Someone wants to join the sect
- **Requires decision**: yes
- **Choices**:
  - `accept_<name>` — Accept the applicant
  - `reject_<name>` — Reject the applicant
- **Consequence**:
  - If `choiceId.ToLowerInvariant().Contains("accept")`:
    - `RecruitOuterDisciple()` — adds new outer disciple
  - Else: no-op (just log)

### `herb_garden_bloom`

- **Description**: The sect's herb garden unexpectedly blooms
- **Requires decision**: no (informational)
- **Choices**: none
- **Consequence**: `+30 herb`

### `wandering_merchant`

- **Description**: A merchant passes by offering to trade
- **Requires decision**: yes
- **Choices**:
  - `trade_ore_for_provisions` — Trade 20 ore for 40 provisions
  - `decline` — Don't trade
- **Consequence**:
  - Currently: `-20 ore, +40 provisions` regardless of choice
  - ⏳ TODO: differentiate by choice (decline = no change)

## Adding a New Event

1. Open `DataTables/Data/event.xlsx`
2. Add a row with id, description, weight, requiresDecision
3. Open `DataTables/Data/event_choice.xlsx`
4. Add 0+ choice rows referencing the eventId
5. Run `DataTables/gen.sh` (regenerates JSON + C# classes)
6. In `Unity`, refresh asset database (or restart)
7. In `SectStateProvider.ApplyDecisionConsequence`, add a case for the new
   event id (if it has consequences)

## Adding Consequence Logic

```csharp
case "my_new_event":
    AdjustAndNotify(_state.Stockpile.RawResources, "herb", 50);
    break;

case "new_disciple_promotion":
    if (choiceId == "promote_to_inner") {
        var disciple = _state.Disciples.FirstOrDefault(d => d.DiscipleId == someId);
        if (disciple != null) {
            disciple.Rank = DiscipleRank.InnerDisciple;
            _rankChangedPublisher.Publish(new DiscipleRankChangedMessage { ... });
        }
    }
    break;
```

## ⏳ Missing Features (Future)

- Choice-specific consequences (currently all choices in an event have the
  same effect)
- Event chains (event A triggers event B)
- State-conditional event selection (e.g. low resources → more merchant events)
- Cooldowns (no event fires twice in a row)

## Related Pages

- [[concepts/world-events|World Events (concept)]]
- [[concepts/decision-pipeline|Decision Pipeline]]
- [[concepts/data-pipeline|Data Pipeline (Luban)]]
