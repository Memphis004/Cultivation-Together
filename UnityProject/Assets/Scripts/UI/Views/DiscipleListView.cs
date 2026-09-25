using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    // One row (card) inside the disciple list: icon + name + rank/task + value.
    // Rows are built entirely in code (see DiscipleListView.CreateCard); nothing
    // on the card is serialized, so the prefab cannot desync from the view —
    // same discipline as ResourcePopupView.CreateRow (its prefab-serialized
    // row template kept getting lost/stale across imports).
    public sealed class DiscipleListCard : MonoBehaviour
    {
        private TMP_Text nameText;
        private TMP_Text detailText;
        private TMP_Text walletText;
        private Image iconImage;
        private Button button;

        private string discipleId;

        public event Action<string> Clicked;

        public string DiscipleId => discipleId;

        public void Init(TMP_Text name, TMP_Text detail, TMP_Text wallet, Image icon, Button cardButton)
        {
            nameText = name;
            detailText = detail;
            walletText = wallet;
            iconImage = icon;
            button = cardButton;

            if (button != null) button.onClick.AddListener(Click);
        }

        public void Set(string id, string name, string detail, string wallet, Sprite icon)
        {
            discipleId = id;
            if (nameText != null) nameText.text = name;
            if (detailText != null) detailText.text = detail;
            if (walletText != null) walletText.text = wallet;
            if (iconImage != null)
            {
                iconImage.sprite = icon;
                iconImage.enabled = icon != null;
            }
        }

        public string Name => nameText != null ? nameText.text : null;
        public Sprite Icon => iconImage != null ? iconImage.sprite : null;

        /// <summary>Public on purpose: the card button listens to this, and tests
        /// invoke it directly (no reflection — project rule C12).</summary>
        public void Click() => Clicked?.Invoke(discipleId);

        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(Click);
            Clicked = null;
        }
    }

    /// <summary>
    /// Disciple list panel ("ศิษย์" บน bottom menu): scrollable card list,
    /// one card per disciple. Cards are constructed at runtime in
    /// <see cref="CreateCard"/> (ResourcePopupView pattern) — only
    /// titleText / closeButton / scrollRoot are wired on the prefab.
    /// Cards load baked square icons from Resources/Avatar/Icons (AvatarIconBaker).
    /// </summary>
    public class DiscipleListView : UIViewBase
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Button closeButton;
        [SerializeField] private RectTransform cardsRoot;
        [SerializeField] private ScrollRect scrollRect;

        public event Action CloseClicked;

        public TMP_Text TitleText => titleText;
        public RectTransform CardsRoot => cardsRoot;

        public override void ApplyDefaultLayout()
        {
            var rt = GetComponent<RectTransform>();
            if (rt == null) return;
            // Centered modal, footprint family เดียวกับ ResourcePopup
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(460f, 560f);
            rt.anchoredPosition = Vector2.zero;
        }

        private bool _closeWired;

        private void Awake()
        {
            EnsureCardsLayout();
        }

        // Wire แบบ lazy ใน Show() — production path รับประกันว่า UIService.Open
        // เรียก view.Show() หลัง presenter.Bind เสมอ (refs ครบแล้วทั้ง prefab
        // และ test fixture ที่ wire ผ่าน SerializedObject) lazy flag ทำให้
        // listener ถูกใส่ครั้งเดียวเสมอ และไม่พึ่ง Awake/OnEnable ซึ่ง
        // ไม่ถูกเรียกใน EditMode test สำหรับสคริปต์ธรรมดา
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

        // The prefab normally provides this; add it defensively so the list
        // still stacks cards correctly if the component was dropped on import
        // (same defensive pattern as ResourcePopupView.EnsureRowsLayout).
        private void EnsureCardsLayout()
        {
            if (cardsRoot == null) return;

            var layout = cardsRoot.GetComponent<VerticalLayoutGroup>();
            if (layout != null) return;

            layout = cardsRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = 8f;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        public DiscipleListCard CreateCard()
        {
            if (cardsRoot == null)
            {
                Debug.LogError("[DiscipleListView] cardsRoot not wired on prefab");
                return null;
            }

            // Build the card entirely in code so it cannot desync from the
            // prefab file (ResourcePopupView.CreateRow lesson).
            var cardGo = new GameObject("DiscipleCard", typeof(RectTransform));
            var cardRect = (RectTransform)cardGo.transform;
            cardRect.SetParent(cardsRoot, false);

            var cardElement = cardGo.AddComponent<LayoutElement>();
            cardElement.preferredHeight = 96f;

            var cardImage = cardGo.AddComponent<Image>();
            cardImage.color = new Color(0.13f, 0.12f, 0.10f, 0.85f);

            var cardButton = cardGo.AddComponent<Button>();
            cardButton.targetGraphic = cardImage;

            // Thai-capable font inherited from the panel title (CreateText ของ
            // ResourcePopupView หยิบ titleText.font เหมือนกัน) — font default
            // (LiberationSans) ไม่มีอักษรไทย
            var font = titleText != null ? titleText.font : null;

            // Icon (square, left)
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.SetParent(cardRect, false);
            iconGo.AddComponent<Image>();
            var iconElement = iconGo.AddComponent<LayoutElement>();
            iconElement.preferredWidth = 88f;
            iconElement.preferredHeight = 88f;
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            // Text column (middle)
            var textCol = new GameObject("TextColumn", typeof(RectTransform));
            var textRect = (RectTransform)textCol.transform;
            textRect.SetParent(cardRect, false);
            textCol.AddComponent<VerticalLayoutGroup>();
            var textVlg = textCol.GetComponent<VerticalLayoutGroup>();
            textVlg.spacing = 2f;
            textVlg.childAlignment = TextAnchor.MiddleLeft;
            textVlg.childControlWidth = true;
            textVlg.childControlHeight = true;
            textVlg.childForceExpandWidth = true;
            textVlg.childForceExpandHeight = false;
            var textElement = textCol.AddComponent<LayoutElement>();
            textElement.flexibleWidth = 1f;

            var nameText = CreateText("Name", font, 30f, TextAlignmentOptions.Left, textRect);
            var detailText = CreateText("Detail", font, 24f, TextAlignmentOptions.Left, textRect);
            detailText.color = new Color(0.7f, 0.68f, 0.6f);

            // Wallet (right)
            var walletText = CreateText("Wallet", font, 24f, TextAlignmentOptions.Right, cardRect);
            var walletElement = walletText.gameObject.AddComponent<LayoutElement>();
            walletElement.preferredWidth = 190f;

            var card = cardGo.AddComponent<DiscipleListCard>();
            card.Init(nameText, detailText, walletText, iconImg, cardButton);
            return card;
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

        public void ClearCards()
        {
            if (cardsRoot == null) return;

            for (var i = cardsRoot.childCount - 1; i >= 0; i--)
                Destroy(cardsRoot.GetChild(i).gameObject);
        }
    }
}
