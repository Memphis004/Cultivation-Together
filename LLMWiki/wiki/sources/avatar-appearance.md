---
title: Avatar Character Customization (Sprite Swap)
type: gdd
sources:
  - UnityProject/Assets/Scripts/Shared/SectEconomyState.cs
  - UnityProject/Assets/Scripts/Shared/MockSectData.cs
  - UnityProject/Assets/Scripts/Systems/SectStateProvider.cs
  - McpBridge/Shared/GameMessages.cs
  - McpBridge/Program.cs
related:
  - "[[entities/disciples]]"
  - "[[concepts/state-management]]"
  - "[[concepts/mvp-ui]]"
  - "[[concepts/data-pipeline]]"
  - "[[sources/architecture]]"
created: 2026-08-31
updated: 2026-08-31
confidence: medium
tags: [avatar, customization, sprite-swap, head, hair, body, accessory, parts]
---

# Avatar Character Customization — Sprite Swap

> A character renders as a layered sprite assembled from four swappable parts:
> **Head, Hair, Body, Accessory**. Which part each disciple wears is chosen at
> authoring (Def table) or at runtime (UI editor / shop / reward), stored as
> `AvatarAppearance` inside `DiscipleState`, resolved to a draw order by
> `AvatarRenderer`.

## 1. Why a Sprite-Swap System

The game is sprite-driven (no character models / skinned meshes). Rather than
draw one avatar per disciple variant (explosive art counts), a single base
sprite per part is reused and swapped by selecting which sprite tile to show
per slot. This decouples art volume from character variety and makes every
avatar unambiguously queryable through MCP — a part the AI GM may want to
reward / lock / loot.

**Trade-off by design**: simple "which tile shows" model, no blending/morph
between parts. If verticals later need cross-sprite blending, add a
separate effect layer — do not redesign slot selection now.

## 2. Slots

| Slot | Layer order (draw-from-bottom) | Notes |
|---|---|---|
| `base` | 0 | Always drawn. Disciple's base sprite tile. |
| `body` | 1 | Clothing / robe / armor. Can hide body base. |
| `head` | 2 | Face, hat, or full head piece. |
| `hair` | 3 | Hair / hairpiece. Must not fully occlude head unless intended (author decides). |
| `accessory` | 4 | Ring, amulet, fan — decorative, always on top. |

Each slot resolves to **one or more** atlas layer groups in draw order
(e.g. hair = `[base_hair, shine]`). See §4 resolver.

## 3. Data Model (MessagePack-native)

Follows the codebase rule: **Definitions live in a table; runtime state holds
IDs, never object references** so the whole thing serializes with
`MessagePack` and crosses the bridge. Appearance therefore rides inside
`DiscipleState` (not a separate top-level block):

```csharp
enum AvatarPart { Body, Head, Hair, Accessory }  // draw-bottom -> top

[MessagePackObject([Key(6)])]   // append after existing fields; keep order
public class AvatarAppearance {
    [Key(0)] public string Body   { get; set; }   // PartId or "" = default-from-def
    [Key(1)] public string Head   { get; set; }
    [Key(2)] public string Hair   { get; set; }
    [Key(3)] public string Accessory { get; set; }
}
```

```csharp
[MessagePackObject]
public class AvatarEquipmentChangedMessage
{
    [Key(0)] public string DiscipleId { get; set; }
    [Key(1)] public string Slot { get; set; } // "Body", "Head", etc.
    [Key(2)] public string PartId { get; set; }
}
```

`DiscipleState` gains one field:

```csharp
[Key(6)] public AvatarAppearance Avatar { get; set; } = new AvatarAppearance();
```

- Empty slot (`""`) falls back to the **default defined in the Def table** —
  keeps state tiny and means "un customized" is a valid default roster.
- All part selections are PartIds (`string`), the same convention as
  `ItemDefId`/`ItemTaskDefId`: resolved to art at runtime, so adding new art
  never requires a schema change or save migration.

**Don't introduce a new top-level `Sprite` enum or renumber keys.** Append
`[Key(N)]` at the end of each existing MessagePack class to preserve
backward-compatible reads — same discipline as `DiscipleState` adding
`Avatar` (see `disciples.md` code citation).

## 4. Resolver — Def Table → Draw Order

Design-time (Luban/ScriptableObject, not hand-authored like world events):

⚠️ Important: Do NOT use ScriptableObject for AvatarPartPool. Use plain C# class loading JSON from Resources/DataTables/, exactly like LubanEventPool.

```
AvatarParts Def             (e.g. author in Excel/Luban)
├── Body   { PartId="robe_white",     LayerAtlas="avatar", DrawOrder=1, LayerGroups=["robe_b", "robe_b_rim"], Tint?="" }
├── Head   { PartId="face_young",     LayerAtlas="avatar", DrawOrder=2, LayerGroups=["face_b"] }
└── Hair   { PartId="hair_long",      LayerAtlas="avatar", DrawOrder=3, LayerGroups=["hair_b", "hair_shine"] }

AvatarRenderer.Resolve(AvatarAppearance, AvatarParts) -> List<PartLayer> {
    base -> [base_b]                                    // constant
    sort by DrawOrder, then emit { Atlas, LayerGroup }
}
```

