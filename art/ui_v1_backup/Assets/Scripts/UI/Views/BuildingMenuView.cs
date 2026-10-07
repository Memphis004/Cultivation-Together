using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Xianxia.Sect.Building;
using Xianxia.Sect.Building;


namespace Xianxia.Sect.UI
{
    /// <summary>Open args for the building menu — close callback pattern
    /// เดียวกับ ResourcePopupArgs/DiscipleListArgs (opener เป็นคนปิด panel).</summary>
    public sealed class BuildingMenuArgs
    {
        public System.Action CloseCallback;
    }

    /// <summary>แท็บหมวดหมู่ 1 ปุ่ม (ผลิต/สิ่งอำนวยความสะดวก/ร้านค้า/ภูมิทัศน์)</summary>
    public sealed class BuildingTabCell : MonoBehaviour
    {
        private BuildingCategory _category;
        private Button _button;
        private TMP_Text _label;
        private Image _background;

        public event Action<BuildingCategory> Clicked;

        public void Init(BuildingCategory category, Button button, TMP_Text label, Image background)
        {
            _category = category;
            _button = button;
            _label = label;
            _background = background;

            if (_button != null) _button.onClick.AddListener(OnClick);
        }

        public void SetLabel(string text)
        {
            if (_label != null) _label.text = text;
        }

        /// <summary>ไฮไลต์แท็บที่กำลังเลือก (สีพื้นเข้ม/สว่าง)</summary>
        public void SetSelected(bool selected)
        {
            if (_background != null)
                _background.color = selected
                    ? new Color(0.85f, 0.72f, 0.45f)
                    : new Color(0.8f, 0.8f, 0.8f);
        }

        /// <summary>Public on purpose: tests invoke directly (no reflection — C12).</summary>
        public void Click() => OnClick();

        private void OnClick() => Clicked?.Invoke(_category);

