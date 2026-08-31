---
title: Avatar Appearance
type: entity
sources:
  - UnityProject/Assets/Scripts/Shared/SectEconomyState.cs
  - McpBridge/Shared/GameMessages.cs
related:
  - "[[sources/avatar-appearance|Avatar Appearance (Sprite Swap)]]"
  - "[[entities/disciples|Disciples]]"
created: 2026-08-31
updated: 2026-08-31
confidence: low
tags: [avatar, parts, sprite-swap, head, hair, body, accessory]
---

# Avatar Appearance

> Per-disciple sprite customization via four **swappable layers** — Body, Head,
> Hair, Accessory. Which part each disciple wears is stored on `DiscipleState.
> Avatar` as an **AvatarAppearance** object keyed by slot.

## Slots (draw bottom → top)

| Slot | Draw Order | What it covers |
|---|---|---|
| `Body` | 0 | robe / tunic / armor |
| `Head` | 1 | face / hat / full head piece |
| `Hair` | 2 | hair / hairpiece (painted above head by default) |
| `Accessory` | 3 | ring / amulet / fan (always top) |

An empty slot (`""`) means "use the Def-table default" — keeps default
rosters tiny and makes "un-customized" a first-class state.

## Schema

```csharp
[MessagePackObject([Key(6)])]   // append after existing Key(0..5) fields
public class AvatarAppearance {
    [Key(0)] public string Body     { get; set; }   // PartId or ""
    [Key(1)] public string Head     { get; set; }   // PartId or ""
    [Key(2)] public string Hair     { get; set; }   // PartId or ""
    [Key(3)] public string Accessory { get; set; }   // PartId or ""
}
```

Lives on `DiscipleState` as `Avatar` — **inside** the `SectEconomyState`
snapshot, so it is already returned by `get_sect_state` via the MCP bridge.

See [[sources/avatar-appearance|the design doc]]; this is the runtime view of
it (still planned). Confidence `low` — not implemented yet.

## Related Pages

- [[sources/avatar-appearance|Avatar Appearance (Sprite Swap)]]
- [[entities/disciples|Disciples]]
