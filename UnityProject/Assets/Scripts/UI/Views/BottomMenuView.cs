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
    }
}

