using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    public sealed class DiscipleListCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        private TMP_Text nameText, detailText, walletText, badgeNum;
        private Image iconImage, bodyImage, innerPanel, badgeDot;
        private Button button;
        private RectTransform badgeRoot;
        private string discipleId;
        private bool hovered, selected;
        public event Action<string> Clicked;
        public string DiscipleId => discipleId;
        public Color BodyColor => bodyImage != null ? bodyImage.color : default;
        public Color BadgeColor => badgeDot != null ? badgeDot.color : default;
        public string BadgeText => badgeNum != null ? badgeNum.text : null;
        public string Name => nameText != null ? nameText.text : null;
        public Sprite Icon => iconImage != null ? iconImage.sprite : null;
        public static Color TierColor(DiscipleRank rank) => UiPalette.RankAccent(rank);
        public void Init(TMP_Text name, TMP_Text detail, TMP_Text wallet, Image icon, Button cardButton,
            Image body, Image panel, Image dot, TMP_Text badgeLabel, RectTransform badge)
        {
            nameText = name; detailText = detail; walletText = wallet; iconImage = icon; button = cardButton;
            bodyImage = body; innerPanel = panel; badgeDot = dot; badgeNum = badgeLabel; badgeRoot = badge;
            if (button != null) button.onClick.AddListener(Click);
        }
        public void Set(string id, string name, string detail, string wallet, Sprite icon)
        {
            discipleId = id;
            if (nameText != null) nameText.text = name;
            if (detailText != null) detailText.text = detail;
            if (walletText != null) walletText.text = wallet;
            if (iconImage != null) { iconImage.sprite = icon; iconImage.enabled = icon != null; }
        }
        public void ApplyTier(DiscipleRank rank)
        {
            if (badgeDot != null) badgeDot.color = TierColor(rank);
            var label = transform.Find("RankLabel")?.GetComponent<TMP_Text>();
            if (label != null) label.text = UiPalette.RankLabel(rank);
            RefreshBorder();
        }
        public void SetBadge(int order)
        {
            if (badgeRoot != null) badgeRoot.gameObject.SetActive(order > 0);
            if (badgeNum != null) badgeNum.text = order > 0 ? order.ToString() : string.Empty;
        }
        internal void BindPortrait(DiscipleState disciple)
        {
            var map = new Xianxia.Sect.Visual.PortraitOverrideMap();
            string path = map.ResolveOverride(disciple.DiscipleId);
            Sprite sprite = string.IsNullOrEmpty(path) ? null : InkWidgets.Load(path);
            if (iconImage != null) { iconImage.sprite = sprite; iconImage.enabled = sprite != null; }
            var host = transform.Find("PortraitFallback") as RectTransform;
            if (host == null) return;
            host.gameObject.SetActive(sprite == null);
            if (sprite != null) return;
            var renderer = host.GetComponent<AvatarRenderer>();
            if (renderer == null)
            {
                host.gameObject.AddComponent<CanvasGroup>();
                renderer = host.gameObject.AddComponent<AvatarRenderer>();
                var proto = InkWidgets.Rect(host, "LayerPrototype", 0, 0, 136, 204);
                InkWidgets.Stretch(proto); var image = InkWidgets.Fill(proto, Color.white);
                image.preserveAspect = true; proto.gameObject.SetActive(false);
                renderer.Configure(host, image);
            }
            renderer.Initialize(new AvatarPartPool());
            renderer.SetFraming(AvatarFraming.FullBody);
            renderer.SetAppearance(disciple.Avatar?.Clone() ?? new AvatarAppearance());
        }
        private void RefreshBorder()
        {
            if (bodyImage != null) bodyImage.color = hovered || selected ? UiPalette.Vermilion : UiPalette.Ink;
            if (innerPanel != null) InkWidgets.Stretch(innerPanel.rectTransform, hovered || selected ? 4 : 2);
        }
        public void OnPointerEnter(PointerEventData data) { hovered = true; RefreshBorder(); }
        public void OnPointerExit(PointerEventData data) { hovered = false; RefreshBorder(); }
        public void OnSelect(BaseEventData data) { selected = true; RefreshBorder(); }
        public void OnDeselect(BaseEventData data) { selected = false; RefreshBorder(); }
        public void Click() => Clicked?.Invoke(discipleId);
        private void OnDestroy() { if (button != null) button.onClick.RemoveListener(Click); Clicked = null; }
    }

    public class DiscipleListView : UIViewBase
    {
        internal const int GridColumns = 5;
        internal const float GridSpacing = 20;
        internal const float CardAspect = 316f / 236f;
        internal const int GridRowsInView = 1;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Button closeButton;
        [SerializeField] private RectTransform cardsRoot;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private TMP_Text footerText;
        [SerializeField] private TMP_InputField searchInput;
        [SerializeField] private Button sortButton;
        public event Action CloseClicked;
        public TMP_Text TitleText => titleText;
        public RectTransform CardsRoot => cardsRoot;
        private bool wired, reverseSort;
        private void Awake() => EnsureCardsLayout();
        public override void ApplyDefaultLayout()
        {
            var rt = transform as RectTransform; if (rt == null) return;
            // Root remains full-canvas so the backdrop does not inherit the window's size.
            InkWidgets.Stretch(rt);
        }
        public override void Show()
        {
            EnsureCardsLayout();
            if (!wired)
            {
                if (closeButton != null) closeButton.onClick.AddListener(OnCloseClicked);
                if (searchInput != null) searchInput.onValueChanged.AddListener(Filter);
                if (sortButton != null) sortButton.onClick.AddListener(Sort);
                wired = true;
            }
            gameObject.SetActive(true);
        }
        internal void SetSummary(int count, int idle)
        {
            if (footerText != null) footerText.text = $"ศิษย์ {count} · ไม่มีงาน {idle} · ความจุ / ค่าเลี้ยงดูต่อเดือน: ยังไม่มีข้อมูล";
            Filter(searchInput != null ? searchInput.text : "");
        }
        private void Filter(string value)
        {
            if (cardsRoot == null) return;
            foreach (Transform child in cardsRoot)
            {
                var card = child.GetComponent<DiscipleListCard>();
                if (card != null) child.gameObject.SetActive(string.IsNullOrEmpty(value) || (card.Name ?? "").IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }
        private void Sort()
        {
            reverseSort = !reverseSort;
            var cards = new System.Collections.Generic.List<DiscipleListCard>(cardsRoot.GetComponentsInChildren<DiscipleListCard>(true));
            cards.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal) * (reverseSort ? -1 : 1));
            foreach (var card in cards) card.transform.SetAsLastSibling();
        }
        private void OnCloseClicked() => CloseClicked?.Invoke();
        public void PressEscape() { if (isActiveAndEnabled) CloseClicked?.Invoke(); }
        private void Update() { if (Input.GetKeyDown(KeyCode.Escape)) PressEscape(); }
        private void EnsureCardsLayout()
        {
            if (cardsRoot == null) return;
            if (cardsRoot.GetComponent<FitWidthGridLayoutGroup>() == null) cardsRoot.gameObject.AddComponent<FitWidthGridLayoutGroup>();
        }
        public DiscipleListCard CreateCard()
        {
            if (cardsRoot == null) { Debug.LogError("[DiscipleListView] cardsRoot not wired on prefab"); return null; }
            var rt = InkWidgets.InkPanel(cardsRoot, "DiscipleCard", 0, 0, 236, 316);
            var body = rt.GetComponent<Image>(); body.raycastTarget = true;
            var panel = rt.Find("Paper").GetComponent<Image>(); InkWidgets.Stretch(panel.rectTransform, 2);
            var button = rt.gameObject.AddComponent<Button>(); button.targetGraphic = body; button.transition = Selectable.Transition.None;
            var font = titleText != null ? titleText.font : null;
            var seal = InkWidgets.Seal(rt, "TierBadge", 10, 10, UiPalette.Vermilion);
            InkWidgets.Text(rt, "RankLabel", "", font, 26, 38, 4, 186, 34);
            var portrait = InkWidgets.Rect(rt, "Portrait", 50, 38, 136, 204);
            InkWidgets.Fill(portrait, UiPalette.Portrait);
            var iconRt = InkWidgets.Rect(rt, "Icon", 50, 38, 136, 204);
            var icon = InkWidgets.Fill(iconRt, Color.white); icon.preserveAspect = true;
            var fallback = InkWidgets.Rect(rt, "PortraitFallback", 50, 38, 136, 204);
            fallback.gameObject.SetActive(false);
            var plate = InkWidgets.Rect(rt, "NamePlate", 6, 246, 224, 34); InkWidgets.Fill(plate, UiPalette.Ink);
            var name = InkWidgets.Text(plate, "Name", "", font, 28, 4, 0, 216, 34, UiPalette.LightText, TextAlignmentOptions.Center);
            var detail = InkWidgets.Text(rt, "Detail", "", font, 26, 10, 282, 142, 30, UiPalette.Secondary);
            var wallet = InkWidgets.Text(rt, "Wallet", "", font, 26, 156, 282, 70, 30, UiPalette.Text, TextAlignmentOptions.MidlineRight);
            var card = rt.gameObject.AddComponent<DiscipleListCard>();
            card.Init(name, detail, wallet, icon, button, body, panel, seal, null, seal.rectTransform);
            return card;
        }
        public void ClearCards()
        {
            if (cardsRoot == null) return;
            for (int i = cardsRoot.childCount - 1; i >= 0; i--)
            {
                var child = cardsRoot.GetChild(i); child.gameObject.SetActive(false); child.SetParent(null, false); Destroy(child.gameObject);
            }
        }
        private void OnDestroy()
        {
            if (closeButton != null && wired) closeButton.onClick.RemoveListener(OnCloseClicked);
            if (searchInput != null) searchInput.onValueChanged.RemoveListener(Filter);
            if (sortButton != null) sortButton.onClick.RemoveListener(Sort);
            CloseClicked = null;
        }
    }
}
