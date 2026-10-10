using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MessagePipe;
using UnityEngine;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    /// <summary>
    /// P11A/P12A — the ONE authority that knows whether a full session is authoritative,
    /// and that owns capture, validate-only and restore. A consumer that persists a
    /// narrower slice (viewer membership) asks this before it auto-restores, so it can
    /// never overwrite a full-session load; the P11B save service and the P12A session
    /// coordinator drive capture/validate/restore through the SAME instance.
    ///
    /// Kept as an interface so those consumers do not depend on the snapshot service's
    /// concrete construction (and so tests can pass a trivial stub). Widened in P12A so
    /// the single registration serves every caller without a second instance.
    /// </summary>
    public interface ISessionRestoreAuthority
    {
        /// <summary>True once a full-session snapshot has been committed to live state.</summary>
        bool HasFullSessionAuthority { get; }

        /// <summary>Capture a detached, versioned envelope of the live session (main thread only).</summary>
        SectSessionSnapshotEnvelope CaptureEnvelope();

        /// <summary>Validate a versioned envelope in full WITHOUT committing anything.</summary>
        bool TryValidateEnvelope(SectSessionSnapshotEnvelope envelope, out string failReason);

        /// <summary>Validate + commit a versioned envelope (publishes the replaced-state notification after commit).</summary>
        bool TryRestoreEnvelope(SectSessionSnapshotEnvelope envelope, out string failReason);

        /// <summary>Validate + commit a fresh starter snapshot for a New Game (never a restore).</summary>
        bool TryApplyNewSession(SectSessionSnapshot snapshot, out string failReason);

        /// <summary>Drop the full-session authority flag when a session ends.</summary>
        void ClearSessionAuthority();
    }

    /// <summary>
    /// P11A — the single orchestration owner of full-session restore.
    ///
    /// Responsibilities:
    ///   1. Capture a detached, versioned snapshot (economy + work accumulators from
    ///      <see cref="ISectStateProvider"/>, simulation state from
    ///      <see cref="TimeSystem"/>).
    ///   2. On restore, validate EVERYTHING (envelope version + simulation portion +
    ///      gameplay portion) before replacing any live state. An invalid restore
    ///      leaves the running session untouched.
    ///   3. Commit gameplay state through the authority
    ///      (<see cref="ISectStateProvider.TryApplySessionSnapshot"/>) and simulation
    ///      state through <see cref="TimeSystem.RestoreSimulationState"/>, then — and
    ///      only then — publish <see cref="SessionRestoredMessage"/> so derived
    ///      systems (occupancy, visuals, AI scheduling, HUD) invalidate their caches.
    ///
    /// Main-thread only: capture/restore touch live Unity gameplay state. The
    /// synchronous methods are the ones the main thread calls; the async wrappers
    /// hop first (E1-pre pattern) so a background caller can never mutate live state
    /// off-thread. Background file I/O may only ever serialize a detached snapshot.
    ///
    /// No disk I/O here by design (P11A does not implement save/load I/O or Title UI).
    /// </summary>
    public class SessionSnapshotService : ISessionRestoreAuthority
    {
        private readonly ISectStateProvider _stateProvider;
        private readonly TimeSystem _timeSystem;
        private readonly IPublisher<SessionRestoredMessage> _restoredPublisher;
        private readonly IClock _clock;

        /// <summary>True once a full-session snapshot has been committed.</summary>
        public bool HasFullSessionAuthority { get; private set; }

        /// <summary>True while a restore is committing — the session transition gate.</summary>
        public bool IsTransitioning { get; private set; }

        /// <summary>Reason from the last rejected restore (empty when the last attempt succeeded / none ran).</summary>
        public string LastRestoreError { get; private set; } = string.Empty;

        /// <summary>Diagnostics — how many restores have committed.</summary>
        public int RestoreCount { get; private set; }

        public SessionSnapshotService(
            ISectStateProvider stateProvider,
            TimeSystem timeSystem,
            IPublisher<SessionRestoredMessage> restoredPublisher,
            IClock clock = null)
        {
            _stateProvider = stateProvider;
            _timeSystem = timeSystem;
            _restoredPublisher = restoredPublisher;
            _clock = clock ?? new UtcClock();
        }

        // ---------- capture ----------

        /// <summary>
        /// P11A — capture a detached full-session snapshot (economy + accumulators
        /// from the state authority, simulation speed + pending decision from
        /// TimeSystem). Read-only; main-thread only.
        /// </summary>
        public SectSessionSnapshot CaptureSnapshot()
        {
            var snapshot = _stateProvider.CaptureSessionSnapshot();
            if (snapshot == null) snapshot = new SectSessionSnapshot();

            snapshot.SimulationSpeed = _timeSystem != null ? _timeSystem.Speed : 1;
            snapshot.PendingEventId = _timeSystem != null ? _timeSystem.PendingEventId : null;
            snapshot.PendingDescription = _timeSystem != null ? _timeSystem.PendingDescription : null;
            snapshot.PendingChoices = _timeSystem != null
                ? new List<EventChoiceInfo>(_timeSystem.PendingChoices)
                : new List<EventChoiceInfo>();
            snapshot.PendingDecisionPaused = _timeSystem != null && _timeSystem.IsPendingDecisionPaused;

            return snapshot;
        }

        /// <summary>P11A — wrap a fresh snapshot in the versioned envelope (stamped with real UTC).</summary>
        public SectSessionSnapshotEnvelope CaptureEnvelope()
        {
            return new SectSessionSnapshotEnvelope
            {
                Version = SectSessionSnapshotEnvelope.CurrentVersion,
                SavedAtUtc = _clock.UtcNow,
                Snapshot = CaptureSnapshot(),
            };
        }

        // ---------- restore ----------

        /// <summary>
        /// P11A — validate + commit a versioned envelope. The version must be exactly
        /// <see cref="SectSessionSnapshotEnvelope.CurrentVersion"/>; an older/newer/
        /// missing version fails closed with an explicit reason (no migration exists
        /// yet — add one, and bump CurrentVersion, when the shape changes).
        /// </summary>
        public bool TryRestoreEnvelope(SectSessionSnapshotEnvelope envelope, out string failReason)
        {
            failReason = string.Empty;
            LastRestoreError = string.Empty;

            if (!TryCheckEnvelopeVersion(envelope, out string versionReason))
                return Fail(versionReason, out failReason);

            return TryRestore(envelope.Snapshot, out failReason);
        }

        /// <summary>
        /// P12A — validate a versioned envelope in FULL without committing anything. Used by
        /// the session coordinator to prove a candidate save is loadable BEFORE any
        /// destructive transition work begins (scene unload / live-state replacement). A
        /// false result leaves live state, the loaded scene and the pending decision
        /// untouched. Never throws for invalid data.
        /// </summary>
        public bool TryValidateEnvelope(SectSessionSnapshotEnvelope envelope, out string failReason)
        {
            failReason = string.Empty;

            if (!TryCheckEnvelopeVersion(envelope, out failReason)) return false;

            var snapshot = envelope.Snapshot;
            if (snapshot == null)
            {
                failReason = "Session envelope has no snapshot payload.";
                return false;
            }

            if (!ValidateSimulationPortion(snapshot, out failReason)) return false;
            if (!_stateProvider.TryValidateSessionSnapshot(snapshot, out failReason)) return false;
            return true;
        }

        /// <summary>
        /// P12A — commit a FRESH starter snapshot for a New Game. The candidate is validated
        /// in full before anything is replaced (the same rules a restore uses), then applied
        /// and the simulation state reset to the starter's speed with no pending decision.
        ///
        /// This is deliberately NOT a restore: it publishes the same "authoritative state
        /// was replaced" notification (<see cref="SessionRestoredMessage"/>) so derived
        /// systems (occupancy, visuals, AI schedule, HUD) rebuild, but it does not increment
        /// <see cref="RestoreCount"/>. Accumulators, the real-UTC task-change cooldown and
        /// per-tick scratch are reset by the authority's apply path.
        /// </summary>
        public bool TryApplyNewSession(SectSessionSnapshot snapshot, out string failReason)
        {
            failReason = string.Empty;
            LastRestoreError = string.Empty;

            if (snapshot == null)
                return Fail("Starter session snapshot is null.", out failReason);

            if (IsTransitioning)
                return Fail("A session transition is already in progress.", out failReason);

            IsTransitioning = true;
            try
            {
                if (!ValidateSimulationPortion(snapshot, out failReason))
                {
                    LastRestoreError = failReason;
                    return false;
                }

                if (!_stateProvider.TryValidateSessionSnapshot(snapshot, out failReason))
                {
                    LastRestoreError = failReason;
                    return false;
                }

                _stateProvider.TryApplySessionSnapshot(snapshot);
                // The coordinator has already cleared pause reasons (TimeSystem.EndSession);
                // a fresh session has speed 1x and no pending decision or bridge cache.
                _timeSystem.RestoreSimulationState(snapshot.SimulationSpeed, null, null, null, false);
            }
            finally
            {
                IsTransitioning = false;
            }

            HasFullSessionAuthority = true;

            var economy = snapshot.Economy;
            _restoredPublisher?.Publish(new SessionRestoredMessage
            {
                FullSession = true,
                RestoredAtUtc = _clock.UtcNow,
                DiscipleCount = economy != null && economy.Disciples != null ? economy.Disciples.Count : 0,
                BuildingCount = economy != null && economy.PlacedBuildings != null ? economy.PlacedBuildings.Count : 0,
            });

            Debug.Log($"[SessionSnapshotService] New session starter state applied: " +
                      $"{CountDisciples(snapshot)} disciple(s), {CountBuildings(snapshot)} building(s), " +
                      $"speed {snapshot.SimulationSpeed}x.");
            return true;
        }

        /// <summary>
        /// P12A — drop the coordinator's full-session authority when a session ends. The
        /// SessionGeneration remains the real validity fact; this flag only gates the
        /// narrower membership-slice auto-import, so a stale "full session is authoritative"
        /// flag cannot linger into the next session's startup.
        /// </summary>
        public void ClearSessionAuthority()
        {
            HasFullSessionAuthority = false;
            LastRestoreError = string.Empty;
        }

        /// <summary>
        /// P12A — the envelope version gate shared by the restore and validate-only paths.
        /// Missing / newer / older-than-supported all fail closed with an explicit reason.
        /// </summary>
        private static bool TryCheckEnvelopeVersion(SectSessionSnapshotEnvelope envelope, out string failReason)
        {
            failReason = string.Empty;

            if (envelope == null)
            {
                failReason = "Session envelope is null.";
                return false;
            }

            if (envelope.Version == 0)
            {
                failReason = "Session envelope has no readable format version (missing or pre-versioned).";
                return false;
            }

            if (envelope.Version > SectSessionSnapshotEnvelope.CurrentVersion)
            {
                failReason = $"Session envelope version {envelope.Version} is newer than the supported version " +
                             $"{SectSessionSnapshotEnvelope.CurrentVersion}.";
                return false;
            }

            if (envelope.Version < SectSessionSnapshotEnvelope.CurrentVersion)
            {
                failReason = $"Session envelope version {envelope.Version} is older than the supported version " +
                             $"{SectSessionSnapshotEnvelope.CurrentVersion} and no migration is defined.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// P11A — validate + commit a detached snapshot on the Unity main thread.
        /// The candidate is validated in full BEFORE any live state is replaced, so a
        /// failure leaves the running session byte-for-byte unchanged. On success the
        /// session-transition gate is released and <see cref="SessionRestoredMessage"/>
        /// is published exactly once. Never throws for invalid data.
        /// </summary>
        public bool TryRestore(SectSessionSnapshot snapshot, out string failReason)
        {
            failReason = string.Empty;
            LastRestoreError = string.Empty;

            if (snapshot == null)
                return Fail("Session snapshot is null.", out failReason);

            if (IsTransitioning)
                return Fail("A session transition is already in progress.", out failReason);

            IsTransitioning = true;
            try
            {
                // 1. simulation portion (TimeSystem domain) — speed range + pending choices.
                if (!ValidateSimulationPortion(snapshot, out failReason))
                {
                    LastRestoreError = failReason;
                    return false;
                }

                // 2. gameplay portion (state authority) — full candidate validation.
                if (!_stateProvider.TryValidateSessionSnapshot(snapshot, out failReason))
                {
                    LastRestoreError = failReason;
                    return false;
                }

                // 3. commit — neither step can fail after the validation above.
                _stateProvider.TryApplySessionSnapshot(snapshot);
                _timeSystem.RestoreSimulationState(
                    snapshot.SimulationSpeed,
                    snapshot.PendingEventId,
                    snapshot.PendingDescription,
                    snapshot.PendingChoices,
                    snapshot.PendingDecisionPaused);
            }
            finally
            {
                IsTransitioning = false;
            }

            // 4. authority + coherent notification AFTER commit.
            HasFullSessionAuthority = true;
            RestoreCount++;

            var economy = snapshot.Economy;
            _restoredPublisher?.Publish(new SessionRestoredMessage
            {
                FullSession = true,
                RestoredAtUtc = _clock.UtcNow,
                DiscipleCount = economy != null && economy.Disciples != null ? economy.Disciples.Count : 0,
                BuildingCount = economy != null && economy.PlacedBuildings != null ? economy.PlacedBuildings.Count : 0,
            });

            Debug.Log($"[SessionSnapshotService] Session restored: " +
                      $"{CountDisciples(snapshot)} disciple(s), " +
                      $"{CountBuildings(snapshot)} building(s), speed {snapshot.SimulationSpeed}x" +
                      (snapshot.HasPendingDecision ? $", pending decision '{snapshot.PendingEventId}'" : string.Empty) + ".");
            return true;
        }

        private static int CountDisciples(SectSessionSnapshot snapshot)
            => snapshot.Economy != null && snapshot.Economy.Disciples != null ? snapshot.Economy.Disciples.Count : 0;

        private static int CountBuildings(SectSessionSnapshot snapshot)
            => snapshot.Economy != null && snapshot.Economy.PlacedBuildings != null ? snapshot.Economy.PlacedBuildings.Count : 0;

        /// <summary>
        /// P11A — main-thread hop for a restore initiated off the Unity main thread.
        /// Live gameplay state is never touched off-thread; the reason lands on
        /// <see cref="LastRestoreError"/> (an awaitable cannot carry an out param).
        /// </summary>
        public async UniTask<bool> TryRestoreEnvelopeAsync(SectSessionSnapshotEnvelope envelope)
        {
            await UniTask.SwitchToMainThread();
            return TryRestoreEnvelope(envelope, out _);
        }

        /// <summary>P11A — main-thread hop for a snapshot restore initiated off-thread.</summary>
        public async UniTask<bool> TryRestoreAsync(SectSessionSnapshot snapshot)
        {
            await UniTask.SwitchToMainThread();
            return TryRestore(snapshot, out _);
        }

        // ---------- validation (simulation portion) ----------

        private static bool ValidateSimulationPortion(SectSessionSnapshot snapshot, out string failReason)
        {
            failReason = string.Empty;

            if (snapshot.SimulationSpeed < TimeSystem.MinSpeed || snapshot.SimulationSpeed > TimeSystem.MaxSpeed)
            {
                failReason = $"Session snapshot simulation speed {snapshot.SimulationSpeed} is outside the " +
                             $"supported range {TimeSystem.MinSpeed}..{TimeSystem.MaxSpeed}.";
                return false;
            }

            if (snapshot.PendingChoices != null)
            {
                for (int i = 0; i < snapshot.PendingChoices.Count; i++)
                {
                    var choice = snapshot.PendingChoices[i];
                    if (choice == null || string.IsNullOrEmpty(choice.ChoiceId))
                    {
                        failReason = $"Session snapshot pending decision '{snapshot.PendingEventId}' has an " +
                                     $"invalid choice at index {i}.";
                        return false;
                    }
                }
            }

            return true;
        }

        private bool Fail(string reason, out string failReason)
        {
            failReason = reason;
            LastRestoreError = reason;
            Debug.LogWarning($"[SessionSnapshotService] Session restore refused: {reason}");
            return false;
        }
    }
}
