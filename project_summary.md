# Xianxia Sect Simulator — MCP-Enabled Game Framework

สรุปการออกแบบเท่าที่คุยกันมา

## แนวคิดโปรเจกต์

เกม simulator บริหารสำนักเซียน (xianxia cultivation sect) บน Unity ที่ออกแบบให้ AI Agent
(เช่น Open-LLM-VTuber) เข้ามาเล่นเป็น **Game Master / เจ้าสำนัก** แทนคนได้ผ่าน MCP
โดยมีเป้าหมายรองรับสตรีมแบบโหวตร่วมกับผู้ชม (คล้าย King of the Castle บน Twitch)
ให้ผู้ชมร่วมเป็น "ศิษย์สำนัก" โหวตตัดสินใจเหตุการณ์ต่างๆ ผ่าน Twitch extension

**แรงจูงใจหลัก**: ลดความเหนื่อยของสตรีมเมอร์ที่ต้องอ่านออกเสียง/คุมกลไกเกมเองตลอด
โดยให้ AI VTuber รับบทนี้แทน

## Tech stack ที่ตกลงไว้

| ส่วน | เทคโนโลยี |
|---|---|
| Game engine | Unity (C#) |
| DI / composition root | **VContainer** — wires ทุก subsystem, เบากว่า Zenject |
| Internal messaging (subsystem-to-subsystem) | **MessagePipe** — pub/sub + request-response, zero-alloc, inject ผ่าน VContainer |
| Unity ↔ MCP bridge IPC | **MessagePipe.Interprocess (TCP, localhost)** — Unity host TCP endpoint, bridge process ต่อเป็น client |
| MCP bridge process | **.NET console app แยก process** (ไม่ใช่ Node/Python) — ใช้ `MessagePipe.Interprocess` client ตรงๆ ได้ และ expose tools ผ่าน official `ModelContextProtocol` C# SDK (stdio transport) ให้ AI GM client (Open-LLM-VTuber) ต่อเข้ามา แนวเดียวกับ Wanxiang.Prelude ที่แยก Frontend/Backend เป็น C# ทั้งคู่ |
| Twitch extension | Next.js (เลือกเพราะ API routes ทำหน้าที่ EBS ในตัว, ตัวอย่าง/library เยอะกว่า Vue สำหรับ Extensions) |
| Vote aggregation backend | Node.js + WebSocket (Socket.io/ws) หรือ Supabase Realtime/PlayFab |
| Session state (คนเล่นเองไม่มี AI) | authoritative server แบบ JSON state ธรรมดา ไม่ต้องใช้ Mirror/Netcode เพราะเป็นเกม turn-based ไม่ใช่ real-time physics |
| Persistent data | Node.js + Fastify/Express + Redis (session ชั่วคราว) + Postgres (progression) |

**MCP tools ที่วางแนวไว้**: แยก read (`get_sect_state`, `await_next_world_event`)
กับ write (`execute_decision`) เป็นคนละ tool type ชัดเจนในโค้ดฝั่ง bridge

**Message type แยกจาก state**: pub/sub message ที่ส่งผ่าน MessagePipe (เช่น
`DiscipleRecruitedMessage`, `WorldEventTriggeredMessage`) เป็นคนละชุดกับ
`SectEconomyState` ใน `economy.proto` — message คือ delta/event แจ้งว่า
"เกิดอะไรขึ้น", ส่วน state คือ full snapshot ที่ query ตามต้องการ ไม่ปนกัน

## Core loop (สถานะ: ยัง WIP — จงใจไม่ fix ตอนนี้)

วางไว้คร่าวๆ ว่า `[Sect State] → [Event Trigger] → [Choices] → [Decision] →
[Consequences] → [Check End] → loop` แต่ระหว่าง brainstorm ตัดสินใจ **ยังไม่ fix
core loop** เพราะยังคิดระบบเกมไม่ครบ รอ finalize gameplay systems ก่อน

## แนวทางเกม (อ้างอิง 龙胤立志传 / The Scroll of Taiwu / Rimworld / Amazing Cultivation Simulator)

- **แผนที่**: มีเมือง/หมู่บ้าน/สำนัก แต่ละที่มีแหล่งเก็บเกี่ยวทรัพยากร
- **ระบบทรัพยากร-คราฟท์**: ศิษย์เก็บเกี่ยว (เสบียง/สมุนไพร/ไม้/เหล็ก) → คราฟท์เป็นของสำเร็จ
  (อาหาร/ยา) โดยเกรดวัตถุดิบมีผลต่อเกรดผลลัพธ์ (แนวเดียวกับ Taiwu: Beyond The Dome)
- **ระบบเวลา**: real-time ไหลต่อเนื่อง + กด pause ได้ + ปรับความเร็ว (แนว Rimworld/ACS)
  ไม่ใช้ turn-based รายเดือนแบบ Taiwu — เสนอเพิ่ม auto-pause เมื่อมี event สำคัญ
  เพื่อให้เป็น checkpoint ตัดสินใจของ AI GM ได้พอดี
- **ระบบศิษย์**: สมัครเข้ามาเรื่อยๆ (รวมถึงผู้ชมที่กด join) → เริ่มเป็นศิษย์นอก →
  ผู้เล่น/AI แต่งตั้งตำแหน่งได้ จำนวนตำแหน่งปลดล็อกจากสิ่งปลูกสร้าง
- **สิ่งปลูกสร้าง**: คลิกสร้างอย่างเดียว ไม่มีระบบวางตำแหน่งบนแมพ (เพื่อไม่ให้ Agent loop
  ซับซ้อนเกินไปสำหรับ AI)
- **ยังไม่ตัดสินใจ**: ระบบ combat (เสนอแนวทาง ACS มากกว่า Rimworld เพราะ resolve
  เร็วกว่า เหมาะกับ AI ตัดสินใจ), stat system, วิชา/ตำรา, ไอเทม/อาวุธ/ยา

**หลักคิดกลางที่ใช้คุมทุกระบบ**: ทุกกลไกต้องเป็นสิ่งที่ AI ตัดสินใจได้จากข้อมูลที่ query
ผ่าน MCP ได้จริง ไม่พึ่ง "สัญชาตญาณมนุษย์" ที่ AI เข้าไม่ถึง

## ระบบเศรษฐกิจสำนัก

**สองสกุลเงิน**:
- **หินวิญญาณ (Spirit Stones)** — currency กลาง ใช้แลกเปลี่ยนไอเทม ได้จากภารกิจ/ผจญภัย/
  ปราบปีศาจ แจกเป็นเบี้ยเลี้ยงรายเดือนตามตำแหน่ง และ **ปล้น/แย่งชิงกันเองได้**
- **ค่าคุณูปการ (Contribution)** — ผูกกับสำนัก ใช้แลกของจากสำนัก (ตำรา/ยา/ไอเทม)
  ได้จากผลงาน: สายเก็บเกี่ยว (ปลูกผัก/เก็บสมุนไพร/ตัดไม้/ขุดแร่), สายฝีมือ
  (ครัว/หลอมยา/สร้างยุทโธปกรณ์/เขียนยันต์/สร้าง artifact — เกรดยิ่งสูงยิ่งได้เยอะ),
  สายต่อสู้ (ประลอง/สงครามสำนัก [WIP]/ภารกิจ)

**ทรัพยากรดิบ** (เสบียง/สมุนไพร/ไม้/เหล็ก) เป็นของส่วนรวม ศิษย์เบิกมาคราฟท์

**กติกาความเป็นเจ้าของผลผลิต**:
- ศิษย์ทั่วไป: ของที่คราฟท์ได้เข้าคลังสำนัก → ศิษย์อื่นแลกซื้อด้วยค่าคุณูปการ
- ผู้อาวุโสขึ้นไป: เก็บของที่คราฟท์เป็นของส่วนตัวได้

## การตัดสินใจด้าน data architecture

**ScriptableObject vs Protobuf** — ใช้ทั้งคู่ คนละหน้าที่ ไม่แทนกัน:
- **ScriptableObject**: ข้อมูลนิยามที่ author ใน Editor และไม่เปลี่ยนตอนรัน
  (RecipeDef, BuildingDef, ItemDef, EventDef)
- **Protobuf**: runtime state ที่เปลี่ยนตลอดและต้องส่งข้ามขอบเขต process
  (DiscipleState, SectEconomyState, InventoryEntry) — จำเป็นเพราะ (1) state ศิษย์
  เป็นร้อยคน+inventory ไม่เหมาะกับ SO ที่เป็น asset ไม่ใช่ instance data (2) MCP bridge
  ต้องส่ง state ข้าม process ไปหา Node/Python agent แบบ type-safe (3) save file
  ใหญ่ขึ้นเรื่อยๆ ตามจำนวนศิษย์

Runtime state จะ reference กลับไปหา Definition ผ่าน ID (string/int) ไม่ใช้ direct
object reference เพื่อให้ save/load และส่งผ่าน MCP ได้สะอาด

## งานที่ทำไปแล้ว (lab รอบแรก)

ไฟล์ที่ส่งมอบแล้ว:
- `economy.proto` — schema เบื้องต้น: `CurrencyWallet`, `InventoryItem` (มี
  `OwnerScope` enum), `DiscipleState`, `SectStockpile` (raw resources เป็น
  `map<string,int32>` เผื่อขยายชนิดทรัพยากรทีหลังโดยไม่ต้องแก้ schema),
  `SectEconomyState`
- `SectEconomyState.cs` — plain C# mirror ของ proto ไว้ใช้ก่อนต่อ protoc
  pipeline จริง (ย้ายไป `Google.Protobuf`/`protobuf-net` ทีหลังได้แบบ mechanical
  เพราะ field ตรงกัน)
- `MockSectData.cs` — mock data 3 ศิษย์ (ศิษย์นอก/ศิษย์ใน/ผู้อาวุโส) + คลังสำนัก
  ไว้ทดสอบ UI ก่อนมีระบบเกมจริง
- `sect_ui_wireframe.html` — wireframe คลังสำนัก + ตารางศิษย์ ใช้โทนสี
  หมึก-กระดาษเก่า-ตราประทับ (จงใจเลี่ยงพาเลตแบบ AI-cliché) แสดงผล
  `OwnerScope` ต่อยศเห็นชัดในตาราง (ศิษย์นอก/ใน = เข้าคลังสำนัก,
  ผู้อาวุโส = เก็บส่วนตัวได้)

### lab รอบสอง — VContainer + MessagePipe

- `GameMessages.cs` — pub/sub message DTOs (`DiscipleRecruitedMessage`,
  `WorldEventTriggeredMessage` ฯลฯ) แยกจาก `SectEconomyState`, มี
  `SectStateQuery`/`SectStateSnapshot` สำหรับ request-response
- `GameLifetimeScope.cs` — VContainer composition root: register MessagePipe
  in-memory bus + `MessagePipe.Interprocess` TCP transport (Unity เป็น host)
  + subsystem ทั้ง 4 เป็น entry point
- `TimeSystem.cs` — ตัวอย่าง subsystem ที่ publish ผ่าน bus แทนการผูก
  reference ตรง, มี `SectStateQueryHandler` ตอบ state snapshot ให้ bridge
- `McpBridgeProgram.cs` — external .NET console app: ต่อ Unity ผ่าน
  `MessagePipe.Interprocess` (TCP client) และ expose MCP tools ผ่าน stdio
  ด้วย official `ModelContextProtocol` C# SDK, แยก read tools
  (`SectQueryTools`) กับ write tools (`SectActionTools`) เป็นคนละคลาส
- แยก workspace จริงเป็น `UnityProject/`, `McpBridge/`, `Shared/` (ต้นทาง
  เดียว sync เข้าทั้งสองฝั่งด้วย `sync-shared.sh`) พร้อม README อธิบาย
  วิธีรันแต่ละส่วน

### lab รอบสาม — ไล่ debug จนรัน round trip ผ่านจริง

Bug จริงที่เจอระหว่าง build/run (เก็บไว้กันลืม เผื่อเจอซ้ำตอนเพิ่ม message
type ใหม่):

1. **`Duplicate 'Compile' items'` (NETSDK1022)** — SDK-style csproj
   auto-include `.cs` ทุกไฟล์ในโฟลเดอร์อยู่แล้ว ไม่ต้องมี
   `<Compile Include="Shared/*.cs">` ซ้ำ
2. **`AddMessagePipeTcpInterprocess` ไม่มีจริง** — API จริงคือ
   `services.AddMessagePipe()` (คืน `IMessagePipeBuilder`) แล้วเรียก
   `.AddTcpInterprocess(...)` ต่อจาก builder นั้น ไม่ใช่ extension method
   ลอยบน `IServiceCollection` ตรงๆ
3. **`RegisterTcpRemoteRequestHandler` ต้องเรียกทั้งสองฝั่ง** แม้ฝั่งที่
   `HostAsServer = true` (Unity) ก็ต้องเรียกด้วย ไม่ใช่แค่ฝั่ง caller —
   ยืนยันจากโค้ดจริงของ `Wanxiang.Guanxiangtai/src/Frontend/FrontendIpcServer.cs`
4. **`ValueTask<T>` vs `UniTask<T>`** — MessagePipe ฝั่ง VContainer/Unity ใช้
   `UniTask<T>` (จาก `Cysharp.Threading.Tasks`) ไม่ใช่ `ValueTask<T>`
   มาตรฐาน .NET ที่ฝั่ง `IServiceCollection` ใช้ได้ปกติ
5. **`economy.proto` ยังไม่ผ่าน protoc** — ใส่ `[MessagePackObject]`/`[Key]`
   ให้ `SectEconomyState.cs` แล้วเพิ่ม `ToByteArray()`/`FromByteArray()`
   เป็น serializer ชั่วคราวด้วย MessagePack (แพ็กเกจเดียวกับที่มีอยู่แล้ว)
   ปลด blocker นี้ไปพลางก่อน ค่อยเปลี่ยนเป็น protobuf จริงทีหลัง
6. **สั่งคำสั่งผิดหน้าต่าง/ผิดโฟลเดอร์** — พิมพ์คำสั่งใหม่ใส่ terminal ที่
   `dotnet run` ค้างอยู่ (มันตีความเป็น stdin ของ MCP protocol) และรัน
   `npx @modelcontextprotocol/inspector dotnet run --project McpBridge/...`
   จากข้างในโฟลเดอร์ `McpBridge/` เอง (path ซ้ำชั้น) — ไม่ใช่บั๊กโค้ด
   เป็นเรื่อง terminal ล้วนๆ

**ผลทดสอบ round trip เต็มวง (ยืนยันแล้ว 21 ส.ค. 2026)**: Unity (Play mode) ↔
MessagePipe.Interprocess TCP ↔ McpBridge ↔ MCP Inspector — เรียก
`get_sect_state` ซ้ำหลายรอบผ่าน MCP Inspector ได้ JSON ข้อมูล mock กลับมา
ถูกต้องทุกครั้ง (`IsError = False`, latency ~26-53ms) สถาปัตยกรรมทั้งชุด
ตั้งแต่ diagram แรกพิสูจน์แล้วว่าทำงานได้จริง

### lab รอบสี่ — ทาง write (bridge → Unity) ผ่าน ExecuteDecision

ปิด gap ที่ค้างไว้ตั้งแต่ lab รอบสอง (`ExecuteDecision` เคยเป็นแค่
`NotImplementedException`) และทดสอบทิศทางตรงข้ามกับที่ผ่านไปแล้ว
(`get_sect_state` คือ Unity → bridge, อันนี้คือ bridge → Unity):

- `GameMessages.cs` — เพิ่ม `ExecuteDecisionMessage` (`EventId`, `ChoiceId`)
  และ topic คีย์ `InterprocessTopics.ExecuteDecision`
- `GameLifetimeScope.cs` — register broker เพิ่มสำหรับ message ใหม่นี้
- `DecisionLogger.cs` (ใหม่) — subscriber ฝั่ง Unity, `Debug.Log` เมื่อ
  ได้รับ decision จาก bridge และเรียก `TimeSystem.SetPaused(false)` ต่อ
  (ปิด loop กับ auto-pause ที่ `RaiseWorldEvent` ตั้งไว้ตอนมี event ต้อง
  ตัดสินใจ)
- `McpBridgeProgram.cs` — `SectActionTools.ExecuteDecision` publish
  `ExecuteDecisionMessage` จริงแทน stub เดิม

ยังไม่ได้ทดสอบรันจริง (รอผลจากผู้ใช้) — คาดว่าเรียก `execute_decision`
ผ่าน MCP Inspector แล้วควรเห็น log `[DecisionLogger] Received decision
from bridge - ...` ขึ้นใน Unity Console ทันที

**ผลทดสอบ (ยืนยันแล้ว 22 ส.ค. 2026)**: เรียก `execute_decision` ผ่าน MCP
Inspector (`eventId=bandit_raid_001`, `choiceId=send_inner_disciples`) →
Unity Console ขึ้น log ตามที่คาด ครบ stack trace ยืนยัน chain การทำงาน
`TcpWorker.RunReceiveLoop → AsyncMessageBroker.Publish →
DecisionLogger.OnDecisionReceived → Debug.Log` ตรงตามที่ออกแบบไว้ทุก
ขั้นตอน — **ตอนนี้ทั้งทางอ่าน (`get_sect_state`) และทางเขียน
(`execute_decision`) ทำงานจริงครบทั้งสองทิศทางแล้ว**

### lab รอบห้า — WorldEventSystem ปิด loop เต็มวง

`await_next_world_event` เรียกได้แต่ค้างตลอดไป (ไม่มีอะไรเรียก
`RaiseWorldEvent`) และ `execute_decision` ส่งได้แต่ยังไม่มี event จริงให้
ตอบ — เพิ่ม `WorldEventSystem.cs` (ใหม่) เป็น placeholder ง่ายๆ ที่สุ่ม
trigger event จาก list คงที่ทุก 30 วินาที (ข้ามรอบถ้ายังรอ decision อยู่
เช็คผ่าน `TimeSystem.IsPaused` ที่เปิด public ใหม่)

การเปลี่ยนแปลงประกอบ:
- `TimeSystem.cs` — `RaiseWorldEvent` เปลี่ยนจาก `Task` เป็น `UniTask` ให้
  สอดคล้องกับส่วนอื่นของโปรเจกต์ (เรียก `.Forget()` แบบ fire-and-forget
  จาก `Tick()` ได้), เปิด `IsPaused` เป็น public, เพิ่ม `Debug.Log` ตอน
  raise event เพื่อความชัดเจนตอนดู Console
- `WorldEventSystem.cs` (ใหม่) — `ITickable`, inject `TimeSystem` ตรงๆ,
  สุ่ม event id จาก `bandit_raid_001` / `new_disciple_applicant` /
  `herb_garden_bloom` / `wandering_merchant`
- `GameLifetimeScope.cs` — register `WorldEventSystem` เป็น entry point
  เพิ่ม

ตอนนี้ loop เต็มรูปแบบควรทำงานได้: Unity สุ่ม event ทุก 30 วิ → auto-pause
→ `await_next_world_event` คืนค่าจริง (ไม่ค้างแล้ว) → เรียก
`execute_decision` → `DecisionLogger` unpause กลับ → รอบถัดไปเริ่มนับใหม่
— ยังไม่ได้ทดสอบรันจริง รอผลจากผู้ใช้

### lab รอบหก — เจอข้อจำกัดจริงของ library: bridge subscribe ผ่าน TCP ไม่ได้

ทดสอบ `await_next_world_event` แล้วเจอ `SocketException 10048 (Only one
usage of each socket address is normally permitted)` — ไล่อ่าน source
code จริงของ `MessagePipe.Interprocess` (`TcpWorker.cs`,
`TcpDistributedPublisherSubscriber.cs`) แล้วเจอ root cause ที่ยืนยันได้
100% ไม่ใช่การเดา:

```csharp
// TcpDistributedSubscriber<TKey,TMessage> constructor:
worker.StartReceiver();   // เรียกเสมอ ไม่เช็ค HostAsServer เลย

// StartReceiver() ข้างใน:
var s = server.Value;     // = SocketTcpServer.Listen(host, port)
```

**สรุปกฎที่ยืนยันแล้ว**: `IDistributedSubscriber<K,V>` ผ่าน TCP
interprocess ของ library นี้ **เปิด listening socket ของตัวเองเสมอ ไม่
สนใจค่า `HostAsServer`** — ฝั่งที่เป็น hub อยู่แล้ว (Unity,
`HostAsServer=true`) ปลอดภัยเพราะ `StartReceiver()` มี guard กันเรียกซ้ำ
(`Interlocked.Increment == 1`) จึงเป็นแค่ no-op รอบสอง แต่ฝั่งที่ไม่ใช่
hub (Bridge) เรียกครั้งแรกจะพยายาม bind port เดียวกับที่ Unity ยึดไว้
แล้ว **ชนกันเสมอ** — ส่วน `IDistributedPublisher` (`Publish()`) และ
`IRemoteRequestHandler` (request-response) ใช้แค่ `client.Value` →
`Connect()` เท่านั้น ไม่มีปัญหานี้ (ตรงกับที่ `get_sect_state` และ
`execute_decision` ทดสอบผ่านมาก่อนหน้า)

**บทเรียนสำหรับอนาคต**: ฝั่ง Bridge (client) ห้ามใช้
`IDistributedSubscriber` ผ่าน TCP interprocess ของ library นี้เด็ดขาด —
ต้องใช้ request-response แทนเสมอสำหรับทิศทาง "Unity → Bridge" ถ้าจะเพิ่ม
message ใหม่ที่ Bridge ต้องรอรับ (เช่นในอนาคตถ้าจะ subscribe
`DiscipleRecruitedMessage`/`SectResourceChangedMessage` จริงจัง ต้องแปลง
เป็น request-response ด้วยเช่นกัน ไม่ใช่ subscribe ตรงๆ)

**การแก้**: เปลี่ยน `await_next_world_event` จาก pub/sub เป็น
request-response ทั้งหมด (pattern เดียวกับ `get_sect_state` ที่พิสูจน์
แล้วว่าทำงานจริง):
- `GameMessages.cs` — เพิ่ม `AwaitWorldEventRequest`/`AwaitWorldEventResponse`,
  ลบ `WorldEventTriggeredMessage` ออกจาก interprocess broker (เหลือแค่
  in-memory publish ไว้เผื่อ UI ใน Unity เองในอนาคต)
- `TimeSystem.cs` — เปลี่ยน `_worldEventPublisher` จาก
  `IDistributedPublisher` เป็น `IPublisher` ธรรมดา (in-memory), เพิ่ม
  `UniTaskCompletionSource<AwaitWorldEventResponse>` ที่ reset ทุกครั้ง
  `RaiseWorldEvent` ถูกเรียก, เพิ่ม `WaitForNextWorldEventAsync()` และ
  `AwaitWorldEventHandler` (request-response handler ใหม่)
- `GameLifetimeScope.cs` — เอา broker ของ `WorldEventTriggeredMessage`
  ออก, เพิ่ม register RPC คู่ `AwaitWorldEventRequest`/`Response`
- `WorldEventSystem.cs` — `RaiseWorldEvent` ไม่ async แล้ว เรียกตรงๆ
  ไม่ต้อง `.Forget()`
- `McpBridgeProgram.cs` — `AwaitNextWorldEvent` ใช้
  `IRemoteRequestHandler<AwaitWorldEventRequest, AwaitWorldEventResponse>`
  แทน `IDistributedSubscriber`

ยังไม่ได้ทดสอบรันจริงหลังแก้ — รอผลจากผู้ใช้

**ผลทดสอบ (ยืนยันแล้ว 22 ส.ค. 2026)**: `await_next_world_event` คืนค่า
จริงแล้ว (เช่น `herb_garden_bloom`) ไม่ค้าง ไม่ error — **ตอนนี้ loop เต็ม
รูปแบบทำงานจริงครบวงแล้ว**: Unity สุ่ม event → `await_next_world_event`
คืนค่า (request-response) → `execute_decision` ส่งกลับ → `DecisionLogger`
unpause → รอบถัดไปเริ่มนับใหม่

**Gotcha ที่เจอระหว่างเทสต์ (ไม่เกี่ยวกับโค้ด)**: ปุ่ม **Pause ของ Unity
Editor เอง** (คนละตัวกับ `TimeSystem.IsPaused` ในโค้ด) ถ้าติดค้างไว้จะทำ
ให้ `Update`/`Tick` ทั้งหมดหยุดทำงาน รวมถึง `WorldEventSystem.Tick()` ด้วย
— เจอตอนรอบแรก `await_next_world_event` timeout ที่ 60 วิ (ค่า default
ของ MCP Inspector) เพราะ Editor pause ค้างอยู่ ไม่ใช่บั๊กโค้ด

**Gotcha ที่สอง (พฤติกรรมตามที่ออกแบบไว้ ไม่ใช่บั๊ก)**: หลัง
`await_next_world_event` ได้ event ที่ `requiresDecision: true` มา
`TimeSystem` จะ pause ค้างไว้ และ `WorldEventSystem.Tick()` เช็ค
`IsPaused` แล้ว skip ทุกเฟรม — **ต้องเรียก `execute_decision` ก่อนเสมอ**
ถึงจะ unpause ให้รอบถัดไปเริ่มนับเวลาใหม่ ถ้าเรียก
`await_next_world_event` ซ้ำโดยไม่มี `execute_decision` คั่น จะ timeout
ทุกครั้ง ยืนยันจาก log จริงหลายรอบ

**ปรับ**: ลด `IntervalSeconds` จาก 30 → 15 วิ ใน `WorldEventSystem.cs`
(ตามคำขอ ไม่กระทบ logic อื่น), เพิ่ม `RaiseInitialEvent()` ยิง event แรก
ทันทีตอน startup (ไม่ต้องรอ interval แรก) พร้อม cache ใน `TimeSystem`
(`_cachedPendingEvent`) ให้ `await_next_world_event` ตอบทันทีถ้ามี event
รออยู่แล้วตอน bridge เพิ่งต่อเข้ามา

### lab รอบเจ็ด — ExecuteDecision มีผลจริงต่อ state + ตัดสินใจเลิกใช้ protobuf

**ตัดสินใจ**: เลิกใช้ protobuf ไปเลย ใช้ MessagePack เป็นตัวจริงถาวร ไม่
กลับไปตั้ง protoc pipeline อีก — เหตุผลเดิมของ protobuf (ต้องส่งข้าม
process ไปหา bridge ที่อาจเป็นภาษาอื่น) หมดความจำเป็นไปแล้วตั้งแต่ตัดสินใจ
ให้ bridge เป็น .NET/C# ทั้งคู่ (lab รอบสอง) MessagePack พิสูจน์แล้วว่า
ทำงานจริงหลายรอบ ไม่ต้องมี toolchain แยก เปลี่ยนชื่อ field
`EconomyStateProtobuf` → `EconomyStateBytes` ให้ตรงกับความจริง (ใน
`GameMessages.cs`, `TimeSystem.cs`, `McpBridgeProgram.cs`)

**ปัญหาที่เจอก่อนแก้**: `SectStateProvider.BuildSectEconomyState()` เรียก
`MockSectData.Create()` ใหม่ทุกครั้งที่ query — แปลว่าต่อให้มี mutation
logic ที่ไหนก็ตาม จะไม่มีวันเห็นผล เพราะรอบถัดไปสร้าง state ใหม่จาก
scratch เสมอ

**การแก้**:
- `SectStateProvider.cs` — เปลี่ยนเป็นถือ **live state instance เดียว**
  (`private readonly SectEconomyState _state = MockSectData.Create();`
  สร้างครั้งเดียว) แทนที่จะสร้างใหม่ทุก query, เพิ่ม
  `ApplyDecisionConsequence(eventId, choiceId)` ปรับ
  `Stockpile.RawResources` จริงตาม event (ยังไม่แตะ disciple เพราะ
  DiscipleSystem ถูก defer ไว้ตามที่ตกลง) — กติกาตอนนี้เป็น placeholder
  ง่ายๆ ยังไม่ใช่ balance จริง รอทำพร้อม EventData ScriptableObject
- `TimeSystem.cs` — เพิ่ม `ApplyDecisionConsequence` เข้า
  `ISectStateProvider` interface
- `DecisionLogger.cs` — inject `ISectStateProvider` เพิ่ม เรียก
  `ApplyDecisionConsequence` ก่อน unpause

**วิธีทดสอบว่าเห็นผลจริง**: `get_sect_state` (baseline) →
`await_next_world_event` → `execute_decision` → `get_sect_state` อีกรอบ
ควรเห็นตัวเลขใน `Stockpile.RawResources` เปลี่ยนไปจริง — ยังไม่ได้ทดสอบ
รันจริง รอผลจากผู้ใช้

### lab รอบแปด — DiscipleSystem จริง: รับสมัคร + เก็บเกี่ยว passive

ขอบเขต (ยังไม่แตะ BuildingSystem/ตำแหน่งตามที่ตกลง — ศิษย์ใหม่ทั้งหมดเป็น
ศิษย์นอกก่อน):

- **รับสมัคร**: ผูกกับ event `new_disciple_applicant` ที่มีอยู่แล้ว แทนที่
  placeholder เดิม (หัก provisions 5) ด้วยของจริง — ถ้า `choiceId` มีคำว่า
  "accept" จะสร้างศิษย์นอกใหม่เพิ่มเข้า roster จริง (`SectStateProvider.
  RecruitOuterDisciple()`) มอบหมาย gathering task แบบ round-robin, publish
  `DiscipleRecruitedMessage`
- **เก็บเกี่ยว passive**: `SectStateProvider.TickGathering()` ให้ศิษย์ที่
  `CurrentTask` เป็น `gathering_*` ผลิตทรัพยากรเข้าคลังตาม tick จริง (มี
  fractional accumulator กันเสียเศษระหว่าง tick), เรียกจาก
  `DiscipleSystem.Tick()` (เปลี่ยนจาก stub ว่างเป็น tick loop จริง)

**Gotcha ที่เจอระหว่างเทสต์ (ไม่เกี่ยวกับโค้ด)**: ตัวเลขทรัพยากรเพิ่มแบบ
กระตุก (หยุดนิ่งหลายสิบวิ แล้วพุ่งทีเดียว) แทนที่จะไหลลื่น — สาเหตุคือ
**Unity Editor ไม่ได้เปิด `Run In Background`** (`Edit > Project Settings
> Player > Resolution and Presentation`) เวลาสลับไปเบราว์เซอร์ (MCP
Inspector) `Update()`/`Tick()` เลยหยุดทำงานจริงๆ ไม่ใช่แค่ช้าลง เปิด
setting นี้แล้วแก้ปัญหาได้ทันที ไม่ใช่บั๊กโค้ด — ยืนยันจากตัวเลขที่ตรงกับ
สูตร (0.2/วิ) เป๊ะในช่วงที่ tick ทำงานจริง

### lab รอบเก้า — ResourceCraftingSystem จริง: คราฟท์ตาม ownership rule

เบิกทรัพยากรดิบไปคราฟท์เป็นของสำเร็จตาม `CurrentTask` ที่มีอยู่แล้ว
(`refining_elixir` ของ Su Yan, `forging_artifact` ของ Elder Zhao) —
`ResourceCraftingSystem` เปลี่ยนจาก stub เป็น tick loop จริง เรียก
`SectStateProvider.TickCrafting()`

สูตรที่ใส่ไว้ (ตัวเลขสมมติ รอปรับ balance จริงทีหลัง):

| Task | ใช้ | ได้ | เวลา |
|---|---|---|---|
| `refining_elixir` | herb ×10 | `elixir_qi_gathering` เกรด 3 | 20 วิ |
| `forging_artifact` | ore ×15 + wood ×10 | `sword_azure_flame` เกรด 5 | 30 วิ |

**Implement ตาม ownership rule จากตอนออกแบบเศรษฐกิจ**: เช็ค
`disciple.Rank >= DiscipleRank.Elder` — ศิษย์ทั่วไป (Su Yan) → ของเข้า
`Stockpile.CraftedGoods` (merge stack ถ้ามี item เดิมเกรดเดียวกันอยู่แล้ว),
ผู้อาวุโสขึ้นไป (Elder Zhao) → เข้า `PersonalInventory` ของตัวเอง

**พฤติกรรมถ้าทรัพยากรไม่พอ**: progress ค้างที่ 100% รอเฉยๆ ไม่เสีย
progress ทิ้ง (`TryConsume` แบบ all-or-nothing) พอมีทรัพยากรพอ (จาก
gathering หรือ event) คราฟท์เสร็จทันทีโดยไม่ต้องเริ่มนับใหม่

ยังไม่ได้ทดสอบรันจริง — รอผลจากผู้ใช้

## แผนงานถัดไปที่ตกลงลำดับไว้แล้ว

1. ~~ResourceCraftingSystem~~ (เสร็จแล้ว — lab รอบเก้า)
2. ~~ร้านค้าค่าคุณูปการ~~ (เสร็จแล้ว — lab รอบสิบ, `purchase_item` MCP tool)
3. ~~EventData → Luban pipeline~~ (เสร็จแล้ว — lab รอบสิบเอ็ด/สิบสอง)

### lab รอบสิบ — ร้านค้าค่าคุณูปการ

เพิ่ม MCP tool ใหม่ `purchase_item` (`discipleId`, `itemDefId`, `grade`,
`quantity`) — ศิษย์ซื้อของจาก `Stockpile.CraftedGoods` ด้วยค่าคุณูปการ
ตัวเอง ใช้ pattern request-response (`PurchaseItemRequest`/`Response`)
เหมือน `get_sect_state` เพราะต้องเช็ค-หัก atomic (เงินพอไหม/ของเหลือไหม)

ราคา placeholder: `grade × 50` ค่าคุณูปการต่อชิ้น เช็คของในคลังก่อน →
เงินพอไหม → หักเงินจาก `Wallet.Contribution`, ลด quantity ใน
`CraftedGoods` (ลบ entry ถ้าเหลือ 0), ย้ายของเข้า `PersonalInventory`
ของศิษย์คนนั้น — ทดสอบผ่านแล้ว

ไฟล์ที่แก้/ใหม่: `GameMessages.cs`, `SectStateProvider.cs` (เพิ่ม
`TryPurchaseItem`), `PurchaseItemHandler.cs` (ใหม่), `TimeSystem.cs`
(เพิ่ม method เข้า interface), `GameLifetimeScope.cs` (register RPC ใหม่),
`McpBridgeProgram.cs` (tool ใหม่ใน `SectActionTools`)

### lab รอบสิบเอ็ด — UI พื้นฐานในเกม (UGUI)

เพิ่ม `SectHudView.cs` (MonoBehaviour, top bar HUD) แสดง stockpile
(herb/wood/ore/provisions พร้อม delta indicator สีเขียว/แดงแบบ reference
screenshot) และ personal wallet ของ "เจ้าสำนัก" (spirit stones/contribution)

**เพิ่ม disciple ใหม่**: `MockSectData.cs` ไม่มี entry สำหรับผู้เล่น
(rank `SectMaster`) มาก่อน — เพิ่มเข้าไปเป็นตัวแรกในลิสต์ (`d000`,
"You (Sect Master)") เพราะ original design ระบุว่าผู้เล่น = เจ้าสำนัก

Register ผ่าน `RegisterComponentInHierarchy` (คนละแบบกับ
`RegisterEntryPoint` — ใช้กับ component ที่มีอยู่ใน scene แล้ว) ต้องสร้าง
Canvas/TMP Text/ผูก reference เองใน Editor (สร้าง .unity scene object จาก
นอก Editor ไม่ได้)

### lab รอบสิบสอง — ย้าย EventData จาก ScriptableObject ไป Luban pipeline

เปลี่ยนจากคลิกสร้าง `.asset` ทีละไฟล์ เป็น **Excel → generate → runtime
data** ด้วย [Luban](https://github.com/focus-creative-games/luban)
(`focus-creative-games/luban_unity` สำหรับฝั่ง Unity)

**แก้ความเข้าใจผิดสำคัญ**: Luban **ไม่ได้** generate ScriptableObject
`.asset` จาก Excel — มันสร้าง C# class + runtime loader ที่อ่านข้อมูลจาก
json/binary ตอนเกมรัน (ยืนยันจากการดาวน์โหลด
`focus-creative-games/luban_examples` มาแกะโค้ดจริง ไม่ใช่เดาจาก docs)
นี่แก้ pain point "ขี้เกียจคลิกสร้าง Asset หลายที" ได้ดีกว่า ScriptableObject
เดิมด้วยซ้ำ เพราะไม่มีการคลิกสร้าง asset เลยแม้แต่ครั้งเดียว

**ตัดสินใจ**: ใช้ `cs-simple-json` (ไม่ใช่ protobuf แม้ Luban จะรองรับ)
สอดคล้องกับทิศทางเลิกใช้ protobuf ที่ตัดสินใจไว้ตั้งแต่ lab รอบเจ็ด — ข้อมูล
ยังน้อย (4-8 แถว) ประโยชน์ของ binary/protobuf ยังไม่เห็นผล แต่ json
debug ง่ายกว่า (เปิดดูตรงๆ ได้)

**สถาปัตยกรรม**: แยก 2 ตารางเรียบๆ (`TbEvent`, `TbEventChoice` join ด้วย
`eventId`) แทนที่จะ nest choices เป็น list-in-one-row ในแถวเดียว — ปลอดภัย
กว่าเพราะ syntax แบบ multi-row nested list ของ Luban ซับซ้อนและ verify
ยาก (ไม่มี .NET SDK ในเครื่องมือที่ใช้พัฒนาให้ลองรันจริง)

Bug จริงที่เจอ (เก็บไว้กันลืม):
1. **`<module name="event">` ชน C# reserved keyword** — `event` เป็น
   keyword สงวนของ .NET (ใช้ประกาศ event) พอ generate เป็น `cfg.event.X`
   compiler แตกยกไฟล์ (error กระจายเป็นลูกโซ่เพราะ parser หลุด track)
   แก้โดยเปลี่ยนชื่อ module เป็น `worldevent` แทน — **บทเรียน: ตั้งชื่อ
   module ใน Luban schema เลี่ยงคำสงวนของ C# เสมอ**
2. **namespace ของ SimpleJSON ที่แถมมากับ `luban_unity` คือ
   `Luban.SimpleJSON`** ไม่ใช่ `SimpleJSON` เฉยๆ ตามที่เดาไว้ตอนแรก (ไม่มี
   dotnet ให้รันดูโค้ด generate จริงในเครื่องมือพัฒนา ต้องให้ผู้ใช้เทสต์แก้เอง)
3. gen script รันแล้วดู "ค้าง" ตอนแรก (เห็นแค่ banner โผล่มาแล้วนิ่ง) — ที่
   จริงยังทำงานอยู่ แค่ cold start ของ C# scripting engine ข้างในช้า
   (รอนานขึ้นแล้วผ่าน ไม่ใช่บั๊ก)

Toolchain: `Tools/Luban/` (binary จริงจาก
`focus-creative-games/luban_examples`), `DataTables/luban.conf` +
`Defines/worldevent.xml` (schema) + `Data/event.xlsx`,
`event_choice.xlsx`, `gen.bat`/`gen.sh` — ต้องติดตั้ง `luban_unity`
package ผ่าน git URL ใน `manifest.json` เพิ่มด้วย (ให้ `SimpleJSON`/
runtime class ของ Luban)

ลบ `EventData.cs`/`EventPool.cs` (ScriptableObject เดิม) ออกแล้ว —
`WorldEventSystem.cs`/`GameLifetimeScope.cs` ผูกกับ `LubanEventPool.cs`
(plain C# class, ไม่ใช่ ScriptableObject) แทน

### lab รอบสิบสาม — Xianxia.UI.MVP Lite (EventPopup + ResourceHud)

Implement ตาม spec ที่ผู้ใช้ร่างมาเอง (`XIANXIA_UI_MVP_LITE_SPEC.md`,
อ้างอิงแนวคิดจาก CycloneGames.UIFramework) — MVP pattern แยก View
(MonoBehaviour) / Presenter (plain C#) / Service (orchestrator), panel
resolve ผ่าน VContainer ด้วย explicit enum→Type mapping ไม่ scan assembly,
Presenter เป็น Transient (instance ใหม่ทุกครั้งเปิด panel)

**บั๊กที่เจอใน spec ก่อนลงมือ (แก้ก่อน implement)**:
1. `EventPopupPresenter` ตาม spec เดิมจะ publish ผ่าน `IPublisher<ExecuteDecisionMessage>`
   (in-memory channel) แต่ `DecisionLogger` ฟังผ่าน
   `IDistributedSubscriber<string, ExecuteDecisionMessage>` (interprocess
   channel) — **คนละ graph กัน ไม่เชื่อมกัน** กดปุ่มใน popup จะไม่มีอะไร
   เกิดขึ้นเลยแบบเงียบๆ ไม่ error ด้วย — แก้โดยดึง logic ออกมาเป็น
   `DecisionExecutor.cs` (ใหม่) ให้ทั้ง `DecisionLogger` (ทาง bridge) และ
   `EventPopupPresenter` (ทาง UI) เรียกตรงๆ แทนการอ้อมผ่าน pub/sub
   (pub/sub ควรใช้แค่ข้าม process เท่านั้น ไม่ใช่ในโปรเซสเดียวกัน)
2. Spec แนะนำ `IRemoteRequestHandler` สำหรับ purchase item จาก UI —
   type นี้คือ proxy ฝั่ง client ที่ bridge เรียกเข้า Unity เท่านั้น ถ้า UI
   จะซื้อของต้องเรียก `ISectStateProvider.TryPurchaseItem(...)` ตรงๆ
   (ยังไม่ได้ implement purchase panel ใน pass นี้ แค่บันทึกไว้กันพลาด
   ตอนทำจริง)

**สิ่งที่ต้องแก้นอก `UI/` folder** (spec เขียนว่าไม่ให้แก้ แต่จำเป็นจริง):
- `GameMessages.cs` — เพิ่ม `Description`/`Choices` ใน
  `WorldEventTriggeredMessage` (spec เองก็ระบุไว้ว่าจำเป็น)
- `TimeSystem.cs` — 1 บรรทัดที่ publish message ต้องส่ง field ใหม่ด้วย
- `SectStateProvider.cs` — เพิ่ม publish `SectResourceChangedMessage`
  จริง (นิยามไว้นานแล้วแต่ไม่เคยถูก publish เลยสักครั้ง) ผ่าน
  `AdjustAndNotify()` (wrapper รอบ `Adjust()` เดิม, single choke point)
  — publish delta ที่เกิดขึ้นจริงหลัง clamp ไม่ใช่ delta ที่ขอ (กันกรณี
  ขอ -50 แต่ของเหลือแค่ 20)

**ตัดสินใจ**: `ResourceHud` panel ใหม่ **แทนที่ `SectHudView` เดิมไปเลย**
(ลบไฟล์ทิ้ง) — ย้าย delta indicator logic (สีเขียว/แดง) เข้า
`ResourceHudView`/`ResourceHudPresenter` ตาม MVP pattern แทน polling
`ISectStateProvider` ทุก 0.5 วิแบบเดิม เปลี่ยนเป็น subscribe
`SectResourceChangedMessage` ที่มี delta คำนวณมาให้พร้อมในตัว message
เลย (ไม่ต้องเก็บ previous value ฝั่ง client อีกต่อไป) — wallet
(spirit stones/contribution) ยังไม่มี dedicated message เลย piggyback
refresh ไปกับทุกครั้งที่ resource เปลี่ยน (ยอมรับ trade-off เพราะยังไม่
คุ้มจะเพิ่ม polling timer แยกสำหรับ presenter ที่ตั้งใจให้เป็น plain C#
ไม่มี tick ของตัวเอง)

**ผลทดสอบ (ยืนยันแล้ว 30 ส.ค. 2026)**: `ResourceHud` โผล่ทำงานถูกต้อง
ตั้งแต่รอบแรก ส่วน `EventPopup` รอบแรกไม่โผล่ (แก้ไม่ยาก — ใส่ try-catch
ใน `WorldEventUISystem` แล้วเจอว่าเป็นแค่ปัญหา prefab/catalog setup ไม่ใช่
บั๊กโค้ด) หลังแก้แล้วทดสอบเต็ม loop ผ่าน: `WorldEventSystem` ยิง event
อัตโนมัติ → `EventPopup` เปิดเอง → กด choice → `DecisionExecutor` →
`SectStateProvider` (เห็น log "Recruited outer disciple: Jiang Yu")
**พิสูจน์ว่าบั๊กจุดที่ 1 (channel ผิด) แก้ถูกจริง ไม่ใช่แค่ทฤษฎี**

**gotcha ที่เจอเพิ่ม (ผู้ใช้ diagnose เอง)**: UI ทั้งหมดไปโผล่กลางจอแทนที่
จะอยู่ตำแหน่งจริง (top bar / center popup) สาเหตุคือ 2 อย่างรวมกัน:
(1) `UIRoot` GameObject เองไม่ได้ stretch เต็ม Canvas ตั้งแต่แรก
(2) `ContentSizeFitter` ตั้งเป็น `PreferredSize` ซึ่งคำนวณขนาดใหม่ทุกเฟรม
ทับค่าที่ anchor stretch ตั้งไว้ — **นี่คือ root cause ตัวจริง** แก้โดย
เปลี่ยนเป็น `Unconstrained` แทน, เพิ่ม `UIRoot.Awake()` บังคับ stretch
ตัวเองเต็ม Canvas + แก้ layout ของทุก child ที่มีอยู่แล้ว, และเพิ่ม
`UIRoot.ApplyLayout()` (static) ให้ `UIService.Open()` เรียกทุกครั้งหลัง
`Instantiate` panel ใหม่ ครอบคลุมทั้ง panel ที่ bake ไว้ใน scene ตั้งแต่ต้น
และ panel ที่เปิดตอน runtime

## ยังไม่ได้ตัดสินใจ / รอคุยต่อ

- Core loop แบบ final (รอ finalize gameplay systems ก่อน)
- ระบบต่อสู้ (ACS-style vs Rimworld-style)
- Stat system, วิชา/ตำรา, ไอเทม/อาวุธ/เกราะ/ยา แบบละเอียด
- สงครามสำนัก (WIP)
- กติกาการปล้นหินวิญญาณระหว่างศิษย์ ยังไม่ลง schema
- `BuildingSystem` ยังเป็น stub ว่างเปล่า — deferred ต่อจนกว่าจะถึงคิว
  (ตำแหน่ง/สิทธิ์ที่ปลดล็อกจากอาคารยังไม่มีผลอะไรตอนนี้)
- `WorldEventSystem` ยังสุ่มแบบ weighted เฉยๆ ไม่เช็คเงื่อนไข sect state
  ก่อน trigger (เช่นทรัพยากรน้อยเกินไปควรเกิด event ต่างจากทรัพยากรเยอะ)
- ราคาร้านค้า (`grade × 50`) และ consequence ของแต่ละ event ยังเป็น
  placeholder รอ balance จริง
- `economy.proto`/protobuf เลิกใช้แล้วถาวรทั้งโปรเจกต์ (ทั้ง runtime state
  และตอนนี้รวม Luban ด้วย) ใช้ MessagePack/JSON แทนทุกจุด
- `DiscipleListPresenter`/panel ยังไม่ implement (enum
  `UIPresenterKind.DiscipleList` มีไว้เผื่ออนาคต, `ResolvePresenterType`
  throw `NotImplementedException` ถ้าเรียกตอนนี้)
- ยังไม่มี wallet-changed message โดยเฉพาะ — `ResourceHudPresenter`
  refresh wallet แบบ piggyback บน resource-changed event เป็น workaround
  ชั่วคราว
- `UIRoot.ApplyLayout()` ผูก layout กับชื่อ prefab ตรงๆ (string matching)
  — ใช้ได้กับ 2 panel ตอนนี้ แต่ถ้า panel เยอะขึ้นควรย้ายไปเป็น method
  บน view เอง (`IUIView.ApplyDefaultLayout()`) แทน ยังไม่จำเป็นต้องแก้
  ตอนนี้
