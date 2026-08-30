---
title: SectStateProvider.cs
type: snippet
sources:
  - UnityProject/Assets/Scripts/Systems/SectStateProvider.cs
related:
  - "[[concepts/state-management]]"
  - "[[concepts/gathering-system]]"
  - "[[concepts/crafting-system]]"
  - "[[concepts/purchase-store]]"
  - "[[concepts/decision-pipeline]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [state, economy, gathering, crafting, purchase, recruiting]
---

# SectStateProvider.cs

> The **single state aggregator** for the entire game. Holds the only live
> `SectEconomyState` instance. Every gameplay subsystem either reads from or
> mutates this object.
> **File path**: `UnityProject/Assets/Scripts/Systems/SectStateProvider.cs` (398 lines)

## Purpose

- Holds the live `SectEconomyState` for the process lifetime
- Provides query interface for `SectStateQueryHandler` (MCP `get_sect_state`)
- Provides mutation methods called by:
  - `DiscipleSystem.Tick()` → `TickGathering()`
  - `ResourceCraftingSystem.Tick()` → `TickCrafting()`
  - `DecisionExecutor` → `ApplyDecisionConsequence()`
  - `PurchaseItemHandler` (interprocess request) → `TryPurchaseItem()`
- Publishes events on every state change (UI + bridge can react)

## Public API

| Member | Returns | Description |
|---|---|---|
| `BuildSectEconomyState()` | `SectEconomyState` | Returns the live instance (NOT a copy — careful) |
| `ApplyDecisionConsequence(eventId, choiceId)` | `void` | Applies rules for a chosen event choice |
| `TickGathering(deltaSeconds)` | `void` | Called every frame from `DiscipleSystem` |
| `TickCrafting(deltaSeconds)` | `void` | Called every frame from `ResourceCraftingSystem` |
| `RecruitOuterDisciple()` | `void` | Adds a new outer disciple (round-robin task/name) |
| `TryPurchaseItem(discipleId, itemDefId, grade, qty)` | `PurchaseItemResponse` | Atomic check-and-deduct purchase |

## Constants (placeholders, awaiting real balance)

```csharp
// Gathering rates
GatheringRates = {
  "gathering_herb":         ("herb",         0.20 /s),
  "gathering_wood":         ("wood",         0.20 /s),
  "gathering_ore":          ("ore",          0.15 /s),
  "gathering_provisions":   ("provisions",   0.25 /s),
}

// Crafting recipes
"refining_elixir":  herb × 10 → elixir_qi_gathering (grade 3) in 20s
"forging_artifact": ore × 15 + wood × 10 → sword_azure_flame (grade 5) in 30s

// Pricing
ContributionPricePerGrade = 50  // price = grade * 50 * qty
```

## Dependencies

- `MessagePipe.IPublisher<DiscipleRecruitedMessage>`
- `MessagePipe.IPublisher<SectResourceChangedMessage>`
- `SectEconomyState` (from `Shared/`)
- `MockSectData.Create()` — initial state factory (for now)

## Code — Key Sections

### Constructor (one-time setup)

```csharp
private readonly SectEconomyState _state = MockSectData.Create();
private readonly IPublisher<DiscipleRecruitedMessage> _discipleRecruitedPublisher;
private readonly IPublisher<SectResourceChangedMessage> _resourceChangedPublisher;
private readonly Dictionary<string, float> _gatherAccumulators = new();
private readonly Dictionary<string, float> _craftProgress = new();

public SectStateProvider(
    IPublisher<DiscipleRecruitedMessage> discipleRecruitedPublisher,
    IPublisher<SectResourceChangedMessage> resourceChangedPublisher)
{
    _discipleRecruitedPublisher = discipleRecruitedPublisher;
    _resourceChangedPublisher = resourceChangedPublisher;
}
```

**Critical lesson** (lab 7): `_state` is created **once** in the field
initializer. Earlier versions called `MockSectData.Create()` on every
`BuildSectEconomyState()` call → mutations invisible. See
[[sources/bug-log]] BUG-L7-01.

### Gathering loop (called every frame)

```csharp
public void TickGathering(float deltaTimeSeconds)
{
    foreach (var disciple in _state.Disciples)
    {
        if (!GatheringRates.TryGetValue(disciple.CurrentTask, out var rate)) continue;

        var accKey = disciple.CurrentTask;
        var acc = _gatherAccumulators.TryGetValue(accKey, out var existing) ? existing : 0f;
        acc += rate.PerSecond * deltaTimeSeconds;

        var wholeUnits = Mathf.FloorToInt(acc);
        if (wholeUnits > 0)
        {
            AdjustAndNotify(_state.Stockpile.RawResources, rate.Resource, wholeUnits);
            acc -= wholeUnits;
            Debug.Log($"[SectStateProvider] Gathered +{wholeUnits} {rate.Resource} (task={accKey})");
        }

        _gatherAccumulators[accKey] = acc;
    }
}
```

**Pattern**: fractional accumulator per task — keeps sub-1 production across
ticks. Multiple disciples on the same task share one accumulator (not one
per disciple).

### Crafting loop (called every frame)

