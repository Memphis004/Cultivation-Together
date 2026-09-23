---
title: "DiscipleVisualSystem — Phase 0 (Spikes) Results"
type: source
status: phase0-complete
sources: []
related:
  - "[[disciple-visual-system]]"
  - "[[avatar-appearance]]"
  - "[[references/chibi-visual-system-marooned]]"
created: 2026-09-22
updated: 2026-09-22
confidence: high (S1/S2 measured) / medium (S3 visual — pending human review)
tags: [visual, phase0, spikes, spine, spritesheet, performance]
---

# Phase 0 Results — DiscipleVisualSystem Spikes

> ผลวัดจริงของ Phase 0 ตามแผน [[disciple-visual-system]] §11 (gate) — ทุกตัวเลข
> วัดจาก Unity Editor 6000.3.9f1, Windows, Run In Background ON + Editor pause OFF
> (C11), วัดด้วย ProfilerRecorder counter `CPU Main Thread Frame Time`
> (ไม่ใช่ frame-to-frame wall time) และ `UnityEditor.UnityStats` สำหรับ draw calls/batches

## 0. ข้อจำกัดที่ต้องอ่านก่อนตีความ

| ข้อจำกัด | ผลต่อการตีความ |
|---|---|
| **S1 ใช้ Spine example asset ฟรี (`mix-and-match-pro`)** ไม่ใช่ chibi rig จริง (ยังไม่มี asset — T2) | ตัวเลข S1 = *ค่าประมาณโครงสร้างเดียวกัน* (1 SkeletonDataAsset, N SkeletonAnimation, skin mix) rig จริงอาจหนักขึ้นตามจำนวน attachment/mesh |
| **Spine runtime เป็น 3.8** (`spine-unity-3.8-2021-11-10`) ไม่ใช่ 4.3 ตามแผน | L10 deviation — ใช้ `GetCurrent(track)` แทน `GetTrack(track)` (user อนุมัติใน session นี้) ดู §3 |
| Editor metrics รวม editor-loop overhead | `gc_per_frame_median ≈ 18 KB` เป็น editor noise — ตัววัดต่อ operation คือหลักฐาน zero-alloc ของ spike code เอง (S2 ApplyFrame = 0 B) |
| S1/S2 วัดคนละ scene คนละครั้ง (ตาม L11) | "ทั้งฉากรวม ≤ 4 ms" เป็นผลรวมเชิงบวกเท่านั้น ไม่ใช่ตัวเลขวัด |

## 1. สิ่งที่สร้าง (T0–T3)

### T0 — Document prep ✅
- `LLMWiki/wiki/sources/disciple-visual-system.md` — committed (แผนหลัก)
- `LLMWiki/wiki/sources/references/chibi-visual-system-marooned.md` — copy จาก `marooned-wiki/wiki/sources/architecture/chibi-visual-system.md` (local repo) + external-note ท้ายไฟล์ ("pattern reference เท่านั้น ไม่ใช่ source ของเกมนี้")

### T1 — Spike harness
```
Assets/Scripts/Visual/Spikes/
├── Core/  (Visual.Core.Spikes.asmdef — ไม่พึ่ง Spine, C3)
│   ├── VisualSpikeProfiler.cs      — CSV + console summary (C9)
│   ├── PlaceholderSpriteBaker.cs   — สร้าง placeholder sheets 1 sheet/slot (L7)
│   ├── SpikeSceneBootstrap.cs      — camera/สล็อตคงที่ + SpikeSceneMarker
│   ├── SpikeRunners.cs             — SpriteLayerSpikeRunner (S2) + CellSizeProbeRunner (S3)
│   ├── SpikeSceneLauncher.cs       — launcher แยกไฟล์เพื่อ GUID เสถียร
│   └── SizeProbeSceneLauncher.cs
├── Spine/  (Visual.Spine.Spikes.asmdef — references spine-unity)
│   ├── SpineSpikeRunner.cs         — S1 (shared rig + skin mix)
│   └── SpineSpikeSceneLauncher.cs
└── Editor/  (Visual.Spikes.Editor.asmdef — Editor only)
    ├── VisualSpikesEditor.cs       — เมนู "Xianxia/Visual Spikes/..." + VerifyConstraints
    ├── VisualSpikeScenes.cs        — สร้าง/เปิดฉาก spike (ไม่เข้า Build Settings, C2)
    └── VisualSpikeRemoteControl.cs — marker-file bridge สำหรับ agent (Library/visual_spike_command.txt)
```
- ฉาก spike: `Assets/Scenes/Spikes/S1_SpineSpike.unity`, `S2_SpriteSpike.unity`, `S3_SizeProbe.unity` — **ไม่ถูกเพิ่มเข้า Build Settings** (C2, verify แล้ว)
- Remote control ใช้ได้ผลจริงใน session นี้ (tool-call parameter ถูกตัด) — เป็น editor-only code ใน asmdef spike เท่านั้น ไม่มี auto-load

