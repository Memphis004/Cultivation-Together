---
title: DiscipleVisualSystem — แผน 3 Backend (Portrait / Spine / SpriteSheet)
type: gdd
status: draft v1
sources:
  - LLMWiki/wiki/sources/avatar-appearance.md
  - LLMWiki/wiki/entities/avatar-appearance.md
  - UnityProject/Assets/Scripts/Shared/SectEconomyState.cs
  - UnityProject/Assets/Scripts/Shared/MockSectData.cs
  - UnityProject/Assets/Scripts/Data/AvatarPartPool.cs
  - UnityProject/Assets/Scripts/Systems/SectStateProvider.cs
  - UnityProject/Assets/Scripts/Core/GameLifetimeScope.cs
  - UnityProject/Assets/Scripts/Core/SceneLoader.cs
  - marooned-wiki/wiki/sources/architecture/chibi-visual-system.md (pattern reference: IChibiVisual + dual backend)
related:
  - "[[sources/avatar-appearance]]"
  - "[[entities/avatar-appearance]]"
  - "[[entities/disciples]]"
  - "[[concepts/additive-scene-architecture]]"
  - "[[concepts/mvp-ui]]"
  - "[[sources/task-system]]"
created: 2026-09-22
updated: 2026-09-22
confidence: medium
tags: [visual, avatar, chibi, spine, sprite-sheet, portrait, tier, monetization, plan]
---

# DiscipleVisualSystem — แผน 3 Backend

> **สถานะ: draft v1.** ตัวเลข performance ในเอกสารนี้ **ยังไม่ได้วัด** — Phase 0 (spike) มีไว้เปลี่ยนตัวเลขเดาให้เป็นตัวเลขจริงก่อนลงมือสร้างระบบ
> เอกสารนี้ **ต่อยอด** [[sources/avatar-appearance]] (decision record §1–§2 ยังใช้ได้ทั้งหมด — โดยเฉพาะ "ห้ามฟื้น outfit") และแทนที่เฉพาะ **สถาปัตยกรรมการ render**

## 0. สรุปการตัดสินใจ

| # | Decision | เหตุผลสั้น ๆ |
|---|---|---|
| D1 | **State เดิมอยู่ต่อ** — `AvatarAppearance` (Parts/Colors/PoseId) เป็น source of truth เดียวของทั้ง 3 backend | เปลี่ยน schema = พัง save/wire/MCP โดยไม่ได้อะไรเพิ่ม; สิ่งที่ "ถูกแทน" คือชั้น rendering |
| D2 | **Portrait ไม่ใช่ tier** — ทุกคนมี Portrait; tier เลือกแค่ **world backend** (`SpriteSheet` \| `Spine`) | ร่างเดิมใส่ 3 ค่าใน enum เดียว ทั้งที่ Portrait (UI) กับ Spine/Sprite (ในฉาก) เป็นคนละแกน |
| D3 | **Tier = entitlement (เก็บใน state) + runtime budget (สปอว์นเนอร์ลดระดับเอง)** | เกิน budget แล้วตัวละครที่ซื้อเกมไม่ควร "หาย" — แค่ degrade เป็น Sprite ชั่วคราวโดยไม่แตะ state |
| D4 | **Resolve ครั้งเดียว, render สามทาง** — `AppearanceResolver` แปลง Appearance + Backend → `List<ResolvedLayer>` | กัน logic (fallback, order, tint) กระจาย 3 ที่ |
| D5 | **SpriteSheet tier = layered SpriteRenderer + central frame clock** ไม่ใช้ Animator ต่อตัว ไม่ bake texture ต่อตัว | 100–300 ตัว: Animator ×300 แพง, bake ต่อตัวกิน memory |
| D6 | **Spine tier = shared rig + skin mix-and-match** (+ rig เฉพาะสำหรับตัวละครเนื้อเรื่อง) | ถ้า rig ต่อ part/ต่อคน art จะระเบิด; 1 rig แล้วสลับ attachment |
| D7 | **เสนอเฉพาะ part ที่ทุก backend ของศิษย์คนนั้น render ได้** (coverage) | กันผู้เล่นเลือก part ที่ chibi ไม่มีภาพ |
| D8 | **ไม่ฟื้น outfit** — chibi มี "ท่า" (idle/walk/talk) แต่นั่นคือ *animation state* ไม่ใช่ `PoseId` ของ appearance | เงื่อนไขฟื้น outfit (มี pose ที่ 2 ใน portrait) ยังไม่เกิด |
| D9 | **Event-driven ทั้งหมด** ไม่มี polling ฝั่ง chibi | ตามแพทเทิร์น Lab B ของ Marooned; polling 0.1s ของ `AvatarRenderer` คงไว้เฉพาะ Portrait จนกว่าจะ verify ว่าเอาออกได้ |

## 1. ขอบเขต: อะไรคงเดิม / แทน / ใหม่

