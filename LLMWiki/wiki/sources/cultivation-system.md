---
title: Cultivation System (P14) — Revised Design
type: design
status: implemented — P14.0 (P14A model + accrual / P14B breakthrough + UI) + P14B follow-up (`attempt_breakthrough` MCP tool) ship แล้ว 2026-10-11
sources:
  - ../../project_summary.md
  - UnityProject/Assets/Scripts/Shared/DiscipleAttributes.cs (CultivationConfig + [Key(2)]/[Key(3)])
  - UnityProject/Assets/Scripts/Systems/SectStateProvider.cs
  - UnityProject/Assets/Scripts/Core/TimeSystem.cs (ISectStateProvider)
related:
  - "[[sources/open-questions]]"
  - "[[concepts/state-management]]"
  - "[[concepts/time-system]]"
  - "[[entities/disciples]]"
  - "[[sources/task-system-v2]]"
created: 2026-10-11
updated: 2026-10-11
confidence: high
tags: [design, cultivation, p14, implemented, breakthrough]
---

# Cultivation System (P14) — Revised Design

> เอกสารนี้คือดีไซน์ **P14 (สายบำเพ็ญ)** ฉบับปรับปรุงให้ตรงกับของที่ ship จริง
> (P14A + P14B, 2026-10-11) — ไม่ใช่แผนก่อนลงมือ
>
> ตามกฎ repo: **code คือ source of truth** ถ้าเอกสารนี้ขัดกับ code ให้ code ชนะ แล้วแจ้งว่า
> หน้านี้ล้าสมัย (ดู `LLMWiki/wiki/AGENTS.md` — sources/ เป็นเอกสารที่คนดูแล,
> concepts/ เป็นหน้าที่ agent ดูแล)
>
> ⚠️ **ตัวเลขทุกตัวในระบบนี้เป็น prototype ไม่ใช่ balance data** — มีอยู่เพื่อให้ rule
> และ test ใช้แหล่งความจริงเดียวกันเท่านั้น

## 0. ทำไมต้องมีหน้านี้

P14 ถูกส่งมอบเป็น 2 phase โดยไม่มีเอกสาร design บนดิสก์เลย (P14A/P14B อยู่ในแค่ prompt
ของแต่ละรอบ) — หน้านี้คือ design ก้อนนี้ที่เรียกกันว่า **P14.0** ซึ่ง consolidate
ทั้งสอง phase ไว้ด้วยกัน เพื่อให้:
คนอ่านทีหลังเห็น **กติกา** ไม่ใช่แค่ diff; เห็นชัดว่าอะไรจงใจไม่ทำ (backlog);
และเห็นว่า point ไหนถูก "แก้จากดีไซน์ตอนลงมือ" เพราะอะไร

## 1. ขอบเขตที่ส่งมอบ

| Phase | ได้อะไร |
|---|---|
| **P14A** — model + accrual | ตาราง stage แบบ static ใน Shared, `[Key(2)] CultivationProgress` + `[Key(3)] CultivationStageId`, การเพิ่ม progress ตอนนั่งสมาธิ, normalize/restore validation, snapshot ผ่าน roster เดิม |
| **P14B** — breakthrough + UI | `CanBreakthrough` / `TryBreakthrough` (validated, atomic), `DiscipleCultivationChangedMessage` (Unity-only), แถวบำเพ็ญ + ปุ่ม "ทะลวงขั้น" บนแท็บ Info, log line, tests |

**ไม่ทำ (โดยเจตนา) ใน P14:** RPC/`interprocess` ใด ๆ, MCP tool, การแก้ `Shared/` ใน P14B,
RNG, ค่าใช้จ่าย, cooldown, โอกาสล้มเหลว, โบนัส stat/combat, การเลื่อนขั้น (rank promotion),
viewer chat command, และ AI ทะลวงขั้นเอง — ดู §10

> **อัปเดต 2026-10-11 (P14B follow-up):** ข้อ "ไม่ทำ RPC/MCP tool" ถูกยกเลิกตามมติใหม่ —
> เพิ่ม RPC pair + MCP tool `attempt_breakthrough` **ตัวเดียว** โดยส่ง `SECT_MASTER` และใช้
> permission authority ตัวเดิม (`EvaluateTaskPermission`) ไม่มี policy ที่สอง ดู §13

