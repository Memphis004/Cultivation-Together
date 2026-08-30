---
title: Crafting System
type: concept
sources:
  - UnityProject/Assets/Scripts/Systems/ResourceCraftingSystem.cs
  - UnityProject/Assets/Scripts/Systems/SectStateProvider.cs
related:
  - "[[concepts/gathering-system]]"
  - "[[concepts/purchase-store]]"
  - "[[concepts/state-management]]"
  - "[[entities/items|Items]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [crafting, recipes, ownership, all-or-nothing]
---

# Crafting System

> Disciples with a crafting `CurrentTask` accumulate progress, consume raw
> resources on completion, produce items with the **ownership rule**.

## Recipes (placeholders, awaiting real balance)

| Task | Item | Grade | Time | Cost |
|---|---|---|---|---|
| `refining_elixir` | `elixir_qi_gathering` | 3 | 20s | herb × 10 |
| `forging_artifact` | `sword_azure_flame` | 5 | 30s | ore × 15, wood × 10 |

Defined in `SectStateProvider.CraftingRecipes` dict.

## Loop

```
ResourceCraftingSystem.Tick() [every frame]
  → _stateProvider.TickCrafting(Time.deltaTime)
    → for each disciple:
      if CurrentTask matches a recipe: accumulate dt into per-disciple progress
      if progress >= CraftSeconds:
        if TryConsume(resources, costs) succeeds:
          build InventoryItem
          if disciple.Rank >= Elder: → PersonalInventory
          else:                       → Stockpile.CraftedGoods (merge by item+grade)
          publish per-resource consumption events
          reset progress (carry over overshoot)
        else:  // not enough resources
          hold progress at CraftSeconds, wait (don't lose time)
```

**Per-disciple progress** (not per-task) — two disciples crafting the same
recipe must progress independently.

## Ownership Rule (the key business logic)

```csharp
OwnerScope = disciple.Rank >= DiscipleRank.Elder
    ? OwnerScope.Personal    // Elder/SectMaster: own the craft
    : OwnerScope.SectStockpile;  // Outer/Inner: craft for the sect
```

From the design doc (`project_summary.md`):
> - ศิษย์ทั่วไป: ของที่คราฟท์ได้เข้าคลังสำนัก → ศิษย์อื่นแลกซื้อด้วยค่าคุณูปการ
> - ผู้อาวุโสขึ้นไป: เก็บของที่คราฟท์เป็นของส่วนตัวได้

## All-or-Nothing Consumption

`TryConsume` only deducts if every cost can be fully paid:

```csharp
private static bool TryConsume(Dictionary<string, int> resources, Dictionary<string, int> costs)
{
    foreach (var (resource, amount) in costs)
    {
        if (!resources.TryGetValue(resource, out var have) || have < amount) return false;
    }
    // all checks pass → deduct
    foreach (var (resource, amount) in costs)
        resources[resource] -= amount;
    return true;
}
```

If resources are short, progress holds at 100% (not reset to 0). When
gathering fills the stockpile enough, the craft completes immediately with
no time penalty.

## Stockpile Merge

When adding a sect-owned item to the stockpile, merge into existing stack
if same `ItemDefId` + `Grade` + `OwnerScope`:

```csharp
var existing = _state.Stockpile.CraftedGoods
    .FirstOrDefault(g => g.ItemDefId == item.ItemDefId
                       && g.Grade == item.Grade
                       && g.OwnerScope == item.OwnerScope);
if (existing != null) existing.Quantity += item.Quantity;
else _state.Stockpile.CraftedGoods.Add(item);
```

So `4 × elixir_qi_gathering (grade 3)` + 1 new craft = `5 × elixir_qi_gathering (grade 3)`.

## Adding a New Recipe

1. Add a `CraftingRecipes` entry in `SectStateProvider`:
   ```csharp
   ["my_new_recipe"] = new CraftingRecipe(
       "my_item_id", 3, 25f,
       new Dictionary<string, int> { ["herb"] = 5, ["wood"] = 3 }),
   ```
2. Assign a disciple's `CurrentTask` to `"my_new_recipe"` (in mock data or
   through some assignment mechanism)
3. (Optional) Add a way for the player to assign tasks — currently only
   `new_disciple_applicant` (accept) auto-assigns via `RecruitOuterDisciple()`

## How UI Sees Crafting

Two events fire on completion:
- Per-resource `SectResourceChangedMessage` (negative delta for each consumed resource)
- (Future) A `DiscipleRankChangedMessage` is NOT fired — items are produced, not rank changes

Stockpile updates also fire `SectResourceChangedMessage` indirectly (via
`AdjustAndNotify` for raw resources). For crafted items, no event — UI must
re-query state.

⏳ **TODO**: add a `CraftedItemProducedMessage` if UI needs to react to
specific items appearing.

## Related Pages

- [[concepts/gathering-system|Gathering System]]
- [[concepts/purchase-store|Purchase Store]]
- [[concepts/state-management|State Management]]
- [[entities/items|Items]]
- [[entities/disciples|Disciples]]
