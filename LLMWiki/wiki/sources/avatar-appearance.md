---
title: Avatar Character Customization — Portrait Swap + Outfit Packages (v3)
type: gdd
status: v3 draft — supersedes v2 paper-doll/pure-slot design; v2 dictionary schema retained
sources:
UnityProject/Assets/Scripts/Shared/SectEconomyState.cs
UnityProject/Assets/Scripts/Shared/MockSectData.cs
UnityProject/Assets/Scripts/Data/AvatarPartPool.cs
UnityProject/Assets/Scripts/UI/Views/AvatarRenderer.cs
UnityProject/Assets/Scripts/UI/Presenters/AvatarCustomizationPresenter.cs
UnityProject/Assets/Scripts/Systems/SectStateProvider.cs
related:
"[[entities/disciples]]"
"[[entities/avatar-appearance]]"
"[[concepts/state-management]]"
"[[concepts/mvp-ui]]"
"[[concepts/sex-gender-system]]"
created: 2026-08-31
updated: 2026-09-02
confidence: medium (v3 sections) / high (v2 implemented sections)
tags: [avatar, customization, portrait-swap, outfit, pose, sprite-swap, parts]

# Avatar Character Customization — Portrait Swap + Outfit Packages (v3)

Characters render as pre-aligned full-canvas portrait chunks stacked in
draw order (visual-novel style, reference: 觅长生 / 鬼谷八荒). v2 shipped a
dictionary-based slot system; v3 adds **pose-linked Outfit Packages** on top
because reference games change the character's POSE together with the outfit —
a robe drawn for "arms crossed" cannot be worn on a "pointing hand" body.

**v2 stays.** `AvatarAppearance.Parts/Colors` dictionaries, 10 slots,
category tabs, hair 2-layer, framing presets, sex field — all implemented
and retained. v3 adds one axis: **poseId**, plus an **outfit** def-table that
batch-applies a coherent part set for one pose.

## 1. Implemented baseline (v2 — do not regress)

- `AvatarAppearance` = `{ [Key(0)] Parts: slot→partId, [Key(1)] Colors: slot→colorId }`
  on `DiscipleState.Avatar [Key(6)]`; `""`/absent = Def-table default.
- Slots: `base`(fixed) + equippable `body, head, eyes, brows, mouth, nose,
  hair, face_marking, eyeshadow, accessory`; UI categories
  ใบหน้า / ลักษณะ / ร่างกาย (`AvatarSlots.Categories`).
- `AvatarPartDef`: id, slot, category, displayName, spritePath,
  spritePathBack, thumbPath, drawOrder, drawOrderBack, isDefault, tintable.
- Draw stack: `base 0 → hair_back 10 → body 20 → head 30 → face_marking 34
  → hair_front 40 → accessory 50` (renderer builds `(order, path)` list,
  sorts, spawns — hair back/front split from one def).
- `AvatarFraming` presets FullBody / Bust / HeadIcon on one 1024×1536 canvas;
  head-center must sit at identical pixel coordinates in every file.
- `DiscipleSex [Key(7)]` on `DiscipleState`; recruitment resolves sex and
  seeds head by sex (`CreateStarterAvatar(index, sex)`).
- Mutation choke point `SectStateProvider.TryChangeAvatarPart()` broadcasts
  `AvatarEquipmentChangedMessage`; UI uses draft/diff-commit/rollback;
  interprocess write path `ChangeAvatarPartRequest/Response` registered
  (bridge tool not yet exposed).

## 2. Why Outfit Packages (v3)

Reference screenshots show each outfit carries its own pose (pointing hand,
arms crossed, holding fan). Consequences the pure-slot model cannot express:

1. `body` art is pose-specific (sleeves follow the arms of THAT pose).
2. `head`/`hair` art must be re-authored per pose (head tilt/angle changes).
3. Letting players mix a "pointing" robe with a "standing" head produces
   visibly broken composites.

Design answer: **pose becomes the registration axis**. Every pose-dependent
part is tagged with the pose it was drawn for; an **outfit** is an authored
preset that batch-selects one coherent part per slot for one pose. Within a
pose family, slots remain swappable (any hair authored for `pose_idle_01`
fits any body of `pose_idle_01`) — freedom is preserved, broken mixes are
structurally impossible.

## 3. Data model changes (v3)

### 3.1 AvatarPartDef += poseId, sexTag
```csharp
[System.Serializable]
public class AvatarPartDef
{
    // ...existing v2 fields...
    public string poseId;  // pose template the part was drawn for; "" = universal (fits all poses)
    public string sexTag;  // "male" / "female" / "" = any
}
```

### 3.2 New OutfitDef table (same JSON file)
```json
{
  "outfits": [
    { "id": "outfit_outer_male",   "displayName": "ชุดศิษย์นอก (ชาย)",  "poseId": "pose_idle_01", "sexTag": "male",
      "thumbPath": "Avatar/thumbs/outfit_outer_male",
      "parts": { "body": "body_robe_grey", "head": "head_male_01", "hair": "hair_short" } },
    { "id": "outfit_outer_female", "displayName": "ชุดศิษย์นอก (หญิง)", "poseId": "pose_idle_01", "sexTag": "female",
      "thumbPath": "Avatar/thumbs/outfit_outer_female",
      "parts": { "body": "body_robe_grey", "head": "head_female_01", "hair": "hair_twin_tail" } },
    { "id": "outfit_master_azure", "displayName": "ชุดฟ้าคราม (เจ้าสำนัก)", "poseId": "pose_idle_01", "sexTag": "male",
      "thumbPath": "Avatar/thumbs/outfit_master_azure",
      "parts": { "body": "body_robe_azure", "head": "head_male_01", "hair": "hair_topknot_long", "accessory": "acc_jade_crown" } }
  ],
  "parts": [ /* existing v2 parts, now tagged poseId */ ]
}
```

