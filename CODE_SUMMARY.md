# ทำเอกสารฉบับนี้โดย freebuff MiMo 2.5 prompt ช่วยอ่านโค้ดทั้งหมด แล้วสรุปการทำงานเป็นไฟล์เอกสาร .md ให้ที 11:59 8/28/2026

# Cultivation-Together — สรุปการทำงานของโปรเจค

> เอกสารสรุปนี้เขียนจากโค้ดจริงทั้งหมดในโปรเจค ณ วันที่ 22 ส.ค. 2026

---

## ภาพรวมโปรเจค

**Cultivation-Together** เป็นเกม simulator บริหารสำนักเซียน (Xianxia Cultivation Sect) บน Unity ที่ออกแบบมาให้ **AI Agent** (เช่น Open-LLM-VTuber) สามารถเข้ามาเล่นเป็น **Game Master / เจ้าสำนัก** แทนคนได้ ผ่านโปรโตคอล MCP (Model Context Protocol)

เป้าหมายคือรองรับสตรีมแบบโหวตร่วมกับผู้ชม (คล้าย King of the Castle บน Twitch) โดยให้ผู้ชมร่วมเป็น "ศิษย์สำนัก" โหวตตัดสินใจเหตุการณ์ต่างๆ ผ่าน Twitch extension

**แรงจูงใจหลัก**: ลดความเหนื่อยของสตรีมเมอร์ที่ต้องอ่านออกเสียง/คุมกลไกเกมเองตลอด โดยให้ AI VTuber รับบทนี้แทน

---

## สถาปัตยกรรม (Architecture)

```
┌─────────────────────────────────────────────────────────┐
│                    AI GM Client                         │
│              (Open-LLM-VTuber / MCP Inspector)          │
│                   ┌──────────┐                          │
│                   │ MCP stdio│                          │
│                   └────┬─────┘                          │
└────────────────────────┼────────────────────────────────┘
                         │
┌────────────────────────┼────────────────────────────────┐
│              MCP Bridge Process (.NET 8)                │
│         ┌──────────────┴──────────────┐                 │
│         │      McpBridge/Program.cs   │                 │
│         │  ┌──────────────────────┐   │                 │
│         │  │  SectQueryTools      │   │  ← Read tools   │
│         │  │  SectActionTools     │   │  ← Write tools  │
│         │  └──────────┬───────────┘   │                 │
│         └─────────────┼───────────────┘                 │
│                       │ MessagePipe.Interprocess TCP     │
│                       │ (127.0.0.1:3215, client)        │
└───────────────────────┼─────────────────────────────────┘
                        │
┌───────────────────────┼─────────────────────────────────┐
│                Unity Project (C#)                       │
│  ┌────────────────────┴────────────────────┐            │
│  │     GameLifetimeScope (VContainer)      │            │
│  │  ┌─────────────────────────────────┐    │            │
│  │  │   MessagePipe Bus (TCP Host)    │    │            │
│  │  └─────────────────────────────────┘    │            │
│  │                                         │            │
│  │  ┌──────────┐ ┌──────────┐ ┌────────┐  │            │
│  │  │TimeSystem│ │Disciple  │ │Resource│  │            │
│  │  │          │ │System    │ │Crafting│  │            │
│  │  └──────────┘ └──────────┘ └────────┘  │            │
│  │  ┌──────────┐ ┌──────────┐ ┌────────┐  │            │
│  │  │WorldEvent│ │Building  │ │Decision│  │            │
│  │  │System    │ │System    │ │Logger  │  │            │
│  │  └──────────┘ └──────────┘ └────────┘  │            │
│  │                                         │            │
│  │  ┌──────────────────────────────────┐   │            │
│  │  │    SectStateProvider             │   │            │
│  │  │    (live state aggregator)       │   │            │
│  │  └──────────────────────────────────┘   │            │
│  └─────────────────────────────────────────┘            │
└─────────────────────────────────────────────────────────┘
```

---

## Tech Stack

