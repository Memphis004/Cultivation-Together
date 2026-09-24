---
title: "Visual Overrides — Straight-Alpha Conversion (Spine atlases on Linear color space)"
type: adr
status: accepted
sources:
  - LLMWiki/wiki/sources/disciple-visual-system.md
  - UnityProject/Assets/Resources/Data/visual_overrides.json
  - UnityProject/Assets/Scripts/Visual/Spine/VisualOverrideMap.cs
  - UnityProject/Assets/Scripts/Visual/Spine/SpineChibiVisual.cs
  - UnityProject/Assets/Scripts/Core/GameLifetimeScope.cs
related:
  - "[[sources/disciple-visual-system]]"
  - "[[concepts/disciple-visual-system]]"
created: 2026-09-25
updated: 2026-09-25
confidence: high
tags: [adr, spine, visual, atlas, color-space, straight-alpha, texture, s4-license]
---

# ADR — Straight-Alpha Conversion สำหรับ atlas ของ rig เฉพาะตัวละคร (Q5)

## บริบท

Rig จริงชุดแรก (male `1113103_1`, female `1123102_1`) ที่ import เข้า
`Resources/Avatar/Spine/` ส่ง atlas มาเป็น **Premultiplied-alpha (PMA)** PNG
(ตรวจด้วย pixel test: ทุกช่อง RGB ของ pixel semi-transparent ถูกคูณด้วย alpha —
ไม่เดาจาก export setting) ขณะที่โปรเจกต์ตั้งเป็น **Linear color space**
(`ProjectSettings.asset → m_ActiveColorSpace: 1`)

spine-unity 3.8 เตือนสอง warning เมื่อ import:

1. *"disable `Alpha Is Transparency` on Premultiply alpha textures"* — เพราะ
   texture import บางไฟล์ตั้ง `alphaIsTransparency: 1` ขณะ material คิดว่าเป็น PMA
2. *"Premultiply-alpha atlas textures not supported in Linear color space"* —
   PMA shader ของ spine ถูกออกแบบบนสมมติ Gamma blending; บน Linear สีขอบ/ส่วน
   semi-transparent จะเพี้ยน (fringe มืด/สว่างผิด)

## การตัดสินใจ

เลือก **ทางแก้ (a) ของ spine-unity: แปลงเป็น straight-alpha workflow** ทั้งชุด —
**ไม่** ใช้ทางแก้ (b) สลับโปรเจกต์เป็น Gamma (กระทบศิลป์/แสงทั้งเกม)

ขั้นตอนที่ทำแล้ว (apply กับ atlas ใหม่ทุกไฟล์ด้วย):

1. **Un-premultiply PNG**: `RGB_true = RGB_stored / alpha` (คูณ 255, clamp 255)
   ทุก pixel ที่ alpha < 255; pixel alpha=0 ตั้ง RGB=0 กัน fringe
   (backup PNG ต้นฉบับ: `Library/pm_backup/`)
2. **Material**: `_StraightAlphaInput: 1` **พร้อมย้าย keyword**
   `_STRAIGHT_ALPHA_INPUT` ไป `m_ValidKeywords` — ครั้งแรกที่ import มันอยู่ใน
   `m_InvalidKeywords` (Unity 6 ต้องประกาศ keyword ใน material จริง ไม่ใช่แค่
   float property) ทำให้ shader ไม่รู้จัก toggle นี้เลยและ warning B โผล่
3. **Texture import**: `alphaIsTransparency: 1` — ใน straight workflow ค่านี้
   *ถูกต้องแล้ว* (มันเป็นการ bloat RGB ของพื้นที่โปร่งใส ซึ่งเป็นพฤติกรรมที่ต้องการ
   กัน fringe ตอน bilinear filter) เงื่อนไขของ warning A จึงหายเองเมื่อ
   material ไม่ถือว่า texture เป็น PMA อีก

**การประเมิน visual หลังแปลง** (render จริงจาก TestGameplayScene + วิเคราะห์
pixel): ตัวละคร render ครบ (attachments 16/24 ครบตาม rig) ไม่มี fringe ผิดปกติ
จากการ un-premultiply (ค่าที่แก้คือคืนสีต้นฉบับ ไม่ใช่การคิดสีใหม่)

