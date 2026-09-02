แนะนำว่า **ร่างเอกสารก่อน implement** ครับ เพราะนี่คือ architecture change ครั้งใหญ่ที่มีผลกระทบต่อหลายระบบ และสอดคล้องกับ pattern ที่โปรเจกต์นี้ทำมาตลอด (Avatar, Sex/Gender ก็เริ่มจากเอกสารก่อน)

## เหตุผลที่ควรทำเอกสารก่อน

1. **ต้องตัดสินใจหลายจุดสำคัญ:**
   - อะไรอยู่ใน CoreScene vs GameplayScene
   - VContainer scope จะจัดยังไง (single root vs parent-child)
   - Scene-specific systems จะ register ยังไง
   - Backward compatibility กับ scene เดิม

2. **ลดความเสี่ยง:** Implement แล้วรื้อมี cost สูงกว่า document แล้ว adjust

3. **เป็น reference สำหรับอนาคต:** เมื่อมี scene เพิ่มขึ้น (dungeon, town, sect) จะมี pattern ให้ follow

## โครงสร้างเอกสารที่แนะนำ

ผมเสนอให้สร้าง 2 เอกสาร:

### 1. `LLMWiki/concepts/additive-scene-architecture.md` (เอกสารหลัก)

```markdown
---
title: Additive Scene Architecture
type: concept
status: draft
created: 2026-09-02
related:
  - "[[architecture]]"
  - "[[vcontainer-composition]]"
  - "[[mvp-ui]]"
tags: [scene-management, additive-loading, core-scene, architecture]
---

# Additive Scene Architecture

## Overview
เปลี่ยนจาก Single Scene เป็น Additive Scene pattern:
- **CoreScene**: โหลดตลอด (persistent systems + UI)
- **GameplayScene(s)**: โหลด additive, เปลี่ยนได้โดยไม่กระทบ CoreScene

## Motivation
- UI persistent ข้าม scene (ไม่ต้อง recreate ทุกรอบ)
- แยก concerns: core systems vs scene content
- รองรับ multiple biomes/locations ในอนาคต

## Scene Structure

### CoreScene (โหลดแบบ Single, never unload)
**Persistent Systems:**
- GameLifetimeScope (VContainer root)
- TimeSystem
- SectStateProvider
- MessagePipe bus
- SceneLoader

**Persistent UI:**
- Canvas (Screen Space - Overlay)
- UIRoot
- ResourceHud (top bar)
- SettingsPanel
- HotkeyMenu

**Singleton Components:**
- EventSystem (ต้องมีแค่ 1 ตัว)
- AudioListener (ต้องมีแค่ 1 ตัว)
- MainCamera (ถ้ามี)

### GameplayScene (โหลดแบบ Additive, unload/reload ได้)
**Scene Content:**
- Environment / Terrain
- NPCs / Disciples (3D/2D objects)
- Buildings

**Scene-specific Systems:**
- BuildingSystem (ถ้า scene-dependent)
- Scene-specific triggers / events

**Scene-specific UI:**
- EventPopup (เปิดเมื่อมี event ใน scene นี้)
- DialoguePanel (คุยกับ NPC ใน scene นี้)

## Implementation Details

### SceneLoader (Singleton ใน CoreScene)
```csharp
public class SceneLoader
{
    private string _currentGameplayScene;
    
