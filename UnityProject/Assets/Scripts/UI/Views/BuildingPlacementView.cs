using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Xianxia.Sect.Building;

namespace Xianxia.Sect.UI
{
    /// <summary>Open args for the floating placement controls — close callback
    /// ผูกกับ ghost ปัจจุบัน (opener เป็นคนปิด, แพทเทิร์นเดียวกับ ResourcePopupArgs).</summary>
    public sealed class BuildingPlacementArgs
    {
        public System.Action CloseCallback;
    }

    /// <summary>
    /// ปุ่มลอย 3 ปุ่มระหว่าง placement mode (building-system.md §5, screenshot 3):
    /// ✕ ยกเลิก | ⟲ หมุน | ✓ ยืนยัน + label แสดงเหตุผลเมื่อวางไม่ได้.
    /// ตั้งใจสร้างโดย BuildingMenuPresenter (ไม่ใช่ catalog panel — ไม่เพิ่ม
    /// UIPresenterKind ใหม่) จึงมี Build() static ประกอบ hierarchy เองในโค้ด
    /// ทั้งชุด (ไม่มี prefab ให้ desync) และ layout แบบ anchor ตายตั้งกลางล่างจอ
    /// </summary>
    public class BuildingPlacementView : UIViewBase
    {
        // ปุ่มลอยนี้ code-built นอก prefab จึงไม่ได้ฟอนต์ผ่าน titleText (LiberationSans
        // ไม่มีอักษรไทย — ✕/⟲/✓ เคยแสดงเป็นสี่เหลี่ยมว่าง) โหลดจาก Resources ตรง ๆ
        private const string ThaiFontResourcePath = "Fonts/THSarabunPSK SDF";
        private static TMP_FontAsset _thaiFont;

        private static TMP_FontAsset ThaiFont()
        {
            if (_thaiFont == null)
                _thaiFont = Resources.Load<TMP_FontAsset>(ThaiFontResourcePath);
            return _thaiFont;
        }

        private RectTransform _rootRect;
        private Button _cancelButton;
        private Button _rotateButton;
        private Button _confirmButton;
        private TMP_Text _statusText;

        /// <summary>root RectTransform (anchor ยืดเต็มจอ) — system ใช้วางตำแหน่งแถบปุ่มลอย</summary>
        public RectTransform RootRect => _rootRect;
        public RectTransform ButtonBarRect { get; private set; }

        public event Action CancelClicked;
        public event Action RotateClicked;
        public event Action ConfirmClicked;

        public Button CancelButton => _cancelButton;
        public Button RotateButton => _rotateButton;
        public Button ConfirmButton => _confirmButton;
        public TMP_Text StatusText => _statusText;

