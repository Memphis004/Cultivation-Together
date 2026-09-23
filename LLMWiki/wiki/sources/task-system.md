title: Task System v1 — Disciple Work Assignment
type: gdd
status: draft v1
sources:
UnityProject/Assets/Scripts/Shared/SectEconomyState.cs
UnityProject/Assets/Scripts/Systems/SectStateProvider.cs
UnityProject/Assets/Scripts/Shared/MockSectData.cs
UnityProject/Assets/Scripts/Shared/GameMessages.cs
UnityProject/Assets/Scripts/Core/GameLifetimeScope.cs
UnityProject/Resources/Data/task_defs.json
related:
"[[entities/disciples]]"
"[[concepts/gathering-system]]"
"[[concepts/crafting-system]]"
"[[concepts/state-management]]"
"[[concepts/mvp-ui]]"
"[[concepts/mcp-bridge]]"
created: 2026-09-04
updated: 2026-09-04
confidence: high
tags: [task, disciple, gathering, crafting, assignment, work, mcp]

# Task System v1 — Disciple Work Assignment

ระบบมอบหมายงานให้ศิษย์ — ผู้เล่น/AI GM สามารถเปลี่ยน `CurrentTask` ของศิษย์ได้
แทนที่จะ stuck กับ task เดิมตลอดเกม (สถานะปัจจุบัน: round-robin ตอน recruit แล้วเปลี่ยนไม่ได้)

## 0. สถานะปัจจุบัน (จากโค้ดจริง)

### สิ่งที่มีอยู่แล้ว:
```csharp
// SectEconomyState.cs
[Key(5)] public string CurrentTask { get; set; }  // string เช่น "gathering_herb"

// SectStateProvider.cs
public void TickGathering(float dt) { ... }   // เช็ค CurrentTask == "gathering_*"
public void TickCrafting(float dt) { ... }    // เช็ค CurrentTask == "refining_elixir" / "forging_artifact"

// MockSectData.cs
CurrentTask = "gathering_herb"  // hardcode ตอนสร้างศิษย์
```

### สิ่งที่ขาด:
1.  **Task Definition Table** — ไม่มี `TaskDef` ที่บอกว่า task ไหนใช้ทรัพยากรอะไร/ได้什么/ใช้เวลาเท่าไหร่
2.  **Task Assignment Logic** — ไม่มี `TryAssignTask(discipleId, taskId)` ที่ validate + mutate
3.  **Task Pool / Catalog** — ไม่มี list ของ task ที่มีให้เลือก (ตอนนี้ hardcode ใน `TickGathering`/`TickCrafting`)
4.  **UI สำหรับเปลี่ยน Task** — ไม่มี panel ให้ผู้เล่น/AI สั่งเปลี่ยน task
5.  **MCP Tool** — ไม่มี `assign_task` tool ให้ AI GM เรียก

## 1. ทำไมต้องทำ Task System ตอนนี้

### ปัญหาของสถานะปัจจุบัน:
-   ศิษย์ถูก assign task ตอน recruit (round-robin) แล้ว **เปลี่ยนไม่ได้เลย**
-   ผู้เล่น/AI GM สั่งให้ "Su Yan หยุดต้มยา ไปเก็บสมุนไพรแทน" → **ทำไม่ได้**
-   ศิษย์ทุกคน stuck กับ task เดิมตลอดเกม → ไม่มี strategic depth
-   AI GM ไม่สามารถ "บริหารกำลังคน" ได้จริง → ขัดกับหลักคิดกลาง ("AI ต้องตัดสินใจได้จากข้อมูล")

### ทำไมถึงสำคัญ:
-   เป็น prerequisite ของ Building System (ตำแหน่งงานปลดล็อกจากอาคาร)
-   เป็น prerequisite ของ Technique Learning (ศิษย์ต้อง "ว่าง" หรือ "ฝึกวิชา" แทนทำงาน)
-   เป็น prerequisite ของ Combat/Mission (ส่งศิษย์ไปทำภารกิจ)
-   ทำให้ gameplay loop มี strategic depth จริง

