---
title: State Management
type: concept
sources:
  - UnityProject/Assets/Scripts/Shared/SectEconomyState.cs
  - UnityProject/Assets/Scripts/Systems/SectStateProvider.cs
  - UnityProject/Assets/Scripts/Shared/GameMessages.cs
related:
  - "[[concepts/message-pipe-bus]]"
  - "[[concepts/decision-pipeline]]"
  - "[[concepts/purchase-store]]"
created: 2026-08-31
updated: 2026-09-04
confidence: high
tags: [state, economy, messagepack, delta-events, avatar]
---

# State Management

> The single source of truth for all game state. The contract between
> subsystems, UI, and the MCP bridge.

## Single Live Instance

**Critical lesson** (lab 7, BUG-L7-01): state must be held as ONE live
instance for the process lifetime, not rebuilt per query.

```csharp
// SectStateProvider.cs:55
private readonly SectEconomyState _state = MockSectData.Create();
```

Constructor takes the publishers; field initializer takes the state. After
this, every method reads/mutates `_state` in place.

## The State Shape

From `Shared/SectEconomyState.cs`:

```
SectEconomyState
├── Disciples: List<DiscipleState>
│   └── DiscipleState
│       ├── DiscipleId: string       (e.g. "d000", "d001")
│       ├── DisplayName: string
│       ├── Rank: DiscipleRank       (Outer/Inner/Elder/SectMaster)
│       ├── Wallet: CurrencyWallet
│       │   ├── SpiritStones: long
│       │   └── Contribution: long
│       ├── PersonalInventory: List<InventoryItem>
│       ├── CurrentTask: string      (e.g. "gathering_herb", "refining_elixir")
│       ├── Avatar: AvatarAppearance (dictionary schema — ดู entities/avatar-appearance)
│       │     Parts:  Dict<slot, partId>   (""/absent = slot default)
│       │     Colors: Dict<slot, colorId>  (tintable slots only)
│       │     PoseId: string               ("" → fallback "pose_idle_01"; pose เดียว ไม่มี UI เปลี่ยน)
│       └── Sex: DiscipleSex               (Unspecified/Male/Female — ใช้เลือก head ตอนสร้าง + filter ตอนสุ่ม)
└── Stockpile: SectStockpile
    ├── RawResources: Dict<string, int>   (herb, wood, ore, provisions)
    └── CraftedGoods: List<InventoryItem>
        └── InventoryItem
            ├── ItemDefId: string
            ├── Quantity: int
            ├── Grade: int            (1-5)
            └── OwnerScope: enum      (Personal / SectStockpile)
```

## Two Communication Channels

| Channel | Purpose | Format |
|---|---|---|
| **Pub/sub events** | "Something happened" — UI + bridge react | Small DTOs (e.g. `SectResourceChangedMessage` with `ResourceId`, `Delta`, `NewTotal`) |
| **State query** | "What's the current state?" | Full `SectEconomyState` serialized as MessagePack bytes |

The two are **never the same shape**. Pub/sub is for deltas, query is for
snapshots. This avoids forcing every subscriber to diff full state.

## The "Adjust and Notify" Pattern

Every state mutation that touches resources goes through:

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
requested delta. When `Adjust()` clamps at 0, the two can differ.

## All-or-Nothing Resource Consumption

```csharp
private static bool TryConsume(Dictionary<string, int> resources, Dictionary<string, int> costs)
{
    foreach (var (resource, amount) in costs)
    {
        if (!resources.TryGetValue(resource, out var have) || have < amount) return false;
    }

    foreach (var (resource, amount) in costs)
    {
        resources[resource] -= amount;
    }

    return true;
}
```

Used in crafting — if you can't fully pay, you don't pay at all. No partial
state.

## MessagePack Serialization

`SectEconomyState` has `[MessagePackObject]`/`[Key]` attributes. The
`ToByteArray()` method produces bytes for the bridge:

```csharp
public byte[] ToByteArray() => MessagePackSerializer.Serialize(this);
public static SectEconomyState FromByteArray(byte[] bytes) =>
    MessagePackSerializer.Deserialize<SectEconomyState>(bytes);
```

The bridge receives these bytes in `SectStateSnapshot.EconomyStateBytes` and
is expected to decode them to JSON for the AI client.

## Missing: Dedicated WalletChangedMessage

There's no `WalletChangedMessage` yet. `WalletHudPresenter` refreshes the
wallet piggyback on every `SectResourceChangedMessage`.

This works because all current wallet mutations (purchase) also change
resources. If you add a code path that mutates wallet without going through
`AdjustAndNotify`, **the UI will silently desync**.

⏳ **TODO**: add `WalletChangedMessage` when wallet-only events become common.

## Related Pages

- [[concepts/decision-pipeline|Decision Pipeline]]
- [[concepts/purchase-store|Purchase Store]]
- [[concepts/message-pipe-bus|MessagePipe Bus]]
- [[entities/avatar-appearance|Avatar Appearance]]
- [[sources/bug-log|Bug Log]] (BUG-L7-01)
