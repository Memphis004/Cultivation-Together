# Cultivation Together — Project Summary / Design Log

> ไฟล์นี้เป็น design log ระดับโปรเจกต์ ที่ `build-context.sh` บังคับแนบเข้า context pack
> และ `LLMWiki/wiki/sources/*.md` อ้างเป็น source
>
> **สถานะ:** reconstruct 2026-10-11 จาก (ก) อ่าน code จริงใน repo และ (ข) ประวัติใน
> `LLMWiki/wiki/log.md` — ทุกข้อที่เขียนว่า "ยืนยันจาก code" ตรวจจากไฟล์จริงแล้ว
> ส่วน phase history ยกมาจาก wiki log ตามที่บันทึกไว้ ถ้าขัดกับ code ให้ **code ชนะ**
> แล้วแก้ไฟล์นี้

---

## 1. ภาพรวม (What this is)

เกม **修仙 / จัดการสำนัก** (xianxia sect management) ที่ **AI GM เล่นแทน/เล่นร่วมกับผู้เล่น**
ผ่าน MCP — Unity เป็นเจ้าของ gameplay state ทั้งหมด และมี process ภายนอก (`McpBridge`)
ต่อเข้ามาอ่าน state / สั่ง decision ผ่าน TCP

วงจรหลักระดับ MVP:

```
[Sect State] → [World Event Trigger] → [Choices] → [Decision] →
[Consequences] → [loop]
```

- World event ยิงเป็นคาบ (weighted-random จาก Luban table, ยังไม่มี state-based condition)
- decision event จะ auto-pause เกม (`PauseReason.PendingDecision`) จนกว่าจะตัดสิน
- 1 decision = 1 occurrence (P13A) — event id เดียวกัน pending ซ้อนกันได้ ต้องอ้าง `InstanceId`
- **AI GM ไม่มี bypass:** สวมบท `SECT_MASTER` และติด rule Hybrid protection เดียวกับคนเล่น

Core loop สุดท้ายยัง **จงใจไม่ฟิก** (ดู `LLMWiki/wiki/sources/open-questions.md`)

---

## 2. Tech stack (ยืนยันจาก code)

| ส่วน | เทคโนโลยี | หลักฐาน |
|---|---|---|
| Engine | Unity 6 (URP 17.3.0, 2D) | `UnityProject/Packages/manifest.json`, `UserSettings/Layouts/default-6000.dwlt` |
| ภาษา | **C# 8 เท่านั้น** — ห้าม `record` / `init` / `global using` / target-typed `new` | `Common-Rules.md` §3, wiki `conventions.md` |
| DI | VContainer `jp.hadashikick.vcontainer` 1.19.0 (pin git URL) | `manifest.json` |
| Messaging | MessagePipe + MessagePipe.VContainer + MessagePipe.Interprocess (git) | `manifest.json`, `Assets/Plugins/MessagePipe.Interprocess/` |
| Async | UniTask | `manifest.json` |
| Serialization | **MessagePack** (protobuf ถูกยกเลิกใน lab 7) | `Assets/Scripts/Shared/*.cs` `[Key(N)]` |
| UI | UGUI + TextMeshPro (ไม่ใช้ UI Toolkit) | `Assets/Scripts/UI/`, `Assets/Resources/Fonts/THSarabunPSK SDF.asset` |
| Data pipeline | **Luban** (`com.code-philosophy.luban`) | `DataTables/`, `luban.conf` |
| NuGet ใน Unity | NuGetForUnity (`com.github-glitchenzo.nugetforunity`) | `manifest.json` |
| Bridge | .NET 8 console app + `ModelContextProtocol` 2.2.0 | `McpBridge/McpBridge.csproj` |
| Tests | Unity Test Framework 1.6.0 (EditMode) | `Assets/Tests/EditMode/*.asmdef` |
| Visual (Tier-2) | Spine-Unity runtime (OSS exception, license ยืนยัน 2026-09-25) | `Assets/Spine/`, wiki `conventions.md` |
| Visual (Tier-1) | Sprite-sheet / layered sprite | `Assets/Scripts/Visual/` |

