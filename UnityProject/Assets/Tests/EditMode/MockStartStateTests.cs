using System.Collections.Generic;
using System.Linq;
using MessagePipe;
using NUnit.Framework;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// Mock start-state acceptance (task building-requirements follow-up):
    ///   - MockSectData.Create() placed buildings all satisfy BuildingGrid.CanPlace
    ///     on an empty production-sized grid (50x50, origin -25,-25) with the
    ///     PlaceableLandMask applied — same invariant BuildingSystem.RebuildGridFromState
    ///     relies on at Start (AC #6).
    ///   - d001 (gathering_herb, herb_plot b001 in start state) accrues herb over
    ///     several ticks, preserving the fractional-accumulator remainder.
    ///   - A disciple on refining_elixir with no pill_hall placed produces and
    ///     consumes NOTHING and still has CurrentTask "refining_elixir" (gate
    ///     skips without rewriting the task or resetting progress).
    /// Same provider construction / BufferPublisher pattern as AssignTaskTests.
    /// </summary>
    public class MockStartStateTests
    {
        private SectStateProvider _provider;
        private BufferPublisher<SectResourceChangedMessage> _resourceBuffer;

        [SetUp]
        public void SetUp()
        {
            _resourceBuffer = new BufferPublisher<SectResourceChangedMessage>();

            _provider = new SectStateProvider(
                new BufferPublisher<DiscipleRecruitedMessage>(),
                _resourceBuffer,
                new BufferPublisher<AvatarEquipmentChangedMessage>(),
                new BufferPublisher<DiscipleChibiBackendChangedMessage>(),
                new AvatarPartPool(),
                Visual.VisualRuntimeConfig.Instance,
                new Visual.DefaultEntitlementProvider(),
                new BuildingDefPool(),
                new BufferPublisher<BuildingPlacedMessage>(),
                new BufferPublisher<DiscipleTaskChangedMessage>());
        }

        private SectEconomyState State => _provider.BuildSectEconomyState();

        private int Raw(string id)
        {
            int v;
            return State.Stockpile.RawResources.TryGetValue(id, out v) ? v : 0;
        }

        private DiscipleState Find(string id)
        {
            foreach (var d in State.Disciples)
            {
                if (d.DiscipleId == id) return d;
            }
            return null;
        }

        // ---- item 3: mock placed buildings satisfy CanPlace with the land mask ----

        [Test]
        public void MockPlacedBuildings_SatisfyCanPlace_OnProductionGridWithLandMask()
        {
            // production-sized grid: 50x50, origin -25,-25 — same shape BuildingSystem uses
            var grid = new BuildingGrid(PlaceableLandMask.Width, PlaceableLandMask.Height,
                                        PlaceableLandMask.OriginX, PlaceableLandMask.OriginY);
            grid.SetPlaceableMask(PlaceableLandMask.OriginX, PlaceableLandMask.OriginY,
                                  PlaceableLandMask.Width, PlaceableLandMask.Height,
                                  PlaceableLandMask.BuildCells(PlaceableLandMask.OriginX, PlaceableLandMask.OriginY,
                                                               PlaceableLandMask.Width, PlaceableLandMask.Height));

            var pool = new BuildingDefPool();
            var placed = State.PlacedBuildings;
            Assert.GreaterOrEqual(placed.Count, 1, "mock start state must carry at least one placed building");

            foreach (var pb in placed)
            {
                var def = pool.GetById(pb.DefId);
                Assert.IsNotNull(def, "mock placed building must reference a known def: " + pb.DefId);
                Assert.IsTrue(grid.CanPlace(pb.GridX, pb.GridZ, def.GridWidth, def.GridHeight, pb.Rotation),
                              $"mock building '{pb.InstanceId}' ({pb.DefId}) at ({pb.GridX},{pb.GridZ}) " +
                              $"rot={pb.Rotation} must satisfy CanPlace on the production grid with the land mask");
            }
        }

        // ---- item 5a: d001 accrues herb over several ticks ----

        [Test]
        public void TickGathering_D001GathersHerb_OverSeveralTicks_WithRemainderHeld()
        {
            Assert.AreEqual("gathering_herb", Find("d001").CurrentTask, "mock start d001 task");

            int herbBefore = Raw("herb"); // mock start: 120

            // 0.2/s * 10s = 2.0 whole units
            _provider.TickGathering(10f);
            Assert.AreEqual(herbBefore + 2, Raw("herb"), "10s tick yields 2 herb");

            // 0.25 fractional — no whole unit yet, remainder must be held
            _provider.TickGathering(1.25f);
            Assert.AreEqual(herbBefore + 2, Raw("herb"), "fractional tick produces nothing yet");

            _provider.TickGathering(1f);
            Assert.AreEqual(herbBefore + 2, Raw("herb"), "remainder 0.25+0.2 still under 1");

            _provider.TickGathering(3f);
            Assert.AreEqual(herbBefore + 3, Raw("herb"), "remainder 0.45+0.6 crosses 1 → +1");
        }

        // ---- item 5b: refining_elixir with no pill_hall — gated, task kept ----

        [Test]
        public void TickCrafting_RefiningElixirWithoutPillHall_ProducesAndConsumesNothing_KeepsTask()
        {
            // test setup: put d001 on refining_elixir directly (CurrentTask is not a
            // resource — AdjustAndNotify does not apply; mock has NO pill_hall)
            var d001 = Find("d001");
            d001.CurrentTask = "refining_elixir";

            int herb = Raw("herb");
            int wood = Raw("wood");
            int goodsCount = State.Stockpile.CraftedGoods.Count;

            _provider.TickCrafting(9999f); // far past the 20s craft — must still be gated
            _provider.TickGathering(100f); // gathering side must be gated too

            Assert.AreEqual(herb, Raw("herb"), "no resource consumed or produced while gated");
            Assert.AreEqual(wood, Raw("wood"), "no other resource touched");
            Assert.AreEqual(goodsCount, State.Stockpile.CraftedGoods.Count, "no crafted goods added");
            Assert.AreEqual(0, _resourceBuffer.Messages.Count, "no SectResourceChangedMessage while gated");
            Assert.AreEqual("refining_elixir", d001.CurrentTask,
                            "gate must skip — never rewrite CurrentTask");
        }

        // ---- item 5c: blocked window preserves the fractional gather accumulator ----
        // Herb_plot removed mid-run → gated ticks produce nothing AND the held
        // remainder survives; re-placing it must resume from that remainder.
        // (If the gate reset the accumulator, 3.75s post-unblock = 0.75 < 1 → no herb.)
        [Test]
        public void TickGathering_BlockedWindow_PreservesFractionalAccumulator()
        {
            Assert.AreEqual("gathering_herb", Find("d001").CurrentTask, "mock start d001 task");

            int herbBefore = Raw("herb");

            // build a 0.25 remainder (0.2/s * 1.25s)
            _provider.TickGathering(1.25f);
            Assert.AreEqual(herbBefore, Raw("herb"), "remainder only — no whole unit yet");

            // block: remove herb_plot b001 from live state (gathering_herb gated)
            var herbPlot = State.PlacedBuildings.Find(p => p.InstanceId == "b001");
            Assert.IsNotNull(herbPlot, "mock start must contain herb_plot b001");
            State.PlacedBuildings.Remove(herbPlot);

            _provider.TickGathering(10f);
            _provider.TickGathering(100f);
            Assert.AreEqual(herbBefore, Raw("herb"), "gated ticks produce nothing");
            Assert.AreEqual("gathering_herb", Find("d001").CurrentTask,
                            "gate must skip — never rewrite CurrentTask");

            // unblock: remainder 0.25 must still be there. 0.25 + 0.2/s*3.75 = 1.0 → +1
            State.PlacedBuildings.Add(herbPlot);
            _provider.TickGathering(3.75f);
            Assert.AreEqual(herbBefore + 1, Raw("herb"),
                            "held remainder must survive the blocked window");
        }

        // ---- item 5d: blocked window preserves craft progress (held, never reset/advanced) ----
        // d001 on refining_elixir: 15s progress → pill_hall removed → 9999s gated tick
        // must leave progress at 15. Re-placing then ticking 4s (19 < 20) must produce
        // nothing (progress was not advanced), and one more second must complete it
        // (progress was not reset).
        [Test]
        public void TickCrafting_BlockedWindow_PreservesCraftProgress()
        {
            var d001 = Find("d001");
            d001.CurrentTask = "refining_elixir";

            // place pill_hall on a plain grid (occupancy irrelevant — the gate reads state)
            var grid = new BuildingGrid(10, 10);
            string reason;
            PlacedBuildingState pillHall;
            Assert.IsTrue(_provider.TryPlaceBuilding("pill_hall", 0, 0, 0, grid, out reason, out pillHall), reason);

            // 15s of progress toward the 20s craft — below threshold, nothing happens
            int herbBefore = Raw("herb");
            _provider.TickCrafting(15f);
            Assert.AreEqual(herbBefore, Raw("herb"), "sub-craftTime tick produces nothing");

            // block: remove pill_hall → gated, far-past-craftTime tick must not advance
            State.PlacedBuildings.Remove(pillHall);
            _provider.TickCrafting(9999f);
            Assert.AreEqual(herbBefore, Raw("herb"), "gated tick consumes/produces nothing");
            Assert.AreEqual("refining_elixir", d001.CurrentTask,
                            "gate must skip — never rewrite CurrentTask");

            // unblock: if progress had advanced to 20 during the blocked window,
            // this 4s tick would already complete the craft
            State.PlacedBuildings.Add(pillHall);
            _provider.TickCrafting(4f);
            Assert.AreEqual(herbBefore, Raw("herb"), "progress was held at 15, not advanced to 20");

            // if progress had been reset to 0, 5s total would still be short of 20s
            _provider.TickCrafting(1f);
            Assert.AreEqual(herbBefore - 10, Raw("herb"), "held 15s + 5s completes the craft (cost 10 herb)");
            Assert.AreEqual("refining_elixir", d001.CurrentTask, "craft completes without rewriting CurrentTask");
        }

        // ---- P2: sequential validate→occupy on ONE shared grid ----
        // Checking each building on a separate empty grid would miss overlaps;
        // here every placement is validated against the occupancy left by the
        // ones before it — the exact order BuildingSystem.RebuildGridFromState
        // performs at Start (AC #6).
        [Test]
        public void StarterBuildings_SequentialValidateThenOccupy_NoOverlap()
        {
            var grid = new BuildingGrid(PlaceableLandMask.Width, PlaceableLandMask.Height,
                                        PlaceableLandMask.OriginX, PlaceableLandMask.OriginY);
            grid.SetPlaceableMask(PlaceableLandMask.OriginX, PlaceableLandMask.OriginY,
                                  PlaceableLandMask.Width, PlaceableLandMask.Height,
                                  PlaceableLandMask.BuildCells(PlaceableLandMask.OriginX, PlaceableLandMask.OriginY,
                                                               PlaceableLandMask.Width, PlaceableLandMask.Height));

            var pool = new BuildingDefPool();
            var placed = State.PlacedBuildings;
            Assert.GreaterOrEqual(placed.Count, 1, "mock start state must carry at least one placed building");

            var ids = new HashSet<string>();
            foreach (var pb in placed)
            {
                Assert.IsTrue(ids.Add(pb.InstanceId),
                              $"InstanceId must be unique: '{pb.InstanceId}' appears twice");

                var def = pool.GetById(pb.DefId);
                Assert.IsNotNull(def, "mock placed building must reference a known def: " + pb.DefId);

                // validate against everything already occupied, THEN occupy
                Assert.IsTrue(grid.CanPlace(pb.GridX, pb.GridZ, def.GridWidth, def.GridHeight, pb.Rotation),
                              $"'{pb.InstanceId}' ({pb.DefId}) at ({pb.GridX},{pb.GridZ}) rot={pb.Rotation} " +
                              "must not overlap earlier starter buildings and must sit on land");
                grid.Occupy(pb.InstanceId, pb.GridX, pb.GridZ,
                            def.GridWidth, def.GridHeight, pb.Rotation);
            }

            Assert.AreEqual(1, placed.Count(p => p.DefId == "herb_plot"),
                            "starter configuration carries exactly one herb_plot");
        }

        // ---- P2: every initial disciple task is available in the starter state ----
        // Fresh Play must not have any disciple on a task whose required building
        // is missing (refining_elixir/forging_artifact need pill_hall/forge — the
        // mock routes those disciples to meditation instead).
        [Test]
        public void InitialTasks_AllAvailable_InStarterState()
        {
            foreach (var d in State.Disciples)
            {
                string reason;
                Assert.IsTrue(_provider.IsTaskAvailable(d.CurrentTask, out reason),
                              $"{d.DiscipleId} starts on '{d.CurrentTask}' which is unavailable: {reason}");
            }
        }

        /// <summary>
        /// Minimal IPublisher&lt;T&gt; over a list — same pattern as
        /// AssignTaskTests / TaskRequirementTests.
        /// </summary>
        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public readonly List<T> Messages = new List<T>();
            public void Publish(T message) => Messages.Add(message);
        }
    }
}