    public async UniTask LoadGameplayScene(string sceneName)
    {
        if (!string.IsNullOrEmpty(_currentGameplayScene)) {
            await SceneManager.UnloadSceneAsync(_currentGameplayScene);
        }
        await SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        _currentGameplayScene = sceneName;
    }
}
```

### VContainer Strategy
**Option A: Single Root Scope (แนะนำสำหรับตอนนี้)**
- CoreScene มี GameLifetimeScope เดียว
- ทุก system/UI register ที่เดียว
- GameplayScene ไม่มี LifetimeScope ของตัวเอง
- ง่ายที่สุด, ไม่ต้องจัดการ parent-child scope

**Option B: Parent-Child Scope (สำหรับอนาคต)**
- CoreScene = Parent scope (persistent services)
- GameplayScene = Child scope (scene-specific services)
- ซับซ้อนกว่า แต่แยก concerns ชัดเจนกว่า

### UI Persistence
**Persistent Panels (อยู่ใน CoreScene):**
```csharp
// เปิดครั้งเดียวตอน CoreScene load
public class UIBootstrap : IStartable {
    public void Start() {
        _uiService.Open("ResourceHud");  // persistent
        _uiService.Open("HotkeyMenu");   // persistent
    }
}
```

**Scene-specific Panels (Instantiate จาก Prefab):**
```csharp
// เปิดเมื่อมี event ใน scene
_worldEventSub.Subscribe(msg => {
    _uiService.Open("EventPopup", msg);  // scene-specific
});
```

### Technical Considerations

**Canvas Sorting:**
- CoreScene Canvas: Sorting Order = 0
- GameplayScene Canvas (ถ้ามี): Sorting Order = -1 (อยู่หลัง UI)

**EventSystem:**
- ต้องมีใน CoreScene เท่านั้น
- GameplayScene ห้ามมี EventSystem ซ้ำ

**Camera:**
- ถ้าใช้ 3D: MainCamera อยู่ใน CoreScene, GameplayScene มี camera ลูก (disable)
- ถ้าใช้ 2D UI only: ไม่ต้องกังวล

**AudioListener:**
- ต้องมีแค่ 1 ตัวใน CoreScene
- GameplayScene ห้ามมี AudioListener

## Migration Plan

### Phase 1: Create CoreScene
1. สร้าง CoreScene ใหม่
2. ย้าย Canvas + UIRoot + GameLifetimeScope เข้า CoreScene
3. เพิ่ม DontDestroyOnLoad ใน GameLifetimeScope
4. สร้าง SceneLoader class
5. ทดสอบ: CoreScene โหลดเดียว, UI ทำงานปกติ

### Phase 2: Extract GameplayScene
1. สร้าง GameplayScene ใหม่
2. ย้าย scene content (environment, NPCs) ออกมา
3. ทดสอบ: CoreScene + GameplayScene โหลดพร้อมกัน
4. ทดสอบ: Unload GameplayScene, CoreScene ยังอยู่

### Phase 3: Scene Transition
1. Implement SceneLoader.LoadScene()
2. ทดสอบ: เปลี่ยนจาก Scene A → Scene B
3. ทดสอบ: Persistent UI ยังอยู่หลังเปลี่ยน scene

## Risks & Mitigations

**Risk 1: VContainer references หลุดตอนเปลี่ยน scene**
- Mitigation: GameLifetimeScope อยู่ใน CoreScene ที่ never unload

**Risk 2: Duplicate EventSystem/AudioListener**
- Mitigation: ตรวจสอบใน Awake() ของ GameplayScene, disable ถ้ามีซ้ำ

**Risk 3: Scene-specific UI เปิดไม่ถูกตอนเปลี่ยน scene**
- Mitigation: SceneLoader publish `SceneLoadedMessage`, UI subscribe แล้วเปิด panel ที่เหมาะสม

## Open Questions
- [ ] GameplayScene ควรมี child LifetimeScope ไหม?
- [ ] Scene transition แบบ fade/transition effect?
- [ ] วิธี handle scene-specific data (เช่น building positions)?

## References
- [[architecture]] - overall system architecture
- [[vcontainer-composition]] - DI container setup
- [[mvp-ui]] - UI framework pattern
```

### 2. อัปเดต `LLMWiki/architecture.md`

เพิ่ม section ใหม่หลัง "Architecture" section:

```markdown
## Scene Management

### Additive Scene Pattern
เกมใช้ **Additive Scene Loading** แทน Single Scene:

- **CoreScene** (persistent):
  - Canvas + UIRoot (persistent UI)
  - GameLifetimeScope (VContainer root)
  - Core systems (TimeSystem, SectStateProvider, MessagePipe)
  - EventSystem, AudioListener, MainCamera
  - Never unloads

- **GameplayScene** (transient):
  - Environment, NPCs, Buildings
  - Scene-specific UI (EventPopup, Dialogue)
  - Loads additive, can be unloaded/reloaded

**Scene Transition Flow:**
1. CoreScene loads (Single mode)
2. GameplayScene loads (Additive mode)
3. To change scene: Unload old GameplayScene → Load new GameplayScene
4. CoreScene (and persistent UI) survives throughout

See [[additive-scene-architecture]] for implementation details.
```

### 3. อัปเดต `project_summary.md`

เพิ่มใน section "Lab Rounds" (ต่อจาก lab 13):

```markdown
### Lab 14 (Draft) — Additive Scene Architecture (ยังไม่ implement)
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
```

---

## ขั้นตอนต่อไป

