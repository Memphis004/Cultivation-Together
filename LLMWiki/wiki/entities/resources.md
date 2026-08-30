---
title: Resources
type: entity
sources:
  - UnityProject/Assets/Scripts/Shared/MockSectData.cs
  - UnityProject/Assets/Scripts/Systems/SectStateProvider.cs
related:
  - "[[concepts/gathering-system]]"
  - "[[concepts/crafting-system]]"
  - "[[concepts/state-management]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [resource, raw, stockpile, herb, wood, ore, provisions]
---

# Resources (Raw Materials)

> Raw resources in the sect stockpile. Used for crafting and consumed by
> world event consequences.

## Schema

Stored as `Dictionary<string, int>` in `SectStockpile.RawResources`:

```csharp
SectStockpile {
    RawResources: Dictionary<string, int>  // resource id → count
    CraftedGoods: List<InventoryItem>      // see [[items]]
}
```

## Known Resources (4)

| ID | Source | Rate | Notes |
|---|---|---|---|
| `herb` | `gathering_herb` task | 0.2/s | Used for elixirs |
| `wood` | `gathering_wood` task | 0.2/s | Used for forging |
| `ore` | `gathering_ore` task | 0.15/s | Used for forging (slower gathering) |
| `provisions` | `gathering_provisions` task | 0.25/s | Used for events, food |

## Initial Stockpile (from `MockSectData.Create()`)

| Resource | Amount |
|---|---|
| herb | 120 |
| wood | 340 |
| ore | 88 |
| provisions | 260 |

Sufficient for ~2-3 `refining_elixir` and 1-2 `forging_artifact` at start.

## Why a Dictionary (Not a Fixed Class)

Per `project_summary.md` design rationale:

> `SectStockpile` (raw resources เป็น `map<string,int32>` เผื่อขยายชนิด
> ทรัพยากรทีหลังโดยไม่ต้องแก้ schema)

Adding a new resource type requires:
1. Add a row to `GatheringRates` in `SectStateProvider`
2. (Optional) Add initial amount in `MockSectData`
3. (If crafting consumes it) Update recipe `Costs`
4. (Optional) Add to `ApplyDecisionConsequence` for events

No schema migration needed. Dictionary key is the contract.

## How Resources Change

All mutations go through `AdjustAndNotify()` (single choke point in
`SectStateProvider.cs:259`):

```csharp
private void AdjustAndNotify(Dictionary<string, int> resources, string key, int delta)
{
    var before = resources.TryGetValue(key, out var b) ? b : 0;
    Adjust(resources, key, delta);
    var after = resources.TryGetValue(key, out var a) ? a : 0;

    _resourceChangedPublisher.Publish(new SectResourceChangedMessage {
        ResourceId = key,
        Delta = after - before,  // ACTUAL applied delta, not requested
        NewTotal = after,
    });
}
```

**Clamped at 0** — subtracting more than available only removes what exists:

```csharp
private static void Adjust(Dictionary<string, int> resources, string key, int delta)
{
    var current = resources.TryGetValue(key, out var value) ? value : 0;
    resources[key] = Math.Max(0, current + delta);
}
```

**Publishing the actual delta** is critical — UI shows "−20" not "−50" when
stockpile was only 20.

## UI Display

`ResourceHudView` (top bar) shows all 4 resources with:
- Current amount (TMP text)
- Green "+N" indicator on `SectResourceChangedMessage` (delta > 0)
- Red "−N" indicator on `SectResourceChangedMessage` (delta < 0)

## ⏳ TODO

- Resource tiers (basic/intermediate/advanced herb, etc.)
- Tool bonuses (better sickle → faster herb gathering)
- Storage limits (sects with bigger storehouses hold more)
- Spoilage (provisions decay over time?)

## Related Pages

- [[concepts/gathering-system|Gathering System]]
- [[concepts/crafting-system|Crafting System]]
- [[concepts/state-management|State Management]]
- [[entities/items|Items]]
