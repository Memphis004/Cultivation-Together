
## “ยืมแนวคิด” จาก CycloneGames https://github.com/MaiKuraki/UnityStarter/tree/main/UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.UIFramework
---

# Xianxia.UI.MVP Lite — Specification for AI Code Agent

## 1. Project Context (อ่านก่อนเขียนโค้ด)

### 1.1 Existing Architecture
โปรเจกต์ใช้ **VContainer** เป็น DI container และ **MessagePipe** เป็น internal messaging bus  
UI framework ต้อง **ไม่แทนที่** ระบบเหล่านี้ แต่ต้อง **integrate เข้ากับพวกมัน**

| Component | Technology | Notes |
|-----------|-----------|-------|
| DI Container | VContainer | `GameLifetimeScope.cs` เป็น composition root |
| Internal Messaging | MessagePipe | Pub/Sub + Request-Response, inject ผ่าน VContainer |
| IPC to MCP Bridge | MessagePipe.Interprocess (TCP) | Unity = server, Bridge = client |
| Game State | `ISectStateProvider` | Singleton, holds live `SectEconomyState` |
| Time Control | `TimeSystem` | Auto-pause on decision events |

### 1.2 Key Constraint
> **ห้ามสร้าง DI container ใหม่, ห้ามสร้าง event system ใหม่, ห้ามสร้าง state management ใหม่**  
> UI Layer เป็นเพียง **consumer** ของระบบที่มีอยู่แล้ว

### 1.3 Target Platform
- Unity (UGUI + TextMeshPro)
- C# 8.0 compatible (no `record`, no `init`, no `global using`)
- No UI Toolkit

---

## 2. Design Principles

1. **MVP Separation**: View = MonoBehaviour (UGUI), Presenter = plain C# class, Service = orchestrator
2. **VContainer Native**: ทุก Presenter/Service register ผ่าน VContainer, inject dependencies ตามปกติ
3. **MessagePipe Native**: Presenter subscribe messages ผ่าน `ISubscriber<T>` ที่ inject มา
4. **No Reflection**: Panel registration ใช้ explicit mapping (enum → Type), ไม่ scan assembly
5. **Disposable**: Presenter implement `IDisposable`, UIService เรียก Dispose เมื่อปิด panel
6. **Lightweight**: ไม่มี animation, cache, localization, async loading ใน v0.1

---

## 3. Core Interfaces

### 3.1 IUIView
```csharp
namespace Xianxia.Sect.UI
{
    public interface IUIView
    {
        GameObject GameObject { get; }
        void Show();
        void Hide();
    }
}
```

### 3.2 IUIViewPresenter
```csharp
namespace Xianxia.Sect.UI
{
    public interface IUIViewPresenter : IDisposable
    {
        void Bind(IUIView view);
        void OnOpen(object args);
        void OnClose();
    }
}
```

### 3.3 UIPresenter\<TView\> (Abstract Base)
```csharp
namespace Xianxia.Sect.UI
{
    public abstract class UIPresenter<TView> : IUIViewPresenter
        where TView : class, IUIView
    {
        protected TView View { get; private set; }

        public void Bind(IUIView view)
        {
            View = view as TView
                ?? throw new InvalidCastException(
                    $"Expected {typeof(TView).Name}, got {view.GetType().Name}");
            OnViewBound();
        }

        protected virtual void OnViewBound() { }
        public virtual void OnOpen(object args) { }
        public virtual void OnClose() { }
        public virtual void Dispose() { }
    }
}
```

---

## 4. Panel Registration System

### 4.1 UIPresenterKind Enum
```csharp
namespace Xianxia.Sect.UI
{
    public enum UIPresenterKind
    {
        EventPopup,
        ResourceHud,
        DiscipleList,
        // เพิ่มได้เมื่อมี panel ใหม่
    }
}
```

### 4.2 UIPanelDefinition (ScriptableObject-friendly)
```csharp
namespace Xianxia.Sect.UI
{
    [Serializable]
    public class UIPanelDefinition
    {
        public string PanelId;
        public GameObject Prefab;
        public UIPresenterKind PresenterKind;
    }
}
```