## 2. Data Model

### 2.1 TaskDef (JSON table)
```json
{
  "tasks": [
    {
      "id": "idle",
      "displayName": "พักผ่อน",
      "type": "idle",
      "resourceInput": {},
      "resourceOutput": {},
      "craftTime": 0,
      "craftResult": null
    },
    {
      "id": "gathering_herb",
      "displayName": "เก็บสมุนไพร",
      "type": "gathering",
      "resourceInput": {},
      "resourceOutput": { "herb": 0.2 },
      "craftTime": 0,
      "craftResult": null
    },
    {
      "id": "gathering_wood",
      "displayName": "ตัดไม้",
      "type": "gathering",
      "resourceInput": {},
      "resourceOutput": { "wood": 0.2 },
      "craftTime": 0,
      "craftResult": null
    },
    {
      "id": "gathering_ore",
      "displayName": "ขุดแร่",
      "type": "gathering",
      "resourceInput": {},
      "resourceOutput": { "ore": 0.15 },
      "craftTime": 0,
      "craftResult": null
    },
    {
      "id": "gathering_provisions",
      "displayName": "หาเสบียง",
      "type": "gathering",
      "resourceInput": {},
      "resourceOutput": { "provisions": 0.25 },
      "craftTime": 0,
      "craftResult": null
    },
    {
      "id": "refining_elixir",
      "displayName": "หลอมยาอายุวัฒนะ",
      "type": "crafting",
      "resourceInput": { "herb": 10 },
      "resourceOutput": {},
      "craftTime": 20,
      "craftResult": { "itemDefId": "elixir_qi_gathering", "grade": 3 }
    },
    {
      "id": "forging_artifact",
      "displayName": "ตีอาวุธ",
      "type": "crafting",
      "resourceInput": { "ore": 15, "wood": 10 },
      "resourceOutput": {},
      "craftTime": 30,
      "craftResult": { "itemDefId": "sword_azure_flame", "grade": 5 }
    }
  ]
}
```

### 2.2 TaskDef struct (C#)
```csharp
[System.Serializable]
public class TaskDef
{
    public string id;
    public string displayName;
    public string type;  // "idle" / "gathering" / "crafting"
    public Dictionary<string, float> resourceInput;   // crafting ใช้
    public Dictionary<string, float> resourceOutput;  // gathering ใช้ (ต่อวินาที)
    public float craftTime;  // seconds
    public CraftResult craftResult;  // nullable
}

[System.Serializable]
public class CraftResult
{
    public string itemDefId;
    public int grade;
}
```

### 2.3 TaskPool (plain C# loader)
```csharp
public class TaskPool
{
    private const string ResourcePath = "Data/task_defs";
    private readonly Dictionary<string, TaskDef> _byId = new();
    
    public bool IsLoaded { get; private set; }
    
    public TaskPool() { Load(); }
    
    private void Load()
    {
        var asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null) { Debug.LogError($"[TaskPool] Could not find Resources/{ResourcePath}.json"); return; }
        
        TaskDefTable table;
        try { table = JsonUtility.FromJson<TaskDefTable>(asset.text); }
        catch (System.Exception ex) { Debug.LogError($"[TaskPool] JSON parse failed: {ex.Message}"); return; }
        
        if (table?.tasks != null)
        {
            foreach (var t in table.tasks)
            {
                if (!string.IsNullOrEmpty(t.id))
                    _byId[t.id] = t;
            }
        }
        IsLoaded = true;
        Debug.Log($"[TaskPool] Loaded {_byId.Count} tasks");
    }
    
    public TaskDef GetById(string taskId) => 
        string.IsNullOrEmpty(taskId) ? null : _byId.TryGetValue(taskId, out var def) ? def : null;
    
    public IReadOnlyList<TaskDef> GetAll() => _byId.Values.ToList();
    
    public IReadOnlyList<TaskDef> GetByType(string type) => 
        _byId.Values.Where(t => t.type == type).ToList();
}

[System.Serializable]
public class TaskDefTable
{
    public List<TaskDef> tasks = new();
}
```

