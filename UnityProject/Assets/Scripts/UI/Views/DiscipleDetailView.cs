using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    /// <summary>แท็บของ DiscipleDetail (เรียงตาม reference ภาพ) — ห้ามสลับลำดับ</summary>
    public enum DiscipleTab
    {
        Info = 0,
        Status = 1,
        Equipment = 2,
        Skill = 3,
        Destiny = 4,
        SpiritRoot = 5,
    }

    /// <summary>
    /// Phase 1 — โครง 3 โซน: LeftRail (สลับศิษย์) / Header / RightTabRail + ContentHost
    /// โครงเก่า (text 4 บรรทัด + closeButton + portraitRenderer) คงไว้หมด — presenter
    /// เดิมอ้างอยู่ และ Info tab ยังใช้ text ชุดนี้ bind ชื่อ/rank/task/wallet
    /// Rail items ถูก pool ด้วย Stack (ไม่ Instantiate/Destroy ต่อการเปิด/สลับ)
    /// </summary>
    public class DiscipleDetailView : UIViewBase
    {
        [Header("Legacy info (Info tab)")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text rankText;
        [SerializeField] private TMP_Text taskText;
        [SerializeField] private TMP_Text walletText;

        [Header("Shell")]
        [SerializeField] private Button closeButton;
        [SerializeField] private AvatarRenderer portraitRenderer;
        [SerializeField] private RectTransform railContent;      // LeftRail/Viewport/Content
        [SerializeField] private RectTransform tabRail;          // RightTabRail (ปุ่ม 6 อันเรียงตาม enum)
        [SerializeField] private RectTransform contentHost;      // เนื้อหากลาง (child 6 อันตาม enum)
        [SerializeField] private TMP_Text headerNameText;        // Header: ชื่อ
        [SerializeField] private TMP_Text headerRankText;        // Header: tag แสดง Rank (ไม่มี class จริง — label ชัดว่า rank)

        public event Action CloseClicked;
        public event Action<string> RailItemClicked;   // discipleId
        public event Action<DiscipleTab> TabClicked;

        public AvatarRenderer PortraitRenderer { get { return portraitRenderer; } }
        public RectTransform ContentHost => contentHost;

        // ── rail pooling ──
        private readonly Stack<RailItem> _railPool = new Stack<RailItem>();
        private readonly List<RailItem> _activeRail = new List<RailItem>();

        // ── tab buttons (generator สร้างครบ 6 อันตาม enum ลำดับ) ──
        private readonly Dictionary<DiscipleTab, Button> _tabButtons = new Dictionary<DiscipleTab, Button>();
        private readonly Dictionary<DiscipleTab, Image> _tabBadges = new Dictionary<DiscipleTab, Image>();
        private readonly Dictionary<DiscipleTab, RectTransform> _tabContents =
            new Dictionary<DiscipleTab, RectTransform>();

        private void Awake()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(OnCloseClicked);
        }

        private void OnDestroy()
        {
            if (closeButton != null)
                closeButton.onClick.RemoveListener(OnCloseClicked);
            CloseClicked = null;
            RailItemClicked = null;
            TabClicked = null;
        }

        private void OnCloseClicked() => CloseClicked?.Invoke();

        public void SetHeader(string name, string rank)
        {
            if (headerNameText != null) headerNameText.text = name;
            if (headerRankText != null) headerRankText.text = rank; // TODO(backlog): class tag เมื่อมี job/class field
        }

        // ────────────────────────────────────────────────────────────────
        // Tab wiring — self-wired จากลำดับ child ของ tabRail/contentHost
        // (generator สร้างเรียงตาม DiscipleTab enum แล้ว ไม่ต้องผ่าน SerializedObject รายปุ่ม)
        // ────────────────────────────────────────────────────────────────

        private bool _tabsWired;

        private void EnsureTabsWired()
        {
            if (_tabsWired) return;
            _tabsWired = true;

            if (tabRail != null)
            {
                int i = 0;
                foreach (Transform child in tabRail)
                {
                    if (i >= (int)DiscipleTab.SpiritRoot + 1) break;
                    var button = child.GetComponent<Button>();
                    if (button == null) { i++; continue; }

                    var tab = (DiscipleTab)i;
                    _tabButtons[tab] = button;

                    var badge = child.Find("Badge") as RectTransform;
                    _tabBadges[tab] = badge != null ? badge.GetComponent<Image>() : null;
                    SetTabBadge(tab, false);

                    var captured = tab;
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() =>
                    {
                        var cb = TabClicked;
                        if (cb != null) cb(captured);
                    });
                    i++;
                }
            }

            if (contentHost != null)
            {
                int i = 0;
                foreach (Transform child in contentHost)
                {
                    if (i >= (int)DiscipleTab.SpiritRoot + 1) break;
                    _tabContents[(DiscipleTab)i] = (RectTransform)child;
                    i++;
                }
            }
        }

        public RectTransform GetTabContent(DiscipleTab tab)
        {
            EnsureTabsWired();
            RectTransform rt;
            return _tabContents.TryGetValue(tab, out rt) ? rt : null;
        }

        public void SetTabBadge(DiscipleTab tab, bool hasBadge)
        {
            EnsureTabsWired();
            Image badge;
            if (_tabBadges.TryGetValue(tab, out badge) && badge != null)
                badge.gameObject.SetActive(hasBadge);
            // Phase 1: presenter ส่ง false เสมอ — logic badge จริงเป็น backlog (ComputeBadges)
        }

        public void HighlightTab(DiscipleTab tab)
        {
            EnsureTabsWired();
            foreach (var kv in _tabButtons)
            {
                if (kv.Value == null) continue;
                var colors = kv.Value.colors;
                // แท็บที่เลือก = ทอง (แพทเทิร์นเดียวกับ AvatarCustomizationView.RenderCategoryTabs)
                colors.normalColor = kv.Key == tab
                    ? new Color(1f, 0.85f, 0.45f)
                    : Color.white;
                kv.Value.colors = colors;
            }
        }

        public void SetInfo(string name, string rank, string task, string wallet)
        {
            if (nameText != null) nameText.text = name;
            if (rankText != null) rankText.text = rank;
            if (taskText != null) taskText.text = task;
            if (walletText != null) walletText.text = wallet;
        }

        /// <summary>เปิดเนื้อหาแท็บเดียว ปิดที่เหลือ (SetActive เท่านั้น — ห้าม Destroy)</summary>
        public void ShowTabContent(DiscipleTab tab)
        {
            EnsureTabsWired();
            foreach (var kv in _tabContents)
            {
                if (kv.Value == null) continue;
                kv.Value.gameObject.SetActive(kv.Key == tab);
            }
        }

        // ────────────────────────────────────────────────────────────────
        // Left rail — pool + selection highlight
        // ──────────────────────────────────────────────────────────────���─

        /// <summary>จ่าย roster ให้ rail — pool ซ้ำ ไม่ Instantiate/Destroy ถ้าจำนวนไม่โตกว่าเดิม</summary>
        public void SetRailItems(IReadOnlyList<(string id, AvatarAppearance avatar, DiscipleSex sex)> items)
        {
            EnsureTabsWired();
            if (railContent == null)
            {
                Debug.LogError("[DiscipleDetailView] railContent not wired");
                return;
            }

            // คืนเกินก่อน แล้ว rent ตามจำนวนใหม่ (pool ไม่ Destroy เลย)
            while (_activeRail.Count > items.Count)
            {
                var released = _activeRail[_activeRail.Count - 1];
                _activeRail.RemoveAt(_activeRail.Count - 1);
                released.gameObject.SetActive(false);
                _railPool.Push(released);
            }

            for (int i = 0; i < items.Count; i++)
            {
                RailItem item;
                if (i < _activeRail.Count)
                {
                    item = _activeRail[i]; // reuse ตำแหน่งเดิม
                }
                else
                {
                    item = RentRailItem();
                    _activeRail.Add(item);
                }
                item.Bind(items[i].id, items[i].avatar, items[i].sex, HandleRailClicked);
            }
        }

        public void SetRailSelection(string id)
        {
            for (int i = 0; i < _activeRail.Count; i++)
                _activeRail[i].SetSelected(_activeRail[i].DiscipleId == id);
        }

        private RailItem RentRailItem()
        {
            RailItem item = _railPool.Count > 0
                ? _railPool.Pop()
                : RailItem.Create(railContent);
            item.gameObject.SetActive(true);
            item.transform.SetAsLastSibling();
            return item;
        }

        private void HandleRailClicked(string id)
        {
            var cb = RailItemClicked;
            if (cb != null) cb(id);
        }

        /// <summary>
        /// รายการศิษย์ 1 แถวใน left rail — สร้างในโค้ดครั้งแรกแล้ว pool ตลอด
        /// (ไม่มี prefab: layerPrototype เป็น plain Image ให้ AvatarRenderer)
        /// </summary>
        public sealed class RailItem : MonoBehaviour
        {
            private const float GoldR = 1f, GoldG = 0.85f, GoldB = 0.45f;

            private string _id;
            private AvatarRenderer _renderer;
            private Image _ring;
            private Button _button;
            private Action<string> _onClick;

            public string DiscipleId => _id;

            public void Bind(string id, AvatarAppearance avatar, DiscipleSex sex, Action<string> onClick)
            {
                _id = id;
                _onClick = onClick;
                if (_renderer == null) return; // Create() ยังไม่รัน (ไม่ควรเกิด)

                _renderer.Initialize(PartPool());
                _renderer.SetFraming(AvatarFraming.HeadIcon);
                _renderer.SetAppearance(avatar != null ? avatar.Clone() : new AvatarAppearance());
                SetSelected(false);
                _ = sex; // Phase 1: ยังไม่ filter rail ตามเพศ (ทุกคนโชว์)
            }

            public void SetSelected(bool selected)
            {
                if (_ring != null)
                    _ring.color = selected
                        ? new Color(GoldR, GoldG, GoldB, 1f)
                        : new Color(1f, 1f, 1f, 0.35f);
            }

            private void HandleClick()
            {
                var cb = _onClick;
                if (cb != null) cb(_id);
            }

            /// <summary>โครง item: ring Image + child AvatarRoot (CanvasGroup+AvatarRenderer) + invisible button</summary>
            public static RailItem Create(RectTransform parent)
            {
                var go = new GameObject("RailItem", typeof(RectTransform), typeof(CanvasGroup));
                var rt = (RectTransform)go.transform;
                rt.SetParent(parent, false);
                rt.sizeDelta = new Vector2(72f, 72f);

                var ringGo = new GameObject("Ring", typeof(RectTransform), typeof(Image));
                var ringRt = (RectTransform)ringGo.transform;
                ringRt.SetParent(rt, false);
                ringRt.anchorMin = Vector2.zero; ringRt.anchorMax = Vector2.one;
                ringRt.offsetMin = Vector2.zero; ringRt.offsetMax = Vector2.zero;
                var ring = ringGo.GetComponent<Image>();
                ring.color = new Color(1f, 1f, 1f, 0.35f);
                ring.raycastTarget = false;

                var avatarGo = new GameObject("AvatarRoot", typeof(RectTransform), typeof(CanvasGroup));
                var avatarRt = (RectTransform)avatarGo.transform;
                avatarRt.SetParent(rt, false);
                avatarRt.anchorMin = new Vector2(0.5f, 0.5f); avatarRt.anchorMax = new Vector2(0.5f, 0.5f);
                avatarRt.pivot = new Vector2(0.5f, 0.5f);
                avatarRt.sizeDelta = new Vector2(56f, 56f);
                avatarGo.GetComponent<CanvasGroup>().alpha = 1f; // ⚠️ =1 ไม่งั้น AvatarRenderer ไม่ Update

                var renderer = avatarGo.AddComponent<AvatarRenderer>();
                var proto = new GameObject("LayerPrototype", typeof(RectTransform), typeof(Image));
                proto.GetComponent<Image>().raycastTarget = false;
                proto.GetComponent<Image>().preserveAspect = true;
                proto.SetActive(false);
                proto.transform.SetParent(avatarGo.transform, false);
                renderer.Configure(avatarRt, proto.GetComponent<Image>());

                var item = go.AddComponent<RailItem>();
                item._renderer = renderer;
                item._ring = ring;

                // invisible full-stretch Button บนสุดรับคลิก
                var btnGo = new GameObject("HitArea", typeof(RectTransform), typeof(Image), typeof(Button));
                var btnRt = (RectTransform)btnGo.transform;
                btnRt.SetParent(rt, false);
                btnRt.anchorMin = Vector2.zero; btnRt.anchorMax = Vector2.one;
                btnRt.offsetMin = Vector2.zero; btnRt.offsetMax = Vector2.zero;
                var img = btnGo.GetComponent<Image>();
                img.color = new Color(0, 0, 0, 0); // โปร่งใสแต่ raycastTarget = true
                item._button = btnGo.GetComponent<Button>();
                item._button.onClick.AddListener(item.HandleClick);

                return item;
            }

            private static AvatarPartPool s_Pool; // rail items ใช้ pool เดียวกัน (readonly data)

            private static AvatarPartPool PartPool()
            {
                // AvatarPartPool โหลด JSON ครั้งเดียวต่อ instance — rail items ทุกตัว
                // ใช้ instance เดียวกันกันโหลดซ้ำ (presenter จ่ายผ่าน Initialize ก็ได้
                // แต่ Phase 1 เลือก static แบบนี้เพื่อไม่เปลี่ยน signature ของ Bind)
                if (s_Pool == null) s_Pool = new AvatarPartPool();
                return s_Pool;
            }

            private void OnDestroy()
            {
                if (_button != null) _button.onClick.RemoveListener(HandleClick);
                _onClick = null;
            }
        }
    }
}
