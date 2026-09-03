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