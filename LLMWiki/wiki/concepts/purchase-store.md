---
title: Purchase Store
type: concept
sources:
  - UnityProject/Assets/Scripts/Core/PurchaseItemHandler.cs
  - UnityProject/Assets/Scripts/Systems/SectStateProvider.cs
related:
  - "[[concepts/crafting-system]]"
  - "[[concepts/state-management]]"
  - "[[entities/items|Items]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [purchase, store, contribution, transaction]
---

# Purchase Store

> Disciples buy items from the **Sect Stockpile** using their own
> **Contribution**. Atomic check-and-deduct — no partial state on failure.

## MCP Tool

`purchase_item(discipleId, itemDefId, grade, quantity)` → returns
`PurchaseItemResponse { Success, Message, RemainingContribution }`

## Pricing (placeholder)

```csharp
private const int ContributionPricePerGrade = 50;
// cost = grade * 50 * quantity
```

So a grade 3 item costs 150 contribution per unit.

## Atomic Transaction

```csharp
public PurchaseItemResponse TryPurchaseItem(string discipleId, string itemDefId, int grade, int quantity)
{
    // 1. Validate quantity
    if (quantity <= 0) return Fail("Quantity must be positive.");

    // 2. Find disciple
    var disciple = _state.Disciples.FirstOrDefault(d => d.DiscipleId == discipleId);
    if (disciple == null) return Fail($"No disciple with id '{discipleId}'.");

    // 3. Find stock
    var stockEntry = _state.Stockpile.CraftedGoods.FirstOrDefault(g =>
        g.ItemDefId == itemDefId && g.Grade == grade && g.OwnerScope == OwnerScope.SectStockpile);
    var haveQuantity = stockEntry?.Quantity ?? 0;
    if (stockEntry == null || haveQuantity < quantity)
        return Fail($"Not enough '{itemDefId}'...");

    // 4. Check funds
    var cost = (long)grade * ContributionPricePerGrade * quantity;
    if (disciple.Wallet.Contribution < cost)
        return Fail($"{disciple.DisplayName} needs {cost} contribution but only has {disciple.Wallet.Contribution}.");

    // 5. ALL CHECKS PASS — mutate atomically
    disciple.Wallet.Contribution -= cost;
    stockEntry.Quantity -= quantity;
    if (stockEntry.Quantity <= 0) _state.Stockpile.CraftedGoods.Remove(stockEntry);
    disciple.PersonalInventory.Add(new InventoryItem { ... });

    return Success($"Purchased {quantity}x {itemDefId} (grade {grade}) for {cost} contribution.");
}
```

**Pattern**: validate all → mutate all → return. On any failure, return
without mutating.

## Why Request-Response (Not Pub/Sub)

From `GameMessages.cs:138-143` (comment):

> Request/response rather than a fire-and-forget message because the bridge
> needs to know immediately whether the purchase actually succeeded (enough
> contribution, enough stock) - same reasoning as SectStateQuery.

## What Only Outer/Inner Can Buy

Only items with `OwnerScope.SectStockpile` are in the buyable pool. Items
crafted by Elders (which go to their `PersonalInventory`) are NOT available
in the sect store.

## Wallet Changes Don't Publish Yet

After the purchase, `disciple.Wallet.Contribution` decreases. There's no
`WalletChangedMessage` yet — `WalletHudPresenter` piggybacks on the
per-resource `SectResourceChangedMessage` for refresh.

**Works only because purchase deducts raw resources from the stockpile** —
the resource change triggers the HUD refresh. If a future code path
deducts contribution without touching resources, **UI will silently desync**.

⏳ **TODO**: add `WalletChangedMessage` when wallet-only events become common.

## Related Pages

- [[concepts/crafting-system|Crafting System]]
- [[concepts/state-management|State Management]]
- [[concepts/mcp-bridge|MCP Bridge]]
- [[entities/items|Items]]
