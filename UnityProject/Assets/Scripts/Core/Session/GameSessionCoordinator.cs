using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using UnityEngine;
using VContainer.Unity;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    /// <summary>
    /// P12A — the ONE application-level owner of the game-session lifecycle, layered on
    /// top of the existing additive <see cref="SceneLoader"/>. It does NOT create a second
    /// composition root or message bus: it drives the existing loader, the existing
    /// persistence services, and the existing simulation authority.
    ///
    /// Lifecycle:
    ///   Title → StartingNewGame → Playing
    ///   Title → LoadingGame     → Playing
    ///   Playing → ReturningToTitle → Title
    ///
    /// Guarantees:
    ///   * Gameplay ticking requires <see cref="GameSessionPhase.Playing"/> — the shared
    ///     simulation clock is frozen (a SEPARATE fact from a player/decision pause) while
    ///     the phase is otherwise; see <see cref="TimeSystem.SetSessionActive"/>.
    ///   * A New Game builds fresh state through <see cref="IStarterStateFactory"/> and
    ///     resets accumulators / AI dwell / pending decisions / ownership &amp; membership /
    ///     session-scoped observability.
    ///   * Load reads + fully validates the candidate save BEFORE any destructive work, and
    ///     a failed scene load rolls back to a recoverable Title with live state untouched.
    ///   * Overlapping / duplicate requests are refused while a transition is in flight;
    ///     in-flight save/load completions from a previous session are invalidated through
    ///     <see cref="SessionTransitionTracker"/>.
    ///   * Returning to Title unloads gameplay content, closes session UI, stops
    ///     simulation, completes outstanding world-event waiters with a defined outcome,
    ///     and clears the world-event handoff cache. Persistent infrastructure stays alive.
    ///
    /// Main-thread only: every method touches live gameplay state / Unity scene APIs.
    /// </summary>
    public sealed class GameSessionCoordinator : IGameSessionCoordinator, IStartable, IDisposable
    {
        private readonly IGameplaySceneLoader _sceneLoader;
        private readonly ISessionRestoreAuthority _snapshots;
        private readonly TimeSystem _timeSystem;
        private readonly IStarterStateFactory _starterFactory;
        private readonly SessionTransitionTracker _transition;
        private readonly SaveSessionService _saves;
        private readonly AutoTaskScheduler _autoScheduler;
        private readonly OwnershipObservabilityBuffer _ownership;
        private readonly TaskChangeObservabilityBuffer _taskChange;
        private readonly ISessionUiCloser _uiCloser;
        private readonly IPublisher<SessionPhaseChangedMessage> _phasePublisher;

        private GameSessionPhase _phase = GameSessionPhase.Title;
        private bool _transitioning;
        private string _lastError = string.Empty;
        private int _completedTransitions;
        private bool _disposed;

        public GameSessionCoordinator(
            IGameplaySceneLoader sceneLoader,
            ISessionRestoreAuthority snapshots,
            TimeSystem timeSystem,
            IStarterStateFactory starterFactory,
            SessionTransitionTracker transition,
            SaveSessionService saves = null,
            AutoTaskScheduler autoScheduler = null,
            OwnershipObservabilityBuffer ownership = null,
            TaskChangeObservabilityBuffer taskChange = null,
            ISessionUiCloser uiCloser = null,
            IPublisher<SessionPhaseChangedMessage> phasePublisher = null)
        {
            _sceneLoader = sceneLoader;
            _snapshots = snapshots;
            _timeSystem = timeSystem;
            _starterFactory = starterFactory;
            _transition = transition;
            _saves = saves;
            _autoScheduler = autoScheduler;
            _ownership = ownership;
            _taskChange = taskChange;
            _uiCloser = uiCloser;
            _phasePublisher = phasePublisher;

            // P12A — the coordinator starts in Title, so the simulation clock is inactive
            // from construction rather than from the first transition: nothing may tick —
            // and no mutation may be accepted — before a Playing session exists.
            _timeSystem?.SetSessionActive(false);
        }

        public GameSessionPhase Phase => _phase;
        public bool IsPlaying => _phase == GameSessionPhase.Playing && !_transitioning;
        public bool IsTransitioning => _transitioning;
        public string LastTransitionError => _lastError;
        public int SessionGeneration => _transition != null ? _transition.SessionGeneration : 0;
        public int CompletedTransitions => _completedTransitions;

        /// <summary>
        /// Entry point — boot default. There is no Title prefab yet, so the boot behaviour
        /// (gameplay auto-loads) is preserved by immediately requesting a New Game. When
        /// the Title UI lands this becomes a user-driven call and nothing else changes.
        /// </summary>
        public void Start()
        {
            if (_disposed) return;
            Debug.Log("[GameSessionCoordinator] boot: no Title UI yet — starting a new game session");
            StartNewGameAsync().Forget();
        }

        // ---------- New Game ----------

        public async UniTask<SessionTransitionResult> StartNewGameAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) return SessionTransitionResult.Busy(_phase, "The session coordinator is disposed.");
            if (_transitioning)
                return SessionTransitionResult.Busy(_phase, "A session transition is already in progress.");

            var previous = _phase;
            _transitioning = true;
            _lastError = string.Empty;
            SetPhase(GameSessionPhase.StartingNewGame);

            try
            {
                // 1. Invalidate the previous session first: in-flight saves/loads must not
                //    commit, the simulation clock stops, waiters complete, session-scoped
                //    observability / AI schedule are cleared.
                BeginSessionBoundary("a new game started");

                // 2. Load gameplay content BEFORE replacing live state, so a failed scene
                //    load rolls back with live state untouched.
                string sceneError = await TryLoadGameplaySceneAsync(cancellationToken);
                if (!string.IsNullOrEmpty(sceneError))
                    return FailToTitle(previous, sceneError);

                // 3. Fresh starter state through the factory, validated + committed by the
                //    P11A authority (accumulators/ownership/membership all reset there).
                var starter = _starterFactory != null ? _starterFactory.CreateStarterSnapshot() : null;
                if (!_snapshots.TryApplyNewSession(starter, out string stateError))
                {
                    await SafeUnloadAsync();
                    return FailToTitle(previous, "Could not create a new session: " + stateError);
                }

                // 4. Playing.
                EnterPlaying();
                _completedTransitions++;
                Debug.Log("[GameSessionCoordinator] New game session started.");
                return SessionTransitionResult.Ok(_phase, previous);
            }
            catch (OperationCanceledException)
            {
                await SafeUnloadAsync();
                return SessionTransitionResult.Cancelled(_phase, previous);
            }
            finally
            {
                _transitioning = false;
            }
        }

        // ---------- Load ----------

        public async UniTask<SessionTransitionResult> LoadGameAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) return SessionTransitionResult.Busy(_phase, "The session coordinator is disposed.");
            if (_transitioning)
                return SessionTransitionResult.Busy(_phase, "A session transition is already in progress.");

            var previous = _phase;
            _transitioning = true;
            _lastError = string.Empty;
            SetPhase(GameSessionPhase.LoadingGame);

            try
            {
                if (_saves == null)
                    return FailToTitle(previous, "No save/load service is available.");

                // 1. Read + FULLY validate the candidate save before any destructive work.
                //    A save that does not validate leaves live state and the scene untouched.
                var read = await _saves.ValidateAsync(cancellationToken);
                if (!read.Success)
                {
                    // Nothing destructive has happened yet — return to the phase we came
                    // from (Title or Playing) instead of tearing the session down.
                    SetPhase(previous);
                    _lastError = read.Error;
                    Debug.LogWarning("[GameSessionCoordinator] Load refused: " + read.Error);
                    return SessionTransitionResult.Failed(previous, previous, read.Error);
                }

                // 2. Session boundary now that the save is known-loadable.
                BeginSessionBoundary("a save is being loaded");

                // 3. Load gameplay content (destructive). A failure here leaves live state
                //    untouched because the restore has not been committed yet.
                string sceneError = await TryLoadGameplaySceneAsync(cancellationToken);
                if (!string.IsNullOrEmpty(sceneError))
                    return FailToTitle(previous, sceneError);

                // 4. Commit the already-validated restore on the main thread. The authority
                //    re-validates and publishes SessionRestoredMessage only after the commit.
                if (!_snapshots.TryRestoreEnvelope(read.Envelope, out string restoreError))
                {
                    await SafeUnloadAsync();
                    return FailToTitle(previous, "The save was validated but refused at commit: " + restoreError);
                }

                EnterPlaying();
                _completedTransitions++;
                Debug.Log("[GameSessionCoordinator] Loaded session" +
                          (read.RecoveredFromBackup ? " (recovered from backup)" : string.Empty) + ".");
                return SessionTransitionResult.Ok(_phase, previous);
            }
            catch (OperationCanceledException)
            {
                await SafeUnloadAsync();
                return SessionTransitionResult.Cancelled(_phase, previous);
            }
            finally
            {
                _transitioning = false;
            }
        }

        // ---------- Return to Title ----------

        public async UniTask<SessionTransitionResult> ReturnToTitleAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) return SessionTransitionResult.Busy(_phase, "The session coordinator is disposed.");
            if (_transitioning)
                return SessionTransitionResult.Busy(_phase, "A session transition is already in progress.");

            if (_phase == GameSessionPhase.Title)
                return SessionTransitionResult.AlreadyInPhase(GameSessionPhase.Title);

            var previous = _phase;
            _transitioning = true;
            _lastError = string.Empty;
            SetPhase(GameSessionPhase.ReturningToTitle);

            try
            {
                // Session-scoped teardown. Persistent infrastructure (bus, TCP transport,
                // persistence systems, UI root) is deliberately NOT disposed here.
                BeginSessionBoundary("returned to title");
                _snapshots?.ClearSessionAuthority();

                // Close session UI while the state it displays still exists.
                _uiCloser?.CloseSessionUi();

                // Unload gameplay content; this disposes the scene child LifetimeScope, so
                // every scene-specific subscription (camera rig, overlay, backdrop) is
                // detached as part of the normal unload path.
                await SafeUnloadAsync();

                SetPhase(GameSessionPhase.Title);
                _completedTransitions++;
                Debug.Log("[GameSessionCoordinator] Returned to title.");
                return SessionTransitionResult.Ok(GameSessionPhase.Title, previous);
            }
            finally
            {
                _transitioning = false;
            }
        }

        // ---------- internals ----------

        /// <summary>
        /// Everything that must happen exactly once at the seam between two sessions:
        /// invalidate in-flight save/load completions, stop the simulation clock, complete
        /// outstanding world-event waiters, and clear session-scoped derived state.
        /// </summary>
        private void BeginSessionBoundary(string reason)
        {
            // §5 — a completion from the session we are leaving must never land in the new one.
            if (_saves != null) _saves.BeginSessionTransition();
            else _transition?.BeginTransition();

            // Stop gameplay progression as a SEPARATE fact from a pause, clear the
            // authoritative pending decision, drop the stale world-event handoff cache and
            // complete any outstanding awaiter with a defined "session ended" response.
            _timeSystem?.EndSession(reason);

            // Session-scoped observability describes the session being left.
            _ownership?.Clear();
            _taskChange?.Clear();

            // The AI scheduling/dwell maps reference the previous roster; restart them.
            _autoScheduler?.ResetDerivedSchedule();
        }

        private void EnterPlaying()
        {
            _timeSystem?.SetSessionActive(true);
            SetPhase(GameSessionPhase.Playing);
        }

        /// <summary>
        /// Recoverable failure: leave the coordinator idle and usable, ensure the world is
        /// frozen, close session UI and land on Title. Live gameplay state is whatever the
        /// last committed session produced (a failed New Game / Load never half-applies).
        /// </summary>
        private SessionTransitionResult FailToTitle(GameSessionPhase previous, string error)
        {
            _lastError = string.IsNullOrEmpty(error) ? "unknown session transition failure" : error;
            _timeSystem?.EndSession("session transition failed");
            _uiCloser?.CloseSessionUi();
            SetPhase(GameSessionPhase.Title);
            Debug.LogError("[GameSessionCoordinator] Session transition failed: " + _lastError);
            return SessionTransitionResult.Failed(GameSessionPhase.Title, previous, _lastError);
        }

        /// <summary>
        /// Load the gameplay scene. Returns null/empty on success, or the failure reason.
        /// (An <c>async</c> method cannot carry an <c>out</c> parameter.)
        /// </summary>
        private async UniTask<string> TryLoadGameplaySceneAsync(CancellationToken cancellationToken)
        {
            if (_sceneLoader == null)
                return "No gameplay scene loader is available.";

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _sceneLoader.LoadGameplayScene(SceneNames.Sect);
                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return "Could not load the gameplay scene '" + SceneNames.Sect + "': " + ex.Message;
            }
        }

        private async UniTask SafeUnloadAsync()
        {
            if (_sceneLoader == null) return;
            try
            {
                await _sceneLoader.UnloadCurrentGameplayScene();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GameSessionCoordinator] Gameplay scene unload failed: " + ex.Message);
            }
        }

        private void SetPhase(GameSessionPhase next)
        {
            if (_phase == next) return;
            var previous = _phase;
            _phase = next;
            _phasePublisher?.Publish(new SessionPhaseChangedMessage { Previous = previous, Current = next });
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
