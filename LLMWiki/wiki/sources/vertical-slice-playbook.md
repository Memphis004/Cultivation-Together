---
title: Vertical Slice Playbook (P15)
type: source
sources:
  - "[[sources/cultivation-system]]"
  - "[[sources/architecture]]"
  - "[[concepts/time-system]]"
  - "[[concepts/state-management]]"
related:
  - "[[sources/cultivation-system]]"
  - "[[concepts/decision-pipeline]]"
  - "[[concepts/gathering-system]]"
created: 2026-10-11
updated: 2026-10-11
confidence: high
tags: [verification, vertical-slice, playbook, autosave, metrics]
---

# Vertical Slice Playbook (P15) — ทดสอบทั้งเกมเป็นหนึ่งเส้นทาง

> P15 = พิสูจน์ว่า **ทั้งเกมเดินเป็นเส้นทางเดียวได้จริง** โดยไม่ต้องมี Twitch
> (local vertical slice) — clean launch → Title → New Game → assign task ด้วยมือ →
> เปิด Auto → ทำงาน/ฟื้น stamina → event decision → Save → Return to Title → Continue
> → state กลับมาตรงเป๊ะ
>
> ไฟล์นี้มี 2 ส่วนที่คนละหน้าที่: **§1 coverage อัตโนมัติ** (รันได้ทุกครั้ง) และ
> **§2 playbook ที่คนต้องรันเอง** — ส่วน §3–§6 คือผลที่เก็บได้จริงจากการรัน P15

---

## 0. สถานะ: IMPLEMENTED vs NOT VERIFIED

| เรื่อง | สถานะ | หลักฐาน |
|---|---|---|
| integration suite สำหรับ vertical slice (12 tests) | ✅ IMPLEMENTED | `Tests/EditMode/VerticalSliceIntegrationTests.cs` — 12/12 ผ่าน |
| 1×/2×/3× เทียบเท่ากันเมื่อ **simulation time เท่ากัน** | ✅ IMPLEMENTED | `SpeedEquivalence_1x_2x_3x_OverEqualSimulationTime_ProducesEqualProducedMass` |
| pause ถูกต้องทุกแกน (งาน/ของ/เวลา) | ✅ IMPLEMENTED | `Pause_FreezesEveryAxis_AndResumeHoldsProgressWithoutLossOrJump` |
| Viewer Hybrid protection ผ่าน local fixture | ✅ IMPLEMENTED | `ViewerHybridProtection_LocalFixture_BlocksMasterWhileOwnerActive_ThenAllows` |
| ไม่มี task notification / craft reward ซ้ำ | ✅ IMPLEMENTED | `NoDuplicateTaskNotificationsOrCraftRewards_AcrossTicksAndAcrossASaveLoad` |
| crafting / fractional progress ข้าม save | ✅ IMPLEMENTED | `FractionalGatherAndCraftProgress_SurviveSaveAndLoad_WithoutDoubleProduction` |
| building occupancy หลัง load | ✅ IMPLEMENTED | `BuildingOccupancy_AfterLoad_MatchesPlacedBuildings_AndStillBlocksOverlap` |
| old-session RPC / event callback ถูกปฏิเสธ | ✅ IMPLEMENTED | `OldSessionRpcAndEventCallbacks_AreRejected_AfterReturnToTitle` |
| Title/NewGame/Load หลายรอบไม่ leak listener | ✅ IMPLEMENTED | `RepeatedTitleNewGameLoadCycles_LeakNoListeners_AndNotifyExactlyOncePerEvent` |
| cold restart / corrupt save / failed write | ✅ IMPLEMENTED | `ColdRestart_…`, `CorruptSave_FailsClosed_…` |
| debug metrics (switches / work mix / resource) | ✅ IMPLEMENTED (เก็บ + รายงาน) | `DebugMetrics_…` (§3) |
| **playbook รอบเล่นจริงบนหน้าจอ** | ⚠️ **NOT VERIFIED** | ต้องมีคนกดใน Play Mode (§2) — ยังไม่มีใครรันรอบนี้ |
| **autosave ของ session** | ❌ NOT IMPLEMENTED | ตรวจแล้วว่ายังไม่มี (§5) — ยังไม่ตัดสินใจทำ |
| **tuning / ปรับ balance** | ❌ NOT APPLIED | §4 เป็นข้อเสนอ ไม่ได้แก้ค่าในเกม |
| **defect ที่ P15 ค้นพบในตัวเกม** | — ไม่มี | เทสต์เขียว; ทุกอย่างที่ fail ระหว่างพัฒนาเป็น bug ของ harness/test เอง (§6) |