1. **สร้างเอกสารทั้ง 3 ไฟล์** (ใช้ template ด้านบน)
2. **Review และ adjust** ตามความต้องการจริง
3. **เมื่อเอกสาร stable แล้ว** ค่อยสั่ง AI Agent implement

## Prompt สำหรับ AI Agent (หลังจากเอกสาร ready)

```markdown
Implement Additive Scene Architecture ตาม spec ใน LLMWiki/concepts/additive-scene-architecture.md

## Tasks:
1. สร้าง CoreScene ใหม่:
   - ย้าย Canvas + UIRoot จาก scene เดิม
   - ย้าย GameLifetimeScope
   - เพิ่ม DontDestroyOnLoad ใน GameLifetimeScope.Awake()
   - สร้าง SceneLoader singleton (register ใน VContainer)

2. สร้าง GameplayScene ใหม่:
   - ย้าย scene content (environment, NPCs) จาก scene เดิม
   - ตรวจสอบว่าไม่มี duplicate EventSystem/AudioListener

3. Implement SceneLoader:
   - LoadGameplayScene(string sceneName) - unload old, load new additive
   - Publish SceneLoadedMessage เมื่อโหลดเสร็จ

4. แยก UI เป็น 2 ประเภท:
   - Persistent (ResourceHud, Settings): เปิดใน UIBootstrap
   - Scene-specific (EventPopup, Dialogue): เปิดเมื่อมี event

5. ทดสอบ:
   - CoreScene โหลดเดียว, UI ทำงาน
   - GameplayScene โหลด additive
   - เปลี่ยน scene: UI ยังอยู่, content เปลี่ยน

## Constraints:
- อ่าน LLMWiki/concepts/additive-scene-architecture.md ก่อน
- ใช้ VContainer single root scope (Option A)
- ไม่แก้ UI Framework MVP Lite
- Backward compatible กับ scene เดิม

## Acceptance Criteria:
- [ ] CoreScene + GameplayScene โหลดพร้อมกัน
- [ ] Persistent UI (ResourceHud) อยู่หลังเปลี่ยน scene
- [ ] Scene-specific UI เปิดได้ปกติ
- [ ] ไม่มี duplicate EventSystem/AudioListener
- [ ] SceneLoader.LoadScene() ทำงาน
```

เอาแนวทางนี้ไหมครับ? หรือมีจุดไหนอยาก adjust?


แนะนำว่า **ร่างเอกสารก่อน implement** ครับ เพราะนี่คือ architecture change ครั้งใหญ่ที่มีผลกระทบต่อหลายระบบ และสอดคล้องกับ pattern ที่โปรเจกต์นี้ทำมาตลอด (Avatar, Sex/Gender ก็เริ่มจากเอกสารก่อน)

## เหตุผลที่ควรทำเอกสารก่อน

1. **ต้องตัดสินใจหลายจุดสำคัญ:**
   - อะไรอยู่ใน CoreScene vs GameplayScene
   - VContainer scope จะจัดยังไง (single root vs parent-child)
   - Scene-specific systems จะ register ยังไง
   - Backward compatibility กับ scene เดิม

2. **ลดความเสี่ยง:** Implement แล้วรื้อมี cost สูงกว่า document แล้ว adjust

3. **เป็น reference สำหรับอนาคต:** เมื่อมี scene เพิ่มขึ้น (dungeon, town, sect) จะมี pattern ให้ follow

## โครงสร้างเอกสารที่แนะนำ

ผมเสนอให้สร้าง 2 เอกสาร:

### 1. `LLMWiki/concepts/additive-scene-architecture.md` (เอกสารหลัก)