---

## 3. Workspace layout (monorepo)

```
Cultivation Together/                ← repo root
├── Shared/                          ← ★ canonical source ของ DTO/state — แก้ที่นี่เท่านั้น
│   ├── GameMessages.cs              ←   sync ไป 2 consumer
│   ├── SectEconomyState.cs
│   ├── SectSessionSnapshot.cs
│   ├── ViewerMembership.cs
│   ├── DiscipleAttributes.cs
│   ├── MessageTypes.cs
│   └── MockSectData.cs
├── UnityProject/                    ← เปิด folder นี้ใน Unity Hub
│   └── Assets/Scripts/              ← logic ทั้งหมด (asmdef: Xianxia.Sect.Runtime)
├── McpBridge/                       ← .NET 8 console app (MCP server, stdio)
├── DataTables/                      ← Luban: Data/*.csv + Defines/schema.xml + luban.conf
├── Tools/Luban/                     ← Luban binary (vendored — อย่าลบ)
├── LLMWiki/                         ← wiki (อ่าน wiki/index.md ก่อน)
├── scripts/                         ← dev helper scripts (python)
├── sync-shared.sh                   ← คัดลอก Shared/*.cs เข้า 2 consumer
└── build-context.sh                 ← สร้าง .ai/context-pack.md
```

**Shared มี 3 สำเนา:** `Shared/` (canonical) → `UnityProject/Assets/Scripts/Shared/` และ
`McpBridge/Shared/` ผ่าน `./sync-shared.sh` — **ห้ามแก้สำเนาตรง ๆ** ถ้าไม่ sync จะ drift

---

## 4. Architecture

```
┌─ Unity (Host) ────────────────────────────────────────────────┐
│  GameLifetimeScope (VContainer root)                           │
│    └─ Installers: Gameplay / UI / Building / Visual / Interprocess
│  MessagePipe bus  ── in-memory pub/sub                        │
│                   └─ TCP interprocess  127.0.0.1:3215         │
│  Subsystems: TimeSystem, DiscipleSystem, ResourceCraftingSystem,
│              WorldEventSystem, DecisionLogger, WorldEventUISystem,
│              ViewerMembershipPersistenceSystem, UIBootstrap …  │
│  ISectStateProvider (single live state) = SectStateProvider    │
│  Data: LubanEventPool, AvatarPartPool, BuildingDefs            │
│  UI: UIRoot + UIPanelCatalog + UIService (MVP-Lite)            │
└───────────────────────────┬───────────────────────────────────┘
                            │ TCP
┌─ McpBridge (.NET 8, client) ──────────────────────────────────┐
│  SectQueryTools  (read):  get_sect_state, await_next_world_event,
│                           get_ownership_log, get_task_change_log,
│                           get_task_protection
│  SectActionTools (write): execute_decision, purchase_item, assign_task
│  stdio → ModelContextProtocol SDK
└───────────────────────────┬───────────────────────────────────┘
                            │ stdio
                    AI GM client
```

**Transport rule (สำคัญ):** `IDistributedSubscriber` บน TCP transport นี้เรียก
`worker.StartReceiver()` (bind listen socket เอง) **ไม่สนใจ `HostAsServer`** → process
ที่ไม่ใช่ hub (bridge) ใช้ subscriber ไม่ได้เพราะ port ชนกัน
สิ่งที่ bridge ต้องการ *คำตอบ* ให้ใช้ **request-response** (`IRemoteRequestHandler`)
สิ่งที่เป็นการแจ้งเตือน UI ให้ใช้ **pub/sub**

**In-process pub/sub ห้ามใช้แทนการเรียกข้าม process:** `EventPopupPresenter` เคย publish
`ExecuteDecisionMessage` in-memory แต่ `DecisionLogger` listen อยู่บน interprocess channel
→ คลิกแล้วเงียบ แก้โดยดึง `DecisionExecutor` ออกมาให้ทั้งสองทางเรียกตัวเดียวกัน