Resolution order each frame (cheap, only when avatar visible):

```
resolve(base disciple ID)  ==→  list of layer groups, dedup, sort by draw order
```

## 5. Rendering (UGUI, no UI Toolkit)

Per-game GameObject per disciple, always in scene (cheap — 5 sprites):

```
[char_<discipleId>] (Canvas Group, Canvas = World / HUD as design picks)
├── CG: base    (Image, sprite[base tile])
├── Mask
│   ├── CG: body   (9-slice or tile)
│   ├── CG: head
│   └── CG: hair   (author controls which paints over which)
└── CG: accessory (Image, on top)
```

`AvatarRenderer` runs on a 0.1s updater (only while the character is on
screen) and drives which atlas + tile each renderer shows. Base/base-asset
tile is constant; only 4 slots re-bind (or re-show-hide) per change.

```csharp
public class AvatarRenderer : MonoBehaviour 
{
}
```
**Canvas placement**: character canvas is the design decision — e.g. the AI
VTuber / main disciple lives in a portrait HUD, other disciples are sprites
in a 2D "presence" layer. Not yet decided — do not couple the renderer to one
specific panel until the viewport choice exists.

## 6. Where it Fits the Architecture

```
Authoring (design time):  Excel/Luban → cfg.AvatarParts + default per disciple in MockSectData.cs
        ↓
Runtime state:      DiscipleState.Avatar  (MessagePack, inside the economy state snapshot)
        ↓
Renderer:           AvatarRenderer (in-process, reads MockSectData Defs + Avatar)
        ↓
Notify:             AvatarEquipmentChangedMessage { DiscipleId, Slot, PartId, Timestamp }
        ↓
UI:                 AvatarCustomizationView/Presenter (MVP pattern like EventPopup)
        ↓
Bridge:             get_sect_state already carries .Avatar?   execute_avatar_slot?
```

**Grounded in existing parts**:

- **State** — add `AvatarAppearance` to `SectEconomyState.cs`
  `DiscipleState` (`Shared/SectEconomyState.cs`), same namespace and style.
- **Def loader** — mirror `LubanEventPool.cs` (`concepts/data-pipeline`) as a
  plain-C# `AvatarPartPool` that parses `cfg.AvatarParts` JSON. Keep it a
  **plain C# class, not a ScriptableObject** — the project moved event data
  this way in lab 12.
- **UI** — follow the `EventPopupPresenter` MVP flow: a stateless
  `Presenter` (Transient, resolves via VContainer enum→Type mapping, no
  reflection) reading from `ISectStateProvider`.
- **Notify** — new message in `GameMessages.cs`; publish from the same
  single mutation choke point pattern used by `SectStateProvider.AdjustAndNotify`.
- **Bridge** — `get_sect_state` already returns the full snapshot including
  `.Avatar`, so appearance is AI-queryable on day zero via `(disciple.Avatar, body)`.
  The AI GM could call a new write tool later — but the pattern follows
  `execute_decision` (request/response for mutating games) rather than the
  broken TCP-subscribe path (see `game-messages.md` — keep new mutation as
  write/request-response to avoid the `AwaitWorldEventRequest` transport trap).

**MCP exposure note**: `get_sect_state` (`McpBridge/Program.cs` `SectQueryTools`)
returns the MessagePack-embedded `.Avatar`. No new read tool strictly needed
until AI actions on appearance are required (e.g. reward avatar / lock avatar).

## 7. Not Yet Decided

- **Which tool buys/edits a part?** Shop (`purchase-item`) is items-only —
  add an `AvatarPart` as a purchasable "item," or a separate MCP write tool.
  Do not conflate the two until a choice is made.
- **Do avatars cost Contribution?** Reward vs purchase vs free — all open.
- **Canvas/viewport**: portrait-only (current UI is a single top-bar HUD +
  center popup) vs shared 2D stage — blocks final Renderer/canvas wiring.
- **Layer occlusion rules**: hair-overhead vs head-over-hair per slot — author
  per slot in the Def; default = later slot paints on top.

## 8. Implementation Plan (Draft)

1. Add `AvatarAppearance` to `DiscipleState` (MessagePack `[Key(N)]` append).
2. Author `cfg.AvatarParts` Excel → Luban (`AvatarParts.xsd` +
   `AvatarParts.xlsx`) → C# rows. Add defaults per disciple in
   `MockSectData`.
3. `AvatarPartPool` (plain C# loader, mirror `LubanEventPool`).
4. `AvatarRenderer` (UGUI, no UI Toolkit) — resolves def → layer groups.
5. `AvatarEquipmentChangedMessage` + subscription in `ResourceHudView`/UI.
6. (Later) `AvatarEditorPresenter` MVP panel + MCP write tool.

## Related Pages

- [[entities/disciples|Disciples]] — appearance lives on this entity
- [[concepts/state-management|State Management]] — MessagePack field placement
- [[concepts/mvp-ui|MVP UI Pattern]] — customization panel flow
- [[concepts/data-pipeline|Data Pipeline (Luban)]] — authoring `AvatarParts`
- [[concepts/mcp-bridge|MCP Bridge]] — MCP read/write exposure
