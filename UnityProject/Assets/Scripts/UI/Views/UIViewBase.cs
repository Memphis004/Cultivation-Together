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
