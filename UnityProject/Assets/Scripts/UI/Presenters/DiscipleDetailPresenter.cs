using System;
using System.Collections.Generic;
using MessagePipe;
using TMPro;
using Xianxia.Sect;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// Phase 1 — presenter ของ DiscipleDetail แบบ 3 โซน:
    /// - OnOpen(discipleId): BuildRail() จาก roster จริงทุกครั้งที่เปิด + SwitchDisciple()
    /// - RailItemClicked → SwitchDisciple(id) โดยตรง (panel ยังเปิด ไม่ Close/Open ใหม่)
    /// - TabClicked → SwitchTab(): Hide/Show ของ tab presenter + SetActive ของ host
    ///   เท่านั้น (ห้าม Destroy/Instantiate ตอนสลับ)
    ///
    /// เส้นทางเข้าเดิมไม่เปลี่ยน: DiscipleDetailUISystem เรียก UIService.Open("DiscipleDetail", id)
    /// เมื่อได้รับ DiscipleSelectedMessage (คลิก chibi หรือคลิกการ์ดใน DiscipleList)
    /// </summary>
    public class DiscipleDetailPresenter : UIPresenter<DiscipleDetailView>
    {
        private readonly ISectStateProvider _stateProvider;
        private readonly AvatarPartPool _partPool;

        private readonly Dictionary<DiscipleTab, IDiscipleTabPresenter> _tabs =
            new Dictionary<DiscipleTab, IDiscipleTabPresenter>();

        private DiscipleTab _currentTab = DiscipleTab.Info;
        private string _currentDiscipleId;
        private bool _tabsBuilt;

        public DiscipleDetailPresenter(ISectStateProvider stateProvider, AvatarPartPool partPool)
        {
            _stateProvider = stateProvider;
            _partPool = partPool;
        }

        protected override void OnViewBound()
        {
            View.CloseClicked += OnCloseClicked;
            View.RailItemClicked += SwitchDisciple;
            View.TabClicked += SwitchTab;
        }

        public override void OnOpen(object args)
        {
            var discipleId = args as string;
            if (string.IsNullOrEmpty(discipleId)) return;

            // TODO(backlog): live rail update — subscribe DiscipleRecruitedMessage เมื่อ
            // ต้องการให้ rail โผล่ศิษย์ใหม่ทันทีระหว่าง panel เปิดอยู่ (Phase นี้ refresh จาก
            // state ตอน OnOpen เท่านั้น)
            BuildRail();
            BuildTabs();

            SwitchDisciple(discipleId);
        }

        private void BuildRail()
        {
            var disciples = _stateProvider.BuildSectEconomyState()?.Disciples;
            if (disciples == null) return;

            var items = new List<(string id, AvatarAppearance avatar, DiscipleSex sex)>();
            foreach (var d in disciples)
            {
                if (d == null) continue;
                items.Add((d.DiscipleId, d.Avatar, d.Sex));
            }
            View.SetRailItems(items);
        }

        /// <summary>
        /// สร้าง tab presenter ครั้งเดียวต่อ presenter lifecycle (Transient — ตายพร้อม panel)
        /// </summary>
        private void BuildTabs()
        {
            if (_tabsBuilt) return;

            var statusContent = View.GetTabContent(DiscipleTab.Status);

            _tabs[DiscipleTab.Info] = new InfoTabPresenter(View);
            _tabs[DiscipleTab.Status] = new StatusTabPresenter(
                statusContent != null
                    ? statusContent.GetComponentInChildren<RadarChartGraphic>(true)
                    : null,
                statusContent != null
                    ? statusContent.GetComponentsInChildren<TMP_Text>(true)
                    : new TMP_Text[0]);

            // Equipment/Skill/Destiny/SpiritRoot — placeholder ร่วมตัวเดียว
            // (แท็บทั้งสี่แสดงข้อความเดียวกัน; แยก presenter เมื่อทำงานจริง)
            var placeholderContent = View.GetTabContent(DiscipleTab.Equipment);
            var placeholder = placeholderContent != null
                ? placeholderContent.GetComponentInChildren<TMP_Text>(true)
                : null;
            var sharedPlaceholder = new PlaceholderTabPresenter(placeholder);
            _tabs[DiscipleTab.Equipment] = sharedPlaceholder;
            _tabs[DiscipleTab.Skill] = sharedPlaceholder;
            _tabs[DiscipleTab.Destiny] = sharedPlaceholder;
            _tabs[DiscipleTab.SpiritRoot] = sharedPlaceholder;

            _tabsBuilt = true;
        }

        /// <summary>สลับศิษย์: header + portrait + rail selection + tab ปัจจุบัน — ไม่ปิด panel</summary>
        private void SwitchDisciple(string discipleId)
        {
            if (string.IsNullOrEmpty(discipleId)) return;

            var d = _stateProvider.BuildSectEconomyState()?.Disciples?
                .Find(x => x != null && x.DiscipleId == discipleId);
            if (d == null) return;

            _currentDiscipleId = discipleId;

            // header + portrait (FullBody ตาม reference — ไม่ใช่ Bust แบบเดิม)
            View.SetHeader(d.DisplayName, "Rank: " + d.Rank); // TODO(backlog): class tag เมื่อมี job/class field
            View.SetInfo(d.DisplayName, d.Rank.ToString(), d.CurrentTask, "");
            var renderer = View.PortraitRenderer;
            if (renderer != null)
            {
                renderer.Initialize(_partPool);
                renderer.SetFraming(AvatarFraming.FullBody);
                renderer.SetAppearance(d.Avatar != null ? d.Avatar.Clone() : new AvatarAppearance());
            }
            View.SetRailSelection(discipleId);

            // badge: ไม่มีระบบ pending จริง — ส่ง false เสมอในเฟสนี้ (ComputeBadges เป็น backlog)
            foreach (DiscipleTab tab in Enum.GetValues(typeof(DiscipleTab)))
                View.SetTabBadge(tab, false);

            RefreshCurrentTab();
        }

        /// <summary>สลับแท็บ: Hide ตัวเก่า → SetActive host → Show ตัวใหม่ (ไม่ Destroy อะไร)</summary>
        private void SwitchTab(DiscipleTab tab)
        {
            if (tab == _currentTab) return;

            IDiscipleTabPresenter old;
            if (_tabs.TryGetValue(_currentTab, out old)) old.Hide();

            _currentTab = tab;
            RefreshCurrentTab();
        }

        private void RefreshCurrentTab()
        {
            View.ShowTabContent(_currentTab);
            View.HighlightTab(_currentTab);

            IDiscipleTabPresenter presenter;
            if (_tabs.TryGetValue(_currentTab, out presenter))
            {
                var d = _stateProvider.BuildSectEconomyState()?.Disciples?
                    .Find(x => x != null && x.DiscipleId == _currentDiscipleId);
                presenter.Show(d);
            }
        }

        private void OnCloseClicked()
        {
            View.Hide();
        }

        public override void Dispose()
        {
            View.CloseClicked -= OnCloseClicked;
            View.RailItemClicked -= SwitchDisciple;
            View.TabClicked -= SwitchTab;
        }
    }
}
