# Xianxia Sect Simulator — MCP-Enabled Game Framework
สรุปการออกแบบ + บันทึกความคืบหน้า (อัปเดตล่าสุด: Additive Scene Architecture implement เสร็จแล้ว (lab 18) + เพิ่ม Task System v2 — Viewer-Controlled Tasks design phase)

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
| IPC (Unity ↔ Bridge) | **MessagePipe.Interprocess (TCP)** | Unity เป็น host, Bridge เป็น client — **รองรับหลาย client พร้อมกัน** (McpBridge + ในอนาคต Twitch bot service) |
| MCP Bridge | **.NET 8 Console App** | ใช้ `ModelContextProtocol` C# SDK (stdio) |
| Serialization | **MessagePack** | เลิกใช้ protobuf แล้วถาวร (lab รอบ 7) |
| DataTable | **Luban** | Excel → JSON → C# Plain Class (ไม่ใช้ ScriptableObject เพื่อเลี่ยงการคลิกสร้าง Asset) |
| UI Framework | **Xianxia.UI.MVP Lite** | Custom UGUI + TMP (View=MonoBehaviour, Presenter=Plain C#) ทำงานร่วมกับ VContainer/MessagePipe โดยตรง ไม่พึ่ง Reflection |
| Scene Architecture | **Additive Scene (CoreScene + GameplayScene)** | ✅ implement แล้ว (lab 18) |

## หลักคิดกลางที่ใช้คุมทุกระบบ
> ทุกกลไกต้องเป็นสิ่งที่ AI ตัดสินใจได้จากข้อมูลที่ query ผ่าน MCP ได้จริง
> ไม่พึ่ง "สัญชาตญาณมนุษย์" ที่ AI เข้าไม่ถึง

> **หลักคิดใหม่ (จาก Task System v2):** เมื่อมีมากกว่าหนึ่ง "ผู้สั่งการ" ที่เป็นไปได้ (AI GM /
> ผู้เล่น / ผู้ชม Twitch) การตรวจสิทธิ์ต้องเกิด **ครั้งเดียว ฝั่งเดียว ที่ Unity** เสมอ — ไม่เชื่อ
> client ฝั่งไหนว่า "ส่งมาถูกต้องแล้ว" หลักเดียวกับที่ `TryPurchaseItem` ใช้ (atomic check-and-deduct
> ที่ single source of truth) ตอนนี้ถูกยกระดับเป็นหลักการทั่วไปสำหรับทุกฟีเจอร์ที่ viewer ควบคุมได้

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

### 3. Xianxia.UI.MVP Lite (EventPopup + ResourceHud + LogWindow + AvatarCustomization)
**สิ่งที่ทำ:**
-   **Pattern:** View (MonoBehaviour) / Presenter (Plain C#, Transient) / Service (Singleton)
-   **Panel Resolution:** Explicit `UIPanelType` enum → Type mapping (ไม่ scan assembly)
-   **Bug Fix สำคัญ (Lab 13):**
    -   `EventPopupPresenter` เคย publish ผ่าน in-memory `IPublisher` แต่ `DecisionLogger` ฟังผ่าน interprocess `IDistributedSubscriber` → คนละ graph กัน กดปุ่มแล้วเงียบ
    -   **แก้:** ดึง logic ออกมาเป็น `DecisionExecutor.cs` ให้ทั้ง UI และ Bridge เรียกตรงๆ (Direct Method Call) แทน pub/sub สำหรับ in-process logic
-   **ResourceHud:** แทนที่ `SectHudView` เดิม, subscribe `SectResourceChangedMessage` (มี delta มาให้ในตัว) แทน polling
-   **LogWindow (lab 14):** scrolling narrative log — subscribe เฉพาะ `DiscipleRecruitedMessage`/`WorldEventTriggeredMessage`/`DecisionExecutedMessage` (ไม่รวม resource ticks กันน้ำท่วมจอ)
-   **AvatarCustomization:**
    -   Draft Pattern (Clone → Edit → Confirm/Rollback) → ไม่ยิง message ข้าม TCP ทุกคลิก
    -   Category Tabs (ใบหน้า/ลักษณะ/ร่างกาย) → slot tabs → part grid (ไม่มี Outfit Tab — rollback แล้ว)
    -   Object Pooling สำหรับปุ่มในกริด (กัน GC กระตุกตอนสลับแท็บ)
-   **UIRoot Layout Fix:**
    -   Root cause เดิม: `ContentSizeFitter=PreferredSize` ทับ anchor stretch → UI กองกลางจอ
    -   แก้: `UIRoot.Awake()` บังคับ stretch เต็ม Canvas + `ApplyLayout()` ตั้ง `Unconstrained` ที่ root panel

**สถานะ:** ✅ EventPopup, ResourceHud, LogWindow, AvatarCustomization ทำงานจริง (ยืนยันแล้ว 30 ส.ค. – 1 ก.ย. 2026)

### 4. Additive Scene Architecture — ✅ Implement เสร็จแล้ว (lab 18, v0.12)
เดิมอยู่ในสถานะ Draft (ดูหัวข้อ "Lab 16 (Draft)" เวอร์ชันก่อนหน้าของเอกสารนี้) — ตอนนี้ implement จริงแล้ว:

-   **CoreScene** (Single, never unload): `GameLifetimeScope`, `TimeSystem`, `SectStateProvider`, MessagePipe bus,
    `SceneLoader` + persistent UI (Canvas, `UIRoot`, `ResourceHud`, `LogWindow`, `AvatarCustomization`) + `EventSystem`/`AudioListener` ตัวเดียว
-   **GameplayScene(s)** (Additive, unload/reload ได้): environment/terrain, NPC, buildings, scene-specific UI (`EventPopup`)
-   `SceneLoader` singleton — `LoadGameplayScene(name)` unload scene เดิมก่อนโหลดใหม่ (A→B swap), publish `SceneLoadedMessage`
-   `AdditiveSceneTest` — runtime GUI ทดสอบ Load/Unload/Swap
-   **VContainer strategy ที่เลือกใช้ตอนนี้**: Option A (single root scope ใน CoreScene) — parent-child scope (Option B) เลื่อนไปทำเมื่อมี scene-specific service จริง

**Gotcha ที่เจอ**: ทั้งสอง scene ต้องอยู่ใน Build Settings ก่อนถึงจะ async load ได้; GameplayScene ห้ามมี `EventSystem`/`AudioListener` ซ้ำ

**สถานะ:** ✅ implemented (v0.12) — ดูรายละเอียดที่ LLMWiki `concepts/additive-scene-architecture.md`

### 5. 🆕 Task System v2 — Viewer-Controlled Tasks + Ownership Model (Design Phase, ยังไม่ implement)

> เอกสารต้นฉบับ: `task-system-v2.md` — เป็นการ revise `task-system.md` (ฉบับที่แข็งแรงกว่าในสอง draft
> ที่ AI ช่วยร่างไว้) **ไม่ใช้ `TaskSystem_GDD.md` เป็นฐาน** เพราะฉบับนั้นอ้าง `[Key(8)]` สำหรับ
> `CurrentTask` ผิด (ของจริงคือ `[Key(5)]`) และเสนอแนวทางที่โปรเจกต์ปฏิเสธไปแล้ว (ScriptableObject-backed
> data — ขัดกับ decision ของ lab 12) เก็บไว้อ่านเป็น background เท่านั้น ไม่ใช่ source of truth

#### 5.1 เป้าหมายการออกแบบ (confirmed)

-   **โซโล่ / ไม่มีผู้ชม**: ผู้เล่น (Sect Master) สั่งเปลี่ยน task ของศิษย์ **คนไหนก็ได้** อิสระแบบ
    Rimworld/ACS — ไม่มีข้อจำกัด
-   **มีผู้ชมเข้าร่วมเป็นศิษย์**: ผู้ชมคนนั้นสั่งเปลี่ยน task ได้ **เฉพาะศิษย์ของตัวเอง** (ผ่าน Twitch
    chat command หรือ extension UI ในอนาคต) — ผู้เล่นยัง override ใครก็ได้เหมือนเดิม รวมถึงศิษย์ที่
    ผู้ชมเป็นเจ้าของ
-   **ตรวจสิทธิ์ครั้งเดียว ฝั่ง Unity เท่านั้น** — ไม่เชื่อ client ฝั่งไหน (AI GM / Twitch bot / UI ใน
    อนาคต) ว่าส่งคำขอมาถูกต้อง หลักการเดียวกับ atomic check-and-deduct ของ `TryPurchaseItem`: มี
    authority เดียว ไม่มี client ไหน "สันนิษฐานว่าตัวเองถูกต้อง"

#### 5.2 การเปลี่ยน Data Model

**`DiscipleState` — เพิ่ม field ความเป็นเจ้าของ:**

```csharp
public enum DiscipleOwnerType
{
    Npc,      // AI/ไม่มีเจ้าของ — Sect Master คุมโดย default
    Player,   // เจ้าของคือ Sect Master เอง (เผื่อโมเดล alt/avatar แยกในอนาคต)
    Viewer,   // ควบคุมโดยผู้ชม Twitch คนใดคนหนึ่ง
}
```

```csharp
// ⚠️ ต้องยืนยัน [Key(N)] ที่ว่างจริงกับ SectEconomyState.cs ปัจจุบันก่อนเขียนโค้ด —
// ล่าสุดที่ยืนยันแล้ว: Avatar = Key(6), Sex = Key(7) → ถ้าไม่มีอะไรถูกเพิ่มหลังจากนั้น
// ตัวถัดไปควรเป็น Key(8)/(9) แต่ต้องเช็คไฟล์จริงก่อนพิมพ์เลขลงโค้ด อย่าเดา
[Key(N)]   public DiscipleOwnerType OwnerType { get; set; } = DiscipleOwnerType.Npc;
[Key(N+1)] public string OwnerId { get; set; } = string.Empty; // Twitch user id เมื่อ OwnerType=Viewer, ไม่งั้น ""
```

`OwnerId` เป็น plain string ตาม convention เดียวกับ `ItemDefId`/`PartId` ที่อื่นในโค้ด — resolve/validate
โดยฝั่งที่เรียกเข้ามา (Twitch bot, AI GM) ไม่ใช่ Unity เอง Unity สนใจแค่ "OwnerId นี้ตรงกับผู้ขอไหม"

**`MockSectData.cs`**: ศิษย์เริ่มต้นทั้ง 4 คน default เป็น `OwnerType = Npc` (ค่า default ของ enum
อยู่แล้ว) — ไม่มีใคร viewer-owned ตั้งแต่ต้น ไม่ต้องแก้ constructor เพิ่ม

#### 5.3 `SectStateProvider.TryAssignTask` — พร้อม permission check

```csharp
// requesterId convention:
//   "SECT_MASTER"    → ผู้เล่น หรือ AI GM ที่ทำหน้าที่ Sect Master; ข้ามการเช็คความเป็นเจ้าของ
//                       ทั้งหมด สั่งใครก็ได้
//   string อื่นๆ      → ต้องตรงกับ disciple.OwnerId เป๊ะ ไม่งั้น reject
public bool TryAssignTask(string requesterId, string discipleId, string taskId, out string failReason)
{
    failReason = string.Empty;

    var disciple = _state.Disciples.FirstOrDefault(d => d.DiscipleId == discipleId);
    if (disciple == null)
    {
        failReason = $"No disciple with id '{discipleId}'.";
        return false;
    }

    if (requesterId != "SECT_MASTER" && disciple.OwnerId != requesterId)
    {
        failReason = "You don't have permission to reassign this disciple.";
        return false;
    }

    var taskDef = _taskPool.GetTask(taskId);
    if (taskDef == null)
    {
        failReason = $"Unknown task id '{taskId}'.";
        return false;
    }

    // ... existing requirement checks from task-system.md (rank, etc.) ...

    disciple.CurrentTask = taskId;

    _discipleTaskChangedPublisher.Publish(new DiscipleTaskChangedMessage
    {
        DiscipleId = discipleId,
        TaskId = taskId,
    });

    return true;
}
```

สิ่งที่เพิ่มจาก draft เดิม (`task-system.md`): พารามิเตอร์ `requesterId` เป็นตัวแรก + บล็อกตรวจ
ความเป็นเจ้าของ ทุก call site (MCP tool, Twitch bot, UI ในอนาคต) ต้องส่งมาว่า "ใครเป็นคนถาม" — ไม่มี
caller ไหน "ถูกไว้ก่อน" อีกต่อไป

#### 5.4 🐛 บั๊กที่แก้ก่อน implement — `TickCrafting` ต้องผ่าน `AdjustAndNotify` เสมอ

Draft เดิม (`task-system.md`) เวอร์ชัน refactor หัก resource ตรงๆ:

```csharp
// ❌ ห้ามทำแบบนี้ — ทำให้ HUD เงียบแบบไม่มี error
_state.Stockpile.RawResources[kvp.Key] -= (int)kvp.Value;
```

วิธีนี้ bypass `SectResourceChangedMessage` ที่ `ResourceHudPresenter` ต้องพึ่งสำหรับ live delta display
(ตั้งแต่ lab 13) ตัวเลขภายในยังถูกต้อง แต่ HUD จะไม่อัปเดตเฉพาะตอนที่ crafting กินทรัพยากร — เป็น
regression แบบเงียบ (ไม่ crash) ที่ ship ไปโดยไม่รู้ตัวได้ง่ายมาก

**วิธีแก้**: ให้การหัก stockpile ทุกจุดผ่าน helper `AdjustAndNotify(resources, key, delta)` ที่มีอยู่แล้ว
เหมือนที่ `TickGathering` และ `ApplyDecisionConsequence` ทำอยู่:

```csharp
// ✅ ถูกต้อง
foreach (var kvp in taskDef.ResourceCost)
{
    AdjustAndNotify(_state.Stockpile.RawResources, kvp.Key, -(int)kvp.Value);
}
```

#### 5.5 Threading — บทเรียนจาก `DecisionExecutor` ต้องใช้ซ้ำที่นี่

`AssignTaskRequest` จะถูกตอบโดย `AssignTaskHandler`
(`IAsyncRequestHandler<AssignTaskRequest, AssignTaskResponse>`) ที่มาผ่าน `MessagePipe.Interprocess` —
ยืนยันแล้วในโปรเจกต์นี้ว่า handler แบบนี้รันบน **TCP receive thread ไม่ใช่ main thread ของ Unity**

`TryAssignTask` เองแตะแค่ plain C# state (ปลอดภัยนอก main thread) ความเสี่ยงอยู่ที่ปลายทาง: ถ้า UI ไหน
(เช่น `TaskAssignmentPresenter` ในอนาคต) subscribe `DiscipleTaskChangedMessage` แล้วทำอะไรที่แตะ Unity
API — `Instantiate`, อนิเมท progress bar ผ่าน MonoBehaviour เมธอด ฯลฯ — จะพังด้วย exception เดียวกับ
`Internal_CloneSingleWithParent can only be called from the main thread` ที่เคยเจอกับ
`DecisionExecutor` มาแล้ว

**กฎต่อจากนี้**: request handler ใดๆ ที่มาจาก `MessagePipe.Interprocess` แล้วนำไปสู่โค้ดที่แตะ UI ต้อง
hop กลับ main thread ก่อน (`await UniTask.SwitchToMainThread();`) ก่อนรันโค้ด UI นั้น — pattern เดียวกับ
`DecisionExecutor.ExecuteAsync` ที่มีอยู่แล้ว ไม่ต้องรอเจอซ้ำอีกรอบ

#### 5.6 Twitch chat commands — ไม่ต้องสร้าง transport ใหม่

Unity host `MessagePipe.Interprocess` TCP endpoint เป็น server อยู่แล้ว (`HostAsServer = true`) และ TCP
server โดยธรรมชาติรับหลาย client พร้อมกันได้ — `McpBridge` ไม่จำเป็นต้องเป็น client ตัวเดียวที่ต่ออยู่

```
Twitch chat: "!cultivate"
        │
        ▼
Twitch integration service (แยก process — Node.js bot หรือ C# service เล็กๆ ที่ใช้
MessagePipe.Interprocess ไลบรารีเดียวกับที่ McpBridge ใช้อยู่แล้ว)
        │  ดูว่า Twitch user นี้เป็นเจ้าของ DiscipleId ตัวไหน
        ▼
AssignTaskRequest { RequesterId = twitchUserId, DiscipleId = ..., TaskId = "cultivation" }
        │  ส่งผ่าน TCP port เดียวกัน (127.0.0.1:3215) ที่ McpBridge ใช้อยู่
        ▼
Unity: AssignTaskHandler → SectStateProvider.TryAssignTask(requesterId, ...)
        │  ตรวจสิทธิ์ที่นี่ ครั้งเดียว ไม่ว่าจะมาจาก client ไหน
        ▼
Task ของศิษย์เปลี่ยนจริง (หรือได้เหตุผลที่ถูก reject กลับมา)
```

AI GM (ผ่าน MCP tool `assign_task`) กับ Twitch bot เป็นแค่ client คนละตัวที่เรียก request type เดียวกัน
ด้วยค่า `RequesterId` ต่างกัน — ไม่มี pipeline คู่ขนาน ไม่มี port ใหม่ ไม่มี message type ใหม่นอกจากที่
หัวข้อ 5.7 ต้องมี

**คำถามเปิด ไม่บล็อกตอนนี้**: Twitch integration service จะรู้ได้ยังไงว่า "Twitch user คนนี้เป็นเจ้าของ
DiscipleId ตัวไหน"? นั่นคือ flow การรับสมัคร/ผูกความเป็นเจ้าของ (ผู้ชมเข้าร่วม → กลายเป็นศิษย์ → ได้
`OwnerId` เขียนลง `DiscipleState`) — อยู่นอก scope เอกสารนี้ มาทีหลังพร้อมงาน Twitch integration จริง
ระหว่างนี้ตั้ง `OwnerId` มือได้ผ่าน `MockSectData.cs` หรือ debug tool เพื่อทดสอบ permission logic
แยกเดี่ยวไปก่อน

#### 5.7 Message เพิ่มใหม่

```csharp
[MessagePackObject]
public class AssignTaskRequest
{
    [Key(0)] public string RequesterId { get; set; } = string.Empty; // "SECT_MASTER" หรือ id ของผู้ชม
    [Key(1)] public string DiscipleId { get; set; } = string.Empty;
    [Key(2)] public string TaskId { get; set; } = string.Empty;
}

[MessagePackObject]
public class AssignTaskResponse
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string FailReason { get; set; } = string.Empty;
}

// In-memory only เหตุผลเดียวกับ DecisionExecutedMessage — ไม่มี bridge write tool subscribe
// ตัวนี้โดยตรง มีไว้ให้ UI ในเครื่องตอบสนอง "task เพิ่งเปลี่ยน" (progress bar refresh, disciple list update)
[MessagePackObject]
public class DiscipleTaskChangedMessage
{
    [Key(0)] public string DiscipleId { get; set; } = string.Empty;
    [Key(1)] public string TaskId { get; set; } = string.Empty;
}
```

ใช้ request/response ไม่ใช่ pub/sub — เหตุผลเดียวกับ `PurchaseItemRequest` และ `ExecuteDecisionMessage`:
ผู้เรียก (AI GM หรือ Twitch bot) ต้องรู้ทันทีว่าการสั่งงานสำเร็จจริงไหม ไม่ใช่แค่ fire-and-hope

#### 5.8 สิ่งที่ยังคงเดิมจาก `task-system.md` (ผ่านการรีวิวแล้ว ไม่แก้)

-   โครงสร้าง `TaskDef` (id, displayName, type, resourceCost, duration, resourceGain/craftResult)
-   รูปแบบ loader ของ `TaskPool` (id → def lookup) — **หมายเหตุ**: ตอนนี้ร่างไว้เป็น plain
    `JsonUtility` + `Resources.Load` ไม่ได้ผ่าน Luban pipeline แบบที่ `LubanEventPool`/`AvatarPartPool`
    ใช้ — เป็นจุดที่ไม่ consistent (task def เป็นข้อมูล tabular แบบเดียวกับที่ event/avatar-part ผ่าน
    Luban ไปแล้ว) ที่ควรพิจารณาอีกทีในอนาคต แต่ยังไม่ต้อง force รอบนี้ — flag ไว้ ไม่ block
-   UI Draft/Diff-Commit pattern สำหรับ panel มอบหมาย task
-   ส่วน "What NOT to Touch" ที่ scope ไว้เดิม
-   `TickGathering`/เมธอดอื่นของ `SectStateProvider` ที่ไม่เกี่ยวข้อง

#### 5.9 Implementation Checklist (อัปเดตล่าสุด)

-   [ ] ยืนยัน `[Key(N)]` ที่ว่างจริงบน `DiscipleState` กับ `SectEconomyState.cs` ตัวจริง (ไม่ใช่เลข
        placeholder ในเอกสารนี้)
-   [ ] เพิ่ม `DiscipleOwnerType` enum + field `OwnerType`/`OwnerId`
-   [ ] `TaskDef`/`TaskPool` (plain C# loader ตาม draft เดิม)
-   [ ] `SectStateProvider.TryAssignTask(requesterId, ...)` พร้อม ownership check —
        **ใช้ `AdjustAndNotify` กับทุกการหักทรัพยากร** ห้ามเขียน dictionary ตรงๆ
-   [ ] `AssignTaskRequest`/`Response`/`DiscipleTaskChangedMessage` ใน `GameMessages.cs`
-   [ ] `AssignTaskHandler` (`IAsyncRequestHandler`) + register ใน `GameLifetimeScope`
        (RPC pattern เดียวกับ `PurchaseItemHandler`)
-   [ ] MCP tool `assign_task` ใน `McpBridge/Program.cs`
        (`SectActionTools`, `requesterId` เป็น `"SECT_MASTER"` เสมอจาก path นี้ตอนนี้)
-   [ ] ใช้ `UniTask.SwitchToMainThread()` ในโค้ด UI ใดๆ ที่ตอบสนอง `DiscipleTaskChangedMessage`
        ถ้าแตะ Unity API
-   [ ] Test: สั่งงานผ่าน MCP tool ในนาม `SECT_MASTER` → ต้องสำเร็จเสมอ
-   [ ] Test: ตั้ง `OwnerId` ของศิษย์คนหนึ่งใน `MockSectData.cs` มือ แล้วเรียก `TryAssignTask` ด้วย
        `requesterId` ที่ไม่ตรง → ต้องถูก reject พร้อมเหตุผล
-   [ ] (ทีหลัง แยกงาน) Twitch integration service + flow ผูกความเป็นเจ้าของ viewer→disciple จริง

**สถานะ:** ⏳ Design phase — ยังไม่เริ่ม implement โค้ดจริง เอกสารผ่านการรีวิวและแก้บั๊ก 2 จุดจาก draft
แรกแล้ว (§5.4, §5.5) พร้อมเริ่มเขียนโค้ดตาม checklist ข้างบน

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

### Lab 14: LogWindow Event Log
-   `LogWindowView` (scrolling TMP log, FIFO eviction, auto-scroll) + `LogWindowPresenter`
    (subscribe เฉพาะ `DiscipleRecruitedMessage`/`WorldEventTriggeredMessage`/`DecisionExecutedMessage`
    — ตัด `SectResourceChangedMessage` ออกกันน้ำท่วมจอ)
-   `DecisionExecutedMessage` ถูก publish จาก `DecisionExecutor` (funnel เดียว ไม่ว่าจะมาจาก UI หรือ bridge)
-   `UIBootstrap` เปิด LogWindow ตอนเกมเริ่ม
-   **Result:** ใช้งานจริง (1 ก.ย. 2026)

### Lab 15: Avatar v2.5 Portrait Swap + Sex/Gender
-   Dictionary Schema, Hair 2-Layer, Framing Presets, Category Tabs
-   `DiscipleSex` enum + Recruitment Logic
-   **Result:** Data Model พร้อม, MCP คืนค่า Sex ถูกต้อง

### Lab 16: Avatar v3 Outfit Packages — implement แล้ว **rollback**
-   (ตอน v3): `OutfitDef` + `TryApplyOutfit()` + UI Tab "ชุดแต่งกาย" + filtered options by pose/sex + pose-conflict warning
-   **(rollback 2026-09-03):** ลบ `outfits[]`/`OutfitDef`/`TryApplyOutfit`/`GetOutfit(s)`/`AvatarOutfitChangedMessage`/tab "ชุดแต่งกาย"/pose-conflict warning ออกทั้งหมด
-   **คงไว้:** `PoseId`/`sexTag` บน part, `[Key(2)] PoseId` ใน state (fallback `pose_idle_01`), pose validation ใน `TryChangeAvatarPart`, `PoseId` ใน `BuildSignature`, Randomize กรอง pose/sex
-   **Result:** กลับเป็น parts-only MVP — เลือก part ทีละ slot อิสระ; เหตุผล: LLMWiki `wiki/sources/avatar-appearance.md` §1

### Lab 17: Sex/Gender + Unity-MCP setup
-   `MockSectData` founders ได้ sex ชัดเจน (d000 M, d001 F, d002 F, d003 M)
-   `AvatarPartPool.GetPartsForSlot(slot, poseId, sex)` — `OnRandomize` กรอง part ตาม sex + pose
-   Side work: Unity-MCP integration setup (`.zcode` skills), ร่าง additive scene architecture (→ lab 18)

### Lab 18: Additive Scene Architecture — ✅ Implemented (v0.12)
-   Single Scene → Additive Scene: **CoreScene** (persistent systems + UI, never unload) +
    **GameplayScene(s)** (additive, unload/reload ได้)
-   `SceneLoader` singleton — `LoadGameplayScene(name)` unload-then-load (A→B swap), publish
    `SceneLoadedMessage`; registered ใน `GameLifetimeScope`
-   `AdditiveSceneTest` — runtime GUI ทดสอบ Load/Unload/Swap
-   VContainer: เลือก single root scope ใน CoreScene ไปก่อน (parent-child Option B เลื่อนไปทำเมื่อมี
    scene-specific service จริง)
-   **Gotchas**: ทั้งสอง scene ต้องอยู่ใน Build Settings ก่อนถึงจะ async load ได้; GameplayScene ห้ามมี
    `EventSystem`/`AudioListener` ซ้ำ
-   **Result:** เสร็จสมบูรณ์ — เอกสารสถานะเดิมของหัวข้อนี้ (เคยเขียนว่า "Draft phase") **ล้าสมัยแล้ว**
    อัปเดตในฉบับนี้ให้ตรงกับโค้ดจริง

### Lab 19 (Design, ยังไม่เขียนโค้ด): Task System v2 — Viewer-Controlled Tasks
-   ดูรายละเอียดเต็มที่หัวข้อ "🆕 ความคืบหน้าล่าสุด (รอบนี้) § 5" ด้านบน
-   สรุปสั้น: เพิ่ม `DiscipleOwnerType`/`OwnerId` บน `DiscipleState`, `TryAssignTask(requesterId, ...)`
    ตรวจสิทธิ์ฝั่ง Unity ครั้งเดียว, ใช้ TCP interprocess เดิม (ไม่สร้าง transport ใหม่) ให้ Twitch bot
    service ต่อเข้ามาเป็น client เพิ่มอีกตัว, แก้บั๊ก `TickCrafting` ที่ bypass `AdjustAndNotify`, และ
    ย้ำกฎ threading (`UniTask.SwitchToMainThread()`) สำหรับ handler ที่มาจาก interprocess แล้วโยงไปแตะ UI
-   **Status:** เอกสารออกแบบเสร็จ ผ่านการรีวิวบั๊ก 2 จุดแล้ว — ยังไม่เริ่ม implementation checklist

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
7.  **🆕 Task System v2 — เริ่ม implement ตาม checklist ในหัวข้อ § 5.9:**
    -   ยืนยัน `[Key(N)]` ว่างจริงก่อน แล้วเพิ่ม `DiscipleOwnerType`/`OwnerId`
    -   เขียน `TaskDef`/`TaskPool`, `TryAssignTask(requesterId, ...)` (**ใช้ `AdjustAndNotify` เสมอ**),
        `AssignTaskRequest/Response`, `AssignTaskHandler`, MCP tool `assign_task`
    -   ระวัง threading — ใส่ `UniTask.SwitchToMainThread()` ในทุก UI ที่ subscribe
        `DiscipleTaskChangedMessage`
    -   Twitch integration service + flow ผูก viewer→disciple ownership เป็นงานแยกทีหลัง (ไม่ block
        การ implement ส่วน permission/task core)

### Low Priority (Phase 3+)
8.  **AvatarIconBaker:**
    -   Bake RenderTexture cache สำหรับ HUD icon (กัน UGUI rebuild หนักเมื่อมีศิษย์เยอะ)
9.  **DialoguePanel:**
    -   Portrait (Bust framing) + Text box
    -   ใช้ `AvatarRenderer` ตัวเดิม
10. **BuildingSystem / Combat / Stats:**
    -   ยังเป็น stub / ยังไม่ตัดสินใจแนวทาง
11. **Twitch Extension เต็มรูปแบบ**:
    -   ต่อยอดจาก Task System v2 — UI ฝั่งผู้ชม, flow ผูกความเป็นเจ้าของศิษย์, Twitch bot service จริง

---

## 📂 Workspace Layout (Multi-project Monorepo)
```text
Cultivation Together/              ← workspace root
 ├── Shared/                        ← canonical source for shared types
 │   ├── GameMessages.cs            ← sync to Unity + Bridge (+ AssignTaskRequest/Response,
 │   │                                 DiscipleTaskChangedMessage เมื่อ Task System v2 เริ่ม implement)
 │   ├── SectEconomyState.cs        ← Dictionary AvatarAppearance + DiscipleSex + PoseId
 │   │                                 (+ DiscipleOwnerType/OwnerId เมื่อ Task System v2 เริ่ม implement)
 │   └── MockSectData.cs            ← FromSlots factory + explicit Sex
 ├── UnityProject/                  ← open in Unity Hub
 │   ├── Assets/Scripts/
 │   │   ├── Core/                  ← TimeSystem, DecisionExecutor, GameLifetimeScope, SceneLoader
 │   │   ├── Data/                  ← AvatarPartPool (parts-only def-table + poseId/sexTag), LubanEventPool,
 │   │   │                             (TaskPool — planned, Task System v2)
 │   │   ├── Systems/               ← SectStateProvider (TryChangeAvatarPart + pose validation,
 │   │   │                             TryAssignTask — planned), DiscipleSystem, etc.
 │   │   └── UI/                    ← Xianxia.UI.MVP Lite (Views/Presenters/Core)
 │   └── Resources/Data/            ← avatar_parts.json (parts only — outfits ถูกลบ), worldevent_*.json
 ├── McpBridge/                     ← .NET 8 console app
 │   └── Program.cs                 ← MCP server (SectQueryTools/SectActionTools, + assign_task — planned)
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

### Why Additive Scene Architecture (implemented lab 18)
-   UI persistent ข้าม scene (ไม่ต้อง recreate ทุกรอบ)
-   แยก concerns: core systems (TimeSystem, SectStateProvider, MessagePipe) vs scene content
    (environment/NPC/buildings)
-   รองรับ multiple biomes/locations ในอนาคตโดยไม่กระทบ persistent UI/state
-   VContainer: เลือก single root scope ก่อน (ง่ายที่สุด) แทน parent-child scope จนกว่าจะมี
    scene-specific service ที่จำเป็นจริง

### 🆕 Why Permission Check Happens Once, Server-Side, in Unity (Task System v2)
-   มีมากกว่าหนึ่ง client ที่อาจเรียกสั่งงานได้ (AI GM ผ่าน MCP, Twitch bot ในอนาคต, ผู้เล่นผ่าน UI) —
    ถ้าให้แต่ละ client เช็คสิทธิ์เอง จะมีจุดโกง/บั๊กได้หลายจุด
-   ใช้หลักการเดียวกับ `TryPurchaseItem`: validate ทั้งหมด → mutate ทั้งหมด → return, ไม่มี partial state
-   `requesterId` เป็นพารามิเตอร์บังคับของทุก mutation ที่ viewer อาจเรียกได้ ("SECT_MASTER" = ข้าม
    check, อย่างอื่น = ต้องตรง `OwnerId`) — ไม่มี "trusted caller" โดย default

### 🆕 Why Twitch Integration Reuses the Existing TCP Interprocess Server (not a New Pipeline)
-   Unity เป็น TCP server (`HostAsServer = true`) อยู่แล้ว และ TCP server รับหลาย client ได้โดย
    ธรรมชาติ — ไม่จำเป็นต้องผูกกับ `McpBridge` แค่ตัวเดียว
-   Twitch bot service เป็นแค่ client อีกตัวที่พูด `MessagePipe.Interprocess` โปรโตคอลเดียวกัน ส่ง
    request type เดียวกัน (`AssignTaskRequest`) ต่างแค่ค่า `RequesterId`
-   หลีกเลี่ยงการดูแล transport คู่ขนาน (port ใหม่, message type ใหม่, serialization แยก) ที่ไม่จำเป็น
