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

> Per-disciple sprite customization via a **dictionary-based** structure. Which part each disciple wears is stored on `DiscipleState.Avatar` as an **AvatarAppearance** object containing Parts and Colors dictionaries.

## Slots & Draw Order

Defined in `AvatarSlots.cs`. Equippable slots include:
`Body`, `Head`, `Eyes`, `Brows`, `Mouth`, `Nose`, `Hair`, `FaceMarking`, `Eyeshadow`, `Accessory`.

They are grouped into categories for UI ("ใบหน้า", "ลักษณะ", "ร่างกาย").
An empty string or missing key means "use the default for this slot".

## Schema

```csharp
[MessagePackObject]
public sealed class AvatarAppearance
{
    [Key(0)] public Dictionary<string, string> Parts { get; set; } = new Dictionary<string, string>();
    [Key(1)] public Dictionary<string, string> Colors { get; set; } = new Dictionary<string, string>();
    
    // ... helper methods like GetSlot, SetSlot, Clone, and FromSlots ...
}

public struct SlotPart
{
    public string Slot;
    public string PartId;
}
```
> 📎 Source: Assets/Scripts/Shared/SectEconomyState.cs

Lives on `DiscipleState` as `Avatar` — **inside** the `SectEconomyState`
snapshot, so it is already returned by `get_sect_state` via the MCP bridge.

## Related Pages

- [[sources/avatar-appearance|Avatar Appearance (Sprite Swap)]]
- [[entities/disciples|Disciples]]
