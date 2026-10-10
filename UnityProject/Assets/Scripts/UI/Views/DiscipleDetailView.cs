using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace Xianxia.Sect.UI
{
    public enum DiscipleTab { Info, Status, Equipment, Skill, Destiny, SpiritRoot }
    public class DiscipleDetailView : UIViewBase
    {
        [SerializeField] private TMP_Text nameText, rankText, taskText, walletText;
        [SerializeField] private Button closeButton;
        [SerializeField] private AvatarRenderer portraitRenderer;
        [SerializeField] private RectTransform railContent, tabRail, contentHost;
        [SerializeField] private TMP_Text headerNameText, headerRankText;
        // P10C — attributes shown on the ข้อมูล tab (stamina bar + number, three skill values).
        [SerializeField] private Image staminaBarFill;
        [SerializeField] private TMP_Text staminaText, skillGatheringText, skillAlchemyText, skillForgingText;
        public event Action CloseClicked;
        public event Action<string> RailItemClicked;
        public event Action<DiscipleTab> TabClicked;

        /// <summary>
        /// P10C — raised at most ~4×/s on UNSCALED time, and only while the panel is
        /// visible. The presenter re-reads the live attributes and writes text; no
        /// message is published per attribute tick.
        /// </summary>
        public event Action RefreshTick;

        /// <summary>P10C — unscaled refresh period (≈4 refreshes per second).</summary>
        public const float AttributeRefreshIntervalSeconds = 0.25f;

        private float _nextAttributeRefreshAt;
        private bool _visible;
        public AvatarRenderer PortraitRenderer => portraitRenderer;
        public RectTransform ContentHost => contentHost;
        private readonly Stack<RailItem> pool = new Stack<RailItem>();
        private readonly List<RailItem> active = new List<RailItem>();
        private bool wired;
        private void Awake() { if (closeButton != null) closeButton.onClick.AddListener(OnClose); }
        public override void Show() { Wire(); _visible = true; _nextAttributeRefreshAt = 0f; base.Show(); }
        public override void Hide() { _visible = false; base.Hide(); }
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Hide();
            TickAttributeRefresh(Time.unscaledTime); // unscaled: a paused game still shows live attributes
        }

        /// <summary>
        /// P10C — the visibility + throttle gate for the attribute refresh, split out of
        /// <see cref="Update"/> so EditMode tests can drive it with explicit unscaled
        /// timestamps. Returns true when a refresh was actually raised. While the panel is
        /// hidden nothing ticks at all, so closing it stops the refresh (and a destroyed
        /// panel simply stops existing).
        /// </summary>
        public bool TickAttributeRefresh(float unscaledTimeSeconds)
        {
            if (!_visible) return false;
            if (unscaledTimeSeconds < _nextAttributeRefreshAt) return false;

            _nextAttributeRefreshAt = unscaledTimeSeconds + AttributeRefreshIntervalSeconds;
            RefreshTick?.Invoke();
            return true;
        }

        /// <summary>
        /// P10C — stamina: bar fill + number, plus the low-stamina indicator colour. The
        /// indicator is VISUAL ONLY (the palette marks low/high via the shared thresholds);
        /// it never reassigns a Manual/Player/Viewer disciple, and attributes never change
        /// yield or craft speed.
        /// </summary>
        public void SetStamina(float stamina)
        {
            float max = DiscipleAttributesConfig.StaminaMax;
            float clamped = Mathf.Clamp(stamina, DiscipleAttributesConfig.StaminaMin, max);
            if (staminaText != null) staminaText.text = Mathf.RoundToInt(clamped) + " / " + Mathf.RoundToInt(max);

            if (staminaBarFill == null) return;
            var fill = staminaBarFill.rectTransform;
            var track = fill.parent as RectTransform;
            float fullWidth = track != null ? track.rect.width : 0f;
            if (fullWidth <= 0f) fullWidth = fill.sizeDelta.x;
            float ratio = max > 0f ? clamped / max : 0f;
            fill.sizeDelta = new Vector2(fullWidth * ratio, fill.sizeDelta.y);

            staminaBarFill.color = clamped <= DiscipleAttributesConfig.RecoveryThresholdLow ? UiPalette.Danger
                                 : clamped < DiscipleAttributesConfig.RecoveryThresholdHigh ? UiPalette.Vermilion
                                 : UiPalette.Jade;
        }

        /// <summary>P10C — one text per skill category (e.g. "Lv 3 · XP 340"); the presenter
        /// formats them from the shared attribute helpers, this only displays.</summary>
        public void SetSkills(string gathering, string alchemy, string forging)
        {
            if (skillGatheringText != null) skillGatheringText.text = gathering ?? string.Empty;
            if (skillAlchemyText != null) skillAlchemyText.text = alchemy ?? string.Empty;
            if (skillForgingText != null) skillForgingText.text = forging ?? string.Empty;
        }
        private void OnClose() => CloseClicked?.Invoke();
        private void Wire()
        {
            if (wired || tabRail == null) return; wired = true;
            for(int i=0;i<tabRail.childCount && i<6;i++)
            {
                var tab=(DiscipleTab)i;var button=tabRail.GetChild(i).GetComponent<Button>();
                if(button!=null) button.onClick.AddListener(()=>TabClicked?.Invoke(tab));
            }
        }
        public void SetHeader(string name,string rank)
        {
            if(headerNameText!=null)headerNameText.text=name;
            if(headerRankText!=null)headerRankText.text=rank;
        }
        public RectTransform GetTabContent(DiscipleTab tab)
        {
            Wire(); return contentHost!=null && (int)tab<contentHost.childCount ? contentHost.GetChild((int)tab) as RectTransform : null;
        }
        public void SetTabBadge(DiscipleTab tab,bool hasBadge)
        {
            if(tabRail==null || (int)tab>=tabRail.childCount)return;
            var badge=tabRail.GetChild((int)tab).Find("Badge");if(badge!=null)badge.gameObject.SetActive(hasBadge);
        }
        public void HighlightTab(DiscipleTab tab)
        {
            Wire();if(tabRail==null)return;
            for(int i=0;i<tabRail.childCount;i++) { var b=tabRail.GetChild(i).GetComponent<Button>();if(b!=null)InkWidgets.Select(b,i==(int)tab); }
        }
        public void SetInfo(string name,string rank,string task,string wallet)
        {
            if(nameText!=null)nameText.text=name;if(rankText!=null)rankText.text=rank;
            if(taskText!=null)taskText.text=task;if(walletText!=null)walletText.text=wallet;
            var footer=transform.Find("Window/WalletFooter")?.GetComponent<TMP_Text>();if(footer!=null)footer.text=wallet;
        }
        public void ShowTabContent(DiscipleTab tab)
        {
            if(contentHost==null)return;
            for(int i=0;i<contentHost.childCount;i++)contentHost.GetChild(i).gameObject.SetActive(i==(int)tab);
        }
        public void SetRailItems(IReadOnlyList<(string id,AvatarAppearance avatar,DiscipleSex sex)> items)
        {
            if(railContent==null) { Debug.LogError("[DiscipleDetailView] railContent not wired");return; }
            while(active.Count>items.Count) { var item=active[active.Count-1];active.RemoveAt(active.Count-1);item.gameObject.SetActive(false);pool.Push(item); }
            for(int i=0;i<items.Count;i++)
            {
                if(i>=active.Count) { var item=pool.Count>0?pool.Pop():RailItem.Create(railContent);item.gameObject.SetActive(true);item.transform.SetAsLastSibling();active.Add(item); }
                active[i].Bind(items[i].id,items[i].avatar,items[i].sex,id=>RailItemClicked?.Invoke(id));
            }
        }
        public void SetRailSelection(string id) { foreach(var item in active)item.SetSelected(item.DiscipleId==id); }
        public sealed class RailItem : MonoBehaviour
        {
            private string id;private Image icon,ring;private Button button;private Action<string> click;
            public string DiscipleId=>id;
            public void Bind(string id,AvatarAppearance avatar,DiscipleSex sex,Action<string> onClick)
            {
                this.id=id;click=onClick;
                var sprite=InkWidgets.Load("Avatar/Icons/icon_"+id);icon.sprite=sprite;icon.enabled=sprite!=null;SetSelected(false);
            }
            public void SetSelected(bool selected) { if(ring!=null)ring.color=selected?UiPalette.Vermilion:UiPalette.Ink; }
            public static RailItem Create(RectTransform parent)
            {
                var rt=InkWidgets.Rect(parent,"RailItem",0,0,60,60);
                var ring=InkWidgets.Fill(rt,UiPalette.Ink,true);ring.sprite=InkWidgets.Circle;
                var maskRt=InkWidgets.Rect(rt,"CircleMask",2,2,56,56);var maskImage=InkWidgets.Fill(maskRt,UiPalette.PaperDark);maskImage.sprite=InkWidgets.Circle;
                maskRt.gameObject.AddComponent<Mask>().showMaskGraphic=true;
                var iconRt=InkWidgets.Rect(maskRt,"Icon",0,0,56,56);var image=InkWidgets.Fill(iconRt,Color.white);image.preserveAspect=true;
                var item=rt.gameObject.AddComponent<RailItem>();item.icon=image;item.ring=ring;
                item.button=rt.gameObject.AddComponent<Button>();item.button.targetGraphic=ring;item.button.transition=Selectable.Transition.None;
                item.button.onClick.AddListener(item.Click);return item;
            }
            private void Click()=>click?.Invoke(id);
            private void OnDestroy() { if(button!=null)button.onClick.RemoveListener(Click);click=null; }
        }
        private void OnDestroy() { if(closeButton!=null)closeButton.onClick.RemoveListener(OnClose);CloseClicked=null;RailItemClicked=null;TabClicked=null; }
    }
}