### 2.4 DiscipleState — ไม่แก้ schema
`CurrentTask [Key(5)]` ยังเป็น `string` เหมือนเดิม — ไม่ต้องแก้ MessagePack schema
เพราะ task ID เป็น string อยู่แล้ว (เหมือน `AvatarAppearance.Parts` ที่เป็น dictionary)

## 3. Runtime Logic

### 3.1 TryAssignTask (SectStateProvider)
```csharp
public bool TryAssignTask(string discipleId, string taskId, out string failReason)
{
    failReason = string.Empty;
    
    var disciple = _state.Disciples.FirstOrDefault(d => d.DiscipleId == discipleId);
    if (disciple == null) { failReason = $"ไม่พบศิษย์ {discipleId}"; return false; }
    
    var taskDef = _taskPool.GetById(taskId);
    if (taskDef == null) { failReason = $"ไม่พบ task {taskId}"; return false; }
    
    var oldTask = disciple.CurrentTask;
    disciple.CurrentTask = taskId;
    
    _taskChangedPublisher.Publish(new DiscipleTaskChangedMessage
    {
        DiscipleId = discipleId,
        OldTask = oldTask,
        NewTask = taskId
    });
    
    Debug.Log($"[SectStateProvider] Assigned task '{taskId}' to {disciple.DisplayName} (was '{oldTask}')");
    return true;
}
```

### 3.2 Refactor TickGathering (อ่านจาก TaskDef)
```csharp
public void TickGathering(float dt)
{
    foreach (var disciple in _state.Disciples)
    {
        var taskDef = _taskPool.GetById(disciple.CurrentTask);
        if (taskDef == null || taskDef.type != "gathering") continue;
        
        foreach (var kvp in taskDef.resourceOutput)
        {
            float amount = kvp.Value * dt;
            // fractional accumulator (กันเสียเศษระหว่าง tick)
            _gatheringAccumulator[kvp.Key] += amount;
            int wholeAmount = (int)_gatheringAccumulator[kvp.Key];
            if (wholeAmount > 0)
            {
                _state.Stockpile.RawResources[kvp.Key] += wholeAmount;
                _gatheringAccumulator[kvp.Key] -= wholeAmount;
                
                _resourceChangedPublisher.Publish(new SectResourceChangedMessage
                {
                    ResourceId = kvp.Key,
                    Delta = wholeAmount,
                    NewTotal = _state.Stockpile.RawResources[kvp.Key]
                });
            }
        }
    }
}
```

### 3.3 Refactor TickCrafting (อ่านจาก TaskDef)
```csharp
public void TickCrafting(float dt)
{
    foreach (var disciple in _state.Disciples)
    {
        var taskDef = _taskPool.GetById(disciple.CurrentTask);
        if (taskDef == null || taskDef.type != "crafting") continue;
        
        // เช็คทรัพยากรพอไหม
        bool hasResources = true;
        foreach (var kvp in taskDef.resourceInput)
        {
            if (!_state.Stockpile.RawResources.TryGetValue(kvp.Key, out int available) || available < kvp.Value)
            {
                hasResources = false;
                break;
            }
        }
        if (!hasResources) continue;  // รอทรัพยากรพอ
        
        // นับเวลาคราฟท์
        if (!_craftingProgress.TryGetValue(disciple.DiscipleId, out float progress))
            progress = 0f;
        
        progress += dt;
        if (progress >= taskDef.craftTime)
        {
            // หักทรัพยากร
            foreach (var kvp in taskDef.resourceInput)
            {
                _state.Stockpile.RawResources[kvp.Key] -= (int)kvp.Value;
            }
            
            // เพิ่มผลผลิต
            var result = taskDef.craftResult;
            var item = new InventoryItem
            {
                ItemDefId = result.itemDefId,
                Quantity = 1,
                Grade = result.grade,
                OwnerScope = disciple.Rank >= DiscipleRank.Elder ? OwnerScope.Personal : OwnerScope.SectStockpile
            };
            
            if (item.OwnerScope == OwnerScope.Personal)
            {
                disciple.PersonalInventory.Add(item);
            }
            else
            {
                // merge stack ถ้ามี item เดิมเกรดเดียวกัน
                var existing = _state.Stockpile.CraftedGoods.FirstOrDefault(i => 
                    i.ItemDefId == item.ItemDefId && i.Grade == item.Grade);
                if (existing != null)
                    existing.Quantity++;
                else
                    _state.Stockpile.CraftedGoods.Add(item);
            }
            
            progress = 0f;  // reset
            Debug.Log($"[SectStateProvider] {disciple.DisplayName} crafted {result.itemDefId} (grade {result.grade})");
        }
        
        _craftingProgress[disciple.DiscipleId] = progress;
    }
}
```