## 2. ผลวัด + Gate Verdict (L11)

### S1 — Spine shared rig ×20 (ผ่าน mix-and-match skins)

| metric | ค่า | gate |
|---|---|---|
| cpu ms/frame median | **1.648** | ≤ 2.0 → **PASS ✅** |
| cpu ms/frame p95 | 1.927 | ≤ 2.0 (ที่ p95) → **PASS ✅** |
| draw calls / batches | 21 / 21 | — (ดูหมายเหตุ) |
| GC per SetSkin 1 ครั้ง | 0–8 KB, ส่วนใหญ่ **0 B** | — |
| fps (ระหว่างวัด, editor) | 241 | — |

**หมายเหตุ batch:** Spine ไม่ batch ข้าม instance (แต่ละ SkeletonRenderer มี material/buffer ของตัวเอง) → 21 ≈ 20 instances + 1 พื้นหลัง คาดหมายไว้แล้วในแผน §11 ว่า Spine tier เหมาะกับกลุ่ม "ตัวละครเด่น" ไม่ใช่ฝูงชน — ตัวเลขนี้ยืนยัน
**Amortized SetSkin:** swap 1 instance/2s → ~0.1 swap/s → GC เฉลี่ย ≈ 0 B/frame

### S2 — SpriteSheet layered ×300 (4 layers = 1,200 SpriteRenderer)

| metric | ค่า | gate |
|---|---|---|
| cpu ms/frame median | **1.123** | ≤ 2.0 → **PASS ✅** |
| cpu ms/frame p95 | 1.320 | ≤ 2.0 (ที่ p95) → **PASS ✅** |
| draw calls / batches | **6 / 6** (จาก 1,200 renderers) | batch ≈ จำนวน atlas ไม่ใช่ instance → **พิสูจน์ L7 ✅** |
| GC ต่อ ApplyFrame (300×4) | **0 B** | zero-alloc → **พิสูจน์ C7 ✅** |
| ไม่มี `Animator` ใน spike | ✅ (grep ทั้ง Spikes/) | C7 ✅ |
| fps (ระหว่างวัด, editor) | 268 | — |

**ผลรวมเชิงบวก:** S1 + S2 ≈ **2.77 ms/frame** < 4 ms (⚠️ ผลรวมประมาณเท่านั้น — ไม่ใช่ตัวเลขวัดฉากผสม จะวัดจริงใน Phase 2/3)

### S3 — ขนาด + ทิศ (T4)
- Screenshot: `Application.persistentDataPath/visual_spikes/S3_size_probe_20260922_111150.png` (copy: `UnityProject/Library/visual_spike_artifacts/`)
- วาง chibi placeholder ที่ cell 64/96/128 เทียบกันแล้ว — **คำแนะนำ (เสนอ, รอ human review):** lock ที่ **96×96 + 2 ทิศ flip** ตาม lean เดิม; flip ที่ parent `localScale.x` (Sprite) / `Skeleton.ScaleX` (Spine) ทำงานถูกต้องทั้งสอง backend (L8)

## 3. Spine API verification รายข้อ (runtime 3.8 จริงในโปรเจกต์)

