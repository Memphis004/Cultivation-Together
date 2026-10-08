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
    /// P4 — local ownership harness acceptance (ผ่าน Unity-side service จริง:
    /// SectStateProvider.TrySetDiscipleOwner + TryAssignTask — ไม่ใช่ปุ่ม UI):
    ///   - legitimate ownership: d001 → Viewer viewer_test_01, then that identity
    ///     can assign ONLY its own disciple via the real TryAssignTask gate
    ///   - another viewer's rejection (viewer_test_02 ≠ viewer_test_01)
    ///   - empty requester rejected by TryAssignTask (fail-closed)
    ///   - undefined enum value rejected, state unchanged
    ///   - Npc normalizes OwnerId to empty; invalid Npc-with-id rejected
    ///   - identity convention enforced; duplicate Viewer binding rejected
    ///   - DiscipleOwnerChangedMessage: published once per real change only,
    ///     never on no-op/rejection; inventory OwnerScope untouched
    /// MockSectData stays unmodified — viewer_test_01 is assigned in fixtures.
    /// ⚠️ viewer_test_01/02 are SYNTHETIC ids — never production Twitch auth.
    /// </summary>
    public class OwnershipHarnessTests
    {
        private SectStateProvider _provider;
        private BufferPublisher<DiscipleOwnerChangedMessage> _ownerChanged;
        private BufferPublisher<DiscipleTaskChangedMessage> _taskChanged;

        [SetUp]
        public void SetUp()
        {
            _ownerChanged = new BufferPublisher<DiscipleOwnerChangedMessage>();
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
                _ownerChanged);
        }

        private SectEconomyState State => _provider.BuildSectEconomyState();

        private DiscipleState Find(string id)
        {
            foreach (var d in State.Disciples)
                if (d.DiscipleId == id) return d;
            return null;
        }

        private string DumpOwner(string id)
        {
            var d = Find(id);
            return d != null ? d.OwnerType + ":" + d.OwnerId : "<missing>";
        }

        // ---- legitimate ownership via the harness path (fixture = harness call) ----

        [Test]
        public void Harness_AssignsViewer_test_01_AndThatViewerControlsOwnDisciple()
        {
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer,
                                                          "viewer_test_01", out var reason), reason);
            Assert.AreEqual(DiscipleOwnerType.Viewer, Find("d001").OwnerType);
            Assert.AreEqual("viewer_test_01", Find("d001").OwnerId);

            // the REAL service gate: that viewer can reassign its own disciple
            Assert.IsTrue(_provider.TryAssignTask("viewer_test_01", "d001", "gathering_wood", out reason), reason);
            Assert.AreEqual("gathering_wood", Find("d001").CurrentTask);
        }

        // ---- another viewer's rejection ----

        [Test]
        public void TryAssignTask_AnotherViewer_Rejected()
        {
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer,
                                                         "viewer_test_01", out _));

            // viewer_test_02 ≠ viewer_test_01 — the real gate must fail-closed
            Assert.IsFalse(_provider.TryAssignTask("viewer_test_02", "d001", "gathering_ore", out var reason));
            StringAssert.Contains("not allowed", reason);
            Assert.AreEqual("gathering_herb", Find("d001").CurrentTask, "rejection leaves CurrentTask");
            Assert.AreEqual(0, _taskChanged.Messages.Count);
        }

        // ---- empty requester rejection ----

        [Test]
        public void TryAssignTask_EmptyRequester_Rejected()
        {
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer,
                                                         "viewer_test_01", out _));

            Assert.IsFalse(_provider.TryAssignTask("", "d001", "gathering_ore", out _));
            Assert.AreEqual("gathering_herb", Find("d001").CurrentTask, "empty requester changes nothing");
        }

        // ---- undefined enum value ----

        [Test]
        public void TrySetDiscipleOwner_UndefinedEnum_Rejected_StateUnchanged()
        {
            var undefined = (DiscipleOwnerType)999;
            Assert.IsFalse(_provider.TrySetDiscipleOwner("d001", undefined, "", out var reason));
            StringAssert.Contains("Undefined", reason);
            Assert.AreEqual(DiscipleOwnerType.Npc, Find("d001").OwnerType, "state unchanged");
            Assert.AreEqual(string.Empty, Find("d001").OwnerId, "state unchanged");
            Assert.AreEqual(0, _ownerChanged.Messages.Count, "no message on rejection");
        }

        // ---- Npc normalization + invalid Npc ----

        [Test]
        public void TrySetDiscipleOwner_Npc_NormalizesOwnerIdToEmpty()
        {
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer,
                                                         "viewer_test_01", out _));
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Npc, "", out _));
            Assert.AreEqual(DiscipleOwnerType.Npc, Find("d001").OwnerType);
            Assert.AreEqual(string.Empty, Find("d001").OwnerId, "Npc always carries an empty OwnerId");
        }

        [Test]
        public void TrySetDiscipleOwner_NpcWithOwnerId_Rejected()
        {
            Assert.IsFalse(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Npc, "some_id", out var reason));
            StringAssert.Contains("empty OwnerId", reason);
            Assert.AreEqual(DiscipleOwnerType.Npc, Find("d001").OwnerType, "state unchanged");
        }

        // ---- identity convention ----

        [TestCase("viewer_test_01", true)]
        [TestCase("player_main", true)]
        [TestCase("", false)]
        [TestCase("   viewer_test_01   ", true)]  // trimmed before validation
        [TestCase("has space", false)]
        [TestCase("1starts_with_digit", false)]
        [TestCase("viewer test", false)]
        public void TrySetDiscipleOwner_IdentityConvention_Enforced(string ownerId, bool shouldPass)
        {
            var ok = _provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer, ownerId, out var reason);
            Assert.AreEqual(shouldPass, ok, $"OwnerId '{ownerId}': {reason}");
            if (!shouldPass)
                Assert.AreEqual(DiscipleOwnerType.Npc, Find("d001").OwnerType, "invalid change leaves state unchanged");
        }

        // ---- duplicate viewer binding ----

        [Test]
        public void TrySetDiscipleOwner_DuplicateViewerBinding_Rejected()
        {
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer,
                                                         "viewer_test_01", out _));

            // same viewer id on a second disciple must fail — one identity, one disciple
            Assert.IsFalse(_provider.TrySetDiscipleOwner("d002", DiscipleOwnerType.Viewer,
                                                          "viewer_test_01", out var reason));
            StringAssert.Contains("already controls", reason);
            StringAssert.Contains("d001", reason);
            Assert.AreEqual("Npc:", DumpOwner("d002").Substring(0, 4), "d002 unchanged");
        }

        [Test]
        public void TrySetDiscipleOwner_RebindingSameViewer_MovesOffTheOldDisciple()
        {
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer,
                                                         "viewer_test_01", out _));
            // explicit release first, then rebind — the only legal path
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Npc, "", out _));
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d002", DiscipleOwnerType.Viewer,
                                                         "viewer_test_01", out _));
            Assert.AreEqual("Npc:", DumpOwner("d001").Substring(0, 4));
            Assert.AreEqual("viewer_test_01", Find("d002").OwnerId);
        }

        // ---- message rules ----

        [Test]
        public void OwnerChangedMessage_PublishedOncePerRealChange_OnlyOnSuccess()
        {
            _ownerChanged.Messages.Clear();

            // real change → exactly one message with old/new values
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer,
                                                         "viewer_test_01", out _));
            Assert.AreEqual(1, _ownerChanged.Messages.Count);
            var msg = _ownerChanged.Messages[0];
            Assert.AreEqual("d001", msg.DiscipleId);
            Assert.AreEqual(DiscipleOwnerType.Npc, msg.OldType);
            Assert.AreEqual(string.Empty, msg.OldOwnerId);
            Assert.AreEqual(DiscipleOwnerType.Viewer, msg.NewType);
            Assert.AreEqual("viewer_test_01", msg.NewOwnerId);

            // no-op (same values) → no message
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer,
                                                         "viewer_test_01", out _));
            Assert.AreEqual(1, _ownerChanged.Messages.Count, "no-op publishes nothing");

            // rejection → no message
            Assert.IsFalse(_provider.TrySetDiscipleOwner("nope", DiscipleOwnerType.Viewer,
                                                          "viewer_test_02", out _));
            Assert.IsFalse(_provider.TrySetDiscipleOwner("d002", DiscipleOwnerType.Viewer,
                                                          "viewer_test_01", out _));
            Assert.AreEqual(1, _ownerChanged.Messages.Count, "rejections publish nothing");
        }

        // ---- scope guard: inventory OwnerScope untouched ----

        [Test]
        public void OwnershipChange_DoesNotTouchInventoryOwnerScope()
        {
            // d003 carries a Personal sword in the mock start state
            var d003 = Find("d003");
            Assert.Greater(d003.PersonalInventory.Count, 0, "precondition: d003 has an inventory item");
            var scopeBefore = d003.PersonalInventory[0].OwnerScope;

            Assert.IsTrue(_provider.TrySetDiscipleOwner("d003", DiscipleOwnerType.Viewer,
                                                         "viewer_test_01", out _));
            Assert.AreEqual(scopeBefore, d003.PersonalInventory[0].OwnerScope,
                            "ownership change must not modify inventory OwnerScope");
        }

        // ---- unknown disciple ----

        [Test]
        public void TrySetDiscipleOwner_UnknownDisciple_Rejected()
        {
            Assert.IsFalse(_provider.TrySetDiscipleOwner("d999", DiscipleOwnerType.Viewer,
                                                          "viewer_test_01", out var reason));
            StringAssert.Contains("No disciple", reason);
            Assert.AreEqual(0, _ownerChanged.Messages.Count);
        }

        // ---- P4 observability: ring buffer + read-only query (bridge get_ownership_log) ----

        [Test]
        public void OwnershipBuffer_RecordsEachRealChange_ThroughTheSubscription()
        {
            var subscriber = new FakeSubscriber<DiscipleOwnerChangedMessage>();
            var buffer = new OwnershipObservabilityBuffer(subscriber);

            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer,
                                                         "viewer_test_01", out _));
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d002", DiscipleOwnerType.Viewer,
                                                         "viewer_test_02", out _));
            ForwardPublishedToBuffer(buffer);

            var snapshot = buffer.Snapshot();
            Assert.AreEqual(2, snapshot.Length, "one entry per real change");
            Assert.AreEqual("d001", snapshot[0].DiscipleId);
            Assert.AreEqual("viewer_test_01", snapshot[0].NewOwnerId);
            Assert.AreEqual("d002", snapshot[1].DiscipleId);
            Assert.AreEqual("viewer_test_02", snapshot[1].NewOwnerId);
        }

        [Test]
        public void OwnershipBuffer_NoOpOrRejection_RecordedNothing()
        {
            var subscriber = new FakeSubscriber<DiscipleOwnerChangedMessage>();
            var buffer = new OwnershipObservabilityBuffer(subscriber);

            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer,
                                                         "viewer_test_01", out _));
            ForwardPublishedToBuffer(buffer);

            // no-op (same values) and both rejection paths publish nothing
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer,
                                                         "viewer_test_01", out _));
            Assert.IsFalse(_provider.TrySetDiscipleOwner("d001", (DiscipleOwnerType)999, "", out _));
            Assert.IsFalse(_provider.TrySetDiscipleOwner("d002", DiscipleOwnerType.Viewer,
                                                          "viewer_test_01", out _));
            ForwardPublishedToBuffer(buffer);

            Assert.AreEqual(1, buffer.Snapshot().Length, "log stays at the one real change");
        }

        [Test]
        public void OwnershipBuffer_CapsAtMaxEntries_OldestDroppedFirst()
        {
            var subscriber = new FakeSubscriber<DiscipleOwnerChangedMessage>();
            var buffer = new OwnershipObservabilityBuffer(subscriber);

            // max+5 real changes (Npc → Viewer flip on the same disciple is always a real change)
            for (int i = 0; i < OwnershipObservabilityBuffer.MaxEntries + 5; i++)
            {
                var toViewer = i % 2 == 0;
                Assert.IsTrue(_provider.TrySetDiscipleOwner("d001",
                    toViewer ? DiscipleOwnerType.Viewer : DiscipleOwnerType.Npc,
                    toViewer ? "viewer_test_01" : "", out var reason), reason);
                ForwardPublishedToBuffer(buffer);
            }

            var snapshot = buffer.Snapshot();
            Assert.AreEqual(OwnershipObservabilityBuffer.MaxEntries, snapshot.Length,
                            "ring buffer is bounded");
            // 105 alternating changes, cap 100 → changes #6..#105 survive; #6 was
            // the Viewer→Npc flip of iteration i=5, so its OldType is Viewer.
            Assert.AreEqual(DiscipleOwnerType.Viewer, snapshot[0].OldType, "oldest entries dropped first");
            Assert.AreEqual(DiscipleOwnerType.Npc, snapshot[0].NewType);
            Assert.AreEqual(DiscipleOwnerType.Viewer, snapshot[snapshot.Length - 1].NewType,
                            "newest entry is the last iteration (i=104 → Viewer)");
        }

        [Test]
        public void OwnershipObservabilityHandler_ReturnsSnapshotWithRequestId()
        {
            var subscriber = new FakeSubscriber<DiscipleOwnerChangedMessage>();
            var buffer = new OwnershipObservabilityBuffer(subscriber);
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer,
                                                         "viewer_test_01", out _));
            ForwardPublishedToBuffer(buffer);

            var handler = new OwnershipObservabilityHandler(buffer);
            var response = handler.InvokeAsync(new OwnershipObservabilityQuery
            {
                RequestId = "req-42",
            }).GetAwaiter().GetResult();

            Assert.AreEqual("req-42", response.RequestId);
            Assert.AreEqual(1, response.Events.Count);
            Assert.AreEqual("d001", response.Events[0].DiscipleId);
        }

        // ---- wiring guards (source-scan convention, CompositionRootTests) ----

        [Test]
        public void ObservabilityWiring_RegisteredAtRootScope()
        {
            var interprocess = ReadSource("Assets/Scripts/Core/Installers/InterprocessInstaller.cs");
            StringAssert.Contains("RegisterTcpRemoteRequestHandler<OwnershipObservabilityQuery", interprocess,
                "query pair must be wired to the TCP worker");
            StringAssert.Contains("RegisterAsyncRequestHandler<OwnershipObservabilityQuery", interprocess,
                "handler must be registered (missing either fails silently at runtime)");

            var gameplay = ReadSource("Assets/Scripts/Core/Installers/GameplayInstaller.cs");
            StringAssert.Contains("RegisterEntryPoint<OwnershipObservabilityBuffer>", gameplay,
                "buffer must be an entry point (activated at build — a plain Register would miss changes until first query)");
        }

        [Test]
        public void NoMcpToolExposesOwnershipAssignment()
        {
            var program = ReadSource("../McpBridge/Program.cs");
            StringAssert.Contains("GetOwnershipLog", program,
                "observability tool must exist");
            Assert.IsFalse(program.Contains("TrySetDiscipleOwner"),
                "bridge must have NO path to TrySetDiscipleOwner — assignment stays dev-harness-only");
        }

        private void ForwardPublishedToBuffer(OwnershipObservabilityBuffer buffer)
        {
            // In production MessagePipe connects IPublisher→ISubscriber; in these
            // tests the BufferPublisher just collects, so forward explicitly.
            foreach (var m in _ownerChanged.Messages) buffer.Handle(m);
            _ownerChanged.Messages.Clear();
        }

        private static string ReadSource(string relativePath)
        {
            return System.IO.File.ReadAllText(System.IO.Path.Combine(
                UnityEngine.Application.dataPath + "/..", relativePath));
        }

        /// <summary>Minimal IPublisher over a list — same pattern as MockStartStateTests.</summary>
        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public readonly List<T> Messages = new List<T>();
            public void Publish(T message) => Messages.Add(message);
        }

        /// <summary>
        /// MessagePipe's real shape: IDisposable Subscribe(IMessageHandler&lt;T&gt;, ...filters).
        /// Captures the handler so the test can drive it exactly like the real bus.
        /// </summary>
        private sealed class FakeSubscriber<T> : ISubscriber<T>
        {
            private IMessageHandler<T> _handler;

            public IDisposable Subscribe(IMessageHandler<T> handler, params MessageHandlerFilter<T>[] filters)
            {
                _handler = handler;
                return new SubscriptionStub();
            }

            public void Publish(T message) => _handler.Handle(message);

            private sealed class SubscriptionStub : IDisposable
            {
                public void Dispose() { }
            }
        }
    }
}