| ส่วน | เทคโนโลยี | หมายเหตุ |
|---|---|---|
| Game engine | Unity (C#) | ใช้ C# ทั้งสองฝั่ง (Unity + Bridge) |
| DI / Composition Root | **VContainer** | เบากว่า Zenject, register ทุก subsystem |
| Internal messaging | **MessagePipe** | pub/sub + request-response, zero-alloc |
| IPC (Unity ↔ Bridge) | **MessagePipe.Interprocess (TCP)** | Unity เป็น host, Bridge เป็น client |
| MCP Bridge | **.NET 8 Console App** | ใช้ `ModelContextProtocol` C# SDK (stdio) |
| Serialization | **MessagePack** | เลิกใช้ protobuf แล้ว (ตัดสินใจ lab รอบ 7) |
| DataTable | **Luban** | Excel → JSON → C# Plain Class (ไม่ใช้ ScriptableObject เพื่อเลี่ยงการคลิกสร้าง Asset และรองรับ Data-Driven Design) |
| UI Framework | **UI MVP LITE** | Custom UGUI + TMP framework (View=MonoBehaviour, Presenter=Plain C#) ออกแบบมาเพื่อทำงานร่วมกับ VContainer/MessagePipe โดยตรง ไม่พึ่ง Reflection |

---

## โครงสร้างไฟล์

```
Cultivation-Together/
├── README.md                    # คู่มือโครงสร้าง workspace
├── project_summary.md           # บันทึกการออกแบบ & lab rounds
├── sync-shared.sh               # สคริปต์ sync Shared/*.cs ไปทั้งสองฝั่ง
│
├── Shared/                      # ★ ต้นทางเดียวของไฟล์ร่วม (แก้ที่นี่!)
│   ├── economy.proto            # Schema อ้างอิง (เลิกใช้แล้ว 保留 ไว้เป็น reference)
│   ├── GameMessages.cs          # Message DTOs สำหรับ MessagePipe
│   ├── MockSectData.cs          # ข้อมูล mock 3 ศิษย์ สำหรับทดสอบ
│   └── SectEconomyState.cs      # State model ทั้งหมด (C# class + MessagePack)
│
├── UnityProject/                # เปิดใน Unity Hub
│   └── Assets/Scripts/
│       ├── Core/
│       │   ├── GameLifetimeScope.cs    # VContainer composition root
│       │   ├── TimeSystem.cs           # ระบบเวลา + request handlers
│       │   ├── DecisionLogger.cs       # Subscriber ตัดสินใจจาก bridge
│       │   └── PurchaseItemHandler.cs  # Request handler ซื้อของ
│       │
│       ├── Systems/
│       │   ├── DiscipleSystem.cs       # Passive resource gathering
│       │   ├── ResourceCraftingSystem.cs # Crafting loop
│       │   ├── BuildingSystem.cs       # Stub (ยังไม่ implement)
│       │   ├── WorldEventSystem.cs     # สุ่ม world event ทุก 15 วินาที
│       │   └── SectStateProvider.cs    # Live state aggregator + logic หลัก
│       │
│       └── Shared/                     # ← Synced จาก Shared/ (ห้ามแก้!)
│           ├── GameMessages.cs
│           ├── MockSectData.cs
│           └── SectEconomyState.cs
│
└── McpBridge/                   # .NET 8 console app (รันแยกจาก Unity)
    ├── McpBridge.csproj
    ├── Program.cs               # MCP server + MessagePipe TCP client
    └── Shared/                  # ← Synced จาก Shared/ (ห้ามแก้!)
        ├── GameMessages.cs
        ├── MockSectData.cs
        └── SectEconomyState.cs
```

---

## ระบบหลักที่ Implement แล้ว

### 1. ระบบเศรษฐกิจสำนัก (Sect Economy)

**สองสกุลเงิน**:
- **หินวิญญาณ (Spirit Stones)** — currency กลาง ใช้แลกเปลี่ยนไอเทม ได้จากภารกิจ/ผจญภัย
- **ค่าคุณูปการ (Contribution)** — ผูกกับสำนัก ใช้แลกของจากคลังสำนัก

**ทรัพยากรดิบ** (ของส่วนรวม):
- `herb` (สมุนไพร), `wood` (ไม้), `ore` (แร่), `provisions` (เสบียง)

**กฎความเป็นเจ้าของ**:
- ศิษย์ทั่วไป → ของคราฟท์เข้าคลังสำนัก
- ผู้อาวุโสขึ้นไป → เก็บของคราฟท์เป็นของส่วนตัวได้

**โมเดลข้อมูล** (`SectEconomyState`):+
```csharp
SectEconomyState
├── Disciples: List<DiscipleState>
│   ├── DiscipleId, DisplayName
│   ├── Rank (OuterDisciple / InnerDisciple / Elder / SectMaster)
│   ├── Wallet (SpiritStones + Contribution)
│   ├── PersonalInventory: List<InventoryItem>
│   └── CurrentTask: string
└── Stockpile: SectStockpile
    ├── RawResources: Dictionary<string, int>
    └── CraftedGoods: List<InventoryItem>
```

### 2. ระบบเก็บเกี่ยว被动 (Passive Gathering)

`DiscipleSystem` → `SectStateProvider.TickGathering()`

| Task | ทรัพยากร | อัตรา/วินาที |
|---|---|---|
| `gathering_herb` | herb | 0.2 |
| `gathering_wood` | wood | 0.2 |
| `gathering_ore` | ore | 0.15 |
| `gathering_provisions` | provisions | 0.25 |

มี fractional accumulator กันเสียเศษระหว่าง tick

### 3. ระบบคราฟท์ (Crafting)

`ResourceCraftingSystem` → `SectStateProvider.TickCrafting()`

| Recipe | วัตถุดิบ | ผลลัพธ์ | เวลา |
|---|---|---|---|
| `refining_elixir` | herb ×10 | `elixir_qi_gathering` (เกรด 3) | 20 วินาที |
| `forging_artifact` | ore ×15 + wood ×10 | `sword_azure_flame` (เกรด 5) | 30 วินาที |

- ถ้าทรัพยากรไม่พอ → progress ค้างที่ 100% รอ (all-or-nothing)
- คราฟท์เสร็จ → ของเข้าคลังสำนัก หรือ inventory ส่วนตัว (ตาม rank)

### 4. ระบบเหตุการณ์โลก (World Events)

`WorldEventSystem` สุ่ม trigger event ทุก 15 วินาที (skip ถ้ายัง waiting decision อยู่)

Event ที่มี:
- `bandit_raid_001` — โจรสЇ่บุก → หัก provisions 20
- `new_disciple_applicant` — ผู้สมัครใหม่ → ถ้า accept รับสมัครศิษย์นอก
- `herb_garden_bloom` — สวนสมุนไพรบาน → ได้ herb 30
- `wandering_merchant` — พ่อค้าเร่ → แลก ore 20 เป็น provisions 40

**งานที่ trigger → auto-pause → รอ decision → execute_decision → unpause → รอบถัดไป**

### 5. ระบบรับสมัครศิษย์ (Recruiting)

`SectStateProvider.RecruitOuterDisciple()` — สร้างศิษย์นอกใหม่ มอบหมาย gathering task แบบ round-robin

ชื่อที่มี: Chen Wei, Bai Ling, Zhou Tao, Xiao Mei, Jiang Yu, Wen Hao

### 6. ระบบร้านค้า (Contribution Store)

`SectStateProvider.TryPurchaseItem()` — ศิษย์ซื้อของจากคลังสำนักด้วยค่าคุณูปการ

- ราคา = grade × 50 × จำนวน
- ตรวจสอบทั้ง stock พอและ contribution พอ
- หัก contribution + ลด stock + เพิ่ม personal inventory

### 7. ระบบเวลา (Time System)

`TimeSystem` — จัดการ speed/pause, publish `TimeSpeedChangedMessage`

- `RaiseWorldEvent()` → auto-pause ถ้า `requiresDecision=true`
- `SetPaused(false)` → unpause เมื่อได้รับ decision

---

## MCP Tools (Interface สำหรับ AI GM)

### Read Tools (SectQueryTools)

| Tool | ฟังก์ชัน | ใช้ |
|---|---|---|
| `get_sect_state` | ดึง state ทั้งหมดของสำนัก (disciples, wallet, stockpile) | `SectStateQuery` → `SectStateSnapshot` |
| `await_next_world_event` | บล็อคจนกว่าจะมี event ใหม่ที่ต้องตัดสินใจ | `AwaitWorldEventRequest` → `AwaitWorldEventResponse` |

### Write Tools (SectActionTools)

| Tool | ฟังก์ชัน | ใช้ |
|---|---|---|
| `execute_decision` | ส่งตัดสินใจกลับไป Unity (event_id + choice_id) | `ExecuteDecisionMessage` (pub/sub) |
| `purchase_item` | ให้ศิษย์ซื้อของจากคลังสำนักด้วย contribution | `PurchaseItemRequest` → `PurchaseItemResponse` |

---

## Message Types (GameMessages.cs)

### Pub/Sub Messages (_delta/event)
| Class | ใช้สำหรับ |
|---|---|
| `DiscipleRecruitedMessage` | แจ้งเมื่อรับสมัครศิษย์ใหม่ |
| `DiscipleRankChangedMessage` | แจ้งเมื่อเปลี่ยนตำแหน่ง |
| `SectResourceChangedMessage` | แจ้งเมื่อทรัพยากรเปลี่ยน |
| `ContributionEarnedMessage` | แจ้งเมื่อได้ค่าคุณูปการ |
| `WorldEventTriggeredMessage` | แจ้งเหตุการณ์ (in-memory only ตอนนี้) |
| `TimeSpeedChangedMessage` | แจ้งเปลี่ยน speed/pause (internal only) |
| `ExecuteDecisionMessage` | ส่งตัดสินใจจาก bridge กลับ Unity |

### Request/Response (interprocess)
| Pair | ใช้สำหรับ |
|---|---|
| `SectStateQuery` → `SectStateSnapshot` | ดึง state snapshot |
| `AwaitWorldEventRequest` → `AwaitWorldEventResponse` | รอ event ใหม่ |
| `PurchaseItemRequest` → `PurchaseItemResponse` | ซื้อของจากคลัง |

---

## วิธีรัน

### 1. Sync Shared Files
```bash
./sync-shared.sh
```

### 2. รัน Unity
- เปิด `UnityProject/` ใน Unity Hub
- ติดตั้ง package ผ่าน openupm-cli:
```bash
openupm add jp.hadashikick.vcontainer
openupm add com.cysharp.messagepipe
openupm add com.cysharp.messagepipe.vcontainer
openupm add com.cysharp.messagepipe.interprocess
openupm add com.cysharp.unitask
```
- กด Play

### 3. รัน MCP Bridge
```bash
cd McpBridge
dotnet restore
dotnet run
```

### 4. ทดสอบ
- เปิด MCP Inspector
- npx @modelcontextprotocol/inspector dotnet run
- เรียก `get_sect_state` → ได้ JSON ข้อมูล mock กลับมา
- เรียก `await_next_world_event` → รอ event ใหม่
- เรียก `execute_decision` → ส่งตัดสินใจกลับ Unity

---

## ข้อจำกัดและ Known Issues

1. **BuildingSystem** — ยังเป็น stub ว่างเปล่า ยังไม่ implement
2. **WorldEventSystem** — placeholder สุ่ม 4 event คงที่ ยังไม่เช็คเงื่อนไข sect state
3. **ไม่มีระบบต่อสู้** — ยังไม่ตัดสินใจ (ACS-style vs Rimworld-style)
4. **ไม่มี stat system / วิชา / ตำรา / ไอเทม/อาวุธ** — deferred
5. **`economy.proto`** — เลิกใช้แล้ว ใช้ MessagePack จริงถาวร
6. **ตัวเลข balance** — ยังเป็น placeholder รอปรับจริงทีหลัง
7. **Unity Editor Pause** — ถ้ากด Pause ของ Editor จะทำให้ทุก Tick หยุด (ไม่ใช่บั๊กโค้ด)
8. **Run In Background** — ต้องเปิด `Edit > Project Settings > Player > Resolution and Presentation > Run In Background` ไม่งั้น tick จะหยุดเมื่อสลับหน้าต่าง

---

## บันทึกการออกแบบสำคัญ

1. **Message แยกจาก State**: pub/sub message คือ delta/event, state คือ full snapshot — ไม่ปนกัน
2. **ScriptableObject vs Protobuf**: SO สำหรับ data ที่ author ใน Editor (RecipeDef, ItemDef), MessagePack สำหรับ runtime state
3. **IDistributedSubscriber ใช้ไม่ได้จาก Bridge**: library เปิด listening socket เสมอ → ชน port — ต้องใช้ request-response เท่านั้นสำหรับ "Unity → Bridge" ทิศทางเดียว
4. **Request-Response ทั้งสองฝั่ง**: แม้ฝั่ง Unity (HostAsServer=true) ก็ต้อง register `RegisterTcpRemoteRequestHandler`

---

## แผนงานถัดไป

1. ✅ ResourceCraftingSystem (เสร็จแล้ว)
2. ✅ ร้านค้าค่าคุณูปการ — เอา CraftedGoods มาให้ศิษย์แลกซื้อ
3. ✅ EventData ScriptableObject — เลิก hardcode event list ใน WorldEventSystem
4. ⏳ Minimal UI in-game
 1. ✅  SectState `herb` (สมุนไพร), `wood` (ไม้), `ore` (แร่), `provisions` (เสบียง) 
 2. ✅  Personal Wallet  (Spirit Stones),(Contribution)
 3. 🔮 Windows Log

??. 🔮 Recipe/Event Deep-Detail
??. 🔮 History คลังสำนัก
??. 🔮 Character Avatar
??. 🔮 Disciple Rank Advancement
??. 🔮 ระบบต่อสู้
??. 🔮 Stat system / วิชา / ตำรา
??. 🔮 Tounament
??. 🔮 Disciple AI Brain (Utility AI + Narrative Traits)
??. 🔮 Vtuber AI Agent Game loop
??. 🔮 Twitch extension
??. 🔮 BuildingSystem
??. 🔮 สงครามสำนัก

## แล้ว Behavior Tree / GOAP / Utility AI เกี่ยวไหม?

| แนวทาง | 	เหมาะกับ | ตัวอย่างในเกมนี้ | 
|---|---|---|
| `Behavior Tree` | พฤติกรรมเป็นลำดับขั้น ชัดเจน | "ถ้าหิว→กิน, ถ้ามีงาน→ทำ, ถ้าว่าง→หาอะไรทำ" | 
| `Utility AI` | ตัดสินใจจากคะแนนความเหมาะสม | "เก็บสมุนไพร=80แต้ม, คราฟท์ยา=60แต้ม, พักผ่อน=30แต้ม → เลือกเก็บสมุนไพร" | 
| `GOAP` | วางแผนเป็นขั้นตอนเพื่อบรรลุเป้าหมาย | "อยากเลื่อนขั้น → ต้องมี contribution 500 → ต้องคราฟท์ยา 10 เม็ด → ต้องมี herb 100 → ไปเก็บ herb ก่อน" | 
| `Narrative AI` | สร้างเรื่องราว/ความสัมพันธ์ | "ศิษย์ A ชอบศิษย์ B, เกลียดศิษย์ C, มีความหลังกับสำนัก D" → CK2/CK3 ใช้แบบนี้เยอะ" | 

### คำแนะนำสำหรับโปรเจกต์นี้
Utility AI + Narrative Traits คือคู่ที่ลงตัวที่สุดค่ะ
### เพราะ:
Utility AI ตอบคำถาม "ตอนนี้ควรทำอะไร?" จาก state ปัจจุบัน (ทรัพยากร, ความเหนื่อย, ตำแหน่ง)
Narrative Traits ตอบคำถาม "ทำไมถึงเลือกสิ่งนี้?" จาก personality/relationship (ศิษย์ขยัน vs ขี้เกียจ, มิตร vs ศัตรู)