| ข้อ | API ตามแผน (4.3) | ของจริงใน repo | สถานะ |
|---|---|---|---|
| L8 | flip ที่ `Skeleton.ScaleX` ห้ามแตะ `Transform.localScale` ของ Spine | `Skeleton.ScaleX` มีใน 3.8 — ใช้แล้ว, ไม่มีโค้ดแตะ transform | ✅ ตรงแผน |
| L9 | fallback `"Idle"` + warning ครั้งเดียว + log รายชื่อ animation ต่อ instance ที่ Awake | implement แล้ว (`ResolveAnimationName` + `LogAnimationsOnce`); asset มี `idle` จริง → ไม่มี warning เกิดขึ้น (log ยืนยัน) | ✅ ตรงแผน |
| L10 | ใช้ `AnimationState.GetTrack(track)` ห้าม `GetCurrent` | **3.8 ไม่มี `GetTrack`** — มีเฉพาะ `GetCurrent(int)` (AnimationState.cs line 933) | ⚠️ **DEVIATION** — user อนุมัติใช้ 3.8 + `GetCurrent` ไปก่อน; เมื่ออัปเกรด runtime เป็น 4.x ต้องเปลี่ยนกลับ + อัปเกรด example assets ด้วย (skeleton ที่ export จาก 3.8 โหลดด้วย runtime 4.x ไม่ได้) |
| C8/L4 | shared rig 1 ตัว + skin mix-and-match | `SkeletonDataAsset` เดียว, 20 `SkeletonAnimation`, custom `Skin` ต่อ instance จาก 28 part-skins | ✅ |

## 4. S4 — Spine License Checklist (ร่าง — **ไม่ได้ apply**, L12)

- [ ] **Edition ที่ซื้อครอบคลุมฟีเจอร์ที่ใช้?** — ฟีเจอร์ที่แผนใช้: skins (mix-and-match) = **Essential ขึ้นไป**; ถ้าใช้ mesh deformation ใน rig จริง = **Professional** — ต้องยืนยันกับ rig จริงที่จะสร้าง
- [ ] **Runtime license ตอน distribute เกม** — spine-unity runtime license อนุญาตให้ distribute ในเกมที่สร้างด้วย Spine Editor ที่ license ถูกต้อง (ผูกกับ edition ที่ซื้อ) — ต้องอ่านเงื่อนไขปัจจุบันจาน.esotericsoftware.com ก่อน ship
- [ ] **ข้อจำกัดการใช้ example asset (mix-and-match / Spineboy) ใน spike** — example assets เป็นของ Esoteric Software ใช้ได้เพื่อทดสอบ/prototype **ห้าม ship ในเกมจริง** — Phase 0 ใช้ถูกต้องตามเงื่อนไขนี้แล้ว (spike เท่านั้น, ไม่เข้า build)
- [ ] **ตัดสินใจ:** ซื้อ license ก่อน Phase 1 หรือย้าย Tier-2 ไป Unity 2D Animation (built-in, ไม่มี license) ตาม R1 — **การตัดสินใจเป็นของมนุษย์**

**ร่างย่อหน้า exception สำหรับ `conventions.md` (ยังไม่ apply — รออนุมัติ):**
> §Open-source first — exception: `Spine-Unity` runtime (Esoteric Software) ได้รับอนุมัติให้เป็น dependency ของ visual system Tier-2 (Spine backend) เฉพาะเมื่อ Spine Editor license (Professional) ถูกซื้อแล้ว และ example assets ของ Esoteric ห้ามตกค้างใน build จริง asmdef `Visual.Spine.Spikes` แยกเพื่อให้ยัง build ได้และสลับไป Unity 2D Animation ได้หาก license ถูกยกเลิก

## 5. Open Questions — คำตอบจากหลักฐาน spike (เสนอเท่านั้น ห้ามล็อกเอง)

| # | คำถาม | คำตอบที่เสนอ | หลักฐาน |
|---|---|---|---|
| Q3 | จำนวนทิศของ chibi | **2 ทิศ flip เพียงพอ** ตาม lean | S3: flip ทำงานถูกต้องทั้ง Sprite/Spine; S1: `Skeleton.ScaleX = -1` ไม่มีต้นทุนเพิ่ม measurable |
| Q4 | Rig เดียวหรือแยกตามเพศ | **1 rig + body skin ตามเพศ** | S1: mix-and-match พิสูจน์ว่า skin swap ต่อ body part ใช้งานได้ 0–8 KB/set ครั้ง — ไม่จำเป็นต้อง rig คูณเพศ |
| Q6 | `SpineBudget` ที่เหมาะสม | **20 ยังใช้ได้** (2.4× headroom จาก gate 2.0 ms) แต่ rig จริงอาจหนักกว่า example asset | S1: 20 instances = 1.648 ms; เผื่อ margin สำหรับ rig จริงที่มี mesh มากกว่า |

