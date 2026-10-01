using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    // One card in the disciple list, laid out VERTICALLY like the reference art:
    // portrait on top, name under it, stat band at the bottom. Cards are built
    // entirely in code (see DiscipleListView.CreateCard); nothing on the card is
    // serialized, so the prefab cannot desync from the view — same discipline as
    // ResourcePopupView.CreateRow.
    //
    // Task C — tier visuals (reference image: framed cards + numbered corner
    // badges): the card body/inner panel are tinted per DiscipleState.Rank and
    // a circular dot with the roster order sits on the card's top-left corner.
    // All tier colours/behaviour live in ApplyTier/TierColor so tests and a
    // later art pass override them in one place.
    public sealed class DiscipleListCard : MonoBehaviour
    {
        private TMP_Text nameText;
        private TMP_Text detailText;
        private TMP_Text walletText;
        private Image iconImage;
        private Button button;
        private Image bodyImage;
        private Image innerPanel;
        private Image badgeDot;
        private TMP_Text badgeNum;
        private RectTransform badgeRoot;

        private string discipleId;

        public event Action<string> Clicked;

        public string DiscipleId => discipleId;
        public Color BodyColor => bodyImage != null ? bodyImage.color : default;
        public Color BadgeColor => badgeDot != null ? badgeDot.color : default;
        public string BadgeText => badgeNum != null ? badgeNum.text : null;

        /// <summary>Tier palette (Task C) — muted washes that stay calm on the
        /// parchment card, one hue per rank (unspecified/outer share the warm
        /// orange, matching the reference's plain-disciple look).</summary>
        public static Color TierColor(DiscipleRank rank) => rank switch
        {
            DiscipleRank.SectMaster     => new Color(0.44f, 0.28f, 0.55f), // เข้ม — จานสีม่วง
            DiscipleRank.Elder          => new Color(0.38f, 0.53f, 0.44f), // จานสีเขียวหม่น
            DiscipleRank.InnerDisciple  => new Color(0.47f, 0.58f, 0.66f), // จานสีฟ้าหม่น
            _                           => new Color(0.86f, 0.62f, 0.47f), // จานสีส้มอ่อน (outer/unspecified)
        };

        private static readonly Color BodyDefault = new Color(0.98f, 0.96f, 0.92f, 1f);
        private static readonly Color BodyElder = new Color(0.96f, 0.92f, 0.84f, 1f); // warm wash for Elder+

        public void Init(TMP_Text name, TMP_Text detail, TMP_Text wallet, Image icon, Button cardButton,
            Image body, Image panel, Image dot, TMP_Text badgeLabel, RectTransform badge)
        {
            nameText = name;
            detailText = detail;
            walletText = wallet;
            iconImage = icon;
            button = cardButton;
            bodyImage = body;
            innerPanel = panel;
            badgeDot = dot;
            badgeNum = badgeLabel;
            badgeRoot = badge;

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

        /// <summary>Task C: tint the frame/inner panel + badge dot for the
        /// disciple's rank. The frame (border) colour IS the card body — the
        /// 8px layout padding exposes the body around the inner panel, so the
        /// body reads as a border while the panel keeps the writing surface.</summary>
        public void ApplyTier(DiscipleRank rank)
        {
            if (bodyImage != null) bodyImage.color = TierColor(rank);
            if (innerPanel != null) innerPanel.color = rank >= DiscipleRank.Elder ? BodyElder : BodyDefault;
            if (badgeDot != null) badgeDot.color = TierColor(rank);
        }

        /// <summary>Task C: roster order number on the badge (1-based, like the
        /// reference art). order &lt;= 0 hides the badge.</summary>
        public void SetBadge(int order)
        {
            if (badgeRoot != null) badgeRoot.gameObject.SetActive(order > 0);
            if (badgeNum != null) badgeNum.text = order > 0 ? order.ToString() : string.Empty;
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
    /// Disciple list panel ("ศิษย์" บน bottom menu): horizontal scroll window with
    /// a 6-column card grid, one vertical card per disciple. Only
    /// titleText / closeButton / cardsRoot / scrollRect are wired on the prefab;
    /// the grid layout and the cards themselves are constructed at runtime
    /// (ResourcePopupView pattern) so they cannot be lost to a stale import.
    /// Cards load baked square icons from Resources/Avatar/Icons (AvatarIconBaker).
    /// </summary>
    public class DiscipleListView : UIViewBase
    {
        // Grid constants — mirrored by DiscipleListPanelGenerator.BuildPanel
        // (which creates the same FitWidthGridLayoutGroup on the prefab;
        // EnsureCardsLayout below is only the defensive fallback).
        // columns = 6 fixed, spacing 16, card aspect 290:225 per the reference art.
        internal const int GridColumns = 6;
        internal const float GridSpacing = 16f;
        internal const float CardAspect = 290f / 225f; // height = width * CardAspect
        internal const int GridRowsInView = 2;         // Task D: 2 rows fill the viewport

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
            // Centered modal window, 16:9 (~1400x790) = 72.9% x 73.1% of the
            // 1920x1080 reference canvas (Canvas.prefab, ScaleWithScreenSize,
            // match = height). Fixed canvas units are REQUIRED here, not
            // proportion anchors: the scroll_paper 9-slice borders are fixed
            // sprite pixels, so the paper's content insets only line up at one
            // window size (same fixed-size pattern as EventPopup/ResourcePopup).
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1400f, 790f);
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
            EnsureCardsLayout();
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

        /// <summary>
        /// Esc closes the panel (Task B). Public on purpose: the play-mode flow
        /// test drives a keyboard close without injecting real input (same
        /// "tests invoke it directly, no reflection" rule as
        /// DiscipleListCard.Click — project rule C12).
        /// </summary>
        public void PressEscape()
        {
            if (!isActiveAndEnabled) return;
            CloseClicked?.Invoke();
        }

        // Update runs ONLY while the panel is active — UIViewBase.Hide() is
        // SetActive(false), so there is no polling cost while the list is
        // closed. Fires the same CloseClicked event as the coin button, so the
        // close still goes through UIBootstrap's CloseCallback → UIService
        // (no data/message-layer involvement).
        private void Update()
        {
            // activeInputHandler = Both, so the legacy Input API works.
            if (Input.GetKeyDown(KeyCode.Escape)) PressEscape();
        }

        // The prefab normally provides this; add it defensively so the list
        // still lays cards out correctly if the component was dropped on import
        // (same defensive pattern as ResourcePopupView.EnsureRowsLayout).
        // Grid, not VerticalLayoutGroup: the reference art is a 6-column card grid.
        private void EnsureCardsLayout()
        {
            if (cardsRoot == null) return;

            // Import-drift defense: a stale prefab may still carry the old
            // VerticalLayoutGroup or a plain (non-fitting) GridLayoutGroup —
            // remove them so we never end up with competing layout components.
            var staleVlg = cardsRoot.GetComponent<VerticalLayoutGroup>();
            if (staleVlg != null) Destroy(staleVlg);

            var grid = cardsRoot.GetComponent<FitWidthGridLayoutGroup>();
            if (grid == null)
            {
                var staleGrid = cardsRoot.GetComponent<GridLayoutGroup>();
                if (staleGrid != null) Destroy(staleGrid);

                grid = cardsRoot.gameObject.AddComponent<FitWidthGridLayoutGroup>();
                grid.padding = new RectOffset(0, 0, 0, 0); // insets live on the generator's content area
                grid.childAlignment = TextAnchor.UpperCenter;
            }
        }

        public DiscipleListCard CreateCard()
        {
            if (cardsRoot == null)
            {
                Debug.LogError("[DiscipleListView] cardsRoot not wired on prefab");
                return null;
            }

            // Build the card entirely in code so it cannot desync from the
            // prefab file (ResourcePopupView.CreateRow lesson). Vertical card:
            // portrait on top, name under it, stat band at the bottom. The grid
            // drives the card's size; no LayoutElement on the card root.
            var cardGo = new GameObject("DiscipleCard", typeof(RectTransform));
            var cardRect = (RectTransform)cardGo.transform;
            cardRect.SetParent(cardsRoot, false);

            // Task C: the BODY is the tier frame — the vertical layout's 8px
            // padding exposes it around the inner panel below, so tinting the
            // body recolours the border without any extra ring image.
            var cardImage = cardGo.AddComponent<Image>();
            cardImage.color = DiscipleListCard.TierColor(DiscipleRank.Unspecified);

            var cardButton = cardGo.AddComponent<Button>();
            cardButton.targetGraphic = cardImage;

            var cardVlg = cardGo.AddComponent<VerticalLayoutGroup>();
            cardVlg.childAlignment = TextAnchor.UpperCenter;
            cardVlg.spacing = 6f;
            cardVlg.padding = new RectOffset(8, 8, 8, 8);
            cardVlg.childControlWidth = true;
            cardVlg.childControlHeight = true;
            cardVlg.childForceExpandWidth = true;
            cardVlg.childForceExpandHeight = false;

            // Thai-capable font inherited from the panel title (CreateText ของ
            // ResourcePopupView หยิบ titleText.font เหมือนกัน) — font default
            // (LiberationSans) ไม่มีอักษรไทย
            var font = titleText != null ? titleText.font : null;

            // Portrait + badge zone. CardTop owns the flexible height so the
            // Task D extra cell height lands on the portrait, never on text.
            var topGo = new GameObject("CardTop", typeof(RectTransform));
            var topRect = (RectTransform)topGo.transform;
            topRect.SetParent(cardRect, false);
            var topImage = topGo.AddComponent<Image>();
            topImage.raycastTarget = false;
            var topVlg = topGo.AddComponent<VerticalLayoutGroup>();
            topVlg.childAlignment = TextAnchor.UpperCenter;
            topVlg.padding = new RectOffset(4, 4, 4, 4); // inner panel margin inside the frame
            topVlg.childControlWidth = true;
            topVlg.childControlHeight = true;
            topVlg.childForceExpandWidth = true;
            topVlg.childForceExpandHeight = false;
            var topElement = topGo.AddComponent<LayoutElement>();
            topElement.flexibleHeight = 1f;
            topElement.minHeight = 130f;

            // Portrait: preferredHeight is the floor; CardTop's flexible space
            // absorbs whatever the grid's cell height leaves over.
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.SetParent(topRect, false);
            iconGo.AddComponent<Image>();
            var iconElement = iconGo.AddComponent<LayoutElement>();
            iconElement.preferredHeight = 150f;
            iconElement.minHeight = 120f;
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            // Task C badge — the numbered dot on the card's TOP-LEFT CORNER
            // (reference art). Anchored to the card (not CardTop) so the corner
            // overlap reads as a badge sitting ON the frame. Created after the
            // panel content → draws on top. raycastTarget=false everywhere: the
            // whole card body is the click target.
            var badgeGo = new GameObject("TierBadge", typeof(RectTransform));
            var badgeRect = (RectTransform)badgeGo.transform;
            badgeRect.SetParent(cardRect, false);
            badgeRect.anchorMin = new Vector2(0f, 1f);
            badgeRect.anchorMax = new Vector2(0f, 1f);
            badgeRect.pivot = new Vector2(0.5f, 0.5f);
            badgeRect.sizeDelta = new Vector2(34f, 34f);
            badgeRect.anchoredPosition = new Vector2(-2f, 2f); // half-off the corner, like the reference

            var dotGo = new GameObject("BadgeDot", typeof(RectTransform));
            var dotRect = (RectTransform)dotGo.transform;
            dotRect.SetParent(badgeRect, false);
            dotRect.anchorMin = Vector2.zero;
            dotRect.anchorMax = Vector2.one;
            dotRect.offsetMin = Vector2.zero;
            dotRect.offsetMax = Vector2.zero;
            var dotImage = dotGo.AddComponent<Image>();
            dotImage.color = DiscipleListCard.TierColor(DiscipleRank.Unspecified);
            dotImage.raycastTarget = false;

            var badgeText = CreateText("BadgeNum", font, 17f, TextAlignmentOptions.Center, badgeRect);
            badgeText.color = Color.white;
            badgeText.fontStyle = FontStyles.Bold;
            badgeText.textWrappingMode = TextWrappingModes.NoWrap;
            badgeText.overflowMode = TextOverflowModes.Overflow; // never clip the numeral

            // Name centred under the portrait. Ellipsis (not the default
            // Overflow clip) so a long name ends in "…" instead of a hard
            // mid-glyph cut; NoWrap keeps it to the single line the 42px row
            // is sized for.
            var nameText = CreateText("Name", font, 30f, TextAlignmentOptions.Center, cardRect);
            nameText.color = new Color(0.17f, 0.13f, 0.09f, 1f);
            nameText.textWrappingMode = TextWrappingModes.NoWrap;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            var nameElement = nameText.gameObject.AddComponent<LayoutElement>();
            nameElement.preferredHeight = 42f;

            // Bottom stat band: rank/task left, wallet value right.
            var statBand = new GameObject("StatBand", typeof(RectTransform));
            var statRect = (RectTransform)statBand.transform;
            statRect.SetParent(cardRect, false);
            var statHlg = statBand.AddComponent<HorizontalLayoutGroup>();
            statHlg.childAlignment = TextAnchor.MiddleLeft;
            statHlg.spacing = 8f;
            statHlg.childControlWidth = true;
            statHlg.childControlHeight = true;
            statHlg.childForceExpandWidth = false;
            statHlg.childForceExpandHeight = false;
            var statElement = statBand.AddComponent<LayoutElement>();
            statElement.preferredHeight = 30f;

            // Task text: ellipsis on overflow ("…" instead of the old hard
            // mid-word clip) + word wrap on, so a long task degrades to a
            // readable truncation rather than chopped Thai/English glyphs.
            var detailText = CreateText("Detail", font, 24f, TextAlignmentOptions.Left, statRect);
            detailText.color = new Color(0.62f, 0.48f, 0.28f, 1f);
            detailText.textWrappingMode = TextWrappingModes.Normal;
            detailText.overflowMode = TextOverflowModes.Ellipsis;
            var detailElement = detailText.gameObject.AddComponent<LayoutElement>();
            detailElement.flexibleWidth = 1f;

            var walletText = CreateText("Wallet", font, 24f, TextAlignmentOptions.Right, statRect);
            walletText.color = new Color(0.17f, 0.13f, 0.09f, 1f);
            walletText.textWrappingMode = TextWrappingModes.NoWrap; // numbers never wrap mid-figure
            walletText.overflowMode = TextOverflowModes.Ellipsis;
            var walletElement = walletText.gameObject.AddComponent<LayoutElement>();
            walletElement.preferredWidth = 90f;

            var card = cardGo.AddComponent<DiscipleListCard>();
            card.Init(nameText, detailText, walletText, iconImg, cardButton,
                cardImage, topImage, dotImage, badgeText, badgeRect);
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