        /// <summary>สร้าง hierarchy ทั้งก้อนในโค้ด — คืน view พร้อมใช้ (ยังไม่ Show).
        /// root ไม่มี Image → ไม่บัง raycast: ลาก ghost ผ่านด้านหลังได้ ปุ่มเท่านั้นที่รับคลิก</summary>
        public static BuildingPlacementView Build(Transform uiParent)
        {
            var rootGo = new GameObject("BuildingPlacementPanel", typeof(RectTransform));
            var rt = (RectTransform)rootGo.transform;
            rt.SetParent(uiParent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var view = rootGo.AddComponent<BuildingPlacementView>();
            view._rootRect = rt;

            // แถบปุ่มลอย: ล่างกลางจอ เหนือ bottom menu (120px)
            var barGo = new GameObject("ButtonBar", typeof(RectTransform));
            var barRt = (RectTransform)barGo.transform;
            barRt.SetParent(rt, false);
            barRt.anchorMin = new Vector2(0.5f, 0f);
            barRt.anchorMax = new Vector2(0.5f, 0f);
            barRt.pivot = new Vector2(0.5f, 0f);
            barRt.sizeDelta = new Vector2(460f, 72f);
            barRt.anchoredPosition = new Vector2(0f, 136f);

            view.ButtonBarRect = barRt;

            var hlg = barGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 24f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            // ป้ายภาษาไทย ไม่ใช้สัญลักษณ์ ✕/⟲/✓ — glyph เหล่านั้นไม่มีใน font atlas
            // (แสดงเป็นสี่เหลี่ยมว่าง) และช่วยให้ผู้เล่นรู้ว่าปุ่มทำอะไรชัดเจน
            view._cancelButton = view.CreateBarButton(barRt, "CancelButton", "ยกเลิก", new Color(0.75f, 0.3f, 0.28f), 150f);
            view._rotateButton = view.CreateBarButton(barRt, "RotateButton", "หมุน", new Color(0.45f, 0.55f, 0.7f), 110f);
            view._confirmButton = view.CreateBarButton(barRt, "ConfirmButton", "วาง", new Color(0.35f, 0.65f, 0.4f), 150f);

            // status label เหนือแถบปุ่ม (เหตุผลตอนวางไม่ได้ — ข้อความเดียวรวมทุกกรณี §3.2)
            var statusGo = new GameObject("StatusText", typeof(RectTransform));
            var statusRt = (RectTransform)statusGo.transform;
            statusRt.SetParent(rt, false);
            statusRt.anchorMin = new Vector2(0.5f, 0f);
            statusRt.anchorMax = new Vector2(0.5f, 0f);
            statusRt.pivot = new Vector2(0.5f, 0f);
            statusRt.sizeDelta = new Vector2(700f, 44f);
            statusRt.anchoredPosition = new Vector2(0f, 204f);

            var tmp = statusGo.AddComponent<TextMeshProUGUI>();
            var font = ThaiFont();
            if (font != null) tmp.font = font;
            tmp.fontSize = 30f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            view._statusText = tmp;

            rootGo.SetActive(false);
            return view;
        }

        private Button CreateBarButton(RectTransform parent, string name, string label, Color color, float width)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(width, 72f);

            var image = go.AddComponent<Image>();
            image.color = color;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var textGo = new GameObject("Label", typeof(RectTransform));
            var textRt = (RectTransform)textGo.transform;
            textRt.SetParent(rect, false);
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            var font = ThaiFont();
            if (font != null) tmp.font = font;
            tmp.text = label;
            tmp.fontSize = 30f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;

            return button;
        }

        public override void Show()
        {
            WireButtons();
            gameObject.SetActive(true);
        }

        private bool _wired;

        private void WireButtons()
        {
            if (_wired) return;
            if (_cancelButton != null) _cancelButton.onClick.AddListener(OnCancelClicked);
            if (_rotateButton != null) _rotateButton.onClick.AddListener(OnRotateClicked);
            if (_confirmButton != null) _confirmButton.onClick.AddListener(OnConfirmClicked);
            _wired = true;
        }

        private void OnDestroy()
        {
            if (_wired && _cancelButton != null) _cancelButton.onClick.RemoveListener(OnCancelClicked);
            if (_wired && _rotateButton != null) _rotateButton.onClick.RemoveListener(OnRotateClicked);
            if (_wired && _confirmButton != null) _confirmButton.onClick.RemoveListener(OnConfirmClicked);
            _wired = false;
            CancelClicked = null;
            RotateClicked = null;
            ConfirmClicked = null;
        }

        private void OnCancelClicked() => CancelClicked?.Invoke();
        private void OnRotateClicked() => RotateClicked?.Invoke();
        private void OnConfirmClicked() => ConfirmClicked?.Invoke();

        /// <summary>ข้อความสถานะ (เหตุผลตอนวางไม่ได้ / ยืนยันสำเร็จ) — ว่าง = ซ่อน</summary>
        public void SetStatus(string message)
        {
            if (_statusText != null) _statusText.text = message ?? string.Empty;
        }
    }
}
