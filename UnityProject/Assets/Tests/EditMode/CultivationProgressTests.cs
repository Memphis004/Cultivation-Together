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
    /// P14A — cultivation progression DATA + the meditation accrual.
    ///
    /// Covered here:
    ///   - the stage table helpers (first/known/requirement/next/final/ready);
    ///   - normalization: null/empty id → first stage, NaN/Infinity/negative → 0,
    ///     clamp to the stage requirement, final stage → 0, a saved 0 preserved;
    ///   - progress accrues ONLY while the disciple is Resting (meditation), only from
    ///     the tick's simulation delta, clamped at the requirement with no carry-over;
    ///   - the final stage accrues nothing; pause (delta 0) accrues nothing;
    ///   - equal simulation time at 1x and 3x yields equal progress (speed applied once);
    ///   - meditation stamina regeneration is EXACTLY the P10B rate (unchanged);
    ///   - rank and cultivation stage are independent (neither derives from the other);
    ///   - the mock founders and a recruit are born valid;
    ///   - MessagePack round-trip, legacy payload without the new keys → defaults,
    ///     deep copy independence, and a NON-EMPTY unknown stage id rejected on restore.
    ///
    /// No breakthrough operation, no UI and no MCP tool exists in P14A — none is tested.
    /// Same provider construction / publisher pattern as DiscipleWorkAttributeTests.
    /// </summary>
    public class CultivationProgressTests
    {
        private const float Tol = 1e-4f;

        private SectStateProvider _provider;

        [SetUp]
        public void SetUp()
        {
            _provider = new SectStateProvider(
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

        // ---------------------------------------------------------------- helpers

        private SectEconomyState State => _provider.BuildSectEconomyState();

        private DiscipleState Find(string id)
        {
            foreach (var d in State.Disciples)
                if (d.DiscipleId == id) return d;
            return null;
        }

        private float Progress(string id) => Find(id).Attributes.CultivationProgress;
        private string Stage(string id) => Find(id).Attributes.CultivationStageId;

        private void SetCultivation(string id, string stageId, float progress)
        {
            var a = Find(id).Attributes;
            a.CultivationStageId = stageId;
            a.CultivationProgress = progress;
        }

        // ------------------------------------------------------- stage table API

        [Test]
        public void StageTable_IsOrdered_AndHelpersAgree()
        {
            Assert.AreEqual(CultivationConfig.StageQiCondensation, CultivationConfig.FirstStageId);
            Assert.AreEqual(4, CultivationConfig.Stages.Length);

            // every id is known, unique and non-empty
            var seen = new HashSet<string>();
            for (int i = 0; i < CultivationConfig.Stages.Length; i++)
            {
                var def = CultivationConfig.Stages[i];
                Assert.IsTrue(CultivationConfig.IsKnownStage(def.Id), "unknown row id: " + def.Id);
                Assert.IsTrue(seen.Add(def.Id), "duplicate row id: " + def.Id);
                Assert.IsFalse(string.IsNullOrEmpty(def.DisplayName), "missing Thai display name: " + def.Id);
            }

            // only the LAST row is final and only it has no requirement
            for (int i = 0; i < CultivationConfig.Stages.Length; i++)
            {
                bool isLast = i == CultivationConfig.Stages.Length - 1;
                Assert.AreEqual(isLast, CultivationConfig.IsFinal(CultivationConfig.Stages[i].Id),
                    "IsFinal mismatch at row " + i);
                if (isLast) Assert.AreEqual(0f, CultivationConfig.Requirement(CultivationConfig.Stages[i].Id));
                else Assert.Greater(CultivationConfig.Requirement(CultivationConfig.Stages[i].Id), 0f);
            }

            // NextStageId walks the table in order and stops at the final row
            Assert.AreEqual(CultivationConfig.StageFoundationEstablishment,
                CultivationConfig.NextStageId(CultivationConfig.StageQiCondensation));
            Assert.AreEqual(CultivationConfig.StageCoreFormation,
                CultivationConfig.NextStageId(CultivationConfig.StageFoundationEstablishment));
            Assert.AreEqual(CultivationConfig.StageNascentSoul,
                CultivationConfig.NextStageId(CultivationConfig.StageCoreFormation));
            Assert.AreEqual(string.Empty,
                CultivationConfig.NextStageId(CultivationConfig.StageNascentSoul));
        }

        [Test]
        public void StageTable_UnknownOrEmptyId_IsNotKnown_AndHasNoRequirement()
        {
            Assert.IsFalse(CultivationConfig.IsKnownStage(null));
            Assert.IsFalse(CultivationConfig.IsKnownStage(""));
            Assert.IsFalse(CultivationConfig.IsKnownStage("no_such_stage"));
            Assert.IsFalse(CultivationConfig.IsFinal("no_such_stage"), "an unknown id is never final");
            Assert.AreEqual(0f, CultivationConfig.Requirement("no_such_stage"));
            Assert.AreEqual(string.Empty, CultivationConfig.NextStageId("no_such_stage"));
            Assert.AreEqual(string.Empty, CultivationConfig.DisplayName("no_such_stage"));
        }

        [Test]
        public void IsReady_RequiresAKnownNonFinalStageAtItsRequirement()
        {
            float need = CultivationConfig.Requirement(CultivationConfig.StageQiCondensation);

            Assert.IsFalse(CultivationConfig.IsReady(need - 1f, CultivationConfig.StageQiCondensation));
            Assert.IsTrue(CultivationConfig.IsReady(need, CultivationConfig.StageQiCondensation));
            Assert.IsTrue(CultivationConfig.IsReady(need + 1000f, CultivationConfig.StageQiCondensation));

            // final stage is never "ready" (it has no next stage)
            Assert.IsFalse(CultivationConfig.IsReady(999999f, CultivationConfig.StageNascentSoul));
            // unknown id / non-finite progress is never ready
            Assert.IsFalse(CultivationConfig.IsReady(need, "no_such_stage"));
            Assert.IsFalse(CultivationConfig.IsReady(float.NaN, CultivationConfig.StageQiCondensation));
            Assert.IsFalse(CultivationConfig.IsReady(float.PositiveInfinity, CultivationConfig.StageQiCondensation));
        }

        // -------------------------------------------------------- normalization

        [Test]
        public void Normalize_NullOrEmptyStageId_BecomesFirstStage()
        {
            var nullId = new DiscipleAttributes { CultivationStageId = null };
            DiscipleAttributes.Normalize(nullId);
            Assert.AreEqual(CultivationConfig.FirstStageId, nullId.CultivationStageId);

            var emptyId = new DiscipleAttributes { CultivationStageId = string.Empty };
            DiscipleAttributes.Normalize(emptyId);
            Assert.AreEqual(CultivationConfig.FirstStageId, emptyId.CultivationStageId);
        }

        [Test]
        public void Normalize_NonFiniteOrNegativeProgress_BecomesZero()
        {
            var a = new DiscipleAttributes { CultivationStageId = CultivationConfig.StageQiCondensation, CultivationProgress = float.NaN };
            DiscipleAttributes.Normalize(a);
            Assert.AreEqual(0f, a.CultivationProgress);

            a.CultivationProgress = float.PositiveInfinity;
            DiscipleAttributes.Normalize(a);
            Assert.AreEqual(0f, a.CultivationProgress);

            a.CultivationProgress = float.NegativeInfinity;
            DiscipleAttributes.Normalize(a);
            Assert.AreEqual(0f, a.CultivationProgress);

            a.CultivationProgress = -5f;
            DiscipleAttributes.Normalize(a);
            Assert.AreEqual(0f, a.CultivationProgress);
        }

        [Test]
        public void Normalize_ClampsProgressToTheStageRequirement()
        {
            float need = CultivationConfig.Requirement(CultivationConfig.StageFoundationEstablishment);
            var a = new DiscipleAttributes
            {
                CultivationStageId = CultivationConfig.StageFoundationEstablishment,
                CultivationProgress = need + 12345f,
            };

            DiscipleAttributes.Normalize(a);

            Assert.AreEqual(need, a.CultivationProgress, Tol,
                "progress can never exceed the current stage's requirement (no banking)");
        }

        [Test]
        public void Normalize_FinalStage_ProgressIsZero()
        {
            var a = new DiscipleAttributes
            {
                CultivationStageId = CultivationConfig.StageNascentSoul,
                CultivationProgress = 500f,
            };

            DiscipleAttributes.Normalize(a);

            Assert.AreEqual(CultivationConfig.StageNascentSoul, a.CultivationStageId, "final stage id is kept");
            Assert.AreEqual(0f, a.CultivationProgress, "the final stage has no requirement, so it holds no progress");
        }

        [Test]
        public void Normalize_DoesNotRepairANonEmptyUnknownStageId()
        {
            var a = new DiscipleAttributes
            {
                CultivationStageId = "no_such_stage",
                CultivationProgress = 50f,
            };

            DiscipleAttributes.Normalize(a);

            Assert.AreEqual("no_such_stage", a.CultivationStageId,
                "a non-empty unknown id must NOT be silently downgraded to the first stage");
        }

        [Test]
        public void Normalize_PreservesLegitimatelySavedZeroProgress()
        {
            var a = new DiscipleAttributes
            {
                CultivationStageId = CultivationConfig.StageQiCondensation,
                CultivationProgress = 0f,
            };

            DiscipleAttributes.Normalize(a);

            Assert.AreEqual(0f, a.CultivationProgress, "a saved 0 progress is a real value, not 'missing'");
            Assert.AreEqual(CultivationConfig.StageQiCondensation, a.CultivationStageId);
        }

        [Test]
        public void FreshInstance_StartsOnTheFirstStage()
        {
            var a = new DiscipleAttributes();
            Assert.AreEqual(CultivationConfig.FirstStageId, a.CultivationStageId);
            Assert.AreEqual(0f, a.CultivationProgress);
        }

        // -------------------------------------------------------------- accrual

        [Test]
        public void Resting_AccruesProgressFromTheSimulationDelta()
        {
            // every mock founder is on "meditation" at start
            Assert.AreEqual("meditation", Find("d000").CurrentTask);

            float before = Progress("d000");
            _provider.TickGathering(1f);

            Assert.AreEqual(before + CultivationConfig.ProgressPerSecondResting, Progress("d000"), Tol,
                "one simulation second of meditation accrues exactly the configured rate");
        }

        [Test]
        public void NonRestingTasks_AccrueNothing()
        {
            Assert.IsTrue(_provider.TryAssignTask(SectStateProvider.SectMasterRequesterId, "d001",
                "gathering_wood", out var reason), reason);

            float gatheringBefore = Progress("d001");
            float restingBefore = Progress("d002");

            _provider.TickGathering(5f);

            Assert.AreEqual(gatheringBefore, Progress("d001"), Tol, "a gathering disciple accrues no cultivation");
            Assert.AreEqual(restingBefore + 5f * CultivationConfig.ProgressPerSecondResting, Progress("d002"), Tol,
                "a meditating disciple in the same tick still accrues");
        }

        [Test]
        public void CraftingTick_DoesNotAccrueCultivationForAMeditationDisciple()
        {
            // TickCrafting records None for a meditation task; only TickGathering owns Resting.
            float before = Progress("d000");
            _provider.TickCrafting(10f);
            Assert.AreEqual(before, Progress("d000"), Tol,
                "cultivation must be applied exactly once per tick, by the gathering tick only");
        }

        [Test]
        public void Paused_ZeroDelta_AccruesNothing()
        {
            float before = Progress("d000");
            _provider.TickGathering(0f); // SimulationDelta is 0 while paused
            Assert.AreEqual(before, Progress("d000"), Tol, "a paused tick must not advance cultivation");
        }

        [Test]
        public void Accrual_ClampsAtTheRequirement_WithoutOverflowOrCarryOver()
        {
            float need = CultivationConfig.Requirement(CultivationConfig.StageQiCondensation);
            SetCultivation("d000", CultivationConfig.StageQiCondensation, need - 0.5f);

            _provider.TickGathering(1f); // +1.0 → would overshoot by 0.5
            Assert.AreEqual(need, Progress("d000"), Tol, "progress clamps exactly at the requirement");

            _provider.TickGathering(1000f);
            Assert.AreEqual(need, Progress("d000"), Tol, "no carry-over accumulates past the requirement");
            Assert.AreEqual(CultivationConfig.StageQiCondensation, Stage("d000"),
                "accrual never advances the stage on its own (breakthrough is a separate operation)");
        }

        [Test]
        public void FinalStage_AccruesNothing()
        {
            SetCultivation("d000", CultivationConfig.StageNascentSoul, 0f);

            _provider.TickGathering(500f);

            Assert.AreEqual(0f, Progress("d000"), Tol, "the final stage has nothing left to accrue");
            Assert.AreEqual(CultivationConfig.StageNascentSoul, Stage("d000"));
        }

        [Test]
        public void UnknownStageId_AccruesNothing()
        {
            SetCultivation("d000", "no_such_stage", 0f);

            _provider.TickGathering(500f);

            Assert.AreEqual(0f, Progress("d000"), Tol,
                "an unknown stage id must never silently accrue (restore validation rejects it)");
        }

        [Test]
        public void EqualSimulationTime_At1xAnd3x_YieldsEqualProgress()
        {
            // 30 frames @1x of 0.1s = 3.0 simulation seconds
            // 10 frames @3x of 0.1s = 3.0 simulation seconds (game speed applied once, in TimeSystem)
            var provider1x = NewProvider();
            var provider3x = NewProvider();

            float frameDelta = 0.1f;
            float delta1x = TimeSystem.ComputeSimulationDelta(false, 1, frameDelta);
            float delta3x = TimeSystem.ComputeSimulationDelta(false, 3, frameDelta);

            for (int i = 0; i < 30; i++) provider1x.TickGathering(delta1x);
            for (int i = 0; i < 10; i++) provider3x.TickGathering(delta3x);

            float p1x = provider1x.BuildSectEconomyState().Disciples[0].Attributes.CultivationProgress;
            float p3x = provider3x.BuildSectEconomyState().Disciples[0].Attributes.CultivationProgress;

            Assert.AreEqual(3f, p1x, 1e-3f, "3.0 simulation seconds of meditation");
            Assert.AreEqual(p1x, p3x, 1e-3f, "equal simulation time gives equal progress at 1x and 3x");
        }

        [Test]
        public void MeditationStaminaRegeneration_IsUnchangedFromP10B()
        {
            var d000 = Find("d000");
            d000.Attributes.Stamina = 50f;

            _provider.TickGathering(1f);

            Assert.AreEqual(50f + DiscipleAttributesConfig.WorkStaminaRegenPerSecondResting,
                d000.Attributes.Stamina, Tol,
                "P14A adds cultivation accrual WITHOUT altering the P10B stamina rate");
            Assert.AreEqual(CultivationConfig.ProgressPerSecondResting, Progress("d000"), Tol,
                "the same tick both regenerates stamina and accrues cultivation");
        }

        // -------------------------------------------- rank vs stage independence

        [Test]
        public void RankAndCultivationStage_AreIndependent()
        {
            // structurally: four founders with four different ranks, all on the same stage
            string firstStage = CultivationConfig.FirstStageId;
            var ranks = new HashSet<DiscipleRank>();
            foreach (var d in State.Disciples)
            {
                ranks.Add(d.Rank);
                Assert.AreEqual(firstStage, d.Attributes.CultivationStageId,
                    "stage must not be derived from rank (" + d.DiscipleId + ")");
            }
            Assert.Greater(ranks.Count, 1, "the mock roster must span more than one rank for this to mean anything");

            // mutating rank does not move the stage
            var d000 = Find("d000");
            d000.Attributes.CultivationProgress = 10f;
            d000.Rank = DiscipleRank.OuterDisciple;
            Assert.AreEqual(firstStage, d000.Attributes.CultivationStageId);
            Assert.AreEqual(10f, d000.Attributes.CultivationProgress, Tol);

            // accrual + normalize do not touch rank
            _provider.TickGathering(30f);
            DiscipleAttributes.Normalize(d000.Attributes);
            Assert.AreEqual(DiscipleRank.OuterDisciple, d000.Rank, "cultivation never rewrites rank");
        }

        // ------------------------------------------------------- construction

        [Test]
        public void MockFounders_AreBornWithAValidFirstStage()
        {
            Assert.AreEqual(4, State.Disciples.Count);
            foreach (var d in State.Disciples)
            {
                Assert.IsTrue(CultivationConfig.IsKnownStage(d.Attributes.CultivationStageId),
                    "founder " + d.DiscipleId + " has an unknown stage id");
                Assert.AreEqual(CultivationConfig.FirstStageId, d.Attributes.CultivationStageId);
                Assert.AreEqual(0f, d.Attributes.CultivationProgress, "no invented balance values for founders");
                Assert.IsFalse(CultivationConfig.IsReady(d.Attributes.CultivationProgress, d.Attributes.CultivationStageId));
            }
        }

        [Test]
        public void RecruitedDisciple_IsBornWithAValidFirstStage()
        {
            int before = State.Disciples.Count;
            _provider.RecruitOuterDisciple(DiscipleSex.Male);

            Assert.AreEqual(before + 1, State.Disciples.Count);
            var recruit = State.Disciples[State.Disciples.Count - 1];
            Assert.IsTrue(CultivationConfig.IsKnownStage(recruit.Attributes.CultivationStageId));
            Assert.AreEqual(CultivationConfig.FirstStageId, recruit.Attributes.CultivationStageId);
            Assert.AreEqual(0f, recruit.Attributes.CultivationProgress);
            Assert.AreEqual(DiscipleRank.OuterDisciple, recruit.Rank);
        }

        // -------------------------------------------------------------- save

        [Test]
        public void MessagePack_RoundTrip_PreservesCultivation()
        {
            var a = new DiscipleAttributes
            {
                CultivationStageId = CultivationConfig.StageFoundationEstablishment,
                CultivationProgress = 512.5f,
            };

            var restored = MessagePackSerializer.Deserialize<DiscipleAttributes>(
                MessagePackSerializer.Serialize(a));

            Assert.AreEqual(CultivationConfig.StageFoundationEstablishment, restored.CultivationStageId);
            Assert.AreEqual(512.5f, restored.CultivationProgress, Tol);
        }

        [Test]
        public void LegacyPayload_WithoutCultivationKeys_DefaultsToFirstStageAtZero()
        {
            var legacy = new LegacyAttributesWithoutCultivation
            {
                Stamina = 55f,
                SkillXp = new Dictionary<string, float> { [DiscipleAttributesConfig.CategoryGathering] = 12f },
            };

            var restored = MessagePackSerializer.Deserialize<DiscipleAttributes>(
                MessagePackSerializer.Serialize(legacy));
            DiscipleAttributes.Normalize(restored);

            Assert.AreEqual(55f, restored.Stamina, "existing fields must survive an old payload");
            Assert.AreEqual(CultivationConfig.FirstStageId, restored.CultivationStageId,
                "a payload written before the key existed loads on the first stage");
            Assert.AreEqual(0f, restored.CultivationProgress);
        }

        [Test]
        public void EconomyLoad_LegacyRosterWithoutCultivationKeys_IsRepaired()
        {
            var state = new SectEconomyState();
            state.Disciples.Add(new DiscipleState
            {
                DiscipleId = "d_legacy",
                // a pre-P14A attribute block: no stage id at all
                Attributes = new DiscipleAttributes { CultivationStageId = null, CultivationProgress = 0f },
            });

            var restored = SectEconomyState.FromByteArray(state.ToByteArray());

            var d = restored.Disciples[0];
            Assert.IsNotNull(d.Attributes);
            Assert.AreEqual(CultivationConfig.FirstStageId, d.Attributes.CultivationStageId);
            Assert.AreEqual(0f, d.Attributes.CultivationProgress);
        }

        [Test]
        public void RestoreValidation_RejectsANonEmptyUnknownStageId()
        {
            var good = _provider.CaptureSessionSnapshot();
            Assert.IsTrue(_provider.TryValidateSessionSnapshot(good, out var okReason), okReason);

            var corrupt = _provider.CaptureSessionSnapshot();
            corrupt.Economy.Disciples[0].Attributes.CultivationStageId = "no_such_stage";

            Assert.IsFalse(_provider.TryValidateSessionSnapshot(corrupt, out var failReason),
                "an unknown non-empty stage id must be rejected, never silently downgraded");
            StringAssert.Contains("cultivation stage", failReason);
            StringAssert.Contains("no_such_stage", failReason);
        }

        [Test]
        public void RestoreValidation_RejectsNonFiniteCultivationProgress()
        {
            var corrupt = _provider.CaptureSessionSnapshot();
            corrupt.Economy.Disciples[0].Attributes.CultivationProgress = float.NaN;

            Assert.IsFalse(_provider.TryValidateSessionSnapshot(corrupt, out var failReason));
            StringAssert.Contains("cultivation progress", failReason);
        }

        [Test]
        public void SnapshotRoundTrip_PreservesCultivation()
        {
            SetCultivation("d002", CultivationConfig.StageCoreFormation, 1234.5f);

            var snapshot = _provider.CaptureSessionSnapshot();
            var next = NewProvider();
            Assert.IsTrue(next.TryValidateSessionSnapshot(snapshot, out var reason), reason);
            Assert.IsTrue(next.TryApplySessionSnapshot(snapshot));

            var d = next.BuildSectEconomyState().Disciples[2];
            Assert.AreEqual(CultivationConfig.StageCoreFormation, d.Attributes.CultivationStageId);
            Assert.AreEqual(1234.5f, d.Attributes.CultivationProgress, Tol);
        }

        // ---------------------------------------------------------- deep copy

        [Test]
        public void Clone_DoesNotShareCultivationState()
        {
            var original = new DiscipleAttributes
            {
                CultivationStageId = CultivationConfig.StageQiCondensation,
                CultivationProgress = 100f,
            };

            var clone = original.Clone();

            Assert.AreEqual(100f, clone.CultivationProgress, Tol);
            Assert.AreEqual(CultivationConfig.StageQiCondensation, clone.CultivationStageId);

            clone.CultivationProgress = 999f;
            clone.CultivationStageId = CultivationConfig.StageNascentSoul;

            Assert.AreEqual(100f, original.CultivationProgress, Tol, "the clone must not alias the source");
            Assert.AreEqual(CultivationConfig.StageQiCondensation, original.CultivationStageId);
        }

        [Test]
        public void Snapshot_IsDetachedFromLiveCultivationState()
        {
            SetCultivation("d000", CultivationConfig.StageQiCondensation, 10f);

            var snapshot = _provider.CaptureSessionSnapshot();
            snapshot.Economy.Disciples[0].Attributes.CultivationProgress = 999f;
            snapshot.Economy.Disciples[0].Attributes.CultivationStageId = CultivationConfig.StageNascentSoul;

            Assert.AreEqual(10f, Progress("d000"), Tol, "mutating the snapshot must not touch live state");
            Assert.AreEqual(CultivationConfig.StageQiCondensation, Stage("d000"));
        }

        // ------------------------------------------------------------- helpers

        private SectStateProvider NewProvider()
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

        /// <summary>
        /// Test-local publisher (no MessagePipe registration needed) — the same helper the
        /// other P10 suites declare.
        /// </summary>
        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public readonly List<T> Messages = new List<T>();
            public void Publish(T message) => Messages.Add(message);
        }

        /// <summary>
        /// A pre-P14A attribute payload: only the keys that existed before cultivation was
        /// added. Serializing this and reading it back as <see cref="DiscipleAttributes"/>
        /// is the legacy-save scenario (the new keys are simply absent).
        /// </summary>
        [MessagePackObject]
        internal sealed class LegacyAttributesWithoutCultivation
        {
            [Key(0)] public float Stamina { get; set; }
            [Key(1)] public Dictionary<string, float> SkillXp { get; set; }
        }
    }
}
