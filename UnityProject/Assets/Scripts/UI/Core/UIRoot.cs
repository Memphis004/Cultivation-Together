using UnityEngine;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// Container that every UI panel is parented under.
    /// On Awake it also forces its own RectTransform to stretch-fill the
    /// Canvas and applies the correct layout to every known child panel.
    /// </summary>
    public class UIRoot : MonoBehaviour
    {
        [SerializeField] private Transform root;
        public Transform Root => root != null ? root : transform;

        private void Awake()
        {
            // Force this RectTransform to fill the entire Canvas.
            var myRt = GetComponent<RectTransform>();
            if (myRt != null)
            {
                myRt.anchorMin = Vector2.zero;
                myRt.anchorMax = Vector2.one;
                myRt.offsetMin = Vector2.zero;
                myRt.offsetMax = Vector2.zero;
            }

            // Fix every already-existing child (baked-in prefab clones).
            foreach (Transform child in transform)
            {
                ApplyLayout(child.gameObject);
            }
        }

        /// <summary>
        /// Also called by UIService after instantiating a new panel.
        /// </summary>
        public static void ApplyLayout(GameObject go)
        {
            var name  = go.name;
            var rt    = go.GetComponent<RectTransform>();
            if (rt == null) return;

            if (name.StartsWith("ResourceHud"))
                ApplyResourceHudLayout(rt);
            else if (name.StartsWith("EventPopup"))
                ApplyEventPopupLayout(rt);
            else if (name.StartsWith("LogWindow"))
                ApplyLogWindowLayout(rt);
            else if (name.StartsWith("AvatarCustomization"))
                ApplyAvatarCustomizationLayout(rt);
            else if (name.StartsWith("BottomMenu"))
                ApplyBottomMenuLayout(rt);
            else if (name.StartsWith("ResourcePopup"))
                ApplyResourcePopupLayout(rt);
        }

        // ---------- ResourceHud ----------
        private static void ApplyResourceHudLayout(RectTransform rt)
        {
            // Top-stretch bar: full width, 100 px tall.
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 100f);
            rt.anchoredPosition = Vector2.zero;

            var hlg = rt.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.padding           = new RectOffset(20, 20, 10, 10);
                hlg.spacing           = 12f;
                hlg.childAlignment    = TextAnchor.MiddleLeft;
                hlg.childControlWidth  = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth  = true;
                hlg.childForceExpandHeight = true;
            }

            var csf = rt.GetComponent<ContentSizeFitter>();
            if (csf != null)
            {
                csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                csf.verticalFit   = ContentSizeFitter.FitMode.Unconstrained;
            }

            foreach (Transform child in rt)
            {
                var cr = child.GetComponent<RectTransform>();
                if (cr == null) continue;
                cr.anchorMin = new Vector2(0.5f, 0.5f);
                cr.anchorMax = new Vector2(0.5f, 0.5f);
                cr.pivot     = new Vector2(0.5f, 0.5f);

                cr.sizeDelta = new Vector2(150f, 80f);
                if (child.name == "SpiritStonesText" || child.name == "ContributionText")
                {
                    var text = child.GetComponent<TMPro.TMP_Text>();
                    if (text != null)
                    {
                        text.alignment = TMPro.TextAlignmentOptions.MidlineRight;
                        text.margin = new Vector4(42f, 0f, 8f, 0f);
                    }
                }
            }
        }

        // ---------- BottomMenu ----------
        private static void ApplyBottomMenuLayout(RectTransform rt)
        {
            // Bottom bar: full width, fixed height, anchored to the bottom edge.
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot     = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, 120f);
            rt.anchoredPosition = Vector2.zero;

            var hlg = rt.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.padding           = new RectOffset(24, 24, 12, 12);
                hlg.spacing           = 16f;
                hlg.childAlignment    = TextAnchor.MiddleCenter;
                hlg.childControlWidth  = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth  = false;
                hlg.childForceExpandHeight = false;
            }

            var csf = rt.GetComponent<ContentSizeFitter>();
            if (csf != null)
            {
                csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                csf.verticalFit   = ContentSizeFitter.FitMode.Unconstrained;
            }

            // Buttons keep their native sprite aspect (96x95): width ~110, height ~110.
            foreach (Transform child in rt)
            {
                var cr = child.GetComponent<RectTransform>();
                if (cr == null) continue;
                cr.anchorMin = new Vector2(0.5f, 0.5f);
                cr.anchorMax = new Vector2(0.5f, 0.5f);
                cr.pivot     = new Vector2(0.5f, 0.5f);
                cr.sizeDelta = new Vector2(110f, 110f);
            }
        }

        // ---------- ResourcePopup (คลังสินค้า) ----------
        private static void ApplyResourcePopupLayout(RectTransform rt)
        {
            // Centered modal, same footprint family as EventPopup.
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(460f, 520f);
            rt.anchoredPosition = Vector2.zero;

            var csf = rt.GetComponent<ContentSizeFitter>();
            if (csf != null)
            {
                csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                csf.verticalFit   = ContentSizeFitter.FitMode.Unconstrained;
            }

            // Layout of children is fully prefab-driven (rows live under
            // RowsRoot with their own VerticalLayoutGroup).
        }

        // ---------- EventPopup ----------
        private static void ApplyEventPopupLayout(RectTransform rt)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(600f, 400f);
            rt.anchoredPosition = Vector2.zero;

            var vlg = rt.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.padding           = new RectOffset(20, 20, 20, 20);
                vlg.spacing           = 15f;
                vlg.childAlignment    = TextAnchor.UpperCenter;
                vlg.childControlWidth  = true;
                vlg.childControlHeight = false;
                vlg.childForceExpandWidth  = true;
                vlg.childForceExpandHeight = false;
            }

            var csf = rt.GetComponent<ContentSizeFitter>();
            if (csf != null)
            {
                csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
            }
        }

        // ---------- AvatarCustomization ----------
        private static void ApplyAvatarCustomizationLayout(RectTransform rt)
        {
            // Full-screen modal: centered, 900x620
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(900f, 620f);
            rt.anchoredPosition = Vector2.zero;

            // ContentSizeFitter must be Unconstrained on the panel root
            // (the real layout is driven by anchors + sizeDelta above)
            var csf = rt.GetComponent<ContentSizeFitter>();
            if (csf != null)
            {
                csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                csf.verticalFit   = ContentSizeFitter.FitMode.Unconstrained;
            }
        }

        // ---------- LogWindow ----------
        private static void ApplyLogWindowLayout(RectTransform rt)
        {
            // Bottom-left anchored log window: 400x300
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot     = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(400f, 300f);
            rt.anchoredPosition = new Vector2(10f, 10f);
        }
    }
}
