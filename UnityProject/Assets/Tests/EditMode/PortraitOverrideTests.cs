using NUnit.Framework;
using UnityEngine;
using Xianxia.Sect.UI;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// Portrait v2 — PortraitOverrideMap / PortraitOverrideBinding:
    /// - ตาราง parse ถูก, entry พัง/ว่าง ถูกข้าม, reload ซ้ำ deterministic
    /// - ศิษย์ที่ไม่มีในตาราง (รวม recruit ใหม่) ต้องได้ string ว่าง = fallback สู่
    ///   layered portrait เดิม 100% (สัญญาที่ห้ามพัง)
    /// - binding เก็บ/เปลี่ยน discipleId ได้ (rail reuse) โดยไม่ต้อง notify renderer
    /// </summary>
    public class PortraitOverrideTests
    {
        private const string SampleJson =
            "{ \"overrides\": [" +
            "  { \"discipleId\": \"d000\", \"portraitResourcePath\": \"Avatar/Portraits/portrait_d000\" }," +
            "  { \"discipleId\": \"d002\", \"portraitResourcePath\": \"Avatar/Portraits/portrait_d002\" }" +
            "]}";

        [Test]
        public void LoadFromJson_ParsesEntries()
        {
            var map = new PortraitOverrideMap();
            map.LoadFromJson(SampleJson);

            Assert.AreEqual(2, map.Count);
            Assert.AreEqual("Avatar/Portraits/portrait_d000", map.ResolveOverride("d000"));
            Assert.AreEqual("Avatar/Portraits/portrait_d002", map.ResolveOverride("d002"));
            Assert.IsTrue(map.HasOverride("d000"));
            Assert.IsTrue(map.HasAny());
        }

        [Test]
        public void ResolveOverride_UnknownDisciple_ReturnsEmpty()
        {
            var map = new PortraitOverrideMap();
            map.LoadFromJson(SampleJson);

            // recruit ใหม่ / ศิษย์ที่ไม่มีภาพเฉพาะตัว (เช่น d001) = fallback layered
            Assert.AreEqual(string.Empty, map.ResolveOverride("d999"));
            Assert.IsFalse(map.HasOverride("d001"));
        }

        [Test]
        public void ResolveOverride_NullOrEmptyId_ReturnsEmpty()
        {
            var map = new PortraitOverrideMap();
            map.LoadFromJson(SampleJson);

            Assert.AreEqual(string.Empty, map.ResolveOverride(null));
            Assert.AreEqual(string.Empty, map.ResolveOverride(""));
        }

        [Test]
        public void LoadFromJson_SkipsMalformedEntries()
        {
            var map = new PortraitOverrideMap();
            map.LoadFromJson(
                "{ \"overrides\": [" +
                "  { \"discipleId\": \"\", \"portraitResourcePath\": \"Avatar/Portraits/x\" }," +
                "  { \"discipleId\": \"d010\", \"portraitResourcePath\": \"\" }," +
                "  { \"discipleId\": \"d011\", \"portraitResourcePath\": \"Avatar/Portraits/ok\" }" +
                "]}");

            Assert.AreEqual(1, map.Count);
            Assert.AreEqual("Avatar/Portraits/ok", map.ResolveOverride("d011"));
        }

        [Test]
        public void LoadFromJson_Reload_ReplacesPreviousEntries()
        {
            var map = new PortraitOverrideMap();
            map.LoadFromJson(SampleJson);
            map.LoadFromJson(
                "{ \"overrides\": [ { \"discipleId\": \"d003\", \"portraitResourcePath\": \"Avatar/Portraits/portrait_d003\" } ] }");

            Assert.AreEqual(1, map.Count);
            Assert.AreEqual(string.Empty, map.ResolveOverride("d000"), "entries เก่าต้องถูกล้างตอน reload");
            Assert.AreEqual("Avatar/Portraits/portrait_d003", map.ResolveOverride("d003"));
        }

        [Test]
        public void LoadFromJson_InvalidJson_NeverThrows()
        {
            var map = new PortraitOverrideMap();
            map.LoadFromJson("this is not json {{{");
            Assert.AreEqual(0, map.Count);

            map.LoadFromJson(null);
            Assert.AreEqual(0, map.Count);
        }

        [Test]
        public void Binding_SetDiscipleId_Roundtrips()
        {
            var go = new GameObject("PortraitOverrideBindingTest");
            try
            {
                var binding = go.AddComponent<PortraitOverrideBinding>();
                Assert.AreEqual(string.Empty, binding.DiscipleId);

                binding.SetDiscipleId("d002");
                Assert.AreEqual("d002", binding.DiscipleId);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Binding_SetDiscipleId_NullBecomesEmpty()
        {
            var go = new GameObject("PortraitOverrideBindingTest2");
            try
            {
                var binding = go.AddComponent<PortraitOverrideBinding>();
                binding.SetDiscipleId("d000");
                binding.SetDiscipleId(null);
                Assert.AreEqual(string.Empty, binding.DiscipleId);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
