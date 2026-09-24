using UnityEngine;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    // Bottom main-menu bar: the persistent "สร้าง (Build)" / "ศิษย์ (Disciple)"
    // buttons. Visual-only reskin task — the sprites come from the extracted
    // ปรมาจารย์ครองเซียん assets; each button keeps its own UnityEngine.UI.Button
    // so click handlers can be attached later without changing this view.
    // Buttons with no extracted asset yet (วิถีเซียน / ห้องคลังสินค้า) are kept
    // as plain placeholder buttons so the bar layout is final.
    public class BottomMenuView : UIViewBase
    {
        [Header("Buttons (asset-backed)")]
        [SerializeField] private Button buildButton;
        [SerializeField] private Button discipleButton;

        [Header("Placeholder buttons (no asset yet - keep as-is)")]
        [SerializeField] private Button immortalWayButton;
        [SerializeField] private Button warehouseButton;

        public Button BuildButton => buildButton;
        public Button DiscipleButton => discipleButton;
        public Button ImmortalWayButton => immortalWayButton;
        public Button WarehouseButton => warehouseButton;

        // Data-driven layout (open question #13) - moved verbatim from
        // UIRoot.ApplyBottomMenuLayout: bottom bar hugging the right edge.
        public override void ApplyDefaultLayout()
        {
            // Bottom bar: full width, fixed height, anchored to the bottom edge.
            var rt = GetComponent<RectTransform>();
            if (rt == null) return;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot     = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, 120f);
            rt.anchoredPosition = Vector2.zero;

            var hlg = GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.padding           = new RectOffset(24, 24, 12, 12);
                hlg.spacing           = 16f;
                // Reskin round 2: bar hugs the right edge of the screen
                // (reference images 1-2), ~24 px inset via padding.right.
                hlg.childAlignment    = TextAnchor.MiddleRight;
                hlg.childControlWidth  = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth  = false;
                hlg.childForceExpandHeight = false;
            }

            var csf = GetComponent<ContentSizeFitter>();
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
    }
}