## วิธี re-export จาก Spine IDE (สำหรับ atlas ชุดถัดไป)

ให้ art ส่งไฟล์ที่ **ตรง workflow นี้ตั้งแต่ต้น** ไม่ต้องแปลงซ้ำ:

1. เปิด export dialog ใน Spine Editor → **Texture Packing** → ช่อง
   **Premultiply alpha**: ❌ **ยกเลิกการติ๊ก** (ค่า default ของ Spine คือติ๊กอยู่แล้ว —
   อย่าติ๊ก)
2. PNG ที่ได้คือ straight alpha ตรง ๆ → import เข้า Unity ได้เลย
3. Material ที่ spine-unity generate จะตั้ง `_StraightAlphaInput = 1` เองเมื่อ
   ติ๊ก "Straight Alpha Texture" ใน inspector ของ material — ตรวจว่า keyword
   `_STRAIGHT_ALPHA_INPUT` อยู่ใน **Included/Valid** keywords ไม่ใช่ Invalid
4. Texture import: คง `Alpha Is Transparency = ✔` (ค่า default — อย่าปิดตาม
   warning เดิมที่ใช้ได้เฉพาะกับ PMA)

ถ้าได้ไฟล์ PMA มาแล้ว (ลืมตั้งค่า) แปลงซ้ำได้ด้วยสคริปต์ un-premultiply เดียวกับ
ข้อ 1 ด้านบน — ห้ามแก้เฉพาะ material ให้กลับไป PMA เพื่อเลี่ยงการแปลง เพราะ
Linear color space จะกลับมาเตือนและสีจะผิดจริง

## ส่วนเสริม — การเปิดใช้งานจริงถาวร (S4 Activation — Permanent, 2026-09-25)

### การตัดสินใจ (สถานะ: เปิดถาวร — ไม่ใช่ช่วงทดลอง)

ผู้รับผิดชอบผลิตภัณฑ์ยืนยันอย่างชัดเจน (2026-09-25 — ตอบคำถามยืนยันด้วยตัวเลือก
"ใช่ — เปิดถาวร") ว่า **Spine Editor + runtime license ครอบคลุม** การใช้งานตาม
checklist ใน phase0-results §4 → การเปิด S4 เป็น**การตัดสินใจถาวร** gate
`SpineActivationRequested` ตั้ง `true`
**ที่ composition root** (`GameLifetimeScope.Configure`) — จุดตัดสินใจของมนุษย์ตาม L12 และ
เป็น pattern เดียวกับ `SpriteSheetEnabled` ที่อยู่บรรทัดถัดขึ้นไป พร้อม comment บันทึกวันที่
และเงื่อนไขไว้ในโค้ด

- `VisualSpineBootstrap` ยังเป็นผู้ activate ตามลำดับเดิม (gate → rig load → hooks) และยัง
  ตกไป INERT ได้ถ้า asset โหลดไม่สำเร็จ — การตั้ง gate ไม่ใช่การ bypass การตรวจใด ๆ
- **Interim shared rig** = `Avatar/Spine/male/1113103_1` (rig ตัวละครชาย) ใช้แทน chibi_base
  ที่ยังไม่มี: ศิษย์ที่ได้ Spine slot จาก §7 แต่ไม่มี Q5 override (ปัจจุบัน: d003) เรนเดอร์ด้วย
  rig นี้; ตัวละครเนื้อเรื่อง (d000/d002) เรนเดอร์ rig ตัวเองผ่าน `visual_overrides.json` เสมอ
- กฎเดิมไม่เปลี่ยน: bootstrap ไม่มีสิทธิ์ set gate เอง — ตั้งได้เฉพาะ composition root

### ข้อควรระวังที่ verify จับได้ตอนเปิดจริง

- เมื่อ interim shared rig ชี้ไฟล์เดียวกับ rig ของ d000 ชั่วคราว assertion
  "d000 rig is NOT the shared rig" ต้องเขียนแบบ **path-aware** (asset เดียวกันโดยนิยาม —
  `Resources.Load` คืน asset ตัวเดียวกัน) เมื่อเปลี่ยนเป็น chibi_base จริง check จะเริ่มคุม
  invariant ตามที่ออกแบบ
