using System.Collections.Generic;
using MessagePack;
using MessagePipe;
using NUnit.Framework;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// P10A — disciple attribute DATA only. Covers defaults, normalization
    /// (null / NaN / Infinity / range / missing categories / saved zero), derived
    /// level boundaries, MessagePack round-trip, deep-copy independence, and the
    /// two construction sites (mock founders + RecruitOuterDisciple).
    /// No ticking/scoring/productivity/permission behavior is tested — none exists yet.
    /// </summary>
    public class DiscipleAttributesTests
    {
        private static readonly string[] Categories =
        {
            DiscipleAttributesConfig.CategoryGathering,
            DiscipleAttributesConfig.CategoryAlchemy,
            DiscipleAttributesConfig.CategoryForging,
        };

        // ---------- defaults ----------

        [Test]
        public void NewAttributes_HaveDefaultStamina()
        {
            var a = new DiscipleAttributes();
            Assert.AreEqual(DiscipleAttributesConfig.StaminaDefault, a.Stamina);
        }

        [Test]
        public void Normalize_FreshInstance_EnsuresThreeCategoriesAtZero()
        {
            var a = DiscipleAttributes.Normalize(new DiscipleAttributes());

            foreach (var c in Categories)
            {
                Assert.IsTrue(a.SkillXp.ContainsKey(c), "missing category after normalize: " + c);
                Assert.AreEqual(0f, a.SkillXp[c]);
            }
            Assert.AreEqual(3, a.SkillXp.Count, "normalize must not invent extra categories");
        }

        // ---------- range clamping ----------

        [Test]
        public void Normalize_ClampsStaminaAndXp()
        {
            var low = new DiscipleAttributes { Stamina = -50f };
            low.SkillXp[DiscipleAttributesConfig.CategoryGathering] = -10f;
            low.SkillXp[DiscipleAttributesConfig.CategoryAlchemy] = 99999f;

            DiscipleAttributes.Normalize(low);

            Assert.AreEqual(DiscipleAttributesConfig.StaminaMin, low.Stamina);
            Assert.AreEqual(DiscipleAttributesConfig.SkillXpMin, low.SkillXp[DiscipleAttributesConfig.CategoryGathering]);
            Assert.AreEqual(DiscipleAttributesConfig.SkillXpMax, low.SkillXp[DiscipleAttributesConfig.CategoryAlchemy]);

            var high = new DiscipleAttributes { Stamina = 500f };
            DiscipleAttributes.Normalize(high);
            Assert.AreEqual(DiscipleAttributesConfig.StaminaMax, high.Stamina);
        }

        // ---------- NaN / Infinity ----------

        [Test]
        public void Normalize_ReplacesNonFiniteWithDefaults()
        {
            var a = new DiscipleAttributes { Stamina = float.NaN };
            a.SkillXp[DiscipleAttributesConfig.CategoryGathering] = float.NaN;
            a.SkillXp[DiscipleAttributesConfig.CategoryAlchemy] = float.PositiveInfinity;
            a.SkillXp[DiscipleAttributesConfig.CategoryForging] = float.NegativeInfinity;

            DiscipleAttributes.Normalize(a);

            Assert.AreEqual(DiscipleAttributesConfig.StaminaDefault, a.Stamina);
            Assert.AreEqual(0f, a.SkillXp[DiscipleAttributesConfig.CategoryGathering]);
            Assert.AreEqual(0f, a.SkillXp[DiscipleAttributesConfig.CategoryAlchemy]);
            Assert.AreEqual(0f, a.SkillXp[DiscipleAttributesConfig.CategoryForging]);
        }

        [Test]
        public void Normalize_InfiniteStamina_BecomesDefault()
        {
            var a = new DiscipleAttributes { Stamina = float.PositiveInfinity };
            DiscipleAttributes.Normalize(a);
            Assert.AreEqual(DiscipleAttributesConfig.StaminaDefault, a.Stamina);
        }

        // ---------- derived level boundaries ----------

        [Test]
        public void SkillLevel_Boundaries()
        {
            Assert.AreEqual(0, DiscipleAttributes.SkillLevel(0f));
            Assert.AreEqual(0, DiscipleAttributes.SkillLevel(99.9f));
            Assert.AreEqual(1, DiscipleAttributes.SkillLevel(100f));
            Assert.AreEqual(9, DiscipleAttributes.SkillLevel(999.9f));
            Assert.AreEqual(10, DiscipleAttributes.SkillLevel(1000f));
            Assert.AreEqual(DiscipleAttributesConfig.SkillLevelMax, DiscipleAttributes.SkillLevel(5000f),
                "above cap clamps to the level cap");
        }

        [Test]
        public void SkillLevel_NonFiniteOrNegative_IsZero()
        {
            Assert.AreEqual(0, DiscipleAttributes.SkillLevel(float.NaN));
            Assert.AreEqual(0, DiscipleAttributes.SkillLevel(float.PositiveInfinity));
            Assert.AreEqual(0, DiscipleAttributes.SkillLevel(float.NegativeInfinity));
            Assert.AreEqual(0, DiscipleAttributes.SkillLevel(-100f));
        }

        // ---------- missing-category accessor ----------

        [Test]
        public void GetSkillXp_MissingCategoryOrNull_ReturnsZero()
        {
            var a = DiscipleAttributes.Normalize(new DiscipleAttributes());

            Assert.AreEqual(0f, DiscipleAttributes.GetSkillXp(a, "not_a_category"));
            Assert.AreEqual(0f, DiscipleAttributes.GetSkillXp(null, DiscipleAttributesConfig.CategoryGathering));
            Assert.AreEqual(0f, DiscipleAttributes.GetSkillXp(new DiscipleAttributes { SkillXp = null },
                                                              DiscipleAttributesConfig.CategoryGathering));
            Assert.AreEqual(0f, DiscipleAttributes.GetSkillXp(a, null));
        }

        [Test]
        public void GetSkillXp_PresentValue_ReturnedAndLevelDerived()
        {
            var a = DiscipleAttributes.Normalize(new DiscipleAttributes());
            a.SkillXp[DiscipleAttributesConfig.CategoryForging] = 250f;

            Assert.AreEqual(250f, DiscipleAttributes.GetSkillXp(a, DiscipleAttributesConfig.CategoryForging));
            Assert.AreEqual(2, a.SkillLevelOf(DiscipleAttributesConfig.CategoryForging));
            Assert.AreEqual(0, a.SkillLevelOf("not_a_category"));
        }

        // ---------- null / older-data normalization ----------

        [Test]
        public void Normalize_Null_ReturnsUsableDefault()
        {
            var a = DiscipleAttributes.Normalize(null);

            Assert.IsNotNull(a);
            Assert.AreEqual(DiscipleAttributesConfig.StaminaDefault, a.Stamina);
            Assert.IsNotNull(a.SkillXp);
            foreach (var c in Categories) Assert.IsTrue(a.SkillXp.ContainsKey(c));
        }

        [Test]
        public void Normalize_NullDictionary_IsReplacedInPlace()
        {
            var a = new DiscipleAttributes { SkillXp = null };

            var result = DiscipleAttributes.Normalize(a);

            Assert.AreSame(a, result, "non-null attributes are repaired in place");
            Assert.IsNotNull(a.SkillXp);
            foreach (var c in Categories) Assert.IsTrue(a.SkillXp.ContainsKey(c));
        }

        [Test]
        public void Normalize_ExtraCategory_IsKeptButSanitized()
        {
            var a = new DiscipleAttributes();
            a.SkillXp["legacy_unknown"] = 5000f;

            DiscipleAttributes.Normalize(a);

            Assert.IsTrue(a.SkillXp.ContainsKey("legacy_unknown"), "unknown keys are preserved");
            Assert.AreEqual(DiscipleAttributesConfig.SkillXpMax, a.SkillXp["legacy_unknown"]);
        }

        [Test]
        public void FromByteArray_LegacyRosterWithoutAttributesKey_RepairsOnLoad()
        {
            var legacy = new LegacyStateWithoutAttributes
            {
                Disciples = new List<LegacyDiscipleWithoutAttributes>
                {
                    new LegacyDiscipleWithoutAttributes { DiscipleId = "d_legacy" },
                },
            };

            var restored = SectEconomyState.FromByteArray(MessagePackSerializer.Serialize(legacy));

            Assert.AreEqual(1, restored.Disciples.Count);
            var d = restored.Disciples[0];
            Assert.AreEqual("d_legacy", d.DiscipleId);
            Assert.IsNotNull(d.Attributes, "legacy state must load with a valid attribute block");
            foreach (var c in Categories) Assert.IsTrue(d.Attributes.SkillXp.ContainsKey(c));
        }

        [Test]
        public void FromByteArray_NullAttributes_RewrittenOnLoad()
        {
            var state = new SectEconomyState();
            state.Disciples.Add(new DiscipleState { DiscipleId = "d_old", Attributes = null });

            var restored = SectEconomyState.FromByteArray(state.ToByteArray());

            var d = restored.Disciples[0];
            Assert.IsNotNull(d.Attributes, "load must repair a null attribute block");
            Assert.AreEqual(DiscipleAttributesConfig.StaminaDefault, d.Attributes.Stamina);
            foreach (var c in Categories) Assert.IsTrue(d.Attributes.SkillXp.ContainsKey(c));
        }

        // ---------- saved zero preserved ----------

        [Test]
        public void Normalize_PreservesLegitimatelySavedZero()
        {
            var a = new DiscipleAttributes { Stamina = 0f };
            a.SkillXp[DiscipleAttributesConfig.CategoryGathering] = 0f;
            a.SkillXp[DiscipleAttributesConfig.CategoryAlchemy] = 300f;

            DiscipleAttributes.Normalize(a);

            Assert.AreEqual(0f, a.Stamina, "exhausted stamina 0 is a real value, not 'missing'");
            Assert.AreEqual(0f, a.SkillXp[DiscipleAttributesConfig.CategoryGathering],
                "a saved 0 XP must not be replaced with a default");
            Assert.AreEqual(300f, a.SkillXp[DiscipleAttributesConfig.CategoryAlchemy]);
        }

        // ---------- MessagePack round-trip ----------

        [Test]
        public void MessagePack_RoundTrip_PreservesAttributes()
        {
            var a = DiscipleAttributes.Normalize(new DiscipleAttributes { Stamina = 42.5f });
            a.SkillXp[DiscipleAttributesConfig.CategoryGathering] = 87.5f;
            a.SkillXp[DiscipleAttributesConfig.CategoryForging] = 400f;

            var restored = MessagePackSerializer.Deserialize<DiscipleAttributes>(
                MessagePackSerializer.Serialize(a));

            Assert.AreEqual(42.5f, restored.Stamina);
            Assert.AreEqual(87.5f, restored.SkillXp[DiscipleAttributesConfig.CategoryGathering]);
            Assert.AreEqual(400f, restored.SkillXp[DiscipleAttributesConfig.CategoryForging]);
        }

        [Test]
        public void DiscipleState_RoundTrip_PreservesAttributes()
        {
            var state = new SectEconomyState();
            var d = new DiscipleState { DiscipleId = "d_rt" };
            d.Attributes = DiscipleAttributes.Normalize(new DiscipleAttributes { Stamina = 77f });
            d.Attributes.SkillXp[DiscipleAttributesConfig.CategoryAlchemy] = 250f;
            state.Disciples.Add(d);

            var restored = SectEconomyState.FromByteArray(state.ToByteArray());

            Assert.AreEqual(77f, restored.Disciples[0].Attributes.Stamina);
            Assert.AreEqual(250f, restored.Disciples[0].Attributes.SkillXp[DiscipleAttributesConfig.CategoryAlchemy]);
        }

        // ---------- deep copy independence ----------

        [Test]
        public void Clone_DoesNotShareDictionary()
        {
            var a = DiscipleAttributes.Normalize(new DiscipleAttributes { Stamina = 50f });
            a.SkillXp[DiscipleAttributesConfig.CategoryGathering] = 120f;

            var copy = a.Clone();

            Assert.AreNotSame(a, copy);
            Assert.AreEqual(a.Stamina, copy.Stamina);
            Assert.AreNotSame(a.SkillXp, copy.SkillXp, "SkillXp dictionary must not be shared");

            copy.SkillXp[DiscipleAttributesConfig.CategoryGathering] = 999f;
            Assert.AreEqual(120f, a.SkillXp[DiscipleAttributesConfig.CategoryGathering],
                "mutating the copy must not touch the source");
        }

        // ---------- construction sites ----------

        [Test]
        public void MockFounders_HaveValidDistinctAttributes()
        {
            var state = MockSectData.Create();
            Assert.AreEqual(4, state.Disciples.Count);

            var signatures = new HashSet<string>();
            var dictionaries = new List<Dictionary<string, float>>();

            foreach (var d in state.Disciples)
            {
                Assert.IsNotNull(d.Attributes, d.DiscipleId + " must have attributes");
                Assert.IsNotNull(d.Attributes.SkillXp, d.DiscipleId + " must have a skill map");
                Assert.AreEqual(DiscipleAttributesConfig.StaminaDefault, d.Attributes.Stamina);
                foreach (var c in Categories)
                    Assert.IsTrue(d.Attributes.SkillXp.ContainsKey(c), d.DiscipleId + " missing " + c);

                signatures.Add(
                    d.Attributes.SkillXp[DiscipleAttributesConfig.CategoryGathering] + "," +
                    d.Attributes.SkillXp[DiscipleAttributesConfig.CategoryAlchemy] + "," +
                    d.Attributes.SkillXp[DiscipleAttributesConfig.CategoryForging]);
                dictionaries.Add(d.Attributes.SkillXp);
            }

            Assert.AreEqual(4, signatures.Count, "the four founders must have distinct skill XP");
            for (int i = 0; i < dictionaries.Count; i++)
            {
                for (int j = i + 1; j < dictionaries.Count; j++)
                {
                    Assert.AreNotSame(dictionaries[i], dictionaries[j],
                        "founders must not share a SkillXp dictionary instance");
                }
            }
        }

        [Test]
        public void RecruitOuterDisciple_ProducesValidAttributes()
        {
            var provider = NewProvider();
            // BuildSectEconomyState returns the LIVE roster, so capture the count by
            // value — holding the list reference would see it grow after the recruit.
            int beforeCount = provider.BuildSectEconomyState().Disciples.Count;

            provider.RecruitOuterDisciple();

            var disciples = provider.BuildSectEconomyState().Disciples;
            Assert.AreEqual(beforeCount + 1, disciples.Count);
            var recruit = disciples[disciples.Count - 1];

            Assert.IsNotNull(recruit.Attributes, "recruit must have attributes");
            Assert.AreEqual(DiscipleAttributesConfig.StaminaDefault, recruit.Attributes.Stamina);
            foreach (var c in Categories)
                Assert.IsTrue(recruit.Attributes.SkillXp.ContainsKey(c), "recruit missing " + c);

            Assert.AreNotSame(disciples[0].Attributes.SkillXp, recruit.Attributes.SkillXp,
                "each disciple gets its own SkillXp dictionary");
        }

        // ---------- helpers ----------

        private static SectStateProvider NewProvider()
        {
            return new SectStateProvider(
                new BufferPublisher<DiscipleRecruitedMessage>(),
                new BufferPublisher<SectResourceChangedMessage>(),
                new BufferPublisher<AvatarEquipmentChangedMessage>(),
                new BufferPublisher<DiscipleChibiBackendChangedMessage>(),
                new AvatarPartPool(),
                VisualRuntimeConfig.Instance,
                new DefaultEntitlementProvider(),
                new BuildingDefPool(),
                new BufferPublisher<BuildingPlacedMessage>(),
                new BufferPublisher<DiscipleTaskChangedMessage>());
        }

        [MessagePackObject]
        internal sealed class LegacyDiscipleWithoutAttributes
        {
            [Key(0)] public string DiscipleId { get; set; }
        }

        [MessagePackObject]
        internal sealed class LegacyStateWithoutAttributes
        {
            [Key(0)] public List<LegacyDiscipleWithoutAttributes> Disciples { get; set; }
        }

        /// <summary>Minimal IPublisher&lt;T&gt; — same pattern as AssignTaskTests.</summary>
        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public void Publish(T message) { }
        }
    }
}