| ส่วน | สถานะ | หมายเหตุ |
|---|---|---|
| `AvatarAppearance`, `AvatarSlots`, `DiscipleState [Key(0–7)]` | ✅ **คงเดิม** | append-only เท่านั้น |
| `TryChangeAvatarPart()` (choke point) | ✅ คงเดิม + **เพิ่ม validation ชั้น 5–6** (coverage, entitlement) | §4.5 |
| `ChangeAvatarPartRequest/Handler`, `AvatarEquipmentChangedMessage` | ✅ คงเดิม | chibi subscribe ตัวเดียวกับที่ Presenter ใช้ |
| `AvatarPartPool` | 🔧 **ขยาย** field ของ `AvatarPartDef` (backward-compatible) | §4.2 |
| `AvatarRenderer` | 🔧 **refactor** ให้ implement `IPortraitVisual` + ใช้ resolver | ไม่เปลี่ยนภาพ |
| `AvatarCustomizationPresenter/View` | ✅ คงเดิม (เพิ่ม lock ใน Phase 5) | `AvatarPartOptionViewData.IsLocked` มีอยู่แล้ว |
| `DiscipleState.ChibiBackend [Key(8)]` | 🆕 | §4.1 |
| `AppearanceResolver`, `VisualTierPolicy` | 🆕 | §4.3, §7 |
| `DiscipleVisualSystem`, `ChibiSceneRoot` | 🆕 | §5 |
| `SpriteChibiVisual`, `ChibiFrameBank`, `ChibiFrameClock` | 🆕 | §6.2 |
| `SpineChibiVisual`, `ChibiActivityMap` | 🆕 | §6.3 |
| `DiscipleDetail` panel (Portrait) | 🆕 (แทน `DiscipleList` ที่เป็น stub) | Phase 4 |

## 2. สถาปัตยกรรม

```
                 ┌──────────────────────────────────────────┐
                 │ DiscipleState  (state — ไม่เปลี่ยนรูปแบบ)  │
                 │  Avatar {Parts, Colors, PoseId}   [Key 6] │
                 │  Sex                              [Key 7] │
                 │  ChibiBackend (entitlement)  🆕   [Key 8] │
                 └───────────────────┬──────────────────────┘
   TryChangeAvatarPart / TrySetChibiBackend / Recruit  (mutation choke points)
                                     │ publish (in-memory MessagePipe)
                                     ▼
                 ┌──────────────────────────────────────────┐
                 │ AppearanceResolver                        │
                 │ (Appearance + VisualBackend)              │
                 │   → List<ResolvedLayer> เรียงตาม Order     │
                 └──────┬──────────────┬──────────────┬─────┘
                        ▼              ▼              ▼
              ┌──────────────┐ ┌───────────────┐ ┌────────────────┐
              │ Portrait     │ │ SpriteSheet   │ │ Spine          │
              │ UGUI Image   │ │ SpriteRenderer│ │ SkeletonAnim.  │
              │ IPortrait-   │ │ ×N layers     │ │ shared rig +   │
              │ Visual       │ │ + FrameClock  │ │ skins          │
              └──────────────┘ └───────┬───────┘ └───────┬────────┘
                 UI detail /           └──── IChibiVisual ──┘
                 roster icon          ในฉาก: DiscipleVisualSystem เป็นคนตัดสิน
                                      "ตัวนี้ใช้ backend ไหน" (entitlement ∩ budget)
```

เปรียบเทียบ 3 backend:

| | Portrait | SpriteSheet | Spine |
|---|---|---|---|
| ใช้ที่ | UI (detail panel, roster `HeadIcon`) | ในฉาก | ในฉาก |
| ใครเห็น | ทุกคน | default ของทุกคน | entitled **และ** ยังไม่เกิน budget |
| Render | UGUI `Image` layers | `SpriteRenderer` layers | `SkeletonAnimation` |
| Animation | — | frame clock กลาง (data-driven) | Spine animation |
| จำนวนพร้อมกัน | 1 ถึงหลักสิบ | เป้า 100–300 | เป้า ≤ 20 (**ตัวเลขจริงหลัง S1**) |
| Tint (`tintable`) | `Image.color` | `SpriteRenderer.color` (vertex color — ไม่ทำลาย batch) | slot color |
| Art ต่อ part | 1024×1536 layer (เดิม) | sheet ต่อ slot บน grid เดียวกัน | attachment ใน skin ของ rig กลาง |

## 3. จุดที่ปรับจากร่างที่แปะมา

| ร่างเดิม | ปัญหา | ที่เสนอ |
|---|---|---|
| `enum VisualTier { Portrait, Spine, SpriteSheet }` | ผสมสองแกน (UI vs ในฉาก) — เลือก "Portrait" แล้ว chibi ในฉากใช้อะไร? | `VisualBackend` (3 ค่า, ไว้ resolve) แยกจาก `ChibiBackend` (2 ค่า, เก็บใน state) |
| `DiscipleVisualController.SetTier()` ตัวเดียวสลับ 3 โหมด | god class, ทุก field อยู่ใน prefab เดียว | interface แยก (`IPortraitVisual`, `IChibiVisual`) + factory ต่อ backend (แพทเทิร์นเดียวกับ Marooned) |
| Sprite: 8 ทิศ × 4 เฟรม, Animator + sprite swap | art ต่อ part ×4 เมื่อเทียบกับ 2 ทิศ; Animator ×300 | เริ่ม **2 ทิศ (ซ้าย/ขวา flip)** ตัดสินหลัง S3; frame clock กลาง |
| "64×64" | ตาประมาณจากภาพ reference: chibi สูงราว 60–90 px บนจอ 1080p — 64 อาจเล็กไป/ใหญ่ไปแล้วแต่ zoom | ล็อกขนาด cell ใน S3 (เริ่มทดลอง 96×96) |
| Perf: Spine ~10–20 ms, Sprite ~2–5 ms, รวมแล้ว "40–80 FPS" | เป็นตัวเลขที่ไม่มีที่มา และรวมกันแบบผิดหลัก | ใช้เป็นแค่ทิศทาง; **gate จริง = วัดใน Phase 0** |
| "Free = limited customization" | ไม่ระบุว่า limit อะไร | ทำเป็น flag `entitlement` ต่อ part (§8) |

