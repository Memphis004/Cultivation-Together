using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// Radar (spider) chart — MaskableGraphic ตัวเดียว mesh เดียว:
    /// วาดโครงใยแมงมุม (วงแหวน + เส้นแกน) เป็น line strips บาง ๆ และรูป
    /// polygon ค่าจริงเป็น fill กึ่งโปร่งใน mesh เดียวกัน
    ///
    /// Phase 1: ผูกกับ mock data ที่ presenter generate เอง (deterministic ต่อ
    /// disciple แต่ไม่เก็บ ไม่ผูก state) — label 6 ตัวเป็น TMP วางตำแหน่งไว้ใน
    /// generator แล้ว presenter set แค่ .text
    ///
    /// ห้ามเรียก SetValues ทุกเฟรม: เรียกเฉพาะตอนค่าเปลี่ยน (SetVerticesDirty)
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class RadarChartGraphic : MaskableGraphic
    {
        [SerializeField] private int axes = 6;

        [SerializeField] private float maxValue = 100f;

        [SerializeField] private Color lineColor = new Color(0.85f, 0.72f, 0.35f, 0.9f);
        [SerializeField] private Color webColor = new Color(1f, 1f, 1f, 0.18f);
        [SerializeField] private Color fillColor = new Color(1f, 0.85f, 0.45f, 0.35f);
        [SerializeField] private int webRings = 4;

        private readonly int[] _values = new int[6];

        public int Axes => axes;
        public float MaxValue => maxValue;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        /// <summary>
        /// ตั้งค่า 6 แกน — clamp ทุกค่าเข้าช่วง [0, maxValue] แล้ว dirty เฉพาะ
        /// เมื่อค่าเปลี่ยนจริง (ค่า 0/ติดลบ/เกิน max ต้องไม่ throw — tests pin)
        /// </summary>
        public void SetValues(int[] values)
        {
            if (values == null) return;

            var changed = false;
            for (int i = 0; i < _values.Length; i++)
            {
                int raw = i < values.Length ? values[i] : 0;
                int clamped = Mathf.Clamp(raw, 0, Mathf.RoundToInt(maxValue));
                if (_values[i] != clamped) changed = true;
                _values[i] = clamped;
            }

            if (changed)
            {
                _dirty = true;
                SetVerticesDirty();
            }
        }

        /// <summary>ค่าปัจจุบัน (สำหรับ tests/verify — clamp แล้ว)</summary>
        public IReadOnlyList<int> GetValues() => _values;

        /// <summary>
        /// สถานะ dirty ปัจจุบัน (test seam): true เมื่อ SetValues เห็นค่าเปลี่ยน
        /// และถูกเคลียร์เมื่อ OnPopulateMesh สร้าง mesh แล้ว — ใช้ยืนยันว่า
        /// เรียก SetVerticesDirty เฉพาะตอนค่าเปลี่ยนจริง (แผน §4)
        /// </summary>
        public bool IsDirty() => _dirty;

        private bool _dirty;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            _dirty = false; // mesh สร้างแล้ว — เคลียร์ flag (test seam)
            vh.Clear();

            var rect = GetPixelAdjustedRect();
            var center = rect.center;
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f * 0.86f; // เผื่อขอบ

            // ── วงแหวนใยแมงมุม (line strip บาง ๆ ต่อวง) ──
            if (webRings > 0)
            {
                for (int ring = 1; ring <= webRings; ring++)
                {
                    float r = radius * ring / webRings;
                    AddPolygonOutline(vh, center, r, webColor);
                }
            }

            // ── เส้นแกน 6 เส้น (จากกลางถึงขอบ) ──
            for (int a = 0; a < axes; a++)
            {
                var dir = AxisDir(a);
                AddLine(vh, center, center + dir * radius, webColor);
            }

            // ── polygon ค่าจริง: fill + ขอบ ──
            var pts = new Vector2[axes];
            for (int a = 0; a < axes; a++)
            {
                float t = maxValue > 0f ? Mathf.Clamp01(_values[a] / maxValue) : 0f;
                pts[a] = center + AxisDir(a) * (radius * t);
            }

            int vi = 0;
            for (int a = 0; a < axes; a++) // fan fill (center + จุดขอบคู่ข้างต่อ triangle)
            {
                var v = UIVertex.simpleVert;
                v.color = fillColor;
                if (a == 0) { v.position = center; vh.AddVert(v); }
                var p1 = v; p1.position = pts[a];
                var p2 = v; p2.position = pts[(a + 1) % axes];
                vh.AddVert(p1);
                vh.AddVert(p2);
                vh.AddTriangle(vi, vi + 1, vi + 2);
                vi += 2;
            }

            // ขอบ polygon ค่าจริง
            for (int a = 0; a < axes; a++)
            {
                AddQuadLine(vh, pts[a], pts[(a + 1) % axes], lineColor);
            }
        }

        private Vector2 AxisDir(int a)
        {
            // เริ่มที่บน (12 นาฬิกา) แล้วเวียนตามเข็ม
            float angle = Mathf.PI / 2f - 2f * Mathf.PI * a / axes;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        private static void AddPolygonOutline(VertexHelper vh, Vector2 center, float radius, Color color)
        {
            // เก็บตำแหน่งไว้เอง — ไม่ read back จาก VertexHelper (กัน O(n²)/fragile)
            s_RingPoints.Clear();
            for (int a = 0; a < 360; a += 12) // segment 30° พอสำหรับ hex web
            {
                float rad = a * Mathf.Deg2Rad;
                s_RingPoints.Add(center + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * radius);
            }
            for (int i = 0; i < s_RingPoints.Count; i++)
            {
                AddQuadLine(vh, s_RingPoints[i], s_RingPoints[(i + 1) % s_RingPoints.Count], color);
            }
        }

        private static readonly List<Vector2> s_RingPoints = new List<Vector2>(32);

        private static void AddLine(VertexHelper vh, Vector2 from, Vector2 to, Color color)
        {
            AddQuadLine(vh, from, to, color);
        }

        /// <summary>เส้นบาง ๆ = quad 2 สามเหลี่ยม หนา 2px รอบแนวเส้น</summary>
        private static void AddQuadLine(VertexHelper vh, Vector2 from, Vector2 to, Color color, float thickness = 2f)
        {
            var dir = (to - from);
            float len = dir.magnitude;
            if (len < 0.001f) return;
            var n = new Vector2(-dir.y, dir.x) / len * (thickness * 0.5f);

            int start = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert;
            v.color = color;
            v.position = from + n; vh.AddVert(v);
            v.position = from - n; vh.AddVert(v);
            v.position = to - n;   vh.AddVert(v);
            v.position = to + n;   vh.AddVert(v);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
