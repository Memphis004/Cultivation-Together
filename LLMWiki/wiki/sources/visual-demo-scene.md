---
title: Visual Demo Scene — DiscipleVisualSystem (Phase 3, Spine example rig)
type: gdd
status: dev-only demo (implemented)
sources:
  - UnityProject/Assets/Scripts/Visual/Spikes/Spine/VisualDemoSpineEnabler.cs
  - UnityProject/Assets/Scripts/Visual/Spikes/Spine/VisualDemoHud.cs
  - UnityProject/Assets/Scripts/Visual/Spikes/Editor/VisualDemoSceneTool.cs
  - UnityProject/Assets/Scripts/Visual/Spikes/Editor/DemoVerifyRunner.cs
  - UnityProject/Assets/Scripts/Visual/Spine/DemoSpineRigMap.cs
  - UnityProject/Assets/Resources/Data/demo_spine_rig_map.json
  - UnityProject/Assets/Scenes/VisualDemo/VisualDemoScene.unity
related:
  - "[[sources/disciple-visual-system]]"
  - "[[entities/avatar-appearance]]"
created: 2026-09-23
updated: 2026-09-25
confidence: high
tags: [visual, demo, dev-only, spine, example-rig, tier-policy]
---

# Visual Demo Scene — DiscipleVisualSystem (Phase 3)

> ⚠️ **DEV-ONLY — DO NOT SHIP.** ทุกอย่างในหน้านี้มีชีวิตอยู่แค่ใน demo scene:
> `Assets/Scenes/VisualDemo/VisualDemoScene.unity` + โค้ดใต้ `Assets/Scripts/Visual/Spikes/`
> Scene นี้ **ห้าม** เข้า Build Settings (guard T8 ตรวจทุกครั้งที่เปิดเดโม)

เดโมพิสูจน์ Phase 3 (Spine + tier policy) ของ [[sources/disciple-visual-system]] โดยใช้
**free Spine example rig (mix-and-match-pro)** เป็น stand-in — ความเหมือน art ไม่สำคัญ
ความถูกต้องของ pipeline สำคัญ: skin rebuild กับที่, degrade โดย state ไม่เปลี่ยน,
click → message, fallback animation, budget ตัดสิน effective backend

## 0. สถานะ

- ✅ Implement ครบ (T1–T6, T8) — **demo verify COMPLETE pass=33 fail=0**
  (2026-09-22, `UnityProject/Library/demo_verify_report.txt`)
- ✅ **อัปเดต (2026-09-25):** S4 ยืนยันแล้ว — production เปิด Spine ถาวรผ่าน
  `VisualSpineBootstrap` (gate `SpineActivationRequested` = true ที่ composition root,
  ดู [[decisions/visual-overrides-straight-alpha]]) — เดโมหน้านี้จึงเหลือหน้าที่เป็น
  **DEV-ONLY harness** สำหรับทดสอบ tier policy บน example rig โดยเฉพาะ
  (สิ่งที่เดโมพิสูจน์เกี่ยวกับ budget/degrade ยังใช้ได้เหมือนเดิม)

## 1. วิธีเปิดเดโม

Unity Editor menu:

```
Xianxia/Visual Demo/Play Demo        ← เปิดเดโมเต็มรอบ (guards → scaffold → play)
Xianxia/Visual Demo/Run Guard Checks Only
```

`Play Demo` ทำตามลำดับ (T4 + T8):

1. **Guards** (T8) — ต้องผ่านก่อน ไม่งั้น dialog + Console error แล้วหยุด:
   (a) `VisualDemoScene` ไม่อยู่ใน `EditorBuildSettings.scenes`
   (b) grep ทั้ง `Assets/Scripts` — ห้ามมีใคร **เขียน** `DevSpineOverride`
       นอกจาก `VisualDemoSpineEnabler.cs` (อ่านได้ทุกที่)