### 4.3 UIPanelCatalog (ScriptableObject)
```csharp
namespace Xianxia.Sect.UI
{
    [CreateAssetMenu(menuName = "Xianxia/UI/Panel Catalog")]
    public class UIPanelCatalog : ScriptableObject
    {
        [SerializeField] private List<UIPanelDefinition> panels;

        public UIPanelDefinition Get(string panelId)
        {
            foreach (var p in panels)
                if (p.PanelId == panelId) return p;
            throw new Exception($"UI panel not found: {panelId}");
        }
    }
}
```

---

## 5. UIService

### 5.1 Responsibilities
- Resolve Presenter จาก VContainer ตาม `UIPresenterKind`
- Instantiate prefab, bind view, call OnOpen
- Track opened panels, handle Close + Dispose
- Map `UIPresenterKind` → `Type` explicitly

### 5.2 Constructor Dependencies
```csharp
public UIService(
    IObjectResolver resolver,      // VContainer
    UIPanelCatalog catalog,        // Panel definitions
    UIRoot uiRoot                  // Canvas root transform
)
```

### 5.3 Public API
```csharp
UIPanelHandle Open(string panelId, object args = null);
void Close(string panelId);
```

### 5.4 Presenter Type Mapping
```csharp
private Type ResolvePresenterType(UIPresenterKind kind) => kind switch
{
    UIPresenterKind.EventPopup   => typeof(EventPopupPresenter),
    UIPresenterKind.ResourceHud  => typeof(ResourceHudPresenter),
    UIPresenterKind.DiscipleList => typeof(DiscipleListPresenter),
    _ => throw new ArgumentOutOfRangeException(nameof(kind))
};
```

### 5.5 UIPanelHandle
```csharp
public class UIPanelHandle
{
    public string PanelId { get; }
    public IUIView View { get; }
    public IUIViewPresenter Presenter { get; }
    // internal constructor only
}
```

---

## 6. UIRoot Component

```csharp
namespace Xianxia.Sect.UI
{
    public class UIRoot : MonoBehaviour
    {
        [SerializeField] private Transform root;
        public Transform Root => root != null ? root : transform;
    }
}
```

วางใน Scene: `Canvas > UIRoot`

---

## 7. Concrete Panels (v0.1 Scope)

### 7.1 EventPopup

**View**: `EventPopupView : UIViewBase`
- Fields: `TMP_Text titleText`, `TMP_Text descriptionText`, `Transform choicesRoot`, `Button choiceButtonPrefab`
- Methods: `SetEvent(eventId, description)`, `SetChoices(List<EventChoiceViewData>)`
- Event: `Action<string> ChoiceClicked`

**Presenter**: `EventPopupPresenter : UIPresenter<EventPopupView>`
- Inject: `IPublisher<ExecuteDecisionMessage>`
- OnOpen: รับ `EventPopupOpenArgs`, แสดงข้อมูล, สร้างปุ่ม choice
- OnChoiceClicked: publish `ExecuteDecisionMessage`, hide view
- Dispose: unsubscribe event

**Args**:
```csharp
public class EventPopupOpenArgs
{
    public string EventId;
    public string Description;
    public List<EventChoiceInfo> Choices; // from GameMessages.cs
}
```

### 7.2 ResourceHud

**View**: `ResourceHudView : UIViewBase`
- Methods: `UpdateResource(resourceId, newTotal)`

**Presenter**: `ResourceHudPresenter : UIPresenter<ResourceHudView>`
- Inject: `ISubscriber<SectResourceChangedMessage>`
- OnViewBound: subscribe message
- OnMessage: call `View.UpdateResource()`
- Dispose: dispose subscription

---

## 8. Reactive UI System (Auto-open panels on messages)

### 8.1 WorldEventUISystem
```csharp
// IStartable, IDisposable
// Inject: ISubscriber<WorldEventTriggeredMessage>, UIService
// Start: subscribe WorldEventTriggeredMessage
// OnMessage: if RequiresDecision → uiService.Open("EventPopup", args)
// Dispose: dispose subscription
```

