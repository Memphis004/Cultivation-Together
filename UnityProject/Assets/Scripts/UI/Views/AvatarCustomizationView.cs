using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// Avatar customization panel — View layer only.
    /// หน้าที่ 3 อย่างเท่านั้น: วาด / รับคลิก / ยิง event ออก
    /// ไม่มี if ทาง business logic แม้แต่บรรทัดเดียว
    ///
    /// ใช้ Object Pooling กับปุ่มในกริดตั้งแต่แรก (บทเรียนจาก LogWindowView)
    /// เพราะกดสลับแท็บทีนึง rebuild ทั้งกริด ถ้า Instantiate/Destroy รัวๆ
    /// GC จะกระตุกเห็นชัดตอนสลับเร็วๆ
    ///
    /// Phase 1: เพิ่ม category tab bar (ใบหน้า/ลักษณะ/ร่างกาย) เหนือ slot tab bar
    /// </summary>
    public class AvatarCustomizationView : UIViewBase
    {
        [Header("Header")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text subtitleText;

        [Header("Preview")]
        [SerializeField] private AvatarRenderer previewRenderer;

        [Header("Category Tabs")]
        [SerializeField] private RectTransform categoryTabRoot;
        [SerializeField] private Button        categoryTabPrefab;

        [Header("Slot Tabs")]
        [SerializeField] private RectTransform slotTabRoot;
        [SerializeField] private Button        slotTabPrefab;

        [Header("Option Grid")]
        [SerializeField] private RectTransform     optionGridRoot;
        [SerializeField] private AvatarOptionButton optionButtonPrefab;

        [Header("Footer")]
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button randomizeButton;
        [SerializeField] private TMP_Text errorText;

        // ── events → Presenter ────────────────────────────────
        public event Action<string> CategoryTabClicked;  // category
        public event Action<string> SlotTabClicked;      // slot
        public event Action<string> PartOptionClicked;   // partId
        public event Action ConfirmClicked;
        public event Action CancelClicked;
        public event Action RandomizeClicked;

        // ── pooling ───────────────────────────────────────────
        private readonly List<AvatarOptionButton>  _activeOptions  = new List<AvatarOptionButton>();
        private readonly Stack<AvatarOptionButton> _optionPool     = new Stack<AvatarOptionButton>();
        private readonly List<Button>              _activeSlotTabs = new List<Button>();
        private readonly Stack<Button>             _slotTabPool    = new Stack<Button>();
        private readonly List<Button>              _activeCatTabs  = new List<Button>();
        private readonly Stack<Button>             _catTabPool     = new Stack<Button>();

        private void Awake()
        {
            confirmButton.onClick.AddListener(() => Raise(ConfirmClicked));
            cancelButton.onClick.AddListener(() => Raise(CancelClicked));
            if (randomizeButton != null)
                randomizeButton.onClick.AddListener(() => Raise(RandomizeClicked));

            if (errorText != null) errorText.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            // เคลียร์ทุก handler กัน presenter เก่า (Transient) ถูกอ้างค้าง
            CategoryTabClicked = null;
            SlotTabClicked = null;
            PartOptionClicked = null;
            ConfirmClicked = null;
            CancelClicked = null;
            RandomizeClicked = null;
        }

        private static void Raise(Action a) { if (a != null) a(); }

        // ── API ที่ Presenter เรียก ────────────────────────────

        public void SetHeader(string title, string subtitle)
        {
            if (titleText != null)    titleText.text = title;
            if (subtitleText != null) subtitleText.text = subtitle;
        }

        /// <summary>ส่ง AvatarRenderer ให้ presenter จ่าย pool ให้ (renderer ไม่ resolve DI เอง)</summary>
        public AvatarRenderer PreviewRenderer { get { return previewRenderer; } }

        public void RenderPreview(AvatarAppearance draft)
        {
            if (previewRenderer != null) previewRenderer.SetAppearance(draft);
        }

        /// <summary>渲染 category tabs (ใบหน้า / ลักษณะ / ร่างกาย)</summary>
        public void RenderCategoryTabs(IList<AvatarCategoryTabViewData> tabs)
        {
            ReleaseCategoryTabs();
            for (int i = 0; i < tabs.Count; i++)
            {
                var data = tabs[i];
                var btn  = RentCategoryTab();
                if (btn == null) continue;

                var label = btn.GetComponentInChildren<TMP_Text>();
                if (label != null) label.text = data.DisplayName;

                var colors = btn.colors;
                colors.normalColor = data.IsActive
                    ? new Color(1f, 0.85f, 0.45f)      // แท็บที่เลือก = ทอง
                    : Color.white;
                btn.colors = colors;

                string captured = data.Category;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(delegate
                {
                    var cb = CategoryTabClicked;
                    if (cb != null) cb(captured);
                });
            }
        }

        /// <summary>渲染 slot tabs (ตา/คิ้ว/ปาก/ผม/...)</summary>
        public void RenderSlotTabs(IList<AvatarSlotTabViewData> tabs)
        {
            ReleaseSlotTabs();
            for (int i = 0; i < tabs.Count; i++)
            {
                var data = tabs[i];
                var btn  = RentSlotTab();

                var label = btn.GetComponentInChildren<TMP_Text>();
                if (label != null) label.text = data.DisplayName;

                var colors = btn.colors;
                colors.normalColor = data.IsActive
                    ? new Color(1f, 0.85f, 0.45f)      // แท็บที่เลือก = ทอง
                    : Color.white;
                btn.colors = colors;

                string captured = data.Slot;            // capture กัน closure ผิดตัว
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(delegate
                {
                    var cb = SlotTabClicked;
                    if (cb != null) cb(captured);
                });
            }
        }

        /// <summary>spriteResolver = ให้ Presenter/Pool เป็นคนแปลง path → Sprite (View ไม่โหลดเอง)</summary>
        public void RenderOptions(IList<AvatarPartOptionViewData> options,
                                  Func<string, Sprite> spriteResolver)
        {
            ReleaseOptions();
            for (int i = 0; i < options.Count; i++)
            {
                var data   = options[i];
                var sprite = spriteResolver != null ? spriteResolver(data.SpritePath) : null;
                var btn    = RentOption();
                btn.Bind(data, sprite, HandleOptionClicked);
            }
        }

        private void HandleOptionClicked(string partId)
        {
            var cb = PartOptionClicked;
            if (cb != null) cb(partId);
        }

        public void SetDirty(bool isDirty)
        {
            confirmButton.interactable = isDirty;
        }

        public void ShowError(string message)
        {
            if (errorText == null) return;
            bool has = !string.IsNullOrEmpty(message);
            errorText.gameObject.SetActive(has);
            errorText.text = message;
        }

        // ── pooling helpers ───────────────────────────────────

        private AvatarOptionButton RentOption()
        {
            AvatarOptionButton b = _optionPool.Count > 0
                ? _optionPool.Pop()
                : Instantiate(optionButtonPrefab, optionGridRoot);
            b.transform.SetAsLastSibling();          // รักษาลำดับตาม def-table
            b.gameObject.SetActive(true);
            _activeOptions.Add(b);
            return b;
        }

        private void ReleaseOptions()
        {
            for (int i = 0; i < _activeOptions.Count; i++)
            {
                _activeOptions[i].gameObject.SetActive(false);
                _optionPool.Push(_activeOptions[i]);
            }
            _activeOptions.Clear();
        }

        private Button RentSlotTab()
        {
            Button b = _slotTabPool.Count > 0 ? _slotTabPool.Pop() : Instantiate(slotTabPrefab, slotTabRoot);
            b.transform.SetAsLastSibling();
            b.gameObject.SetActive(true);
            _activeSlotTabs.Add(b);
            return b;
        }

        private void ReleaseSlotTabs()
        {
            for (int i = 0; i < _activeSlotTabs.Count; i++)
            {
                _activeSlotTabs[i].onClick.RemoveAllListeners();
                _activeSlotTabs[i].gameObject.SetActive(false);
                _slotTabPool.Push(_activeSlotTabs[i]);
            }
            _activeSlotTabs.Clear();
        }

        private Button RentCategoryTab()
        {
            Button b = _catTabPool.Count > 0 ? _catTabPool.Pop()
                : (categoryTabPrefab != null ? Instantiate(categoryTabPrefab, categoryTabRoot) : null);
            if (b == null) return null;
            b.transform.SetAsLastSibling();
            b.gameObject.SetActive(true);
            _activeCatTabs.Add(b);
            return b;
        }

        private void ReleaseCategoryTabs()
        {
            for (int i = 0; i < _activeCatTabs.Count; i++)
            {
                _activeCatTabs[i].onClick.RemoveAllListeners();
                _activeCatTabs[i].gameObject.SetActive(false);
                _catTabPool.Push(_activeCatTabs[i]);
            }
            _activeCatTabs.Clear();
        }
    }
}
