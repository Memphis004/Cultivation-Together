---
title: Building System — แผน Grid Placement (Phase 1)
type: gdd
status: draft v1 — เฉพาะ Phase 1 (grid placement engine), ยังไม่รวม task gating / movement
sources:
  - Reference game: ปรมาจารย์ครองเซียน (Xianxia Sovereign) — screenshots ที่แนบใน session นี้
  - UnityProject/Assets/Scripts/Systems/BuildingSystem.cs (stub ปัจจุบัน)
  - LLMWiki/wiki/sources/game-design-doc.md §7 (World Design — buildings)
  - LLMWiki/wiki/sources/open-questions.md #8
related:
  - "[[sources/open-questions]]"
  - "[[sources/task-system-v2]]"
  - "[[concepts/vcontainer-composition]]"
  - "[[concepts/state-management]]"
  - "[[concepts/mcp-bridge]]"
created: 2026-09-22
tags: [building, grid, placement, gdd, plan]
---

# Building System — Grid Placement Engine (Phase 1)

> **ขอบเขตเอกสารนี้: Phase 1 เท่านั้น** — วางอาคารลง grid ได้, เซฟตำแหน่งได้,
> เช็คการซ้อนทับได้, หักทรัพยากรได้ **ไม่รวม**: ศิษย์เดินไปทำงานที่อาคาร
> (movement — Phase 2), การผูก task กับอาคาร (task gating — คุยทีหลังสุด
> ตามที่ตกลงกัน)

## 0. การพลิกมติเดิม (ต้องรู้ก่อนเริ่ม)

`sources/mechanics.md` #8 และ `sources/open-questions.md` #8 เขียนไว้ว่า
Buildings ตั้งใจให้เป็น **"คลิกสร้างอย่างเดียว ไม่มีระบบวางตำแหน่งบนแมพ"**
เพื่อกัน AI GM loop ซับซ้อน — เอกสารนี้ **พลิกมตินั้น** โดยตั้งใจ ตามที่ผู้เล่น
ยืนยันแล้วว่าอยากได้ grid placement แบบเกม ref เมื่อ implement เสร็จ ต้อง
กลับไปแก้ทั้งสองไฟล์นั้นให้ตรงกับความจริงใหม่

## 1. สิ่งที่มีอยู่แล้ว vs ต้องสร้างใหม่

| ส่วน | สถานะ | หมายเหตุ |
|---|---|---|
| `BuildingSystem : IStartable, ITickable` | ✅ มีแล้ว แต่ **ว่างเปล่า 100%** (`Start(){} Tick(){}`) | จุดเริ่ม entry point เดิม — จะใส่ logic จริงที่นี่ หรือแยกเป็นระบบใหม่ต้องตัดสินใจ (§8) |
| `SectEconomyState` | ✅ มีแล้ว | ต้อง**เพิ่ม** `PlacedBuildings: List<PlacedBuildingState>` เป็น `[Key(2)]` ใหม่ (append-only ตามกฎเดิมของโปรเจกต์) |
| Grid data / placement validation | 🆕 ทั้งหมด | §3 |
| Building def table (ชนิดอาคาร, ราคา, ขนาด) | 🆕 ทั้งหมด | §2 — ควรเข้า Luban เหมือน event/avatar หรือไม่ (§8 Q1) |
| Placement UI (ghost preview, หมุน, ยืนยัน/ยกเลิก) | 🆕 ทั้งหมด | §5 |
| Build menu (หมวดหมู่ + รายการ) | 🆕 ทั้งหมด | §4 |
| Resource cost + currency display บนหัวจอโหมด build | 🆕 ทั้งหมด | §6 |

## 2. Building Definition — ข้อมูล design-time

จากเกม ref เห็นหมวดหมู่: **ผลิต** (การผลิต/แปลง), **สิ่งอำนวยความสะดวก**,
**ร้านค้า**, **ภูมิทัศน์** (ของตกแต่ง) แต่ละอาคารมี thumbnail preview
+ ปุ่มหมุน (⟲) ก่อนวางจริง

