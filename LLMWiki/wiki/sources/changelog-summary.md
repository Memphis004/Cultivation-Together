# Changelog — สรุปการเปลี่ยนแปลงทั้งหมดจากบทสนทนานี้

> เอกสารนี้เป็น **changelog ไม่ใช่ GDD ตัวจริง** — สรุปทุกอย่างที่ถูกตัดสินใจ/
> เปลี่ยนแปลงตลอดบทสนทนา เพื่อให้ merge เข้า GDD หลักได้ง่าย จัดกลุ่มตามระบบ
> ไม่เรียงตามลำดับเวลา (ยกเว้น section สุดท้ายที่เรียงตาม lab)

---

## 1. Tech Stack (เปลี่ยนแปลงมากที่สุด)

| ส่วน | เดิม (ตอนเริ่มคุย) | ปัจจุบัน |
|---|---|---|
| DI Container | ยังไม่ตัดสินใจ | **VContainer** |
| Internal messaging | ยังไม่ตัดสินใจ | **MessagePipe** (pub/sub + request-response) |
| Serialization (runtime state) | **Protobuf** (ตัดสินใจแรกสุด) | **MessagePack** (เลิก protobuf ถาวรตั้งแต่ lab 7 — เหตุผล cross-language หมดความจำเป็นเพราะ bridge เป็น C# ทั้งคู่) |
| MCP bridge process | คาดว่าเป็น Node/Python | **.NET console app แยก process** (ใช้ `MessagePipe.Interprocess` client ตรงๆ ได้, `ModelContextProtocol` C# SDK อย่างเป็นทางการ) |
| Event/design-time data | ScriptableObject | **Luban** (Excel → generate C# + JSON runtime data) — ScriptableObject ใช้เฉพาะของที่ผูกกับ Unity object reference จริง (เช่น `UIPanelCatalog` ที่ต้องอ้าง Prefab) |
| UI framework | ยังไม่ตัดสินใจ | **UGUI + TextMeshPro** (ไม่ใช้ UI Toolkit) แบบ MVP Lite (View/Presenter/Service แยกชั้น) |

---

## 2. Architecture หลัก

### 2.1 Composition root
`GameLifetimeScope.cs` (VContainer) เป็น single source of truth ของการ wire
ทุกอย่าง — subsystem ทั้งหมด register ผ่าน `RegisterEntryPoint<T>`,
component ที่ bake ไว้ใน scene ผ่าน `RegisterComponentInHierarchy<T>()`

### 2.2 Message bus (MessagePipe)
สองชั้นแยกกันชัดเจน:
- **In-memory** — สื่อสารภายใน Unity process เดียว (เช่น
  `WorldEventTriggeredMessage`, `DecisionExecutedMessage`)
- **Interprocess (TCP)** — Unity เป็น **server** (`HostAsServer=true`,
  port `3215`), รองรับหลาย client เชื่อมพร้อมกันได้ (สำคัญมากสำหรับแผน
  Twitch integration ในอนาคต — ไม่ต้องสร้าง transport ใหม่)

**กฎเหล็กที่เจอบั๊กจริงมาก่อนถึงได้กฎนี้**: in-process pub/sub ห้ามใช้แทน
cross-process call — ถ้า UI (in-process) ต้องเรียก logic ที่ปกติมาจาก
bridge (cross-process) ต้องมี shared entry point ที่ทั้งสองทางเรียกตรงๆ
(ดู `DecisionExecutor` ใน section 4)

### 2.3 MCP Bridge
`McpBridge/Program.cs` — .NET console app, ต่อ Unity ผ่าน
`MessagePipe.Interprocess` (TCP client), expose MCP tools ผ่าน stdio ด้วย
`ModelContextProtocol` SDK แยก **read tools** (`SectQueryTools`) กับ
**write tools** (`SectActionTools`) เป็นคนละคลาสชัดเจน

Tools ที่มีตอนนี้: `get_sect_state`, `await_next_world_event`,
`execute_decision`, `purchase_item`

---

## 3. Bug/Gotcha สำคัญที่เจอ (เก็บไว้กันเจอซ้ำ)

| # | ปัญหา | สาเหตุ | แก้ |
|---|---|---|---|
| 1 | `Duplicate 'Compile' items` (NETSDK1022) | SDK-style csproj auto-include `.cs` อยู่แล้ว ไม่ต้องประกาศซ้ำ | ลบ `<Compile Include>` block |
| 2 | `AddMessagePipeTcpInterprocess` ไม่มีจริง | สับสน API ระหว่าง VContainer (`ToMessagePipeBuilder()`) กับ `IServiceCollection` (`AddMessagePipe()` คืน `IMessagePipeBuilder` ตรงๆ) | ใช้ API ให้ตรงฝั่ง |
| 3 | `RegisterTcpRemoteRequestHandler` ต้องเรียกทั้งสองฝั่ง | แม้ฝั่ง `HostAsServer=true` ก็ต้องเรียกด้วย ไม่ใช่แค่ caller | เพิ่มให้ครบทั้งคู่ |
| 4 | `ValueTask<T>` vs `UniTask<T>` | MessagePipe ฝั่ง VContainer/Unity ใช้ `UniTask`, ฝั่ง `IServiceCollection` ใช้ `ValueTask` ได้ปกติ | เปลี่ยน return type ให้ตรง context |
| 5 | `SocketException 10048` ตอน `await_next_world_event` | `TcpDistributedSubscriber` เปิด listening socket เองเสมอ ไม่สนใจ `HostAsServer` — ฝั่ง client (bridge) subscribe ผ่าน TCP นี้ไม่ได้เลย | เปลี่ยนจาก pub/sub เป็น request-response ทั้งหมด |
| 6 | `record` ใช้ใน Unity ไม่ได้ (`IsExternalInit` not found) | Unity runtime ไม่มี type นี้แม้ compiler รองรับ C# 9 syntax | ใช้ `class` ธรรมดาแทน |
| 7 | `Instantiate()` throw "main thread only" | Callback จาก `MessagePipe.Interprocess` TCP receive loop ทำงานบน **background thread** ไม่ใช่ main thread | `await UniTask.SwitchToMainThread();` ก่อนแตะ Unity API ใดๆ (เจอครั้งแรกใน `DecisionExecutor`, ต้อง apply กับทุก handler ที่มาจาก interprocess) |
| 8 | Luban `<module name="event">` compile พังยกไฟล์ | `event` เป็น C# reserved keyword | เปลี่ยนชื่อ module เป็น `worldevent` |
| 9 | Luban `SimpleJSON` namespace ผิด | เดาว่าเป็น `SimpleJSON` เฉยๆ ที่จริงคือ `Luban.SimpleJSON` | แก้ `using` ให้ตรง |
| 10 | UI ทั้งหมดไปอยู่กลางจอ | `ContentSizeFitter=PreferredSize` ชนกับ anchor stretch, `UIRoot` เองไม่ stretch เต็ม Canvas | เปลี่ยนเป็น `Unconstrained` + บังคับ stretch ใน `Awake()` |
| 11 | `UniTask.ContinueWith` ไม่มีจริง | UniTask ใช้ async/await ไม่ใช่ fluent `.ContinueWith()` แบบ `Task` | เปลี่ยนเป็น local `async UniTaskVoid` method |
| 12 | Unity Editor Pause/`Run In Background` | ปุ่ม Pause ของ Editor เอง (ไม่ใช่ `TimeSystem.IsPaused`) หยุด `Tick()` ทั้งหมด, ไม่เปิด `Run In Background` ทำให้ทำงานกระตุกตอนสลับหน้าต่าง | เปิด setting เหล่านี้ |

---

## 4. Decision Pipeline

`DecisionExecutor` เป็น **single entry point** ที่ทั้ง `DecisionLogger`
(มาจาก bridge, interprocess) และ `EventPopupPresenter` (มาจาก UI คลิกใน
เกม) เรียกตรงๆ — ป้องกันบั๊กที่เคยเกิด (publish ผ่าน channel ผิดแล้วเงียบ
ไม่มีอะไรเกิดขึ้น) พร้อม hop ไป main thread ก่อนทำงาน (bug #7 ด้านบน)

---

## 5. ระบบเศรษฐกิจ/เกมเพลย์ที่ implement แล้ว

- **Gathering** — `SectStateProvider.TickGathering()`, fractional
  accumulator ต่อ task กัน sub-1 หาย
- **Crafting** — `TickCrafting()`, per-disciple progress, all-or-nothing
  consumption, ownership rule (ศิษย์ทั่วไป→คลังสำนัก, ผู้อาวุโส+→ส่วนตัว)
- **Recruiting** — ผูกกับ event `new_disciple_applicant`, round-robin
  gathering task ให้คนใหม่
- **Purchase store** — `TryPurchaseItem()`, ราคา `grade×50` (placeholder),
  atomic check-and-deduct
- **World events** — `WorldEventSystem` สุ่ม weighted จาก `LubanEventPool`
  (ไม่ hardcode แล้ว), auto-pause จนกว่าจะ `execute_decision`
- **ทุกจุดที่แก้ `RawResources` ต้องผ่าน `AdjustAndNotify()`** เท่านั้น —
  single choke point ให้ publish `SectResourceChangedMessage` ครบ ไม่งั้น
  HUD ไม่อัปเดต (เจอเป็นบั๊กจริงในฉบับร่าง Task System ด้วย ดู section 7)

---

## 6. UI Framework (Xianxia.UI.MVP Lite)

Pattern: View (MonoBehaviour) / Presenter (plain C#, Transient) / Service
(orchestrator) — panel resolve ผ่าน VContainer ด้วย explicit enum→Type
mapping ไม่ scan assembly

**Panel ที่มีแล้ว**: `ResourceHud` (top bar, delta indicator สีเขียว/แดง
auto-clear หลัง 1 วิ), `EventPopup` (เปิดอัตโนมัติเมื่อมี event ต้อง
ตัดสินใจ), `LogWindow` (log เหตุการณ์สำคัญ — รับสมัคร/event/decision
เท่านั้น ไม่รวม resource tick ที่จะรกเกินไป)

`SectHudView` เดิม (polling-based) **ถูกลบทิ้งแล้ว** แทนที่ด้วย
`ResourceHud` panel ที่ message-driven ทั้งหมด

---

## 7. Task System v2 (ล่าสุด — เพิ่งทำเสร็จ)

**เป้าหมาย**: โซโล่/ไม่มี viewer → ผู้เล่นสั่ง task ศิษย์ทุกคนได้อิสระแบบ
Rimworld/ACS — มี viewer เข้าร่วมเป็นศิษย์ → viewer สั่งได้แค่ของตัวเอง

**เพิ่มเข้า `DiscipleState`**: `DiscipleOwnerType` (Npc/Player/Viewer) +
`OwnerId` (Twitch user id ถ้าเป็น Viewer) — เลข `[Key(N)]` ที่แน่นอนยังไม่
ยืนยัน ต้องเช็คกับไฟล์จริงก่อน (`Avatar` อยู่ที่ `Key(6)` ล่าสุดที่ยืนยันได้)

**`TryAssignTask(requesterId, discipleId, taskId, out failReason)`** —
`requesterId == "SECT_MASTER"` ข้ามการเช็คสิทธิ์ได้ทุกคน, อื่นๆ ต้องตรงกับ
`disciple.OwnerId` เท่านั้น — เช็คฝั่ง Unity ที่เดียว (single source of
truth กันทุก client โกง)

**สถาปัตยกรรม Twitch chat command (`!cultivate`)**: ไม่ต้องสร้าง
transport ใหม่ — Unity เป็น TCP server รับหลาย client ได้อยู่แล้ว, Twitch
bot service (แยก process) ส่ง `AssignTaskRequest` เข้า port เดียวกับที่
`McpBridge` ใช้อยู่ได้เลย เป็นแค่ client คนละตัวที่ใส่ `RequesterId`
ต่างกัน

**บั๊กที่แก้ก่อนถึงจะ implement ได้** (ดู section 3): ร่างเดิมของ
`TickCrafting` bypass `AdjustAndNotify()`, และต้องระวัง thread-safety
(bug #7) ถ้า UI ในอนาคต subscribe `DiscipleTaskChangedMessage`

**ยังไม่ทำ** (out of scope รอบนี้): flow "viewer เข้าร่วม → ได้ `OwnerId`"
จริง, Twitch bot service ตัวจริง, UI panel มอบหมาย task

---

## 8. Avatar System (อยู่ระหว่างพัฒนา นอกบทสนทนานี้เป็นหลัก)

พัฒนาไปไกลกว่าที่ผมมีในเครื่องมือ (Dictionary-based `Parts` แทน fixed
property, `DiscipleSex`, hair 2-layer render, framing preset, avatar
customization UI panel แบบ draft pattern) — เปลี่ยนจาก fixed-slot ที่ผม
ร่างไว้ตอนแรกไปเป็น dynamic dictionary ตามเหตุผลเดียวกับที่
`SectStockpile.RawResources` เป็น Dictionary (AI ค้นพบ slot เองได้จาก
`get_sect_state` ไม่ต้องรู้ schema ล่วงหน้า)

กำลังคุยเรื่อง **Pose-Linked Outfit Package** ต่อ (แก้ปัญหาผสม
part ข้าม pose แล้วตำแหน่งเพี้ยน) — พบช่องโหว่ที่ยังไม่ปิด (Override ไม่
validate pose compatibility) และคำถามเรื่อง economy (ซื้อ outfit ทั้งชุด
vs ซื้อ part แยกชิ้น) ที่ยังไม่ตัดสินใจ

---

## 9. Additive Scene Architecture — ตัดสินใจเลื่อนออกไปก่อน

มีเอกสาร draft พร้อม (`additive-scene-architecture.md`) แต่ตัดสินใจ
**ยังไม่ implement เต็มรูปแบบตอนนี้** เพราะยังไม่มี gameplay content จริง
ที่ต้องแยก scene (BuildingSystem ยัง stub) — เก็บเอกสารไว้เป็น draft,
กลับมาทำเมื่อมี content จริงที่ต้องการ GameObject ในโลก

แต่มีการเริ่มทำ `SceneLoader` + `AdditiveSceneTest.cs` (test script) ไว้
ล่วงหน้าแล้วบางส่วน — เจอบั๊ก `UniTask.ContinueWith` ไม่มีจริง (bug #11)
ระหว่างทดสอบ

---

## 10. ลำดับ lab แบบย่อ (อ้างอิงไล่เวลา)

1. Architecture diagram แรก (Unity + MCP, ยังไม่มี VContainer/MessagePipe)
2. เพิ่ม VContainer + MessagePipe เข้า stack
3. Debug round-trip จนรันได้จริงครั้งแรก (`get_sect_state`)
4. ทาง write (`execute_decision`) ผ่าน
5. `WorldEventSystem` ปิด loop เต็มวง
6-7. เจอ+แก้ข้อจำกัด TCP subscribe, ตัดสินใจเลิก protobuf ถาวร
8-9. `DiscipleSystem`/`ResourceCraftingSystem` จริง (เลิก mock)
10. Purchase store
11. UI พื้นฐานครั้งแรก (ก่อนเปลี่ยนเป็น MVP Lite)
12. ย้าย EventData จาก ScriptableObject → Luban
13. Xianxia.UI.MVP Lite เต็มรูปแบบ (EventPopup + ResourceHud + LogWindow)
14. Avatar system (พัฒนาต่อนอกบทสนทนาหลัก ไปไกลกว่าที่ตามมาได้ทัน)
15. Additive Scene — ตัดสินใจเลื่อน
16. Task System v2 — เพิ่ม ownership model สำหรับ viewer control

---

## 11. สิ่งที่ยังไม่ได้ตัดสินใจ (รวมจากทั้งบทสนทนา)

- Core loop แบบ final
- ระบบต่อสู้ (เอียงไปทาง ACS-style)
- Stat system, วิชา/ตำรา, ไอเทม/อาวุธ/เกราะ/ยา แบบละเอียด
- สงครามสำนัก (WIP)
- กติกาปล้นหินวิญญาณระหว่างศิษย์
- Avatar: outfit package vs ซื้อ part แยก (economy meaning), pose
  compatibility validation ยังไม่ปิด
- `BuildingSystem` ยัง stub เต็มๆ
- Twitch integration service ตัวจริง (มีแค่สถาปัตยกรรมที่ออกแบบไว้)
- ราคา/consequence หลายจุดยังเป็น placeholder รอ balance จริง