## 2. Stage model (P14A)

ตาราง stage เป็น **static C# table** — ไม่ใช่ enum และไม่ผ่าน Luban (ไม่มี data file ใหม่):

`Shared/DiscipleAttributes.cs:159-165`

| # | id (เก็บใน save) | ชื่อที่แสดง (UI เท่านั้น) | Requirement (วินาทีจำลอง) |
|---|---|---|---|
| 0 | `qi_condensation` | คัดลมปราณ | 300 |
| 1 | `foundation_establishment` | ก่อรากฐาน | 900 |
| 2 | `core_formation` | ก่อแก่นทอง | 2700 |
| 3 | `nascent_soul` | ก่อจิตวิญญาณ | 0 = **ขั้นสุดท้าย** |

กติกาของตาราง (ล็อกด้วยเทสต์):
- **Ordered + append-only** — เพิ่มขั้นใหม่ได้แค่ **ต่อท้าย** ห้าม insert/สลับลำดับ
  เพื่อให้ id ที่ save ไว้ยังมีความหมายเดิม
- id เป็น **string ธรรมดา ไม่ใช่ enum** — append stage แล้วไม่ต้อง renumber ไม่ต้อง migrate save
- `Requirement` = progress ที่ต้องสะสมเพื่อ **ออกจาก** ขั้นนั้น (ขั้นสุดท้าย = 0)
- helper ทั้งชุดใน `CultivationConfig`: `IsKnownStage` / `IsFinal` / `Requirement` /
  `NextStageId` / `DisplayName` / `IsReady` / `Describe`
- `Describe()` (`Shared/DiscipleAttributes.cs:230-241`) ถูก log ตอน boot เพื่อให้ตาราง
  "มองเห็นได้" ไม่ถูกซ่อนใน code

**แยกบทบาทให้ชัด (จุดที่คนอ่านมักสับสน):**

- `CultivationConfig.IsReady(progress, stageId)` (`:220-228`) = **predicate ระดับ model ล้วน ๆ**
  (stage รู้จัก + ไม่ใช่ขั้นสุดท้าย + `progress >= requirement` แบบ strict) — ไม่รู้จัก permission
  ไม่มี epsilon; ใช้ในเทสต์/ตรรกะบริสุทธิ์
- `ISectStateProvider.CanBreakthrough` = **authoritative query** ของเกม (เพิ่ม permission
  + epsilon เข้ามา) — UI และ test ต้องยึดตัวนี้ ไม่ใช่ `IsReady` (ดู §5)

## 3. Progress accrual (P14A)

- อัตรา: `CultivationConfig.ProgressPerSecondResting = 1.0f` ต่อ **วินาทีจำลอง**
  (`Shared/DiscipleAttributes.cs:153`) → ขั้นแรก 300 วิจำลอง ≈ 100 วิจริงที่ความเร็ว 3×
- เพิ่มใน **tick เดียวกับ** stamina/XP ของ P10B และใช้ delta ก้อนเดียวกัน
  (`SectStateProvider.cs:530-543`): นับเฉพาะ `DiscipleWorkOutcome.Resting`
  (นั่งสมาธิ) — งานอื่นสะสม 0
- **Clamp AT requirement ไม่มี carry-over** (`AddCultivationProgress`, `:589-604`) —
  overflow ไม่เกิด และ progress ส่วนเกินไม่ไหลไปขั้นถัดไป
- ขั้นสุดท้ายสะสมไม่ได้, stage id ที่ไม่รู้จักสะสมไม่ได้ (ปล่อยให้ restore validation
  เป็นคนปฏิเสธ)
- **Pause = delta ศูนย์** เพราะใช้ `TimeSystem.SimulationDelta` เท่านั้น — ไม่มีการอ่าน
  `Time.deltaTime` และไม่มีการคูณความเร็วเกมซ้ำ (Common-Rules 6/11)
- **ไม่ publish ต่อ tick** — มีข้อความเฉพาะตอนทะลวงขั้นสำเร็จเท่านั้น (ไม่งั้น log/UI ท่วม)

## 4. Persistence + validation (P14A)

- `DiscipleAttributes` ถูกผูกกับ `DiscipleState` ที่ **`[Key(12)]`** (append-only) และเพิ่ม
  key ใหม่ต่อท้าย:
  - `[Key(2)] float CultivationProgress`
  - `[Key(3)] string CultivationStageId` (initializer = `CultivationConfig.FirstStageId`)
