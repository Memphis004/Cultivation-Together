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
                existing.View.Show();
                existing.Presenter.OnOpen(args);
                return existing;
            }

            var definition = _catalog.Get(panelId);
            var instance = UnityEngine.Object.Instantiate(definition.Prefab, _uiRoot.Root);

            // --- Ensure the RectTransform fills the parent correctly ---
            UIRoot.ApplyLayout(instance);

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

        public void Close(string panelId)
        {
            if (!_openPanels.TryGetValue(panelId, out var handle)) return;

            handle.Presenter.OnClose();
            handle.View.Hide();
            handle.Presenter.Dispose();

            if (handle.View.GameObject != null)
            {
                UnityEngine.Object.Destroy(handle.View.GameObject);
            }

            _openPanels.Remove(panelId);
        }

        private static Type ResolvePresenterType(UIPresenterKind kind)
        {
            switch (kind)
            {
                case UIPresenterKind.EventPopup: return typeof(EventPopupPresenter);
                case UIPresenterKind.ResourceHud: return typeof(ResourceHudPresenter);
                case UIPresenterKind.LogWindow: return typeof(LogWindowPresenter);
                case UIPresenterKind.AvatarCustomization: return typeof(AvatarCustomizationPresenter);
                case UIPresenterKind.DiscipleDetail: return typeof(DiscipleDetailPresenter);
                case UIPresenterKind.DiscipleList:
                    throw new NotImplementedException("DiscipleListPresenter is not implemented yet.");
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }
    }
}
