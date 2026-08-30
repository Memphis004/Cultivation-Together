---
title: Items
type: entity
sources:
  - UnityProject/Assets/Scripts/Shared/SectEconomyState.cs
  - UnityProject/Assets/Scripts/Shared/MockSectData.cs
  - UnityProject/Assets/Scripts/Systems/SectStateProvider.cs
related:
  - "[[concepts/crafting-system]]"
  - "[[concepts/purchase-store]]"
  - "[[entities/resources|Resources]]
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [item, crafted, inventory, ownership, grade]
---

# Items (Crafted Goods)

> Items produced by the crafting system. Live in either the sect stockpile
> (buyable by other disciples) or in a disciple's personal inventory.

## Schema

```csharp
InventoryItem {
    ItemDefId: string    // e.g. "elixir_qi_gathering", "sword_azure_flame"
    Quantity: int        // 1+ (stacks in stockpile)
    Grade: int           // 1-5
    OwnerScope: enum     // Personal | SectStockpile
}
```

## OwnerScope

```csharp
enum OwnerScope {
    Unspecified,
    Personal,
    SectStockpile
}
```

Where an item lives determines who can interact with it:
- **SectStockpile**: visible to all disciples, buyable with Contribution
- **Personal**: only the owning disciple, not tradeable via the store

## Current Items (2)

| ItemDefId | Grade | Task | Time | Cost |
|---|---|---|---|---|
| `elixir_qi_gathering` | 3 | `refining_elixir` | 20s | herb × 10 |
| `sword_azure_flame` | 5 | `forging_artifact` | 30s | ore × 15, wood × 10 |

## Ownership Rule (Crafting)

From `SectStateProvider.TickCrafting()`:

```csharp
OwnerScope = disciple.Rank >= DiscipleRank.Elder
    ? OwnerScope.Personal    // Elder+: own the craft
    : OwnerScope.SectStockpile  // Outer/Inner: craft for the sect
```

So:
- Su Yan (Inner) refines elixir → goes to `Stockpile.CraftedGoods`
- Elder Zhao forges sword → goes to his `PersonalInventory`

## Stockpile Merge

When adding a sect-owned item, merge into existing stack if same
`ItemDefId + Grade + OwnerScope`:

```csharp
var existing = _state.Stockpile.CraftedGoods
    .FirstOrDefault(g => g.ItemDefId == item.ItemDefId
                       && g.Grade == item.Grade
                       && g.OwnerScope == item.OwnerScope);
if (existing != null) existing.Quantity += item.Quantity;
else _state.Stockpile.CraftedGoods.Add(item);
```

So stockpile shows: `4 × elixir_qi_gathering (grade 3)`, not 4 separate rows.

## Initial Stockpile (from `MockSectData.Create()`)

| Item | Grade | Qty | Owner |
|---|---|---|---|
| `elixir_qi_gathering` | 3 | 4 | SectStockpile |

Elder Zhao's personal inventory starts with:
- `sword_azure_flame` (grade 5) × 1

## Purchase (SectStore)

Disciples buy from `Stockpile.CraftedGoods` with their `Contribution` —
see [[concepts/purchase-store]]. Cost = `grade × 50` per unit.

## Item Effects (Not Yet Implemented)

Currently items have no gameplay effect beyond:
- Existing in inventory (cosmetic)
- Existing in stockpile (buyable)

⏳ **TODO when systems need them**:
- `elixir_qi_gathering` → boost cultivation speed?
- `sword_azure_flame` → combat damage multiplier?
- Equipment slots?
- Consumable usage UI?

## Adding a New Item

1. Add a `CraftingRecipes` entry in `SectStateProvider`
2. (Optional) Add initial stock in `MockSectData`
3. (Optional) Add to a `purchase_item` test or use the MCP tool to add

## Related Pages

- [[concepts/crafting-system|Crafting System]]
- [[concepts/purchase-store|Purchase Store]]
- [[entities/resources|Resources]]
- [[entities/disciples|Disciples]]