## 4. Messages

### 4.1 DiscipleTaskChangedMessage
```csharp
[MessagePackObject]
public class DiscipleTaskChangedMessage
{
    [Key(0)] public string DiscipleId { get; set; } = string.Empty;
    [Key(1)] public string OldTask { get; set; } = string.Empty;
    [Key(2)] public string NewTask { get; set; } = string.Empty;
}
```

### 4.2 AssignTaskRequest / Response (MCP)
```csharp
[MessagePackObject]
public class AssignTaskRequest
{
    [Key(0)] public string DiscipleId { get; set; } = string.Empty;
    [Key(1)] public string TaskId { get; set; } = string.Empty;
}

[MessagePackObject]
public class AssignTaskResponse
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string FailReason { get; set; } = string.Empty;
}
```

## 5. UI — TaskAssignmentPanel

### 5.1 Layout
```
┌─────────────────────────────────────────────┐
│  มอบหมายงานให้ศิษย์                          │
├─────────────────────────────────────────────┤
│  [รายชื่อศิษย์]        [เลือกงาน]           │
│  ┌──────────────┐      ┌────────────────┐  │
│  │ d001 Lin Feng│      │ ☐ พักผ่อน      │  │
│  │ d002 Su Yan  │      │ ☑ เก็บสมุนไพร  │  │
│  │ d003 Elder   │      │ ☐ ตัดไม้       │  │
│  └──────────────┘      │ ☐ ขุดแร่       │  │
│                        │ ☐ หลอมยา       │  │
│                        │ ☐ ตีอาวุธ      │  │
│                        └────────────────┘  │
├─────────────────────────────────────────────┤
│         [ยกเลิก]  [ยืนยัน]                   │
└─────────────────────────────────────────────┘
```

### 5.2 Presenter Pattern (Draft / Diff-Commit)
```csharp
public class TaskAssignmentPresenter : UIPresenter<TaskAssignmentView>
{
    private readonly ISectStateProvider _stateProvider;
    private readonly TaskPool _taskPool;
    private readonly ISubscriber<DiscipleTaskChangedMessage> _taskChangedSub;
    
    private Dictionary<string, string> _originalTasks;  // discipleId → taskId
    private Dictionary<string, string> _draftTasks;     // discipleId → taskId (ร่าง)
    
    public override void OnOpen(object args)
    {
        var state = _stateProvider.BuildSectEconomyState();
        _originalTasks = state.Disciples.ToDictionary(d => d.DiscipleId, d => d.CurrentTask);
        _draftTasks = new Dictionary<string, string>(_originalTasks);
        
        RefreshAll();
    }
    
    private void OnConfirm()
    {
        // diff-commit: เรียก TryAssignTask เฉพาะศิษย์ที่เปลี่ยน task
        foreach (var kvp in _draftTasks)
        {
            if (kvp.Value != _originalTasks[kvp.Key])
            {
                _stateProvider.TryAssignTask(kvp.Key, kvp.Value, out _);
            }
        }
        RequestClose();
    }
}
```

## 6. MCP Exposure

### 6.1 assign_task tool (McpBridge)
```csharp
// McpBridge/Program.cs
[McpServerTool(Name = "assign_task")]
public async Task<AssignTaskResponse> AssignTask(AssignTaskRequest request)
{
    var response = await _unityClient.SendRequestAsync<AssignTaskRequest, AssignTaskResponse>(request);
    return response;
}
```