2. **Scaffold** (T3) — สร้าง/refresh scene: Main Camera (ortho 2D, size 4.5),
   `ChibiSceneRoot` (grid 2×2 = spawnOrigin(-1.2,0), spacing(1.6,1.2), perRow 2,
   taskAnchors ว่าง), `VisualDemoSpineEnabler` (+ rig ref), `VisualDemoHud`
3. **Boot จริง** — เปิด `SampleScene` (CoreScene: `GameLifetimeScope` + `SceneLoader`)
   เข้า play mode แล้ว watcher (`[InitializeOnLoad]`) สั่ง
   `SceneLoader.LoadGameplayScene("VisualDemoScene")` แบบ additive →
   `SceneLoadedMessage` → `DiscipleVisualSystem` สแกนเจอ `ChibiSceneRoot` → spawn เอง
   (C2 — เดโมไม่มี spawner คู่ขนานเลย)

**ผลลัพธ์ตอนเข้าเดโม:** 4 chibi — d000/d003 = Spine (rig ตัวอย่าง), d001/d002 =
SpriteSheet — ครบทั้ง 2 backend ในจอเดียว (d000 ชนะ budget slot ก่อนเพราะ priority
rank-desc ตาม §7)

Remote verify (ไม่ต้องกดปุ่ม): marker command `demo_verify` —
`VisualDemoSceneTool.DemoVerify()` → guards → scaffold → boot → `DemoVerifyRunner`
เดิน step machine ครบ 7 ปุ่ม → เขียนรายงาน `Library/demo_verify_report.txt`

## 2. DevSpineOverride — seam เดียว (C3) ⚠️

```csharp
// VisualRuntimeConfig (Core):
public bool DevSpineOverride { get; set; }   // default false — ship ไม่แตะ
// tier policy อ่าน: (SpineEnabled || DevSpineOverride) ทั้ง EffectiveBackend + AllocateSpineSlots
```

| กฎ | รายละเอียด |
|---|---|
| ใครเขียนได้ | **`VisualDemoSpineEnabler` เท่านั้น** — Awake → `true` + register demo factory, OnDestroy → restore factory + `false` (Spine path ปิดตอน scene ตายพอดี) |
| ไม่ใช่ S4 gate | `SpineActivationRequested` (การตัดสิน license ของมนุษย์) **ไม่ถูกแตะ** โดยเดโม — gate ถูกตั้ง true แล้วที่ composition root (`GameLifetimeScope`, 2026-09-25) ไม่ใช่ที่นี่ |
| Ship defaults | `SpineEnabled` ยัง `false`, `DevSpineOverride` เป็น `true` เฉพาะช่วง demo scene มีชีวิตอยู่ |
| Guard | T8(b) grep-level — เขียนนอก enabler = guard fail, เปิดเดโมไม่ได้ |

**R1/S4 สถานะ (อัปเดต 2026-09-25):** คำเตือนเดิมหมดผลแล้ว — S4 ยืนยัน + exception
"Open-source first" ลง `conventions.md` แล้ว (ดู R1 ของ
[[sources/disciple-visual-system]] + [[decisions/visual-overrides-straight-alpha]])
กฎที่ยังคงอยู่: ห้าม flip gate ในโค้ด demo/tools — การตัดสินใจยังต้องเกิดที่
composition root เท่านั้น และ example assets ของ Esoteric ยังห้ามตกค้างใน build จริง

## 3. HUD buttons → สิ่งที่พิสูจน์ (T5)

ทุกปุ่มเดิน **production path** (state provider / MessagePipe) — ไม่มี bypass:

