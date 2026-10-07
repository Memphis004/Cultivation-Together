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

        /// <summary>
        /// Data-driven layout hook (open question #13): each concrete view
        /// overrides this with its own default layout. UIService.Open() calls
        /// it right after instantiation, and UIRoot.Awake() calls it on any
        /// panel already baked into the scene — replacing the old
        /// string-matching on prefab names in UIRoot.ApplyLayout().
        /// </summary>
        public virtual void ApplyDefaultLayout()
        {
            // No-op by default (e.g. DiscipleDetail — layout is prefab-driven).
        }
    }
}
