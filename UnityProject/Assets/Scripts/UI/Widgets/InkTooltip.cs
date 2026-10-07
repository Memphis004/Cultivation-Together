using UnityEngine;
using UnityEngine.EventSystems;

namespace Xianxia.Sect.UI
{
    public sealed class InkTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private GameObject tip;
        public void Configure(GameObject value) => tip = value;
        private void Awake() { if (tip == null) tip = transform.Find("Tooltip")?.gameObject; }
        public void OnPointerEnter(PointerEventData data) { if (tip != null) { tip.SetActive(true); tip.transform.SetAsLastSibling(); } }
        public void OnPointerExit(PointerEventData data) { if (tip != null) tip.SetActive(false); }
        private void OnDisable() { if (tip != null) tip.SetActive(false); }
    }
}
