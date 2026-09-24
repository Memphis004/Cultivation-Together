---
title: Avatar Appearance — Design Notes & Decisions
type: gdd
sources:
  - UnityProject/Assets/Scripts/Shared/SectEconomyState.cs
  - UnityProject/Assets/Scripts/Data/AvatarPartPool.cs
  - UnityProject/Assets/Scripts/UI/Views/AvatarRenderer.cs
  - UnityProject/Assets/Resources/Data/avatar_parts.json
related:
  - "[[entities/avatar-appearance]]"
  - "[[entities/disciples]]"
  - "[[sources/sex-gender-system]]"
  - "[[concepts/state-management]]"
created: 2026-09-01
updated: 2026-09-24
confidence: high
tags: [avatar, design-decision, pose, outfit, face-customization]
---

# Avatar Appearance — Design Notes & Decisions

> ⚠️ **BANNER (Phase 1, 2026-09-22):** สถาปัตยกรรม **render** ของหน้าตาตัวละครถูกแทนด้วย
> [[disciple-visual-system]] (3 backend: Portrait / SpriteSheet / Spine) — ส่วน §1 (outfit
> rejected) และ §2 (poseId/sexTag เก็บไว้) ในเอกสารนี้ **ยังมีผลควบคุมทั้งหมด**
> (decision record ห้ามฟื้น outfit ยัง enforce อยู่); หน้านี้คงค่าเป็น design notes
> ฝั่ง Portrait/state ที่ยังใช้งานอยู่

## 0. สถานะปัจจุบัน (v2.5 parts-only MVP — implemented; v3 outfit packages rolled back 2026-09-03, do not regress)

- `AvatarAppearance = { [Key(0)] Parts: slot→partId, [Key(1)] Colors: slot→colorId,
  [Key(2)] PoseId }` บน `DiscipleState.Avatar [Key(6)]`
  — `""` / absent = default ของ Def-table
- **Slots**: `base`(fixed) + equippable `body, head, eyes, brows, mouth, nose,
  hair, face_marking, eyeshadow, accessory`
  → UI categories `ใบหน้า / ลักษณะ / ร่างกาย` (`AvatarSlots.Categories`)
- **AvatarPartDef**: `id, slot, category, displayName, spritePath, spritePathBack,
  thumbPath, drawOrder, drawOrderBack, isDefault, tintable, poseId, sexTag`
- **Draw stack**: `base 0 → hair_back 10 → body 20 → head 30 → brows 31 → eyes 32
  → nose 33 → mouth 34 → face_marking 35 → eyeshadow 36–39 → hair_front 40 → accessory 50`
  (face window ขยายตาม face split 2026-09-24 — ดู [[concepts/disciple-visual-system]] Track ขนาน)
- **AvatarFraming** presets `FullBody / Bust / HeadIcon` บน canvas 1024×1536
  — head-center ต้องอยู่พิกัดเดียวกันทุกไฟล์
- **Mutation choke point** `SectStateProvider.TryChangeAvatarPart()`
  → broadcast `AvatarEquipmentChangedMessage`
- **UI**: draft / diff-commit / rollback + external sync
- **Interprocess**: `ChangeAvatarPartRequest/Response` (bridge tool กำลังทำ)
- **poseId / sexTag**: ถูกอ่านแล้ว 2 จุด — pose validation ใน `TryChangeAvatarPart()` + filter ใน `OnRandomize`
  (ยังไม่มี UI สลับ pose, grid ยังไม่กรอง sex — ดู §2 และ Roadmap ข้อ 5)

---

## 1. Decision Record — ❌ ยกเลิก "Outfit Package" (v3 draft)

> **สถานะ: REJECTED — 2026-09-03**
> ตาราง `outfits[]`, `OutfitDef`, `TryApplyOutfit()`, `GetOutfitsFor()`,
> `AvatarOutfitChangedMessage`, `ApplyOutfitRequest/Response`
> และแท็บ UI "ชุดแต่งกาย" — **ถูก implement ชั่วคราวใน v3 (commit 0d1a696) แล้ว rollback กลับเป็น parts-only
> (2026-09-03); โค้ด/JSON ปัจจุบันไม่มีอีกแล้ว**