**จำนวนเทสต์ EditMode ทั้งชุด: 697/697 ผ่าน** (618 ก่อน P14 → 680 หลัง P14A/B →
685 หลัง P14B follow-up → **697** หลัง P15 +12) — วัดจาก `run_tests` mode editor, 2026-10-11

---

## 1. Automated coverage — `Tests/EditMode/VerticalSliceIntegrationTests.cs`

ไฟล์เดียว 12 เทสต์ ขับ **service จริง** ทั้งหมด (`SectStateProvider`, `TimeSystem`,
`AutoTaskScheduler`, `DecisionExecutor`, `SessionSnapshotService`, `SaveSessionService`,
`GameSessionCoordinator`, `AssignTaskHandler`) ผ่าน seam ที่มีอยู่ — **ไม่ reimplement
กติกาของเกม และไม่มี policy สำหรับเทสต์โดยเฉพาะ**

| # | เทสต์ | พิสูจน์อะไร |
|---|---|---|
| 1 | `FullPath_Title_NewGame_ManualAuto_Decision_Save_Continue_RestoresExactState` | เส้นทางหลักทั้งเส้น: Title → New Game → assign ด้วยมือ → เปิด Auto → เดินเวลา → decision → Save → Return to Title → Continue แล้ว state (งาน/ทรัพยากร/attributes/speed/pending) ตรงกับก่อน save |
| 2 | `SpeedEquivalence_1x_2x_3x_OverEqualSimulationTime_ProducesEqualProducedMass` | 1×/2×/3× ให้ผลผลิตเท่ากันเมื่อจำลองเวลาเท่ากัน (ไม่มีการคูณ speed ซ้ำ) |
| 3 | `Pause_FreezesEveryAxis_AndResumeHoldsProgressWithoutLossOrJump` | pause แล้วงาน/of/เวลาหยุดพร้อมกัน, resume แล้วเศษที่ถืออยู่ไม่หายและไม่กระโดด |
| 4 | `ViewerHybridProtection_LocalFixture_BlocksMasterWhileOwnerActive_ThenAllows` | เจ้าของ (viewer fixture) สั่งได้, `SECT_MASTER` ถูกบล็อกระหว่างหน้าต่าง 600 วิ (+ breakthrough ใช้ authority เดียวกัน) แล้วผ่านหลังพ้นหน้าต่าง |
| 5 | `NoDuplicateTaskNotificationsOrCraftRewards_AcrossTicksAndAcrossASaveLoad` | task change ที่ publish ตรงกับที่ state เปลี่ยนจริง และ completion/XP จ่ายครั้งเดียว — รวมหลัง save/load |
| 6 | `FractionalGatherAndCraftProgress_SurviveSaveAndLoad_WithoutDoubleProduction` | เศษของ gathering/crafting ข้าม save โดยไม่ผลิตซ้ำ (ไม่มี double production ที่ tick แรกหลัง load) |
| 7 | `BuildingOccupancy_AfterLoad_MatchesPlacedBuildings_AndStillBlocksOverlap` | หลัง load grid occupancy ตรงกับอาคารที่วางไว้ และยังกันการวางทับ |
| 8 | `OldSessionRpcAndEventCallbacks_AreRejected_AfterReturnToTitle` | หลัง Return to Title callback/RPC ของ session เก่าถูกปฏิเสธ ไม่ mutate |
| 9 | `RepeatedTitleNewGameLoadCycles_LeakNoListeners_AndNotifyExactlyOncePerEvent` | วน Title → NewGame → Load หลายรอบ ไม่มี listener leak และ event แจ้งครั้งเดียวต่อเหตุการณ์ |
| 10 | `ColdRestart_LoadsTheSaveFromDiskWithNoLiveState_AndRefusesWhenNoSaveExists` | เปิดโปรเซสใหม่ (ไม่มี live state) แล้ว Continue ได้จากไฟล์จริง; ไม่มี save → ปฏิเสธ |
| 11 | `CorruptSave_FailsClosed_AndRecoversFromTheBackup_AndAFailedWriteKeepsThePreviousSave` | save เสีย → recover จาก backup; **ทั้งสอง generation เสีย → fail closed ไม่แตะ live state**; write ที่ fail (เกิน limit) ไม่ทับ save เดิมแม้แต่ไบต์เดียว |
| 12 | `DebugMetrics_TaskSwitches_WorkMix_AndResourceChangeOverSimulationTime` | เก็บ metrics (§3) พร้อม invariant ว่าทุกวินาทีจำลองถูกนับในหมวดเดียว และ mass = rate × เวลาที่ผลิตจริง |

