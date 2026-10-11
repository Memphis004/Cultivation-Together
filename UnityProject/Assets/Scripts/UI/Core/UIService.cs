using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;

namespace Xianxia.Sect.UI
{
    public class UIService
    {
        private readonly IObjectResolver _resolver;
        private readonly UIPanelCatalog _catalog;
        private readonly UIRoot _uiRoot;
        private readonly Dictionary<string, UIPanelHandle> _openPanels = new Dictionary<string, UIPanelHandle>();

        public UIService(IObjectResolver resolver, UIPanelCatalog catalog, UIRoot uiRoot)
        {
            _resolver = resolver;
            _catalog = catalog;
            _uiRoot = uiRoot;
        }

        public UIPanelHandle Open(string panelId, object args = null)
        {
            if (_openPanels.TryGetValue(panelId, out var existing))
            {
                // The cached instance may have been destroyed without going through
                // Close() (a session boundary destroying the session UI, a scene unload).
                // Using it through the interface-typed handle would throw
                // MissingReferenceException on the first Unity member read, so a dead
                // handle is dropped and the panel is rebuilt below instead.
                if (existing.IsAlive)
                {
                    existing.View.Show();
                    // Z-order fix (§6.1): Show() only flips activeSelf — without this a
                    // panel re-opened after panels created LATER sits underneath them
                    // (DiscipleDetail opened via DiscipleList hides behind the list).
                    // Hide()/Show() panels (DiscipleDetail) never re-parent, so this one
                    // line is the whole fix — no other presenter touched.
                    existing.View.GameObject.transform.SetAsLastSibling();
                    existing.Presenter.OnOpen(args);
                    return existing;
                }

                _openPanels.Remove(panelId);
            }

            var definition = _catalog.Get(panelId);
            var instance = UnityEngine.Object.Instantiate(definition.Prefab, _uiRoot.Root);

            // --- Data-driven layout (open question #13): the view applies its
            // own default layout — no prefab-name string matching. ---
            instance.GetComponent<UIViewBase>()?.ApplyDefaultLayout();

            var view = instance.GetComponent<IUIView>();
            if (view == null)
            {
                throw new Exception($"Panel prefab for '{panelId}' has no IUIView component.");
            }

            var presenterType = ResolvePresenterType(definition.PresenterKind);
            var presenter = (IUIViewPresenter)_resolver.Resolve(presenterType);

            presenter.Bind(view);
            var handle = new UIPanelHandle(panelId, view, presenter);
            _openPanels[panelId] = handle;

            view.Show();
            presenter.OnOpen(args);

            return handle;
        }

        /// <summary>
        /// P12B — show or hide an ALREADY-OPEN panel without closing it. Closing runs
        /// presenter OnClose/Dispose and destroys the instance (which would detach the
        /// persistent HUD's button wiring); this only flips the view's active state, so the
        /// instance, its subscriptions and its wiring survive a Title detour.
        ///
        /// Returns true only when the panel was open and its visibility really changed — a view
        /// that refuses to show (e.g. the time-control bar while no session is Playing) reports
        /// false rather than pretending it is visible.
        /// </summary>
        public bool TrySetVisible(string panelId, bool visible)
        {
            if (!_openPanels.TryGetValue(panelId, out var handle)) return false;
            // IsAlive, not "View == null": the interface-typed reference never reads as null
            // for a destroyed instance, and the GameObject read below would throw.
            if (!handle.IsAlive) return false;

            if (handle.View.GameObject.activeSelf == visible) return false;

            if (visible) handle.View.Show();
            else handle.View.Hide();

            var go = handle.View.GameObject;
            return go != null && go.activeSelf == visible;
        }

        /// <summary>
        /// P12A — close every open panel except the supplied persistent ids (null/empty
        /// closes them all). Each close runs the presenter's OnClose/Dispose and destroys
        /// the panel instance, exactly like <see cref="Close"/>. Returns how many panels
        /// were actually closed. Used by the session coordinator when a session ends.
        /// </summary>
        public int CloseAllExcept(ICollection<string> keepOpen)
        {
            if (_openPanels.Count == 0) return 0;

            var toClose = new List<string>();
            foreach (var panelId in _openPanels.Keys)
            {
                if (keepOpen == null || !keepOpen.Contains(panelId)) toClose.Add(panelId);
            }

            for (int i = 0; i < toClose.Count; i++) Close(toClose[i]);
            return toClose.Count;
        }

        public void Close(string panelId)
        {
            if (!_openPanels.TryGetValue(panelId, out var handle)) return;

            handle.Presenter.OnClose();

            // The instance can already be gone (see IsAlive): Hide()/GameObject would throw and
            // leave the presenter undisposed, so the Unity half is guarded and the presenter is
            // released either way.
            if (handle.IsAlive)
            {
                handle.View.Hide();
            }

            handle.Presenter.Dispose();

            if (handle.IsAlive)
            {
                // Destroy() is a no-op-with-an-error outside Play Mode ("Destroy may not be
                // called from edit mode") — EditMode tests and editor tooling close panels too,
                // so the mode picks the call instead of leaving the instance behind.
                if (Application.isPlaying) UnityEngine.Object.Destroy(handle.View.GameObject);
                else UnityEngine.Object.DestroyImmediate(handle.View.GameObject);
            }

            _openPanels.Remove(panelId);
        }

        private static Type ResolvePresenterType(UIPresenterKind kind)
        {
            switch (kind)
            {
                case UIPresenterKind.EventPopup: return typeof(EventPopupPresenter);
                case UIPresenterKind.ResourceHud: return typeof(WalletHudPresenter);
                case UIPresenterKind.LogWindow: return typeof(LogWindowPresenter);
                case UIPresenterKind.AvatarCustomization: return typeof(AvatarCustomizationPresenter);
                case UIPresenterKind.DiscipleDetail: return typeof(DiscipleDetailPresenter);
                case UIPresenterKind.BottomMenu: return typeof(BottomMenuPresenter);
                case UIPresenterKind.ResourcePopup: return typeof(ResourcePopupPresenter);
                case UIPresenterKind.BuildingMenu: return typeof(BuildingMenuPresenter);
                case UIPresenterKind.TaskAssignment: return typeof(TaskAssignmentPresenter);
                case UIPresenterKind.DiscipleList: return typeof(DiscipleListPresenter);
                case UIPresenterKind.TimeControl: return typeof(TimeControlPresenter);
                case UIPresenterKind.EventChip: return typeof(WorldEventChipPresenter);
                case UIPresenterKind.TitleMenu: return typeof(TitleMenuPresenter);
                case UIPresenterKind.IngameMenu: return typeof(IngameMenuPresenter);
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }
    }
}