## 4. Data model

### 4.1 State — `Shared/SectEconomyState.cs` (แก้ที่ top-level `Shared/` แล้วรัน `sync-shared.sh`)

```csharp
// entitlement ของศิษย์: "ได้สิทธิ์" แสดงแบบไหนในฉาก — ไม่ใช่สิ่งที่ render จริงเสมอไป
// (runtime อาจ degrade Spine → SpriteSheet เมื่อเกิน budget โดยไม่แก้ค่านี้)
public enum ChibiBackend { SpriteSheet = 0, Spine = 1 }   // 0 = default → save/roster เก่าได้ SpriteSheet

[MessagePackObject]
public class DiscipleState
{
    // [Key(0)]..[Key(7)] เดิม — ห้ามแตะ
    [Key(8)] public ChibiBackend ChibiBackend { get; set; } = ChibiBackend.SpriteSheet;
}
```

- ตามหลัก *"runtime state holds ids/flags, never hidden derivations"* → เก็บเป็น flag ชัดเจน ไม่ derive จาก Rank ตอน render
- `MockSectData`: d000 (SectMaster) และ d003 (Elder Zhao) = `Spine`, d001/d002 = `SpriteSheet` → ใช้ทดสอบทั้งสองทางตั้งแต่ Phase 3

### 4.2 `AvatarPartDef` — เพิ่ม field แบบ flat (JsonUtility-safe, JSON เก่ายังโหลดได้)

```csharp
// Portrait (เดิม): spritePath, spritePathBack, thumbPath, drawOrder, drawOrderBack
// --- SpriteSheet chibi ---
public string chibiSheetPath;      // "" = part นี้ไม่มี layer บน chibi
public string chibiSheetPathBack;  // ผมหลัง
public int    chibiOrder;          // 0 = ใช้ค่า default ของ slot
public int    chibiOrderBack;
// --- Spine ---
public string spineSkin;           // เช่น "hair/hair_topknot_long"; "" = ไม่มี
// --- Access (Phase 5; ว่าง = free) ---
public string entitlement;         // "" | "owner" | "dlc:<packId>"
```

**Coverage เป็นค่า derive ตอน load ไม่เก็บใน JSON** (กัน dual source of truth แบบที่เคยเจอกับ outfit):

```csharp
public bool Supports(VisualBackend b)
{
    switch (b)
    {
        case VisualBackend.Portrait:    return !string.IsNullOrEmpty(spritePath);
        case VisualBackend.SpriteSheet: return !string.IsNullOrEmpty(chibiSheetPath);
        case VisualBackend.Spine:       return !string.IsNullOrEmpty(spineSkin);
        default: return false;
    }
}
```

### 4.3 Slot × Backend matrix + Resemblance rule

| slot | Portrait | SpriteSheet | Spine |
|---|---|---|---|
| base | ✔ | — (รวมอยู่ใน body sheet) | — (รวมอยู่ใน rig) |
| body | ✔ | ✔ | ✔ |
| head (→ `face_shape` หลัง split) | ✔ | ✔ (chibi head) | ✔ |
| eyes / brows / nose / mouth / eyeshadow | ✔ | ✖ | ✖ |
| hair (+back) | ✔ | ✔ | ✔ |
| face_marking | ✔ | ◻ optional | ◻ optional |
| accessory | ✔ | ✔ | ✔ |

**Resemblance rule** (สิ่งที่ต้องตรงกันทุก backend ไม่งั้นผู้เล่นจะไม่รู้ว่านี่คือคนเดียวกัน): `sex`, `body`, `hair` (ทรง+สี), `head` (ตระกูลใบหน้า), `accessory`, `Colors` — ส่วนรายละเอียดใบหน้า (ตา/คิ้ว/จมูก/ปาก) เป็น **Portrait-only** ซึ่งทำให้ Roadmap #1 (แตก head) กระทบเฉพาะ Portrait

### 4.4 ผลลัพธ์ของ resolver

