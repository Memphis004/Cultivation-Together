using System.Collections.Generic;
using MessagePipe;
using NUnit.Framework;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// Task building-requirement gate (§6 addendum — TaskRequiredBuilding):
    ///   - refining_elixir fails with no pill_hall in PlacedBuildings
    ///     (mock start already places herb_plot b001, so gathering_herb succeeds
    ///     out of the box — see GatheringHerb_SucceedsWithMockStartHerbPlot)
    ///   - meditation and gathering_wood succeed with no buildings
    ///   - unknown task keeps its original failReason
    ///   - no DiscipleTaskChangedMessage on a requirement failure
    ///
    /// Buildings are placed through TryPlaceBuilding (state append) on a plain
    /// 10x10 grid; the requirement check itself is placed-def-id presence in
    /// state, so grid/geography is irrelevant here. Same provider construction
    /// and BufferPublisher pattern as AssignTaskTests.
    /// </summary>
    public class TaskRequirementTests
    {
        private SectStateProvider _provider;
        private BuildingGrid _grid;
        private BuildingDefPool _defPool;
        private BufferPublisher<DiscipleTaskChangedMessage> _taskChanged;

        [SetUp]
        public void SetUp()
        {
            _taskChanged = new BufferPublisher<DiscipleTaskChangedMessage>();

            _provider = new SectStateProvider(
                new BufferPublisher<DiscipleRecruitedMessage>(),
                new BufferPublisher<SectResourceChangedMessage>(),
                new BufferPublisher<AvatarEquipmentChangedMessage>(),
                new BufferPublisher<DiscipleChibiBackendChangedMessage>(),
                new AvatarPartPool(),
                Visual.VisualRuntimeConfig.Instance,
                new Visual.DefaultEntitlementProvider(),
                _defPool = new BuildingDefPool(),
                new BufferPublisher<BuildingPlacedMessage>(),
                _taskChanged);

            _grid = new BuildingGrid(10, 10);
        }

        private DiscipleState Find(string id)
        {
            foreach (var d in _provider.BuildSectEconomyState().Disciples)
            {
                if (d.DiscipleId == id) return d;
            }
            return null;
        }

        private bool Place(string defId)
        {
            string reason;
            PlacedBuildingState placed;
            return _provider.TryPlaceBuilding(defId, 0, 0, 0, _grid, out reason, out placed);
        }

        // ---- requirement gate on TryAssignTask ----
        // (mock start state already places herb_plot b001, so the failure paths
        // exercise refining_elixir/pill_hall — pill_hall is NOT in the start state)

        [Test]
        public void RefiningElixir_FailsWithoutPillHall()
        {
            string reason;
            Assert.IsFalse(_provider.TryAssignTask("SECT_MASTER", "d000", "refining_elixir", out reason));
            Assert.IsTrue(reason.Contains("pill_hall"),
                          "failReason must name the missing building — got: " + reason);
            Assert.AreEqual("meditation", Find("d000").CurrentTask, "task must not change");
        }

        [Test]
        public void GatheringHerb_SucceedsWithMockStartHerbPlot()
        {
            // mock start already places herb_plot b001 — no setup needed
            string reason;
            Assert.IsTrue(_provider.TryAssignTask("SECT_MASTER", "d000", "gathering_herb", out reason), reason);
            Assert.AreEqual("gathering_herb", Find("d000").CurrentTask);
            Assert.AreEqual(1, _taskChanged.Messages.Count);
        }

        [Test]
        public void Meditation_SucceedsWithNoBuildings()
        {
            string reason;
            Assert.IsTrue(_provider.TryAssignTask("SECT_MASTER", "d000", "meditation", out reason), reason);
            Assert.AreEqual("meditation", Find("d000").CurrentTask);
        }

        [Test]
        public void GatheringWood_SucceedsWithNoBuildings()
        {
            string reason;
            Assert.IsTrue(_provider.TryAssignTask("SECT_MASTER", "d000", "gathering_wood", out reason), reason);
            Assert.AreEqual("gathering_wood", Find("d000").CurrentTask);
        }

        [Test]
        public void UnknownTask_KeepsOriginalFailReason()
        {
            string reason;
            Assert.IsFalse(_provider.TryAssignTask("SECT_MASTER", "d000", "not_a_real_task", out reason));
            Assert.AreEqual("Unknown task: 'not_a_real_task'.", reason);
            Assert.AreEqual(0, _taskChanged.Messages.Count);
        }

        [Test]
        public void RequirementFailure_PublishesNoDiscipleTaskChangedMessage()
        {
            string reason;
            Assert.IsFalse(_provider.TryAssignTask("SECT_MASTER", "d000", "refining_elixir", out reason));
            Assert.AreEqual(0, _taskChanged.Messages.Count, "no message on requirement failure");
        }

        [Test]
        public void RequirementFailure_StateUnchanged_NoResourceMutation()
        {
            int wood = _provider.BuildSectEconomyState().Stockpile.RawResources["wood"];

            string reason;
            Assert.IsFalse(_provider.TryAssignTask("SECT_MASTER", "d000", "refining_elixir", out reason));
            Assert.IsTrue(reason.Contains("pill_hall"), "got: " + reason);
            Assert.AreEqual("meditation", Find("d002").CurrentTask, "d002 keeps its own task");
            Assert.AreEqual(wood, _provider.BuildSectEconomyState().Stockpile.RawResources["wood"]);
        }

        // ---- IsTaskAvailable called directly ----

        [Test]
        public void IsTaskAvailable_UnknownTask_Reasons()
        {
            string reason;
            Assert.IsFalse(_provider.IsTaskAvailable("not_a_real_task", out reason));
            Assert.IsTrue(reason.Contains("Unknown task"), "got: " + reason);
        }

        [Test]
        public void IsTaskAvailable_NoRequirement_Task_IsTrue()
        {
            string reason;
            Assert.IsTrue(_provider.IsTaskAvailable("meditation", out reason), reason);
            Assert.IsTrue(_provider.IsTaskAvailable("gathering_wood", out reason), reason);
        }

        [Test]
        public void IsTaskAvailable_RequiredBuilding_PresenceFlipsResult()
        {
            string reason;
            Assert.IsFalse(_provider.IsTaskAvailable("refining_elixir", out reason));
            Assert.IsTrue(reason.Contains("pill_hall"), "got: " + reason);

            Assert.IsTrue(Place("pill_hall"));
            Assert.IsTrue(_provider.IsTaskAvailable("refining_elixir", out reason), reason);
        }

        [Test]
        public void IsTaskAvailable_EmptyTaskId_Reasons()
        {
            string reason;
            Assert.IsFalse(_provider.IsTaskAvailable("", out reason));
            Assert.IsTrue(reason.Contains("Unknown task"), "got: " + reason);
        }

        /// <summary>
        /// Minimal IPublisher&lt;T&gt; over a list — same pattern as
        /// AssignTaskTests / BuildingPlacementTests.
        /// </summary>
        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public readonly List<T> Messages = new List<T>();
            public void Publish(T message) => Messages.Add(message);
        }
    }
}
