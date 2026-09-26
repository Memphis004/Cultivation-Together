using System.Collections.Generic;
using MessagePipe;
using NUnit.Framework;
using UnityEngine;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// Placement acceptance (building-system.md §3.2/§5 + task AC #3–#7):
    ///   - วางสำเร็จ → หักทรัพยากรถูกต้อง + SectResourceChangedMessage ยิงจริง
    ///     (ผ่าน AdjustAndNotify เดิม — ไม่มีหักตรง)
    ///   - ซ้อนทับ → reject, state ไม่เปลี่ยน (no partial mutation)
    ///   - ทรัพยากรไม่พอ → reject, state ไม่เปลี่ยน
    ///   - commit สำเร็จ → PlacedBuildings + grid occupancy ตรงกัน + BuildingPlacedMessage
    ///   - Cancel ระหว่าง placement mode → state ไม่เปลี่ยนเลย
    /// </summary>
    public class BuildingPlacementTests
    {
        private SectStateProvider _provider;
        private BuildingGrid _grid;
        private BuildingDefPool _defPool;
        private PlacementController _controller;
        private BufferPublisher<SectResourceChangedMessage> _resourceBuffer;
        private BufferPublisher<BuildingPlacedMessage> _placedBuffer;

        [SetUp]
        public void SetUp()
        {
            _resourceBuffer = new BufferPublisher<SectResourceChangedMessage>();
            _placedBuffer = new BufferPublisher<BuildingPlacedMessage>();
            _defPool = new BuildingDefPool();

            _provider = new SectStateProvider(
                new BufferPublisher<DiscipleRecruitedMessage>(),
                _resourceBuffer,
                new BufferPublisher<AvatarEquipmentChangedMessage>(),
                new BufferPublisher<DiscipleChibiBackendChangedMessage>(),
                new AvatarPartPool(),
                Visual.VisualRuntimeConfig.Instance,
                new Visual.DefaultEntitlementProvider(),
                _defPool,
                _placedBuffer);

            _grid = new BuildingGrid(10, 10);
            _controller = new PlacementController(_defPool, _provider);
        }

        private SectEconomyState State => _provider.BuildSectEconomyState();

        private int Raw(string id)
        {
            int v;
            return State.Stockpile.RawResources.TryGetValue(id, out v) ? v : 0;
        }

        private static string DumpRaw(SectEconomyState state)
        {
            var parts = new List<string>();
            foreach (var kv in state.Stockpile.RawResources) parts.Add(kv.Key + "=" + kv.Value);
            return string.Join(", ", parts.ToArray());
        }

        // ---- วางสำเร็จ: หักทรัพยากรผ่าน choke point เดิม ----

        [Test]
        public void Commit_Success_DeductsExactCost_AndPublishesResourceMessages()
        {
            int woodBefore = Raw("wood");
            var def = _controller.BeginPlacement("herb_plot"); // cost wood 20
            Assert.IsNotNull(def);

            _controller.UpdatePosition(2, 2);
            Assert.IsTrue(_controller.CanCommit(_grid));

            PlacedBuildingState placed;
            string reason;
            Assert.IsTrue(_controller.TryCommit(_grid, out reason, out placed), reason);

            Assert.AreEqual(woodBefore - 20, Raw("wood"), "cost must be deducted exactly");
            Assert.AreEqual(1, State.PlacedBuildings.Count, "state append exactly one entry");

            // SectResourceChangedMessage ต้องยิงจริง (HUD อัปเดตจาก message นี้)
            bool resourceMsg = false;
            foreach (var m in _resourceBuffer.Messages)
            {
                if (m.ResourceId == "wood" && m.Delta == -20)
                {
                    resourceMsg = true;
                    Assert.AreEqual(woodBefore - 20, m.NewTotal, "NewTotal must match state");
                }
            }
            Assert.IsTrue(resourceMsg, "AdjustAndNotify must publish SectResourceChangedMessage(-20 wood)");

            Assert.AreEqual(1, _placedBuffer.Messages.Count, "BuildingPlacedMessage once per commit");
            Assert.AreEqual(placed.InstanceId, _placedBuffer.Messages[0].InstanceId);
            Assert.AreEqual("herb_plot", _placedBuffer.Messages[0].DefId);
        }

        [Test]
        public void Commit_Success_GridOccupancy_MatchesState()
        {
            _controller.BeginPlacement("herb_plot");
            _controller.UpdatePosition(1, 1);
            PlacedBuildingState placed;
            string reason;
            Assert.IsTrue(_controller.TryCommit(_grid, out reason, out placed), reason);

            Assert.AreEqual(placed.InstanceId, _grid.GetOccupant(1, 1), "corner cell");
            Assert.AreEqual(placed.InstanceId, _grid.GetOccupant(2, 2), "far corner of 2x2");
            Assert.IsNull(_grid.GetOccupant(3, 3), "outside footprint stays free");
        }

        [Test]
        public void Commit_MultiResourceCost_DeductsEveryResource()
        {
            int woodBefore = Raw("wood"), oreBefore = Raw("ore");
            _controller.BeginPlacement("pill_hall"); // wood 40 + ore 30
            _controller.UpdatePosition(0, 0);

            PlacedBuildingState placed;
            string reason;
            Assert.IsTrue(_controller.TryCommit(_grid, out reason, out placed), reason);

            Assert.AreEqual(woodBefore - 40, Raw("wood"));
            Assert.AreEqual(oreBefore - 30, Raw("ore"));
        }

        // ---- ซ้อนทับ → reject, state ไม่เปลี่ยน ----

        [Test]
        public void Commit_Overlap_IsRejected_StateUnchanged()
        {
            PlacedBuildingState first;
            string reason;
            _controller.BeginPlacement("herb_plot");
            _controller.UpdatePosition(2, 2);
            Assert.IsTrue(_controller.TryCommit(_grid, out reason, out first), reason);

            // snapshot state หลังวางแรก
            int wood = Raw("wood");
            int placedCount = State.PlacedBuildings.Count;
            int msgCount = _resourceBuffer.Messages.Count;

            var second = new PlacementController(_defPool, _provider);
            second.BeginPlacement("pill_hall");
            second.UpdatePosition(3, 3); // ซ้อน 2x2 แรก (pill_hall 3x3 ครอบ (3,3))

            Assert.IsFalse(second.CanCommit(_grid), "overlap ghost must be red");
            PlacedBuildingState placed;
            Assert.IsFalse(second.TryCommit(_grid, out reason, out placed), "overlap must reject");
            Assert.IsNotEmpty(reason);

            Assert.AreEqual(wood, Raw("wood"), "no resource touched on reject");
            Assert.AreEqual(placedCount, State.PlacedBuildings.Count, "no state append on reject");
            Assert.AreEqual(msgCount, _resourceBuffer.Messages.Count, "no publish on reject");
            Assert.AreEqual(first.InstanceId, _grid.GetOccupant(2, 2), "grid untouched");
        }

        // ---- ทรัพยากรไม่พอ → reject, state ไม่เปลี่ยน ----

        [Test]
        public void Commit_InsufficientResources_IsRejected_StateUnchanged()
        {
            // pill_hall ต้อง ore 30 — ตั้งค่า mock ให้พอดีขาด (test-only direct set)
            State.Stockpile.RawResources["ore"] = 25;

            int wood = Raw("wood"), provisions = Raw("provisions");
            int placedCount = State.PlacedBuildings.Count;

            _controller.BeginPlacement("pill_hall");
            _controller.UpdatePosition(0, 0);

            Assert.IsFalse(_controller.CanCommit(_grid), "too-poor ghost must be red");
            string reason;
            PlacedBuildingState placed;
            Assert.IsFalse(_controller.TryCommit(_grid, out reason, out placed));
            Assert.IsTrue(reason.Contains("ทรัพยากรไม่พอ"), "reason must say resources missing, got: " + reason);

            Assert.AreEqual(25, Raw("ore"), "ore untouched");
            Assert.AreEqual(wood, Raw("wood"));
            Assert.AreEqual(provisions, Raw("provisions"));
            Assert.AreEqual(placedCount, State.PlacedBuildings.Count);
        }

        // ---- out of bounds ----

        [Test]
        public void Commit_OutOfBounds_IsRejected()
        {
            _controller.BeginPlacement("herb_plot");
            _controller.UpdatePosition(9, 9); // 2x2 ที่ (9,9) ล้น grid 10x10

            Assert.IsFalse(_controller.CanCommit(_grid));
            string reason;
            PlacedBuildingState placed;
            Assert.IsFalse(_controller.TryCommit(_grid, out reason, out placed));
            Assert.AreEqual(0, State.PlacedBuildings.Count);
        }

        // ---- cancel: ไม่มีอะไรเปลี่ยนใน state เลย ----

        [Test]
        public void Cancel_DuringPlacementMode_ChangesNothingInState()
        {
            _controller.BeginPlacement("herb_plot");
            _controller.UpdatePosition(4, 4);
            _controller.Rotate();

            string before = DumpRaw(State);
            int placedCount = State.PlacedBuildings.Count;
            int msgCount = _resourceBuffer.Messages.Count + _placedBuffer.Messages.Count;

            _controller.Cancel();

            Assert.AreEqual(before, DumpRaw(State), "resources identical after cancel");
            Assert.AreEqual(placedCount, State.PlacedBuildings.Count);
            Assert.AreEqual(msgCount, _resourceBuffer.Messages.Count + _placedBuffer.Messages.Count,
                            "no message published by canceling");
            Assert.IsFalse(_controller.IsActive);
            Assert.IsNull(_controller.CurrentDef);
        }

        // ---- rebuild จาก state (AC #6 — reload/reopen ต้องตรง) ----

        [Test]
        public void RebuildFromState_OccupancyMatchesPlacedBuildings()
        {
            _controller.BeginPlacement("herb_plot");
            _controller.UpdatePosition(2, 3);
            string reason;
            PlacedBuildingState a;
            Assert.IsTrue(_controller.TryCommit(_grid, out reason, out a), reason);

            var controller2 = new PlacementController(_defPool, _provider);
            controller2.BeginPlacement("spirit_pond"); // 2x3 rotatable
            controller2.UpdatePosition(6, 0);
            controller2.Rotate(); // rot90 → footprint 3x2
            PlacedBuildingState b;
            Assert.IsTrue(controller2.TryCommit(_grid, out reason, out b), reason);
            Assert.AreEqual(90, b.Rotation);

            // "reload": grid ใหม่เปล่า + rebuild จาก state.PlacedBuildings เท่านั้น
            var freshGrid = new BuildingGrid(10, 10);
            var pool = _defPool;
            foreach (var pb in State.PlacedBuildings)
            {
                var def = pool.GetById(pb.DefId);
                Assert.IsNotNull(def, "def must exist for every placed building");
                freshGrid.Occupy(pb.InstanceId, pb.GridX, pb.GridZ,
                                 def.GridWidth, def.GridHeight, pb.Rotation);
            }

            Assert.AreEqual(a.InstanceId, freshGrid.GetOccupant(2, 3));
            Assert.AreEqual(a.InstanceId, freshGrid.GetOccupant(3, 4));
            // spirit_pond 2x3 rot90 = 3x2 ที่ (6,0): ครอบ (6..8, 0..1)
            Assert.AreEqual(b.InstanceId, freshGrid.GetOccupant(8, 1));
            Assert.IsNull(freshGrid.GetOccupant(6, 2), "unrotated span must stay free");
        }

        // ---- id determinism ----

        [Test]
        public void PlaceTwice_InstanceIdsAreSequential()
        {
            string reason;
            PlacedBuildingState placed;
            _controller.BeginPlacement("stone_lantern"); // 1x1
            _controller.UpdatePosition(0, 0);
            Assert.IsTrue(_controller.TryCommit(_grid, out reason, out placed), reason);

            var c2 = new PlacementController(_defPool, _provider);
            c2.BeginPlacement("stone_lantern");
            c2.UpdatePosition(5, 5);
            PlacedBuildingState second;
            Assert.IsTrue(c2.TryCommit(_grid, out reason, out second), reason);

            Assert.AreNotEqual(placed.InstanceId, second.InstanceId);
            StringAssert.StartsWith("b", second.InstanceId);
        }

        // ---- rotation gating ----

        [Test]
        public void Rotate_NonRotatableDef_IsRejected()
        {
            _controller.BeginPlacement("herb_plot"); // Rotatable=false
            Assert.IsFalse(_controller.Rotate());
            Assert.AreEqual(0, _controller.Rotation);

            _controller.BeginPlacement("rest_pavilion"); // Rotatable=true
            Assert.IsTrue(_controller.Rotate());
            Assert.AreEqual(90, _controller.Rotation);
        }

        // ---- helpers ----

        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public readonly List<T> Messages = new List<T>();
            public void Publish(T message) { Messages.Add(message); }
        }
    }
}