```csharp
namespace Xianxia.Sect.Visual
{
    public enum VisualBackend { Portrait, SpriteSheet, Spine }

    public struct ResolvedLayer
    {
        public string Slot;
        public string PartId;
        public string Asset;      // spritePath / chibiSheetPath / (Spine ไม่ใช้)
        public string SpineSkin;  // เฉพาะ Spine
        public int    Order;
        public bool   IsBack;
        public UnityEngine.Color Tint;  // white ถ้าไม่ tintable
    }

    public class AppearanceResolver
    {
        private readonly AvatarPartPool _pool;
        public AppearanceResolver(AvatarPartPool pool) { _pool = pool; }

        // ลำดับ: partId → def → ถ้า !def.Supports(backend) ให้ fallback เป็น default ของ slot
        // → ถ้า default ก็ไม่รองรับ ให้ข้าม slot + log ครั้งเดียวต่อ (slot, backend)
        public System.Collections.Generic.List<ResolvedLayer> Resolve(AvatarAppearance a, VisualBackend backend) { /* ... */ }

        // ใช้เป็น signature guard แทน BuildSignature เดิม (รวม backend เข้าไปด้วย)
        public string Signature(AvatarAppearance a, VisualBackend backend) { /* ... */ }
    }
}
```

### 4.5 Mutation + messages

- `ISectStateProvider` เพิ่ม `bool TrySetChibiBackend(string discipleId, ChibiBackend backend, out string failReason)` — **หมายเหตุ:** interface นี้ประกาศอยู่ท้ายไฟล์ `TimeSystem.cs` ไม่ใช่ไฟล์ของตัวเอง ต้องแก้ตรงนั้นด้วย
- `TryChangeAvatarPart()` เพิ่มชั้น validation (เดิมมี 4 ชั้น):
  - **ชั้น 5 — coverage:** part ต้อง `Supports()` ทุก backend ที่ศิษย์คนนั้นอาจใช้ = `{Portrait, SpriteSheet}` และถ้า entitled Spine ให้เพิ่ม `Spine` **(ต้องรวม SpriteSheet เสมอ เพราะ budget fallback)** — บังคับเฉพาะ backend ที่ `VisualRuntimeConfig` เปิดแล้ว (ไม่งั้น Phase 1 ที่ยังไม่มี chibi asset จะ reject ทุกอย่าง)
  - **ชั้น 6 — entitlement:** Phase 5
- Message ใหม่ (in-memory เท่านั้น — **ไม่ต้อง register interprocess**):

```csharp
[MessagePackObject] public class DiscipleChibiBackendChangedMessage
{ [Key(0)] public string DiscipleId; [Key(1)] public ChibiBackend Old; [Key(2)] public ChibiBackend New; }

[MessagePackObject] public class DiscipleSelectedMessage      // คลิก chibi → เปิด detail
{ [Key(0)] public string DiscipleId; }

[MessagePackObject] public class SceneUnloadedMessage         // SceneLoader ยังไม่ publish — ต้องเพิ่ม
{ [Key(0)] public string SceneName; }
```

> ⚠️ **สังเกตจากโค้ดจริง:** `SectStateProvider` publish `AvatarEquipmentChangedMessage` ผ่าน `IPublisher<T>` (in-memory) เท่านั้น แม้ `GameLifetimeScope` จะ register interprocess broker ไว้ด้วย — เอกสารเดิมเขียนว่า "broadcast ถึง MCP client" แต่ตามโค้ดตอนนี้ bridge ยังไม่ได้รับ ไม่กระทบแผนนี้ (chibi subscribe แบบ in-memory ตรงกับที่ publish) แต่ควรแก้เอกสารหรือโค้ดให้ตรงกัน

## 5. ตัวคุมในฉาก: `DiscipleVisualSystem`

หน้าที่: ตัดสินว่าศิษย์แต่ละคน "แสดงเป็นอะไร" แล้ว reconcile กับสิ่งที่อยู่บนฉากตอนนี้

```csharp
public interface IChibiVisual : System.IDisposable
{
    VisualBackend Backend { get; }
    UnityEngine.Transform Transform { get; }
    void Bind(string discipleId, AvatarAppearance appearance, DiscipleSex sex);  // apply เต็ม
    void ApplySlot(string slot, string partId);        // incremental จาก AvatarEquipmentChangedMessage
    void SetActivity(ChibiActivity activity);          // Idle / Walking / Talking / Working / Resting
    void SetFacing(bool faceRight);
    void PlayAction(string actionId);                  // one-shot: "pickup", "attack" ...
    void SetSortingBase(int order);
}

public interface IPortraitVisual
{
    void Bind(AvatarAppearance appearance);
    void SetFraming(AvatarFraming framing);            // FullBody / Bust / HeadIcon (เดิม)
}
```

```csharp
public class DiscipleVisualSystem : IStartable, System.IDisposable
{
    // inject: ISectStateProvider, AppearanceResolver, VisualTierPolicy, factories (enum→factory map, ไม่ใช้ reflection),
    //         ISubscriber<DiscipleRecruitedMessage>, ISubscriber<AvatarEquipmentChangedMessage>,
    //         ISubscriber<DiscipleChibiBackendChangedMessage>, ISubscriber<SceneLoadedMessage>, ISubscriber<SceneUnloadedMessage>
    private readonly Dictionary<string, IChibiVisual> _active = new Dictionary<string, IChibiVisual>();
    private ChibiSceneRoot _root;  // ผูกตอน SceneLoadedMessage

    // Reconcile จาก state ทั้งหมด — เรียกตอน scene load, promotion, budget เปลี่ยน
    // event ย่อย (recruit / avatar change) ทำ incremental ผ่านเส้นทางเดียวกัน
    public void Reconcile() { /* plan = tierPolicy.PlanBackends(...); diff กับ _active */ }
}
```

