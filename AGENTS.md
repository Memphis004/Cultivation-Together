# AGENTS.md — instructions for AI coding agents

> Entry point สำหรับ AI agent ทุกตัวที่ทำงานใน repo นี้
> `build-context.sh` แนบไฟล์นี้เข้า context pack เสมอ — ถ้าเพิ่มกฎใหม่ ให้เพิ่มที่นี่
>
> เอกสารประกอบ: `project_summary.md` (design log), `Common-Rules.md` (กฎ phase),
> `LLMWiki/wiki/conventions.md` (workflow), `LLMWiki/wiki/index.md` (สารบัญ wiki)

---

## 0. ก่อนเริ่มทำงาน

1. **อ่าน code จริงก่อนแก้** — ตัวอย่างในแชต, roadmap และ wiki เป็น *design intent*
   ไม่ใช่หลักฐานว่ามี implementation อยู่แล้ว ให้ reuse ของเดิม ห้ามสร้างซ้ำ
2. **code คือ source of truth** — ถ้า wiki/design doc ขัดกับ code ให้ code ชนะ
   แล้วแจ้งว่า doc ล้าสมัย (อย่าแก้ code เงียบ ๆ ให้ตรง doc)
3. **ทำทีละ phase** — เจอ dependency ที่ไม่คาดคิด ให้รายงานก่อนขยาย scope
   ห้าม implement phase ถัดไปเอง
4. **ห้ามเดา API** — VContainer / MessagePipe / UniTask / Luban ใช้ pattern ที่มีใน repo เท่านั้น
5. ถ้าข้อมูลใน context ไม่พอ ให้ตอบ `need_files` block แล้วหยุด (ดู §7)

---

## 1. Repo facts ที่ต้องรู้

| เรื่อง | ค่า |
|---|---|
| Repo | `Memphis004/Cultivation-Together` |
| Unity project | `UnityProject/` (Unity 6, URP 2D) — เปิด folder นี้ใน Unity Hub |
| Runtime asmdef | `Xianxia.Sect.Runtime` (ไฟล์เดียว คลุม `Assets/Scripts/**`) |
| Test asmdef | `Xianxia.Sect.Tests.EditMode` |
| Bridge | `McpBridge/` — .NET 8 console app (`net8.0`), MCP server ผ่าน stdio |
| Transport | Unity = TCP **server** `127.0.0.1:3215`, bridge = client |
| ภาษา | **C# 8** เท่านั้น |
| Serialization | MessagePack (`[Key(N)]`) |
| Data | Luban (`DataTables/`) → `Assets/Scripts/Data/Gen/` → `Resources/DataTables/*.json` |
| Scenes | `Assets/Scenes/SectScene.unity` คือ gameplay scene ที่อยู่ใน build settings |
| จำนวน test ปัจจุบัน | EditMode 618 tests (2026-10-11) |

**Boot order:** Unity ก่อน แล้วค่อยรัน bridge

---

## 2. กฎเหล็ก (violate แล้วพัง)

### 2.1 ภาษาและ schema
- C# 8 — **ห้าม** `record`, `init`, `global using`, target-typed `new`
- MessagePack `[Key(N)]` และ enum value **append-only** — ห้ามแก้เลข/ค่าที่มีอยู่
  key ใหม่ให้ดูจาก code ปัจจุบันแล้วต่อท้าย

### 2.2 Shared contracts (3 สำเนา)
- canonical = `Shared/*.cs` — **แก้ที่นี่เท่านั้น**
- แล้วรัน `./sync-shared.sh` (คัดลอกเข้า `UnityProject/Assets/Scripts/Shared/`
  และ `McpBridge/Shared/`)
- **ห้ามแก้สำเนาใน `UnityProject/` หรือ `McpBridge/` ตรง ๆ** — จะถูกทับตอน sync

### 2.3 Gameplay state
- Unity เป็นเจ้าของ live state ทั้งหมด — mutation บน main thread เท่านั้น
- background thread ทำงานได้เฉพาะกับ **detached data** (เช่น file I/O)
- **ห้าม enumerate live mutable state จาก worker thread**
- resource ทุกตัวแก้ผ่าน **`AdjustAndNotify()`** ใน `SectStateProvider` เท่านั้น

