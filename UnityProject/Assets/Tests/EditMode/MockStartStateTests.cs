using System.Collections.Generic;
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
