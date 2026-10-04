# Task: Implement Task System v2

## Context — read first

1. `task-system-v2.md` (แนบมาด้วย) คือ spec หลัก — ใช้เป็นฐาน ไม่ใช่
   `task-system.md` (v1, มีบั๊กที่ v2 แก้แล้ว)
2. อ้างอิง convention จริงจาก commit `db73e3e` (BuildingSystem Phase 1)
   เป็นแม่แบบการทำงาน — ระบบใหม่นี้ควรมีรูปร่างคล้ายกัน: entry point จริง
   แทน stub, ผ่าน choke point เดิม, มี EditMode tests, มี wiki log entry

## Scope (Phase 1 — ตาม task-system-v2.md section 8 checklist)

- [ ] ยืนยัน next-free `[Key(N)]` บน `DiscipleState` จากไฟล์จริงก่อนเขียน
      (อย่าเดาเลข — เอกสารนี้ตั้งใจไม่ฟันธงไว้)
- [ ] เพิ่ม `DiscipleOwnerType` enum (`Npc`/`Player`/`Viewer`) +
      `OwnerType`/`OwnerId` เข้า `DiscipleState`
- [ ] `TaskDef`/`TaskPool` — ตาม `task-system.md` เดิม (plain C# loader,
      **ไม่ใช้** ScriptableObject)
- [ ] `SectStateProvider.TryAssignTask(requesterId, discipleId, taskId, out failReason)`
      พร้อม permission check (`requesterId=="SECT_MASTER"` ข้ามได้ทุกคน,
      อื่นๆ ต้องตรง `disciple.OwnerId`)
- [ ] `AssignTaskRequest`/`AssignTaskResponse`/`DiscipleTaskChangedMessage`
      ใน `Shared/GameMessages.cs` — sync 3 จุดตาม pattern ที่ log.md เคย
      เตือนไว้ (`sync-shared.sh` ให้ครบ Unity + McpBridge + canonical)
- [ ] `AssignTaskHandler` (`IAsyncRequestHandler`) + register ใน
      `GameLifetimeScope` — RPC pattern เดียวกับ `PurchaseItemHandler`
- [ ] `assign_task` MCP tool ใน `McpBridge/Program.cs`
      (`SectActionTools`, ส่ง `RequesterId="SECT_MASTER"` จากทางนี้เสมอ)
      — **ต่างจาก BuildingSystem Q3** (ที่ตัดสินใจไม่เปิด MCP tool) เพราะ
      Task System ต้องการให้ AI GM สั่งงานได้เป็นหลัก ยืนยันจุดนี้ก่อนถ้า
      ไม่แน่ใจ

## บั๊กที่ต้องระวัง (ยืนยันแล้วจาก session ก่อนหน้า ไม่ใช่แค่ทฤษฎี)

1. **ทุกจุดที่แก้ `Stockpile.RawResources` ต้องผ่าน `AdjustAndNotify()`**
   ห้าม mutate dictionary ตรงๆ เด็ดขาด — v1 draft ของ `TickCrafting` เคย
   bypass ตัวนี้มาก่อน ทำให้ `SectResourceChangedMessage` ไม่ publish, HUD
   เงียบไม่อัปเดต (ไม่ error แต่ผิด)
2. **Thread safety** — `AssignTaskHandler` ถูกเรียกจาก
   `MessagePipe.Interprocess` TCP receive thread (ไม่ใช่ main thread) ถ้า
   subscriber ของ `DiscipleTaskChangedMessage` ตัวไหนแตะ Unity API
   (`Instantiate`, UI update ที่ไม่ใช่แค่ set `.text`) ต้อง
   `await UniTask.SwitchToMainThread();` ก่อน — pattern เดียวกับที่
   `DecisionExecutor` ใช้อยู่แล้ว ไปดูไฟล์นั้นเป็นตัวอย่าง
3. **C# 8 compatible เท่านั้น** — ห้ามใช้ `record`, `init`-only property
   (Unity runtime ไม่มี `IsExternalInit` แม้ compiler รองรับ syntax)

## Deliverable format (ตาม convention ที่ repo ใช้อยู่แล้ว)

- **EditMode tests** ให้ครบ (มาตรฐาน repo นี้: ทุก feature ใหม่มี test คู่
  — ดู `BuildingGridTests.cs`/`BuildingPlacementTests.cs` เป็นแบบ) อย่าง
  น้อยครอบคลุม: `TryAssignTask` สำเร็จกรณี `SECT_MASTER`, ปฏิเสธกรณี
  `OwnerId` ไม่ตรง, resource-change publish ยังทำงานถูกหลัง refactor
- **Wiki log entry** ใน `LLMWiki/wiki/log.md` format เดียวกับ entry อื่นๆ
  ที่มีอยู่ (`## [YYYY-MM-DD] new-feature | ไฟล์ที่แก้ | คำอธิบาย`)
- ถ้าเจอจุดที่ spec ไม่ชัด (เช่น เลข `Key(N)` ที่แน่นอน, ชื่อ task id
  เริ่มต้น, ว่า `assign_task` ควรอยู่กลุ่ม `SectQueryTools` หรือ
  `SectActionTools`) **ตัดสินใจ default ที่สมเหตุสมผลไปก่อน** แล้ว log ไว้
  ใน `open-questions.md` (pattern เดียวกับ BuildingSystem Q1-Q6) รอ human
  confirm ทีหลัง — ไม่ต้องหยุดรอถาม

## Explicitly out of scope (อย่าแตะ)

- Twitch bot service ตัวจริง / flow "viewer เข้าร่วม → ได้ `OwnerId`"
  (มีแค่ architecture ที่ออกแบบไว้ใน task-system-v2.md section 5 — ยังไม่
  ต้อง implement)
- UI panel มอบหมาย task (ถ้าจะทำ รอ confirm scope แยกต่างหากก่อน)
- อย่าแตะ `BuildingSystem`/`BuildingMenuPresenter` ที่เพิ่งเสร็จใน
  commit `db73e3e`
