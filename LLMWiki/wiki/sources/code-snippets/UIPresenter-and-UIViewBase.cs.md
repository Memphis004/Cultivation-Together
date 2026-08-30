---
title: UIPresenter + UIViewBase (MVP Lite base)
type: snippet
sources:
  - UnityProject/Assets/Scripts/UI/Core/UIPresenter.cs
  - UnityProject/Assets/Scripts/UI/Views/UIViewBase.cs
related:
  - "[[concepts/mvp-ui]]"
  - "[[concepts/decision-pipeline]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [ui, mvp, presenter, view, vcontainer]
---

# UIPresenter + UIViewBase (MVP Lite Base)

> The two foundation classes of Xianxia.UI.MVP Lite.
> - `UIPresenter<TView>`: plain C# base for presenters
> - `UIViewBase`: MonoBehaviour base for views
> **File paths**:
> - `UnityProject/Assets/Scripts/UI/Core/UIPresenter.cs` (26 lines)
> - `UnityProject/Assets/Scripts/UI/Views/UIViewBase.cs` (19 lines)

## Why MVP Lite

Three responsibilities, three types:
- **View** (MonoBehaviour) — Unity lifecycle, GameObject hierarchy, animation
- **Presenter** (plain C#) — subscribe to events, mutate view, decide
- **Service** (`UIService`) — instantiate prefab, resolve presenter by kind, track handles

Reference design borrowed from CycloneGames UIFramework
(`XIANXIA_UI_MVP_LITE_SPEC.md`).

## Constraints (per spec)

- VContainer-native (every Presenter/Service resolved via DI)
- MessagePipe-native (subscribe via `ISubscriber<T>` injected)
- **No reflection** — explicit enum→Type mapping for panel resolution
- Disposable (`Presenter : IDisposable`)
- Lightweight v0.1: no animation, no cache, no localization, no async loading
- uGUI + TMP only — **NO UI Toolkit**

## Code — UIPresenter.cs (full)

```csharp
using System;

namespace Xianxia.Sect.UI
{
    public abstract class UIPresenter<TView> : IUIViewPresenter
        where TView : class, IUIView
    {
        protected TView View { get; private set; }

        public void Bind(IUIView view)
        {
            View = view as TView;
            if (View == null)
            {
                throw new InvalidCastException(
                    $"Expected {typeof(TView).Name}, got {view.GetType().Name}");
            }
            OnViewBound();
        }

        protected virtual void OnViewBound() { }
        public virtual void OnOpen(object args) { }
        public virtual void OnClose() { }
        public virtual void Dispose() { }
    }
}
```

## Code — UIViewBase.cs (full)

```csharp
using UnityEngine;

namespace Xianxia.Sect.UI
{
    public abstract class UIViewBase : MonoBehaviour, IUIView
    {
        public GameObject GameObject => gameObject;

        public virtual void Show()
        {
            gameObject.SetActive(true);
        }

        public virtual void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
```

## Lifecycle

```
1. UIService.Open(panelId, args)
   → Instantiate prefab from UIPanelCatalog
   → UIRoot.ApplyLayout() (force anchor stretch)
   → Resolve presenter type from UIPresenterKind enum
   → VContainer resolves presenter (Transient — new instance every time)
   → presenter.Bind(view)    → calls OnViewBound() (subscribe to events)
   → presenter.OnOpen(args)  → pass open arguments
   → view.Show()

2. ... user interacts ...

3. presenter.OnClose() (optional cleanup)
   → view.Hide()
   → presenter.Dispose()    → unsubscribe from events
   → Destroy(view.GameObject) (or pool later)
```

## Key Patterns

1. **Generic `UIPresenter<TView>`** — type-safe, no need to cast inside
2. **Transient lifetime** — every panel open gets a fresh presenter (no stale
   subscriptions)
3. **`OnViewBound()` for subscription** — single point to attach event handlers
4. **`Dispose()` for unsubscription** — prevents memory leaks when panels close
5. **Open args as `object`** — flexible but loses type safety; subclasses
   cast in `OnOpen` (see `EventPopupPresenter`)

## Concrete Examples

- `EventPopupPresenter` — `OnChoiceClicked` event from view → calls
  `DecisionExecutor.Execute(...)` directly
- `ResourceHudPresenter` — subscribes to `SectResourceChangedMessage`,
  updates TMP text with delta indicators

## Known Issues

- ⏳ `OnOpen(object args)` is type-unsafe — could add generic
  `UIPresenter<TView, TOpenArgs>` later
- ⏳ No view pooling — `Instantiate` + `Destroy` per open. Fine for 2 panels,
  revisit if FPS drops

## Related Pages

- [[concepts/mvp-ui|MVP UI Pattern]]
- [[concepts/decision-pipeline|Decision Pipeline]]
- [[concepts/message-pipe-bus|MessagePipe Bus]]
