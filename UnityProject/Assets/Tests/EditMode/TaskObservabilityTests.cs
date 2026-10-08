using System;
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
    /// P5B observability — the bridge's two READ-ONLY task queries:
    ///   - get_task_change_log: ring buffer over DiscipleTaskChangedMessage
    ///     (one entry per real change; no-op/rejection logs nothing; bounded)
    ///   - get_task_protection: one row per Viewer-owned disciple with protection,
    ///     remaining time, membership consistency and who may change the task
    /// Both compute through the authority (CheckTaskPermission) — nothing here
    /// ever mutates state, and no new write path is added to the bridge.
    /// </summary>
    public class TaskObservabilityTests
    {
        private SectStateProvider _provider;
        private MutableClock _clock;
        private BufferPublisher<DiscipleTaskChangedMessage> _taskChanged;

        private static readonly DateTime T0 = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

        [SetUp]
        public void SetUp()
        {
            _clock = new MutableClock(T0);
            _taskChanged = new BufferPublisher<DiscipleTaskChangedMessage>();

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
                _taskChanged,
                new BufferPublisher<DiscipleOwnerChangedMessage>(),
                _clock);

            // The start state is building-free, so the gathering_herb changes below
            // need their own herb_plot (state presence is all the requirement gate checks).
            string reason;
            PlacedBuildingState placed;
            Assert.IsTrue(_provider.TryPlaceBuilding("herb_plot", 0, 0, 0, new BuildingGrid(10, 10),
                                                     out reason, out placed), reason);
        }

        private SectEconomyState State => _provider.BuildSectEconomyState();

        private DiscipleState Find(string id)
        {
            foreach (var d in State.Disciples)
                if (d.DiscipleId == id) return d;
            return null;
        }

        // ---- task-change log ----

        [Test]
        public void TaskChangeBuffer_RecordsEachRealChange_InOrder()
        {
            var subscriber = new FakeSubscriber<DiscipleTaskChangedMessage>();
            var buffer = new TaskChangeObservabilityBuffer(subscriber);

            string reason;
            Assert.IsTrue(_provider.TryAssignTask(SectStateProvider.SectMasterRequesterId, "d000", "gathering_wood", out reason), reason);
            _clock.UtcNow = T0.AddSeconds(20); // clear the cooldown for the next real change
            Assert.IsTrue(_provider.TryAssignTask(SectStateProvider.SectMasterRequesterId, "d002", "gathering_herb", out reason), reason);
            ForwardPublishedToBuffer(buffer);

            var snapshot = buffer.Snapshot();
            Assert.AreEqual(2, snapshot.Length, "one entry per real change");
            Assert.AreEqual("d000", snapshot[0].DiscipleId);
            Assert.AreEqual("gathering_wood", snapshot[0].TaskId);
            Assert.AreEqual("d002", snapshot[1].DiscipleId);
            Assert.AreEqual("gathering_herb", snapshot[1].TaskId);
        }

        [Test]
        public void TaskChangeBuffer_NoOpOrRejection_RecordsNothing()
        {
            var subscriber = new FakeSubscriber<DiscipleTaskChangedMessage>();
            var buffer = new TaskChangeObservabilityBuffer(subscriber);

            string reason;
            Assert.IsTrue(_provider.TryAssignTask(SectStateProvider.SectMasterRequesterId, "d000", "gathering_wood", out reason), reason);
            ForwardPublishedToBuffer(buffer);

            // no-op (same task) and a rejection both publish nothing
            Assert.IsTrue(_provider.TryAssignTask(SectStateProvider.SectMasterRequesterId, "d000", "gathering_wood", out reason), reason);
            Assert.IsFalse(_provider.TryAssignTask("viewer_ghost", "d001", "gathering_wood", out reason));
            ForwardPublishedToBuffer(buffer);

            Assert.AreEqual(1, buffer.Snapshot().Length, "log stays at the one real change");
        }

        [Test]
        public void TaskChangeBuffer_CapsAtMaxEntries_OldestDroppedFirst()
        {
            var subscriber = new FakeSubscriber<DiscipleTaskChangedMessage>();
            var buffer = new TaskChangeObservabilityBuffer(subscriber);

            string reason;
            for (int i = 0; i < TaskChangeObservabilityBuffer.MaxEntries + 5; i++)
            {
                _clock.UtcNow = T0.AddSeconds(i * 20); // past the 12s cooldown each time
                var task = i % 2 == 0 ? "gathering_wood" : "gathering_herb";
                Assert.IsTrue(_provider.TryAssignTask(SectStateProvider.SectMasterRequesterId, "d000", task, out reason), reason);
                ForwardPublishedToBuffer(buffer);
            }

            var snapshot = buffer.Snapshot();
            Assert.AreEqual(TaskChangeObservabilityBuffer.MaxEntries, snapshot.Length, "ring buffer is bounded");
            Assert.AreEqual("gathering_herb", snapshot[0].TaskId, "oldest entries dropped first");
        }

        [Test]
        public void TaskChangeHandler_ReturnsSnapshotWithRequestId()
        {
            var subscriber = new FakeSubscriber<DiscipleTaskChangedMessage>();
            var buffer = new TaskChangeObservabilityBuffer(subscriber);

            string reason;
            Assert.IsTrue(_provider.TryAssignTask(SectStateProvider.SectMasterRequesterId, "d000", "gathering_wood", out reason), reason);
            ForwardPublishedToBuffer(buffer);

            var handler = new TaskChangeObservabilityHandler(buffer);
            var response = handler.InvokeAsync(new TaskChangeObservabilityQuery { RequestId = "req-7" })
                                  .GetAwaiter().GetResult();

            Assert.AreEqual("req-7", response.RequestId);
            Assert.AreEqual(1, response.Events.Count);
            Assert.AreEqual("d000", response.Events[0].DiscipleId);
        }

        // ---- protection snapshot ----

        [Test]
        public void ProtectionHandler_SkipsNonViewerDisciples()
        {
            var handler = new TaskProtectionHandler(_provider);
            var response = handler.InvokeAsync(new TaskProtectionQuery { RequestId = "p1" })
                                  .GetAwaiter().GetResult();

            Assert.AreEqual("p1", response.RequestId);
            Assert.AreEqual(0, response.Entries.Count, "mock start is all Npc — no protection rows");
        }

        [Test]
        public void ProtectionHandler_ActiveViewer_Protected_MasterBlocked_OwnerAllowed()
        {
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer, "viewer_test_01", out var bindReason), bindReason);
            _clock.UtcNow = T0.AddSeconds(60);

            var handler = new TaskProtectionHandler(_provider);
            var response = handler.InvokeAsync(new TaskProtectionQuery()).GetAwaiter().GetResult();

            Assert.AreEqual(1, response.Entries.Count);
            var e = response.Entries[0];
            Assert.AreEqual("d001", e.DiscipleId);
            Assert.AreEqual(DiscipleOwnerType.Viewer, e.OwnerType);
            Assert.AreEqual("viewer_test_01", e.OwnerId);
            Assert.AreEqual("meditation", e.CurrentTask, "d001's start task (no herb_plot in the start state)");
            Assert.IsTrue(e.OwnerProtected);
            Assert.AreEqual(540f, e.OwnerProtectionRemainingSeconds, 0.5f, "600 - 60 elapsed");
            Assert.IsTrue(e.MembershipConsistent);
            Assert.IsFalse(e.SectMasterMayChange, "owner still inside the protection window");
            Assert.IsTrue(e.OwnerMayChange, "the owner may always control its own disciple");
            Assert.AreEqual(T0, e.LastActiveAtUtc);
        }

        [Test]
        public void ProtectionHandler_PastWindow_MasterMayChange()
        {
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer, "viewer_test_01", out var bindReason), bindReason);
            _clock.UtcNow = T0.AddMinutes(11);

            var handler = new TaskProtectionHandler(_provider);
            var e = handler.InvokeAsync(new TaskProtectionQuery()).GetAwaiter().GetResult().Entries[0];

            Assert.IsFalse(e.OwnerProtected);
            Assert.IsTrue(e.SectMasterMayChange);
            Assert.IsTrue(e.OwnerMayChange);
        }

        [Test]
        public void ProtectionHandler_MissingMembership_FlagsInconsistency_BlocksEveryone()
        {
            // fixture writes a Viewer-owned row with no membership record
            Find("d001").OwnerType = DiscipleOwnerType.Viewer;
            Find("d001").OwnerId = "viewer_ghost";

            var handler = new TaskProtectionHandler(_provider);
            var e = handler.InvokeAsync(new TaskProtectionQuery()).GetAwaiter().GetResult().Entries[0];

            Assert.IsFalse(e.MembershipConsistent, "missing membership must be reported, not hidden");
            Assert.IsFalse(e.SectMasterMayChange, "consistency error never grants permission");
            Assert.IsFalse(e.OwnerMayChange);
            Assert.AreEqual(DateTime.MinValue, e.LastActiveAtUtc);
        }

        [Test]
        public void ProtectionHandler_IsReadOnly()
        {
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer, "viewer_test_01", out var bindReason), bindReason);
            var activeBefore = State.ViewerRegistry.Find("viewer_test_01").LastActiveAtUtc;
            var taskBefore = Find("d001").CurrentTask;

            _clock.UtcNow = T0.AddSeconds(60);
            new TaskProtectionHandler(_provider).InvokeAsync(new TaskProtectionQuery()).GetAwaiter().GetResult();

            Assert.AreEqual(activeBefore, State.ViewerRegistry.Find("viewer_test_01").LastActiveAtUtc,
                "a read query must not refresh activity");
            Assert.AreEqual(taskBefore, Find("d001").CurrentTask);
        }

        // ---- wiring guards (source-scan convention, CompositionRootTests) ----

        [Test]
        public void ObservabilityWiring_RegisteredAtRootScope()
        {
            var interprocess = ReadSource("Assets/Scripts/Core/Installers/InterprocessInstaller.cs");
            StringAssert.Contains("RegisterTcpRemoteRequestHandler<TaskChangeObservabilityQuery", interprocess);
            StringAssert.Contains("RegisterAsyncRequestHandler<TaskChangeObservabilityQuery", interprocess);
            StringAssert.Contains("RegisterTcpRemoteRequestHandler<TaskProtectionQuery", interprocess);
            StringAssert.Contains("RegisterAsyncRequestHandler<TaskProtectionQuery", interprocess);

            var gameplay = ReadSource("Assets/Scripts/Core/Installers/GameplayInstaller.cs");
            StringAssert.Contains("RegisterEntryPoint<TaskChangeObservabilityBuffer>", gameplay,
                "buffer must be an entry point or it misses changes until the first query");
        }

        [Test]
        public void BridgeTools_AreReadOnly_AndLiveInQueryTools()
        {
            var program = ReadSource("../McpBridge/Program.cs");

            int queryTools = program.IndexOf("class SectQueryTools", StringComparison.Ordinal);
            int actionTools = program.IndexOf("class SectActionTools", StringComparison.Ordinal);
            Assert.Greater(queryTools, -1);
            Assert.Greater(actionTools, queryTools, "read/write split type must still exist");

            int changeLog = program.IndexOf("GetTaskChangeLog", StringComparison.Ordinal);
            int protection = program.IndexOf("GetTaskProtection", StringComparison.Ordinal);
            Assert.Greater(changeLog, queryTools);
            Assert.Less(changeLog, actionTools, "task-change log tool must be a READ tool");
            Assert.Greater(protection, queryTools);
            Assert.Less(protection, actionTools, "protection tool must be a READ tool");

            // neither read tool may take a publisher (a write path would need one)
            var querySection = program.Substring(queryTools, actionTools - queryTools);
            Assert.IsFalse(querySection.Contains("IDistributedPublisher"),
                "query tools must not publish anything");
        }

        private void ForwardPublishedToBuffer(TaskChangeObservabilityBuffer buffer)
        {
            foreach (var m in _taskChanged.Messages) buffer.Handle(m);
            _taskChanged.Messages.Clear();
        }

        private static string ReadSource(string relativePath)
        {
            return System.IO.File.ReadAllText(System.IO.Path.Combine(
                UnityEngine.Application.dataPath + "/..", relativePath));
        }

        private sealed class MutableClock : IClock
        {
            public MutableClock(DateTime now) { UtcNow = now; }
            public DateTime UtcNow { get; set; }
        }

        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public readonly List<T> Messages = new List<T>();
            public void Publish(T message) => Messages.Add(message);
        }

        private sealed class FakeSubscriber<T> : ISubscriber<T>
        {
            private IMessageHandler<T> _handler;
            public IDisposable Subscribe(IMessageHandler<T> handler, params MessageHandlerFilter<T>[] filters)
            {
                _handler = handler;
                return new SubscriptionStub();
            }
            public void Publish(T message) => _handler.Handle(message);
            private sealed class SubscriptionStub : IDisposable { public void Dispose() { } }
        }
    }
}