**Doubles ที่ EditMode บังคับ (และมีแค่ 4 อย่าง):** deterministic scene loader, inline
save worker hop, mutable clock, buffering publishers — ไม่มี double ตัวใดแทนกติกาเกม

---

## 2. วิธีเล่นเองด้วยมือ (Play Mode, ไม่ต้องมี Twitch)

รัน 1 รอบใช้ ~5 นาที ค่าที่ต้องจับตาอยู่ในวงเล็บ

1. **clean launch** — เปิด `UnityProject/` แล้วกด Play (ไม่มี save ค้างใน
   `persistentDataPath/Saves` ก็ได้ หรือลบ `sect_session.slot*` ก่อน)
   → Boot ที่ **Title menu**, HUD ของ session ยัง**ไม่**โชว์, ไม่มีฉาก gameplay โหลด
2. **New Game** — กด New Game → ปุ่มถูก disable ระหว่าง transition → โหลด `SectScene`
   → session HUD/time control bar โผล่
3. **assign task ด้วยมือ** — เปิด panel assign task → ตั้ง `d001` = `gathering_herb`
   (ต้องมี `herb_plot` วางอยู่ ไม่มี = แถวถูกล็อกพร้อมเหตุผล) → confirm
   → เกิด **task change 1 ครั้ง** (ไม่ซ้ำ) และ log window มีบรรทัดเดียว
4. **เปิด Auto** — toggle Auto ที่แถวของ `d002`/`d003` (แถวของ disciple ที่ viewer
   ถืออยู่จะ toggle ไม่ได้ — ถูกต้อง) → รอ ~1 นาที
5. **work / recovery** — ศิษย์ที่ Auto ทยอยทำงาน, `d000` ที่นั่งสมาธิจะไม่ขยับงาน;
   ใคร stamina ต่ำจะเข้า meditation แล้วกลับมาทำงานเมื่อฟื้น (ตอบ ไม่กระพริบไปมา)
6. **event decision** — รอ world event ที่ต้องตัดสิน (popup เด้ง + เกม pause)
   → เลือกตัวเลือก → consequence ถูกใช้ **ครั้งเดียว**; ถ้ากด Escape ปิด popup
   จะมี **chip** ลอยเตือนและเกมยัง pending (ไม่ resume เอง)
7. **Save** — เปิด In-game menu → Save → ขึ้น success (ถ้า fail จะขึ้นเหตุผลจริง
   ไม่ใช่ "failed" ลอย ๆ)
8. **Return to Title** — ต้องยืนยันก่อน → ฉากถูก unload, HUD/panel ของ session ปิดหมด,
   กลับมาที่ Title โดย Title menu ยังอยู่
9. **Continue** — กด Continue → โหลดกลับ → **เทียบด้วยตา** กับตอน save:
   งานของแต่ละคน, ทรัพยากรใน HUD, stamina/skill xp, stage cultivation, speed 1×/2×/3×,
   อาคารบน grid, และ event ที่ค้าง (ถ้ามี) ต้องขึ้นมาให้ตัดสินต่อ
10. **pause** — กด spacebar: label เป็น "หยุด" และ speed ไม่ถูก highlight → รอ 10 วิ
    → ทรัพยากร/งาน/stamina **ต้องไม่ขยับ** → resume แล้วเดินต่อจากจุดเดิม (ไม่มี catch-up)
11. **speed** — 3× แล้วเทียบกับ 1× ในหน้าต่างเวลา sim เท่ากัน: อัตราผลิตต่อ sim-time
    ต้องเท่ากัน (ที่ 3× เร็วขึ้น 3 เท่าในเวลาจริง)
12. **ไม่พึงพอใจอะไร ให้จด**: ก่อนแก้ ให้เพิ่ม regression test ในข้อ §1 ที่ **fail ก่อนแก้** เสมอ

> ถ้าทำครบ 12 ข้อ = P15 verified ด้วยมือ ให้เปลี่ยน §0 บรรทัด playbook เป็น ✅
> แล้วจดวันที่ + สิ่งที่เห็นเพิ่มใน `log.md`

---

## 3. Debug metrics — ที่เก็บได้จริง (2026-10-11)

เก็บด้วย observability ที่มีอยู่แล้ว (`DiscipleWorkOutcome` ต่อ tick จาก
`TickGathering`/`TickCrafting` + `DiscipleTaskChangedMessage` + `AdjustAndNotify`
ที่ choke point เดียว) — 120 วินาที simulation, speed 1×, roster 4 คน, `herb_plot` 1 หลัง
ผลพิมพ์ผ่าน `TestContext.WriteLine` (อยู่ใน `TestResults.xml`):