### 1.1 ข้อเสนอเดิมคืออะไร

ร่าง v3 เสนอตารางที่สองใน `avatar_parts.json`:

```json
{
  "outfits": [
    { "id": "outfit_outer_male", "displayName": "ชุดศิษย์นอก (ชาย)",
      "poseId": "pose_idle_01", "sexTag": "male",
      "parts": { "body": "body_robe_grey", "head": "head_male_01",
                 "hair": "hair_short" } }
  ],
  "parts": [ /* ... */ ]
}
```

เหตุผลตอนนั้น: ภาพ reference แต่ละชุด**มาพร้อม pose ของตัวเอง** (ชี้มือ / กอดอก / ถือพัด)
ซึ่ง pure-slot model แสดงไม่ได้ เพราะ

1. art ของ `body` ผูกกับ pose (แขนเสื้ออยู่ตามแขนของ pose นั้น)
2. `head`/`hair` ต้องวาดให้เข้ากับองศาคอของ pose เดียวกัน

### 1.2 ทำไมถึงปฏิเสธ

| # | เหตุผล | รายละเอียด |
|---|---|---|
| 1 | **ไม่มีข้อมูลใหม่** | `outfit.parts` = "กด `SetSlot` หลายครั้งรวดเดียว" — derive จาก `parts[]` ได้ 100% ไม่มีบิตใดที่ `parts[]` ไม่มีอยู่แล้ว |
| 2 | **สร้าง dual source of truth** | ลบ part ใน `parts[]` แต่ลืมลบใน `outfits[]` → `TryApplyOutfit` พังตอน runtime ต้องเขียน cross-table validator = งานงอกจากตารางที่ไม่จำเป็น |
| 3 | **spec เองก็ยอมรับว่า runtime ไม่ต้องรู้จัก** | *"Parts REMAINS the source of truth. Renderer never needs OutfitId."* — ถ้า renderer ไม่ใช้ และ state ไม่เก็บ `OutfitId` → มันคือ **UI preset** ไม่ใช่ data model |
| 4 | **เงื่อนไขที่ทำให้มันจำเป็นยังไม่เกิด** | outfit มีค่าก็ต่อเมื่อ **มี pose ≥ 2** ตอนนี้ทุก part เป็น `pose_idle_01` เท่านั้น → ไม่มีอะไรให้สลับ |
| 5 | **ผิดลำดับความสำคัญ** | ปัญหาจริงของ MVP ไม่ใช่ "เปลี่ยนชุดยาก" แต่คือ **"ปั้นหน้าไม่ได้"** (ดู §3) |

### 1.3 เงื่อนไขที่จะ "รื้อฟื้น" ข้อเสนอนี้

🔓 นำกลับมาพิจารณา **เมื่อ (และเฉพาะเมื่อ) มี pose ที่สองเข้าโปรเจกต์จริง**
เพราะ ณ จุดนั้น:

| สถานการณ์ | 1 pose (ตอนนี้) | 2+ poses |
|---|---|---|
| ผสม `body`(กอดอก) + `head`(ยืนตรง) | เป็นไปไม่ได้ | **ภาพพัง — คอเอียงคนละองศา** |
| `poseId` validation | ผ่านเสมอ = ไร้ค่า | กันภาพพังได้จริง |
| `outfits` | shortcut เฉยๆ | **ทางเดียวที่ผู้เล่นจะเปลี่ยน pose ได้** |

> 💡 **บทเรียนที่ควรจำ:** `outfits` ไม่ใช่ "ชุดเสื้อผ้า" — มันคือ **"ปุ่มเปลี่ยน pose"** ที่ปลอมตัวมา

---

## 2. Decision Record — ✅ เก็บ `poseId` / `sexTag` ไว้บน part

> **สถานะ: ACCEPTED — เก็บ field และให้โค้ดอ่านเท่าที่จำเป็น (validation + randomize)**
> ยัง **ไม่** มี: UI เลือก pose, pose-switching, sexTag filter ในกริดตัวเลือก

