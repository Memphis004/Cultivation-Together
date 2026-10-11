using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Xianxia.Sect;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// Tab presenter ต่อแท็บ — Show/Hide เท่านั้น (ไม่มี lifecycle อื่น):
    /// presenter หลักเรียก Show(state ของศิษย์ปัจจุบัน) ตอนสลับแท็บ/สลับศิษย์
    /// และ Hide() ตอนแท็บหายไป — ห้าม Destroy/Instantiate ตอนสลับแท็บ
    /// </summary>
    public interface IDiscipleTabPresenter
    {
        void Show(DiscipleState d);
        void Hide();
    }

    /// <summary>
    /// Tab ข้อมูล: bind ชื่อ/rank/task/wallet จริง (ย้ายมาจาก view เดิม)
    /// + P10C — stamina (bar + เลข) และ level ของสามหมวดทักษะ ("Lv n · XP x")
    /// + P14B — สายบำเพ็ญ: ชื่อขั้น + progress bar ("progress / requirement")
    ///   และปุ่ม "ทะลวงขั้น" ที่สถานะ/ข้อความมาจาก ISectStateProvider.CanBreakthrough
    /// </summary>
    public class InfoTabPresenter : IDiscipleTabPresenter
    {
        private readonly DiscipleDetailView _view;
        private readonly ISectStateProvider _stateProvider;

        public InfoTabPresenter(DiscipleDetailView view, ISectStateProvider stateProvider)
        {
            _view = view;
            _stateProvider = stateProvider;
        }

        public void Show(DiscipleState d)
        {
            if (d == null) return;
            _view.SetInfo(
                d.DisplayName,
                UiPalette.RankLabel(d.Rank),
                string.IsNullOrEmpty(d.CurrentTask) ? "—" : d.CurrentTask,
                "หินวิญญาณ " + (d.Wallet?.SpiritStones ?? 0) +
                " · อุทิศ " + (d.Wallet?.Contribution ?? 0));
            ShowAttributes(d);
            ShowCultivation(d);
        }

        /// <summary>
        /// P14B — the cultivation row: stage display name, the progress bar with
        /// "progress / requirement", and the breakthrough button's state + inline reason.
        /// Both come from ISectStateProvider.CanBreakthrough — the SAME validation
        /// TryBreakthrough revalidates — so the panel can never disagree with a commit and
        /// never keeps a second copy of the rule. Called on open/switch and on the panel's
        /// ≤4/s tick, so live meditation progress is visible without any per-tick message.
        /// </summary>
        public void ShowCultivation(DiscipleState d)
        {
            if (d == null) return;
            var attributes = d.Attributes;
            var stageId = attributes != null ? attributes.CultivationStageId : null;
            float progress = attributes != null ? attributes.CultivationProgress : 0f;

            bool known = CultivationConfig.IsKnownStage(stageId);
            bool isFinal = known && CultivationConfig.IsFinal(stageId);

            string reason = string.Empty;
            bool ready = _stateProvider != null &&
                         _stateProvider.CanBreakthrough(SectStateProvider.SectMasterRequesterId,
                                                        d.DiscipleId, out reason);

            _view.SetCultivation(
                CultivationConfig.DisplayName(stageId),
                progress,
                CultivationConfig.Requirement(stageId),
                isFinal,
                ready);
            _view.SetBreakthroughState(ready, reason); // reason is empty when ready
        }

        /// <summary>
        /// P14B — the ปุ่มทะลวงขั้น action: REVALIDATE and commit through the authoritative
        /// provider as the sect master (SECT_MASTER — no viewer bypass, no AI-GM path).
        /// A rejection changes nothing and shows its failReason inline; the button itself is
        /// already disabled whenever CanBreakthrough says no. On success the row is re-derived
        /// from the new authoritative state (next stage, progress 0).
        /// </summary>
        public void TryBreakthrough(DiscipleState d)
        {
            if (d == null || _stateProvider == null) return;

            string failReason;
            if (!_stateProvider.TryBreakthrough(SectStateProvider.SectMasterRequesterId,
                                                d.DiscipleId, out failReason))
            {
                _view.SetBreakthroughState(false, failReason);
                return;
            }

            Show(d);
        }

        /// <summary>
        /// P10C — stamina + the three skill levels, read through the SAME P10A helpers the
        /// rules use (no second level formula, no cached copy). Called on open/switch and
        /// on the panel's ≤4/s tick while it is open; it only writes text, never a message.
        /// Attributes that are missing read as the healthy default / 0 XP.
        /// </summary>
        public void ShowAttributes(DiscipleState d)
        {
            if (d == null) return;
            var attributes = d.Attributes;

            _view.SetStamina(attributes != null ? attributes.Stamina : DiscipleAttributesConfig.StaminaDefault);
            _view.SetSkills(
                SkillLabel(attributes, DiscipleAttributesConfig.CategoryGathering),
                SkillLabel(attributes, DiscipleAttributesConfig.CategoryAlchemy),
                SkillLabel(attributes, DiscipleAttributesConfig.CategoryForging));
        }

        /// <summary>"Lv n · XP x" — level derived by the shared P10A helper, never recomputed here.</summary>
        private static string SkillLabel(DiscipleAttributes attributes, string category)
        {
            float xp = DiscipleAttributes.GetSkillXp(attributes, category);
            return "Lv " + DiscipleAttributes.SkillLevel(xp) + " · XP " + Mathf.RoundToInt(xp);
        }

        public void Hide() { }
    }

    /// <summary>
    /// Tab สเตตัส: RadarChartGraphic + mock array 6 ค่า generate ใน UI layer เท่านั้น
    /// (deterministic ต่อ disciple ด้วย seed จาก DiscipleId — ไม่เก็บ ไม่ผูก state)
    /// label แกนเป็นชื่อไทยตามภาพ เพราะเป็นแค่ label ไม่ใช่ data จริง
    /// </summary>
    public class StatusTabPresenter : IDiscipleTabPresenter
    {
        /// <summary>ชื่อแกน radar (label ประกอบภาพ — ไม่ใช่ field ของ state)</summary>
        public static readonly string[] AxisLabels =
        {
            "รากฐาน", "รากกระดูก", "ปัญญา", "ศักยภาพ", "เสน่ห์", "วาสนา",
        };

        private readonly RadarChartGraphic _radar;
        private readonly TMP_Text[] _labels;

        public StatusTabPresenter(RadarChartGraphic radar, TMP_Text[] labels)
        {
            _radar = radar;
            _labels = labels;
        }

        public void Show(DiscipleState d)
        {
            if (d == null) return;

            // mock deterministic: seed จาก id คนเดิมได้ค่าเดิมเสมอ (ไม่เก็บ, ไม่ผูก state)
            var seed = d.DiscipleId != null ? d.DiscipleId.GetHashCode() : 0;
            var rng = new System.Random(seed);
            var values = new int[6];
            for (int i = 0; i < values.Length; i++) values[i] = rng.Next(20, 101);

            if (_radar != null) _radar.SetValues(values);
            if (_labels != null)
            {
                for (int i = 0; i < _labels.Length && i < AxisLabels.Length; i++)
                {
                    if (_labels[i] != null && _labels[i].name.StartsWith("AxisLabel_")) _labels[i].text = AxisLabels[i];
                }
            }
            if (_labels != null)
                foreach (var text in _labels)
                    if (text != null && text.name == "Value" && text.transform.parent.name.StartsWith("Stat_"))
                    {
                        int index = int.Parse(text.transform.parent.name.Substring(5));
                        text.text = values[index].ToString();
                    }
            // These values are explicitly labelled examples, never saved as real attributes.
        }

        public void Hide() { }
    }

    /// <summary>
    /// ใช้ร่วมสำหรับแท็บที่ยังไม่มีระบบจริง (Equipment/Skill/Destiny/SpiritRoot):
    /// ข้อความเดียวว่างานยังไม่พร้อม — ห้ามแต่งเงื่อนไขปลดล็อกที่ไม่มีจริง
    /// (เช่น "ต้องถึงชั้น 5 ก่อน" — ไม่มีระบบชั้นให้อ้าง)
    /// </summary>
    public class PlaceholderTabPresenter : IDiscipleTabPresenter
    {
        private readonly TMP_Text _note;
        private readonly string _message;

        public PlaceholderTabPresenter(TMP_Text note, string message = "ยังไม่เปิดใช้งาน")
        {
            _note = note;
            _message = message;
        }

        public void Show(DiscipleState d)
        {
            if (_note != null) _note.text = _message;
        }

        public void Hide() { }
    }
}
