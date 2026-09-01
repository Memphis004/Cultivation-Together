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
        case UIPresenterKind.ResourceHud: return typeof(ResourceHudPresenter);
        case UIPresenterKind.LogWindow: return typeof(LogWindowPresenter);
        case UIPresenterKind.AvatarCustomization: return typeof(AvatarCustomizationPresenter);
        case UIPresenterKind.DiscipleList:
            throw new NotImplementedException("DiscipleListPresenter is not implemented yet.");
        default:
            throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
    }
}
```
> 📎 Source: Assets/Scripts/UI/Core/UIService.cs

⏳ **TODO**: implement `DiscipleListPresenter` when the panel is added.

## Implemented Panels (4)

| Panel | Presenter | Subscribes to |
|---|---|---|
| `EventPopup` | `EventPopupPresenter` | n/a (called via `OnOpen(args)`) |
| `ResourceHud` | `ResourceHudPresenter` | `SectResourceChangedMessage` |
| `LogWindow` | `LogWindowPresenter` | `DiscipleRecruitedMessage`, `WorldEventTriggeredMessage`, `DecisionExecutedMessage` |
| `AvatarCustomization` | `AvatarCustomizationPresenter` | `AvatarEquipmentChangedMessage` (external sync) — ดู [[entities/avatar-appearance]] |

See [[concepts/log-window]] for the full LogWindow architecture and data flow.

## Layout Fix (lab 13 gotcha)

Originally all panels landed center-screen. Root cause:
1. `UIRoot` GameObject wasn't stretched to fill Canvas initially
2. `ContentSizeFitter` set to `PreferredSize` recomputed every frame,
   overriding anchor stretch

**Fix**:
- `UIRoot.Awake()` enforces RectTransform stretch to full Canvas
- `ContentSizeFitter` set to `Unconstrained`
- `UIRoot.ApplyLayout()` (static helper) called from `UIService.Open()` for
  runtime-instantiated panels

⏳ **TODO**: `ApplyLayout()` does string matching on prefab names. Works
for 2 panels, will break at 5+. Migrate to `IUIView.ApplyDefaultLayout()`
on the view itself.

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
9. (Optional) Add layout method to `UIRoot.ApplyLayout()` if your prefab
   needs special handling

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
        _uiService.Open("ResourceHud");
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