เก็บ **field** ไว้ และเปิดให้โค้ด**อ่าน**ในวงจำกัด (pose validation + randomize filter)
— กันภาพพังและกันสุ่มเพศผิด โดยไม่สร้างฟีเจอร์ (UI) ที่ยังไม่มี pose ที่ 2 ให้ใช้

| | ลบทิ้ง | เก็บไว้ |
|---|---|---|
| **ตาราง / โค้ด outfit** | ✅ ลบ | — |
| **`poseId` บน AvatarPartDef** | — | ✅ เก็บ + อ่านใน pose validation (`TryChangeAvatarPart`) และ filter `OnRandomize` |
| **`sexTag` บน AvatarPartDef** | — | ✅ เก็บ + filter `OnRandomize` (grid ยังไม่ filter — Roadmap ข้อ 5) |
| **`[Key(2)] PoseId` ใน state** | — | ✅ เก็บ — validation เทียบกับ effective pose (`""` → `pose_idle_01`) |

**หลักที่ใช้ตัดสิน:**
> ลบ **โค้ดและตาราง** ที่ยังไม่ได้ใช้ (เขียนใหม่ทีหลังฟรี)
> แต่เก็บ **การ tag ข้อมูล** ที่ต้องอาศัยความจำของมนุษย์ (ย้อนกลับไปทำทีหลังแพงมาก)

เหตุผลรูปธรรม: วันที่เพิ่ม pose ที่ 2 คุณจะไม่ต้องเปิดไฟล์ art เก่า 200 ชิ้น
มานั่งเดาว่าชิ้นไหนวาดทับเทมเพลตอันไหน — ซึ่งตอนนั้น**ไม่มีทางจำได้แล้ว**

**สิ่งที่ทำแล้ว (พอดี ๆ ไม่เกินเลย):**
- ✅ pose validation ใน `TryChangeAvatarPart` — part ที่ poseId ไม่ตรง effective pose จะถูก reject
  (pose เดียวตอนนี้ → ไม่เคย reject จริง แต่ต้นทุน ~0 และกันภาพพังวันที่ pose ที่ 2 เข้า)
- ✅ `OnRandomize` กรองตัวเลือกด้วย `GetPartsForSlot(slot, effectivePose, sex)`
  — ศิษย์ชายไม่สุ่มได้หัว/ผมของเพศหญิง (และ vice versa)

**สิ่งที่ต้องไม่ทำ (ยัง):**
- ❌ อย่าใส่ UI ให้เลือก pose / pose-switching
- ❌ อย่าเปิด sexTag filter ในกริดตัวเลือกก่อน Roadmap ข้อ 5

---

## 3. ช่องว่างที่แท้จริงของ MVP — `head` เป็นก้อนเดียว

### 3.1 ปัญหา

เกม 捏脸 (character creator) แนวเดียวกันแยก **รูปหน้า / ตา / คิ้ว / จมูก / ปาก / หนวด**
เป็นคนละ slot ให้เลือกอิสระ แต่โปรเจกต์เรา `head_male_01` = **ใบหน้าสำเร็จรูปทั้งใบ**

| | โปรเจกต์เรา (ตอนนี้) | เป้าหมาย |
|---|---|---|
| slot ที่มี data | 6 | 11+ |
| จำนวนใบหน้าที่เป็นไปได้ | **3** | หลักแสน |
| ไฟล์ art ที่ต้องใช้ | 3 | ~50 |

การเพิ่มไฟล์ `head_*.png` เป็น 20 ชิ้น ก็ยังได้แค่ "หน้าสำเร็จรูป 20 แบบ"
— **ไม่ใช่ 捏脸** เพราะ combination ไม่โต

### 3.2 ทำไม slot ย่อยถึงคุ้ม

$\text{combinations} = \prod_{s \in \text{slots}} |P_s|$

แตกใบหน้าเป็น 5 slot ที่มี slot ละ ~8 ตัวเลือก → $8^5 = 32{,}768$ ใบหน้า
จากไฟล์ **40 ชิ้น** เทียบกับ 3 ใบหน้าจาก 3 ชิ้น — **ผลตอบแทนต่อไฟล์ art ต่างกันมหาศาล**