### 2.4 เวลา
- gameplay progression ใช้ **`TimeSystem.SimulationDelta`** เท่านั้น
- **ห้าม** อ่าน `Time.deltaTime` ใน gameplay code
- **ห้าม** set `Time.timeScale`
- pause มี reason ชัดเจน (`User`, `PendingDecision`) — ใช้ `Pause/Resume(reason)` และ
  `ResumeByPlayer`; **ห้ามเรียก `SetPaused` ในโค้ดใหม่**
- real-time clock (UTC cooldown, viewer inactivity 600s) แยกจาก sim time
- **ห้ามคูณ game speed ซ้ำ**

### 2.5 Threading / IPC
- handler ที่มาจาก TCP (`MessagePipe.Interprocess`) รันบน **background thread**
  → `await UniTask.SwitchToMainThread()` ก่อน mutate state / publish in-memory / แตะ UI
- **ห้ามใช้ in-process pub/sub แทนการเรียกข้าม process** — ถ้า UI ต้องทำสิ่งเดียวกับ bridge
  ให้เรียก shared entry point ตรง ๆ (ตัวอย่าง: `DecisionExecutor`)
- **ห้าม register message ฝั่ง visual/building/task บน interprocess broker** — เป็น in-memory เท่านั้น
- request-response ทุกคู่ต้อง register **ทั้ง** `RegisterTcpRemoteRequestHandler`
  **และ** `RegisterAsyncRequestHandler` (ดู `Core/Installers/InterprocessInstaller.cs`)
- **bridge ใช้ `IDistributedSubscriber` ไม่ได้** — TCP transport นี้ `StartReceiver()`
  (bind listen socket) ทุกกรณี ไม่สนใจ `HostAsServer` → port ชนกับ hub
  use **request-response** แทน (connect out เท่านั้น)

### 2.6 DI
- registration ใหม่อยู่ installer ที่ถูกชั้น: `Assets/Scripts/Core/Installers/*`
  (`Gameplay` / `UI` / `Building` / `Visual` / `Interprocess`)
- ผูกกับฉาก → `Scenes/SectScene/SectSceneLifetimeScope.cs`
- **ห้ามยัดกลับ `GameLifetimeScope.Configure()`**
- **ห้าม reflection / `FindObjectOfType` หา service** — constructor injection เท่านั้น
  (UI panel resolve ผ่าน enum→Type mapping)
- แก้ DI registration หรือ constructor ของ registered type → **ต้องพิสูจน์ด้วยการ build
  container จริง** (EditMode test หรือ Play Mode boot smoke) — ห้ามมี public constructor
  ซ้อนบน registered type (VContainer เลือกตัวที่ param เยอะสุด) ให้ pin ผ่าน factory

### 2.7 UI
- panel ใหม่เดินตาม **generator pattern**: Editor generator → regenerate prefab →
  `UIPresenterKind` **ต่อท้ายเท่านั้น** → catalog entry → ฟอนต์ไทย TMP + palette กลาง
- **ห้าม hand-edit prefab YAML**
- UI ที่พึ่ง game state ต้อง derive จาก **authoritative state** (`TimeSystem`,
  `SectStateProvider`) ไม่ใช่จาก message อย่างเดียว — subscription/start order
  ห้ามทำให้ข้อมูลหาย
- dispose subscription และ cancel async ของ session เก่าให้ถูกต้อง

### 2.8 อย่าแตะ
- **อย่าฟื้น outfit / `TryApplyOutfit`** — decision อยู่ใน
  `LLMWiki/wiki/sources/avatar-appearance.md` §1
- **อย่าแก้ไฟล์ใน `Assets/Scripts/Data/Gen/`** — regenerate จาก `DataTables/` แทน
- **อย่าแก้ `Tools/Luban/*.dll`** — vendored binary
- Spyke/Spine example assets ของ Esoteric ห้ามตกค้างใน build จริง

---

## 3. Generated / build output — ห้าม commit

`.gitignore` กันไว้แล้ว แต่ถ้าเจอ tracked อยู่ให้ `git rm --cached`:

```
UnityProject/Logs/          UnityProject/Temp/          UnityProject/obj/
UnityProject/UserSettings/  UnityProject/Library/       UnityProject/Builds/
UnityProject/*.csproj       UnityProject/*.sln
McpBridge/bin/              McpBridge/obj/
```

`McpBridge/McpBridge.csproj` **เป็นไฟล์จริง ต้อง commit**

---

## 4. MCP tools ที่ bridge เปิดอยู่

`McpBridge/Program.cs` — read/write แยก type ชัดเจน

**`SectQueryTools` (read-only เท่านั้น)**
| tool | อ่านอะไร |
|---|---|
| `get_sect_state` | disciples, wallets, stockpile |
| `await_next_world_event` | block จนกว่าจะมี event ที่ต้องตัดสิน (คืน `InstanceId` ด้วย) |
| `get_ownership_log` | log การเปลี่ยนเจ้าของ (ไม่ log rejection/no-op) |
| `get_task_change_log` | log การเปลี่ยน task (ไม่ log rejection/no-op) |
| `get_task_protection` | viewer protection state ต่อ disciple |

**`SectActionTools` (write)**
| tool | ทำอะไร |
|---|---|
| `execute_decision` | ส่ง decision (ระบุ `instanceId` เมื่อ event id เดียวกัน pending ซ้อนกัน) |
| `purchase_item` | ให้ disciple ซื้อของจาก stockpile |
| `assign_task` | assign task — ส่งเป็น `SECT_MASTER` เสมอ (AI GM ไม่มี viewer bypass) |

**ห้ามเพิ่ม tool ฝั่ง write ที่แตะ ownership** — มี guard test สแกน `Program.cs`
ห้ามมี `TrySetDiscipleOwner`; ownership assignment เป็น local dev-harness operation เท่านั้น

---

## 5. Verify ก่อนบอกว่าเสร็จ

```bash
# contracts
./sync-shared.sh                       # แล้วเช็คว่า 3 สำเนาตรงกัน

# data
cd DataTables && ./gen.sh

# bridge
cd McpBridge && dotnet build           # ต้อง 0 error

# Unity: EditMode tests ทั้งหมด (ผ่าน Test Runner หรือ Unity MCP run_tests)
```

- **รัน test จริงแล้วรายงานผลจริง** — ห้ามอ้างว่า Unity compile/test ผ่านถ้าไม่ได้รัน
- แก้ test ให้ผ่านไม่ได้ แปลว่ายังไม่เสร็จ — **ห้าม weaken assertion / skip / swallow error**
  เพื่อให้ผ่าน ถ้าจำเป็นจริง ๆ ต้องอธิบายเหตุผลและ verify behavior นั้น
- เพิ่ม regression test ที่ **fail กับโค้ดก่อนแก้** (พิสูจน์ว่า test มี discriminating power)
- ถ้า filter output ผ่าน pipeline ให้เก็บ exit status ของคำสั่งที่ทดสอบไว้ (เช่น `set -o pipefail`)

---

## 6. Report format ตอนปิดงาน

1. ไฟล์ที่เปลี่ยน
2. behavior ที่เปลี่ยน
3. command/test ที่รัน + ผลจริง
4. ขั้นตอนที่คนต้อง verify เอง
5. ยังค้างอะไร / ข้อจำกัด
6. อัปเดต `LLMWiki/wiki/index.md` + `LLMWiki/wiki/log.md`
   (entry ใหม่ขึ้นต้นด้วย `## [YYYY-MM-DD]` — `build-context.sh` ดึงเฉพาะบรรทัดนี้)
7. รัน `./build-context.sh`
8. **แล้วหยุด** — อย่าเริ่ม phase ถัดไปเอง

---

## 7. ถ้าข้อมูลไม่พอ

ตอบเป็น JSON block นี้แล้วหยุด อย่าเดา:

```need_files
{
  "paths": ["path/to/file.cs"],
  "reason": "เหตุผลที่ต้องอ่านไฟล์นี้"
}
```

เวลา อธิบาย code ให้อ้าง path + line range จาก context pack เช่น
`Shared/GameMessages.cs:45-60`