- ชื่อ animation ของ rig จริงเป็น lowercase (`idle1/walk/run`) — ปิดวงจรด้วย per-rig
  `activityToAnimation` ใน `visual_overrides.json` (ดูหัวข้อถัดไปของ ADR ไม่ต้องแก้ shader)

### หลักฐานหลังเปิด S4

- log จาก play จริง: `[VisualSpineBootstrap] Spine backend ACTIVE — shared rig '1113103_1',
  SpineBudget=20 … Story-character overrides: 2 (Q5 — per-character rigs, outside SpineBudget)`
- `visual_overrides verify` รอบ production-wiring: **COMPLETE pass=41 fail=0** — runner
  ไม่ register hooks เองอีก (bootstrap เป็นคนผูกทั้งหมด) และเพิ่ม assertion ว่า d003
  เรนเดอร์ผ่าน shared rig จริง ขณะ d000/d002 ใช้ rig ตัวเอง

### หลักฐาน visual บนฉากจริง (visual spot-check หลังเปิด S4, 2026-09-25)

Capture จาก TestGameplayScene (`Library/visual_overrides_shot*.png`, 2560×1440;
info: d000 Resting scaleX=1, d002 Working scaleX=-1, d003 Idle scaleX=1):

- ทั้งสาม rig render สมบูรณ์ (silhouette ครบ ตัว+ขา): d000 male, d002 female,
  d003 ผ่าน interim shared rig — ไม่มี object ใด crop หลุดหรือว่าง
- **"ขอบมืด" ที่ flag รอบแรกเป็น false alarm**: source atlas มีเส้น outline ดำเป็น
  สไตล์ศิลป์เอง (dark opaque 12.6% / 13.1% ของ male/female) และใน render พิกเซล
  ขอบมืดที่ opaque = 0 ทุกตัว — ขอบมืดทั้งหมดเป็นพิกเซล semi-transparent (AA ของ
  outline กับพื้นหลังน้ำเงินเข้ม) ไม่ใช่ PMA fringe
- **Blend consistency**: พิกเซล inconsistent 3.4–4.9% แต่ p95 = median = 0
  (95%+ ผสมสีตรงเป๊ะ); worst deviation = 19 บนพิกเซล alpha≈2 เท่านั้น — rounding
  ของพิกเซลโปร่งจัด ไม่ใช่ halo signature ของ PMA บน Linear
- สรุป: straight-alpha conversion ไม่สร้าง artifact ใด ๆ บนฉากจริง

## สิ่งที่ไม่เปลี่ยน

- `SpineChibiVisual` / `VisualOverrideMap` ไม่แตะ texture หรือ material ใน runtime —
  งานนี้เป็น asset-side ทั้งหมด
- S4 license gate (`SpineActivationRequested`) ยังเป็นตัวคุมเดิม
- comment ประจำไฟล์ `VisualOverrideMap.cs` ระบุว่า atlas ชุดนี้เป็น
  straight-alpha — กันการ revert โดยไม่รู้บริบท

## ผลพิสูจน์ (รอบตรวจ asset-side, ก่อนเปิด S4)

- `visual_overrides verify`: **COMPLETE pass=30 fail=0**
  (รวม assertion ว่า `SetActivity(Idle)` เล่น animation `idle1` ของ rig จริง
  ผ่าน per-rig mapping ใน `visual_overrides.json`) — หลังเปิด S4 ชุดเดิมขยายเป็น
  production-wiring verify แล้วได้ pass=41 fail=0 (ดูหัวข้อ "การเปิดใช้งานจริง")
- Unity console: ไม่มี spine texture/material warning หลัง domain reload
- ไฟล์ว่าง `female/1123102_1.asset` (SkeletonDataAsset ไม่มี atlas/skeletonJSON)
  ถูกลบ — ทุก path ใน `visual_overrides.json` ชี้ asset ที่สมบูรณ์