Register as EntryPoint:
```csharp
builder.RegisterEntryPoint<WorldEventUISystem>(Lifetime.Singleton);
```

---

## 9. VContainer Registration Template

ใน `GameLifetimeScope.Configure()`:

```csharp
// --- Xianxia.UI.MVP Lite ---
[SerializeField] private UIRoot uiRoot;
[SerializeField] private UIPanelCatalog uiPanelCatalog;

// Core
builder.RegisterInstance(uiRoot);
builder.RegisterInstance(uiPanelCatalog);
builder.Register<UIService>(Lifetime.Singleton);

// Presenters (Transient - new instance per panel open)
builder.Register<EventPopupPresenter>(Lifetime.Transient);
builder.Register<ResourceHudPresenter>(Lifetime.Transient);
builder.Register<DiscipleListPresenter>(Lifetime.Transient);

// Reactive systems
builder.RegisterEntryPoint<WorldEventUISystem>(Lifetime.Singleton);
```

---

## 10. Integration Points with Existing Systems

| UI Need | Source | How to Access |
|---------|--------|---------------|
| World Events | `WorldEventTriggeredMessage` | Subscribe via `ISubscriber<>` |
| Resource Changes | `SectResourceChangedMessage` | Subscribe via `ISubscriber<>` |
| Disciple Recruited | `DiscipleRecruitedMessage` | Subscribe via `ISubscriber<>` |
| Send Decision | `ExecuteDecisionMessage` | Publish via `IPublisher<>` |
| Full State Snapshot | `ISectStateProvider.BuildSectEconomyState()` | Inject `ISectStateProvider` |
| Purchase Item | `PurchaseItemRequest/Response` | Inject `IRemoteRequestHandler<>` (ถ้าจำเป็น) |
| Pause/Speed | `TimeSpeedChangedMessage` | Subscribe via `ISubscriber<>` |

> ⚠️ **Important**: `WorldEventTriggeredMessage` ตอนนี้ยังมีแค่ `EventId` + `RequiresDecision`  
> ต้องเพิ่ม `Description` และ `Choices` field ก่อน EventPopup จะแสดงข้อมูลครบ  
> ดู `AwaitWorldEventResponse` ใน `GameMessages.cs` เป็น reference

---

## 11. File Structure

```
Assets/Scripts/UI/
├── Core/
│   ├── IUIView.cs
│   ├── IUIViewPresenter.cs
│   ├── UIPresenter.cs
│   ├── UIService.cs
│   ├── UIPanelHandle.cs
│   ├── UIPanelCatalog.cs
│   ├── UIPanelDefinition.cs
│   ├── UIPresenterKind.cs
│   └── UIRoot.cs
├── Views/
│   ├── UIViewBase.cs
│   ├── EventPopupView.cs
│   └── ResourceHudView.cs
├── Presenters/
│   ├── EventPopupPresenter.cs
│   └── ResourceHudPresenter.cs
└── Systems/
    └── WorldEventUISystem.cs
```

---

## 12. Out of Scope for v0.1

- ❌ Animation / Transition
- ❌ UI Stack / Navigation Graph
- ❌ Addressables / Async Loading
- ❌ Localization
- ❌ MVVM Data Binding
- ❌ Cache / Pooling
- ❌ Nested Windows
- ❌ UI Toolkit

---

## 13. Acceptance Criteria

- [ ] `UIService.Open("EventPopup", args)` แสดง popup พร้อม choices
- [ ] กด choice → `ExecuteDecisionMessage` ถูก publish
- [ ] `ResourceHudView` อัปเดต real-time เมื่อ `SectResourceChangedMessage` มา
- [ ] ปิด panel → Presenter.Dispose() ถูกเรียก, subscription ถูก cleanup
- [ ] ทุกอย่าง resolve ผ่าน VContainer, ไม่มี `new` presenter โดยตรง
- [ ] Compile ผ่านบน Unity C# 8.0 (no record/init/global using)
- [ ] ไม่แก้ไขไฟล์ใดๆ ใน `Xianxia.Sect` namespace เดิมนอกจากเพิ่ม field ใน `WorldEventTriggeredMessage`

---