### 6.2 DI Registration
```csharp
// GameLifetimeScope.cs
builder.Register<TaskPool>(Lifetime.Singleton);
builder.Register<Xianxia.Sect.UI.TaskAssignmentPresenter>(Lifetime.Transient);

// Request/response
messagePipeBuilder.RegisterTcpRemoteRequestHandler<AssignTaskRequest, AssignTaskResponse>(interprocess);
builder.RegisterAsyncRequestHandler<AssignTaskRequest, AssignTaskResponse, AssignTaskHandler>(options);

// Pub/sub
messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, DiscipleTaskChangedMessage>(interprocess);
```

## 7. Implementation Plan (สำหรับ AI Agent)

### Tasks (ทำตามลำดับ):
1.  สร้าง `task_defs.json` ใน `Resources/Data/`
2.  สร้าง `TaskDef` + `TaskPool` ใน `Assets/Scripts/Data/TaskPool.cs`
3.  เพิ่ม `TryAssignTask` ใน `SectStateProvider.cs` + interface
4.  Refactor `TickGathering` / `TickCrafting` ให้อ่านจาก `TaskDef`
5.  เพิ่ม `DiscipleTaskChangedMessage` + `AssignTaskRequest/Response` ใน `GameMessages.cs`
6.  สร้าง `TaskAssignmentPresenter` + `TaskAssignmentView` (UI)
7.  Register DI ใน `GameLifetimeScope.cs`
8.  เพิ่ม `assign_task` tool ใน `McpBridge/Program.cs`
9.  Sync shared files + ทดสอบ

### Acceptance Criteria:
-   [ ] `task_defs.json` โหลดได้ถูกต้อง
-   [ ] `TryAssignTask` เปลี่ยน `CurrentTask` ได้
-   [ ] `TickGathering` / `TickCrafting` อ่านจาก `TaskDef` แทน hardcode
-   [ ] UI แสดง list ศิษย์ + เลือก task ใหม่ได้
-   [ ] MCP tool `assign_task` ทำงานได้
-   [ ] `get_sect_state` เห็น `CurrentTask` ที่เปลี่ยนแล้ว

### Files to Create/Modify:
-   **Create:** `Resources/Data/task_defs.json`, `Assets/Scripts/Data/TaskPool.cs`, `Assets/Scripts/UI/Presenters/TaskAssignmentPresenter.cs`, `Assets/Scripts/UI/Views/TaskAssignmentView.cs`, `Assets/Scripts/Core/AssignTaskHandler.cs`
-   **Modify:** `SectStateProvider.cs`, `GameMessages.cs`, `GameLifetimeScope.cs`, `McpBridge/Program.cs`

### What NOT to Touch:
-   `DiscipleState.CurrentTask [Key(5)]` — ยังเป็น string เหมือนเดิม
-   `AvatarAppearance` / Avatar system — ไม่เกี่ยว
-   `WorldEventSystem` / EventPopup — ไม่เกี่ยว

## 8. Roadmap (Phase 2+)

| # | งาน | สถานะ |
|---|-----|-------|
| 1 | Task priority / queue (ศิษย์ทำได้ทีละหลาย task) | ⏳ Phase 2 |
| 2 | Task efficiency / skill bonus (stat system) | ⏳ Phase 3 |
| 3 | Auto-assignment AI (AI GM สั่งเอง) | ⏳ Phase 2 |
| 4 | Task unlock จาก building (BuildingSystem) | ⏳ Phase 3 |
| 5 | Task progress bar ใน UI | ⏳ Phase 2 |

## Related Pages

[[entities/disciples]] — เจ้าของ CurrentTask field
[[concepts/gathering-system]] — TickGathering logic
[[concepts/crafting-system]] — TickCrafting logic
[[concepts/state-management]] — MessagePack field placement
[[concepts/mvp-ui]] — TaskAssignmentPanel pattern
[[concepts/mcp-bridge]] — assign_task tool