- **save/load ไม่ต้องเพิ่ม field ใหม่** — cultivation อยู่ใน attribute block ซึ่งอยู่ใน roster
  ที่ snapshot พาไปอยู่แล้ว (`Shared/SectSessionSnapshot.cs` แก้แค่ doc comment ให้ตรง)
- payload เก่าที่ไม่มี key → ได้ขั้นแรก progress 0 (มีเทสต์ legacy payload)
- `Normalize` (`Shared/DiscipleAttributes.cs:376-388`): stage id ว่าง/null → ขั้นแรก;
  progress non-finite/ติดลบ → 0; แล้ว clamp เป็น `0..Requirement(stage)`
  (ขั้นสุดท้าย/unknown → เหลือ 0)
- **restore validation แบบ fail-closed** (`SectStateProvider.cs:1901-1907`): stage id
  **ที่ไม่ว่างแต่ไม่รู้จัก** = corrupt → ปฏิเสธการ restore; progress non-finite = ปฏิเสธ
  (ไม่ซ่อมเงียบ ๆ — ของที่ save แล้วผิดต้องไม่ถูกกลืน)
- ศิษย์ทุกทางเข้าได้ stage ที่ถูกต้อง: founder ใน mock start และตอน recruit
  (`DiscipleAttributesTests.RecruitOuterDisciple_ProducesValidAttributes`)

## 5. Breakthrough operation (P14B)

API บน `ISectStateProvider` (ประกาศท้าย `Core/TimeSystem.cs:944` — เป็นที่อยู่เดิมของ
interface นี้ ไม่ได้ย้ายไปไฟล์ใหม่):

```csharp
bool CanBreakthrough(string requesterId, string discipleId, out string reason);   // read-only
bool TryBreakthrough(string requesterId, string discipleId, out string failReason); // mutate
```

- `CanBreakthrough` = เรียก `EvaluateBreakthrough` ตัวเดียวกับที่ `TryBreakthrough`
  revalidate → **UI กับ commit ไม่มีทางไม่ตรงกัน** และไม่มี policy ที่สอง
- ลำดับ validation (`SectStateProvider.cs:1432-1503`) — **fail closed ก่อนแตะ state
  เสมอ** และทุกข้อคืน reason ที่อ่านรู้เรื่อง:
  1. requester id ไม่ว่าง (trim ก่อน)
  2. disciple id ไม่ว่าง และ **มีศิษย์ตัวนั้นจริง**
  3. attribute block มี และ **stage id ต้องรู้จัก** (ไม่ว่าง+ไม่รู้จัก = corruption)
  4. **PERMISSION** — ใช้ `EvaluateTaskPermission` ตัวเดียวกับ task assignment
     (`:1191`) ⇒ Hybrid rule ครบ: `SECT_MASTER` override ศิษย์ที่มี viewer เป็นเจ้าของ
     ได้เฉพาะเมื่อเจ้าของ inactive **มากกว่า 600 วินาทีจริง (UTC)**; viewer สั่งได้เฉพาะ
     ศิษย์ที่ตัวเองมี active membership; membership ขัดแย้ง/หาย = consistency error
     แม้เป็น master — **ไม่มี AI-GM bypass**
  5. stage ต้อง **ไม่ใช่ขั้นสุดท้าย**
  6. `progress >= requirement` — ยอมรับหย่อนได้ไม่เกิน `CultivationReadyEpsilon = 1e-4`
     (`:1362`, ≈ 0.1 ms ของการนั่งสมาธิที่อัตรา prototype) เพื่อกัน float สะสมแล้วค้าง
     ต่ำกว่า requirement ค้างหนึ่งหน่วยสุดท้าย
- **สำเร็จ = atomic mutation ก้อนเดียว**: `CultivationStageId = NextStageId(...)` แล้ว
  `CultivationProgress = 0f` (`:1399-1400`) — ไม่มี partial write, ไม่มี RNG/ค่าใช้จ่าย/
  cooldown/โอกาสล้มเหลว, `DiscipleRank` ไม่ถูกแตะ
- **Viewer activity**: คำสั่งที่ถูกต้องของเจ้าของตัวจริง refresh activity ของตัวเอง
  เหมือนการเปลี่ยน task; คำสั่งของ `SECT_MASTER` **ไม่** refresh (§Common-Rules 7)