**Scene binding (จุดที่พลาดง่าย):** `GameLifetimeScope` เป็น root scope เดียวใน CoreScene และ GameplayScene **ไม่มี LifetimeScope** — MonoBehaviour ที่โหลดทีหลังด้วย additive จึง constructor-inject ไม่ได้ และ `RegisterComponentInHierarchy` เห็นเฉพาะของที่มีตอน container build ทางแก้ที่ตรงกับโค้ดที่มี: `ChibiSceneRoot` (MonoBehaviour เก็บ parent transform, anchor, sorting config) อยู่ใน GameplayScene แล้ว `DiscipleVisualSystem` **สแกน root objects ของ scene ที่เพิ่งโหลด** ตอนรับ `SceneLoadedMessage` (แบบเดียวกับที่ `SceneLoader.RemoveDuplicateSingletons` ทำอยู่) — ไม่ใช้ `FindObjectOfType` ต้องเพิ่ม `SceneUnloadedMessage` ใน `SceneLoader.UnloadCurrentGameplayScene()` เพื่อล้าง registry ที่ชี้ object ที่ถูกทำลาย

## 6. รายละเอียดแต่ละ backend

### 6.1 Portrait
- `AvatarRenderer` implement `IPortraitVisual`, ย้ายการ resolve/order/fallback ไปใช้ `AppearanceResolver.Resolve(a, Portrait)` — **ต้องได้ layer list เหมือนเดิมเป๊ะ** (test: 4 founder × ลำดับ layer)
- ไม่แตะ polling 0.1s + signature guard ใน Phase 1 (Presenter ยังพึ่งอยู่ — ตามเอกสารเดิม "แก้ draft → RenderPreview ทันที" น่าจะซ้ำซ้อน แต่ต้อง verify ก่อนเอาออก)
- ใช้ใน `DiscipleDetail` (ภาพ portrait ใหญ่แบบภาพที่ 1) และ roster ซ้ายด้วย `AvatarFraming.HeadIcon`