```csharp
public void TickCrafting(float deltaTimeSeconds)
{
    foreach (var disciple in _state.Disciples)
    {
        if (!CraftingRecipes.TryGetValue(disciple.CurrentTask, out var recipe)) continue;

        var progress = _craftProgress.TryGetValue(disciple.DiscipleId, out var existing) ? existing : 0f;
        progress += deltaTimeSeconds;

        if (progress < recipe.CraftSeconds) {
            _craftProgress[disciple.DiscipleId] = progress;
            continue;
        }

        if (!TryConsume(_state.Stockpile.RawResources, recipe.Costs))
        {
            // Hold at completion threshold, don't lose accumulated time
            _craftProgress[disciple.DiscipleId] = recipe.CraftSeconds;
            continue;
        }

        // ... consume resources, publish per-resource change events, build item ...

        var item = new InventoryItem
        {
            ItemDefId = recipe.ItemDefId,
            Quantity = 1,
            Grade = recipe.Grade,
            OwnerScope = disciple.Rank >= DiscipleRank.Elder
                ? OwnerScope.Personal
                : OwnerScope.SectStockpile,  // <-- ownership rule
        };

        if (item.OwnerScope == OwnerScope.Personal)
            disciple.PersonalInventory.Add(item);
        else
            AddToStockpileGoods(item);

        // Carry over any overshoot
        _craftProgress[disciple.DiscipleId] = progress - recipe.CraftSeconds;
    }
}
```

**Key patterns**:
- All-or-nothing consumption (`TryConsume`)
- Hold at 100% if resources short (don't lose progress)
- Per-disciple progress (not per-task, since two disciples can craft the same item)
- Ownership rule based on rank

### Recruiting (called from decision consequence)

```csharp
public void RecruitOuterDisciple()
{
    var index = _state.Disciples.Count;
    var task = GatheringTasks[index % GatheringTasks.Length];
    var name = RecruitNamePool[index % RecruitNamePool.Length];

    var disciple = new DiscipleState
    {
        DiscipleId = $"d{index + 1:000}",
        DisplayName = name,
        Rank = DiscipleRank.OuterDisciple,
        Wallet = new CurrencyWallet(),
        PersonalInventory = new List<InventoryItem>(),
        CurrentTask = task,
    };

    _state.Disciples.Add(disciple);
    _discipleRecruitedPublisher.Publish(new DiscipleRecruitedMessage
    {
        DiscipleId = disciple.DiscipleId,
        DisplayName = disciple.DisplayName,
    });

    Debug.Log($"[SectStateProvider] Recruited outer disciple: {disciple.DisplayName} ({disciple.DiscipleId}), assigned to {task}");
}
```

**Pattern**: round-robin assignment, ID auto-incremented from existing count.

### AdjustAndNotify — single choke point for state changes

```csharp
private void AdjustAndNotify(Dictionary<string, int> resources, string key, int delta)
{
    var before = resources.TryGetValue(key, out var b) ? b : 0;
    Adjust(resources, key, delta);
    var after = resources.TryGetValue(key, out var a) ? a : 0;

    _resourceChangedPublisher.Publish(new SectResourceChangedMessage
    {
        ResourceId = key,
        Delta = after - before,  // ACTUAL applied delta, not requested
        NewTotal = after,
    });
}
```

**Critical**: publishes the **actual applied delta** (post-clamp), not the
requested delta. When `Adjust()` clamps at 0, the two can differ (e.g.
requesting `-50` on a stock of `20` actually removes `20`).

### Purchase (atomic check-and-deduct)

```csharp
public PurchaseItemResponse TryPurchaseItem(string discipleId, string itemDefId, int grade, int quantity)
{
    if (quantity <= 0)
        return new PurchaseItemResponse { Success = false, Message = "Quantity must be positive." };

    var disciple = _state.Disciples.FirstOrDefault(d => d.DiscipleId == discipleId);
    if (disciple == null)
        return new PurchaseItemResponse { Success = false, Message = $"No disciple with id '{discipleId}'." };

    var stockEntry = _state.Stockpile.CraftedGoods.FirstOrDefault(g =>
        g.ItemDefId == itemDefId && g.Grade == grade && g.OwnerScope == OwnerScope.SectStockpile);

    var haveQuantity = stockEntry?.Quantity ?? 0;
    if (stockEntry == null || haveQuantity < quantity)
        return new PurchaseItemResponse { Success = false, Message = $"Not enough...", RemainingContribution = disciple.Wallet.Contribution };

    var cost = (long)grade * ContributionPricePerGrade * quantity;
    if (disciple.Wallet.Contribution < cost)
        return new PurchaseItemResponse { Success = false, Message = $"{disciple.DisplayName} needs {cost} contribution but only has {disciple.Wallet.Contribution}.", RemainingContribution = disciple.Wallet.Contribution };

    // All checks passed - mutate atomically
    disciple.Wallet.Contribution -= cost;
    stockEntry.Quantity -= quantity;
    if (stockEntry.Quantity <= 0)
        _state.Stockpile.CraftedGoods.Remove(stockEntry);

    disciple.PersonalInventory.Add(new InventoryItem { ... });

    return new PurchaseItemResponse { Success = true, Message = "..." };
}
```

**Pattern**: validate all → mutate all → return. No partial state on failure.

## Known Issues

- ⏳ `ApplyDecisionConsequence` hardcodes rules per event id — move to
  data-driven when events exceed ~10 (see [[sources/open-questions]])
- ⏳ Consequences for new events (e.g. >4) require code change

## Related Pages

- [[concepts/state-management|State Management]]
- [[concepts/decision-pipeline|Decision Pipeline]]
- [[concepts/gathering-system|Gathering System]]
- [[concepts/crafting-system|Crafting System]]
- [[concepts/purchase-store|Purchase Store]]
