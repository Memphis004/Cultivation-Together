using System.Collections.Generic;
using MessagePipe;
using NUnit.Framework;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// Task System v2 (§6) acceptance — SectStateProvider.TryAssignTask:
    ///   - SECT_MASTER can assign an unowned NPC (P5B: Viewer disciples need the
    ///     owner to be inactive past the protection window — see HybridPermissionTests)
    ///   - a viewer with an active membership can assign their own disciple
    ///   - a viewer is rejected on someone else's disciple
    ///   - unknown task rejected
    ///   - unknown disciple rejected
    ///   - DiscipleTaskChangedMessage published on success only
    ///
    /// No Stockpile writes happen on this path, so the AdjustAndNotify choke
    /// point is untouched by design. No interprocess registration — the
    /// message is in-memory only.
    /// </summary>
    public class AssignTaskTests
    {
        private SectStateProvider _provider;
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
                new BuildingDefPool(),
                new BufferPublisher<BuildingPlacedMessage>(),
                _taskChanged);

            // The start state carries NO buildings, so the gathering_herb case below
            // needs its own herb_plot — state presence is all the requirement gate checks.
            PlaceHerbPlot();
        }

        private void PlaceHerbPlot()
        {
            string reason;
            PlacedBuildingState placed;
            Assert.IsTrue(_provider.TryPlaceBuilding("herb_plot", 0, 0, 0, new BuildingGrid(10, 10),
                                                     out reason, out placed), reason);
        }

        private DiscipleState Find(string id)
        {
            foreach (var d in _provider.BuildSectEconomyState().Disciples)
            {
                if (d.DiscipleId == id) return d;
            }
            return null;
        }

        [Test]
        public void SectMaster_CanAssignAnyone()
        {
            string reason;
            Assert.IsTrue(_provider.TryAssignTask("SECT_MASTER", "d000", "gathering_herb", out reason), reason);
            Assert.AreEqual("gathering_herb", Find("d000").CurrentTask);
            Assert.AreEqual(1, _taskChanged.Messages.Count);
        }

        [Test]
        public void Viewer_CanAssignOwnDisciple()
        {
            // P5B: viewer control now requires a valid ACTIVE membership record, so
            // the fixture binds through the real ownership path instead of writing
            // OwnerType/OwnerId directly (which would be a consistency error).
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer, "viewer1", out reason), reason);

            Assert.IsTrue(_provider.TryAssignTask("viewer1", "d001", "gathering_wood", out reason), reason);
            Assert.AreEqual("gathering_wood", Find("d001").CurrentTask);
            Assert.AreEqual(1, _taskChanged.Messages.Count);
            Assert.AreEqual("d001", _taskChanged.Messages[0].DiscipleId);
            Assert.AreEqual("gathering_wood", _taskChanged.Messages[0].TaskId);
        }

        [Test]
        public void Viewer_RejectedOnAnothersDisciple()
        {
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer, "viewer1", out reason), reason);
            var before = Find("d001").CurrentTask;

            Assert.IsFalse(_provider.TryAssignTask("viewer2", "d001", "gathering_ore", out reason));
            Assert.IsFalse(string.IsNullOrEmpty(reason), "rejection must explain why");
            Assert.AreEqual(before, Find("d001").CurrentTask, "state must not change on rejection");
            Assert.AreEqual(0, _taskChanged.Messages.Count, "no message on rejection");
        }

        [Test]
        public void UnknownTask_Rejected()
        {
            // d000 has no OwnerId → default Npc-owned; SECT_MASTER bypasses ownership.
            var before = Find("d000").CurrentTask;

            string reason;
            Assert.IsFalse(_provider.TryAssignTask("SECT_MASTER", "d000", "not_a_real_task", out reason));
            Assert.IsFalse(string.IsNullOrEmpty(reason));
            Assert.AreEqual(before, Find("d000").CurrentTask);
            Assert.AreEqual(0, _taskChanged.Messages.Count);
        }

        [Test]
        public void UnknownDisciple_Rejected()
        {
            string reason;
            Assert.IsFalse(_provider.TryAssignTask("SECT_MASTER", "nope", "meditation", out reason));
            Assert.IsFalse(string.IsNullOrEmpty(reason));
            Assert.AreEqual(0, _taskChanged.Messages.Count);
        }

        [Test]
        public void Message_PublishedOnSuccessOnly()
        {
            string reason;

            // failure first (unknown task) → nothing published
            Assert.IsFalse(_provider.TryAssignTask("SECT_MASTER", "d002", "bogus_task", out reason));
            Assert.AreEqual(0, _taskChanged.Messages.Count);

            // then a real change → exactly one publish. (P5B: assigning the task a
            // disciple already holds is a no-op and publishes NOTHING — so the
            // success case here must actually change the task.)
            Assert.IsTrue(_provider.TryAssignTask("SECT_MASTER", "d002", "gathering_wood", out reason), reason);
            Assert.AreEqual(1, _taskChanged.Messages.Count);
            Assert.AreEqual("d002", _taskChanged.Messages[0].DiscipleId);
            Assert.AreEqual("gathering_wood", _taskChanged.Messages[0].TaskId);
        }

        /// <summary>
        /// Minimal IPublisher&lt;T&gt; over a list — same pattern as
        /// BuildingPlacementTests; the real MessagePipe broker needs a full container.
        /// </summary>
        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public readonly List<T> Messages = new List<T>();
            public void Publish(T message) => Messages.Add(message);
        }
    }
}