```csharp
namespace Xianxia.Sect.Building
{
    public enum BuildingCategory
    {
        Production,      // ผลิต — เช่น แปลงสมุนไพร, หอปรุงยา
        Convenience,      // สิ่งอำนวยความสะดวก
        Shop,             // ร้านค้า
        Landscape,        // ภูมิทัศน์ (ของตกแต่ง, ไม่ผูก task)
    }

    public class BuildingDef
    {
        public string Id;                 // "herb_plot", "pill_hall"
        public string DisplayName;
        public BuildingCategory Category;
        public int GridWidth;             // ขนาดบน grid หน่วยเป็น cell (เช่น 2x2)
        public int GridHeight;
        public Dictionary<string, int> Cost; // resourceId -> amount (herb/wood/ore/provisions/spirit stones ฯลฯ)
        public bool Rotatable;            // บางอาคาร (เช่น กำแพง/ถนน) หมุนได้, ของตกแต่งบางชิ้นอาจไม่ให้หมุน
        public string PrefabPath;         // Resources path สำหรับ preview + placed instance
        // Phase 2 จะเพิ่ม: WorkAnchorOffsets, MaxWorkers ฯลฯ — ไม่ใส่ตอนนี้ (YAGNI)
    }
}
```

**เก็บที่ไหน**: เสนอให้เข้า Luban เหมือน `event.xlsx`/`avatar_parts.json` เพราะ
เป็นข้อมูลตารางล้วนๆ (id, cost, ขนาด) ตรงเหตุผลเดียวกับที่โปรเจกต์ย้าย
ScriptableObject → Luban ตอน lab 12 — แต่เป็น **คำถามเปิด ไม่ใช่มติ** (§8 Q1)
เพราะทีมอาจอยากแก้อาคารบ่อยกว่า event ตอน prototype ระยะแรก ซึ่ง JSON เขียนมือ
(แบบ `avatar_parts.json`) แก้ไวกว่า

## 3. Grid + Placement Validation

### 3.1 พื้นที่สำนัก
```csharp
public class BuildingGrid
{
    // cell ว่าง = null, ไม่ว่าง = instanceId ของ PlacedBuildingState ที่ครองอยู่
    private readonly string[,] _occupancy;
    public int Width { get; }
    public int Height { get; }

    public bool CanPlace(int x, int z, int w, int h)
    {
        // ต้องอยู่ในขอบเขต grid ทั้งหมด และทุก cell ในพื้นที่ต้องว่าง
    }

    public void Occupy(string instanceId, int x, int z, int w, int h) { /* mark cells */ }
    public void Release(string instanceId) { /* สำหรับ Phase ถัดไป — ทุบอาคาร */ }
}
```
ขนาด grid ตายตัวตอน Phase 1 (เช่น 40×40 cell) — ขยายพื้นที่สำนักเป็นเรื่อง
Phase หลังๆ ไม่ต้องคิดตอนนี้

### 3.2 การเช็คตอนวาง (ตามที่เห็นใน screenshot: indicator สีแดง = วางไม่ได้)
ลำดับเช็ค:
1. อยู่ในขอบเขต grid หรือไม่
2. ทุก cell ที่ต้องการว่างหรือไม่ (`CanPlace`)
3. ทรัพยากรพอไหม (`Cost` เทียบกับ `Stockpile.RawResources` / wallet ส่วนกลาง —
   ต้องตัดสินว่า building cost หักจากอะไร, ดู §8 Q2)

ผลลัพธ์ preview ghost:
- เขียว/ปกติ = วางได้
- แดง (ตามรูป) = ซ้อนทับหรือทรัพยากรไม่พอ — **ควรแยกสีหรือ message ต่างกันไหม**
  ระหว่าง "ซ้อนทับ" กับ "เงินไม่พอ" เพื่อ UX ที่ชัดกว่าเกม ref (เกม ref ดูจะใช้
  ไอคอนแดงตัวเดียวรวมทุกกรณี)

### 3.3 State ที่ต้องเพิ่ม (append-only ตามกฎเดิม)
```csharp
[MessagePackObject]
public class PlacedBuildingState
{
    [Key(0)] public string InstanceId { get; set; }
    [Key(1)] public string DefId { get; set; }
    [Key(2)] public int GridX { get; set; }
    [Key(3)] public int GridZ { get; set; }
    [Key(4)] public int Rotation { get; set; }   // 0/90/180/270
}

[MessagePackObject]
public class SectEconomyState
{
    [Key(0)] public List<DiscipleState> Disciples { get; set; } = new();
    [Key(1)] public SectStockpile Stockpile { get; set; } = new();
    [Key(2)] public List<PlacedBuildingState> PlacedBuildings { get; set; } = new(); // 🆕
}
```
`[Key(2)]` เป็น key ใหม่ที่ว่างอยู่จริง (ตรวจแล้วจากไฟล์ `SectEconomyState.cs`
ปัจจุบัน — ไม่ชนกับอะไร) แก้ที่ `Shared/SectEconomyState.cs` แล้วรัน
`sync-shared.sh` ตามกฎ workspace เดิม

