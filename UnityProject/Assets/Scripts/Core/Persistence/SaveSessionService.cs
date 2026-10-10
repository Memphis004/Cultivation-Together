using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xianxia.Sect
{
    /// <summary>
    /// P11B — the application service a save button / Continue button calls: one manual
    /// save slot, one backup, metadata for Continue, async operations with cancellation
    /// and explicit error results.
    ///
    /// Main-thread contract:
    ///   * capturing and restoring live gameplay state happen on the Unity main thread
    ///     (the P11A <see cref="SessionSnapshotService"/> owns both);
    ///   * only the detached envelope and plain file I/O go to a worker
    ///     (<see cref="ISaveWorkScheduler"/>), so no Unity API is ever touched off-thread;
    ///   * a completion that no longer belongs to the live session is INVALIDATED rather
    ///     than committed (§5).
    ///
    /// Error contract (§4): a missing, corrupt, unsupported-version or I/O failure is
    /// reported as itself. <see cref="LoadAsync"/> never falls back to a New Game — live
    /// state is only ever replaced by a save that validated completely.
    ///
    /// No autosave and no Title UI here (explicitly out of scope for P11B): this type is
    /// the storage/application layer those will call. Metadata is read from the same
    /// validated read a load performs, so it cannot drift from the save it describes; a
    /// metadata read does not take the operation gate because it never writes (the
    /// in-flight temp file is never mistaken for the slot).
    /// </summary>
    public sealed class SaveSessionService
    {
        private const string BusyReason =
            "Another save or load is already in progress; try again when it finishes.";

        private readonly ISessionRestoreAuthority _snapshotService;
        private readonly SaveSlotRepository _repository;
        private readonly ISaveWorkScheduler _scheduler;
        private readonly SessionTransitionTracker _transition;
        private readonly SaveOperationGate _gate;

        /// <summary>
        /// Cancelled when a session transition starts, so in-flight saves/loads stop. Never
        /// disposed (it is replaced on transition; the linked tokens own the registrations,
        /// so the old source is collected once the in-flight operations finish).
        /// </summary>
        private CancellationTokenSource _sessionCancellation = new CancellationTokenSource();

        public SaveSessionService(
            ISessionRestoreAuthority snapshotService,
            SaveSlotRepository repository,
            ISaveWorkScheduler scheduler,
            SessionTransitionTracker transition,
            SaveOperationGate gate)
        {
            _snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            _transition = transition ?? throw new ArgumentNullException(nameof(transition));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        }

        /// <summary>The slot this service owns (diagnostics / UI).</summary>
        public string SlotPath => _repository.Paths.SlotPath;

        /// <summary>True while a save or load application operation is in flight.</summary>
        public bool IsBusy => _gate.IsBusy;

        /// <summary>Diagnostics — committed saves.</summary>
        public int SaveCount { get; private set; }

        /// <summary>Diagnostics — committed loads (restores).</summary>
        public int LoadCount { get; private set; }

        // ---------- application operations ----------

        /// <summary>
        /// P11B §1/§3/§6 — capture the live session (main thread), write it to the slot on a
        /// worker, and report what happened. A failed write leaves the previous save (and the
        /// backup) exactly as they were.
        /// </summary>
        public async UniTask<SaveSlotResult> SaveAsync(CancellationToken cancellationToken = default)
        {
            await _scheduler.SwitchToMainThreadAsync();

            if (!_gate.TryEnter())
                return SaveSlotResult.Fail(SaveIoErrorKind.Busy, BusyReason, true);

            try
            {
                int generation = _transition.SessionGeneration;
                var sessionToken = _sessionCancellation.Token;

                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sessionToken))
                {
                    var token = linked.Token;

                    SectSessionSnapshotEnvelope envelope;
                    try
                    {
                        // P11A capture: reads live state + deep-clones it. Main thread only.
                        envelope = _snapshotService.CaptureEnvelope();
                    }
                    catch (Exception ex)
                    {
                        return SaveSlotResult.Fail(SaveIoErrorKind.Io,
                            "Could not capture the session: " + ex.Message, true);
                    }

                    SaveSlotResult result;
                    try
                    {
                        // Detached envelope only; the guard is re-checked by the repository
                        // immediately before the irreversible replace (§5).
                        result = await _scheduler.RunOnWorkerAsync(
                            () => _repository.Write(
                                envelope, token, () => _transition.SessionGeneration == generation),
                            token);
                    }
                    catch (OperationCanceledException)
                    {
                        return CancelledSave(sessionToken);
                    }
                    catch (Exception ex)
                    {
                        return SaveSlotResult.Fail(SaveIoErrorKind.Io,
                            "The save worker failed: " + ex.Message, true);
                    }

                    if (result.Success)
                    {
                        SaveCount++;
                        Debug.Log($"[SaveSessionService] Saved session to '{SlotPath}' " +
                                  $"({result.Metadata.DiscipleCount} disciple(s), " +
                                  $"{result.Metadata.BuildingCount} building(s), " +
                                  $"{result.Metadata.FileSizeBytes} bytes).");
                    }

                    return result;
                }
            }
            finally
            {
                _gate.Exit();
            }
        }

        /// <summary>
        /// P11B §3/§4 — read the slot on a worker (recovering from the backup when the slot is
        /// missing/corrupt), then validate and commit it on the main thread through the P11A
        /// authority. Nothing is restored unless the whole envelope validated; a failure is
        /// reported as Missing/Corrupt/UnsupportedVersion/Io and live state is untouched.
        /// </summary>
        public async UniTask<SaveSlotLoadResult> LoadAsync(CancellationToken cancellationToken = default)
        {
            await _scheduler.SwitchToMainThreadAsync();

            if (!_gate.TryEnter())
                return SaveSlotLoadResult.Fail(SaveIoErrorKind.Busy, BusyReason, null);

            try
            {
                int generation = _transition.SessionGeneration;
                var sessionToken = _sessionCancellation.Token;

                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sessionToken))
                {
                    var token = linked.Token;

                    SaveSlotLoadResult read;
                    try
                    {
                        read = await _scheduler.RunOnWorkerAsync(() => _repository.Read(token), token);
                    }
                    catch (OperationCanceledException)
                    {
                        return CancelledLoad(sessionToken);
                    }
                    catch (Exception ex)
                    {
                        return SaveSlotLoadResult.Fail(SaveIoErrorKind.Io,
                            "The load worker failed: " + ex.Message, null);
                    }

                    // Missing / Corrupt / UnsupportedVersion / Io all come back as themselves.
                    if (!read.Success) return read;

                    // §5 — the read may have finished after the player began another session.
                    if (token.IsCancellationRequested) return CancelledLoad(sessionToken);
                    if (_transition.SessionGeneration != generation)
                        return SaveSlotLoadResult.Fail(SaveIoErrorKind.Obsolete,
                            "The load belongs to a session that is no longer current; nothing was restored.",
                            read.Metadata);

                    // Main thread: validate the WHOLE envelope, then commit atomically
                    // (P11A publishes SessionRestoredMessage after the commit).
                    string failReason;
                    if (!_snapshotService.TryRestoreEnvelope(read.Envelope, out failReason))
                    {
                        var kind = read.Envelope != null &&
                                   read.Envelope.Version != SectSessionSnapshotEnvelope.CurrentVersion
                            ? SaveIoErrorKind.UnsupportedVersion
                            : SaveIoErrorKind.Corrupt;
                        return SaveSlotLoadResult.Fail(kind,
                            "The save was read but refused, so nothing was restored: " + failReason,
                            read.Metadata);
                    }

                    LoadCount++;
                    read.Note = string.IsNullOrEmpty(read.Note)
                        ? $"Restored {read.Metadata.DiscipleCount} disciple(s), " +
                          $"{read.Metadata.BuildingCount} building(s)."
                        : read.Note;

                    Debug.Log($"[SaveSessionService] Loaded session from '{SlotPath}': {read.Note}");
                    return read;
                }
            }
            finally
            {
                _gate.Exit();
            }
        }

        /// <summary>
        /// P12A — read the slot and FULLY validate the envelope on the main thread WITHOUT
        /// committing anything. The session coordinator calls this so a candidate save is
        /// proven loadable BEFORE any destructive transition work (gameplay-scene unload /
        /// live-state replacement) begins.
        ///
        /// Read-only: like <see cref="ReadMetadataAsync"/> it does not take the single-flight
        /// operation gate because it never writes. A cancelled or stale read is reported as
        /// Cancelled/Obsolete; an unreadable or invalid save is reported as itself
        /// (Missing/Corrupt/UnsupportedVersion/Io) and live state is untouched.
        /// </summary>
        public async UniTask<SaveSlotLoadResult> ValidateAsync(CancellationToken cancellationToken = default)
        {
            await _scheduler.SwitchToMainThreadAsync();

            int generation = _transition.SessionGeneration;
            var sessionToken = _sessionCancellation.Token;

            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sessionToken))
            {
                var token = linked.Token;

                SaveSlotLoadResult read;
                try
                {
                    read = await _scheduler.RunOnWorkerAsync(() => _repository.Read(token), token);
                }
                catch (OperationCanceledException)
                {
                    return CancelledLoad(sessionToken);
                }
                catch (Exception ex)
                {
                    return SaveSlotLoadResult.Fail(SaveIoErrorKind.Io,
                        "The load worker failed: " + ex.Message, null);
                }

                // Missing / Corrupt / UnsupportedVersion / Io all come back as themselves.
                if (!read.Success) return read;

                if (token.IsCancellationRequested) return CancelledLoad(sessionToken);
                if (_transition.SessionGeneration != generation)
                    return SaveSlotLoadResult.Fail(SaveIoErrorKind.Obsolete,
                        "The load belongs to a session that is no longer current; nothing was restored.",
                        read.Metadata);

                // Validate the WHOLE envelope, but commit nothing (no live state touched).
                string failReason;
                if (!_snapshotService.TryValidateEnvelope(read.Envelope, out failReason))
                {
                    var kind = read.Envelope != null &&
                               read.Envelope.Version != SectSessionSnapshotEnvelope.CurrentVersion
                        ? SaveIoErrorKind.UnsupportedVersion
                        : SaveIoErrorKind.Corrupt;
                    return SaveSlotLoadResult.Fail(kind,
                        "The save was read but refused, so nothing was restored: " + failReason,
                        read.Metadata);
                }

                read.Note = string.IsNullOrEmpty(read.Note)
                    ? "Validated (not yet restored)."
                    : read.Note;
                return read;
            }
        }

        /// <summary>
        /// P11B §2 — what the Continue UI needs: does a save exist, when was it written, how
        /// big is it. Never restores anything, never starts a New Game, and reports a
        /// missing/corrupt/unsupported save as itself.
        /// </summary>
        public async UniTask<SaveSlotMetadata> ReadMetadataAsync(CancellationToken cancellationToken = default)
        {
            await _scheduler.SwitchToMainThreadAsync();

            var sessionToken = _sessionCancellation.Token;
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sessionToken))
            {
                var token = linked.Token;
                try
                {
                    return await _scheduler.RunOnWorkerAsync(
                        () => _repository.ReadMetadata(token), token);
                }
                catch (OperationCanceledException)
                {
                    return SaveSlotMetadata.Unusable(SaveIoErrorKind.Cancelled,
                        "The save metadata read was cancelled.", SlotPath);
                }
                catch (Exception ex)
                {
                    return SaveSlotMetadata.Unusable(SaveIoErrorKind.Io,
                        "Could not read the save metadata: " + ex.Message, SlotPath);
                }
            }
        }

        /// <summary>
        /// P11B §5 — the player began another game (New Game / Load a different save).
        ///
        /// Bumps the session generation FIRST (so an in-flight commit guard already fails),
        /// then cancels the session token, so every in-flight save/load finishes as
        /// Cancelled/Obsolete instead of overwriting the new session's slot. Main-thread
        /// only: it is a session-lifecycle call.
        /// </summary>
        public void BeginSessionTransition()
        {
            _transition.BeginTransition();

            var previous = _sessionCancellation;
            _sessionCancellation = new CancellationTokenSource();
            previous.Cancel();
        }

        // ---------- failure shapes ----------

        private static SaveSlotResult CancelledSave(CancellationToken sessionToken)
        {
            return SaveSlotResult.Fail(SaveIoErrorKind.Cancelled,
                sessionToken.IsCancellationRequested
                    ? "The save was cancelled because the session changed; it was not committed."
                    : "The save was cancelled before it was committed.",
                true);
        }

        private static SaveSlotLoadResult CancelledLoad(CancellationToken sessionToken)
        {
            return SaveSlotLoadResult.Fail(SaveIoErrorKind.Cancelled,
                sessionToken.IsCancellationRequested
                    ? "The load was cancelled because the session changed; nothing was restored."
                    : "The load was cancelled; nothing was restored.",
                null);
        }
    }
}
