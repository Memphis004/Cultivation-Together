title: Avatar Appearance
type: entity
sources:
  - UnityProject/Assets/Scripts/Shared/SectEconomyState.cs
  - UnityProject/Assets/Scripts/Shared/GameMessages.cs
  - UnityProject/Assets/Scripts/Shared/MockSectData.cs
  - UnityProject/Assets/Scripts/Data/AvatarPartPool.cs
  - UnityProject/Assets/Scripts/UI/Views/AvatarRenderer.cs
  - UnityProject/Assets/Scripts/UI/Presenters/AvatarCustomizationPresenter.cs
  - UnityProject/Assets/Scripts/UI/Views/AvatarCustomizationView.cs
  - UnityProject/Assets/Scripts/UI/Views/AvatarCustomizationViewData.cs
  - UnityProject/Assets/Scripts/UI/Views/AvatarOptionButton.cs
  - UnityProject/Assets/Scripts/Systems/SectStateProvider.cs
  - UnityProject/Assets/Scripts/Core/GameLifetimeScope.cs
  - UnityProject/Assets/Scripts/Core/ChangeAvatarPartHandler.cs
  - UnityProject/Assets/Resources/Data/avatar_parts.json
related:
  - "[[entities/disciples|Disciples]]"
  - "[[sources/avatar-appearance|Design Notes & Decisions]]"
  - "[[sources/sex-gender-system|Sex / Gender System]]"
  - "[[concepts/mvp-ui|MVP UI Pattern]]"
  - "[[concepts/message-pipe-bus|MessagePipe Bus]]"
  - "[[concepts/state-management|State Management]]"
created: 2026-08-31
updated: 2026-09-04
confidence: high
tags: [avatar, parts, sprite-swap, dictionary-schema, hair-2-layer, framing, draft-pattern, parts-only]

# Avatar Appearance

หน้าตาของศิษย์ทั้งตัว เก็บเป็น dictionary `slot → partId` บน `DiscipleState.Avatar`
เรนเดอร์ด้วยการซ้อน sprite เป็นชั้น (layered compositing) ตาม `drawOrder`

Status: `implemented — v2.5 parts-only MVP (outfit packages rolled back 2026-09-03)`

⚠️ **สถาปัตยกรรมปัจจุบัน = Parts-only (ไม่มี Outfit)**
ตาราง `outfits[]`, `OutfitDef`, `TryApplyOutfit()`, `AvatarOutfitChangedMessage`
และแท็บ "ชุดแต่งกาย" ที่เคยร่างใน spec v3 **ถูกลบแล้ว** — เหตุผลเต็มอยู่ที่
[[sources/avatar-appearance]] §1 (สรุป: outfit derive จาก `parts[]` ได้ 100%
และมี pose เดียวจึงไม่มีอะไรให้สลับ) — `Parts` คือ source of truth เพียงหนึ่งเดียว

ระบบประกอบด้วย 4 ชั้น ทุกชั้น verify จากโค้ดจริง:

1. **State schema** — `AvatarAppearance` (dictionary) บน `DiscipleState.Avatar [Key(6)]` + `DiscipleSex [Key(7)]` + `AvatarSlots` constants/categories
2. **Data pool** — `AvatarPartPool` โหลด def-table จาก `Resources/Data/avatar_parts.json` (plain C# JSON, mirror แพทเทิร์นของ `LubanEventPool`)
3. **Rendering** — `AvatarRenderer` (UGUI, layered sprites, hair 2-layer, framing presets, polling 0.1s พร้อม signature guard + sprite cache + layer pooling)
4. **Customization UI** — `AvatarCustomizationPresenter` (draft/diff-commit/rollback/external-sync) + `AvatarCustomizationView` (category/slot tabs, option grid pooling) + `AvatarOptionButton`

## Ownership & Mutation Path

- State: `DiscipleState.Avatar [Key(6)]`
- Mutation choke point เดียว: `SectStateProvider.TryChangeAvatarPart()` → broadcast `AvatarEquipmentChangedMessage`
- UI: `AvatarCustomizationView` + `AvatarCustomizationPresenter` (draft / diff-commit / rollback)
- Render: `AvatarRenderer` (layered sprites, pooling)
- Write path สำหรับ AI Agent: `ChangeAvatarPartRequest` / `ChangeAvatarPartResponse` (request-response — ไม่ใช่ pub/sub เพราะ agent ต้องรู้ผลทันที)
- ❌ ไม่มี `TryApplyOutfit()` / ไม่มี `OutfitId` บน state — การแต่งตัว = เปลี่ยน part ทีละ slot เท่านั้น

## Data Schema

รองรับ slot จำนวนเท่าไหร่ก็ได้โดยไม่ต้องแก้ schema — นี่คือคุณสมบัติที่ทำให้
face customization (eyes/brows/nose/mouth) เพิ่มทีหลังได้ฟรี

```csharp
[MessagePackObject]
public sealed class AvatarAppearance
{
    [Key(0)] public Dictionary<string, string> Parts  { get; set; } = new Dictionary<string, string>();
    [Key(1)] public Dictionary<string, string> Colors { get; set; } = new Dictionary<string, string>();
    [Key(2)] public string PoseId { get; set; } = string.Empty;   // reserved — ดู §PoseId

    public string GetSlot(string slot) => Parts.TryGetValue(slot, out var v) ? v : string.Empty;
    public bool SetSlot(string slot, string partId)
    {
        if (string.IsNullOrEmpty(partId)) { Parts.Remove(slot); return true; }
        Parts[slot] = partId;
        return true;
    }
    // GetColor / SetColor / Clone / FromSlots ...
}
```
📎 Source: `Assets/Scripts/Shared/SectEconomyState.cs`

- `Parts`: slot → partId — ค่าว่าง (`""`/ไม่มี key) = ใช้ default ของ slot นั้น (`SetSlot` ด้วย partId ว่างจะ `Remove` key ออกจาก dict เลย)
- `Colors`: slot → colorId — มีเฉพาะ slot ที่ `tintable` (⏳ schema พร้อม แต่ยังไม่มี UI)
- `Clone()` ใช้ 2 ที่: draft pattern ฝั่ง Presenter และการคืน copy จาก `TryChangeAvatarPart` (ไม่ปล่อย reference ของ live state หลุดออกไป)

สร้างครั้งเดียวหลาย slot ได้จาก factory `FromSlots` + struct `SlotPart`:

```csharp
public struct SlotPart
{
    public string Slot;
    public string PartId;
    public SlotPart(string slot, string partId) { Slot = slot; PartId = partId; }
}

// usage ใน MockSectData:
Avatar = AvatarAppearance.FromSlots(
    new SlotPart(AvatarSlots.Body,      "body_robe_azure"),
    new SlotPart(AvatarSlots.Head,      "head_male_01"),
    new SlotPart(AvatarSlots.Hair,      "hair_topknot_long"),
    new SlotPart(AvatarSlots.Accessory, "acc_jade_crown")
)
```
📎 Source: `Assets/Scripts/Shared/SectEconomyState.cs`, `Assets/Scripts/Shared/MockSectData.cs`

### PoseId — pose คงที่เดียว (reserved สำหรับ multi-pose)

`[Key(2)] PoseId` คงไว้ในสคีมา และถูกอ่านแล้ว 2 จุด — pose validation ใน `TryChangeAvatarPart()` + filter ตอน `OnRandomize` (ยังไม่มี UI ให้เปลี่ยน pose):

| ประเด็น | สถานะ |
| --- | --- |
| ค่าใน state | `""` เสมอ (resolve เป็น `pose_idle_01`) |
| Validation (poseId ของ part เทียบ effective pose) | ✅ implement — pose เดียวตอนนี้จึงไม่เคย fail; กันภาพพังวันที่ pose ที่ 2 เข้า |
| UI ให้เปลี่ยน pose | ❌ ไม่มี |
| ทำไมไม่ลบ | MessagePack key เป็น append-only — จองไว้ตอนนี้ราคา 0 แต่ถอยหลังไม่ได้ |

### DiscipleState — เจ้าของ Avatar

```csharp
[MessagePackObject]
public class DiscipleState
{
    // [Key(0)]..[Key(5)] ...
    [Key(6)] public AvatarAppearance Avatar { get; set; } = new AvatarAppearance();
    [Key(7)] public DiscipleSex Sex { get; set; } = DiscipleSex.Unspecified;  // implemented
    [Key(8)] public ChibiBackend ChibiBackend { get; set; } = ChibiBackend.SpriteSheet;  // 🆕 Phase 1 — entitlement (2 ค่า: SpriteSheet=0, Spine=1)
}
```
📎 Source: `Assets/Scripts/Shared/SectEconomyState.cs`

`[Key(8)] ChibiBackend` (Phase 1 ของ [[disciple-visual-system]]) เป็น **entitlement**
แกนแยกจาก AvatarAppearance — state เก่าที่ไม่มี key นี้ deserialize ได้ `SpriteSheet`
(default index 0, test ครอบใน `ChibiBackendDeserializationTests`)

🆕 **Phase 3: ChibiBackend เป็น render-active แล้ว** — `VisualTierPolicy` resolve
*effective* backend (entitlement ∩ `SpineBudget`) ทุก spawn/reconcile ของ
`DiscipleVisualSystem` และเปลี่ยนค่าได้ผ่าน mutation choke point
`ISectStateProvider.TrySetChibiBackend` (publish `DiscipleChibiBackendChangedMessage`
→ respawn แบบคง position/activity/facing; degrade ที่ budget ไม่พอเปลี่ยนเฉพาะ
effective backend ไม่แตะ state) — ⚠️ **Spine backend ยัง INERT** จนกว่า S4 license
จะถูกยืนยัน (`VisualRuntimeConfig.SpineActivationRequested` = false, ดู
[[disciple-visual-system]] §6.3/§7) — ตอนนี้ entitled disciples render เป็น
SpriteSheet ทั้งหมด

`Sex` implement แล้วตาม [[sources/sex-gender-system]] (recruitment เลือก head ตาม sex จริง,
MockSectData ระบุ sex ชัดเจน) — แต่ `sexTag` filter ในกริดยังไม่ได้เปิดใช้ (ดู Roadmap)

## Slot Constants & Category Tabs

```csharp
public static class AvatarSlots
{
    public const string Base        = "base";
    public const string Body        = "body";
    public const string Head        = "head";
    public const string Eyes        = "eyes";
    public const string Brows       = "brows";
    public const string Mouth       = "mouth";
    public const string Nose        = "nose";
    public const string Hair        = "hair";
    public const string FaceMarking = "face_marking";
    public const string Eyeshadow   = "eyeshadow";
    public const string Accessory   = "accessory";

    public static readonly string[] Equippable =
    {
        Body, Head, Eyes, Brows, Mouth, Nose,
        Hair, FaceMarking, Eyeshadow, Accessory
    };

    public static readonly IReadOnlyDictionary<string, string[]> Categories =
        new Dictionary<string, string[]>
        {
            { "ใบหน้า", new[] { Head, Eyes, Brows, Mouth, Nose } },
            { "ลักษณะ", new[] { Hair, FaceMarking, Eyeshadow } },
            { "ร่างกาย", new[] { Body, Accessory } },
        };
    // Labels: body="เสื้อผ้า", hair="ทรงผม", ...
}
```
📎 Source: `Assets/Scripts/Shared/SectEconomyState.cs`

`Equippable` (10 slots, ไม่รวม `base`) ใช้ขับ diff-commit และ randomize; `base` เป็นโครงคงที่ที่ Renderer resolve เองเสมอ ไม่โผล่ในกริดตัวเลือก

### ⚠️ สถานะ data จริง — slot ที่ "ประกาศแล้วแต่ว่างเปล่า"

| slot | มี part ใน JSON? | หมายเหตุ |
| --- | --- | --- |
| base | ✅ 1 ชิ้น | โครงร่างคงที่ |
| body | ✅ 4 ชิ้น | robe grey/azure/white/black |
| head | ✅ 3 ชิ้น | ใบหน้าสำเร็จรูปทั้งใบ ← **คอขวดของ MVP** |
| hair | ✅ 5 ชิ้น | 2 ชิ้นมี back layer (`hair_topknot_long`, `hair_twin_tail`) |
| face_marking | ✅ 2 ชิ้น | รวม `face_marking_none` |
| accessory | ✅ 4 ชิ้น | รวม `acc_none` |
| eyes / brows / nose / mouth / eyeshadow | ❌ 0 ชิ้น | สคีมารองรับแล้ว รอ content |

🔴 **ข้อจำกัดสำคัญของ MVP:** `head` รวมทั้งใบหน้าเป็นก้อนเดียว → ผู้เล่นเลือกได้แค่ 3 ใบหน้า
ไม่ใช่การ "ปั้นหน้า" (捏脸) — การแตก `head` เป็น face_shape/eyes/brows/nose/mouth
คืองานลำดับ 1 ของเฟสถัดไป (ดู Roadmap + [[sources/avatar-appearance]] §3)

## Data Pool

`AvatarPartPool` register เป็น Singleton ใน `GameLifetimeScope` — แพทเทิร์นเดียวกับ
`LubanEventPool` (plain C# loader, ไม่ใช่ ScriptableObject)

**Public API (parts-only — ไม่มี `GetOutfit` / `GetOutfitsFor` อีกแล้ว):**

| Method | ใช้ทำอะไร |
| --- | --- |
| `GetById(partId)` | หา def จาก id |
| `Resolve(slot, partId)` | คืน def; ถ้าหาไม่เจอ/ว่าง → fallback เป็น `isDefault` ของ slot นั้น |
| `GetDefaultForSlot(slot)` | default part ของ slot |
| `GetPartsForSlot(slot)` | รายการตัวเลือกในกริด (ไม่กรอง) |
| `GetPartsForSlot(slot, poseId, sex)` | กรอง pose/sex — ใช้ใน `OnRandomize` (poseId/sexTag ว่าง = universal) |
| `IsValidForSlot(slot, partId)` | validation ก่อน mutate (`""` = default เสมอ → valid) |

📎 Source: `Assets/Scripts/Data/AvatarPartPool.cs`

### Part Definition Fields

```csharp
[System.Serializable]
public class AvatarPartDef
{
    public string id;
    public string slot;
    public string category;        // "ใบหน้า" / "ลักษณะ" / "ร่างกาย"
    public string displayName;
    public string spritePath;      // path under Resources/ ; "" = empty layer
    public string spritePathBack;  // ผมหลัง — ว่าง = ชิ้นนี้ไม่มี back layer
    public string thumbPath;       // thumbnail สำหรับกริด — ว่าง = ใช้ spritePath หลัก
    public int    drawOrder;
    public int    drawOrderBack;   // 0 = ใช้ drawOrder (ผมหลังใช้ 10)
    public bool   isDefault;
    public bool   tintable;
    public string poseId;          // pose template ที่ part วาดสำหรับ; "" = universal
    public string sexTag;          // "male" / "female" / "" = any — กรองตอน Randomize; grid ยังไม่ filter
}
```
📎 Source: `Assets/Scripts/Data/AvatarPartPool.cs`

`poseId`/`sexTag` ถูกอ่านแล้ว 2 จุด (pose validation + `OnRandomize`) — การ tag ข้อมูลไว้ยังจำเป็น
เพราะย้อนกลับไป tag ไฟล์ art เก่าหลายร้อยชิ้นทีหลังแพงมาก; ส่วน `sexTag` filter ในกริด (UI) ค่อยเปิดตาม Roadmap ข้อ 5

### JSON Layout (`Resources/Data/avatar_parts.json`)

Root มี key เดียวคือ `parts` — `outfits` ถูกลบออกแล้ว:

```json
{
  "parts": [
    { "id": "base_silhouette", "slot": "base", "category": "ร่างกาย",
      "displayName": "โครงร่าง", "spritePath": "Avatar/base_silhouette",
      "spritePathBack": "", "thumbPath": "", "drawOrder": 0, "drawOrderBack": 0,
      "isDefault": true, "tintable": false, "poseId": "", "sexTag": "" }
    /* body / head / hair / face_marking / accessory ... */
  ]
}
```

## Rendering

### Draw Stack

```
base           0
hair_back     10   ← มาจาก spritePathBack ของ def เดียวกับ hair_front
body          20
head          30
face_marking  34
(31–38 สำรองสำหรับ face sub-slots เมื่อแตก head)
hair_front    40
accessory     50
```

Renderer สร้าง list `(order, path)` → sort → spawn layer ตามลำดับ —
ผมหน้า/ผมหลังแตกจาก def เดียวกัน ไม่ต้องมี part แยก:

```csharp
var baseDef = _pool.Resolve(AvatarSlots.Base, string.Empty);
if (baseDef != null) layersToDraw.Add((baseDef.drawOrder, baseDef.spritePath));

foreach (var kvp in _appearance.Parts)
{
    var def = _pool.Resolve(kvp.Key, kvp.Value);
    if (def == null) continue;
    if (!string.IsNullOrEmpty(def.spritePathBack))
    {
        int backOrder = def.drawOrderBack > 0 ? def.drawOrderBack : (def.drawOrder - 30);
        layersToDraw.Add((backOrder, def.spritePathBack));
    }
    if (!string.IsNullOrEmpty(def.spritePath))
        layersToDraw.Add((def.drawOrder, def.spritePath));
}
layersToDraw.Sort((a, b) => a.order.CompareTo(b.order));
```
📎 Source: `Assets/Scripts/UI/Views/AvatarRenderer.cs`

### Framing Presets

`AvatarFraming`: `FullBody` / `Bust` / `HeadIcon` — art ชุดเดียวบน canvas **1024×1536**
ใช้ทั้ง 3 บริบท (scale 1 / 2.2 / 4.5 + pivot offset) เปลี่ยนระหว่าง runtime ด้วย `SetFraming(mode)`

🔒 **Invariant:** จุดกึ่งกลางศีรษะต้องอยู่ที่พิกัดพิกเซลเดียวกันเป๊ะในทุกไฟล์ art
มิฉะนั้น preset ทั้งสามเพี้ยนพร้อมกัน

### Performance guards

- Polling 0.1s + **signature guard** (`BuildSignature` จาก sorted `Parts.Keys`) — ไม่แตะ hierarchy ถ้า data ไม่เปลี่ยน
- **Sprite cache** — cache แม้ผลเป็น null กัน `Resources.Load` รัวตอน path พัง
- **Layer pooling** — `Stack<Image>` (`RentLayer`/`ReleaseAllLayers`) ไม่มี Instantiate/Destroy หลัง warm-up
- `IsVisible()` เช็ค `CanvasGroup.alpha > 0.01` — ปิด cost ตอนซ่อน/scroll พ้นจอ
- `AvatarPartPool` ถูกจ่ายผ่าน `Initialize(pool)` — Renderer เป็น MonoBehaviour บน prefab resolve DI เองไม่ได้; Presenter เรียกให้ตอน `OnOpen`

## UI — Customization Panel

แท็บบนสุด = category (`ใบหน้า` / `ลักษณะ` / `ร่างกาย`) เท่านั้น — ❌ ไม่มีแท็บ "ชุดแต่งกาย" (ถูกลบพร้อม outfit):

```
[ ใบหน้า ][ ลักษณะ ][ ร่างกาย ]        ← category tabs (AvatarSlots.Categories)
[ ใบหน้า ][ ตา ][ คิ้ว ][ ปาก ][ จมูก ]   ← slot tabs ของ category "ใบหน้า" (ตัวอย่าง)
┌─────────────────────────────┐
│  grid ของ GetPartsForSlot   │
└─────────────────────────────┘
[ สุ่ม ]              [ ยกเลิก ][ ยืนยัน ]
```

Panel layout: `UIRoot.ApplyLayout()` จับ prefab ชื่อขึ้นต้น `AvatarCustomization` →
full-screen modal กลางจอ 900×620, `ContentSizeFitter` ต้อง `Unconstrained`

### ViewData structs

```csharp
public struct AvatarCategoryTabViewData { public string Category; public string DisplayName; public bool IsActive; }
public struct AvatarSlotTabViewData     { public string Slot;     public string DisplayName; public bool IsActive; }
public struct AvatarPartOptionViewData  { public string PartId;   public string DisplayName;
                                          public string SpritePath; public bool IsSelected; public bool IsLocked; }
```
📎 Source: `Assets/Scripts/UI/Views/AvatarCustomizationViewData.cs`

### Presenter — Draft / Diff-Commit / Rollback / External Sync

- `OnOpen` → `Clone()` ของจริงมาเป็น `_original` (snapshot) + `_draft` (ร่างที่ผู้เล่นแก้)
- แก้ draft → `RenderPreview` ทันที (ยังไม่แตะ state) → `SetDirty` เปิดปุ่มยืนยัน
- ยืนยัน → diff draft กับของจริง → เรียก `TryChangeAvatarPart()` **เฉพาะ slot ที่ต่าง**; ถ้า slot ไหน fail กลางทาง → rollback slot ที่ผ่านไปแล้วทั้งหมด (กันตัวละครลูกผสมครึ่ง ๆ)
- ยกเลิก → ทิ้ง draft (`_draft = _original.Clone()`) — ไม่ต้อง rollback เพราะไม่เคยแตะ state จริง
- External sync → subscribe `AvatarEquipmentChangedMessage` เผื่อ AI Agent แก้ระหว่างเปิดพาเนล — sync เฉพาะ slot ที่ผู้เล่นไม่ได้แตะ (ไม่ทับงานผู้เล่น), `_isCommitting` เป็น guard กัน echo ของตัวเอง
- `OnRandomize` → สุ่มทุก slot ใน `Equippable` จาก `GetPartsForSlot(slot, effectivePose, sex)` — กรองหัว/ผมตามเพศศิษย์ (`disciple.Sex` ที่ capture ตอนเปิด panel)
- View = View layer only (วาด / รับคลิก / ยิง event) — Object Pooling 3 ชุด (`_optionPool`, `_slotTabPool`, `_catTabPool`), `RemoveAllListeners()` ตอน release กัน closure ค้าง, ปุ่ม tab เลือก = สีทอง

📎 Source: `Assets/Scripts/UI/Presenters/AvatarCustomizationPresenter.cs`, `Assets/Scripts/UI/Views/AvatarCustomizationView.cs`, `Assets/Scripts/UI/Views/AvatarOptionButton.cs`

## State Mutation Choke Point

```csharp
public bool TryChangeAvatarPart(string discipleId, string slot, string partId,
                                out string failReason, out AvatarAppearance result)
{
    failReason = string.Empty;
    result = null;
    var disciple = _state.Disciples.FirstOrDefault(d => d.DiscipleId == discipleId);
    if (disciple == null) { failReason = $"ไม่พบศิษย์ {discipleId}"; return false; }
    if (System.Array.IndexOf(AvatarSlots.Equippable, slot) < 0)
        { failReason = $"Invalid slot: {slot}"; return false; }
    if (!_avatarPartPool.IsValidForSlot(slot, partId))
        { failReason = $"ชิ้นส่วน '{partId}' ไม่ตรงกับ slot '{slot}'"; return false; }
    // Pose validation — part ที่ poseId ระบุไว้ไม่ตรงกับ pose จริงของศิษย์ → reject
    // (pose เดียวตอนนี้ ไม่เคย fail — กันภาพพังวันที่มี pose ที่ 2; "" = universal)
    if (!string.IsNullOrEmpty(partId))
    {
        var partDef = _avatarPartPool.GetById(partId);
        if (partDef != null && !string.IsNullOrEmpty(partDef.poseId))
        {
            string effectivePose = (disciple.Avatar != null && !string.IsNullOrEmpty(disciple.Avatar.PoseId))
                ? disciple.Avatar.PoseId
                : "pose_idle_01";
            if (partDef.poseId != effectivePose)
            {
                failReason = $"Part '{partId}' requires pose '{partDef.poseId}' but disciple is in pose '{effectivePose}'.";
                return false;
            }
        }
    }

    var oldPart = disciple.Avatar.GetSlot(slot);
    disciple.Avatar.SetSlot(slot, partId);
    _avatarChangedPublisher.Publish(new AvatarEquipmentChangedMessage
    {   DiscipleId = discipleId, Slot = slot, OldPartId = oldPart, NewPartId = partId });
    result = disciple.Avatar.Clone();   // return copy, not reference to live state
    return true;
}
```
📎 Source: `Assets/Scripts/Systems/SectStateProvider.cs`

Validation 4 ชั้น: มีศิษย์ → slot อยู่ใน `Equippable` → partId valid สำหรับ slot → poseId ตรงกับ effective pose (`""` = universal ผ่านเสมอ; มี pose เดียวตอนนี้ขั้นนี้จึงไม่เคย reject)
❌ `TryApplyOutfit()` **ถูกลบแล้ว** — ถ้าเจอใน branch เก่าให้ลบทิ้ง

ศิษย์ใหม่ได้ starter avatar ทันทีจาก `CreateStarterAvatar(index, sex)` —
`body_robe_grey` + head ตาม `DiscipleSex` + ทรงผม round-robin จาก
`StarterHair = { "hair_short", "hair_topknot", "hair_twin_tail" }`

## Messages & Interprocess Wire Types

```csharp
[MessagePackObject]
public class AvatarEquipmentChangedMessage
{
    [Key(0)] public string DiscipleId { get; set; } = string.Empty;
    [Key(1)] public string Slot       { get; set; } = string.Empty;   // AvatarSlots.*
    [Key(2)] public string OldPartId  { get; set; } = string.Empty;
    [Key(3)] public string NewPartId  { get; set; } = string.Empty;
}

[MessagePackObject]
public class ChangeAvatarPartRequest
{
    [Key(0)] public string DiscipleId { get; set; } = string.Empty;
    [Key(1)] public string Slot       { get; set; } = string.Empty;
    [Key(2)] public string PartId     { get; set; } = string.Empty;   // "" = back to default
}
```
📎 Source: `Assets/Scripts/Shared/GameMessages.cs`

- Topic: `InterprocessTopics.AvatarEquipmentChanged = "sect.avatar_equipment_changed"`
- ปลายทาง request ฝั่ง Unity: `ChangeAvatarPartHandler` (`IAsyncRequestHandler` → เรียก `TryChangeAvatarPart`)
- ❌ ลบแล้ว: `AvatarOutfitChangedMessage`, `ApplyOutfitRequest/Response`
- **ข้อจำกัดปัจจุบัน:** ฝั่ง Unity register wire ครบแล้ว แต่ `McpBridge/Program.cs` ยังไม่มี MCP tool `change_avatar_part` — tools ที่ bridge เปิดตอนนี้ = `get_sect_state`, `await_next_world_event`, `execute_decision`, `purchase_item` — อย่างไรก็ตาม snapshot ของ `get_sect_state` รวม `Avatar` (dictionary) ของทุกศิษย์มาให้ AI อ่านอยู่แล้ว

## DI Registration

```csharp
// Avatar part definitions loaded from Resources/Data/avatar_parts.json
builder.Register<AvatarPartPool>(Lifetime.Singleton);
builder.Register<Xianxia.Sect.UI.AvatarCustomizationPresenter>(Lifetime.Transient);
// Request/response: changing a disciple's avatar part
messagePipeBuilder.RegisterTcpRemoteRequestHandler<ChangeAvatarPartRequest, ChangeAvatarPartResponse>(interprocess);
builder.RegisterAsyncRequestHandler<ChangeAvatarPartRequest, ChangeAvatarPartResponse, ChangeAvatarPartHandler>(options);
// Interprocess pub/sub: broadcast avatar equipment changes to UI + MCP client
messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, AvatarEquipmentChangedMessage>(interprocess);
```
📎 Source: `Assets/Scripts/Core/GameLifetimeScope.cs`

`UIBootstrap` เปิด panel นี้ตอนเกมเริ่มเพื่อทดสอบ (ศิษย์ d001)

## Roadmap

| # | งาน | ผลกระทบ | สถานะ |
| --- | --- | --- | --- |
| 1 | แตก `head` → face_shape + eyes + brows + nose + mouth พร้อม part จริงใน JSON | 3 ใบหน้า → หลักหมื่น 🔥 | ⏳ ลำดับแรก |
| 2 | เพิ่ม slot `beard` (หนวด/เครา) | ตรงกับ reference (觅长生) | ⏳ |
| 3 | Color picker ให้ `tintable` ใช้ได้จริง (`Colors` dict) | variety เพิ่มโดยไม่เพิ่มไฟล์ | ⏳ schema พร้อม |
| 4 | แทน placeholder art ด้วย art จริง | 🎨 80% ของ "ความเหมือน" | ⏳ |
| 5 | `sexTag` filter ในกริด | หญิงไม่เห็นหัวชาย | ⏳ data พร้อม รอเปิดใช้ |
| 6 | Outfit / multi-pose | เฉพาะเมื่อมี pose ที่ 2 เข้าโปรเจกต์ | 🔒 ยกเลิกใน MVP |

## Related Pages

[[entities/disciples|Disciples]] — เจ้าของ Avatar field
[[sources/avatar-appearance|Design Notes & Decisions]] — เหตุผลเบื้องหลัง + decision record (ทำไมตัด outfit)
[[sources/sex-gender-system|Sex / Gender System]] — `DiscipleState.Sex` implemented; `sexTag` ถูกอ่านตอน Randomize แล้ว (grid filter = Roadmap)
[[concepts/state-management|State Management]] — MessagePack field placement
[[concepts/mvp-ui|MVP UI Pattern]] — customization panel flow
[[concepts/data-pipeline|Data Pipeline]] — authoring AvatarParts
[[concepts/mcp-bridge|MCP Bridge]] — MCP read/write exposure