        private void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(OnClick);
            Clicked = null;
        }
    }

    /// <summary>การ์ดอาคาร 1 ใบในกริดของหมวดที่เลือก (thumbnail + ชื่อ + ราคา)</summary>
    public sealed class BuildingItemCell : MonoBehaviour
    {
        private string _defId;
        private Button _button;
        private TMP_Text _nameText;
        private TMP_Text _costText;
        private Image _thumb;

        public event Action<string> Clicked;

        public string DefId => _defId;

        public void Init(string defId, Button button, TMP_Text nameText, TMP_Text costText, Image thumb)
        {
            _defId = defId;
            _button = button;
            _nameText = nameText;
            _costText = costText;
            _thumb = thumb;

            if (_button != null) _button.onClick.AddListener(OnClick);
        }

        public void Set(string defId, string name, string cost, Sprite thumbnail)
        {
            _defId = defId;
            if (_nameText != null) _nameText.text = name;
            if (_costText != null) _costText.text = cost;
            if (_thumb != null)
            {
                _thumb.sprite = thumbnail;
                // ไม่มี art จริง (SpritePath ว่าง) = placeholder สี่เหลี่ยมสีพื้น
                _thumb.color = thumbnail != null ? Color.white : new Color(0.6f, 0.55f, 0.45f);
            }
        }

        /// <summary>Public on purpose: tests invoke directly (no reflection — C12).</summary>
        public void Click() => OnClick();

        private void OnClick() => Clicked?.Invoke(_defId);

        private void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(OnClick);
            Clicked = null;
        }
    }

    /// <summary>
    /// Building menu ("สร้าง" บน bottom menu): 4 แท็บหมวดหมู่ + กริดการ์ดอาคาร
    /// ของหมวดที่เลือก (building-system.md §4). โครงหลัก (title/close/tabs/scroll)
    /// wire จาก prefab/generator; แท็บและการ์ดทั้งหมดสร้าง runtime ใน CreateTab/
    /// CreateItem — แพทเทิร์นเดียวกับ ResourcePopupView.CreateRow ที่ template
    /// เคยหายตอน import
    /// </summary>
    public class BuildingMenuView : UIViewBase
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Button closeButton;
        [SerializeField] private RectTransform tabRoot;
        [SerializeField] private RectTransform itemRoot;
        [SerializeField] private ScrollRect scrollRect;

        public event Action CloseClicked;

        public TMP_Text TitleText => titleText;
        public RectTransform TabRoot => tabRoot;
        public RectTransform ItemRoot => itemRoot;

        public override void ApplyDefaultLayout()
        {
            var rt = GetComponent<RectTransform>();
            if (rt == null) return;
            // Centered modal — ใหญ่กว่า ResourcePopup เล็กน้อยเพราะมีทั้งแท็บและกริด
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(640f, 680f);
            rt.anchoredPosition = Vector2.zero;
        }

        private bool _closeWired;

        private void Awake()
        {
            EnsureTabLayout();
            EnsureItemLayout();
        }

        // Lazy wire ใน Show() — production path รับประกันว่า UIService.Open เรียก
        // Show() หลัง presenter.Bind เสมอ (DiscipleListView pattern)
        public override void Show()
        {
            EnsureCloseWired();
            gameObject.SetActive(true);
        }

        private void EnsureCloseWired()
        {
            if (_closeWired || closeButton == null) return;
            closeButton.onClick.AddListener(OnCloseClicked);
            _closeWired = true;
        }

        private void OnDestroy()
        {
            if (closeButton != null && _closeWired)
                closeButton.onClick.RemoveListener(OnCloseClicked);
            _closeWired = false;
            CloseClicked = null;
        }

        private void OnCloseClicked() => CloseClicked?.Invoke();

        private void EnsureTabLayout()
        {
            if (tabRoot == null) return;
            if (tabRoot.GetComponent<HorizontalLayoutGroup>() != null) return;

            var hlg = tabRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.UpperCenter;
            hlg.spacing = 8f;
            hlg.padding = new RectOffset(8, 8, 4, 4);
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = false;
        }

        private void EnsureItemLayout()
        {
            if (itemRoot == null) return;

            // การ์ดวางแบบกริด 2 คอลัมน์ (thumbnail grid ต่อหมวด §4)
            var grid = itemRoot.GetComponent<GridLayoutGroup>();
            if (grid == null)
            {
                grid = itemRoot.gameObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(280f, 110f);
                grid.spacing = new Vector2(8f, 8f);
                grid.childAlignment = TextAnchor.UpperCenter;
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = 2;
            }

            // scroll content ต้องยืดตามการ์ด (PreferredSize — ตัวเดียวที่อนุญาตใน prefab
            // เพราะ root ห้าม ContentSizeFitter แต่ content ใน scroll ต้องมี)
            var fitter = itemRoot.GetComponent<ContentSizeFitter>();
            if (fitter == null) fitter = itemRoot.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        public BuildingTabCell CreateTab(BuildingCategory category)
        {
            if (tabRoot == null)
            {
                Debug.LogError("[BuildingMenuView] tabRoot not wired on prefab");
                return null;
            }

            var go = new GameObject("Tab", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(tabRoot, false);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 56f;
            le.flexibleWidth = 1f;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.8f, 0.8f, 0.8f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var label = CreateText("Label", TitleFont(), 30f, TextAlignmentOptions.Center, rect);

            var tab = go.AddComponent<BuildingTabCell>();
            tab.Init(category, button, label, image); // Init ครั้งเดียว — listener ผูกตรงนี้จุดเดียว
            return tab;
        }

        public BuildingItemCell CreateItem()
        {
            if (itemRoot == null)
            {
                Debug.LogError("[BuildingMenuView] itemRoot not wired on prefab");
                return null;
            }

            // การ์ด: [thumb 90x90] [ชื่อ + ราคา] — สร้างในโค้ดทั้งใบ
            var go = new GameObject("BuildingCard", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(itemRoot, false);

            var image = go.AddComponent<Image>();
            image.color = new Color(0.13f, 0.12f, 0.10f, 0.85f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var thumbGo = new GameObject("Thumb", typeof(RectTransform));
            var thumbRect = (RectTransform)thumbGo.transform;
            thumbRect.SetParent(rect, false);
            thumbGo.AddComponent<Image>();
            var thumbLe = thumbGo.AddComponent<LayoutElement>();
            thumbLe.preferredWidth = 90f;
            thumbLe.preferredHeight = 90f;
            var thumb = thumbGo.GetComponent<Image>();
            thumb.preserveAspect = true;
            thumb.raycastTarget = false;

            var textCol = new GameObject("TextColumn", typeof(RectTransform));
            var textRect = (RectTransform)textCol.transform;
            textRect.SetParent(rect, false);
            var vlg = textCol.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2f;
            vlg.childAlignment = TextAnchor.MiddleLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var textLe = textCol.AddComponent<LayoutElement>();
            textLe.flexibleWidth = 1f;

            var font = TitleFont();
            var nameText = CreateText("Name", font, 28f, TextAlignmentOptions.Left, textRect);
            var nameLe = nameText.gameObject.AddComponent<LayoutElement>();
            nameLe.preferredHeight = 38f;

            var costText = CreateText("Cost", font, 24f, TextAlignmentOptions.Left, textRect);
            costText.color = new Color(0.7f, 0.68f, 0.6f);
            var costLe = costText.gameObject.AddComponent<LayoutElement>();
            costLe.preferredHeight = 32f;

            var cell = go.AddComponent<BuildingItemCell>();
            cell.Init(null, button, nameText, costText, thumb);
            return cell;
        }

        public void ClearTabs()
        {
            if (tabRoot == null) return;
            for (var i = tabRoot.childCount - 1; i >= 0; i--)
                DestroyChild(tabRoot.GetChild(i).gameObject);
        }

        public void ClearItems()
        {
            if (itemRoot == null) return;
            for (var i = itemRoot.childCount - 1; i >= 0; i--)
                DestroyChild(itemRoot.GetChild(i).gameObject);
        }

        // Destroy ปกติ defer ถึงจบเฟรม — ใน EditMode (ไม่มี player loop) การ์ด/แท็บเก่า
        // จะค้างเป็น child แล้วโดนนับซ้ำตอน re-build จึงต้อง immediate เมื่อไม่ได้ play
        private static void DestroyChild(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        private TMP_FontAsset TitleFont()
        {
            // Thai-capable font inherited from the panel title (ResourcePopup pattern —
            // default LiberationSans ไม่มีอักษรไทย)
            return titleText != null ? titleText.font : null;
        }

        private static TMP_Text CreateText(
            string name, TMP_FontAsset font, float size, TextAlignmentOptions alignment, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            var text = go.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.fontSize = size;
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }
    }
}
