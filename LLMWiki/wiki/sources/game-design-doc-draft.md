---
title: Game Design Document (Draft)
type: gdd
status: draft — ร่างจากบทสนทนาทั้งหมด รอ merge เข้า GDD จริง/ตรวจทาน
sources:
  - บทสนทนาการออกแบบทั้งหมด (Claude)
  - changelog-summary.md
related:
  - "[[sources/architecture]]"
  - "[[sources/mechanics]]"
  - "[[sources/task-system]]"
  - "[[sources/avatar-appearance]]"
created: 2026-09-04
tags: [gdd, vision, design, xianxia, draft]
---

# Game Design Document (Draft) — Sect Management

> ⚠️ **สถานะ: ร่างจากบทสนทนา** ไม่ใช่ GDD ตัวจริงที่ merge แล้ว มีเนื้อหาที่
> "ยังไม่ตัดสินใจ" ปนอยู่เยอะโดยเจตนา (ดู section 10) — อย่าถือเป็น spec
> สุดท้าย ใช้เป็นจุดตั้งต้น merge เข้า wiki จริงเท่านั้น

## 1. Concept & Vision

**เกม sect-management simulator** แนวเซียนจีนโบราณ (xianxia) บน Unity
ออกแบบให้ **AI Game Master เล่นแทนคนได้จริง** ผ่าน MCP (Model Context
Protocol) — ผู้เล่น/AI VTuber รับบทเจ้าสำนัก บริหารศิษย์ ทรัพยากร
เศรษฐกิจของสำนัก

**แรงจูงใจหลัก**: ลดภาระสตรีมเมอร์ที่ต้องอ่านออกเสียง/คุมกลไกเกมเองตลอด
โดยให้ AI GM รับหน้าที่แทน พร้อมเปิดให้ผู้ชมสตรีมเข้าร่วมเป็น "ศิษย์" โหวต/
สั่งการได้ผ่าน Twitch — คล้าย *King of the Castle* บน Twitch

### Roles
- **ผู้เล่น/AI VTuber** — เจ้าสำนัก (Sect Master), สั่งการศิษย์ทุกคนได้อิสระ
- **ผู้ชมสตรีม** — เข้าร่วมเป็นศิษย์ (เมื่อมี viewer เข้าร่วม) สั่งการได้
  เฉพาะศิษย์ของตัวเอง

### Reference Games
| เกม | สิ่งที่หยิบยืม |
|---|---|
| 龙胤立志传 / The Scroll of Taiwu | แผนที่ เมือง/หมู่บ้าน/สำนัก, ระบบเวลา, เกรดวัตถุดิบ→เกรดผลลัพธ์ |
| Rimworld | Real-time + pause + speed control, ผู้เล่นสั่ง task ศิษย์อิสระ |
| Amazing Cultivation Simulator (ACS) | จังหวะ combat (เอียงไปทางนี้เพื่อให้ AI ตัดสินใจง่าย) |

---

## 2. Design Pillars

1. **AI-Playable First** — ทุกกลไกต้อง query/สั่งการผ่าน MCP ได้จริง ไม่พึ่ง
   "สัญชาตญาณมนุษย์" ที่ AI เข้าไม่ถึง
2. **Decisions Are Checkpoints** — event สำคัญ auto-pause เกม รอการตัดสินใจ
3. **Persistent, Compounding State** — การตัดสินใจมีผลระยะยาวจริง (state
   เดียวคงอยู่ตลอด process ไม่ใช่ mock ที่ regenerate ทุกครั้ง)
4. **Single Source of Truth ทุกจุดที่มีผลประโยชน์ขัดแย้งกัน** — permission/
   economic check ทำที่ Unity (server) เดียว ไม่เชื่อ client ไหนทั้งนั้น
5. **Design-time data แยกจาก runtime state ชัดเจน** — ของที่ author ใน
   Editor/Excel (Luban) ไม่ปนกับ state ที่เปลี่ยนตลอดเวลา (MessagePack)

---

## 3. Core Loop

> ⏳ **ยังไม่ fix เป็นทางการ** — รอ gameplay systems solidify ก่อน โครงที่
> ใช้ทดสอบอยู่ตอนนี้:

```
[Sect State] → [Event Trigger] → [Choices] → [Decision] →
[Consequences] → [Check End] → loop
```