```markdown
---
title: Additive Scene Architecture
type: concept
status: draft
created: 2026-09-02
related:
  - "[[architecture]]"
  - "[[vcontainer-composition]]"
  - "[[mvp-ui]]"
tags: [scene-management, additive-loading, core-scene, architecture]
---

# Additive Scene Architecture

## Overview
เปลี่ยนจาก Single Scene เป็น Additive Scene pattern:
- **CoreScene**: โหลดตลอด (persistent systems + UI)
- **GameplayScene(s)**: โหลด additive, เปลี่ยนได้โดยไม่กระทบ CoreScene

## Motivation
- UI persistent ข้าม scene (ไม่ต้อง recreate ทุกรอบ)
- แยก concerns: core systems vs scene content
- รองรับ multiple biomes/locations ในอนาคต

## Scene Structure

### CoreScene (โหลดแบบ Single, never unload)
**Persistent Systems:**
- GameLifetimeScope (VContainer root)
- TimeSystem
- SectStateProvider
- MessagePipe bus
- SceneLoader

**Persistent UI:**
- Canvas (Screen Space - Overlay)
- UIRoot
- ResourceHud (top bar)
- SettingsPanel
- HotkeyMenu

**Singleton Components:**
- EventSystem (ต้องมีแค่ 1 ตัว)
- AudioListener (ต้องมีแค่ 1 ตัว)
- MainCamera (ถ้ามี)

### GameplayScene (โหลดแบบ Additive, unload/reload ได้)
**Scene Content:**
- Environment / Terrain
- NPCs / Disciples (3D/2D objects)
- Buildings

**Scene-specific Systems:**
- BuildingSystem (ถ้า scene-dependent)
- Scene-specific triggers / events

**Scene-specific UI:**
- EventPopup (เปิดเมื่อมี event ใน scene นี้)
- DialoguePanel (คุยกับ NPC ใน scene นี้)

## Implementation Details

### SceneLoader (Singleton ใน CoreScene)
```csharp
public class SceneLoader
{
    private string _currentGameplayScene;
    
    public async UniTask LoadGameplayScene(string sceneName)
    {
        if (!string.IsNullOrEmpty(_currentGameplayScene)) {
            await SceneManager.UnloadSceneAsync(_currentGameplayScene);
        }
        await SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        _currentGameplayScene = sceneName;
    }
}
```

### VContainer Strategy
**Option A: Single Root Scope (แนะนำสำหรับตอนนี้)**
- CoreScene มี GameLifetimeScope เดียว
- ทุก system/UI register ที่เดียว
- GameplayScene ไม่มี LifetimeScope ของตัวเอง
- ง่ายที่สุด, ไม่ต้องจัดการ parent-child scope

**Option B: Parent-Child Scope (สำหรับอนาคต)**
- CoreScene = Parent scope (persistent services)
- GameplayScene = Child scope (scene-specific services)
- ซับซ้อนกว่า แต่แยก concerns ชัดเจนกว่า

### UI Persistence
**Persistent Panels (อยู่ใน CoreScene):**
```csharp
// เปิดครั้งเดียวตอน CoreScene load
public class UIBootstrap : IStartable {
    public void Start() {
        _uiService.Open("ResourceHud");  // persistent
        _uiService.Open("HotkeyMenu");   // persistent
    }
}
```

**Scene-specific Panels (Instantiate จาก Prefab):**
```csharp
// เปิดเมื่อมี event ใน scene
_worldEventSub.Subscribe(msg => {
    _uiService.Open("EventPopup", msg);  // scene-specific
});
```

### Technical Considerations

**Canvas Sorting:**
- CoreScene Canvas: Sorting Order = 0
- GameplayScene Canvas (ถ้ามี): Sorting Order = -1 (อยู่หลัง UI)

**EventSystem:**
- ต้องมีใน CoreScene เท่านั้น
- GameplayScene ห้ามมี EventSystem ซ้ำ

**Camera:**
- ถ้าใช้ 3D: MainCamera อยู่ใน CoreScene, GameplayScene มี camera ลูก (disable)
- ถ้าใช้ 2D UI only: ไม่ต้องกังวล

**AudioListener:**
- ต้องมีแค่ 1 ตัวใน CoreScene
- GameplayScene ห้ามมี AudioListener

## Migration Plan

### Phase 1: Create CoreScene
1. สร้าง CoreScene ใหม่
2. ย้าย Canvas + UIRoot + GameLifetimeScope เข้า CoreScene
3. เพิ่ม DontDestroyOnLoad ใน GameLifetimeScope
4. สร้าง SceneLoader class
5. ทดสอบ: CoreScene โหลดเดียว, UI ทำงานปกติ

### Phase 2: Extract GameplayScene
1. สร้าง GameplayScene ใหม่
2. ย้าย scene content (environment, NPCs) ออกมา
3. ทดสอบ: CoreScene + GameplayScene โหลดพร้อมกัน
4. ทดสอบ: Unload GameplayScene, CoreScene ยังอยู่

### Phase 3: Scene Transition
1. Implement SceneLoader.LoadScene()
2. ทดสอบ: เปลี่ยนจาก Scene A → Scene B
3. ทดสอบ: Persistent UI ยังอยู่หลังเปลี่ยน scene

## Risks & Mitigations

**Risk 1: VContainer references หลุดตอนเปลี่ยน scene**
- Mitigation: GameLifetimeScope อยู่ใน CoreScene ที่ never unload

**Risk 2: Duplicate EventSystem/AudioListener**
- Mitigation: ตรวจสอบใน Awake() ของ GameplayScene, disable ถ้ามีซ้ำ

**Risk 3: Scene-specific UI เปิดไม่ถูกตอนเปลี่ยน scene**
- Mitigation: SceneLoader publish `SceneLoadedMessage`, UI subscribe แล้วเปิด panel ที่เหมาะสม

## Open Questions
- [ ] GameplayScene ควรมี child LifetimeScope ไหม?
- [ ] Scene transition แบบ fade/transition effect?
- [ ] วิธี handle scene-specific data (เช่น building positions)?

## References
- [[architecture]] - overall system architecture
- [[vcontainer-composition]] - DI container setup
- [[mvp-ui]] - UI framework pattern
```