### 3.3 AvatarAppearance += PoseId (append-only, back-compat)
```csharp
[Key(2)] public string PoseId { get; set; } = string.Empty;
// "" = legacy/unposed → resolved as "pose_idle_01" fallback
```
`Parts` REMAINS the source of truth (slot→partId). Outfit apply = batch
`SetSlot` + set `PoseId`. Renderer never needs `OutfitId`.

### 3.4 Migration rule
Tag ALL existing v2 parts with `"poseId": "pose_idle_01"` in the JSON.
Old saves (`PoseId == ""`) therefore keep resolving exactly as before.

## 4. Resolution rules (v3)

`AvatarPartPool` additions (keep every v2 method signature unchanged):
- `GetOutfit(string outfitId)`
- `IReadOnlyList<OutfitDef> GetOutfitsFor(string sexTagOrEmpty)` — returns
  outfits whose sexTag is "" or matches; disciple Sex Unspecified sees all.
- `GetPartsForSlot(string slot, string poseId, string sexTag)` — filtered:
  part.slot matches AND (part.poseId == "" OR part.poseId == poseId) AND
  (part.sexTag == "" OR part.sexTag == sexTag).
- Old `GetPartsForSlot(slot)` stays (unfiltered) for compatibility.

`SectStateProvider` additions:
- `TryApplyOutfit(discipleId, outfitId, out failReason, out result)`:
  validate outfit exists → sexTag compatible with `disciple.Sex` → every
  referenced partId exists with matching poseId → then set `PoseId` +
  batch `SetSlot`, publish `AvatarEquipmentChangedMessage` per changed slot
  (same message as v2; UI external-sync already handles it).
- `TryChangeAvatarPart` gains pose validation: reject a part whose
  `poseId` is non-empty and differs from the disciple's effective pose
  (`Avatar.PoseId`, "" → "pose_idle_01"). Universal parts (`poseId == ""`)
  always allowed.

`AvatarRenderer`:
- No structural change. Add `PoseId` into `BuildSignature` (defensive).
- Optional one-time warning if equipped parts disagree on poseId.

## 5. UI changes (MVP Lite)

- Add a first category tab **"ชุดแต่งกาย"** (constant, not in
  `AvatarSlots.Categories`): when active, the option grid renders
  `GetOutfitsFor(disciple.Sex)` instead of parts; clicking an outfit calls
  `TryApplyOutfit` on the DRAFT (preview updates immediately), committed on
  Confirm like any slot change (diff detects PoseId/Parts deltas).
- Existing category tabs then show slot options filtered by
  `GetPartsForSlot(slot, draft.PoseId-or-default, disciple.Sex)` — so after
  picking a pose-carrying outfit, only pose-compatible hair/heads appear.
- Draft pattern, pooling, rollback, external sync: unchanged.

## 6. MCP exposure (later phase, noted now)

- `get_sect_state` automatically carries `Avatar.PoseId` (schema append).
- Future write tool `apply_outfit` follows `ChangeAvatarPartRequest`
  request/response pattern (NOT pub/sub). Do not implement in this pass.

## 7. Art pipeline (v3)

- One template PSD **per pose** (`Avatar_Template_pose_idle_01.psd`,
  later `pose_point_hand`, `pose_arms_crossed`, ...), same 1024×1536 canvas
  and head-center coordinate across ALL poses (framing presets depend on it).
- Every pose-dependent part is drawn over its pose template and tagged with
  that poseId in JSON; universal parts (`face_marking` center-forehead,
  simple accessories) may use `poseId: ""` if authored to fit all poses.
- sexTag on head/hair/body parts where sex-specific.
- Outfit thumbnails (`thumbPath`) = pre-rendered composite of the set.

## 8. What does NOT change (contract)

- `AvatarAppearance` Keys 0/1 semantics; `GetSlot/SetSlot/Clone/FromSlots`.
- `DiscipleState` keys; `DiscipleSex`.
- Draw-order stack & hair 2-layer logic; framing presets.
- `AvatarEquipmentChangedMessage`, `ChangeAvatarPartRequest/Response`.
- Draft/diff-commit/rollback/external-sync presenter pattern.
- MCP read path.

## 9. Not yet decided (carried over)

- Tint logic (`Colors` schema ready, no renderer logic) — phase 2.
- `thumbPath` grid thumbnails for parts (outfits get thumbs first).
- `AvatarIconBaker` RenderTexture cache for roster lists.
- DialoguePanel (Bust framing consumer).
- Whether pose switch mid-customization resets incompatible slots with a
  confirmation prompt (v3 simply blocks incompatible picks).

---

## Related Pages

- [[entities/disciples|Disciples]] — appearance lives on this entity
- [[concepts/state-management|State Management]] — MessagePack field placement
- [[concepts/mvp-ui|MVP UI Pattern]] — customization panel flow
- [[concepts/data-pipeline|Data Pipeline (Luban)]] — authoring `AvatarParts`
- [[concepts/mcp-bridge|MCP Bridge]] — MCP read/write exposure
