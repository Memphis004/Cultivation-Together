---
title: MVP UI Pattern
type: concept
sources:
  - UnityProject/Assets/Scripts/UI/Core/UIPresenter.cs
  - UnityProject/Assets/Scripts/UI/Views/UIViewBase.cs
  - UnityProject/Assets/Scripts/UI/Core/UIService.cs
  - UnityProject/Assets/Scripts/UI/Core/UIPanelCatalog.cs
  - ../../XIANXIA_UI_MVP_LITE_SPEC.md
related:
  - "[[concepts/vcontainer-composition]]"
  - "[[concepts/decision-pipeline]]"
  - "[[sources/architecture]]"
created: 2026-08-31
updated: 2026-09-02
confidence: high
tags: [ui, mvp, ugui, vcontainer, panel, avatar]
---

# MVP UI Pattern (Xianxia.UI.MVP Lite)

> Lightweight MVP framework for UGUI + TextMeshPro. Reference design
> borrowed from CycloneGames UIFramework
> (`XIANXIA_UI_MVP_LITE_SPEC.md`).

## Three Roles

| Role | Type | Responsibility |
|---|---|---|
| **View** | `MonoBehaviour` (extends `UIViewBase`) | Unity lifecycle, GameObject hierarchy, animation, TMP text updates |
| **Presenter** | Plain C# (extends `UIPresenter<TView>`) | Subscribe to events, decide what to show, dispatch user input |
| **Service** | Plain C# (`UIService`) | Instantiate prefab, resolve presenter by kind, track handles |

## Constraints (per spec)

- VContainer-native (every Presenter/Service resolved via DI)
- MessagePipe-native (subscribe via `ISubscriber<T>` injected)
- **No reflection** — explicit enum→Type mapping for panel resolution
- Disposable (`Presenter : IDisposable`)
- Lightweight v0.1: no animation, no cache, no localization, no async loading
- uGUI + TMP only — **NO UI Toolkit**

## Panel Lifecycle

```
1. Some system calls UIService.Open(panelId, args)
   - UIService looks up UIPanelDefinition from UIPanelCatalog (ScriptableObject)
   - Instantiates prefab under UIRoot
   - UIRoot.ApplyLayout() forces anchor stretch (lab 13 fix)
   - Resolves presenter type from UIPresenterKind enum (no reflection)
   - VContainer resolves presenter (Transient — new instance every time)
   - presenter.Bind(view)    → OnViewBound() (subscribe to events)
   - presenter.OnOpen(args)
   - view.Show()

2. ... user interacts with view, view fires events back to presenter ...

3. Presenter decides to close (or external close trigger)
   - presenter.OnClose() (optional cleanup)
   - presenter.Dispose()   (unsubscribe events)
   - view.Hide()
   - Destroy(view.GameObject) [or pool later]
```

## Why Transient Presenter

Each panel open gets a **fresh** presenter instance. No stale subscriptions
from prior opens. `Dispose()` is called on close, so subscriptions are
cleaned up deterministically.

## Enum → Type Mapping (no reflection)

`UIService` has a switch/if-else mapping `UIPresenterKind` enum to concrete
presenter types. Currently:

```csharp
private static Type ResolvePresenterType(UIPresenterKind kind)
{
    switch (kind)
    {
        case UIPresenterKind.EventPopup: return typeof(EventPopupPresenter);
        case UIPresenterKind.ResourceHud: return typeof(WalletHudPresenter); // enum member keeps its legacy name — catalog serializes kind as int
        case UIPresenterKind.LogWindow: return typeof(LogWindowPresenter);
        case UIPresenterKind.AvatarCustomization: return typeof(AvatarCustomizationPresenter);
        case UIPresenterKind.DiscipleList: return typeof(DiscipleListPresenter);
        case UIPresenterKind.DiscipleDetail: return typeof(DiscipleDetailPresenter);
        case UIPresenterKind.BottomMenu: return typeof(BottomMenuPresenter);
        case UIPresenterKind.ResourcePopup: return typeof(ResourcePopupPresenter);
        default:
            throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
    }
}
```
> 📎 Source: Assets/Scripts/UI/Core/UIService.cs

## Implemented Panels (8)

> นับจาก `MainPanelCatalog.asset` + mapping ใน `UIService` จริง (2026-09-25) —
> ครบทั้ง 8 entry ของ catalog ไม่มี kind ที่ throw ค้างแล้ว