- Event สุ่ม weighted จาก pool ทุก 15 วิ (ปรับได้) ระหว่างไม่มี event ค้าง
- Event ที่ `requiresDecision=true` → auto-pause จนกว่าจะตัดสินใจ
- ตัดสินใจผ่าน UI ในเกม **หรือ** ผ่าน `execute_decision` MCP tool (AI GM)
  — เข้า entry point เดียวกันเสมอ (`DecisionExecutor`)

---

## 4. เศรษฐกิจสำนัก

### 4.1 สองสกุลเงิน

| สกุลเงิน | ได้จาก | ใช้ทำอะไร |
|---|---|---|
| **หินวิญญาณ (Spirit Stones)** | ภารกิจ/ผจญภัย/ปราบปีศาจ, เบี้ยเลี้ยงรายเดือนตามตำแหน่ง | แลกเปลี่ยนไอเทมทั่วไป, **ปล้น/แย่งชิงกันเองได้** (ยังไม่ลง schema) |
| **ค่าคุณูปการ (Contribution)** | ผลงาน: เก็บเกี่ยว/งานฝีมือ (เกรดสูง=ได้เยอะ)/ต่อสู้ | แลกของจากคลังสำนัก (`purchase_item`) |

### 4.2 ทรัพยากรดิบ (ของส่วนรวมสำนัก)
`herb` (สมุนไพร), `wood` (ไม้), `ore` (แร่), `provisions` (เสบียง) —
เก็บเป็น `Dictionary<string,int>` (ขยายชนิดทรัพยากรได้โดยไม่ต้องแก้ schema)

### 4.3 กติกาความเป็นเจ้าของผลผลิต
- ศิษย์นอก/ศิษย์ใน → ของที่คราฟท์เข้าคลังสำนัก (`CraftedGoods`) → คนอื่น
  ซื้อด้วยค่าคุณูปการได้
- ผู้อาวุโสขึ้นไป → เก็บของที่คราฟท์เป็นของส่วนตัว (`PersonalInventory`)

### 4.4 ร้านค้าสำนัก (`purchase_item`) — ✅ implemented
ราคา `grade × 50` ค่าคุณูปการต่อชิ้น (placeholder รอ balance จริง) —
atomic check-and-deduct: เช็คของในคลัง → เช็คเงินพอไหม → หักพร้อมกันทั้งคู่
ไม่มี partial state ถ้า fail

---

## 5. ระบบศิษย์ (Disciples)

### 5.1 Rank
`OuterDisciple → InnerDisciple → Elder → SectMaster` — promotion flow
ยังไม่ implement (รอระบบตำแหน่งจากอาคาร)

### 5.2 รับสมัคร — ✅ implemented
ผูกกับ world event `new_disciple_applicant` → `execute_decision` ด้วย
choice ที่มีคำว่า "accept" → เพิ่มศิษย์นอกใหม่จริง มอบหมาย gathering task
แบบ round-robin

### 5.3 Task System (v2) — ออกแบบเสร็จ, รอ implement
**เป้าหมาย**: โซโล่/ไม่มี viewer → เจ้าสำนักสั่ง task ศิษย์ทุกคนได้อิสระ
แบบ Rimworld/ACS — มี viewer เข้าร่วมเป็นศิษย์ → **viewer สั่งได้เฉพาะ
ศิษย์ของตัวเอง**

**Ownership model**: `DiscipleState` เพิ่ม `OwnerType` (Npc/Player/Viewer)
+ `OwnerId` (Twitch user id) — permission check ที่ Unity ฝั่งเดียว
(`requesterId == "SECT_MASTER"` ข้ามเช็คได้ทุกคน, อื่นๆ ต้องตรงกับ
`OwnerId`)

**Twitch chat command** (`!cultivate` ฯลฯ) — ไม่ต้องสร้าง transport ใหม่
Unity เป็น TCP server รับหลาย client อยู่แล้ว (bridge + Twitch bot service
ในอนาคต เชื่อม port เดียวกัน, เป็นแค่ client คนละตัว)

รายละเอียดเต็ม: ดู `task-system-v2.md`

### 5.4 Avatar / Character Customization — อยู่ระหว่างพัฒนา
Sprite-swap system, `Parts: Dictionary<string,string>` (slot→partId,
ขยาย slot ได้อิสระ), `DiscipleSex`, hair 2-layer render, framing preset
(FullBody/Bust/HeadIcon บน canvas เดียวกัน)