- เทสต์ล็อก: ปฏิเสธ = state ไม่เปลี่ยน + ไม่ publish; กดซ้ำ = มีผลครั้งเดียว;
  rank/stamina/skill/task คงเดิม; `CanBreakthrough` ตรงกับ `TryBreakthrough` ทุก state
  และเป็น read-only

## 6. Message (P14B)

`UnityProject/Assets/Scripts/Core/CultivationMessages.cs` — **Unity-only, in-memory**:

```csharp
[MessagePackObject] public class DiscipleCultivationChangedMessage {
    [Key(0)] string DiscipleId; [Key(1)] string OldStageId; [Key(2)] string NewStageId; }
```

- **ไม่อยู่ใน `Shared/`** (เหมือน `SceneMessages.cs` / `SessionMessages.cs`) เพราะ
  `sync-shared.sh` copy `Shared/*.cs` ทั้งก้อน → แก้ที่นั่นทำให้ 3 สำเนาสกปรกพร้อมกัน
  และ bridge ไม่มีประโยชน์จากข้อความนี้
- **ไม่ register บน interprocess broker** — publish จาก provider เฉพาะเมื่อเปลี่ยนจริง;
  bridge อ่าน stage ที่เกิดขึ้นผ่าน `get_sect_state` เหมือน field อื่น
- DI: resolve ผ่าน `RegisterMessagePipe` (open generic) ที่ composition root อยู่แล้ว —
  **ไม่ต้องลงทะเบียน broker ต่อ type** (Common-Rules 16 ยืนยันด้วยเทสต์ที่ build
  container จริงและ resolve presenter)

## 7. UI (P14B)

สร้างด้วย **Editor generator** (`UnityProject/Assets/Editor/DiscipleDetailPanelGenerator.cs`)
→ regenerate prefab (`Assets/Prefabs/UI/DiscipleDetailPanel.prefab`) — **ไม่ hand-edit YAML**
(Common-Rules 14)  ทำที่ **แท็บ Info** เท่านั้น; แท็บ Status (radar) ไม่ถูกแตะ

สิ่งที่แถวบำเพ็ญแสดง (`UI/Views/DiscipleDetailView.cs`):

| องค์ประกอบ | ที่มา |
|---|---|
| ชื่อขั้น (`ขั้น · คัดลมปราณ`) | `CultivationConfig.DisplayName` |
| progress bar + `progress / requirement` | ค่าจริงจาก attribute |
| highlight "พร้อมทะลวงขั้น" | จาก `CanBreakthrough` |
| `ขั้นสุดท้าย` | `CultivationConfig.IsFinal` |
| ปุ่ม **ทะลวงขั้น** | `interactable` = `CanBreakthrough`; reason โชว์ inline |

- View **ไม่คิดกติกาเอง** — `SetCultivation(...)` / `SetBreakthroughState(ready, reason)`
  รับผลที่คำนวณมาแล้วเท่านั้น
- ผู้เรียกคือ `InfoTabPresenter` (`UI/Presenters/DiscipleDetailTabPresenters.cs`) ด้วย
  requester = `SectStateProvider.SectMasterRequesterId` (`"SECT_MASTER"`) — ไม่มี viewer bypass
- คลิกปุ่ม = **revalidate แล้ว commit** ผ่าน `ISectStateProvider.TryBreakthrough`; ถ้าปฏิเสธ
  แสดง `failReason` inline (ไม่มี modal) และ state ไม่เปลี่ยน
- refresh สองทาง: (1) throttled attribute tick ของ panel เดิม (≤4×/s, unscaled) และ
  (2) `DiscipleCultivationChangedMessage` — และเพราะ panel derive จาก authoritative state
  ทุก tick ข้อความที่หาย/สลับลำดับจึงทำให้ข้อมูลหายไม่ได้ (Common-Rules 13)
- log window ได้ narrative 1 บรรทัด (`UI/Presenters/LogWindowPresenter.cs`) — story beat
  ไม่ใช่ traffic ต่อ tick

## 8. AI

Auto scheduler **ไม่เรียก `TryBreakthrough`** และ scoring ไม่ถูกแก้เลย (มีเทสต์
`TryBreakthrough_IsNotCalledByTheAutoScheduler`) — การทะลวงขั้นไม่เคยเป็น autonomous loop