| Panel | Presenter | Subscribes to / เปิดโดย |
|---|---|---|
| `EventPopup` | `EventPopupPresenter` | n/a (called via `OnOpen(args)`) |
| `WalletHud` | `WalletHudPresenter` | `SectResourceChangedMessage` |
| `LogWindow` | `LogWindowPresenter` | `DiscipleRecruitedMessage`, `WorldEventTriggeredMessage`, `DecisionExecutedMessage` |
| `AvatarCustomization` | `AvatarCustomizationPresenter` | `AvatarEquipmentChangedMessage` (external sync) — ดู [[entities/avatar-appearance]] |
| `DiscipleList` | `DiscipleListPresenter` | เปิดโดย `UIBootstrap.WireDiscipleList` (ปุ่ม "ศิษย์" → `BottomMenuPresenter.DiscipleClicked`, toggle แบบ `WireResourcePopup`); คลิกการ์ด publish `DiscipleSelectedMessage` — เส้นทางเดียวกับ chibi click |
| `DiscipleDetail` | `DiscipleDetailPresenter` | เปิดโดย `DiscipleDetailUISystem` เมื่อได้รับ `DiscipleSelectedMessage` (คลิก chibi หรือการ์ดใน DiscipleList — Phase 4) |
| `BottomMenu` | `BottomMenuPresenter` | persistent (เปิดโดย `UIBootstrap`); ปุ่มสร้าง/ศิษย์/คลัง wire ผ่าน `BuildClicked`/`DiscipleClicked`/`WarehouseButton` |
| `ResourcePopup` | `ResourcePopupPresenter` | `SectResourceChangedMessage`; เปิดโดย `UIBootstrap.WireResourcePopup` (ปิดผ่าน `ResourcePopupArgs.CloseCallback` — DiscipleList ใช้แพทเทิร์นเดียวกันผ่าน `DiscipleListArgs`) |

**Z-order note (§6.1):** `UIService.Open` ที่ branch "existing" (panel เปิดอยู่แล้ว) เรียก
`SetAsLastSibling()` ก่อน `OnOpen` — panel ที่ปิดด้วย `Hide()` (เช่น DiscipleDetail)
จะกลับมาอยู่บนสุดเมื่อ re-open ไม่งั้นจะไปอยู่หลัง panel ที่ถูกสร้างทีหลัง

See [[concepts/log-window]] for the full LogWindow architecture and data flow.

## Layout Fix (lab 13 gotcha)

Originally all panels landed center-screen. Root cause:
1. `UIRoot` GameObject wasn't stretched to fill Canvas initially
2. `ContentSizeFitter` set to `PreferredSize` recomputed every frame,
   overriding anchor stretch

**Fix**:
- `UIRoot.Awake()` enforces RectTransform stretch to full Canvas
- `ContentSizeFitter` set to `Unconstrained`
- Each panel view applies its own layout by overriding
  `UIViewBase.ApplyDefaultLayout()`, called from `UIService.Open()` and from
  `UIRoot.Awake()` for baked-in clones — data-driven migration done
  25 Sep 2026 (lab 20), replacing the old `UIRoot.ApplyLayout()`
  prefab-name string matching

## Adding a New Panel — Checklist

1. Create `MyView : UIViewBase` (MonoBehaviour)
2. Create `MyPresenter : UIPresenter<MyView>` (plain C#)
3. Create `MyOpenArgs` class for opening arguments (optional)
4. Add `MyKind` to `UIPresenterKind` enum
5. Add mapping in `UIService` (enum → presenter type)
6. Register in `GameLifetimeScope.Configure`:
   ```csharp
   builder.Register<MyPresenter>(Lifetime.Transient);
   ```
7. Add a `UIPanelDefinition` to `MainPanelCatalog.asset` ScriptableObject
8. Create the prefab in `Assets/Prefabs/`
9. (Optional) Override `ApplyDefaultLayout()` on your view if your prefab
   needs a default layout applied at open time

## Core Interfaces

```csharp
public interface IUIView
{
    GameObject GameObject { get; }
    void Show();
    void Hide();
}

public interface IUIViewPresenter : IDisposable
{
    void Bind(IUIView view);
    void OnOpen(object args);
    void OnClose();
}
```
> 📎 Source: Assets/Scripts/UI/Core/IUIView.cs and IUIViewPresenter.cs

## Bootstrapping and Global Systems

```csharp
public class UIBootstrap : IStartable
{
    public void Start()
    {
        _uiService.Open("WalletHud");
        _uiService.Open("LogWindow");
        // ทดสอบเปิดหน้าจอแต่งตัวศิษย์ d001
        _uiService.Open("AvatarCustomization", new AvatarCustomizationPayload("d001"));
    }
}
```
> 📎 Source: Assets/Scripts/UI/Systems/UIBootstrap.cs

```csharp
public class WorldEventUISystem : IStartable, IDisposable
{
    // Listens to WorldEventTriggeredMessage and opens EventPopup
    public void Start() { ... }
}
```
> 📎 Source: Assets/Scripts/UI/Systems/WorldEventUISystem.cs

## Anti-Patterns to Avoid

- ❌ Don't put game logic in the View — only display + input events
- ❌ Don't put UI logic in the Presenter — only data flow + decision
- ❌ Don't use `Find`/`FindObjectOfType` to get references — use DI
- ❌ Don't subscribe in `OnOpen` and not unsubscribe in `Dispose` — memory leak
- ❌ Don't use reflection to find presenter types — use the enum mapping

## Related Pages

- [[concepts/vcontainer-composition|VContainer Composition Root]]
- [[concepts/decision-pipeline|Decision Pipeline]]
- [[sources/architecture|Architecture]]
