# Xianxia Sect Simulator — MCP-Enabled Game Framework
สรุปการออกแบบ + บันทึกความคืบหน้า (อัปเดตล่าสุด: Avatar กลับเป็น v2.5 parts-only MVP — v3 Outfit Packages ถูก rollback)

## แนวคิดโปรเจกต์
เกม simulator บริหารสำนักเซียน (xianxia cultivation sect) บน Unity ที่ออกแบบให้ AI Agent
(เช่น Open-LLM-VTuber) เข้ามาเล่นเป็น Game Master / เจ้าสำนัก แทนคนได้ผ่าน MCP
โดยมีเป้าหมายรองรับสตรีมแบบโหวตร่วมกับผู้ชม (คล้าย King of the Castle บน Twitch)
ให้ผู้ชมร่วมเป็น "ศิษย์สำนัก" โหวตตัดสินใจเหตุการณ์ต่างๆ ผ่าน Twitch extension
แรงจูงใจหลัก: ลดความเหนื่อยของสตรีมเมอร์ที่ต้องอ่านออกเสียง/คุมกลไกเกมเองตลอด
โดยให้ AI VTuber รับบทนี้แทน

## Tech stack ที่ตกลงไว้
| ส่วน | เทคโนโลยี | หมายเหตุ |
| --- | --- | --- |
| Game engine | Unity (C#) | ใช้ C# ทั้งสองฝั่ง (Unity + Bridge) |
| DI / Composition Root | **VContainer** | เบากว่า Zenject, register ทุก subsystem |
| Internal messaging | **MessagePipe** | pub/sub + request-response, zero-alloc |
| IPC (Unity ↔ Bridge) | **MessagePipe.Interprocess (TCP)** | Unity เป็น host, Bridge เป็น client |
| MCP Bridge | **.NET 8 Console App** | ใช้ `ModelContextProtocol` C# SDK (stdio) |
| Serialization | **MessagePack** | เลิกใช้ protobuf แล้วถาวร (lab รอบ 7) |
| DataTable | **Luban** | Excel → JSON → C# Plain Class (ไม่ใช้ ScriptableObject เพื่อเลี่ยงการคลิกสร้าง Asset) |
| UI Framework | **Xianxia.UI.MVP Lite** | Custom UGUI + TMP (View=MonoBehaviour, Presenter=Plain C#) ทำงานร่วมกับ VContainer/MessagePipe โดยตรง ไม่พึ่ง Reflection |

## หลักคิดกลางที่ใช้คุมทุกระบบ
> ทุกกลไกต้องเป็นสิ่งที่ AI ตัดสินใจได้จากข้อมูลที่ query ผ่าน MCP ได้จริง
> ไม่พึ่ง "สัญชาตญาณมนุษย์" ที่ AI เข้าไม่ถึง

---

## 🆕 ความคืบหน้าล่าสุด (รอบนี้)

### 1. Avatar System — Parts-only MVP (Dictionary Schema)

**⚠️ Rollback (2026-09-03):** ระบบ **Outfit Packages (v3) ถูกลบออกแล้ว** — โค้ด/JSON ปัจจุบันเป็น parts-only:
ผู้เล่นเลือก part ทีละ slot (`body`/`head`/`hair`/`accessory`/`face_marking`) อิสระกัน, ไม่มี outfit preset,
ไม่มี pose-switching, ไม่มี UI tab "ชุดแต่งกาย" เหตุผลเต็มอยู่ที่ LLMWiki `wiki/sources/avatar-appearance.md` §1
(สรุป: outfit derive จาก `parts[]` ได้ 100% และตอนนี้มี pose เดียวจึงไม่มีอะไรให้สลับ)

**ปัญหาเดิม:** Schema แบบ fixed property (`Body/Head/Hair/Accessory`) ขยาย slot ไม่ได้โดยไม่ break save file + ผมยาวซ้อน layer ผิด (วิกมุดหัว)

**สิ่งที่แก้:**
-   **`AvatarAppearance` เปลี่ยนเป็น Dictionary-based:**
    ```csharp
    [Key(0)] Dictionary<string, string> Parts;  // slot → partId
    [Key(1)] Dictionary<string, string> Colors; // slot → colorId (เตรียมไว้สำหรับ tint)
    [Key(2)] string PoseId;                     // reserved — pose คงที่ pose_idle_01 (ยังไม่มี UI เปลี่ยน)
    ```
    -   เพิ่ม slot ใหม่ = แก้แค่ JSON ไม่ต้องแก้ schema
    -   AI GM ค้นพบ slot ที่มีอยู่ได้เองจาก `get_sect_state`
    -   `GetSlot/SetSlot/Clone` API เหมือนเดิม → Presenter ไม่ต้องแก้
-   **ขยาย Slot เป็น 10 ช่อง + Category:**
    -   Slots: `body, head, eyes, brows, mouth, nose, hair, face_marking, eyeshadow, accessory`
    -   Categories (UI Tabs): `ใบหน้า` (Head/Eyes/Brows/Mouth/Nose), `ลักษณะ` (Hair/FaceMarking/Eyeshadow), `ร่างกาย` (Body/Accessory)
-   **Hair 2-Layer (ผมหน้า/ผมหลัง):**
    -   `AvatarPartDef` เพิ่ม `spritePathBack` + `drawOrderBack`
    -   `AvatarRenderer` แยก layer: ผมหลัง (order 10, ใต้ตัว) + ผมหน้า (order 40, บนหน้า)
    -   Stack ที่ถูกต้อง: `base(0) → hair_back(10) → body(20) → head(30) → face_marking(34) → hair_front(40) → accessory(50)`
-   **Part metadata (pose/sex):** ทุก `AvatarPartDef` มี `poseId` (`""` = universal, ปัจจุบันมีแค่ `pose_idle_01`)
    และ `sexTag` (`"male"`/`"female"`/`""`) — `Randomize` กรองตาม pose/sex ของศิษย์ (`DiscipleState.Sex`),
    `TryChangeAvatarPart()` มี pose validation (pose เดียวตอนนี้จึงไม่เคย fail); grid ยังไม่กรอง sex
-   **AvatarFraming Presets:**
    -   Enum `FullBody / Bust / HeadIcon` ควบคุม scale + offset ของ `layerRoot`
    -   Art ชุดเดียว (canvas 1024×1536) ใช้ได้ทั้ง Character Creation, Dialogue Portrait, HUD Icon

**สถานะ:** ✅ v2.5 parts-only MVP — dictionary schema + hair 2-layer renderer + framing presets + per-slot draft/commit UI
ทำงานจริง (Randomize/validation ใช้ pose+sex แล้ว); ❌ Outfit Packages ถูกลบ (rollback 2026-09-03) — รอ Art ส่ง Sprite จริง

### 2. Sex / Gender System (Explicit State)
**ปัญหาเดิม:** เพศศิษย์ถูก imply จาก `rosterIndex % 2` ใน `CreateStarterAvatar` → Query ไม่ได้, บังคับเพศตอนรับสมัครไม่ได้

**สิ่งที่แก้:**
-   **เพิ่ม `DiscipleSex` Enum + Field:**
    ```csharp
    public enum DiscipleSex { Unspecified, Male, Female }
    [Key(7)] public DiscipleSex Sex { get; set; } = DiscipleSex.Unspecified;
    ```
    -   Append `[Key(7)]` หลัง `Avatar` → Backward compatible กับ save เก่า
-   **Recruitment Logic:**
    -   `RecruitOuterDisciple(DiscipleSex sex = Unspecified)` รับ optional param
    -   `CreateStarterAvatar(index, sex)` เลือก Head ตาม Sex จริง ไม่ใช่ index
-   **Mock Data:** ระบุ Sex ชัดเจน (d000=Male, d001=Female, d002=Female, d003=Male)
-   **MCP:** `get_sect_state` คืนค่า `Sex` เป็น integer (0/1/2) อัตโนมัติ

**สถานะ:** ✅ Data Model + Recruitment Logic เสร็จแล้ว (รอ UI Selector ใน Phase ถัดไป)

### 3. Xianxia.UI.MVP Lite (EventPopup + ResourceHud + AvatarCustomization)
**สิ่งที่ทำ:**
-   **Pattern:** View (MonoBehaviour) / Presenter (Plain C#, Transient) / Service (Singleton)
-   **Panel Resolution:** Explicit `UIPanelType` enum → Type mapping (ไม่ scan assembly)
-   **Bug Fix สำคัญ (Lab 13):**
    -   `EventPopupPresenter` เคย publish ผ่าน in-memory `IPublisher` แต่ `DecisionLogger` ฟังผ่าน interprocess `IDistributedSubscriber` → คนละ graph กัน กดปุ่มแล้วเงียบ
    -   **แก้:** ดึง logic ออกมาเป็น `DecisionExecutor.cs` ให้ทั้ง UI และ Bridge เรียกตรงๆ (Direct Method Call) แทน pub/sub สำหรับ in-process logic
-   **ResourceHud:** แทนที่ `SectHudView` เดิม, subscribe `SectResourceChangedMessage` (มี delta มาให้ในตัว) แทน polling
-   **AvatarCustomization:**
    -   Draft Pattern (Clone → Edit → Confirm/Rollback) → ไม่ยิง message ข้าม TCP ทุกคลิก
    -   Category Tabs (ใบหน้า/ลักษณะ/ร่างกาย) → slot tabs → part grid (ไม่มี Outfit Tab — rollback แล้ว)
    -   Object Pooling สำหรับปุ่มในกริด (กัน GC กระตุกตอนสลับแท็บ)
-   **UIRoot Layout Fix:**
    -   Root cause เดิม: `ContentSizeFitter=PreferredSize` ทับ anchor stretch → UI กองกลางจอ
    -   แก้: `UIRoot.Awake()` บังคับ stretch เต็ม Canvas + `ApplyLayout()` ตั้ง `Unconstrained` ที่ root panel

**สถานะ:** ✅ EventPopup, ResourceHud, AvatarCustomization ทำงานจริง (ยืนยันแล้ว 30 ส.ค. 2026)

---

## 📋 งานที่ทำไปแล้ว (Lab Rounds Summary)

### Lab 1-3: Foundation + Round Trip
-   `economy.proto` → `SectEconomyState.cs` (MessagePack stub)
-   `GameLifetimeScope.cs` (VContainer + MessagePipe TCP)
-   `TimeSystem.cs`, `McpBridgeProgram.cs`
-   **Result:** `get_sect_state` round trip ผ่านจริง (21 ส.ค. 2026)

### Lab 4-6: Write Path + World Events + Transport Bug
-   `ExecuteDecision` (Bridge → Unity) ผ่าน `IDistributedPublisher`
-   `WorldEventSystem` (สุ่ม event ทุก 15 วิ, auto-pause)
-   **Bug:** `IDistributedSubscriber` over TCP เปิด listen socket เสมอ → Bridge ชน port Unity
-   **Fix:** เปลี่ยน `await_next_world_event` เป็น Request-Response (`AwaitWorldEventRequest/Response`)
-   **Result:** Loop เต็มรูปแบบทำงานจริง (22 ส.ค. 2026)

### Lab 7: ExecuteDecision มีผลจริง + เลิก Protobuf
-   `SectStateProvider` ถือ live state instance เดียว (ไม่สร้างใหม่ทุก query)
-   `ApplyDecisionConsequence` ปรับ Stockpile จริง
-   **Decision:** เลิก Protobuf ถาวร → ใช้ MessagePack เป็นตัวจริง
-   **Result:** เห็นตัวเลขเปลี่ยนจริงหลัง `execute_decision`

### Lab 8-9: DiscipleSystem + Crafting
-   `RecruitOuterDisciple` (ผูกกับ event `new_disciple_applicant`)
-   `TickGathering` (passive resource, fractional accumulator)
-   `TickCrafting` (consume resources, produce items, ownership rule: Elder=Personal, Others=Stockpile)
-   **Gotcha:** Unity Editor `Run In Background` ต้องเปิด ไม่งั้น Tick หยุดตอนสลับหน้าต่าง

### Lab 10: Purchase Store
-   `purchase_item` MCP tool (Request-Response)
-   `TryPurchaseItem` (เช็คของ/เงิน → หัก → ย้ายเข้า PersonalInventory)
-   **Result:** ทดสอบผ่าน

### Lab 11-12: UI พื้นฐาน + Luban Pipeline
-   `SectHudView` (top bar HUD) → ต่อมาถูกแทนด้วย `ResourceHud`
-   **Luban:** ย้าย EventData จาก ScriptableObject → Excel → JSON → C# Plain Class
    -   Bug: `<module name="event">` ชน C# keyword → แก้เป็น `worldevent`
    -   Toolchain: `Tools/Luban/`, `DataTables/`, `gen.bat`

### Lab 13: Xianxia.UI.MVP Lite
-   Implement ตาม spec (`XIANXIA_UI_MVP_LITE_SPEC.md`)
-   Bug Fix: `DecisionExecutor` (แก้ channel ผิด), `UIRoot.ApplyLayout` (แก้ UI กองกลางจอ)
-   **Result:** EventPopup + ResourceHud ทำงานจริง (30 ส.ค. 2026)

### Lab 14: Avatar v2 Portrait Swap + Sex/Gender
-   Dictionary Schema, Hair 2-Layer, Framing Presets, Category Tabs
-   `DiscipleSex` enum + Recruitment Logic
-   **Result:** Data Model พร้อม, MCP คืนค่า Sex ถูกต้อง

### Lab 15: Avatar v3 Outfit Packages — implement แล้ว **rollback**
-   (ตอน v3): `OutfitDef` + `TryApplyOutfit()` + UI Tab "ชุดแต่งกาย" + filtered options by pose/sex + pose-conflict warning
-   **(rollback 2026-09-03):** ลบ `outfits[]`/`OutfitDef`/`TryApplyOutfit`/`GetOutfit(s)`/`AvatarOutfitChangedMessage`/tab "ชุดแต่งกาย"/pose-conflict warning ออกทั้งหมด
-   **คงไว้:** `PoseId`/`sexTag` บน part, `[Key(2)] PoseId` ใน state (fallback `pose_idle_01`), pose validation ใน `TryChangeAvatarPart`, `PoseId` ใน `BuildSignature`, Randomize กรอง pose/sex
-   **Result:** กลับเป็น parts-only MVP — เลือก part ทีละ slot อิสระ; เหตุผล: LLMWiki `wiki/sources/avatar-appearance.md` §1

### Lab 16 (Draft) — Additive Scene Architecture (ยังไม่ implement)
**Motivation:** เปลี่ยนจาก Single Scene เป็น Additive Scene เพื่อให้ UI persistent ข้าม scene

**Plan:**
1. สร้าง CoreScene (Canvas + UIRoot + GameLifetimeScope + Core systems)
2. สร้าง GameplayScene (Environment + NPCs + Scene-specific UI)
3. Implement SceneLoader (LoadSceneAsync Additive mode)
4. Persistent UI (ResourceHud, Settings) อยู่ใน CoreScene
5. Scene-specific UI (EventPopup, Dialogue) instantiate จาก Prefab

**Status:** Draft phase - ยังอยู่ในขั้นตอน document และ design
**Expected Impact:** 
- UI ไม่หายตอนเปลี่ยน scene
- แยก concerns: core vs scene content
- รองรับ multiple locations ในอนาคต

**Open Questions:**
- VContainer scope strategy (single root vs parent-child)
- Scene transition effects
- Scene-specific data persistence

---

## 🚧 สิ่งที่ยังค้าง / TODO

### High Priority (ต้องทำก่อน Art ส่งงาน)
1.  **Art Assets (Sprites):**
    -   สร้างไฟล์ sprite จริงตาม `avatar_parts.json` 19 ชิ้นใน `Resources/Avatar/` (base/body/head/hair/face_marking/accessory)
    -   สร้าง hair back layers (`hair_topknot_long_back.png`, `hair_twin_tail_back.png`)
2.  **ทดสอบ UI จริง:**
    -   กด Play → หน้าจอ Avatar Customization ควรโผล่
    -   ทดสอบเปลี่ยน part แต่ละ slot → Preview ควรอัปเดตทันที
    -   ทดสอบ Randomize: ศิษย์ male ต้องไม่สุ่มได้ `head_female_01` (กรองตาม `DiscipleState.Sex`)

### Medium Priority (Phase 2)
3.  **UI Sex Selector:**
    -   เพิ่ม Toggle ใน `AvatarCustomizationPanel` (เปลี่ยน Male↔Female)
    -   เพิ่ม Selector ใน `EventPopup` (ตอนรับสมัคร)
4.  **Tinting System (Color):**
    -   `AvatarAppearance.Colors` มีอยู่แล้ว แต่ยังไม่มี logic ใน Renderer
    -   เริ่มที่ `Image.color` multiply (Method A) ก่อน
5.  ~~**MCP Tool `apply_outfit`:**~~ — **ยกเลิกพร้อม outfit packages** (rollback 2026-09-03); ถ้ามี pose ที่ 2 เข้าโปรเจกต์ค่อยพิจารณาใหม่
6.  **MCP Tool `change_avatar_part`:**
    -   Wire เรียบร้อยฝั่ง Unity (`ChangeAvatarPartHandler`) แต่ Bridge ยังไม่มี tool expose

### Low Priority (Phase 3+)
7.  **AvatarIconBaker:**
    -   Bake RenderTexture cache สำหรับ HUD icon (กัน UGUI rebuild หนักเมื่อมีศิษย์เยอะ)
8.  **DialoguePanel:**
    -   Portrait (Bust framing) + Text box
    -   ใช้ `AvatarRenderer` ตัวเดิม
9.  **BuildingSystem / Combat / Stats:**
    -   ยังเป็น stub / ยังไม่ตัดสินใจแนวทาง
10. **Additive Scene Architecture:**
    -   แยก CoreScene (persistent UI/systems) + GameplayScene (additive load)
    -   เพื่อให้ UI ข้าม scene ได้โดยไม่ต้อง recreate

---

## 📂 Workspace Layout (Multi-project Monorepo)
```text
Cultivation Together/              ← workspace root
 ├── Shared/                        ← canonical source for shared types
 │   ├── GameMessages.cs            ← sync to Unity + Bridge
 │   ├── SectEconomyState.cs        ← Dictionary AvatarAppearance + DiscipleSex + PoseId
 │   └── MockSectData.cs            ← FromSlots factory + explicit Sex
 ├── UnityProject/                  ← open in Unity Hub
 │   ├── Assets/Scripts/
 │   │   ├── Core/                  ← TimeSystem, DecisionExecutor, GameLifetimeScope
 │   │   ├── Data/                  ← AvatarPartPool (parts-only def-table + poseId/sexTag), LubanEventPool
 │   │   ├── Systems/               ← SectStateProvider (TryChangeAvatarPart + pose validation), DiscipleSystem, etc.
 │   │   └── UI/                    ← Xianxia.UI.MVP Lite (Views/Presenters/Core)
 │   └── Resources/Data/            ← avatar_parts.json (parts only — outfits ถูกลบ), worldevent_*.json
 ├── McpBridge/                     ← .NET 8 console app
 │   └── Program.cs                 ← MCP server (SectQueryTools/SectActionTools)
 ├── DataTables/                    ← Luban Excel sources
 ├── Tools/Luban/                   ← Luban binary
 ├── LLMWiki/                       ← this folder (architecture.md, disciples.md, avatar-appearance.md, etc.)
 └── sync-shared.sh                 ← copy Shared/ → Unity + Bridge
```

**Rule:** แก้ไฟล์ใน `Shared/` → รัน `./sync-shared.sh` → ห้ามแก้ copy ใน Unity/Bridge โดยตรง

---

## 🔑 Key Architectural Decisions (Updated)

### Why Dictionary AvatarAppearance (not Fixed Properties)
-   Slot โตจาก 4 → 10+ → Fixed `[Key]` จะ break schema ทุกครั้งที่เพิ่มหมวด
-   Dictionary = เพิ่ม slot แก้แค่ JSON, AI GM ค้นพบ slot ได้เองจาก `get_sect_state`
-   `GetSlot/SetSlot` API เหมือนเดิม → Presenter ไม่ต้องแก้

### Why NOT Outfit Packages (v3 — REJECTED / rolled back 2026-09-03)
-   ตอนนี้ทุก part เป็น `pose_idle_01` เดียว → outfit = แค่ "กด `SetSlot` หลายครั้งรวดเดียว" = derive จาก `parts[]` ได้ 100% ไม่มีข้อมูลใหม่
-   `outfits[]` สร้าง dual source of truth (ลบ part ใน `parts[]` แต่ลืมลบใน `outfits[]` → runtime พัง) — ไม่คุ้มกับประโยชน์
-   เงื่อนไขรื้อฟื้น = **เมื่อมี pose ≥ 2 เข้าโปรเจกต์จริง** (ตอนนั้น pose validation/outfit ถึงจะมีค่าจริง)
-   คงไว้ (ต้นทุน ~0): `poseId`/`sexTag` บน part, `[Key(2)] PoseId` ใน state (`PoseId == ""` → fallback `pose_idle_01`), pose validation ใน `TryChangeAvatarPart`, Randomize กรอง pose/sex

### Why Portrait Swap (not Paper Doll Tiles)
-   Target visual: VN-style dialogue portrait (ไม่ใช่ chibi tile เล็กๆ)
-   Art วาดบน canvas/pose เดียวกัน → layer ซ้อนสนิทโดยไม่ต้องแก้โค้ด render
-   Hair 2-Layer (back/front) จำเป็นสำหรับผมยาว (ไม่งั้นวิกมุดหัว)

### Why Explicit DiscipleSex (not Implicit from Slot)
-   State ต้อง hold ids/flags ชัดเจน ไม่ซ่อน derivation (cryptic bug-wait)
-   Query ได้ตรงๆ ("is disciple X male?") ไม่ต้อง parse ชื่อไฟล์รูป
-   Source of truth สำหรับ recruitment flavor / gender-gated content ในอนาคต

### Why Direct Method Call for In-Process Logic (not Pub/Sub)
-   **Lesson (Lab 13):** `EventPopupPresenter` (in-memory pub) vs `DecisionLogger` (interprocess sub) → คนละ graph → เงียบกริบ
-   **Rule:** In-process logic ที่ต้อง trigger ระบบอื่นใน Unity → เรียก Direct Method (`DecisionExecutor`)
-   Pub/Sub ใช้เฉพาะ: (1) Cross-process (Bridge ↔ Unity) หรือ (2) Broadcast ที่ไม่มี Unity-side listener นอกจาก publisher เอง

### Why MessagePack (not Protobuf)
-   Bridge เป็น C# เหมือนกัน → ไม่ข้าม language boundary
-   MessagePack เร็วกว่า, ไม่ต้อง codegen pipeline
-   ตัดสินใจถาวรตั้งแต่ Lab 7

### Why Luban (not ScriptableObject)
-   Events/AvatarParts เป็น tabular data → Excel เหมาะกว่า
-   ไม่ต้องคลิกสร้าง `.asset` → แก้ด้วย text editor / git diff ได้
-   Generate C# Plain Class + JSON loader (ไม่ใช่ SO) → สอดคล้องกับแนวทาง "Plain C# everywhere"


**Motivation:** เปลี่ยนจาก Single Scene เป็น Additive Scene เพื่อให้ UI persistent ข้าม scene

**Plan:**
1. สร้าง CoreScene (Canvas + UIRoot + GameLifetimeScope + Core systems)
2. สร้าง GameplayScene (Environment + NPCs + Scene-specific UI)
3. Implement SceneLoader (LoadSceneAsync Additive mode)
4. Persistent UI (ResourceHud, Settings) อยู่ใน CoreScene
5. Scene-specific UI (EventPopup, Dialogue) instantiate จาก Prefab

**Status:** Draft phase - ยังอยู่ในขั้นตอน document และ design
**Expected Impact:** 
- UI ไม่หายตอนเปลี่ยน scene
- แยก concerns: core vs scene content
- รองรับ multiple locations ในอนาคต

**Open Questions:**
- VContainer scope strategy (single root vs parent-child)
- Scene transition effects
- Scene-specific data persistence

---

## 📝 Commit History (Recent)
-   `Unity-v0.11-implement-avatar-system-v3-portrait-swap`: OutfitDef + TryApplyOutfit, pose validation, outfit tab UI, PoseId in signature — **ถูก rollback กลับเป็น parts-only (working tree, 2026-09-03; ยังไม่ commit)**
-   `Unity-v0.10-sex-garden`: DiscipleSex enum, recruitment logic, mock data sex tagging
-   `Unity-v0.9-avatar-portrait-swap`: Dictionary AvatarAppearance, category tabs, hair 2-layer renderer, framing presets
-   `Unity-v0.8-log-window`: LogWindow event log with real-time notifications
-   `Unity-v0.7-ui-framework-and-luban-data`: MVP UI system + Luban migration
-   `Unity-v0.6-luban-event-data`: Replace ScriptableObject with Luban-generated data tables
-   `Unity-v0.5-data-driven-world-events`: EventData ScriptableObjects + weighted EventPool
-   `Unity-v0.4-economic-loop-mcp-transactions`: Purchase system, execute decision publisher, world event transport fix
-   `Unity-v0.3-live-crafting-pipeline`: Crafting system, decision consequences, singleton state mutation
-   `Unity-v0.2-live-decision-loop`: WorldEventSystem, await_next_world_event, execute_decision round trip