## 4. Build Menu — หมวดหมู่ (ตาม screenshot ฝั่งซ้าย)

```
ผลิต (Production)
สิ่งอำนวยความสะดวก (Convenience)
ร้านค้า (Shop)
ภูมิทัศน์ (Landscape)
─────────────
บันทึก (Save) — ปุ่มล่างสุด แยกจากหมวดหมู่ ใช้ commit การจัดวางทั้งหมด
```
ฝั่งขวาของ screenshot มีอีกชุดเมนู (สร้าง / จัดวาง / ค่ายกลเคลื่อนย้าย /
ออกแบบ / ถนน / กำแพง) — Phase 1 ทำเฉพาะ **สร้าง** กับ **จัดวาง** ก่อน
ส่วน ถนน/กำแพง/ค่ายกล เป็น building type พิเศษที่วางเป็นเส้น/ต่อเนื่องได้
(ไม่ใช่ grid เดี่ยว) → เลื่อนไป Phase หลัง ไม่รวมใน Phase 1

`UIService`/`UIPresenterKind` ที่มีอยู่ตอนนี้ (MVP Lite pattern) ใช้แพทเทิร์น
เดิมได้เลย: เพิ่ม `BuildingMenuPresenter` + `BuildingMenuView`,
`UIPresenterKind.BuildingMenu` ใหม่ ตาม checklist ใน `concepts/mvp-ui.md`
"Adding a New Panel"

## 5. Placement Flow (ตาม screenshot 2–4)

```
1. เปิด BuildingMenu → เลือกหมวด → เลือกอาคาร จาก thumbnail grid
2. เข้าสู่ "Placement Mode":
   - ซ่อน/แสดง grid overlay ทับพื้นสำนัก
   - แสดง ghost preview ของอาคารตามตำแหน่งเมาส์/นิ้ว (snap เข้า cell)
   - ปุ่มลอย 3 ปุ่ม (ตาม screenshot 3): ✕ ยกเลิก | ⟲ หมุน | ✓ ยืนยัน
3. ผู้เล่นลาก/แตะเพื่อเลื่อนตำแหน่ง ghost, กด ⟲ เพื่อหมุน (ถ้า Rotatable)
4. กด ✓ → BuildingGrid.CanPlace() ตรวจสอบ
   - ผ่าน → หักทรัพยากร (ผ่าน AdjustAndNotify เดิม ห้ามหักตรงๆ — บทเรียนจาก
     `state-management.md`) → เพิ่ม PlacedBuildingState → publish
     BuildingPlacedMessage (in-memory) → ออกจาก placement mode
   - ไม่ผ่าน → toast/label แจ้งเหตุผล (ตาม screenshot 4: "ยังไม่บันทึกสิ่งก่อสร้าง")
5. กด ✕ ตอนไหนก็ได้ → ยกเลิก ghost ทิ้ง ไม่มีอะไรเปลี่ยนแปลง state
```

