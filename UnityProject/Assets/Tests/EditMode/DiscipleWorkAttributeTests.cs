using System.Collections.Generic;
using MessagePipe;
using NUnit.Framework;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// P10B — attributes change FROM real work; they never feed BACK into anything.
    ///
    /// Covered here:
    ///   - the exact linear rate per outcome (productive gathering / crafting,
    ///     resting, blocked), against the tick's simulation delta;
    ///   - delta 0 (pause) changes nothing, and the same real delta at 3x changes 3x;
    ///   - exactly one outcome per disciple per tick, and no change applied twice
    ///     across the two ticks of one frame;
    ///   - craft XP only on completion and only for the matching category;
    ///   - clamps (stamina 0..100, XP cap);
    ///   - an unknown task changes nothing;
    ///   - REGRESSION: resource amounts, fractional accumulators, craft progress and
    ///     building gating behave exactly as before, now that attributes exist.
    ///
    /// Same provider construction / BufferPublisher pattern as MockStartStateTests.
    /// </summary>
    public class DiscipleWorkAttributeTests
    {
        private SectStateProvider _provider;

        /// <summary>Rate tolerances: all values are derived from the config constants, so
        /// the comparison is float-exact apart from accumulation order.</summary>
        private const float Tol = 1e-4f;

        private const string Gathering = DiscipleAttributesConfig.CategoryGathering;
        private const string Alchemy = DiscipleAttributesConfig.CategoryAlchemy;
        private const string Forging = DiscipleAttributesConfig.CategoryForging;

        [SetUp]
        public void SetUp()
        {
            _provider = new SectStateProvider(
                new BufferPublisher<DiscipleRecruitedMessage>(),
                new BufferPublisher<SectResourceChangedMessage>(),
                new BufferPublisher<AvatarEquipmentChangedMessage>(),
                new BufferPublisher<DiscipleChibiBackendChangedMessage>(),
                new AvatarPartPool(),
                Visual.VisualRuntimeConfig.Instance,
                new Visual.DefaultEntitlementProvider(),
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

        private float Stamina(string id) => Find(id).Attributes.Stamina;

        private float Xp(string id, string category) =>
            DiscipleAttributes.GetSkillXp(Find(id).Attributes, category);

        private void SetStamina(string id, float value) => Find(id).Attributes.Stamina = value;

        private void SetTask(string id, string task) => Find(id).CurrentTask = task;

        private int Raw(string resourceId)
        {
            int v;
            return State.Stockpile.RawResources.TryGetValue(resourceId, out v) ? v : 0;
        }

        /// <summary>Total quantity of a crafted item. AddToStockpileGoods MERGES a stack
        /// with the same def id + grade + owner scope, so counting entries would miss a
        /// craft entirely.</summary>
        private int GoodsQuantity(string itemDefId)
        {
            int total = 0;
            foreach (var g in State.Stockpile.CraftedGoods)
                if (g.ItemDefId == itemDefId) total += g.Quantity;
            return total;
        }

        private PlacedBuildingState Place(string defId)
        {
            string reason;
            PlacedBuildingState placed;
            Assert.IsTrue(_provider.TryPlaceBuilding(defId, 0, 0, 0, new BuildingGrid(10, 10),
                                                     out reason, out placed), reason);
            return placed;
        }

        private bool OutcomeOf(string id, out DiscipleWorkOutcome outcome) =>
            _provider.TryGetLastWorkOutcome(id, out outcome);

        // ---------------------------------------------------------------- productive rates

        [Test]
        public void ProductiveGathering_DrainsStaminaAndAccruesGatheringXp_AtTheExactRate()
        {
            Place("herb_plot");
            SetTask("d001", "gathering_herb");

            float xpBefore = Xp("d001", Gathering);
            float alchemyBefore = Xp("d001", Alchemy);

            _provider.TickGathering(2f);

            Assert.AreEqual(100f - DiscipleAttributesConfig.WorkStaminaDrainPerSecondGathering * 2f,
                            Stamina("d001"), Tol, "productive gathering drains at exactly rate * delta");
            Assert.AreEqual(xpBefore + DiscipleAttributesConfig.WorkGatheringXpPerSecond * 2f,
                            Xp("d001", Gathering), Tol, "gathering XP accrues at exactly rate * delta");
            Assert.AreEqual(alchemyBefore, Xp("d001", Alchemy), "no other category gains XP");

            DiscipleWorkOutcome outcome;
            Assert.IsTrue(OutcomeOf("d001", out outcome));
            Assert.AreEqual(DiscipleWorkOutcome.ProductiveGathering, outcome);
        }

        [Test]
        public void ProductiveCrafting_DrainsStaminaAtTheExactRate_AndAwardsNoXpBeforeCompletion()
        {
            Place("pill_hall");
            SetTask("d002", "refining_elixir");
            float alchemyBefore = Xp("d002", Alchemy);

            _provider.TickCrafting(1f); // 1s of the 20s craft — advanced, not complete

            Assert.AreEqual(100f - DiscipleAttributesConfig.WorkStaminaDrainPerSecondCrafting,
                            Stamina("d002"), Tol, "productive crafting drains at exactly rate * delta");
            Assert.AreEqual(alchemyBefore, Xp("d002", Alchemy), "no craft XP before the craft completes");

            DiscipleWorkOutcome outcome;
            Assert.IsTrue(OutcomeOf("d002", out outcome));
            Assert.AreEqual(DiscipleWorkOutcome.ProductiveCrafting, outcome);
        }

        [Test]
        public void Resting_RecoversStaminaAtTheExactRate_AndGrantsNoSkillXp()
        {
            SetStamina("d000", 50f); // meditation; below the cap so regen is observable
            float gatheringBefore = Xp("d000", Gathering);

            _provider.TickGathering(1f);

            Assert.AreEqual(50f + DiscipleAttributesConfig.WorkStaminaRegenPerSecondResting,
                            Stamina("d000"), Tol, "meditation recovers at exactly rate * delta");
            Assert.AreEqual(gatheringBefore, Xp("d000", Gathering), "meditation grants no skill XP");

            DiscipleWorkOutcome outcome;
            Assert.IsTrue(OutcomeOf("d000", out outcome));
            Assert.AreEqual(DiscipleWorkOutcome.Resting, outcome);
        }

        [Test]
        public void BlockedByMissingBuilding_RegeneratesSlowly_AndProducesNothing()
        {
            SetTask("d001", "gathering_herb"); // no herb_plot in the starter state
            SetStamina("d001", 50f);
            int herbBefore = Raw("herb");

            _provider.TickGathering(1f);

            Assert.AreEqual(50f + DiscipleAttributesConfig.WorkStaminaRegenPerSecondBlocked,
                            Stamina("d001"), Tol, "a gated tick regenerates at the blocked rate");
            Assert.AreEqual(herbBefore, Raw("herb"), "the gate still produces nothing");
            Assert.AreEqual("gathering_herb", Find("d001").CurrentTask, "the gate never rewrites the task");

            DiscipleWorkOutcome outcome;
            Assert.IsTrue(OutcomeOf("d001", out outcome));
            Assert.AreEqual(DiscipleWorkOutcome.Blocked, outcome);
        }

        [Test]
        public void BlockedOnMissingMaterials_RegeneratesSlowly_AwardsNoXp_AndHoldsProgress()
        {
            Place("pill_hall");
            SetTask("d002", "refining_elixir");
            SetStamina("d002", 50f);
            State.Stockpile.RawResources["herb"] = 0; // can't pay the 10-herb cost
            float alchemyBefore = Xp("d002", Alchemy);
            int goodsBefore = GoodsQuantity("elixir_qi_gathering");

            const float heldDelta = 25f;
            _provider.TickCrafting(heldDelta); // past the 20s craft, but held for materials

            Assert.AreEqual(50f + DiscipleAttributesConfig.WorkStaminaRegenPerSecondBlocked * heldDelta,
                            Stamina("d002"), Tol, "a materials-held tick regenerates at the blocked rate * delta");
            Assert.AreEqual(alchemyBefore, Xp("d002", Alchemy), "a rejected craft awards no XP");
            Assert.AreEqual(goodsBefore, GoodsQuantity("elixir_qi_gathering"), "nothing was produced");
            Assert.AreEqual(0, Raw("herb"), "nothing was consumed");

            DiscipleWorkOutcome outcome;
            Assert.IsTrue(OutcomeOf("d002", out outcome));
            Assert.AreEqual(DiscipleWorkOutcome.Blocked, outcome);

            // Held progress (20s) survives: one second of materials is enough to finish.
            State.Stockpile.RawResources["herb"] = 10;
            _provider.TickCrafting(1f);
            Assert.AreEqual(goodsBefore + 1, GoodsQuantity("elixir_qi_gathering"),
                            "held progress was not reset by the blocked tick");
        }

        // ---------------------------------------------------------------- craft XP

        [Test]
        public void CraftCompletion_AwardsXpOnce_ToTheMatchingCategoryOnly()
        {
            Place("pill_hall");
            SetTask("d002", "refining_elixir");
            float alchemyBefore = Xp("d002", Alchemy);
            float gatheringBefore = Xp("d002", Gathering);
            float forgingBefore = Xp("d002", Forging);

            _provider.TickCrafting(20f); // exactly one craft

            Assert.AreEqual(alchemyBefore + DiscipleAttributesConfig.WorkCraftXpPerCompletion,
                            Xp("d002", Alchemy), Tol, "refining_elixir → alchemy, once per completed craft");
            Assert.AreEqual(gatheringBefore, Xp("d002", Gathering), "the wrong category must not gain XP");
            Assert.AreEqual(forgingBefore, Xp("d002", Forging), "the wrong category must not gain XP");

            // A completion tick counts as Productive (stated simplification).
            DiscipleWorkOutcome outcome;
            Assert.IsTrue(OutcomeOf("d002", out outcome));
            Assert.AreEqual(DiscipleWorkOutcome.ProductiveCrafting, outcome);

            // Second craft → exactly one more award (never a lump).
            _provider.TickCrafting(20f);
            Assert.AreEqual(alchemyBefore + 2f * DiscipleAttributesConfig.WorkCraftXpPerCompletion,
                            Xp("d002", Alchemy), Tol, "one award per completed craft");
        }

        [Test]
        public void ForgingCompletion_AwardsTheForgingCategory()
        {
            Place("forge");
            SetTask("d003", "forging_artifact");
            float forgingBefore = Xp("d003", Forging);
            float alchemyBefore = Xp("d003", Alchemy);

            _provider.TickCrafting(30f); // one 30s forge

            Assert.AreEqual(forgingBefore + DiscipleAttributesConfig.WorkCraftXpPerCompletion,
                            Xp("d003", Forging), Tol, "forging_artifact → forging");
            Assert.AreEqual(alchemyBefore, Xp("d003", Alchemy), "the wrong category must not gain XP");
        }

        // ---------------------------------------------------------------- time / pause / speed

        [Test]
        public void ZeroDelta_ChangesNothing()
        {
            Place("herb_plot");
            SetTask("d001", "gathering_herb");
            SetStamina("d001", 50f);
            float staminaBefore = Stamina("d001");
            float xpBefore = Xp("d001", Gathering);

            _provider.TickGathering(0f);   // paused
            _provider.TickCrafting(0f);    // paused

            Assert.AreEqual(staminaBefore, Stamina("d001"), "a paused tick drains no stamina");
            Assert.AreEqual(xpBefore, Xp("d001", Gathering), "a paused tick gains no XP");

            // d000 on meditation is likewise untouched (regen must not tick while paused).
            SetStamina("d000", 50f);
            _provider.TickGathering(0f);
            Assert.AreEqual(50f, Stamina("d000"), "a paused tick regenerates nothing");
        }

        [Test]
        public void SameRealDeltaAtSpeed3_ChangesThreeTimesAsMuch()
        {
            Place("herb_plot");
            SetTask("d001", "gathering_herb");
            float xpBefore = Xp("d001", Gathering);
            const float realDeltaSeconds = 1f;

            // Exactly the delta DiscipleSystem would pass at 3x (P10: one clock, speed applied once).
            float delta3x = TimeSystem.ComputeSimulationDelta(false, 3, realDeltaSeconds);

            _provider.TickGathering(delta3x);

            Assert.AreEqual(100f - DiscipleAttributesConfig.WorkStaminaDrainPerSecondGathering * 3f,
                            Stamina("d001"), Tol, "3x the simulation delta drains 3x the stamina");
            Assert.AreEqual(xpBefore + DiscipleAttributesConfig.WorkGatheringXpPerSecond * 3f,
                            Xp("d001", Gathering), Tol, "3x the simulation delta accrues 3x the XP");
        }

        // ---------------------------------------------------------------- one outcome per tick

        [Test]
        public void EveryTick_RecordsExactlyOneOutcomePerDisciple()
        {
            int roster = State.Disciples.Count;
            Assert.Greater(roster, 0);

            _provider.TickGathering(1f);
            Assert.AreEqual(roster, _provider.LastTickOutcomeRecordCountForTest,
                            "gathering tick: exactly one record per disciple");

            foreach (var d in State.Disciples)
            {
                DiscipleWorkOutcome outcome;
                Assert.IsTrue(_provider.TryGetLastWorkOutcome(d.DiscipleId, out outcome),
                              d.DiscipleId + " must have an outcome in the gathering tick");
            }

            _provider.TickCrafting(1f);
            Assert.AreEqual(roster, _provider.LastTickOutcomeRecordCountForTest,
                            "crafting tick: exactly one record per disciple");
        }

        [Test]
        public void BothTicksOfOneFrame_ApplyEachChangeExactlyOnce()
        {
            Place("herb_plot");
            Place("pill_hall");
            SetTask("d000", "meditation");
            SetTask("d001", "gathering_herb");
            SetTask("d002", "refining_elixir");
            SetTask("d003", "not_a_real_task");

            SetStamina("d000", 50f);
            SetStamina("d003", 50f);

            // One frame = both systems' ticks, same simulation delta.
            _provider.TickGathering(1f);
            _provider.TickCrafting(1f);

            Assert.AreEqual(50f + DiscipleAttributesConfig.WorkStaminaRegenPerSecondResting, Stamina("d000"), Tol,
                            "resting regen must not be applied by both ticks");
            Assert.AreEqual(100f - DiscipleAttributesConfig.WorkStaminaDrainPerSecondGathering, Stamina("d001"), Tol,
                            "gathering drain must not be applied by both ticks");
            Assert.AreEqual(100f - DiscipleAttributesConfig.WorkStaminaDrainPerSecondCrafting, Stamina("d002"), Tol,
                            "crafting drain must not be applied by both ticks");
            Assert.AreEqual(50f, Stamina("d003"), Tol,
                            "an unknown task changes nothing in either tick");
        }

        // ---------------------------------------------------------------- unknown / clamps

        [Test]
        public void UnknownTask_ChangesNothing_AndRecordsNone()
        {
            SetTask("d001", "not_a_real_task");
            SetStamina("d001", 50f);
            float gatheringBefore = Xp("d001", Gathering);

            _provider.TickGathering(10f);
            _provider.TickCrafting(10f);

            Assert.AreEqual(50f, Stamina("d001"), "an unknown task must not change stamina");
            Assert.AreEqual(gatheringBefore, Xp("d001", Gathering), "an unknown task must not grant XP");

            DiscipleWorkOutcome outcome;
            Assert.IsTrue(OutcomeOf("d001", out outcome));
            Assert.AreEqual(DiscipleWorkOutcome.None, outcome);
        }

        [Test]
        public void EmptyTask_ChangesNothing_AndDoesNotThrow()
        {
            SetTask("d001", "");
            SetStamina("d001", 50f);

            Assert.DoesNotThrow(() => _provider.TickGathering(1f));
            Assert.DoesNotThrow(() => _provider.TickCrafting(1f));

            Assert.AreEqual(50f, Stamina("d001"));
        }

        [Test]
        public void Clamps_StaminaAtZeroAndHundred_XpAtTheCap()
        {
            Place("herb_plot");
            SetTask("d001", "gathering_herb");

            SetStamina("d001", 0.05f);
            _provider.TickGathering(10f); // would go far below zero
            Assert.AreEqual(DiscipleAttributesConfig.StaminaMin, Stamina("d001"), "stamina floors at 0");

            // The ceiling needs a RECOVERY path (gathering only drains): d000 meditates.
            SetStamina("d000", 99.99f);
            _provider.TickGathering(10f); // would overshoot the ceiling
            Assert.AreEqual(DiscipleAttributesConfig.StaminaMax, Stamina("d000"), "stamina caps at 100");

            // Reaching 0 has NO consequence in P10B: no stop, no reassignment.
            SetStamina("d001", 0f);
            _provider.TickGathering(1f);
            Assert.AreEqual(0f, Stamina("d001"));
            Assert.AreEqual("gathering_herb", Find("d001").CurrentTask, "exhaustion never reassigns the task");

            // XP cap: one more completion than the cap allows stays at the cap.
            Place("pill_hall");
            SetTask("d002", "refining_elixir");
            Find("d002").Attributes.SkillXp[Alchemy] = DiscipleAttributesConfig.SkillXpMax;
            _provider.TickCrafting(20f);
            Assert.AreEqual(DiscipleAttributesConfig.SkillXpMax, Xp("d002", Alchemy), "XP caps at the configured max");
        }

        // ---------------------------------------------------------------- regression

        /// <summary>
        /// P10B must not change a single production number. These are the same
        /// expectations MockStartStateTests asserts, run again with attributes present
        /// and being drained by the very same ticks.
        /// </summary>
        [Test]
        public void Regression_GatheringNumbersAndAccumulatorAreUnchanged()
        {
            Assert.AreEqual("meditation", Find("d001").CurrentTask, "start state: d001 holds no gathering task");
            Place("herb_plot");
            SetTask("d001", "gathering_herb");

            int herbBefore = Raw("herb");

            _provider.TickGathering(10f); // 0.2/s * 10s = 2.0
            Assert.AreEqual(herbBefore + 2, Raw("herb"), "production is unchanged: 10s yields 2 herb");

            _provider.TickGathering(1.25f); // holds a 0.25 remainder
            Assert.AreEqual(herbBefore + 2, Raw("herb"), "fractional tick still produces nothing");

            _provider.TickGathering(1f);
            Assert.AreEqual(herbBefore + 2, Raw("herb"), "remainder still under 1");

            _provider.TickGathering(3f);
            Assert.AreEqual(herbBefore + 3, Raw("herb"), "remainder still crosses 1 exactly as before");

            // ...and the work outcomes were still recorded on every one of those ticks.
            Assert.AreEqual(State.Disciples.Count, _provider.LastTickOutcomeRecordCountForTest,
                            "attribute bookkeeping does not disturb production ticks");
        }

        [Test]
        public void Regression_GateFailureProducesNothingAndHoldsTheRemainder()
        {
            Place("herb_plot");
            SetTask("d001", "gathering_herb");
            int herbBefore = Raw("herb");

            _provider.TickGathering(1.25f); // 0.25 remainder
            Assert.AreEqual(herbBefore, Raw("herb"));

            var herbPlot = State.PlacedBuildings[0];
            State.PlacedBuildings.Remove(herbPlot);

            _provider.TickGathering(10f);
            _provider.TickGathering(100f);
            Assert.AreEqual(herbBefore, Raw("herb"), "gated ticks still produce nothing");

            State.PlacedBuildings.Add(herbPlot);
            _provider.TickGathering(3.75f); // 0.25 + 0.75 = 1.0
            Assert.AreEqual(herbBefore + 1, Raw("herb"), "the held remainder still survives the blocked window");
        }

        [Test]
        public void Regression_CraftProgressCostAndGatingAreUnchanged()
        {
            Place("pill_hall");
            SetTask("d002", "refining_elixir");
            int herbBefore = Raw("herb");
            int goodsBefore = GoodsQuantity("elixir_qi_gathering");

            _provider.TickCrafting(15f); // 15 of 20s — held
            Assert.AreEqual(herbBefore, Raw("herb"), "sub-craftTime tick consumes nothing");
            Assert.AreEqual(goodsBefore, GoodsQuantity("elixir_qi_gathering"), "sub-craftTime tick produces nothing");

            _provider.TickCrafting(4f); // 19s — still held
            Assert.AreEqual(herbBefore, Raw("herb"));

            _provider.TickCrafting(1f); // 20s exactly → one craft
            Assert.AreEqual(goodsBefore + 1, GoodsQuantity("elixir_qi_gathering"), "exactly one craft at 20s");
            Assert.AreEqual(herbBefore - 10, Raw("herb"), "the 10-herb cost is unchanged");
            Assert.AreEqual("refining_elixir", Find("d002").CurrentTask, "completion never rewrites the task");

            // Gated crafting: the same task with the building gone must produce nothing.
            State.PlacedBuildings.Clear();
            int goodsAfter = GoodsQuantity("elixir_qi_gathering");
            int herbAfter = Raw("herb");
            _provider.TickCrafting(9999f);
            Assert.AreEqual(goodsAfter, GoodsQuantity("elixir_qi_gathering"), "gated crafting still produces nothing");
            Assert.AreEqual(herbAfter, Raw("herb"), "gated crafting still consumes nothing");
        }

        [Test]
        public void AttributesDoNotAffectTaskAssignmentOrAvailability()
        {
            // P10B writes attributes only: assignment/availability must not read them.
            Place("herb_plot");
            SetStamina("d001", 0f);
            Find("d001").Attributes.SkillXp[Gathering] = DiscipleAttributesConfig.SkillXpMax;

            string reason;
            Assert.IsTrue(_provider.IsTaskAvailable("gathering_herb", out reason), reason);
            Assert.IsTrue(_provider.TryAssignTask(SectStateProvider.SectMasterRequesterId, "d001", "gathering_herb", out reason), reason);
            Assert.AreEqual("gathering_herb", Find("d001").CurrentTask);
        }

        // ---------------------------------------------------------------- plumbing

        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public readonly List<T> Messages = new List<T>();
            public void Publish(T message) => Messages.Add(message);
        }
    }
}