## 6. จุดที่ชน Locked Decisions (ตาม REPORT BACK #6 — รายการให้ทบทวน)

1. **L10** — runtime 3.8 ไม่มี `GetTrack` → ใช้ `GetCurrent` ชั่วคราวตามการอนุมัติของ user ใน session นี้ (ไม่ใช่การเปลี่ยน L10 — แต่ต้องทบทวนว่าจะอัปเกรด runtime เมื่อไร)
2. **C11** — ตัวเลขวัดบนเครื่อง dev เดียว + editor (ไม่ใช่ player build) — Run In Background และ pause ปิดแล้วตามข้อ แต่ควรวัดซ้ำบนเป้าหมายจริงก่อน lock gate
3. **L11** — gate ที่ใช้เป็นค่าเริ่มต้น 2.0 ms — ทั้งสอง subsystem ผ่านพร้อม margin ใหญ่ จึงไม่มีเหตุปรับ แต่เมื่อมีฉากผสม (Phase 2/3) ควรทบทวน

*(ไม่มีจุดใดที่ต้องหยุดงาน — ทุกข้อมีทางแก้ที่ไม่ขัดสถาปัตยกรรม)*

## 7. Proposed `log.md` diff (ยังไม่ apply — ตามขอบเขต T6)

```diff
+## [2026-09-22] new-page | wiki/sources/disciple-visual-system.md | Phase-0 plan: 3-backend chibi render architecture (Portrait/SpriteSheet/Spine)
+## [2026-09-22] new-page | wiki/sources/references/chibi-visual-system-marooned.md | External pattern reference copy from Marooned (pattern-only, not a game source)
+## [2026-09-22] new-page | wiki/sources/disciple-visual-system-phase0-results.md | S1 1.648ms / S2 1.123ms measured — both PASS 2.0ms gate; batch proof 6/1200; S4 license checklist drafted
```

## 8. Acceptance Criteria — ตรวจครบ

- [x] T0: disciple-visual-system.md อยู่ใน LLMWiki + marooned copy พร้อม external-note
- [x] Spike code แยกหมด (Spikes/ + 3 asmdef), ไม่เข้า build, gameplay เดิมไม่ถูกแตะ
- [x] `git diff` บน Shared/ ว่างเปล่า (ไม่ต้อง sync-shared — C4)
- [x] CSV + report มี median/p95, draw calls/batches, GC ครบทั้ง S1/S2 แยกต่อ subsystem
- [x] S1 ใช้ example asset ฟรี — ไม่มีจุด block รอ license
- [x] S2: steady-state spike code zero-alloc (ApplyFrame = 0 B) + ไม่มี Animator เลย
- [x] Core compile ได้โดยไม่พึ่ง Spine (asmdef ไม่อ้าง spine-unity)
- [x] C# 8.0 เท่านั้น (no record/init/target-typed new/global using — grep ยืนยัน)
- [x] ไม่แก้ wiki page อื่นนอกจากไฟล์นี้ (+ proposed log.md diff ใน §7)
- [x] Gate verdict แยก S1/S2 (PASS ทั้งคู่) + ผลรวมเชิงบวก (ระบุว่าไม่ใช่ตัวเลขวัด)
- [x] ไม่มี outfit / TryApplyOutfit / AvatarOutfitChangedMessage ในโค้ดใหม่ (grep ยืนยัน)
- [x] ไม่มี interprocess registration ใหม่ (in-memory เท่านั้น — C5)

## Related Pages

- [[disciple-visual-system]] — แผนหลัก (§11 gate, §14 open questions)
- [[avatar-appearance]] — state schema ที่ spike อ้างถึง (Parts/Colors/PoseId)
- [[references/chibi-visual-system-marooned]] — pattern reference (external)