**P14B follow-up:** AI GM เรียกได้แล้วผ่าน MCP tool `attempt_breakthrough` (§13) — ตั้งใจให้เป็น
write path ที่ **มีคน/นโยบายคุม** (ส่ง `SECT_MASTER`, Hybrid 10 นาทีใช้เต็ม) ไม่ใช่ AI ทะลวง
เองแบบอิสระ และมี guard test ในฝั่ง bridge ตามแนวเดียวกับ write tool อื่น

## 9. ตามที่ออกแบบ vs ตามที่สร้างจริง (revision log)

| ประเด็น | ดีไซน์ตอนตั้ง phase | ที่สร้างจริง | เหตุผล |
|---|---|---|---|
| เกณฑ์พร้อม | "progress >= requirement (epsilon เล็ก)" | ยอมรับได้ถึง `requirement − 1e-4` (`progress < requirement - epsilon` = ปฏิเสธ) | tolerance ต้องอยู่ "ต่ำกว่า" requirement ไม่ใช่ "สูงกว่า" เพื่อกัน float ค้างต่ำกว่าหนึ่งหน่วยสุดท้าย; 0.1 ms เล็กพอที่ความเพี้ยนไม่มีนัยสำคัญ |
| ที่อยู่ interface | คาดว่าจะเพิ่มเมธอดใหม่ที่ไฟล์ provider | `ISectStateProvider` ประกาศอยู่ท้าย `Core/TimeSystem.cs` อยู่แล้ว → เพิ่มที่เดิม | ไม่ย้าย interface ไปไฟล์ใหม่ (จะกระทบ context pack + เอกสาร) |
| Snapshot | คาดว่าอาจต้องเพิ่ม field | ไม่ต้อง — cultivation อยู่ใน attributes ที่ roster มีอยู่แล้ว (แก้แค่ comment) | หลีกเลี่ยง field ซ้ำ |
| ข้อความ | "inject publisher เหมือนตัวอื่น" | subscriber ฝั่ง UI เป็น **optional param = null** (ทั้ง `LogWindowPresenter` และ `DiscipleDetailPresenter`) | constructor site เดิมยัง compile ได้; production DI resolve จริง |
| ตัวเลข | 300/900/2700 วิ, 1.0/s | เหมือนเดิม | ยังเป็น prototype รอ balance (§0) |
| UI ข้อความ | ไทย + palette กลาง | ป้าย `ทะลวงขั้น` / `พร้อมทะลวงขั้น` / `ขั้นสุดท้าย` | ตาม Common-Rules 14; TMP dynamic font ได้ glyph เพิ่มตอน regenerate |
| `Shared/` | P14B ห้ามแตะ | P14B ไม่แตะ; **P14B follow-up แก้ `Shared/GameMessages.cs`** (เพิ่ม RPC pair) | ช่วง P14B ทุกอย่างอยู่ใน Unity พอเพิ่ม tool จำเป็นต้องมี DTO บน wire → แก้ที่ canonical แล้ว `sync-shared.sh` ให้ทั้ง 3 สำเนา |
| breakthrough จาก bridge | backlog "ไม่ทำ" (§10 เดิม) | เพิ่ม write pair + tool `attempt_breakthrough` (handler hop main thread เหมือน `AssignTaskHandler`) | มติใหม่: ให้ AI GM ทะลวงได้ แต่ด้วยอำนาจเดียวกับคน + ไม่เพิ่ม read pair (`get_sect_state` มี stage/progress แล้ว) |

## 10. Backlog (ตั้งใจไม่ทำใน P14 — ต้องมีมติก่อน)

- **`attempt_breakthrough` MCP tool** — ✅ **ทำแล้วใน P14B follow-up (§13)** โดยใช้กติกาเดิม:
  (ก) อำนาจ = `SECT_MASTER` เท่านั้น ไม่มี bypass, (ข) ไม่เพิ่ม read pair เพราะ `get_sect_state`
  อ่าน stage/progress ได้อยู่แล้ว, (ค) มี guard test แนวเดียวกับ write tool ที่แตะ ownership
  (ห้ามมี requester ที่ caller เลือกเอง / ห้ามมี policy ที่สองใน handler)
