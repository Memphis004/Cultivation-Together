using System;
using System.Collections.Generic;
using System.IO;
using MessagePipe;
using NUnit.Framework;
using UnityEngine;
using Xianxia.Sect;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// P11A — the full-session snapshot contract + atomic in-memory restore.
    /// Covers: deep snapshot isolation, round-trip (roster/economy/buildings/
    /// appearance/attributes/ownership/membership/Manual-Auto/simulation state),
    /// malformed-data rejection, version handling, unique ids, membership
    /// invariants, work-progress retention, and no duplicate rewards on the first
    /// post-load tick. No disk I/O or Title UI is exercised (out of scope for P11A).
    /// </summary>
    public class SessionSnapshotTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

        private MutableClock _clock;
        private SectStateProvider _provider;
        private TimeSystem _timeSystem;
        private BufferPublisher<SessionRestoredMessage> _restored;
        private SessionSnapshotService _service;

        [SetUp]
        public void SetUp()
        {
            _clock = new MutableClock(T0);
            _provider = NewProvider(_clock);
            _timeSystem = NewTimeSystem();
            _restored = new BufferPublisher<SessionRestoredMessage>();
            _service = new SessionSnapshotService(_provider, _timeSystem, _restored, _clock);
        }

        // ── capture isolation ────────────────────────────────────────────────

        [Test]
        public void Capture_IsDeeplyDetached_BothDirections()
        {
            var snapshot = _service.CaptureSnapshot();

            // mutating the snapshot must not touch live state
            snapshot.Economy.Disciples[0].DisplayName = "MUTATED";
            snapshot.Economy.Disciples[0].Avatar.SetSlot(AvatarSlots.Hair, "hair_mutated");
            snapshot.Economy.Disciples[0].Attributes.SkillXp[DiscipleAttributesConfig.CategoryGathering] = 999f;
            snapshot.Economy.Stockpile.RawResources["herb"] = 9999;

            var live = _provider.BuildSectEconomyState();
            Assert.AreNotEqual("MUTATED", live.Disciples[0].DisplayName);
            Assert.AreEqual("hair_topknot_long", live.Disciples[0].Avatar.GetSlot(AvatarSlots.Hair));
            Assert.AreEqual(9999, snapshot.Economy.Stockpile.RawResources["herb"]);
            Assert.AreEqual(120, live.Stockpile.RawResources["herb"]);

            // …and mutating live state must not touch the snapshot
            live.Stockpile.RawResources["herb"] = 7;
            live.Disciples[0].DisplayName = "LIVE";
            Assert.AreEqual(9999, snapshot.Economy.Stockpile.RawResources["herb"]);
            Assert.AreEqual("MUTATED", snapshot.Economy.Disciples[0].DisplayName);
        }

        [Test]
        public void Capture_IncludesWorkAccumulators_Detached()
        {
            Assert.IsTrue(_provider.TryAssignTask(SectStateProvider.SectMasterRequesterId, "d001", "gathering_wood", out var reason), reason);
            _provider.TickGathering(0.5f); // 0.2/s → 0.1 remainder

            var snapshot = _service.CaptureSnapshot();
            Assert.IsTrue(snapshot.GatherAccumulators.ContainsKey("gathering_wood"));
            Assert.AreEqual(0.1f, snapshot.GatherAccumulators["gathering_wood"], 0.0001f);

            snapshot.GatherAccumulators["gathering_wood"] = 5f;
            var live = _service.CaptureSnapshot(); // re-capture from live
            Assert.AreEqual(0.1f, live.GatherAccumulators["gathering_wood"], 0.0001f,
                "mutating one snapshot's accumulators must not touch live state");
        }

        [Test]
        public void Capture_IncludesSimulationSpeedAndPendingDecision()
        {
            _timeSystem.SetSpeed(3);
            _timeSystem.RaiseWorldEvent("bandit_raid_001", "raiders", true,
                new List<EventChoiceInfo> { new EventChoiceInfo { ChoiceId = "fight", Label = "Fight" } });

            var snapshot = _service.CaptureSnapshot();
            Assert.AreEqual(3, snapshot.SimulationSpeed);
            Assert.IsTrue(snapshot.HasPendingDecision);
            Assert.AreEqual("bandit_raid_001", snapshot.PendingEventId);
            Assert.AreEqual(1, snapshot.PendingChoices.Count);
            Assert.IsTrue(snapshot.PendingDecisionPaused);
        }

        // ── round trip ───────────────────────────────────────────────────────

        [Test]
        public void RoundTrip_RestoresEverything_AndPublishesOnce()
        {
            // session A: gathering progress + owner + speed + pending decision.
            // Assign the task while d001 is still Npc-owned — a freshly-bound viewer
            // owner is protected from the SectMaster override for the full window.
            Assert.IsTrue(_provider.TryAssignTask(SectStateProvider.SectMasterRequesterId, "d001", "gathering_wood", out var taskReason), taskReason);
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer, "viewer_test_01", out var ownerReason), ownerReason);
            _provider.TickGathering(0.5f);
            _timeSystem.SetSpeed(2);
            _timeSystem.RaiseWorldEvent("bandit_raid_001", "raiders", true,
                new List<EventChoiceInfo> { new EventChoiceInfo { ChoiceId = "fight", Label = "Fight" } });

            var envelope = _service.CaptureEnvelope();
            Assert.AreEqual(SectSessionSnapshotEnvelope.CurrentVersion, envelope.Version);

            // session B: brand-new provider/time, mock start
            var nextProvider = NewProvider(new MutableClock(T0.AddDays(1)));
            var nextTime = NewTimeSystem();
            var nextRestored = new BufferPublisher<SessionRestoredMessage>();
            var nextService = new SessionSnapshotService(nextProvider, nextTime, nextRestored, new MutableClock(T0.AddDays(1)));

            Assert.IsTrue(nextService.TryRestoreEnvelope(envelope, out var fail), fail);

            // roster / task / accumulated progress
            var d001 = Find(nextProvider, "d001");
            Assert.AreEqual("gathering_wood", d001.CurrentTask);
            var restoredGather = nextService.CaptureSnapshot();
            Assert.AreEqual(0.1f, restoredGather.GatherAccumulators["gathering_wood"], 0.0001f);

            // ownership + membership restored together
            Assert.AreEqual(DiscipleOwnerType.Viewer, d001.OwnerType);
            Assert.AreEqual("viewer_test_01", d001.OwnerId);
            var record = nextProvider.BuildSectEconomyState().ViewerRegistry.Find("viewer_test_01");
            Assert.IsNotNull(record);
            Assert.AreEqual(ViewerMembershipStatus.Active, record.Status);
            Assert.AreEqual("d001", record.BoundDiscipleId);

            // simulation state
            Assert.AreEqual(2, nextTime.Speed);
            Assert.IsTrue(nextTime.HasPendingDecision);
            Assert.AreEqual("bandit_raid_001", nextTime.PendingEventId);
            Assert.IsTrue(nextTime.IsPendingDecisionPaused);

            // exactly one coherent notification, after commit
            Assert.AreEqual(1, nextRestored.Messages.Count);
            Assert.IsTrue(nextRestored.Messages[0].FullSession);
            Assert.AreEqual(nextProvider.BuildSectEconomyState().Disciples.Count, nextRestored.Messages[0].DiscipleCount);
            Assert.IsTrue(nextService.HasFullSessionAuthority);
        }

        [Test]
        public void Restore_DoesNotAliasTheCallersSnapshot()
        {
            var snapshot = _service.CaptureSnapshot();

            var nextProvider = NewProvider(new MutableClock(T0));
            var nextService = new SessionSnapshotService(nextProvider, NewTimeSystem(),
                new BufferPublisher<SessionRestoredMessage>(), new MutableClock(T0));
            Assert.IsTrue(nextService.TryRestore(snapshot, out var fail), fail);

            // mutating the object the caller still holds must not touch the restored session
            snapshot.Economy.Disciples[0].DisplayName = "MUTATED";
            Assert.AreNotEqual("MUTATED", Find(nextProvider, "d000").DisplayName);
        }

        // ── validation: fail closed, live state untouched ────────────────────

        [Test]
        public void Restore_UnknownTask_Rejected_NoSubstitution()
        {
            var snapshot = _service.CaptureSnapshot();
            snapshot.Economy.Disciples[0].CurrentTask = "gathering_lunar_herb";

            Assert.IsFalse(_service.TryRestore(snapshot, out var fail));
            StringAssert.Contains("unknown task id", fail);
            Assert.AreEqual("meditation", Find(_provider, "d000").CurrentTask, "live task must stay unchanged");
            Assert.AreEqual(0, _restored.Messages.Count, "a failed restore publishes nothing");
        }

        [Test]
        public void Restore_NonFiniteAttribute_Rejected()
        {
            var snapshot = _service.CaptureSnapshot();
            snapshot.Economy.Disciples[0].Attributes.Stamina = float.NaN;

            Assert.IsFalse(_service.TryRestore(snapshot, out var fail));
            StringAssert.Contains("non-finite", fail);
            Assert.AreEqual(100f, _provider.BuildSectEconomyState().Disciples[0].Attributes.Stamina, 0.0001f);
        }

        [Test]
        public void Restore_DuplicateDiscipleId_Rejected()
        {
            var snapshot = _service.CaptureSnapshot();
            snapshot.Economy.Disciples[1].DiscipleId = snapshot.Economy.Disciples[0].DiscipleId;

            Assert.IsFalse(_service.TryRestore(snapshot, out var fail));
            StringAssert.Contains("duplicate disciple id", fail);
        }

        [Test]
        public void Restore_DuplicateBuildingId_Rejected()
        {
            var snapshot = _service.CaptureSnapshot();
            snapshot.Economy.PlacedBuildings.Add(new PlacedBuildingState { InstanceId = "b001", DefId = "pill_hall", GridX = 0, GridZ = 0, Rotation = 0 });
            snapshot.Economy.PlacedBuildings.Add(new PlacedBuildingState { InstanceId = "b001", DefId = "pill_hall", GridX = 6, GridZ = 0, Rotation = 0 });

            Assert.IsFalse(_service.TryRestore(snapshot, out var fail));
            StringAssert.Contains("duplicate building instance id", fail);
            Assert.AreEqual(0, _provider.BuildSectEconomyState().PlacedBuildings.Count);
        }

        [Test]
        public void Restore_UnknownBuildingDef_Rejected()
        {
            var snapshot = _service.CaptureSnapshot();
            snapshot.Economy.PlacedBuildings.Add(new PlacedBuildingState { InstanceId = "b001", DefId = "nonexistent_hall", GridX = 0, GridZ = 0, Rotation = 0 });

            Assert.IsFalse(_service.TryRestore(snapshot, out var fail));
            StringAssert.Contains("unknown def id", fail);
        }

        [Test]
        public void Restore_OverlappingBuildings_Rejected()
        {
            var snapshot = _service.CaptureSnapshot();
            snapshot.Economy.PlacedBuildings.Add(new PlacedBuildingState { InstanceId = "b001", DefId = "pill_hall", GridX = 0, GridZ = 0, Rotation = 0 });
            snapshot.Economy.PlacedBuildings.Add(new PlacedBuildingState { InstanceId = "b002", DefId = "pill_hall", GridX = 1, GridZ = 1, Rotation = 0 });

            Assert.IsFalse(_service.TryRestore(snapshot, out var fail));
            StringAssert.Contains("overlapping", fail);
        }

        [Test]
        public void Restore_MembershipInconsistency_Rejected()
        {
            var snapshot = _service.CaptureSnapshot();
            // disciple claims a viewer owner but the registry has no matching record
            snapshot.Economy.Disciples[0].OwnerType = DiscipleOwnerType.Viewer;
            snapshot.Economy.Disciples[0].OwnerId = "viewer_ghost";

            Assert.IsFalse(_service.TryRestore(snapshot, out var fail));
            StringAssert.Contains("registry does not agree", fail);
        }

        [Test]
        public void Restore_Null_Rejected_WithReason()
        {
            Assert.IsFalse(_service.TryRestore(null, out var fail));
            StringAssert.Contains("null", fail);
            Assert.IsFalse(string.IsNullOrEmpty(_service.LastRestoreError));
        }

        // ── version handling ─────────────────────────────────────────────────

        [Test]
        public void Envelope_MissingVersion_Rejected()
        {
            var envelope = _service.CaptureEnvelope();
            envelope.Version = 0;

            Assert.IsFalse(_service.TryRestoreEnvelope(envelope, out var fail));
            StringAssert.Contains("no readable format version", fail);
        }

        [Test]
        public void Envelope_PreVersionedOrOlder_Rejected()
        {
            // With CurrentVersion == 1, an older envelope IS the pre-versioned (0) case;
            // both are refused with a format-version reason and no side effects.
            var envelope = _service.CaptureEnvelope();
            envelope.Version = SectSessionSnapshotEnvelope.CurrentVersion - 1;

            Assert.IsFalse(_service.TryRestoreEnvelope(envelope, out var fail));
            StringAssert.Contains("version", fail);
            Assert.AreEqual(0, _restored.Messages.Count);
            Assert.AreEqual(0, _service.RestoreCount);
        }

        [Test]
        public void Envelope_NewerVersion_Rejected()
        {
            var envelope = _service.CaptureEnvelope();
            envelope.Version = SectSessionSnapshotEnvelope.CurrentVersion + 1;

            Assert.IsFalse(_service.TryRestoreEnvelope(envelope, out var fail));
            StringAssert.Contains("newer", fail);
        }

        // ── progress retention + no duplicate rewards ────────────────────────

        [Test]
        public void Restore_CraftProgress_Retained_NoDuplicateRewardOnFirstTick()
        {
            // session A: place a pill_hall, start refining near completion
            var grid = new BuildingGrid(40, 40);
            Assert.IsTrue(_provider.TryPlaceBuilding("pill_hall", 0, 0, 0, grid, out var placeFail, out _), placeFail);
            Assert.IsTrue(_provider.TryAssignTask(SectStateProvider.SectMasterRequesterId, "d001", "refining_elixir", out var assignFail), assignFail);
            _provider.TickCrafting(19.9f); // 20s recipe, not finished

            var before = _service.CaptureSnapshot();
            Assert.AreEqual(19.9f, before.CraftProgress["d001"], 0.001f);

            // session B: restore, then run exactly one 0.2s tick — the craft completes ONCE
            var nextProvider = NewProvider(new MutableClock(T0));
            var nextService = new SessionSnapshotService(nextProvider, NewTimeSystem(),
                new BufferPublisher<SessionRestoredMessage>(), new MutableClock(T0));
            Assert.IsTrue(nextService.TryRestore(before, out var restoreFail), restoreFail);

            Assert.AreEqual(19.9f, nextService.CaptureSnapshot().CraftProgress["d001"], 0.001f,
                "progress must survive the load");

            nextProvider.TickCrafting(0.2f);

            Assert.AreEqual(5, StockQuantity(nextProvider, "elixir_qi_gathering", 3),
                "exactly one craft completes on the first post-load tick — no duplicate reward");
            Assert.AreEqual(0.1f, nextService.CaptureSnapshot().CraftProgress["d001"], 0.001f,
                "overshoot carries over, progress is not reset to zero");
        }

        [Test]
        public void Restore_GatherRemainder_ProducesExactlyOnceOnFirstTick()
        {
            Assert.IsTrue(_provider.TryAssignTask(SectStateProvider.SectMasterRequesterId, "d001", "gathering_wood", out var reason), reason);

            var snapshot = _service.CaptureSnapshot();
            snapshot.GatherAccumulators["gathering_wood"] = 0.9f;

            var nextProvider = NewProvider(new MutableClock(T0));
            var nextService = new SessionSnapshotService(nextProvider, NewTimeSystem(),
                new BufferPublisher<SessionRestoredMessage>(), new MutableClock(T0));
            Assert.IsTrue(nextService.TryRestore(snapshot, out var fail), fail);

            int woodBefore = nextProvider.BuildSectEconomyState().Stockpile.RawResources["wood"];
            nextProvider.TickGathering(0.6f); // 0.9 + 0.12 = 1.02 → exactly one whole unit

            Assert.AreEqual(woodBefore + 1, nextProvider.BuildSectEconomyState().Stockpile.RawResources["wood"]);
            Assert.AreEqual(0.02f, nextService.CaptureSnapshot().GatherAccumulators["gathering_wood"], 0.0001f);
        }

        // ── one orchestration owner: membership slice cannot compete ─────────

        [Test]
        public void MembershipSlice_RefusesAfterFullSessionRestore()
        {
            string path = Path.Combine(Path.GetTempPath(), "sect_session_gate_" + Guid.NewGuid().ToString("N") + ".msgpack");
            try
            {
                Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer, "viewer_test_01", out var reason), reason);
                var membership = new ViewerMembershipPersistenceSystem(_provider, _service) { SavePath = path };
                Assert.IsTrue(membership.Save());
                Assert.IsTrue(File.Exists(path));

                // a full session becomes authoritative
                Assert.IsTrue(_service.TryRestore(_service.CaptureSnapshot(), out var fail), fail);
                Assert.IsTrue(_service.HasFullSessionAuthority);

                // the narrower slice must now refuse rather than silently overwrite it
                Assert.IsFalse(membership.Load());
                StringAssert.Contains("full session", membership.LastLoadError);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // ── wiring guard ─────────────────────────────────────────────────────

        [Test]
        public void SessionSnapshotService_RegisteredInGameplayInstaller()
        {
            var gameplay = File.ReadAllText(Path.Combine(
                UnityEngine.Application.dataPath + "/..", "Assets/Scripts/Core/Installers/GameplayInstaller.cs"));
            StringAssert.Contains("ISessionRestoreAuthority, SessionSnapshotService", gameplay,
                "the single orchestration owner must be registered at the root");
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static SectStateProvider NewProvider(IClock clock)
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
                new BufferPublisher<DiscipleTaskChangedMessage>(),
                new BufferPublisher<DiscipleOwnerChangedMessage>(),
                clock);
        }

        private static TimeSystem NewTimeSystem()
        {
            return new TimeSystem(
                new BufferPublisher<TimeSpeedChangedMessage>(),
                new BufferPublisher<WorldEventTriggeredMessage>());
        }

        private static DiscipleState Find(SectStateProvider provider, string id)
        {
            foreach (var d in provider.BuildSectEconomyState().Disciples)
                if (d.DiscipleId == id) return d;
            return null;
        }

        private static int StockQuantity(SectStateProvider provider, string itemDefId, int grade)
        {
            foreach (var g in provider.BuildSectEconomyState().Stockpile.CraftedGoods)
                if (g.ItemDefId == itemDefId && g.Grade == grade && g.OwnerScope == OwnerScope.SectStockpile)
                    return g.Quantity;
            return 0;
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
    }
}