**Installer layering:** registration ใหม่ต้องอยู่ installer ที่ถูกชั้น (`Core/Installers/*`)
หรือ `SectSceneLifetimeScope` ถ้าผูกฉาก — **ห้ามยัดกลับ `GameLifetimeScope.Configure()`**

---

## 5. สถานะ implementation (ณ 2026-10-11)

### 5.1 แกน gameplay

| ระบบ | สถานะ |
|---|---|
| `TimeSystem` — simulation clock, `SimulationDelta`, `Pause/Resume(reason)`, `PendingDecision` | ✅ ใช้จริง + tested |
| `SectStateProvider` — state aggregator, `AdjustAndNotify()` สำหรับ resource | ✅ ใช้จริง |
| `DiscipleSystem` — roster, attribute (stamina / skill xp), auto-scheduler | ✅ ใช้จริง |
| `ResourceCraftingSystem` — gathering / crafting tick (speed-aware, เก็บเศษ) | ✅ ใช้จริง |
| `WorldEventSystem` + `LubanEventPool` — weighted-random spawner, start grace 10s sim-time | ✅ ใช้จริง |
| `DecisionExecutor` / `DecisionLogger` | ✅ ใช้จริง |
| Pending-decision **instances** (P13A) — ซ้อนได้, `InstanceId`, bounded queue | ✅ ใช้จริง |
| Building system (grid, placement, ghost, camera rig) | ✅ ใช้จริง |
| Hybrid permissions (P5B) — `CheckTaskPermission`, cooldown 12s, owner protection 600s | ✅ ใช้จริง |
| Viewer membership (P5A/P5B) — `ViewerRecord`, registry, invariants | ✅ ใช้จริง |
| Save / Load (P11) — slot, backup, versioned envelope, atomic write | ✅ ใช้จริง |
| Session lifecycle — Title ↔ Playing, scene load, stale-completion invalidation | ✅ ใช้จริง |
| Combat / stat / technique system | ❌ ยังไม่ตัดสิน (open questions #2–#4) |

### 5.2 UI (Xianxia.UI.MVP Lite)

`UIService` + `UIPanelCatalog` (ScriptableObject) + `UIRoot`; presenter resolve ผ่าน
enum→Type mapping (**ไม่มี reflection**) — panel ที่ลงทะเบียน: `EventPopup`, `ResourceHud`,
`LogWindow`, `AvatarCustomization`, `DiscipleDetail`, `BottomMenu`, `ResourcePopup`,
`BuildingMenu`, `TaskAssignment`, `DiscipleList`, `TimeControl`, `EventChip`, `TitleMenu`,
`IngameMenu`

**Panel ใหม่ต้องเดินตาม generator pattern:** Editor generator → regenerate prefab →
`UIPresenterKind` **ต่อท้ายเท่านั้น** → catalog entry → ฟอนต์ไทย TMP + palette กลาง —
**ห้าม hand-edit prefab YAML**

**WorldEvent UI:** popup ดึงข้อมูลจาก authoritative state (ไม่พึ่ง message อย่างเดียว)
จึงไม่พังเวลา start/subscribe สลับลำดับ; มี "chip" ลอยเมื่อ popup ถูกซ่อนแต่ยัง pending;
`Hide`/Escape ซ่อนโดย**ไม่**ล้าง pending และ**ไม่** resume เวลา

### 5.3 Data pipeline (Luban)

```
DataTables/Data/EventDef.csv ─────┐
DataTables/Data/EventChoiceDef.csv ┤  DataTables/gen.sh | gen.bat
DataTables/Defines/schema.xml ────┤  (luban.conf: topModule "cfg", target "client")
                                  ↓
        UnityProject/Assets/Scripts/Data/Gen/  (namespace cfg.game; Tables.cs)
                                  ↓
        UnityProject/Assets/Resources/DataTables/*.json  (runtime load)
```

**ห้ามแก้ไฟล์ใน `Data/Gen/`** — regenerate จาก CSV แทน
ตารางปัจจุบัน: `TbEventDef` (id, description, requiresDecision, weight) และ
`TbEventChoiceDef` (eventId, choiceId, label) — สอง flat table join ด้วย `eventId`

---

## 6. Phase history (จาก `LLMWiki/wiki/log.md`)

| Phase | สาระ |
|---|---|
| lab 1–13 (21–30 Aug 2026) | ตั้ง DI graph, MessagePipe bus + TCP, subsystems, UI MVP-Lite; **lab 7** ยกเลิก protobuf → MessagePack; **lab 13** เจอ bug in-process pub/sub ไม่ถึง interprocess → ดึง `DecisionExecutor` |
| E1-pre | main-thread hop สำหรับ handler ที่มาจาก TCP (`MainThreadDispatch`) — handler ต้อง `await UniTask.SwitchToMainThread()` ก่อนแตะ Unity API |
| E2-lite | world-event spawner + `WorldEventRuntimeConfig.StartGraceSeconds` (ไม่ยิงจาก constructor อีก เพราะ Start order ไม่การันตี) |
| P3 | Task assignment UI (row dropdown, draft, conflict detection) |
| P4 | Ownership: `DiscipleOwnerType/OwnerId`, `TrySetDiscipleOwner` + identity convention, `DiscipleOwnerChangedMessage`, local dev harness + `get_ownership_log` (read-only) |
| P5A | Viewer membership data + invariants + injectable `IClock` (UTC เท่านั้น) |
| P5B | Hybrid permissions (`CheckTaskPermission`, 600s protection, 12s cooldown), bridge read-only queries (`get_task_change_log`, `get_task_protection`), membership persistence (local file) |
| P10/P11 | Save/load: slot + backup + versioned envelope, autosave, `TitleMenu`/`IngameMenu`, session coordinator |
| P12A/B | `CloseAllExcept` / `TrySetVisible` — session-end panel cleanup vs persistent HUD |
| P13A | Pending-decision **instances**: id เดียวกันซ้อนได้, `InstanceId` ใน decision, save/restore ทุก occurrence, popup re-target |
| P13B (2026-10-11) | แก้ crash ตอน restore: presenter อ่าน member ของ view ที่ถูก destroy แล้ว (`MissingReferenceException`) — panel handle ตรวจ Unity lifetime แทน |

### Fix ล่าสุด (2026-10-11)

`WorldEventUISystem` / `UIService` อ่าน `handle.View.GameObject` เพื่อเช็คว่า panel ยังอยู่
แต่ property นั้นเป็น member บน MonoBehaviour ที่ถูก `Destroy` ไปแล้ว → `MissingReferenceException`
ตอน session boundary (destroy chip → restore pending event → ต้องเปิด popup กลับ)
แก้โดยเช็ค Unity lifetime ที่ panel handle (view เป็น null / destroyed → ถือว่าไม่มี panel)
แล้วเปิดใหม่ — regression test: `EventPopupLiteTests.SessionBoundary_DestroyingTheChip_StillGivesAPendingEventItsPopupBack`

**EditMode suite: 618/618 ผ่าน** (2026-10-11)

---

## 7. กฎที่ต้องยึด (สรุปจาก `Common-Rules.md` + `conventions.md`)

1. **C# 8 เท่านั้น** — ห้าม `record`, `init`, `global using`, target-typed `new`
2. **MessagePack `[Key(N)]` append-only** — ห้ามแก้เลขเดิม; enum value ใหม่ต่อท้ายเสมอ
3. **Resource mutation ต้องผ่าน `AdjustAndNotify()`** ใน `SectStateProvider` เท่านั้น (ไม่งั้น HUD ไม่อัปเดต)
4. **เวลา:** gameplay progression ใช้ `TimeSystem.SimulationDelta` เท่านั้น — ห้ามอ่าน
   `Time.deltaTime` ใน gameplay code, ห้าม set `Time.timeScale`
   pause ใช้ `Pause/Resume(reason)` + `ResumeByPlayer` (ห้าม `SetPaused` ในโค้ดใหม่)
   real-time clock (UTC cooldown, viewer inactivity) แยกจาก sim time — **ห้ามคูณ speed ซ้ำ**
5. **Threading:** callback จาก TCP/MessagePipe.Interprocess มาบน background thread —
   ต้อง `await UniTask.SwitchToMainThread()` ก่อน mutate state / publish / แตะ UI
6. **request-response ต้อง register ทั้งคู่:** `RegisterTcpRemoteRequestHandler` + `RegisterAsyncRequestHandler`
7. **ห้ามใช้ `FindObjectOfType`/reflection หา service** — ใช้ DI constructor injection
8. **ห้ามฟื้น outfit / `TryApplyOutfit`** (decision ใน `wiki/sources/avatar-appearance.md` §1)
9. **ห้ามแก้ `Shared/`** นอกจากงานที่ระบุชัด แล้วรัน `./sync-shared.sh`
10. **ห้าม commit `McpBridge/bin` หรือ `obj`** (และ `UnityProject/{Logs,Temp,obj,UserSettings}` + generated `.csproj`)
11. **เปลี่ยน DI registration หรือ constructor ของ registered type** ต้องพิสูจน์ด้วยการ build
    container จริง (EditMode test หรือ Play Mode boot smoke test) — ห้ามมี public constructor
    ซ้อนบน registered type (VContainer เลือกตัวที่ param เยอะสุด) ให้ pin ผ่าน factory ที่ composition root
12. **UI ที่พึ่ง game state** ต้อง derive จาก authoritative state (`TimeSystem`, `SectStateProvider`)
    ไม่ใช่จาก message อย่างเดียว เพื่อให้ subscription/start order หาข้อมูลไม่เจอไม่ได้

---

## 8. Known issues / open items

- **Core loop, combat (ACS-style vs Rimworld-style), stat system, technique/manual, item detail
  ยังไม่ตัดสิน** → `LLMWiki/wiki/sources/open-questions.md`
- **Portrait/art pipeline** ยังต้อง generate ผ่าน ComfyUI + chroma-key แล้ว bake (ดู skills
  `chroma-key-portrait-pipeline`, `portrait-edit-prompt`)
- `Visual.Spikes/Editor/Phase4VerifyRunner.cs` มี assertion ที่ stale กับ start state ปัจจุบัน
  (d001 = `meditation` ไม่ใช่ `gathering_herb`) — ไม่ได้อยู่ใน EditMode suite
- Bridge tool `change_avatar_part` ยังไม่ผูกครบฝั่ง bridge (Unity-side request handler มีแล้ว)
- Wiki บางหน้าเขียน scene pattern เป็น "CoreScene + GameplayScene" — **ของจริงใน build
  settings ตอนนี้คือ `SectScene`** (ดู `SceneNamesTests`); ให้ code ชนะ

---

## 9. วิธีรัน / verify

```bash
# sync shared contracts (ทำทุกครั้งที่แก้ Shared/)
./sync-shared.sh

# data pipeline (แก้ CSV แล้ว regenerate)
cd DataTables && ./gen.sh        # หรือ gen.bat บน Windows

# MCP bridge
cd McpBridge && dotnet build && dotnet run     # Unity ต้องรันก่อน (host TCP 3215)

# Unity: เปิด UnityProject/ ใน Unity Hub → resolve packages → Play
# EditMode tests: รันผ่าน Test Runner หรือ Unity MCP tool `run_tests`

# context pack สำหรับ AI
./build-context.sh               # → .ai/context-pack.md
```

ลำดับ boot: **Unity ก่อน** (เป็น TCP server) แล้วค่อยรัน bridge (client)