**กำลังออกแบบต่อ — Pose-Linked Outfit Package**: แก้ปัญหาผสม part ข้าม
pose แล้วตำแหน่งเพี้ยน (เช่น หัวท่ายืน + ตัวท่าชี้ = คอลอย) ด้วยแนวคิด
"outfit = ชุด part ที่วาดมาคู่กันตาม pose เดียวกัน" — **ยังไม่ปิด**:
pose-compatibility validation ของ override, และความหมายทาง economy (ซื้อ
outfit ทั้งชุด vs ซื้อ part แยก)

---

## 6. ระบบทรัพยากร/งานฝีมือ

### 6.1 เก็บเกี่ยว (Gathering) — ✅ implemented
Passive, ตาม `CurrentTask` ของศิษย์ (`gathering_herb` ฯลฯ) — fractional
accumulator กัน production ต่ำกว่า 1 หน่วยหายไประหว่าง tick

### 6.2 งานฝีมือ/คราฟท์ (Crafting) — ✅ implemented
Per-disciple progress, all-or-nothing resource consumption (ถ้าทรัพยากร
ไม่พอ progress ค้างรอ ไม่เสียทิ้ง) ตามด้วยกติกาความเป็นเจ้าของ (§4.3)

**กฎสำคัญ**: ทุกจุดที่แก้ `RawResources` **ต้องผ่าน `AdjustAndNotify()`**
เท่านั้น (single choke point publish `SectResourceChangedMessage`) —
ไม่งั้น HUD จะไม่อัปเดต (เจอเป็นบั๊กจริงตอนร่าง Task System v2)

---

## 7. World Events

### 7.1 ระบบปัจจุบัน — ✅ implemented
สุ่ม weighted จาก event pool ทุก 15 วิ (ปรับได้), ข้าม event ใหม่ถ้ายังมี
event ค้างรอ decision, event มี `description` + `choices` เต็มรูปแบบ
(ไม่ใช่แค่ eventId เปล่าๆ)

### 7.2 Authoring — Luban pipeline
Excel (`event.xlsx`, `event_choice.xlsx` — สองตารางแยก join ด้วย
`eventId`, ไม่ nest list เพื่อความปลอดภัย) → `gen.sh`/`gen.bat` →
generate C# + JSON runtime data → ไม่ต้องคลิกสร้าง asset ทีละไฟล์เลย

### 7.3 ยังไม่ทำ
- State-conditional event selection (ทรัพยากรน้อย → event ต่างจาก
  ทรัพยากรเยอะ)
- Event cooldown/chain/priority
- Consequence ต่อ choice ที่แตกต่างกันจริง (ตอนนี้ทุก choice ของ event
  เดียวกันมีผลเหมือนกันหมด ยกเว้น `new_disciple_applicant`)

---

## 8. Buildings

**สถานะ: stub เต็มๆ ยังไม่มี logic** — ดีไซน์ตั้งใจไว้ว่าคลิกสร้างอย่าง
เดียว ไม่มีระบบวางตำแหน่งบนแมพ (กัน Agent loop ซับซ้อนเกินไปสำหรับ AI)
จำนวนตำแหน่ง (rank slot) ควรปลดล็อกจากการสร้างอาคาร — ยังไม่ implement

---

## 9. สถาปัตยกรรมเทคนิค (สรุปย่อ — อ้างอิงเต็มที่ `architecture.md`)

