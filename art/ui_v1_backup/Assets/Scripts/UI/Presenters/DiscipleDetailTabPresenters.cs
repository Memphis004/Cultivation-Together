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
    /// + placeholder note "ระบบสายบำเพ็ญ — ยังไม่มีข้อมูล"
    /// (คู่แทร็กบำเพ็ญ / CultivationTrack ไม่มีใน state — backlog แยกงาน)
    /// </summary>
    public class InfoTabPresenter : IDiscipleTabPresenter
    {
        private readonly DiscipleDetailView _view;

        public InfoTabPresenter(DiscipleDetailView view)
        {
            _view = view;
        }

        public void Show(DiscipleState d)
        {
            if (d == null) return;
            _view.SetInfo(
                d.DisplayName,
                d.Rank.ToString(),
                string.IsNullOrEmpty(d.CurrentTask) ? "—" : d.CurrentTask,
                "Spirit Stones: " + (d.Wallet?.SpiritStones ?? 0) +
                " · Contribution: " + (d.Wallet?.Contribution ?? 0));
            // TODO(backlog): คู่แทร็กบำเพ็ญ (CultivationTrack) เมื่อมี data model จริง
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
                    if (_labels[i] != null) _labels[i].text = AxisLabels[i];
                }
            }
            // TODO(backlog): DiscipleAttributes จริงเมื่อมี field ใน state
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

        public PlaceholderTabPresenter(TMP_Text note, string message = "ยังไม่พร้อมใช้งาน")
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