- **viewer chat command** (เช่น `!breakthrough`) — รอ flow viewer membership จริง
- เอฟเฟกต์/ราคา/RNG/cooldown ของการทะลวงขั้น — จงใจให้เป็น deterministic ไม่มีค่าใช้จ่ายใน P14
- ตัวเลข balance จริงของ requirement/อัตราสะสม — prototype ทั้งหมด

## 11. Verification (ของจริงที่รัน 2026-10-11)

- `Assets/Tests/EditMode/CultivationProgressTests.cs` — **30 tests** (P14A): accrual จาก
  simulation delta (paused = 0, 1×/3× เท่ากันเมื่อจำลองเวลาเท่ากัน), clamp ไม่มี carry-over,
  stage สุดท้าย/unknown ไม่สะสม, normalize + restore validation, legacy payload,
  snapshot detached, rank/การสะสมเป็นอิสระต่อกัน
- `Assets/Tests/EditMode/CultivationBreakthroughTests.cs` — **32 tests** (P14B):
  สำเร็จเลื่อนขั้นเดียว + progress 0, not-ready/final/unknown-disciple/unknown-stage/empty-id/
  non-finite ถูกปฏิเสธ, viewer อื่นถูกปฏิเสธ, `SECT_MASTER` ถูกบล็อกตอนเจ้าของ active และ
  ผ่านหลัง 10+ นาที (fake clock), ปฏิเสธแล้วไม่เปลี่ยน/ไม่ publish, กดซ้ำมีผลครั้งเดียว,
  rank/stamina/xp/task คงเดิม, `CanBreakthrough` ตรงกับ `TryBreakthrough`, message
  Unity-only/ไม่อยู่บน broker, container จริง resolve ได้, prefab ที่ generator สร้างมี
  แถว+ปุ่มจริง, `Program.cs` ไม่มี breakthrough path
- EditMode ทั้งชุด: **680/680 ผ่าน** (618 ก่อน P14 + 30 + 32)
- `cd McpBridge && dotnet build` → 0 warning / 0 error; สำเนา `Shared/` 3 ฝั่งตรงกัน
- **Manual (Play Mode) ยังต้องมีคนรัน:** New Game → ศิษย์นั่งสมาธิ → 3× จนขั้นแรกพร้อม
  (~100 วิจริง) → กด `ทะลวงขั้น` → ขั้นถัดไป progress 0 → pause ยืนยันว่าไม่เพิ่ม →
  Save → Return to Title → Continue → stage ยังอยู่

## 12. ไฟล์ที่เกี่ยวข้อง

| ไฟล์ | บทบาท |
|---|---|
| `Shared/DiscipleAttributes.cs:102-241` | `CultivationStageDef` + `CultivationConfig` (ตาราง stage + helpers) |
| `Shared/DiscipleAttributes.cs:271-279` | `[Key(2)]`/`[Key(3)]` ของ cultivation |
| `Shared/SectSessionSnapshot.cs` | doc comment ระบุว่า roster พา attributes (รวม cultivation) ไปด้วย |
| `UnityProject/Assets/Scripts/Core/TimeSystem.cs:944` | `ISectStateProvider.CanBreakthrough` / `TryBreakthrough` |
| `UnityProject/Assets/Scripts/Systems/SectStateProvider.cs:530-543, 589-604` | accrual (resting only, clamp) |
| `UnityProject/Assets/Scripts/Systems/SectStateProvider.cs:1362-1503` | epsilon + `CanBreakthrough` + `TryBreakthrough` + `EvaluateBreakthrough` |
| `UnityProject/Assets/Scripts/Systems/SectStateProvider.cs:1191` | `EvaluateTaskPermission` (authority เดียวที่ใช้ร่วมกับ task assignment) |
| `UnityProject/Assets/Scripts/Core/CultivationMessages.cs` | `DiscipleCultivationChangedMessage` (Unity-only) |
| `UnityProject/Assets/Scripts/UI/Views/DiscipleDetailView.cs` | `SetCultivation` / `SetBreakthroughState` / `BreakthroughClicked` |
| `UnityProject/Assets/Scripts/UI/Presenters/DiscipleDetailTabPresenters.cs` | `InfoTabPresenter.ShowCultivation` / `TryBreakthrough` |
| `UnityProject/Assets/Scripts/UI/Presenters/DiscipleDetailPresenter.cs` | wiring ปุ่ม + subscription + refresh |
| `UnityProject/Assets/Scripts/UI/Presenters/LogWindowPresenter.cs` | narrative log line |
| `UnityProject/Assets/Editor/DiscipleDetailPanelGenerator.cs` + `Assets/Prefabs/UI/DiscipleDetailPanel.prefab` | generator + prefab ที่ regenerate |
| `UnityProject/Assets/Tests/EditMode/CultivationProgressTests.cs`, `CultivationBreakthroughTests.cs` | test ของ P14A/P14B |