```
P15 debug metrics — 120 steps x 1s sim time, speed 1x, roster 4
  d000: task=meditation       switches=0 productive=0.0s blocked=0.0s recovery=120.0s idle=0.0s stamina=100.0
  d001: task=gathering_herb   switches=1 productive=120.0s blocked=0.0s recovery=0.0s idle=0.0s stamina=85.6
  d002: task=gathering_ore    switches=1 productive=119.0s blocked=0.0s recovery=1.0s idle=0.0s stamina=85.7
  d003: task=gathering_provisions switches=2 productive=117.0s blocked=0.0s recovery=3.0s idle=0.0s stamina=86.0
  resource herb: 24.0 units over 120.0s sim (12.00/sim-min) from 120.0s productive
  resource wood: 0.0 units over 120.0s sim (0.00/sim-min) from 0.0s productive
```

สิ่งที่ตัวเลขบอก (ไม่ใช่การ auto-rebalance — แค่ข้อมูล):

- **task switches ต่ำมาก** (0–2 ต่อ 2 นาที sim): hysteresis/minimum-dwell ทำงานตามดีไซน์
  — Auto ไม่กระพริบ แต่ก็แปลว่า *ถ้า* ค่าเริ่มไม่ตรง ก็จะไม่ถูกแก้ให้เร็ว
- **productive ≈ 117–120/120 วิ** (97–100%): ยัง**ไม่มี pressure ให้หยุดทำงาน**
  เพราะ 120 วิ sim ยัง drain stamina ไม่พอจะแตะ threshold ต่ำ → ยังมองไม่เห็น
  วงจร recovery เต็มรูปแบบในหน้าต่างสั้น
- **herb 12.00/sim-min จาก productive 120.0 วิ** = rate 0.2/วิ ตรงเป๊ะ (ยืนยันว่า
  mass ไม่มีทางมาจากที่อื่น) — `wood 0` เพราะ Auto **ไม่เลือก** `gathering_wood`
  เมื่อไม่มี shortage (ถูกต้องตาม scoring) จึงให้ข้อมูลน้อย
- `d000` recovery 100% = ค่าเริ่มต้นของ session (manual meditation) — ไม่ถูก Auto แตะ

---

## 4. Tuning proposals (ข้อเสนอ — **ยังไม่แก้ในเกม**)

1. **วัด recovery ให้เห็น**: เก็บ metrics อีกชุดที่ 600–900 วิ sim (หรือ stamina เริ่มต่ำ)
   เพราะ 120 วิ มองไม่เห็น threshold สูง/ต่ำเลย — ตัวเลข recovery ปัจจุบัน**ยังไม่พอ**จะบอกว่า
   threshold ควรขยับ
2. **wood 0 units**: ก่อนจูน weight ควรเก็บ metric ของ shortage (candidates score ต่อ task)
   เพื่อยืนยันว่า "ไม่เลือก wood เพราะไม่มี shortage" ไม่ใช่ "candidate ตกหล่น"
3. **switches=0 ของ d000**: ตั้งใจ (manual) — แต่ควรมี metric แยก manual/Auto
   เพื่อไม่ให้ค่าเฉลี่ยตีความผิด
4. **Blocked = 0 ตลอด**: ยังไม่มีเคส "task ที่ถูก assign แล้ว block" ในหน้าต่างนี้
   (เช่น refining_elixir ไม่มี pill_hall) — เพิ่มเคสนั้นเวลาเก็บ metrics รอบหน้า
   เพราะ `Blocked` เป็นหมวดที่ P14A ตั้งใจมี

---

## 5. Autosave — ตรวจแล้ว **NOT IMPLEMENTED** (feasibility INSPECTED)

สิ่งที่**มีอยู่แล้ว**ในชั้น save/load (P11B) และใช้ต่อได้ทันที:

| ชิ้นส่วน | ไฟล์ | บทบาทที่ autosave จะใช้ |
|---|---|---|
| `SaveSessionService` | `Core/Persistence/SaveSessionService.cs` | capture บน main thread + I/O บน worker, error เป็นผลลัพธ์จริง (ไม่ throw) |
| `SaveOperationGate` | `Core/Persistence/SaveOperationGate.cs` | **single-flight** — ทำให้ write ถูก serialize ไม่มีสอง write พร้อมกัน |
| `SessionTransitionTracker` | `Core/Persistence/SessionTransitionTracker.cs` | generation guard: ผลลัพธ์ของ session ที่ตายแล้วถูก invalidate ไม่ commit |
| `ISaveWorkScheduler` | `Core/Persistence/SaveWorkScheduler.cs` | ย้าย file I/O ออกจาก main thread |
| `SaveSlotPaths` | `Core/Persistence/SaveSlotPersistence.cs:218` | slot + backup + temp (วันนี้เป็น slot เดียว **ไม่มี** ชุด path สำหรับ autosave) |
| `ViewerMembershipPersistenceSystem` | `Core/ViewerMembershipPersistence.cs` | **precedent ของ autosave แบบ real-time** (`AutoSaveIntervalSeconds = 10f`, tick ตามเวลาจริง) |

