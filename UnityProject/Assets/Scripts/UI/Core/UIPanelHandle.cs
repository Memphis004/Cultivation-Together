using UnityEngine;

namespace Xianxia.Sect.UI
{
    public class UIPanelHandle
    {
        public string PanelId { get; }
        public IUIView View { get; }
        public IUIViewPresenter Presenter { get; }

        /// <summary>
        /// Is this panel's view still a live Unity object?
        ///
        /// Deliberately NOT <c>View != null</c>: <see cref="View"/> is typed as the INTERFACE
        /// <see cref="IUIView"/>, so that comparison is a plain reference test (the interface
        /// declares no operator) and a DESTROYED view still compares non-null. Reading a member
        /// of it then throws MissingReferenceException (<c>UIViewBase.GameObject =&gt; gameObject</c>).
        /// A handle outlives its panel whenever something destroys the instance without going
        /// through <see cref="UIService.Close"/> — a session boundary closing the session UI is the
        /// case that bit: the per-frame world-event refresh threw here every frame, so a restored
        /// pending event never got its popup back.
        /// </summary>
        public bool IsAlive => View is Object unityObject && unityObject != null;

        internal UIPanelHandle(string panelId, IUIView view, IUIViewPresenter presenter)
        {
            PanelId = panelId;
            View = view;
            Presenter = presenter;
        }
    }
}