### 3.3 ข้อควรระวังเชิงเทคนิค

- ✅ **ซอย drawOrder แล้ว (2026-09-24)**: `head 30`, `brows 31`, `eyes 32`, `nose 33`,
  `mouth 34`, `face_marking 35`, `eyeshadow 36–39` — ที่ว่างพอโดยไม่ต้อง re-number เป็นหลักสิบ
  (eyeshadow วาดทับ brows จงใจ และอยู่ใต้ hair_front 40; `FaceSplitTests` ล็อก window นี้)
- ทุกชิ้นต้องวาดบน canvas 1024×1536 และอ้าง **head-center พิกัดเดียวกัน**
  ไม่งั้นตา/จมูกจะเลื่อนเมื่อสลับรูปหน้า
- `AvatarSlots.Categories["ใบหน้า"]` จะมี 5–7 slot → **slot tab bar ต้องเลื่อนได้**
  (ตอนนี้เป็น HorizontalLayoutGroup ธรรมดา)

---

## 4. Art Pipeline (ปรับตามการตัด outfit)

- **เทมเพลต PSD หนึ่งไฟล์ต่อหนึ่ง pose** — ตอนนี้มีแค่ `Avatar_Template_pose_idle_01.psd`
- ทุก part ที่ผูกกับ pose วาดทับเทมเพลตนั้น และ tag `poseId` ใน JSON
- part ที่ยืดหยุ่น (`face_marking` กลางหน้าผาก, accessory ง่ายๆ) ใช้ `poseId: ""` ได้
- `sexTag` ใส่บน `head` / `hair` / `body` ที่จำเพาะเพศ
- ❌ **ไม่ต้องทำ outfit thumbnail composite อีกแล้ว** — ประหยัดงาน pre-render ทั้งชุด

---

## 5. Roadmap (ลำดับที่คุ้มที่สุด)

| # | งาน | ไฟล์ | ผลลัพธ์ที่ผู้เล่นเห็น |
|---|---|---|---|
| 1 | **แตก `head` → `eyes/brows/nose/mouth/eyeshadow`** + part จริง ✅ (placeholder 41 parts ลงแล้ว 2026-09-24 — รอแทน art จริงในข้อ 4) | `avatar_parts.json`, art | 3 หน้า → หลักหมื่น 🔥 |
| 2 | เพิ่ม slot `beard` | `SectEconomyState.cs`, JSON | ครบตาม reference |
| 3 | Color picker ให้ `tintable` | View/Presenter | variety เพิ่มฟรี |
| 4 | แทน placeholder ด้วย art จริง | art | 🎨 **80% ของ "ความเหมือน"** |
| 5 | `sexTag` filter ในกริดตัวเลือก | Sex system implement แล้ว (Randomize กรองแล้ว) — เหลือแค่เปิดในกริด | หญิงไม่เห็นหัวชาย |
| 6 | *(ถ้ามี pose 2)* พิจารณา outfit ใหม่ | — | เปลี่ยนท่ายืน |

---

## 6. MCP Exposure

- `get_sect_state` พา `Avatar.Parts` / `Colors` / `PoseId` ไปด้วยอัตโนมัติ (schema append)
- Write tool = **`change_avatar_part`** ตาม `ChangeAvatarPartRequest/Response`
  (request-response — ไม่ใช่ pub/sub เพราะ agent ต้องได้ผลลัพธ์ทันที)
- ❌ **ไม่มี `apply_outfit`**

---

## 7. What does NOT change

Draft pattern, option-grid pooling, rollback, external sync, framing presets,
draw-stack ordering, `AvatarPartPool` fallback-to-default — **ทั้งหมดคงเดิม**

## Related Pages

- [[entities/avatar-appearance]] — สคีมา + โค้ดที่ implement จริง
- [[entities/disciples]] — appearance lives on this entity
- [[sources/sex-gender-system]] — `DiscipleState.Sex` implemented; `sexTag` ถูกอ่านใน Randomize แล้ว
- [[concepts/state-management]] — `[Key(N)]` append discipline
- [[concepts/data-pipeline]] — authoring AvatarParts
- [[concepts/mcp-bridge]] — MCP read/write exposure