> **หมายเหตุ counter:** หลัง P14B follow-up ตัวเลขจริงคือ **685** และหลัง P15 คือ **697** —
> `AGENTS.md` §1 อัปเดตเป็น 697 แล้ว (2026-10-11) จึงไม่มี stale counter ค้างอีก

## 13. P14B follow-up — `attempt_breakthrough` MCP tool

มติใหม่ (2026-10-11): ให้ AI GM ทะลวงขั้นได้ผ่าน bridge แต่ **ด้วยอำนาจเดียวกับคน** —
ไม่ใช่ autonomous loop และไม่มี policy ที่สอง

### 13.1 สัญญา

| ส่วน | รายละเอียด |
|---|---|
| `Shared/GameMessages.cs` | `AttemptBreakthroughRequest` `[Key(0)] RequesterId` / `[Key(1)] DiscipleId` + `AttemptBreakthroughResponse` `[Key(0)] Success` / `[Key(1)] FailReason` (append อย่างเดียว; sync 3 สำเนาเหมือนเดิม) |
| Unity handler | `Core/AttemptBreakthroughHandler.cs` — `MainThreadDispatch.RunAsync` → `ISessionGate.IsPlaying` → `ISectStateProvider.TryBreakthrough(requesterId, discipleId, out failReason)` |
| Registration | `Core/Installers/InterprocessInstaller.cs` — `RegisterTcpRemoteRequestHandler` **และ** `RegisterAsyncRequestHandler` (ขาดตัวใดตัวหนึ่ง = fail เงียบตอน runtime) |
| MCP tool | `McpBridge/Program.cs` → `SectActionTools.AttemptBreakthrough` (write tool ตัวที่สองต่อจาก `assign_task`) |

### 13.2 กติกาที่เลือก (และเหตุผล)

- **อำนาจ**: tool ส่ง `RequesterId = "SECT_MASTER"` hard-coded — ไม่มี parameter ให้ caller เลือก
  identity เพราะนั่นจะเท่ากับ bypass Hybrid 10 นาทีที่คนอื่นโดน; guard test บังคับทั้ง
  "declaration ต้องไม่มี requester" และ "handler ต้องไม่ implement policy เอง"
- **ไม่เพิ่ม read pair**: `get_sect_state` พา `DiscipleState.Attributes`
  (`CultivationStageId` + `CultivationProgress`) มาอยู่แล้ว → เพิ่ม query อีกตัวมีแต่จะ drift
- **ไม่แตะ `DiscipleCultivationChangedMessage`**: ยังเป็น Unity-only/in-memory ไม่อยู่บน broker
  — bridge poll `get_sect_state` แทน
- **ไม่แตะ Auto scheduler**: ยังไม่มีเส้นทางให้ AI ทะลวงเองจาก scheduler
- **Failure text**: คืน `FailReason` ของ authority ตรง ๆ (ไม่ translate/ไม่ retry เอง)

### 13.3 Verification (2026-10-11)

- `Tests/EditMode/CultivationBreakthroughTests.cs` — เพิ่ม/แก่ section 7: pair ลงทะเบียนครบทั้งสองฝั่ง,
  tool อยู่ฝั่ง write ไม่ใช่ query, declaration ไม่มี requester, handler delegate เท่านั้น,
  และ handler + `SECT_MASTER` กับศิษย์ที่มีเจ้าของ active → ปฏิเสธ + ไม่เปลี่ยน state + ไม่ publish
- `Tests/EditMode/MainThreadDispatchTests.cs` — hop contract (ไม่ mutate ก่อน pump) + shutdown fail-closed
- `Tests/EditMode/GameSessionCoordinatorTests.cs` — นอก Playing → `no active session` + ไม่ mutate
- `cd McpBridge && dotnet build` → 0 error (warning เดิม 63 ตัว)