| ชั้น | เทคโนโลยี |
|---|---|
| Game engine | Unity (C#), UGUI + TextMeshPro (ไม่ใช้ UI Toolkit) |
| DI | VContainer, composition root เดียว (`GameLifetimeScope.cs`) |
| Internal messaging | MessagePipe (in-memory pub/sub + request-response) |
| Cross-process (Unity ↔ AI GM / Twitch) | `MessagePipe.Interprocess` TCP — Unity เป็น server รับหลาย client |
| MCP bridge | .NET console app แยก process, `ModelContextProtocol` SDK |
| Serialization | MessagePack (เลิก protobuf ถาวรตั้งแต่ lab 7) |
| Design-time data (event/avatar parts) | Luban (Excel → C# + JSON) |
| UI pattern | MVP Lite — View (MonoBehaviour) / Presenter (plain C#, Transient) / Service |

**กฎเหล็ก**: in-process pub/sub ห้ามใช้แทนการเรียกข้าม process — ถ้า UI
ต้องเรียก logic เดียวกับที่ bridge เรียก ต้องมี shared entry point
(`DecisionExecutor` เป็นตัวอย่าง) ไม่ใช่ publish คนละ channel

**กฎเหล็ก 2**: handler ที่มาจาก `MessagePipe.Interprocess` ทำงานบน
**background thread** — โค้ดที่แตะ Unity API (เช่น `Instantiate`) ต้อง
`await UniTask.SwitchToMainThread()` ก่อนเสมอ

รายละเอียด bug/gotcha ทั้งหมด: ดู `changelog-summary.md` section 3

---

## 10. UI ที่มีแล้ว

MVP Lite framework — panel resolve ผ่าน VContainer, explicit enum→Type
mapping (ไม่ scan assembly)

| Panel | สถานะ | หน้าที่ |
|---|---|---|
| `WalletHud` | ✅ | Top-right bar, SpiritStones + Contribution (เดิมชื่อ `ResourceHud` แสดง stockpile ด้วย) |
| `EventPopup` | ✅ | เปิดอัตโนมัติเมื่อมี event ต้องตัดสินใจ |
| `LogWindow` | ✅ | Log เหตุการณ์สำคัญ (รับสมัคร/event/decision) ไม่รวม resource tick |
| Avatar Customization | 🔶 กำลังทำ | Draft/diff-commit pattern |
| Task Assignment | ⏳ | รอ implement ตาม task-system-v2.md |
| Disciple List | ⏳ | enum จองไว้ ยังไม่ implement |

---

## 11. MCP Tools (AI GM Interface)

| Tool | ประเภท | หน้าที่ |
|---|---|---|
| `get_sect_state` | Read | Full state snapshot |
| `await_next_world_event` | Read (blocking) | รอ event ถัดไปที่ต้องตัดสินใจ |
| `execute_decision` | Write | ตอบ choice ของ event ปัจจุบัน |
| `purchase_item` | Write | ซื้อของจากคลังด้วยค่าคุณูปการ |
| `assign_task` | ⏳ Write (ออกแบบแล้ว) | สั่ง task ศิษย์ (`requesterId="SECT_MASTER"` จากทางนี้เสมอ) |

---

## 12. Twitch Integration (ออกแบบสถาปัตยกรรมแล้ว ยัง implement)

Twitch bot service (แยก process, C#/Node.js) เชื่อมเข้า Unity ผ่าน
`MessagePipe.Interprocess` TCP port เดียวกับ MCP bridge — เป็นแค่ client
คนละตัว ไม่ต้องสร้าง infrastructure ใหม่

**ยังไม่ทำ**: flow "viewer เข้าร่วม → ได้ผูกกับศิษย์คนไหน (`OwnerId`)",
Twitch Extension frontend, vote aggregation

---

## 13. Deferred / เลื่อนออกไปก่อน

**Additive Scene Architecture** — มีเอกสาร draft พร้อมแล้ว แต่ตัดสินใจยัง
ไม่ implement เต็มรูปแบบ เพราะยังไม่มี gameplay content จริงที่ต้องแยก
scene (BuildingSystem ยัง stub) — กลับมาทำเมื่อมี GameObject ในโลกจริง
(อาคาร/NPC เดินได้) ที่ต้องการ separation จริง

---

## 14. สิ่งที่ยังไม่ได้ตัดสินใจ

- ระบบต่อสู้ (เอียงไปทาง ACS-style — resolve เร็ว เหมาะ AI ตัดสินใจ)
- Stat system (cultivation realm, 灵根 ธาตุ ฯลฯ)
- วิชา/ตำรา (เรียนยังไง, เกรดตำรา)
- ไอเทม/อาวุธ/เกราะ/ยา แบบละเอียด (ตอนนี้มีแค่ 2 ไอเทม placeholder)
- สงครามสำนัก (WIP, ไม่มี schema)
- กติกาปล้นหินวิญญาณระหว่างศิษย์ (มีในดีไซน์ แต่ไม่มี schema)
- Save/load system
- Avatar: outfit package economy meaning + pose compatibility validation
- ราคา/consequence หลายจุดยังเป็น placeholder รอ balance จริง

---

## Related Pages

- `architecture.md` — สถาปัตยกรรมเทคนิคเต็ม
- `changelog-summary.md` — timeline การเปลี่ยนแปลงแบบละเอียด
- `task-system-v2.md` — Task System design เต็ม
- `avatar-appearance.md` — Avatar system design
- `additive-scene-architecture.md` — Scene architecture (deferred)