สิ่งที่**ไม่มี** (จึงยังไม่ทำ):

- ไม่มี driver ที่ tick แล้วสั่ง `SaveAsync()` เป็นคาบ — `SaveSessionService.cs:25` และ
  `GameplayInstaller.cs:88` เขียนไว้ชัดว่า autosave อยู่นอก scope มาแต่แรก
- ไม่มี path ชุดที่สองสำหรับ autosave (`SaveSlotPaths` มี slot/backup/temp ชุดเดียว)
- **ห้ามพึ่ง application quit เป็นทางเดียว**: ตอนนี้ทางเดียวที่การันตีว่า session รอดคือ
  ผู้เล่นกด Save (§1 test 10 พิสูจน์ว่า Continue จากไฟล์จริงได้ แต่ก็ต่อเมื่อมีคน save)

ถ้าจะทำ (มติที่ต้องได้ก่อน): (ก) autosave เขียน **slot แยก** (`sect_autosave.slot` + backup
ของตัวเอง) ไม่แตะ slot ของผู้เล่น, (ข) ใช้ `SaveOperationGate` ร่วมกัน — ถ้า manual save
กำลังเขียนอยู่ autosave **ข้ามรอบนั้น** ไม่ต่อคิว, (ค) ผ่าน `SessionTransitionTracker`
generation เดิม, (ง) rotate (เก็บ N ไฟล์) และ (จ) ต้องมี test แบบเดียวกับ §1 ข้อ 10–11
(cold restart + corrupt + failed write) ก่อนเปิดใช้

---

## 6. ข้อจำกัด / สิ่งที่ P15 **ไม่ได้** พิสูจน์

- **ยังไม่มีใครรัน playbook §2 บนหน้าจอ** — integration suite พิสูจน์ logic ไม่ใช่การ
  render/คลิกจริง (panel, popup, ปุ่ม, chip) ข้อบกพร่องเชิง UI ที่ไม่ทำให้ state ผิด
  จึงยังหลุดได้
- **ไม่พบ defect ของตัวเกมจาก P15** — ทุก failure ระหว่างพัฒนาเป็น bug ของ
  test harness/harness double เอง (เช่น constructor ผิดรูป, การนับ switch ที่นับ
  pre-loop ไม่ครบ) จึงไม่มีการแก้ product code ในเฟสนี้
- metrics §3 เป็น snapshot 120 วิ ไม่ใช่โปรไฟล์ยาว — อัตราผลิต/พฤติกรรม recovery
  ในเกมยาว ๆ ยังไม่ถูกวัด
- autosave, tuning และ UI รอบจริง = ตามสถานะใน §0
- ไม่ได้ทดสอบกับ Twitch จริง/IRC — P15 ตั้งใจให้เป็น local slice

---

## 7. ไฟล์ที่เกี่ยวข้อง

| ไฟล์ | บทบาท |
|---|---|
| `UnityProject/Assets/Tests/EditMode/VerticalSliceIntegrationTests.cs` | 12 integration tests + harness + metrics |
| `UnityProject/Assets/Tests/EditMode/SessionSavePersistenceTests.cs` | ชั้น disk/slot/การันตี write (P11B) — P15 เรียกใช้ ไม่ได้เขียนซ้ำ |
| `UnityProject/Assets/Tests/EditMode/SessionSnapshotTests.cs` | snapshot/restore contract (P11A) |
| `UnityProject/Assets/Tests/EditMode/SimulationTimeTests.cs` | 1×/2×/3×, pause, accumulator ที่ระดับ unit |
| `UnityProject/Assets/Scripts/Core/Persistence/*` | `SaveSessionService` / `SaveSlotRepository` / `SaveOperationGate` / `SessionTransitionTracker` |
| `UnityProject/Assets/Scripts/Core/Session/GameSessionCoordinator.cs` | Title ↔ Playing, scene load, stale-completion invalidation |
| `LLMWiki/wiki/sources/cultivation-system.md` | P14 (bำเพ็ญ) — อยู่ในเส้นทางที่ P15 เดินผ่าน |
