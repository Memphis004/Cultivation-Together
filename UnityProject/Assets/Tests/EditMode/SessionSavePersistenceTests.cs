using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
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
    /// P11B — local disk storage for the P11A envelope: one manual slot + one backup,
    /// metadata for Continue, an atomic temp-then-replace write, and async
    /// application operations that report cancellation and failure as themselves.
    ///
    /// Every test uses an ISOLATED temporary directory (created in SetUp, deleted in
    /// TearDown) — nothing here touches Application.persistentDataPath.
    ///
    /// The worker hop is injected (InlineSaveWorkScheduler / GatedSaveWorkScheduler)
    /// because EditMode has no player loop to pump a real thread hop through; the
    /// repository's thread-agnosticism is proven separately by running it on a real
    /// background thread.
    /// </summary>
    public class SessionSavePersistenceTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

        private string _root;
        private SaveSlotPaths _paths;
        private SystemSaveFileSystem _realFileSystem;
        private SaveSlotRepository _repository;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "sect_save_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _paths = new SaveSlotPaths(_root);
            _realFileSystem = new SystemSaveFileSystem();
            _repository = new SaveSlotRepository(_realFileSystem, _paths);
        }

        [TearDown]
        public void TearDown()
        {
            DeleteDirectory(_root);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  repository: write / read / limits
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void Repository_WriteThenRead_RoundTripsThroughADiskFile()
        {
            var session = NewSession(_repository, new MutableClock(T0));
            Assert.IsTrue(session.Provider.TryAssignTask(
                SectStateProvider.SectMasterRequesterId, "d001", "gathering_wood", out var reason), reason);
            session.Provider.TickGathering(0.5f); // 0.2/s → 0.1 remainder
            session.Time.SetSpeed(2);

            var write = session.Saves.SaveAsync().GetAwaiter().GetResult();
            Assert.IsTrue(write.Success, write.Error);
            Assert.IsTrue(File.Exists(_paths.SlotPath), "the slot must exist after a successful save");
            Assert.IsFalse(File.Exists(_paths.TempPath), "the temp file must not survive a successful save");
            Assert.AreEqual(1, session.Saves.SaveCount);

            // Read it back with a FRESH repository (nothing shared but the directory).
            var read = new SaveSlotRepository(new SystemSaveFileSystem(), _paths).Read(CancellationToken.None);

            Assert.IsTrue(read.Success, read.Error);
            Assert.IsFalse(read.RecoveredFromBackup);
            Assert.AreEqual(SectSessionSnapshotEnvelope.CurrentVersion, read.Envelope.Version);
            Assert.AreEqual(0.1f, read.Envelope.Snapshot.GatherAccumulators["gathering_wood"], 0.0001f);
            Assert.AreEqual(2, read.Envelope.Snapshot.SimulationSpeed);
            Assert.IsTrue(read.Metadata.HasSave);
            Assert.AreEqual(T0, read.Metadata.SavedAtUtc);
            Assert.AreEqual(SectSessionSnapshotEnvelope.CurrentVersion, read.Metadata.FormatVersion);
        }

        [Test]
        public void Repository_SecondWrite_KeepsThePreviousSaveAsTheBackup()
        {
            WriteValidSlot(T0);
            byte[] firstBytes = File.ReadAllBytes(_paths.SlotPath);

            WriteValidSlot(T0.AddDays(1));

            Assert.IsTrue(File.Exists(_paths.BackupPath), "a second save must leave the first one as the backup");
            CollectionAssert.AreEqual(firstBytes, File.ReadAllBytes(_paths.BackupPath),
                "the backup must be exactly the previous save");

            var metadata = _repository.ReadMetadata(CancellationToken.None);
            Assert.IsTrue(metadata.HasSave);
            Assert.AreEqual(T0.AddDays(1), metadata.SavedAtUtc, "the slot must hold the newer save");
            Assert.IsFalse(metadata.FromBackup);
        }

        [Test]
        public void Repository_WriteFromABackgroundThread_Succeeds()
        {
            // The repository is plain C#/System.IO — no Unity API — so it must be callable
            // off the main thread, which is exactly where the service runs it (§6).
            var envelope = DefaultEnvelope(T0);
            Exception failure = null;

            var thread = new Thread(() =>
            {
                try
                {
                    _repository.Write(envelope, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(15)), "the background write must finish");

            Assert.IsNull(failure, failure != null ? failure.ToString() : string.Empty);
            var read = _repository.Read(CancellationToken.None);
            Assert.IsTrue(read.Success, read.Error);
        }

        [Test]
        public void Repository_WriteWithCancelledToken_WritesNothing()
        {
            WriteValidSlot(T0);
            byte[] before = File.ReadAllBytes(_paths.SlotPath);

            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var result = _repository.Write(DefaultEnvelope(T0.AddDays(1)), cts.Token);

                Assert.IsFalse(result.Success);
                Assert.AreEqual(SaveIoErrorKind.Cancelled, result.ErrorKind, result.Error);
                Assert.IsTrue(result.PreviousSavePreserved);
                StringAssert.Contains("cancel", result.Error.ToLowerInvariant());
            }

            CollectionAssert.AreEqual(before, File.ReadAllBytes(_paths.SlotPath));
            Assert.IsFalse(File.Exists(_paths.TempPath));
        }

        [Test]
        public void Repository_WriteOverTheSizeLimit_WritesNothing()
        {
            WriteValidSlot(T0);
            byte[] before = File.ReadAllBytes(_paths.SlotPath);

            // A limit smaller than any real save: the refusal happens before any file exists.
            var tiny = new SaveSlotRepository(_realFileSystem, _paths, 64);
            var result = tiny.Write(DefaultEnvelope(T0.AddDays(1)), CancellationToken.None);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(SaveIoErrorKind.Rejected, result.ErrorKind, result.Error);
            StringAssert.Contains("limit", result.Error);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(_paths.SlotPath));
        }

        [Test]
        public void Repository_ReadOverTheSizeLimit_ReportsCorrupt()
        {
            WriteValidSlot(T0);

            var tiny = new SaveSlotRepository(_realFileSystem, _paths, 64);
            var read = tiny.Read(CancellationToken.None);

            Assert.IsFalse(read.Success);
            Assert.AreEqual(SaveIoErrorKind.Corrupt, read.ErrorKind);
            StringAssert.Contains("limit", read.Error);
            Assert.IsFalse(read.Metadata.HasSave);
            Assert.AreEqual(SaveIoErrorKind.Corrupt, read.Metadata.ErrorKind);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  repository: failed write preserves the previous save (§3)
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void Repository_TempWriteFails_PreservesThePreviousSave()
        {
            WriteValidSlot(T0);
            byte[] before = File.ReadAllBytes(_paths.SlotPath);

            var failing = new FaultingSaveFileSystem(_realFileSystem) { FailWrites = true };
            var result = new SaveSlotRepository(failing, _paths)
                .Write(DefaultEnvelope(T0.AddDays(1)), CancellationToken.None);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(SaveIoErrorKind.Io, result.ErrorKind);
            Assert.IsTrue(result.PreviousSavePreserved);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(_paths.SlotPath),
                "a failed write must leave the slot byte-for-byte as it was");
            Assert.IsFalse(File.Exists(_paths.TempPath));
        }

        [Test]
        public void Repository_TempFileFailsVerification_PreservesThePreviousSave()
        {
            WriteValidSlot(T0);
            byte[] before = File.ReadAllBytes(_paths.SlotPath);

            // The write "succeeds" but lands garbage: verification must catch it BEFORE the
            // swap, so the previous valid save is never replaced by an unreadable file.
            var corrupting = new FaultingSaveFileSystem(_realFileSystem) { CorruptWrites = true };
            var result = new SaveSlotRepository(corrupting, _paths)
                .Write(DefaultEnvelope(T0.AddDays(1)), CancellationToken.None);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(SaveIoErrorKind.Io, result.ErrorKind);
            StringAssert.Contains("verification", result.Error);
            Assert.IsTrue(result.PreviousSavePreserved);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(_paths.SlotPath));
            Assert.IsFalse(File.Exists(_paths.TempPath));
        }

        [Test]
        public void Repository_PlatformWithoutNativeReplace_FallsBackToMoveAside()
        {
            var unsupported = new FaultingSaveFileSystem(_realFileSystem) { ReplaceUnsupported = true };
            var repository = new SaveSlotRepository(unsupported, _paths);

            var first = repository.Write(DefaultEnvelope(T0), CancellationToken.None);
            Assert.IsTrue(first.Success, first.Error);
            byte[] firstBytes = File.ReadAllBytes(_paths.SlotPath);

            var second = repository.Write(DefaultEnvelope(T0.AddDays(1)), CancellationToken.None);
            Assert.IsTrue(second.Success, second.Error);
            Assert.AreEqual(1, unsupported.ReplaceCalls,
                "the first save has nothing to replace; the second must have attempted the native replace");

            CollectionAssert.AreEqual(firstBytes, File.ReadAllBytes(_paths.BackupPath),
                "the move-aside fallback must still keep the previous save as the backup");
            Assert.AreEqual(T0.AddDays(1), repository.ReadMetadata(CancellationToken.None).SavedAtUtc);
        }

        [Test]
        public void Repository_CommitGuardFalse_ReportsObsoleteAndLeavesTheSlotUntouched()
        {
            WriteValidSlot(T0);
            byte[] before = File.ReadAllBytes(_paths.SlotPath);

            // True on the first ask, false on the second (the check immediately before the
            // replace) — exactly the window a session transition can fall into (§5).
            int calls = 0;
            var result = _repository.Write(
                DefaultEnvelope(T0.AddDays(1)), CancellationToken.None, () => ++calls == 1);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(SaveIoErrorKind.Obsolete, result.ErrorKind);
            Assert.IsTrue(result.PreviousSavePreserved);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(_paths.SlotPath),
                "an obsolete completion must not overwrite the slot");
            Assert.IsFalse(File.Exists(_paths.TempPath));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  repository: error classification + backup recovery (§4)
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void Repository_MissingSave_ReportsMissing()
        {
            var read = _repository.Read(CancellationToken.None);

            Assert.IsFalse(read.Success);
            Assert.AreEqual(SaveIoErrorKind.Missing, read.ErrorKind);

            var metadata = _repository.ReadMetadata(CancellationToken.None);
            Assert.IsFalse(metadata.HasSave);
            Assert.AreEqual(SaveIoErrorKind.Missing, metadata.ErrorKind);
            Assert.AreEqual(_paths.SlotPath, metadata.SlotPath);
        }

        [Test]
        public void Repository_CorruptSlotWithoutBackup_ReportsCorrupt()
        {
            File.WriteAllBytes(_paths.SlotPath, new byte[] { 0xC1, 0x00, 0x01 }); // 0xC1 is never valid MessagePack

            var read = _repository.Read(CancellationToken.None);

            Assert.IsFalse(read.Success);
            Assert.AreEqual(SaveIoErrorKind.Corrupt, read.ErrorKind);
            StringAssert.Contains("corrupt", read.Error);
        }

        [Test]
        public void Repository_CorruptSlot_RecoversFromTheBackup()
        {
            WriteValidSlot(T0);
            WriteValidSlot(T0.AddDays(1)); // slot = T0+1d, backup = T0
            File.WriteAllBytes(_paths.SlotPath, new byte[] { 0xC1 });

            var read = _repository.Read(CancellationToken.None);

            Assert.IsTrue(read.Success, read.Error);
            Assert.IsTrue(read.RecoveredFromBackup);
            Assert.AreEqual(T0, read.Metadata.SavedAtUtc, "the recovered save must be the backup (the older one)");
            Assert.AreEqual(T0, read.Envelope.SavedAtUtc);
            StringAssert.Contains("backup", read.Note);
        }

        [Test]
        public void Repository_SlotMissingButBackupPresent_RecoversFromTheBackup()
        {
            WriteValidSlot(T0);
            WriteValidSlot(T0.AddDays(1));
            File.Delete(_paths.SlotPath);

            var read = _repository.Read(CancellationToken.None);

            Assert.IsTrue(read.Success, read.Error);
            Assert.IsTrue(read.RecoveredFromBackup);
            Assert.AreEqual(T0, read.Metadata.SavedAtUtc);
        }

        [Test]
        public void Repository_NewerFormatSlot_ReportsUnsupportedVersion_AndDoesNotUseTheBackup()
        {
            WriteValidSlot(T0);
            WriteValidSlot(T0.AddDays(1)); // a valid backup exists…

            var newer = DefaultEnvelope(T0.AddDays(2));
            newer.Version = SectSessionSnapshotEnvelope.CurrentVersion + 1;
            File.WriteAllBytes(_paths.SlotPath,
                MessagePackSerializer.Serialize(newer, SaveSlotRepository.SerializerOptions));

            var read = _repository.Read(CancellationToken.None);

            Assert.IsFalse(read.Success);
            Assert.AreEqual(SaveIoErrorKind.UnsupportedVersion, read.ErrorKind,
                "a newer-format save is reported, never silently downgraded to the backup");
            StringAssert.Contains("newer", read.Error);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  gate
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void Gate_IsSingleFlight()
        {
            var gate = new SaveOperationGate();

            Assert.IsFalse(gate.IsBusy);
            Assert.IsTrue(gate.TryEnter());
            Assert.IsTrue(gate.IsBusy);
            Assert.IsFalse(gate.TryEnter(), "a second operation must be refused while one is in flight");

            gate.Exit();
            Assert.IsFalse(gate.IsBusy);
            Assert.IsTrue(gate.TryEnter());
        }

        // ══════════════════════════════════════════════════════════════════════
        //  application service: the real save → quit → continue flow
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void Service_SaveThenLoad_RestoresIntoAnotherSession_AndPublishesOnce()
        {
            // ── session A: progress worth keeping ──
            var a = NewSession(_repository, new MutableClock(T0));
            Assert.IsTrue(a.Provider.TryAssignTask(
                SectStateProvider.SectMasterRequesterId, "d001", "gathering_wood", out var reason), reason);
            a.Provider.TickGathering(0.5f);
            a.Time.SetSpeed(3);
            a.Time.RaiseWorldEvent("bandit_raid_001", "raiders", true,
                new List<EventChoiceInfo> { new EventChoiceInfo { ChoiceId = "fight", Label = "Fight" } });

            var save = a.Saves.SaveAsync().GetAwaiter().GetResult();
            Assert.IsTrue(save.Success, save.Error);
            Assert.IsTrue(save.Metadata.HasSave);
            Assert.AreEqual(1, a.Saves.SaveCount);

            // ── "restart": a brand-new session (new provider/time, same slot) ──
            var b = NewSession(new SaveSlotRepository(_realFileSystem, _paths), new MutableClock(T0.AddDays(2)));

            var load = b.Saves.LoadAsync().GetAwaiter().GetResult();

            Assert.IsTrue(load.Success, load.Error);
            Assert.AreEqual(1, b.Saves.LoadCount);
            Assert.AreEqual("gathering_wood", Find(b.Provider, "d001").CurrentTask);
            Assert.AreEqual(0.1f, b.Snapshots.CaptureSnapshot().GatherAccumulators["gathering_wood"], 0.0001f);
            Assert.AreEqual(3, b.Time.Speed);
            Assert.IsTrue(b.Time.HasPendingDecision);
            Assert.AreEqual("bandit_raid_001", b.Time.PendingEventId);

            // exactly one coherent notification, published after the commit (P11A contract)
            Assert.AreEqual(1, b.Restored.Messages.Count);
            Assert.IsTrue(b.Restored.Messages[0].FullSession);
            Assert.IsTrue(b.Snapshots.HasFullSessionAuthority);
        }

        [Test]
        public void Service_Metadata_DescribesTheCommittedSave()
        {
            var beforeAnySave = _repository.ReadMetadata(CancellationToken.None);
            Assert.IsFalse(beforeAnySave.HasSave);
            Assert.AreEqual(SaveIoErrorKind.Missing, beforeAnySave.ErrorKind);

            var a = NewSession(_repository, new MutableClock(T0));
            a.Time.RaiseWorldEvent("bandit_raid_001", "raiders", true,
                new List<EventChoiceInfo> { new EventChoiceInfo { ChoiceId = "fight", Label = "Fight" } });
            Assert.IsTrue(a.Saves.SaveAsync().GetAwaiter().GetResult().Success);

            var metadata = a.Saves.ReadMetadataAsync().GetAwaiter().GetResult();

            Assert.IsTrue(metadata.HasSave);
            Assert.AreEqual(T0, metadata.SavedAtUtc);
            Assert.AreEqual(SectSessionSnapshotEnvelope.CurrentVersion, metadata.FormatVersion);
            Assert.AreEqual(a.Provider.BuildSectEconomyState().Disciples.Count, metadata.DiscipleCount);
            Assert.AreEqual(0, metadata.BuildingCount);
            Assert.IsTrue(metadata.HasPendingDecision);
            Assert.Greater(metadata.FileSizeBytes, 0);
        }

        [Test]
        public void Service_LoadWithNoSave_ReportsMissing_AndDoesNotStartANewSession()
        {
            var session = NewSession(_repository, new MutableClock(T0));
            int disciplesBefore = session.Provider.BuildSectEconomyState().Disciples.Count;

            var load = session.Saves.LoadAsync().GetAwaiter().GetResult();

            Assert.IsFalse(load.Success);
            Assert.AreEqual(SaveIoErrorKind.Missing, load.ErrorKind);
            Assert.IsFalse(session.Snapshots.HasFullSessionAuthority);
            Assert.AreEqual(0, session.Snapshots.RestoreCount);
            Assert.AreEqual(0, session.Restored.Messages.Count, "a failed Continue must not look like a fresh session");
            Assert.AreEqual(disciplesBefore, session.Provider.BuildSectEconomyState().Disciples.Count);
        }

        [Test]
        public void Service_LoadWithCorruptSave_ReportsCorrupt_AndRestoresNothing()
        {
            File.WriteAllBytes(_paths.SlotPath, new byte[] { 0xC1, 0x00 });

            var session = NewSession(_repository, new MutableClock(T0));
            int disciplesBefore = session.Provider.BuildSectEconomyState().Disciples.Count;

            var load = session.Saves.LoadAsync().GetAwaiter().GetResult();

            Assert.IsFalse(load.Success);
            Assert.AreEqual(SaveIoErrorKind.Corrupt, load.ErrorKind);
            Assert.AreEqual(0, session.Restored.Messages.Count);
            Assert.IsFalse(session.Snapshots.HasFullSessionAuthority);
            Assert.AreEqual(disciplesBefore, session.Provider.BuildSectEconomyState().Disciples.Count);
        }

        [Test]
        public void Service_LoadNewerFormatSave_ReportsUnsupportedVersion_AndRestoresNothing()
        {
            var newer = DefaultEnvelope(T0);
            newer.Version = SectSessionSnapshotEnvelope.CurrentVersion + 1;
            File.WriteAllBytes(_paths.SlotPath,
                MessagePackSerializer.Serialize(newer, SaveSlotRepository.SerializerOptions));

            var session = NewSession(_repository, new MutableClock(T0));
            var load = session.Saves.LoadAsync().GetAwaiter().GetResult();

            Assert.IsFalse(load.Success);
            Assert.AreEqual(SaveIoErrorKind.UnsupportedVersion, load.ErrorKind);
            Assert.AreEqual(0, session.Snapshots.RestoreCount);
            Assert.AreEqual(0, session.Restored.Messages.Count);
        }

        [Test]
        public void Service_LoadCorruptSlot_UsesTheBackupThroughTheService()
        {
            WriteValidSlot(T0);
            WriteValidSlot(T0.AddDays(1)); // backup = T0
            File.WriteAllBytes(_paths.SlotPath, new byte[] { 0xC1 });

            var session = NewSession(new SaveSlotRepository(_realFileSystem, _paths), new MutableClock(T0.AddDays(2)));
            var load = session.Saves.LoadAsync().GetAwaiter().GetResult();

            Assert.IsTrue(load.Success, load.Error);
            Assert.IsTrue(load.RecoveredFromBackup);
            Assert.AreEqual(1, session.Restored.Messages.Count);
            Assert.IsTrue(session.Snapshots.HasFullSessionAuthority);
        }

        [Test]
        public void Service_LoadStructurallyInvalidSave_RefusesRestore_AndLeavesLiveStateUntouched()
        {
            // A readable envelope whose CONTENT is invalid (unknown task id): the repository
            // can read it, so the refusal has to come from the P11A validation — and it must
            // happen before any live state is replaced.
            var provider = NewProvider(new MutableClock(T0));
            var envelope = NewEnvelope(provider, NewTimeSystem(), T0);
            envelope.Snapshot.Economy.Disciples[0].CurrentTask = "gathering_lunar_herb";
            Assert.IsTrue(_repository.Write(envelope, CancellationToken.None).Success);

            var session = NewSession(_repository, new MutableClock(T0.AddDays(1)));
            string liveTaskBefore = Find(session.Provider, "d000").CurrentTask;

            var load = session.Saves.LoadAsync().GetAwaiter().GetResult();

            Assert.IsFalse(load.Success);
            Assert.AreEqual(SaveIoErrorKind.Corrupt, load.ErrorKind);
            StringAssert.Contains("task id", load.Error);
            Assert.AreEqual(0, session.Snapshots.RestoreCount);
            Assert.AreEqual(0, session.Restored.Messages.Count);
            Assert.IsFalse(session.Snapshots.HasFullSessionAuthority);
            Assert.AreEqual(liveTaskBefore, Find(session.Provider, "d000").CurrentTask,
                "live state must be byte-for-byte untouched after a refused restore");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  application service: overlapping requests + cancellation (§4)
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void Service_OverlappingSave_ReturnsBusy_AndNeverStartsASecondWrite()
        {
            var scheduler = new GatedSaveWorkScheduler();
            scheduler.BeginHold();

            var session = NewSession(_repository, new MutableClock(T0), scheduler);

            var first = session.Saves.SaveAsync();
            Assert.AreEqual(1, scheduler.RunCount);
            Assert.IsFalse(first.Status.IsCompleted(), "the first save is held on the worker");

            var second = session.Saves.SaveAsync();
            var secondResult = second.GetAwaiter().GetResult();

            Assert.IsFalse(secondResult.Success);
            Assert.AreEqual(SaveIoErrorKind.Busy, secondResult.ErrorKind);
            Assert.AreEqual(1, scheduler.RunCount, "a refused request must not start a second write");
            Assert.IsTrue(session.Saves.IsBusy);

            scheduler.Release();
            var firstResult = first.GetAwaiter().GetResult();

            Assert.IsTrue(firstResult.Success, firstResult.Error);
            Assert.AreEqual(1, session.Saves.SaveCount);
            Assert.IsFalse(session.Saves.IsBusy, "the gate must be released when the operation finishes");
        }

        [Test]
        public void Service_CancelledSave_WritesNothing()
        {
            WriteValidSlot(T0);
            byte[] before = File.ReadAllBytes(_paths.SlotPath);

            var scheduler = new GatedSaveWorkScheduler();
            scheduler.BeginHold();
            var session = NewSession(_repository, new MutableClock(T0.AddDays(1)), scheduler);

            using (var cts = new CancellationTokenSource())
            {
                var pending = session.Saves.SaveAsync(cts.Token);
                cts.Cancel();
                scheduler.Release();

                var result = pending.GetAwaiter().GetResult();
                Assert.IsFalse(result.Success);
                Assert.AreEqual(SaveIoErrorKind.Cancelled, result.ErrorKind);
            }

            CollectionAssert.AreEqual(before, File.ReadAllBytes(_paths.SlotPath));
            Assert.AreEqual(0, session.Saves.SaveCount);
            Assert.IsFalse(session.Saves.IsBusy);
        }

        [Test]
        public void Service_CancelledLoad_RestoresNothing()
        {
            WriteValidSlot(T0);

            var scheduler = new GatedSaveWorkScheduler();
            scheduler.BeginHold();
            var session = NewSession(new SaveSlotRepository(_realFileSystem, _paths), new MutableClock(T0.AddDays(1)), scheduler);

            using (var cts = new CancellationTokenSource())
            {
                var pending = session.Saves.LoadAsync(cts.Token);
                cts.Cancel();
                scheduler.Release();

                var result = pending.GetAwaiter().GetResult();
                Assert.IsFalse(result.Success);
                Assert.AreEqual(SaveIoErrorKind.Cancelled, result.ErrorKind);
            }

            Assert.IsFalse(session.Snapshots.HasFullSessionAuthority);
            Assert.AreEqual(0, session.Restored.Messages.Count);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  session transitions invalidate obsolete completions (§5)
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void Service_SessionTransitionDuringSave_DoesNotOverwriteTheSlot()
        {
            WriteValidSlot(T0);
            byte[] previous = File.ReadAllBytes(_paths.SlotPath);

            var scheduler = new GatedSaveWorkScheduler();
            scheduler.BeginHold();
            var session = NewSession(_repository, new MutableClock(T0.AddDays(1)), scheduler);

            var pending = session.Saves.SaveAsync();

            // …the player begins another game while this save is still in flight.
            session.Saves.BeginSessionTransition();
            scheduler.Release();

            var result = pending.GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ErrorKind == SaveIoErrorKind.Cancelled || result.ErrorKind == SaveIoErrorKind.Obsolete,
                "a transition must cancel or invalidate the in-flight save, got " + result.ErrorKind);
            CollectionAssert.AreEqual(previous, File.ReadAllBytes(_paths.SlotPath),
                "the new session's slot must not be overwritten by the old session's save");
            Assert.AreEqual(0, session.Saves.SaveCount);
            Assert.IsFalse(session.Saves.IsBusy);
        }

        [Test]
        public void Service_GenerationBumpedWithoutCancellation_InvalidatesTheSaveCompletion()
        {
            WriteValidSlot(T0);
            byte[] previous = File.ReadAllBytes(_paths.SlotPath);

            var tracker = new SessionTransitionTracker();
            var scheduler = new GatedSaveWorkScheduler();
            scheduler.BeginHold();
            var session = NewSession(_repository, new MutableClock(T0.AddDays(1)), scheduler, tracker);

            var pending = session.Saves.SaveAsync();

            // No cancellation here — only the generation moves on. The completion must still
            // be refused by the repository's commit guard.
            tracker.BeginTransition();
            scheduler.Release();

            var result = pending.GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(SaveIoErrorKind.Obsolete, result.ErrorKind);
            Assert.IsTrue(result.PreviousSavePreserved);
            CollectionAssert.AreEqual(previous, File.ReadAllBytes(_paths.SlotPath));
        }

        [Test]
        public void Service_GenerationBumpedWithoutCancellation_InvalidatesTheLoadCompletion()
        {
            WriteValidSlot(T0);

            var tracker = new SessionTransitionTracker();
            var scheduler = new GatedSaveWorkScheduler();
            scheduler.BeginHold();
            var session = NewSession(new SaveSlotRepository(_realFileSystem, _paths),
                new MutableClock(T0.AddDays(1)), scheduler, tracker);

            var pending = session.Saves.LoadAsync();

            tracker.BeginTransition(); // the read itself is allowed to finish…
            scheduler.Release();

            var result = pending.GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(SaveIoErrorKind.Obsolete, result.ErrorKind); // …the COMMIT is not
            Assert.IsFalse(session.Snapshots.HasFullSessionAuthority);
            Assert.AreEqual(0, session.Snapshots.RestoreCount);
            Assert.AreEqual(0, session.Restored.Messages.Count,
                "an invalidated load must not restore or publish anything");
        }

        [Test]
        public void Service_SessionTransitionDuringLoad_RestoresNothing()
        {
            WriteValidSlot(T0);

            var scheduler = new GatedSaveWorkScheduler();
            scheduler.BeginHold();
            var session = NewSession(new SaveSlotRepository(_realFileSystem, _paths),
                new MutableClock(T0.AddDays(1)), scheduler);

            var pending = session.Saves.LoadAsync();
            session.Saves.BeginSessionTransition();
            scheduler.Release();

            var result = pending.GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ErrorKind == SaveIoErrorKind.Cancelled || result.ErrorKind == SaveIoErrorKind.Obsolete,
                "got " + result.ErrorKind);
            Assert.IsFalse(session.Snapshots.HasFullSessionAuthority);
            Assert.AreEqual(0, session.Restored.Messages.Count);
        }

        [Test]
        public void Service_AfterATransition_NewSavesStillSucceed()
        {
            var scheduler = new GatedSaveWorkScheduler();
            scheduler.BeginHold();
            var session = NewSession(_repository, new MutableClock(T0), scheduler);

            var pending = session.Saves.SaveAsync();
            session.Saves.BeginSessionTransition();
            scheduler.Release();
            Assert.IsFalse(pending.GetAwaiter().GetResult().Success);

            // The session token is replaced, not permanently cancelled, and the gate is free.
            var next = session.Saves.SaveAsync().GetAwaiter().GetResult();

            Assert.IsTrue(next.Success, next.Error);
            Assert.AreEqual(1, session.Saves.SaveCount);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  wiring
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void Wiring_GameplayInstaller_RegistersTheSaveLayer()
        {
            var gameplay = File.ReadAllText(Path.Combine(
                UnityEngine.Application.dataPath + "/..", "Assets/Scripts/Core/Installers/GameplayInstaller.cs"));

            StringAssert.Contains("SaveSlotPaths.UnderPersistentDataPath()", gameplay,
                "persistentDataPath must be resolved on the main thread in the composition root");
            StringAssert.Contains("ISaveFileSystem", gameplay);
            StringAssert.Contains("SaveSlotRepository", gameplay);
            StringAssert.Contains("ISaveWorkScheduler", gameplay);
            StringAssert.Contains("SessionTransitionTracker", gameplay);
            StringAssert.Contains("SaveOperationGate", gameplay);
            StringAssert.Contains("SaveSessionService", gameplay);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  helpers
        // ══════════════════════════════════════════════════════════════════════

        private sealed class SessionObjects
        {
            public MutableClock Clock;
            public SectStateProvider Provider;
            public TimeSystem Time;
            public BufferPublisher<SessionRestoredMessage> Restored;
            public SessionSnapshotService Snapshots;
            public SaveSessionService Saves;
        }

        private static SessionObjects NewSession(
            SaveSlotRepository repository,
            MutableClock clock,
            ISaveWorkScheduler scheduler = null,
            SessionTransitionTracker tracker = null,
            SaveOperationGate gate = null)
        {
            var session = new SessionObjects
            {
                Clock = clock,
                Provider = NewProvider(clock),
                Time = NewTimeSystem(),
                Restored = new BufferPublisher<SessionRestoredMessage>(),
            };
            session.Snapshots = new SessionSnapshotService(session.Provider, session.Time, session.Restored, clock);
            session.Saves = new SaveSessionService(
                session.Snapshots,
                repository,
                scheduler ?? new InlineSaveWorkScheduler(),
                tracker ?? new SessionTransitionTracker(),
                gate ?? new SaveOperationGate());
            return session;
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

        private static TimeSystem NewTimeSystem()
        {
            return new TimeSystem(
                new BufferPublisher<TimeSpeedChangedMessage>(),
                new BufferPublisher<WorldEventTriggeredMessage>());
        }

        private static SectSessionSnapshotEnvelope NewEnvelope(
            SectStateProvider provider, TimeSystem time, DateTime savedAt)
        {
            var snapshots = new SessionSnapshotService(
                provider, time, new BufferPublisher<SessionRestoredMessage>(), new MutableClock(savedAt));
            return snapshots.CaptureEnvelope();
        }

        private static SectSessionSnapshotEnvelope DefaultEnvelope(DateTime savedAt)
        {
            var clock = new MutableClock(savedAt);
            return NewEnvelope(NewProvider(clock), NewTimeSystem(), savedAt);
        }

        private void WriteValidSlot(DateTime savedAt)
        {
            var result = _repository.Write(DefaultEnvelope(savedAt), CancellationToken.None);
            Assert.IsTrue(result.Success, result.Error);
        }

        private static DiscipleState Find(SectStateProvider provider, string id)
        {
            foreach (var disciple in provider.BuildSectEconomyState().Disciples)
                if (disciple.DiscipleId == id) return disciple;
            return null;
        }

        private static void DeleteDirectory(string path)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (Directory.Exists(path)) Directory.Delete(path, true);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(25);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(25);
                }
            }
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

        /// <summary>Runs the worker body inline; completes synchronously (EditMode has no player loop).</summary>
        private sealed class InlineSaveWorkScheduler : ISaveWorkScheduler
        {
            public UniTask<T> RunOnWorkerAsync<T>(Func<T> work, CancellationToken cancellationToken)
            {
                // Same cancellation contract as UniTask.RunOnThreadPool: a cancelled token
                // faults the awaitable and the worker body never runs. (A faulted UniTask
                // rather than a synchronous throw, so the caller's await is what observes it.)
                if (cancellationToken.IsCancellationRequested)
                    return UniTask.FromException<T>(new OperationCanceledException(cancellationToken));

                return UniTask.FromResult(work());
            }

            public UniTask SwitchToMainThreadAsync() => UniTask.CompletedTask;
        }

        /// <summary>Holds the worker body until the test releases it (overlap/cancellation tests).</summary>
        private sealed class GatedSaveWorkScheduler : ISaveWorkScheduler
        {
            private UniTaskCompletionSource _hold;

            public int RunCount { get; private set; }

            public void BeginHold() => _hold = new UniTaskCompletionSource();

            public async UniTask<T> RunOnWorkerAsync<T>(Func<T> work, CancellationToken cancellationToken)
            {
                RunCount++;
                cancellationToken.ThrowIfCancellationRequested();

                var hold = _hold;
                if (hold != null) await hold.Task;

                cancellationToken.ThrowIfCancellationRequested();
                return work();
            }

            public UniTask SwitchToMainThreadAsync() => UniTask.CompletedTask;

            public void Release()
            {
                var hold = _hold;
                _hold = null;
                if (hold != null) hold.TrySetResult();
            }
        }

        /// <summary>
        /// A fault injector over the real filesystem — the only reliable way to test the
        /// "a failed write preserves the previous save" contract on Windows, where a
        /// read-only directory attribute does not stop file creation.
        /// </summary>
        private sealed class FaultingSaveFileSystem : ISaveFileSystem
        {
            private readonly ISaveFileSystem _inner;

            public FaultingSaveFileSystem(ISaveFileSystem inner) { _inner = inner; }

            public bool FailWrites;
            public bool CorruptWrites;
            public bool ReplaceUnsupported;
            public int ReplaceCalls;

            public bool FileExists(string path) => _inner.FileExists(path);
            public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
            public void CreateDirectory(string path) => _inner.CreateDirectory(path);
            public long GetFileLength(string path) => _inner.GetFileLength(path);
            public byte[] ReadAllBytes(string path) => _inner.ReadAllBytes(path);
            public void Delete(string path) => _inner.Delete(path);
            public void Move(string source, string destination) => _inner.Move(source, destination);

            public void WriteAllBytes(string path, byte[] bytes)
            {
                if (FailWrites) throw new IOException("injected: write failure");
                if (CorruptWrites)
                {
                    // "succeeds" with garbage — verification must catch it before the swap.
                    _inner.WriteAllBytes(path, new byte[] { 0xC1 });
                    return;
                }
                _inner.WriteAllBytes(path, bytes);
            }

            public void ReplaceWithBackup(string source, string destination, string backup)
            {
                ReplaceCalls++;
                if (ReplaceUnsupported) throw new PlatformNotSupportedException("injected: no native replace");
                _inner.ReplaceWithBackup(source, destination, backup);
            }
        }
    }
}
