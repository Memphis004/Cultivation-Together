using System;
using System.Collections.Generic;
using System.IO;
using MessagePack;
using MessagePipe;
using NUnit.Framework;
using Xianxia.Sect;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// P5B persistence — the owner of the save/load path (previously the P5A
    /// blocker: "no mechanism exists, so cross-session reclaim is unproven").
    /// The slice restored is viewer membership + ownership + LastActiveAtUtc,
    /// exported/imported together so registry ⇄ ownership invariants hold.
    /// Corrupt or disagreeing data must fail closed back to the mock start.
    /// </summary>
    public class ViewerMembershipPersistenceTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

        private MutableClock _clock;
        private SectStateProvider _provider;
        private string _path;
        private ViewerMembershipPersistenceSystem _system;

        [SetUp]
        public void SetUp()
        {
            _clock = new MutableClock(T0);
            _provider = NewProvider(_clock);
            _path = Path.Combine(Path.GetTempPath(), "sect_membership_" + Guid.NewGuid().ToString("N") + ".msgpack");
            _system = new ViewerMembershipPersistenceSystem(_provider) { SavePath = _path };
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }

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

        private static DiscipleState Find(SectStateProvider provider, string id)
        {
            foreach (var d in provider.BuildSectEconomyState().Disciples)
                if (d.DiscipleId == id) return d;
            return null;
        }

        // ---- the actual cross-session round trip ----

        [Test]
        public void RoundTrip_RestoresMembershipAndLastActiveAtUtc()
        {
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer, "viewer_test_01", out reason), reason);

            // activity advances to T0+300, then the session "ends"
            _clock.UtcNow = T0.AddSeconds(300);
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer, "viewer_test_01", out reason), reason);
            Assert.AreEqual(T0.AddSeconds(300), _provider.BuildSectEconomyState().ViewerRegistry.Find("viewer_test_01").LastActiveAtUtc);

            Assert.IsTrue(_system.Save());
            Assert.IsTrue(File.Exists(_path));

            // ---- a NEW session: fresh provider, mock start (all Npc) ----
            var nextClock = new MutableClock(T0.AddDays(1));
            var nextProvider = NewProvider(nextClock);
            var nextSystem = new ViewerMembershipPersistenceSystem(nextProvider) { SavePath = _path };

            Assert.IsTrue(nextSystem.Load(), nextSystem.LastLoadError);

            var d001 = Find(nextProvider, "d001");
            Assert.AreEqual(DiscipleOwnerType.Viewer, d001.OwnerType);
            Assert.AreEqual("viewer_test_01", d001.OwnerId);

            var record = nextProvider.BuildSectEconomyState().ViewerRegistry.Find("viewer_test_01");
            Assert.IsNotNull(record);
            Assert.AreEqual(ViewerMembershipStatus.Active, record.Status);
            Assert.AreEqual("d001", record.BoundDiscipleId);
            Assert.AreEqual(T0.AddSeconds(300), record.LastActiveAtUtc, "activity must survive the session verbatim");

            Assert.IsTrue(nextProvider.BuildSectEconomyState().ViewerRegistry.VerifyAgainst(nextProvider.BuildSectEconomyState().Disciples),
                "restored registry must agree with the restored ownership");

            // the restored activity drives protection: 60s later the owner is still protected
            nextClock.UtcNow = T0.AddSeconds(360);
            var permission = nextProvider.CheckTaskPermission(
                SectStateProvider.SectMasterRequesterId, "d001", "gathering_herb");
            Assert.IsTrue(permission.OwnerProtected);
            Assert.AreEqual(540f, permission.OwnerProtectionRemainingSeconds, 0.5f);

            // …and the owner can still control its disciple after the restart
            Assert.IsTrue(nextProvider.TryAssignTask("viewer_test_01", "d001", "gathering_wood", out reason), reason);
        }

        [Test]
        public void Export_IsACopy_DoesNotAliasLiveState()
        {
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer, "viewer_test_01", out _));

            var save = _provider.ExportViewerMembership();
            save.Records[0].LastActiveAtUtc = T0.AddYears(5);

            Assert.AreEqual(T0, _provider.BuildSectEconomyState().ViewerRegistry.Find("viewer_test_01").LastActiveAtUtc,
                "mutating an exported save must not touch live state");
        }

        // ---- missing / corrupt file is a normal no-op ----

        [Test]
        public void Load_MissingFile_IsNoOp_AndKeepsMockStart()
        {
            var system = new ViewerMembershipPersistenceSystem(_provider) { SavePath = _path };
            Assert.IsFalse(system.Load(), "no file → nothing to restore");
            Assert.AreEqual(DiscipleOwnerType.Npc, Find(_provider, "d001").OwnerType);
            Assert.AreEqual(0, _provider.BuildSectEconomyState().ViewerRegistry.Records.Count);
        }

        [Test]
        public void Load_CorruptFile_FailsClosed_WithReason()
        {
            File.WriteAllBytes(_path, new byte[] { 0xC1, 0xFF, 0x00, 0x13, 0x37 });

            Assert.IsFalse(_system.Load());
            Assert.IsFalse(string.IsNullOrEmpty(_system.LastLoadError), "the reason is reported, not swallowed");
            Assert.AreEqual(DiscipleOwnerType.Npc, Find(_provider, "d001").OwnerType, "state stays at mock start");
        }

        // ---- import validation (unit level) ----

        [Test]
        public void Import_VersionMismatch_Rejected_StateUnchanged()
        {
            var save = GoodSave();
            save.Version = 99;

            string reason;
            Assert.IsFalse(_provider.TryImportViewerMembership(save, out reason));
            StringAssert.Contains("version", reason);
            Assert.AreEqual(DiscipleOwnerType.Npc, Find(_provider, "d001").OwnerType);
        }

        [Test]
        public void Import_UnknownDisciple_Rejected_StateUnchanged()
        {
            var save = GoodSave();
            save.OwnerByDisciple.Add(new SectSavedOwnership
            {
                DiscipleId = "d999",
                OwnerType = DiscipleOwnerType.Viewer,
                OwnerId = "viewer_ghost",
            });

            string reason;
            Assert.IsFalse(_provider.TryImportViewerMembership(save, out reason));
            StringAssert.Contains("unknown disciple", reason);
            Assert.AreEqual(DiscipleOwnerType.Npc, Find(_provider, "d001").OwnerType);
            Assert.AreEqual(0, _provider.BuildSectEconomyState().ViewerRegistry.Records.Count);
        }

        [Test]
        public void Import_InconsistentRegistry_Rejected_StateUnchanged()
        {
            var save = GoodSave();
            // two active records claiming the same disciple — invariant #4 broken
            save.Records.Add(new ViewerRecord
            {
                ViewerId = "viewer_test_02",
                BoundDiscipleId = "d001",
                Status = ViewerMembershipStatus.Active,
                LastActiveAtUtc = T0,
            });

            string reason;
            Assert.IsFalse(_provider.TryImportViewerMembership(save, out reason));
            Assert.IsFalse(string.IsNullOrEmpty(reason));
            Assert.AreEqual(DiscipleOwnerType.Npc, Find(_provider, "d001").OwnerType);
            Assert.AreEqual(0, _provider.BuildSectEconomyState().ViewerRegistry.Records.Count);
        }

        [Test]
        public void Import_RegistryDisagreesWithSavedOwnership_Rejected_StateUnchanged()
        {
            var save = GoodSave();
            // registry still says d001 is viewer-owned, but the saved ownership says Npc
            foreach (var row in save.OwnerByDisciple)
                if (row.DiscipleId == "d001") row.OwnerType = DiscipleOwnerType.Npc;

            string reason;
            Assert.IsFalse(_provider.TryImportViewerMembership(save, out reason));
            StringAssert.Contains("does not agree", reason);
            Assert.AreEqual(DiscipleOwnerType.Npc, Find(_provider, "d001").OwnerType);
        }

        [Test]
        public void Import_UndefinedOwnerType_Rejected_StateUnchanged()
        {
            var save = GoodSave();
            foreach (var row in save.OwnerByDisciple)
                if (row.DiscipleId == "d001") row.OwnerType = (DiscipleOwnerType)999;

            string reason;
            Assert.IsFalse(_provider.TryImportViewerMembership(save, out reason));
            StringAssert.Contains("undefined", reason);
        }

        // ---- autosave cadence ----

        [Test]
        public void Autosave_WaitsForInterval_ThenSaves()
        {
            Assert.AreEqual(0, _system.SaveCount);

            _system.TickWithDelta(ViewerMembershipPersistenceSystem.AutoSaveIntervalSeconds - 0.5f);
            Assert.AreEqual(0, _system.SaveCount, "below the interval nothing is written");

            _system.TickWithDelta(1f);
            Assert.AreEqual(1, _system.SaveCount);
            Assert.IsTrue(File.Exists(_path));

            _system.TickWithDelta(1f);
            Assert.AreEqual(1, _system.SaveCount, "the timer reset after the write");
        }

        [Test]
        public void Dispose_FlushesLatestState()
        {
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d002", DiscipleOwnerType.Viewer, "viewer_test_02", out _));
            _system.Dispose();

            Assert.AreEqual(1, _system.SaveCount);

            var next = NewProvider(new MutableClock(T0));
            Assert.IsTrue(new ViewerMembershipPersistenceSystem(next) { SavePath = _path }.Load());
            Assert.AreEqual("viewer_test_02", Find(next, "d002").OwnerId);
        }

        // ---- wiring guard ----

        [Test]
        public void Persistence_RegisteredAsRootEntryPoint()
        {
            var gameplay = File.ReadAllText(Path.Combine(
                UnityEngine.Application.dataPath + "/..", "Assets/Scripts/Core/Installers/GameplayInstaller.cs"));
            StringAssert.Contains("RegisterEntryPoint<ViewerMembershipPersistenceSystem>", gameplay,
                "the load must happen at container build, not lazily");
        }

        // ---- helpers ----

        /// <summary>A save that matches the live mock roster: d001 viewer-owned with a consistent record.</summary>
        private SectViewerMembershipSave GoodSave()
        {
            var save = _provider.ExportViewerMembership();
            save.Version = SectViewerMembershipSave.CurrentVersion;
            save.Records.Clear();
            save.PendingApplications.Clear();
            save.OwnerByDisciple.Clear();

            foreach (var d in _provider.BuildSectEconomyState().Disciples)
            {
                var isViewer = d.DiscipleId == "d001";
                save.OwnerByDisciple.Add(new SectSavedOwnership
                {
                    DiscipleId = d.DiscipleId,
                    OwnerType = isViewer ? DiscipleOwnerType.Viewer : DiscipleOwnerType.Npc,
                    OwnerId = isViewer ? "viewer_test_01" : string.Empty,
                });
            }

            save.Records.Add(new ViewerRecord
            {
                ViewerId = "viewer_test_01",
                DisplayName = "viewer_test_01",
                BoundDiscipleId = "d001",
                Status = ViewerMembershipStatus.Active,
                LastActiveAtUtc = T0,
            });

            return save;
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