| ปุ่ม | ทำอะไร | พิสูจน์ |
|---|---|---|
| [1] `SpineBudget=1` | แก้ config + `Reconcile()` | budget ตัดสิน effective backend — d003 degrade เป็น Sprite **บนจอ** แต่ `state.ChibiBackend` ยัง `Spine` (entitlement ไม่ถูกแตะ, §7) |
| [2] คืน budget=20 | `Reconcile()` | d003 กลับมา Spine ในที่เดิม (respawn in place) |
| [3] เปลี่ยนผม d000 | `TryChangeAvatarPart` → `AvatarEquipmentChangedMessage` → `ApplySlot` → skin rebuild | **instanceId เท่าเดิม** — GameObject ไม่ถูก respawn (log ก่อน/หลัง) |
| [4] `TrySetChibiBackend(d001, Spine)` | message → respawn | promotion คง position/activity/facing ครบ |
| [5] คลิก/ปุ่มเลือก d002 | publish `DiscipleSelectedMessage` | `DiscipleDetail` เปิดถูก disciple + Portrait (pipeline เดียวกับ AvatarCustomization) |
| [6] วน CurrentTask | debug helper เขียน `CurrentTask` ตรง ๆ + `Reconcile()` (task system ยังไม่มี — spec อนุญาต) | derive-on-reconcile: task → activity เปลี่ยน, instance เดิม |
| [7] `demo_missing` task | → activity `DemoMissing` → animation `spine_demo_missing_anim` (ที่จงใจไม่มีใน rig) | **fallback เล่น `idle` + warn ครั้งเดียวเท่านั้น** (L9 warn-once per visual+activity) |

ปุ่ม [5] เทียบเท่ากับการคลิกตัว chibi จริง (`ChibiClickTarget` + Collider2D ผูกกับทุก visual)

## 4. Mapping — `Assets/Resources/Data/demo_spine_rig_map.json` (T1/T2)

DEV-ONLY data — โหลดผ่าน `DemoSpineRigMap` (Visual.Spine asmdef) และปรึกษา
**เฉพาะตอน `DevSpineOverride == true`** — ชื่อ skin/animation ของ rig ตัวอย่าง
**ไม่เคย** เข้า `avatar_parts.json` / production data (กัน production เปื้อน)

| ของเรา | ของ rig (mix-and-match-pro) |
|---|---|
| slot `body` | skin `clothes/hoodie-blue-and-scarf` |
| slot `head` | skin `skin-base` |
| slot `hair` | skin `hair/long-blue-with-scarf` |
| slot `face_marking` | skin `eyes/violet` |
| slot `accessory` | skin `accessories/cape-red` |
| part `hair_topknot_long` | skin `hair/blue` (part-level ชนะ slot-level) |
| part `hair_short` | skin `hair/short-red` |
| activity `Idle` | animation `idle` |
| activity `Walking`/`Walk` | animation `walk` |
| activity `Working` | animation `dance` |
| activity `Resting` | animation `aware` |
| activity `DemoMissing` | animation `spine_demo_missing_anim` (**จงใจไม่มีจริง** — ไว้เทสต์ fallback) |

โครง JSON: `{ skeletonDataAssetPath, skeletonDataResourcePath, slotToSkin[],
partToSkin[], activityToAnimation[], intentionalMissingAnimation }`

Chain การ resolve ของ `SpineChibiVisual` (ตอน demo map มีชีวิต):
animation = demo map → production `ChibiActivityMap` → ไม่เจอใน SkeletonData →
**fallback `Idle` (+ demo map ให้ `idle`) + warn ครั้งเดียว** — skin = part-level
demo map → slot-level demo map → `layer.SpineSkin` (production) → partId

## 5. T1 — ผลสำรวจ example rig

เลือก **mix-and-match-pro** (ตามคำแนะนำ spec — โครงสร้าง skin เข้าใกล้ slot/skin
ของระบบเราที่สุด และเคยใช้ verify Phase 3/S1) — ไม่ต้อง fallback ไป Goblins

`mix-and-match-pro.json` (7779 บรรทัด) มี **4 animations**: `aware`, `dance`,
`idle`, `walk` — และ **~27 skins** ในกลุ่ม: `default`, `skin-base`, `hair/{blue,
brown,pink,short-red,long-blue-with-scarf}`, `clothes/{hoodie-blue-and-scarf,...}`,
`eyes/{violet,blue,green,yellow}`, `eyelids/{girly,semiclosed}`,
`legs/{boots-pink,boots-red,pants-green,pants-jeans}`, `nose/{long,short}`,
`accessories/cape-red` ฯลฯ