### 2. อัปเดต `LLMWiki/architecture.md`

เพิ่ม section ใหม่หลัง "Architecture" section:

```markdown
## Scene Management

### Additive Scene Pattern
เกมใช้ **Additive Scene Loading** แทน Single Scene:

- **CoreScene** (persistent):
  - Canvas + UIRoot (persistent UI)
  - GameLifetimeScope (VContainer root)
  - Core systems (TimeSystem, SectStateProvider, MessagePipe)
  - EventSystem, AudioListener, MainCamera
  - Never unloads

- **GameplayScene** (transient):
  - Environment, NPCs, Buildings
  - Scene-specific UI (EventPopup, Dialogue)
  - Loads additive, can be unloaded/reloaded

**Scene Transition Flow:**
1. CoreScene loads (Single mode)
2. GameplayScene loads (Additive mode)
3. To change scene: Unload old GameplayScene → Load new GameplayScene
4. CoreScene (and persistent UI) survives throughout

See [[additive-scene-architecture]] for implementation details.
```

### 3. อัปเดต `project_summary.md`

เพิ่มใน section "Lab Rounds" (ต่อจาก lab 13):

```markdown
### Lab 14 (Draft) — Additive Scene Architecture (ยังไม่ implement)
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
```

---

## ขั้นตอนต่อไป

1. **สร้างเอกสารทั้ง 3 ไฟล์** (ใช้ template ด้านบน)
2. **Review และ adjust** ตามความต้องการจริง
3. **เมื่อเอกสาร stable แล้ว** ค่อยสั่ง AI Agent implement

## Prompt สำหรับ AI Agent (หลังจากเอกสาร ready)

```markdown
Implement Additive Scene Architecture ตาม spec ใน LLMWiki/concepts/additive-scene-architecture.md

## Tasks:
1. สร้าง CoreScene ใหม่:
   - ย้าย Canvas + UIRoot จาก scene เดิม
   - ย้าย GameLifetimeScope
   - เพิ่ม DontDestroyOnLoad ใน GameLifetimeScope.Awake()
   - สร้าง SceneLoader singleton (register ใน VContainer)

2. สร้าง GameplayScene ใหม่:
   - ย้าย scene content (environment, NPCs) จาก scene เดิม
   - ตรวจสอบว่าไม่มี duplicate EventSystem/AudioListener

3. Implement SceneLoader:
   - LoadGameplayScene(string sceneName) - unload old, load new additive
   - Publish SceneLoadedMessage เมื่อโหลดเสร็จ

4. แยก UI เป็น 2 ประเภท:
   - Persistent (ResourceHud, Settings): เปิดใน UIBootstrap
   - Scene-specific (EventPopup, Dialogue): เปิดเมื่อมี event

5. ทดสอบ:
   - CoreScene โหลดเดียว, UI ทำงาน
   - GameplayScene โหลด additive
   - เปลี่ยน scene: UI ยังอยู่, content เปลี่ยน

## Constraints:
- อ่าน LLMWiki/concepts/additive-scene-architecture.md ก่อน
- ใช้ VContainer single root scope (Option A)
- ไม่แก้ UI Framework MVP Lite
- Backward compatible กับ scene เดิม

## Acceptance Criteria:
- [ ] CoreScene + GameplayScene โหลดพร้อมกัน
- [ ] Persistent UI (ResourceHud) อยู่หลังเปลี่ยน scene
- [ ] Scene-specific UI เปิดได้ปกติ
- [ ] ไม่มี duplicate EventSystem/AudioListener
- [ ] SceneLoader.LoadScene() ทำงาน
```

เอาแนวทางนี้ไหมครับ? หรือมีจุดไหนอยาก adjust?