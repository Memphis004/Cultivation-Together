using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Xianxia.Sect.UI;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// RadarChartGraphic — สัญญาตามแผน §4: ค่า 0/ติดลบ/เกิน max ต้องไม่ throw
    /// (clamp เข้าช่วง), และ SetVerticesDirty เฉพาะตอนค่าเปลี่ยน (ห้าม dirty ทุกเฟรม)
    /// </summary>
    public class RadarChartGraphicTests
    {
        private GameObject _go;
        private RadarChartGraphic _radar;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("RadarTest", typeof(RectTransform), typeof(CanvasRenderer));
            _go.GetComponent<RectTransform>().sizeDelta = new Vector2(300f, 300f);
            _radar = _go.AddComponent<RadarChartGraphic>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void SetValues_Zero_Negative_AndOverMax_DoNotThrow_AndClamp()
        {
            Assert.DoesNotThrow(() => _radar.SetValues(new[] { 0, -50, 500, 50, 0, 100 }));

            var v = _radar.GetValues();
            Assert.AreEqual(6, v.Count, "always 6 axes");
            Assert.AreEqual(0, v[0]);
            Assert.AreEqual(0, v[1], "negative clamps to 0");
            Assert.AreEqual((int)_radar.MaxValue, v[2], "over-max clamps to max");
            Assert.AreEqual(50, v[3]);
            Assert.AreEqual(0, v[4]);
            Assert.AreEqual(100, v[5]);
        }

        [Test]
        public void SetValues_Null_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _radar.SetValues(null));
        }

        [Test]
        public void Mesh_Generates_WithoutError_WhenCanvasRebuilds()
        {
            _radar.SetValues(new[] { 30, 40, 50, 60, 70, 80 });

            // rebuild จริงผ่าน canvas flow — OnPopulateMesh ต้องไม่ throw
            // (force ด้วย ForceUpdateCanvases — ไม่มี Canvas ก็ no-op ได้ จึงเรียก Rebuild ตรงด้วย)
            Assert.DoesNotThrow(() => _radar.Rebuild(CanvasUpdate.PreRender));
            Assert.DoesNotThrow(() => Canvas.ForceUpdateCanvases());
        }

        [Test]
        public void Values_Persist_AfterTabSwitchSimulation()
        {
            _radar.SetValues(new[] { 10, 20, 30, 40, 50, 60 });
            var before = _radar.GetValues();

            // จำลอง "สลับแท็บกลับมา" — ค่าต้องยังอยู่ (ไม่ rebuild/reset)
            _radar.SetValues(new[] { 10, 20, 30, 40, 50, 60 });
            var after = _radar.GetValues();

            for (int i = 0; i < before.Count; i++)
                Assert.AreEqual(before[i], after[i], "value " + i + " must persist");
        }

        [Test]
        public void SetValues_Dirty_OnlyOnChange()
        {
            _radar.SetValues(new[] { 1, 2, 3, 4, 5, 6 });
            Assert.IsTrue(_radar.IsDirty(), "changed values must mark dirty");

            _radar.Rebuild(CanvasUpdate.PreRender); // mesh built → flag clear
            Assert.IsFalse(_radar.IsDirty(), "rebuild must clear the flag");

            _radar.SetValues(new[] { 1, 2, 3, 4, 5, 6 }); // ค่าเดิมทุกตัว
            Assert.IsFalse(_radar.IsDirty(), "same values must NOT mark dirty (no per-frame rebuild)");

            _radar.SetValues(new[] { 1, 2, 3, 4, 5, 7 }); // ต่าง 1 ตัว
            Assert.IsTrue(_radar.IsDirty(), "any change must mark dirty");
        }
    }
}