ปุ่ม **บันทึก** (แยกจาก flow ข้างบน) ดูจาก screenshot น่าจะหมายถึง commit
การจัด**หลายชิ้น**พร้อมกันในโหมด "จัดวาง" (rearrange ของเดิม) มากกว่า
วางใหม่ทีละชิ้น — Phase 1 อาจไม่ต้องรองรับ rearrange เต็มรูป แค่วางใหม่ทีละ
ชิ้นแล้ว auto-commit ทันทีที่กด ✓ ก็พอ (ตัดความซับซ้อนของ "draft state ของ
ทั้งผัง" ออกไปก่อน ตามหลัก lean ของโปรเจกต์นี้)

## 6. Resource Cost Display (หัวจอตอนอยู่โหมด build)

Screenshot โชว์แถบทรัพยากร 3 ตัวเหนือจอ (355K, 40K, 40K พร้อมไอคอน + ปุ่ม)
ที่ต่างจาก `ResourceHudView` ปกติ (herb/wood/ore/provisions) — ตัวเลขเหล่านี้
ดูเหมือนเป็น **currency สำหรับ build เฉพาะ** ไม่ใช่ raw resource เดิม

**คำถามเปิด**: อาคารหักจากอะไรกันแน่ — raw resources เดิม (herb/wood/ore/
provisions), หรือ currency ใหม่ที่ยังไม่มีในเกม (เช่น "แต้มก่อสร้าง"),
หรือ Spirit Stones/Contribution ที่มีอยู่แล้ว? อันนี้ต้องตัดสินก่อนเขียน
`BuildingDef.Cost` จริง (§8 Q2)

## 7. DI + โครงไฟล์ที่เสนอ

```csharp
// GameLifetimeScope.Configure — ไม่ต้องมี interprocess registration ใหม่
// (เว้นแต่ AI GM ต้องสั่งสร้างอาคารเองผ่าน MCP tool — ดู §8 Q3)
builder.Register<Xianxia.Sect.Building.BuildingGrid>(Lifetime.Singleton);
builder.Register<Xianxia.Sect.Building.BuildingDefPool>(Lifetime.Singleton);
builder.Register<Xianxia.Sect.UI.BuildingMenuPresenter>(Lifetime.Transient);
// BuildingSystem (ITickable ที่มีอยู่แล้ว) — พิจารณาว่าจะยุบทิ้งหรือใช้เป็น
// จุดรวม state, ดู §8 Q4
```

```
Assets/Scripts/Building/
  BuildingDef.cs · BuildingDefPool.cs · BuildingGrid.cs
  PlacementController.cs      (plain C# — logic ของ ghost/validate/commit)
Assets/Scripts/UI/Views/BuildingMenuView.cs · BuildingPlacementView.cs (ghost overlay, ปุ่มลอย)
Assets/Scripts/UI/Presenters/BuildingMenuPresenter.cs
Assets/Resources/Data/building_defs.json   (หรือ DataTables/ ถ้าไปทาง Luban — §8 Q1)
```

## 8. Open Questions — ต้องตอบก่อนเริ่มโค้ดจริง

| # | คำถาม | ทำไมสำคัญ |
|---|---|---|
| Q1 | Building def เข้า Luban (Excel) หรือ JSON เขียนมือแบบ `avatar_parts.json`? | กำหนด pipeline ทั้งหมดของ §2 |
| Q2 | ค่าก่อสร้างหักจากอะไร — raw resources เดิม, Spirit Stones/Contribution, หรือ currency ใหม่? | กำหนดว่า `Cost` ผูกกับระบบเศรษฐกิจเดิมยังไง |
| Q3 | AI GM (MCP) ต้องสั่งสร้าง/วางอาคารได้ไหมใน Phase 1 หรือเป็นสิทธิ์ผู้เล่นเท่านั้นก่อน? | ถ้าต้อง → เพิ่ม request-response tool คล้าย `purchase_item`; ถ้าไม่ → ตัดออกได้เลยตอนนี้ |
| Q4 | `BuildingSystem.cs` (stub เดิม) เอามาใช้เป็น entry point ของระบบนี้ หรือสร้างระบบใหม่แล้วปล่อย stub ทิ้งไว้เฉยๆ? | เรื่อง naming/ownership ล้วนๆ ไม่กระทบ design แต่ต้องตกลงก่อนโค้ด |
| Q5 | ขนาด grid ของพื้นที่สำนัก (กว้าง×ยาว กี่ cell) และขนาด cell เท่าไหร่ (world unit)? | ตัวเลขจริงต้องมาจากขนาด art/สัดส่วนฉาก ยังไม่มีข้อมูล |
| Q6 | ของตกแต่ง (Landscape) นับเป็น "อาคาร" ที่กิน grid เหมือนกันไหม หรือวางอิสระไม่ snap grid? | กระทบว่า `BuildingGrid` ต้องรองรับ object 2 แบบหรือแบบเดียว |

## 9. What NOT to Touch (Phase 1)

- ไม่แตะ `TaskDef`/`TryAssignTask` ใดๆ — task gating เป็นเรื่องคุยทีหลัง
- ไม่สร้าง movement/pathfinding — ศิษย์ยังไม่ต้องเดินไปอาคารใน Phase 1
- ไม่แตะ `DiscipleVisualSystem` / chibi spawn ที่กำลังทำอยู่ตอนนี้ (Phase 2
  ของ disciple-visual-system) — ระบบนี้เป็นคนละแกน ไม่ overlap กัน
- ไม่ทำ "ทุบ/ย้ายอาคาร" (`Release`, rearrange เต็มรูป) — เตรียม hook ไว้ใน
  `BuildingGrid.Release()` แต่ยังไม่ implement UI

## Related Pages

- [[sources/open-questions]] — #8 (ต้องอัปเดตหลังพลิกมติ), เพิ่มหัวข้อ Building ใหม่
- [[sources/task-system-v2]] — จุดที่จะเชื่อมกันในอนาคต (`RequiredBuildingDefId`)
- [[concepts/mvp-ui]] — panel pattern ที่ BuildingMenu จะใช้
- [[concepts/state-management]] — กฎ `AdjustAndNotify`, append-only key
- [[concepts/additive-scene-architecture]] — ถ้า build เกิดเฉพาะใน GameplayScene
