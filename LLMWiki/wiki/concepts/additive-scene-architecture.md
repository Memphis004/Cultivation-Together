---
title: Additive Scene Architecture
type: concept
status: draft
created: 2026-09-02
updated: 2026-10-02
related:
  - "[[architecture]]"
  - "[[vcontainer-composition]]"
  - "[[mvp-ui]]"
tags: [scene-management, additive-loading, core-scene, architecture]
---

# Additive Scene Architecture

## Overview
เปลี่ยนจาก Single Scene เป็น Additive Scene pattern:
- **CoreScene** = `SampleScene.unity` — โหลดตลอด (persistent systems + UI)
- **GameplayScene** = `SectScene.unity` (เดิมชื่อ `TestGameplayScene`, rename 2026-10-02
  ผ่าน `AssetDatabase.RenameAsset` จึงคง GUID เดิม) — โหลด additive, เปลี่ยนได้โดยไม่กระทบ CoreScene
- `TestGameplayScene2.unity` = ฉากทดสอบสำหรับ verify การสลับฉากเท่านั้น (คงชื่อเดิม)

ชื่อ/พาธของฉากที่โค้ดอ้างอิงรวมที่ `Assets/Scripts/Core/SceneNames.cs`
(`SceneNames.Sect` = "SectScene", `SceneNames.SectAssetPath`) — ห้าม hardcode
string ชื่อฉากที่ใหม่

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
- WalletHud (top bar)
- SettingsPanel
- HotkeyMenu

**Singleton Components:**
- EventSystem (ต้องมีแค่ 1 ตัว)
- AudioListener (ต้องมีแค่ 1 ตัว)
- MainCamera (ถ้ามี)

### GameplayScene = SectScene (โหลดแบบ Additive, unload/reload ได้)
**Scene Content:**
- Environment / Terrain (backdrop ภูเขาสร้าง runtime จาก Resources)
- NPCs / Disciples (3D/2D objects) — `ChibiSceneRoot` อยู่ในฉากนี้
- GameplayCamera (orthographic isometric) + Directional Light
- **`SectSceneLifetimeScope`** — child `LifetimeScope` ของฉาก (Phase 2, 2026-10)

**Scene-specific Systems (อยู่ใน child scope ไม่ใช่ root):**
- CameraRigController + GridOverlayRenderer + TerrainBackdropRenderer (entry points)
- camera rig ports 5 ตัว (IRigMessageBus/ICameraRigEnvironment/ICellSpriteMetrics/
  IMainThreadQueue/IRigClock)
- ดู [[concepts/vcontainer-composition|VContainer Composition Root]] สำหรับวิธีผูก parent

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
**Option A: Single Root Scope (แบบเดิม)**
- CoreScene มี GameLifetimeScope เดียว, ทุก system register ที่เดียว
- GameplayScene ไม่มี LifetimeScope ของตัวเอง

**Option B: Parent-Child Scope** — ✅ **ใช้งานจริงแล้ว (Phase 2, 2026-10)**
- CoreScene = Parent scope (`GameLifetimeScope` — persistent services ที่แตกเป็น
  installer 5 ตัว)
- GameplayScene = Child scope (`SectSceneLifetimeScope` — เฉพาะของที่ผูกฉาก)
- ผูก parent ด้วย `LifetimeScope.EnqueueParent(root)` ใน `SceneLoader` (ไม่ตั้งที่ Inspector)
- บทเรียน: การ "ย้าย" ของจาก root ลง child ต้อง **ลบ registration ฝั่ง root ด้วย**
  ไม่งั้น entry point เกิด 2 instance (log/action ซ้ำ) — รายละเอียดใน
  [[concepts/vcontainer-composition|VContainer Composition Root]]

### UI Persistence
**Persistent Panels (อยู่ใน CoreScene):**
```csharp
// เปิดครั้งเดียวตอน CoreScene load
public class UIBootstrap : IStartable {
    public void Start() {
        _uiService.Open("WalletHud");  // persistent
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
- [x] GameplayScene ควรมี child LifetimeScope ไหม? → ✅ มีแล้ว (`SectSceneLifetimeScope`, Phase 2)
- [ ] Scene transition แบบ fade/transition effect?
- [ ] วิธี handle scene-specific data (เช่น building positions)?

## References
- [[architecture]] - overall system architecture
- [[vcontainer-composition]] - DI container setup
- [[mvp-ui]] - UI framework pattern