- รายชื่อ animation ถูก log ตอน spawn ครั้งเดียวต่อ instance โดย
  `SpineChibiVisual.LogRigContentsOnce()` (`[Conditional("UNITY_EDITOR")]` — L9)
- ชื่อที่ใช้ใน map ทุกตัว **ยืนยันแล้วว่ามีจริง** ใน JSON (skins + animations);
  มีแค่ `spine_demo_missing_anim` ที่จงใจไม่มี

## 6. โครงไฟล์ + asmdef (C1/C6)

```
Assets/Scripts/Visual/
  Spikes/Spine/    VisualDemoSpineEnabler.cs (C3 seam) · VisualDemoHud.cs (T5)
                   — Visual.Spine.Spikes asmdef (อ้าง Spine + Runtime)
  Spikes/Editor/   VisualDemoSceneTool.cs (menu + guards + scaffold + watcher)
                   DemoVerifyRunner.cs (acceptance step machine) — Visual.Spikes.Editor asmdef
  Spine/           DemoSpineRigMap.cs — Visual.Spine asmdef (C6: Spine code อยู่ asmdef แยก)
Assets/Resources/Data/demo_spine_rig_map.json   (DEV-ONLY data)
Assets/Scenes/VisualDemo/VisualDemoScene.unity  (ไม่อยู่ใน Build Settings)
```

เดโม spawn ผ่าน `DiscipleVisualSystem.SpineVisualFactory` (enum→factory seam เดียวกับ
production) ที่ enabler assign ชั่วคราว — ไม่มี interprocess registration ใหม่,
ไม่มี `FindObjectOfType` ใน production/demo path (verify runner ใช้ read-only
inspection แบบเดียวกับ Phase 2–5 carve-out), ไม่มี `Animator` ต่อตัว

## 7. Guard สรุป (T8)

| Guard | วิธีตรวจ | เงื่อนไข fail |
|---|---|---|
| (a) ไม่อยู่ใน Build Settings | เทียบ `EditorBuildSettings.scenes` | scene ใด ๆ ลงท้าย `VisualDemoScene.unity` |
| (b) DevSpineOverride writer เดียว | grep `Assets/Scripts/**/*.cs` หา `.DevSpineOverride =` | เจอในไฟล์อื่นนอกจาก enabler (+ config property เอง, verify runners/tool ถูก exempt เพราะไม่เขียน) |

รันได้จาก `Xianxia/Visual Demo/Run Guard Checks Only` หรืออัตโนมัติก่อน `Play Demo`

## 8. Known notes

- ⚠️ คำเตือน "Problematic material setup" จาก example asset = เสียงรบกวนของตัวอย่าง
  (C7) — material ของ atlas (`mix-and-match-pma_Material.mat`) ใช้ได้ตามที่ asset
  มาให้; **ห้ามแก้ asset ตัวอย่าง**
- Facing ตรวจแล้วใน verify: Spine flip = `Skeleton.ScaleX` (±1), Sprite flip = parent
  `localScale.x` — Transform ของ Spine ไม่ถูกแตะ (L8); sorting = `baseFromY/grid +
  layerIndex` ใช้ code path เดียวกับ production (T6)
- `ChibiActivityMap.FallbackState` คือ `"Idle"` — example rig ตั้งชื่อ lowercase
  จึงมี DEV-ONLY branch resolve fallback ผ่าน demo map ด้วย (production rig คาดว่า
  มี `"Idle"` — branch นี้ไม่รันใน ship)

## Related Pages

- [[sources/disciple-visual-system]] — §5 scene binding, §6.3 Spine, §7 tier policy,
  §10 DI, §11 Phase 3, §12 What NOT to touch
- [[entities/avatar-appearance]] — state schema + Portrait pipeline
- `marooned-wiki/wiki/sources/architecture/chibi-visual-system.md` — บทเรียน
  Spine 4.3 (GetTrack, ScaleX flip, fallback + warn-once, log-once)
