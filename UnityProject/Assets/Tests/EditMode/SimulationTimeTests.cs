using System.Collections.Generic;
using MessagePipe;
using NUnit.Framework;
using Xianxia.Sect;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// P10 — the single simulation-time source (<see cref="TimeSystem.SimulationDelta"/>).
    ///
    /// Contract under test:
    ///   - pause freezes ALL gameplay progression (gathering, crafting, auto evaluation,
    ///     world-event cadence) and never loses or jumps held progress on resume;
    ///   - game speed is applied exactly once, inside TimeSystem, and is clamped to
    ///     1..3 so an out-of-range/never-set speed can never freeze or explode it;
    ///   - every gameplay tick consumes the SAME delta.
    ///
    /// Determinism note: SimulationDelta reads UnityEngine.Time.deltaTime, which an
    /// EditMode test cannot set. So the multiplier is asserted on the pure core
    /// (<see cref="TimeSystem.ComputeSimulationDelta"/>) and the systems are driven
    /// through their existing explicit-delta seams (TickGathering/TickCrafting/Advance)
    /// with the value that core produces. Pause paths are driven through the real
    /// Tick() calls, which are deterministic because a paused delta is exactly 0.
    /// </summary>
    public class SimulationTimeTests
    {
        private const float GatherPerSecond = 0.2f;   // gathering_herb rate (SectStateProvider)
        private const float CraftSeconds = 20f;       // refining_elixir recipe
        private const int CraftCostHerb = 10;

        private SectStateProvider _provider;
        private TimeSystem _timeSystem;
        private DiscipleSystem _discipleSystem;
        private ResourceCraftingSystem _craftingSystem;
        private BufferPublisher<SectResourceChangedMessage> _resourceBuffer;
        private BufferPublisher<WorldEventTriggeredMessage> _worldEvents;

        [SetUp]
        public void SetUp()
        {
            _worldEvents = new BufferPublisher<WorldEventTriggeredMessage>();
            _timeSystem = new TimeSystem(new BufferPublisher<TimeSpeedChangedMessage>(), _worldEvents);

            SectStateProvider provider;
            _provider = provider = NewProvider(out _resourceBuffer);
            _discipleSystem = new DiscipleSystem(provider, _timeSystem);
            _craftingSystem = new ResourceCraftingSystem(provider, _timeSystem);
        }

        // ---------------------------------------------------------------- helpers

        private static SectStateProvider NewProvider(out BufferPublisher<SectResourceChangedMessage> resources)
        {
            resources = new BufferPublisher<SectResourceChangedMessage>();
            var provider = new SectStateProvider(
                new BufferPublisher<DiscipleRecruitedMessage>(),
                resources,
                new BufferPublisher<AvatarEquipmentChangedMessage>(),
                new BufferPublisher<DiscipleChibiBackendChangedMessage>(),
                new AvatarPartPool(),
                VisualRuntimeConfig.Instance,
                new DefaultEntitlementProvider(),
                new BuildingDefPool(),
                new BufferPublisher<BuildingPlacedMessage>(),
                new BufferPublisher<DiscipleTaskChangedMessage>());
            provider.TaskChangeCooldownSeconds = 0f;
            return provider;
        }

        private static DiscipleState FindIn(SectStateProvider provider, string id)
        {
            var disciples = provider.BuildSectEconomyState().Disciples;
            for (int i = 0; i < disciples.Count; i++)
            {
                if (disciples[i].DiscipleId == id) return disciples[i];
            }
            return null;
        }

        private static int RawIn(SectStateProvider provider, string id)
        {
            int value;
            return provider.BuildSectEconomyState().Stockpile.RawResources.TryGetValue(id, out value) ? value : 0;
        }

        private static void SetStockIn(SectStateProvider provider, string id, int amount)
        {
            provider.BuildSectEconomyState().Stockpile.RawResources[id] = amount;
        }

        /// <summary>Place the building d001's gathering task requires, then switch it on.</summary>
        private static PlacedBuildingState GiveHerbPlot(SectStateProvider provider)
        {
            string reason;
            PlacedBuildingState placed;
            Assert.IsTrue(provider.TryPlaceBuilding("herb_plot", 0, 0, 0, new BuildingGrid(10, 10),
                                                    out reason, out placed), reason);
            FindIn(provider, "d001").CurrentTask = "gathering_herb";
            return placed;
        }

        /// <summary>Place the building refining_elixir requires, then switch d001 on.</summary>
        private static PlacedBuildingState GivePillHall(SectStateProvider provider)
        {
            string reason;
            PlacedBuildingState placed;
            Assert.IsTrue(provider.TryPlaceBuilding("pill_hall", 0, 0, 0, new BuildingGrid(10, 10),
                                                    out reason, out placed), reason);
            FindIn(provider, "d001").CurrentTask = "refining_elixir";
            return placed;
        }

        private SectEconomyState State => _provider.BuildSectEconomyState();
        private DiscipleState Find(string id) => FindIn(_provider, id);
        private int Raw(string id) => RawIn(_provider, id);

        /// <summary>The delta TimeSystem hands a consumer for one real (un-scaled) delta.</summary>
        private static float SimDelta(int speed, float realDeltaSeconds)
        {
            return TimeSystem.ComputeSimulationDelta(false, speed, realDeltaSeconds);
        }

        // ------------------------------------------- the multiplier (pure core)

        [Test]
        public void SimulationDelta_Speed_IsClampedTo1Through3_AndNeverZeroOrUnbounded()
        {
            const float frame = 0.5f;

            // default 1x (TimeSystem._speed is initialised to 1)
            Assert.AreEqual(0.5f, TimeSystem.ComputeSimulationDelta(false, 1, frame), 1e-6f, "1x = raw frame delta");
            Assert.AreEqual(1.5f, TimeSystem.ComputeSimulationDelta(false, 3, frame), 1e-6f, "3x = triple");

            // out of range clamps instead of freezing (0) or exploding (unbounded)
            Assert.AreEqual(0.5f, TimeSystem.ComputeSimulationDelta(false, 0, frame), 1e-6f,
                            "speed 0 clamps up to 1x — a bad/never-set speed must not silently freeze the game");
            Assert.AreEqual(0.5f, TimeSystem.ComputeSimulationDelta(false, -7, frame), 1e-6f,
                            "negative speed clamps up to 1x");
            Assert.AreEqual(1.5f, TimeSystem.ComputeSimulationDelta(false, 99, frame), 1e-6f,
                            "speed 99 clamps down to 3x — the delta can never grow without bound");
            Assert.Greater(TimeSystem.ComputeSimulationDelta(false, 1, frame), 0f,
                           "an unpaused delta is never zero");

            // paused is exactly zero, at every speed
            Assert.AreEqual(0f, TimeSystem.ComputeSimulationDelta(true, 1, frame), "paused = 0 (1x)");
            Assert.AreEqual(0f, TimeSystem.ComputeSimulationDelta(true, 3, frame), "paused = 0 (3x)");
        }

        [Test]
        public void SimulationDelta_Property_AppliesClampedSpeedToTheFrameDelta()
        {
            // The property must be the clamped multiplier applied to Time.deltaTime —
            // asserted against the real frame delta so this holds at any frame value.
            float frame = UnityEngine.Time.deltaTime;

            _timeSystem.SetSpeed(1);
            Assert.AreEqual(frame, _timeSystem.SimulationDelta, 1e-6f, "default/1x = raw frame delta");

            _timeSystem.SetSpeed(3);
            Assert.AreEqual(3f * frame, _timeSystem.SimulationDelta, 1e-6f, "3x = triple the frame delta");

            _timeSystem.SetSpeed(99);
            Assert.AreEqual(3f * frame, _timeSystem.SimulationDelta, 1e-6f, "out-of-range clamps to 3x, never unbounded");

            _timeSystem.SetPaused(true);
            Assert.AreEqual(0f, _timeSystem.SimulationDelta, 1e-6f, "paused is exactly zero");
        }

        // ------------------------------------------- pause freezes progression

        [Test]
        public void Pause_FreezesGathering_AndResumesHeldFractionWithoutLossOrJump()
        {
            GiveHerbPlot(_provider);
            int herbBefore = Raw("herb");

            // 0.25 of a unit banked, none produced yet
            _provider.TickGathering(1.25f);
            Assert.AreEqual(herbBefore, Raw("herb"), "sub-unit tick produces nothing");

            _resourceBuffer.Messages.Clear();
            _timeSystem.SetPaused(true);

            // Real Tick() calls: a paused game must advance nothing, however many frames pass.
            for (int i = 0; i < 200; i++) _discipleSystem.Tick();

            Assert.AreEqual(herbBefore, Raw("herb"), "paused gathering must produce nothing");
            Assert.AreEqual(0, _resourceBuffer.Messages.Count, "a paused tick publishes no resource change");

            // Resume: banked 0.25 + 0.2/s * 3.75s = exactly 1.0 → +1 herb, no more, no less.
            _timeSystem.SetPaused(false);
            _provider.TickGathering(3.75f);
            Assert.AreEqual(herbBefore + 1, Raw("herb"),
                            "resume must continue from the held fraction (lost fraction would yield +0, a jump would yield +2)");
        }

        [Test]
        public void Pause_FreezesCrafting_AndResumesHeldProgressWithoutLossOrJump()
        {
            GivePillHall(_provider);
            int herbBefore = Raw("herb");

            // 15s toward the 20s craft — held, nothing produced
            _provider.TickCrafting(15f);
            Assert.AreEqual(herbBefore, Raw("herb"), "sub-craftTime tick produces nothing");

            _resourceBuffer.Messages.Clear();
            _timeSystem.SetPaused(true);
            for (int i = 0; i < 200; i++) _craftingSystem.Tick();

            Assert.AreEqual(herbBefore, Raw("herb"), "paused crafting must consume/produce nothing");
            Assert.AreEqual(0, _resourceBuffer.Messages.Count, "a paused tick publishes no resource change");

            // 4s would complete it if the 15s had advanced during the pause
            _timeSystem.SetPaused(false);
            _provider.TickCrafting(4f);
            Assert.AreEqual(herbBefore, Raw("herb"), "progress must not have advanced while paused");

            // 15 + 5 = 20s exactly → one craft, cost 10 herb
            _provider.TickCrafting(1f);
            Assert.AreEqual(herbBefore - CraftCostHerb, Raw("herb"), "resume completes from held progress with no jump");
            Assert.AreEqual("refining_elixir", Find("d001").CurrentTask, "completion must not rewrite CurrentTask");
        }

        [Test]
        public void DecisionRequiringWorldEvent_FreezesGatheringAndCrafting_UntilTheDecisionUnpauses()
        {
            GiveHerbPlot(_provider);
            var pillHall = GivePillHall(_provider); // note: pill_hall is for d002; d001 is switched to crafting by the helper
            Find("d001").CurrentTask = "gathering_herb"; // keep the gathering path observable
            Find("d002").CurrentTask = "refining_elixir";

            int herbBefore = Raw("herb");

            // This is exactly what WorldEventSystem does for a decision-requiring event.
            _timeSystem.RaiseWorldEvent("bandit_raid_001", "Bandits at the gate.", true, new List<EventChoiceInfo>());
            Assert.IsTrue(_timeSystem.IsPaused, "a decision-requiring event auto-pauses the game");

            _resourceBuffer.Messages.Clear();
            for (int i = 0; i < 200; i++)
            {
                _discipleSystem.Tick();
                _craftingSystem.Tick();
            }

            Assert.AreEqual(herbBefore, Raw("herb"), "no gathering/crafting while a decision is outstanding");
            Assert.AreEqual(0, _resourceBuffer.Messages.Count, "no resource change while paused");
            Assert.AreEqual("gathering_herb", Find("d001").CurrentTask, "pause must not rewrite CurrentTask");

            // The decision path (DecisionExecutor) unpauses; progression resumes.
            // E1: resolving a decision clears PendingDecision specifically.
            _timeSystem.Resume(TimePauseReason.PendingDecision);
            _provider.TickGathering(5f); // 0.2/s * 5s = 1.0 → exactly +1
            Assert.AreEqual(herbBefore + 1, Raw("herb"), "gathering resumes once the decision unpauses");

            // keep the placed building referenced so the intent (a real pill_hall) is clear
            Assert.IsNotNull(pillHall);
        }

        // ------------------------------------------- speed scales exactly once

        [Test]
        public void Gathering_Speed3_ProducesExactlyThreeTimesTheUnits_ForTheSameRealDelta()
        {
            const float realDelta = 10f; // the same un-scaled time budget for both runs

            int atSpeed1 = GatherUnitsAtSpeed(1, realDelta);
            int atSpeed3 = GatherUnitsAtSpeed(3, realDelta);

            Assert.AreEqual(2, atSpeed1, "0.2/s over 10s = 2 units at 1x");
            Assert.AreEqual(3 * atSpeed1, atSpeed3, "the same real delta at 3x yields exactly 3x the units");
        }

        [Test]
        public void Crafting_Speed3_ReachesCompletionInOneThirdOfTheTicks_ForTheSameRealDelta()
        {
            // 6.7 * 1 = 6.7 < 20 (needs 3 ticks), 6.7 * 3 = 20.1 >= 20 (needs 1 tick).
            // A craft per disciple is capped at one per tick, so tick count is the honest
            // observable consequence of 3x progress — the progress VALUE is exactly 3x
            // because both runs consume TimeSystem.ComputeSimulationDelta (see the
            // multiplier test above).
            const float realDelta = 6.7f;

            Assert.AreEqual(3, TicksToCompleteCraft(1, realDelta), "1x needs three ticks to reach 20s");
            Assert.AreEqual(1, TicksToCompleteCraft(3, realDelta), "3x needs one tick — a third of the ticks");
        }

        private static int GatherUnitsAtSpeed(int speed, float realDeltaSeconds)
        {
            SectStateProvider provider;
            BufferPublisher<SectResourceChangedMessage> unused;
            provider = NewProvider(out unused);
            GiveHerbPlot(provider);

            var time = NewTimeSystem();
            time.SetSpeed(speed); // public API, not UI

            int before = RawIn(provider, "herb");
            // exactly the delta a DiscipleSystem tick would pass at this speed
            provider.TickGathering(TimeSystem.ComputeSimulationDelta(false, speed, realDeltaSeconds));
            return RawIn(provider, "herb") - before;
        }

        private static int TicksToCompleteCraft(int speed, float realDeltaSeconds)
        {
            SectStateProvider provider;
            BufferPublisher<SectResourceChangedMessage> unused;
            provider = NewProvider(out unused);
            GivePillHall(provider);

            // controlled stock so completion is observable as herb 100 → 90
            SetStockIn(provider, "herb", 100);

            float delta = TimeSystem.ComputeSimulationDelta(false, speed, realDeltaSeconds);

            int ticks = 0;
            while (RawIn(provider, "herb") == 100 && ticks < 10)
            {
                provider.TickCrafting(delta);
                ticks++;
            }
            Assert.AreEqual(100 - CraftCostHerb, RawIn(provider, "herb"),
                            "the craft must complete within the tick budget");
            return ticks;
        }

        // ------------------------------------------- the other gameplay timers

        [Test]
        public void AutoScheduler_Tick_WhilePaused_DoesNotAdvanceOrEvaluate()
        {
            SetStockIn(_provider, "ore", 0);           // make gathering_ore attractive
            SetStockIn(_provider, "herb", 200);
            SetStockIn(_provider, "wood", 200);
            SetStockIn(_provider, "provisions", 200);

            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleControlMode("d000", DiscipleControlMode.Auto, out reason), reason);

            var scheduler = new AutoTaskScheduler(_provider, _timeSystem)
            {
                EvaluationIntervalSeconds = 10f,
                MinimumDwellSeconds = 0f,
                ImprovementMargin = 0f,
            };

            _timeSystem.SetPaused(true);
            for (int i = 0; i < 200; i++) scheduler.Tick(); // deterministic: SimulationDelta is 0

            Assert.AreEqual("meditation", Find("d000").CurrentTask, "paused gameplay must not evaluate");
            Assert.AreEqual(DiscipleControlMode.Auto, Find("d000").ControlMode);

            // unpausing with a due slot still evaluates — the interval was not consumed
            _timeSystem.SetPaused(false);
            scheduler.Advance(0f);
            Assert.AreEqual("gathering_ore", Find("d000").CurrentTask, "the interval was not consumed while paused");
        }

        [Test]
        public void WorldEventTimer_AdvancesOnSimulationDelta_AndFreezesWhilePaused()
        {
            var events = new BufferPublisher<WorldEventTriggeredMessage>();
            // E2-lite: no event is raised from the constructor any more, and this test
            // is about the event TIMER - so auto-pause is off and the start grace is 0,
            // letting the clock be driven purely by the explicit deltas below.
            var time = new TimeSystem(new BufferPublisher<TimeSpeedChangedMessage>(), events,
                                      new TimeRuntimeConfig { AutoPauseOnDecisionEvent = false });
            var system = new WorldEventSystem(time, new LubanEventPool(),
                                              new WorldEventRuntimeConfig { StartGraceSeconds = 0f });

            int baseline = events.Messages.Count;

            system.Advance(0f); // grace 0 elapses -> the first event fires from the tick path
            Assert.AreEqual(baseline + 1, events.Messages.Count,
                            "the first event fires from the tick path, not from the constructor");
            int afterFirst = events.Messages.Count;

            time.SetPaused(true);
            system.Advance(1000f);
            Assert.AreEqual(afterFirst, events.Messages.Count, "paused: the event timer must not advance (no pile-up)");

            time.SetPaused(false);
            system.Advance(10f);
            Assert.AreEqual(afterFirst, events.Messages.Count, "below the 15s interval: no event");

            system.Advance(6f); // 10 + 6 = 16s of simulation time
            Assert.AreEqual(afterFirst + 1, events.Messages.Count,
                            "the 15s interval elapsed on simulation time (and the paused 1000f was discarded)");
        }

        // ---------------------------------------------------------------- test seams

        private static TimeSystem NewTimeSystem()
        {
            return new TimeSystem(new BufferPublisher<TimeSpeedChangedMessage>(),
                                  new BufferPublisher<WorldEventTriggeredMessage>());
        }

        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public readonly List<T> Messages = new List<T>();
            public void Publish(T message) => Messages.Add(message);
        }
    }
}