### 6.2 SpriteSheet
- Prefab `ChibiSprite`: root + layer `SpriteRenderer` แบบ pool (body, hair_back, head, face_marking, hair_front, accessory)
- ทุก sheet ของทุก slot ใช้ **grid เดียวกัน / pivot ที่เท้าเหมือนกัน** (invariant แบบเดียวกับ "head-center ต้องตรงกันทุกไฟล์" ของ Portrait) แล้วรวมใน Sprite Atlas เดียว → batch ได้
- `ChibiFrameBank` (plain C#): (partId, state, dir) → `Sprite[]` โหลดตาม convention ชื่อ/metadata
- **State อยู่ใน data ไม่ใช่ code** — `chibi_anim.json`: `{ state, fps, frames, loop }` (บทเรียนจาก Marooned: ชื่อ state ไม่แชร์กันข้าม asset เลยย้ายเป็น data)
- `ChibiFrameClock : ITickable` ตัวเดียววน visual ทั้งหมด, เปลี่ยน `sprite` เฉพาะตอน frame index เปลี่ยน, สุ่ม phase เริ่มต้นกันขยับพร้อมกัน
- ✖ ไม่ใช้ `Animator` ต่อตัว ✖ ไม่ bake texture ต่อตัว (ประมาณคร่าว ๆ: 3 ทิศ × ~12 เฟรม × 128² RGBA ≈ 2.3 MB/ตัว → 300 ตัว ≈ 700 MB)
- Facing: flip ที่ parent (`localScale.x`), Sorting: `sortingOrder = baseFromY + layerIndex`
- Culling นอกจอ: ทำเมื่อ S2 บอกว่าจำเป็นเท่านั้น

### 6.3 Spine
- **Rig กลาง `chibi_base`** slot ตรงกับ `AvatarSlots` (hair_back, body, head, face_marking, hair_front, accessory); animation: `Idle, Walking, Talking, Working, Resting, Pickup` เป็นต้น
- Customization: `new Skin(discipleId)` + `AddSkin()` ของ `spineSkin` ทุก layer → `skeleton.SetSkin()` → `SetSlotsToSetupPose()` → `animationState.Apply()` (ตรวจ API จริงของ Spine-Unity 4.3 ใน S1)
- `ApplySlot` เปลี่ยนเฉพาะ skin ของ slot นั้นแล้ว rebuild custom skin — ไม่ respawn
- `ChibiActivityMap` (ScriptableObject / data): `ChibiActivity → animation name` พร้อม fallback เป็น `Idle` + warning ถ้าไม่มีชื่อนั้น (แพทเทิร์นเดียวกับ `SpineVisualController` ของ Marooned)
- บทเรียนจาก Marooned ที่นำมาใช้ตรง ๆ: `GetTrack` (ไม่ใช่ `GetCurrent`) ใน 4.3, flip ด้วย `Skeleton.ScaleX` ไม่แตะ `localScale`, log รายชื่อ animation ตอน Awake ครั้งเดียว
- **ตัวละครเนื้อเรื่อง (rig เฉพาะ เช่น Elena/Derek ใน Marooned):** `visual_overrides.json` (`discipleId → skeletonDataId`) — ไม่ผ่าน part system; ทำหลัง Phase 3

## 7. Tier policy + budget

```
Entitlement (state)   ChibiBackend ∈ {SpriteSheet, Spine}   ← VisualTierPolicy.DefaultFor(...) ตอนสร้าง/เลื่อนขั้น/ซื้อเกม
Effective (runtime)   Spine ถ้า entitled && spineActive < SpineBudget  ไม่งั้น SpriteSheet   ← DiscipleVisualSystem
```

| กรณี | Entitlement |
|---|---|
| SectMaster / Elder / ตัวละครสำคัญ | `Spine` |
| ผู้เล่นที่ซื้อเกม (join AI VTuber Live) | `Spine` (ตั้งผ่าน `TrySetChibiBackend`) |
| Outer / Inner ทั่วไป | `SpriteSheet` |
| ผู้ชม free | `SpriteSheet` |

- ลำดับความสำคัญตอนเกิน budget: ตอนนี้ใช้ Rank มากไปน้อย แล้ว `DiscipleId`; ผู้ซื้อควรได้อันดับสูง → ให้ `IVisualEntitlementProvider.GetPriority(discipleId)` (stub คืนค่าตาม rank) รองรับไว้ก่อน (ดู Q1)
- **Promotion** (ซื้อเกม/เลื่อนขั้น) → `TrySetChibiBackend` → `DiscipleChibiBackendChangedMessage` → respawn โดยคง position / activity / facing
- `SpineBudget` ตั้งต้น 20 ใน config — ปรับตามผล S1

## 8. Customization policy

| | Spine-entitled (ซื้อเกม/สำคัญ) | SpriteSheet (ทั่วไป/free) |
|---|---|---|
| Part ที่เลือกได้ | ทั้งหมดที่ครอบครอง (free + owner + dlc) | เฉพาะ `entitlement == ""` |
| Color (`tintable`) | ✔ | ตัดสินใจ Phase 5 |
| ใบหน้าละเอียด (หลัง split) | ✔ | Preset + Reroll (lean — ดู Q2) |
| Portrait | customize ได้เต็ม | ตามข้างบน |

- ชั้น validation 6 ใน `TryChangeAvatarPart`: `IVisualEntitlementProvider.CanUse(part.entitlement)` → ไม่ผ่านให้ `failReason` ชัดเจน (AI/UI เห็นเหตุผล)
- UI: `AvatarPartOptionViewData.IsLocked` มีอยู่แล้ว ใช้แสดงล็อก + จุดขาย DLC ได้เลย
- Randomize: กรองด้วย entitlement เดียวกัน

## 9. Art pipeline & Coverage validator

| Backend | Spec | หมายเหตุ |
|---|---|---|
| Portrait | 1024×1536 layered PNG, head-center ตรงกันทุกไฟล์ | เดิม |
| SpriteSheet | 1 sheet ต่อ slot, grid/pivot เดียวกัน, 2 ทิศ (flip) ตั้งต้น, cell size ตาม S3 | รวม Sprite Atlas เดียว |
| Spine | 1 rig; part = attachment ใน skin ชื่อ `{slot}/{partId}` | ไม่ rig ต่อ part |

- **Art ต่อ 1 part = 3 ชิ้น** — ต้นทุนหลักของแผนนี้ ลดโดย: (1) ส่ง Portrait + SpriteSheet ก่อน (MVP), Spine ตามหลัง (2) part ที่ยังไม่มี Spine ก็ยังใช้ได้เพราะ coverage ไม่บังคับ Spine จนกว่า `SpineEnabled` (3) ตัด face detail ออกจาก chibi ตามตาราง §4.3
- **Editor validator** `Tools/Xianxia/Validate Visual Coverage` (edit-time เท่านั้น): รายงาน part ที่ขาด backend ใด, path ที่ไม่มีไฟล์, ชื่อ skin ซ้ำ, และ **appearance ที่ขัดกับ `Sex`** — เช่นตอนนี้ d001 Lin Feng ใน `MockSectData` เป็น `Female` แต่ใช้ `head_male_01` validator จะจับได้ทันที

## 10. DI + โครงไฟล์

```csharp
// GameLifetimeScope.Configure — ไม่มี interprocess registration ใหม่
builder.Register<Xianxia.Sect.Visual.AppearanceResolver>(Lifetime.Singleton);
builder.Register<Xianxia.Sect.Visual.VisualTierPolicy>(Lifetime.Singleton);
builder.Register<Xianxia.Sect.Visual.VisualRuntimeConfig>(Lifetime.Singleton);   // SpriteSheetEnabled / SpineEnabled / SpineBudget
builder.Register<Xianxia.Sect.Visual.ChibiFrameBank>(Lifetime.Singleton);
builder.Register<Xianxia.Sect.Visual.IVisualEntitlementProvider, Xianxia.Sect.Visual.DefaultEntitlementProvider>(Lifetime.Singleton);
builder.RegisterEntryPoint<Xianxia.Sect.Visual.ChibiFrameClock>(Lifetime.Singleton).AsSelf();
builder.RegisterEntryPoint<Xianxia.Sect.Visual.DiscipleVisualSystem>(Lifetime.Singleton).AsSelf();
```

```
Assets/Scripts/Visual/
  Core/      AppearanceResolver.cs · IChibiVisual.cs · IPortraitVisual.cs · VisualTierPolicy.cs
             VisualRuntimeConfig.cs · IVisualEntitlementProvider.cs · DiscipleVisualSystem.cs
  Sprite/    SpriteChibiVisual.cs · ChibiFrameBank.cs · ChibiFrameClock.cs
  Spine/     SpineChibiVisual.cs · ChibiActivityMap.cs        (asmdef แยก — Spine เป็น optional dependency)
  Scene/     ChibiSceneRoot.cs
Assets/Resources/Data/chibi_anim.json · visual_overrides.json
```

> Spine-Unity ควรอยู่ใน **asmdef แยก** เพื่อให้ Core/Sprite compile ได้แม้ไม่มี Spine (และเปลี่ยน Tier-2 backend ได้ในอนาคตโดยไม่แก้ Core)

## 11. Phase plan (ทำตามลำดับ, มี gate ท้ายแต่ละ phase)

### Phase 0 — Spikes (ก่อนสร้างจริง) · ขนาด S
1. **S1 Spine shared rig:** 1 rig, 6 slot, 20 instance สุ่ม skin — วัด CPU ms/frame, draw calls, GC ตอน `SetSkin`, ตรวจ API 4.3
2. **S2 Sprite layered:** 300 instance × ~4 layer, atlas เดียว, central clock — วัด CPU ms/frame, batch count, GC steady-state
3. **S3 ขนาด+ทิศ:** วาด placeholder 1 ชุดเข้าฉากเทียบภาพ reference → ล็อก cell size และ 2/4 ทิศ
4. **S4 License:** ยืนยันเงื่อนไข Spine (edition ที่ซื้อครอบคลุม mesh/skins ที่ใช้ + runtime license สำหรับ distribute) แล้วเพิ่ม exception ใน `conventions.md` (ดู R1)
- **Gate:** รวม chibi ทั้งฉาก ≤ **4 ms/frame** บนเครื่องเป้าหมาย (ค่าตั้งต้น ปรับได้) ไม่ผ่าน → ลด `SpineBudget` / เปิด culling / ลดจำนวน layer

### Phase 1 — Foundation (ภาพที่ผู้เล่นเห็นไม่เปลี่ยน) · ขนาด M
- `ChibiBackend` + `[Key(8)]`, ขยาย `AvatarPartDef` + `Supports()`, `AppearanceResolver`, `IPortraitVisual` บน `AvatarRenderer`, `VisualRuntimeConfig`, coverage validator, coverage validation ชั้น 5 (เปิดตาม config)
- **Acceptance:** ☐ Portrait layer list ของ 4 founder เหมือนเดิม ☐ save/roster เก่า deserialize ได้ `SpriteSheet` ☐ Resolver มี EditMode test (fallback, back layer, tint, backend ไม่รองรับ) ☐ validator รันได้ และจับ d001 ได้

### Phase 2 — SpriteSheet + spawn ในฉาก · ขนาด L
- `ChibiSceneRoot`, `SpriteChibiVisual`, `ChibiFrameBank/Clock`, `DiscipleVisualSystem` (Reconcile), `SceneUnloadedMessage`, `chibi_anim.json`, placeholder art
- **Acceptance:** ☐ recruit → chibi ปรากฏ ☐ เปลี่ยนผมใน Portrait panel → chibi เปลี่ยนโดยไม่ respawn ☐ swap scene A→B → despawn/respawn ครบตาม state ☐ 100 ตัวอยู่ใน gate ☐ steady-state GC = 0 และไม่มี `Animator`

### Phase 3 — Spine + tier policy · ขนาด M
- `SpineChibiVisual`, `ChibiActivityMap`, `VisualTierPolicy`, budget degrade, `TrySetChibiBackend`, `DiscipleChibiBackendChangedMessage`
- **Acceptance:** ☐ d000/d003 = Spine, ที่เหลือ Sprite ☐ ตั้ง `SpineBudget=1` → d003 degrade เป็น Sprite โดย state ไม่เปลี่ยน ☐ promote d001 → respawn คง position/activity/facing ☐ `ApplySlot` บน Spine ทำงาน ☐ ชื่อ animation หาย → fallback `Idle` + warning

### Phase 4 — Activity + interaction · ขนาด M
- `CurrentTask → ChibiActivity` (data-driven; พึ่ง `DiscipleTaskChangedMessage` จาก [[sources/task-system]] ถ้าทำแล้ว ไม่งั้น derive จาก `CurrentTask` ตอน reconcile), work anchor ใน `ChibiSceneRoot`, คลิก chibi → `DiscipleSelectedMessage` → `DiscipleDetail` panel (Portrait + roster `HeadIcon`)

### Phase 5 — Customization policy / entitlement · ขนาด M
- field `entitlement`, `IVisualEntitlementProvider` จริง, validation ชั้น 6, lock ใน UI, preset+reroll สำหรับ free, DLC pack hook

### Track ขนาน — Face split (Roadmap #1 ของ avatar)
- เป็นงาน **Portrait-only** (§4.3) ไม่ขึ้นกับ phase ข้างบน ทำได้ทุกเมื่อ

## 12. What NOT to touch
- `AvatarAppearance` `[Key(0–2)]`, `DiscipleState` `[Key(0–7)]` — append-only เท่านั้น
- ห้ามฟื้น `outfits[]` / `TryApplyOutfit` / `AvatarOutfitChangedMessage` ([[sources/avatar-appearance]] §1)
- ห้ามใช้ `FindObjectOfType` / reflection หา backend — enum → factory ตรง ๆ
- ห้าม register message ของระบบนี้บน interprocess broker (ทั้งหมด in-memory; ห้ามให้ chibi ฟัง `IDistributedSubscriber`)
- ห้ามใส่ `Animator` ต่อตัวใน SpriteSheet tier
- ห้ามเปิด `sexTag` filter ในกริด (Roadmap #5 ของ avatar) จนกว่าจะถึงคิว — แผนนี้ไม่ต้องพึ่ง

## 13. Risks

| # | ความเสี่ยง | แผนรับมือ |
|---|---|---|
| R1 | **Spine ขัดกับกฎ "Open-source first" ใน `conventions.md`** + มีเงื่อนไข license (ต้องมี Spine license ที่ครอบคลุมฟีเจอร์ที่ใช้ และ runtime license ตอน distribute) | S4 ตรวจก่อนลงทุน; เพิ่ม exception ใน conventions; `IChibiVisual` + asmdef แยกทำให้สลับ Tier-2 เป็น Unity 2D Animation (built-in, ไม่มี license) ได้ถ้าจำเป็น |
| R2 | Art ต่อ part ×3 | ลำดับส่งมอบ §9; ตัด face detail ออกจาก chibi |
| R3 | Rig เดียวรองรับรูปร่างหลากหลายไม่พอ (เช่น ชาย/หญิงต่างสัดส่วน) | ตัดสินหลัง S1 — lean: 1 rig + body skin ตามเพศ ไม่ใช่ 2 rig |
| R4 | Sprite tier 300 ตัวหนักกว่าคาด | S2 gate; culling; ลดจำนวน layer (รวม hair_back เข้า body สำหรับผมสั้น) |
| R5 | บัฟเฟอร์ budget ทำให้ผู้ซื้อเกม "ดูเหมือนคนทั่วไป" เมื่อฉากแน่น | priority provider (Q1); สำรอง slot สำหรับ owner |
| R6 | ภาพ 3 backend ของคนเดียวกันไม่เหมือนกัน | Resemblance rule §4.3 + checklist ตอน review art |
| R7 | ตัวเลข perf ที่เคยเสนอไม่มีที่มา | Phase 0 gate |

## 14. Open Questions (ห้ามเดา — ตัดสินก่อนทำ phase ที่เกี่ยวข้อง)

| # | คำถาม | Lean | ต้องตอบก่อน |
|---|---|---|---|
| Q1 | ผู้ชม/ผู้ซื้อผูกกับ `DiscipleState` อย่างไร (Twitch id? Steam?) — ต้องมี field เช่น `ViewerId [Key(9)]` และ source ของ entitlement | นอกขอบเขตแผนนี้; ทำผ่าน `IVisualEntitlementProvider` | Phase 5 |
| Q2 | Free tier customize ระดับไหน | Preset + Reroll (สอดคล้อง "หน้าโหล่") ไม่ให้เลือกละเอียด | Phase 5 |
| Q3 | จำนวนทิศของ chibi | 2 (flip) — ขยายทีหลังถ้า S3 บอกว่าไม่พอ | S3 |
| Q4 | Rig เดียวหรือแยกตามเพศ | 1 rig | หลัง S1 |
| Q5 | ตัวละครเนื้อเรื่อง (rig เฉพาะ) เริ่มเมื่อไร | หลัง Phase 3 | Phase 3+ |
| Q6 | `SpineBudget` ที่เหมาะสม | ตั้ง 20 ชั่วคราว | หลัง S1 |

## 15. Wiki ที่ต้องอัปเดตเมื่อแผนนี้ถูกยอมรับ

- `sources/avatar-appearance.md` — เพิ่ม banner "สถาปัตยกรรม render ถูกแทนด้วย [[sources/disciple-visual-system]]; §1–§2 (decision record) ยังมีผล"
- `entities/avatar-appearance.md` — ชั้น 3–4 (Rendering/UI) ชี้ไปหน้านี้
- `entities/disciples.md` + `concepts/state-management.md` — เพิ่ม `[Key(8)] ChibiBackend`
- `conventions.md` — exception ของ Spine (R1)
- `index.md`, `log.md`, `sources/open-questions.md` (Q1–Q6)

## Related Pages

- [[sources/avatar-appearance]] — decision record (outfit rejected, poseId/sexTag kept)
- [[entities/avatar-appearance]] — implemented schema + Portrait rendering
- [[concepts/additive-scene-architecture]] — CoreScene / GameplayScene, `SceneLoader`
- [[concepts/mvp-ui]] — DiscipleDetail panel pattern
- [[sources/task-system]] — `CurrentTask` → activity
