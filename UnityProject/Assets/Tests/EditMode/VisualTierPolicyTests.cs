using System.Collections.Generic;
using NUnit.Framework;
using Xianxia.Sect;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// Phase 3 — VisualTierPolicy (§7) unit tests. Pure C# — no play mode needed.
    ///
    /// Covers the acceptance criteria verifiable WITHOUT the S4 license gate:
    /// entitlement resolution, budget degrade (render-only — state untouched),
    /// priority ordering (rank desc, then DiscipleId asc), and Spine-off behavior.
    /// The full runtime path (d000/d003 actually spawning as SpineChibiVisual) is
    /// gated behind SpineActivationRequested — verified when the license decision
    /// lands, not here.
    /// </summary>
    public sealed class VisualTierPolicyTests
    {
        private bool _savedSpineEnabled;
        private int _savedBudget;

        [SetUp]
        public void SetUp()
        {
            _savedSpineEnabled = VisualRuntimeConfig.Instance.SpineEnabled;
            _savedBudget = VisualRuntimeConfig.Instance.SpineBudget;
            VisualRuntimeConfig.Instance.SpineEnabled = true;
            VisualRuntimeConfig.Instance.SpineBudget = 20;
        }

        [TearDown]
        public void TearDown()
        {
            VisualRuntimeConfig.Instance.SpineEnabled = _savedSpineEnabled;
            VisualRuntimeConfig.Instance.SpineBudget = _savedBudget;
        }

        private static DiscipleState Make(string id, DiscipleRank rank, ChibiBackend backend)
        {
            return new DiscipleState { DiscipleId = id, Rank = rank, ChibiBackend = backend };
        }

        [Test]
        public void EffectiveBackend_SpineEntitled_UnderBudget_IsSpine()
        {
            var d = Make("d000", DiscipleRank.SectMaster, ChibiBackend.Spine);
            Assert.AreEqual(VisualBackend.Spine, VisualTierPolicy.Instance.EffectiveBackend(d, 0));
        }

        [Test]
        public void EffectiveBackend_SpriteEntitled_AlwaysSprite()
        {
            var d = Make("d001", DiscipleRank.InnerDisciple, ChibiBackend.SpriteSheet);
            Assert.AreEqual(VisualBackend.SpriteSheet, VisualTierPolicy.Instance.EffectiveBackend(d, 0));
        }

        [Test]
        public void EffectiveBackend_OverBudget_DegradesToSprite_StateUntouched()
        {
            // budget=1, one Spine already active → the second entitled disciple
            // renders Sprite while the entitlement in state stays Spine (§7, render-only)
            VisualRuntimeConfig.Instance.SpineBudget = 1;
            var d = Make("d003", DiscipleRank.Elder, ChibiBackend.Spine);
            var effective = VisualTierPolicy.Instance.EffectiveBackend(d, 1);
            Assert.AreEqual(VisualBackend.SpriteSheet, effective);
            Assert.AreEqual(ChibiBackend.Spine, d.ChibiBackend, "entitlement must never be mutated by degrade");
        }

        [Test]
        public void EffectiveBackend_SpineDisabled_EveryoneIsSprite()
        {
            VisualRuntimeConfig.Instance.SpineEnabled = false;
            var d = Make("d000", DiscipleRank.SectMaster, ChibiBackend.Spine);
            Assert.AreEqual(VisualBackend.SpriteSheet, VisualTierPolicy.Instance.EffectiveBackend(d, 0));
        }

        [Test]
        public void AllocateSpineSlots_PriorityWinsSlots_RankDescThenIdAsc()
        {
            // d000 (SectMaster, Spine), d003 (Elder, Spine), d006 (Elder, Spine, later id)
            var roster = new List<DiscipleState>
            {
                Make("d003", DiscipleRank.Elder, ChibiBackend.Spine),
                Make("d000", DiscipleRank.SectMaster, ChibiBackend.Spine),
                Make("d006", DiscipleRank.Elder, ChibiBackend.Spine),
                Make("d001", DiscipleRank.InnerDisciple, ChibiBackend.SpriteSheet),
            };

            var allocated = new HashSet<string>(System.StringComparer.Ordinal);
            VisualTierPolicy.Instance.AllocateSpineSlots(roster, s => s.DiscipleId, allocated);

            Assert.IsTrue(allocated.Contains("d000"), "SectMaster must win a slot");
            // budget 20 > 3 entitled → all entitled disciples allocated, sprite one is not
            Assert.IsTrue(allocated.Contains("d003"));
            Assert.IsTrue(allocated.Contains("d006"));
            Assert.IsFalse(allocated.Contains("d001"));
        }

        [Test]
        public void AllocateSpineSlots_TightBudget_GoesToHighestPriority()
        {
            VisualRuntimeConfig.Instance.SpineBudget = 1;

            var roster = new List<DiscipleState>
            {
                Make("d003", DiscipleRank.Elder, ChibiBackend.Spine),
                Make("d000", DiscipleRank.SectMaster, ChibiBackend.Spine),
            };

            var allocated = new HashSet<string>(System.StringComparer.Ordinal);
            VisualTierPolicy.Instance.AllocateSpineSlots(roster, s => s.DiscipleId, allocated);

            Assert.AreEqual(1, allocated.Count);
            Assert.IsTrue(allocated.Contains("d000"), "rank desc wins the single slot");
            Assert.AreEqual(ChibiBackend.Spine, roster[0].ChibiBackend, "d003 entitlement untouched");
            Assert.AreEqual(ChibiBackend.Spine, roster[1].ChibiBackend, "d000 entitlement untouched");
        }

        [Test]
        public void AllocateSpineSlots_WhenSpineDisabled_AllocatesNobody()
        {
            VisualRuntimeConfig.Instance.SpineEnabled = false;

            var roster = new List<DiscipleState> { Make("d000", DiscipleRank.SectMaster, ChibiBackend.Spine) };
            var allocated = new HashSet<string>(System.StringComparer.Ordinal);
            VisualTierPolicy.Instance.AllocateSpineSlots(roster, s => s.DiscipleId, allocated);

            Assert.AreEqual(0, allocated.Count);
            Assert.AreEqual(ChibiBackend.Spine, roster[0].ChibiBackend, "state untouched");
        }

        [Test]
        public void SortBySpawnPriority_SameRank_OrderByIdAscending()
        {
            var a = Make("d010", DiscipleRank.OuterDisciple, ChibiBackend.SpriteSheet);
            var b = Make("d009", DiscipleRank.OuterDisciple, ChibiBackend.SpriteSheet);
            var list = new List<DiscipleState> { a, b };
            VisualTierPolicy.Instance.SortBySpawnPriority(list);
            Assert.AreEqual("d009", list[0].DiscipleId, "same rank → id ascending for determinism");
        }
    }
}
