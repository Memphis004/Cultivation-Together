using System.Threading;
using Cysharp.Threading.Tasks;

namespace Xianxia.Sect
{
    /// <summary>
    /// P12A — the narrow read-only session fact every mutation path consults: is a live
    /// gameplay session in the <see cref="GameSessionPhase.Playing"/> phase right now.
    ///
    /// Kept separate from <see cref="TimePauseReason"/> on purpose: "no active game"
    /// (Title / transitioning) must never be reported as a player pause or a
    /// pending-decision pause. A mutation handler only needs this bool; it does not need
    /// the whole coordinator.
    /// </summary>
    public interface ISessionGate
    {
        /// <summary>True only while a live session is fully in the Playing phase.</summary>
        bool IsPlaying { get; }
    }

    /// <summary>P12A — the exact refusal text handed back when a mutation arrives outside Playing.</summary>
    public static class SessionGate
    {
        public const string NoActiveSessionReason =
            "no active session: gameplay is not in a Playing state (Title or transitioning)";
    }

    /// <summary>
    /// P12A — the application-level session coordinator. Owns the explicit lifecycle
    /// (Title → StartingNewGame / LoadingGame → Playing → ReturningToTitle) on top of the
    /// existing additive <see cref="SceneLoader"/>; it never creates a second composition
    /// root or message bus.
    /// </summary>
    public interface IGameSessionCoordinator : ISessionGate
    {
        /// <summary>The current lifecycle phase.</summary>
        GameSessionPhase Phase { get; }

        /// <summary>True while a transition is in flight (mutations are refused).</summary>
        bool IsTransitioning { get; }

        /// <summary>Reason from the last failed transition; empty when the last transition succeeded / none ran.</summary>
        string LastTransitionError { get; }

        /// <summary>The live session generation (delegates to <see cref="SessionTransitionTracker"/>).</summary>
        int SessionGeneration { get; }

        /// <summary>Diagnostics — how many transitions have completed (New Game / Load / Return to Title).</summary>
        int CompletedTransitions { get; }

        /// <summary>
        /// Create a fresh session from the starter-state factory, load the gameplay scene
        /// and enter Playing. A failed scene load leaves live state untouched and returns
        /// to a recoverable Title phase.
        /// </summary>
        UniTask<SessionTransitionResult> StartNewGameAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Validate a candidate save (read + full envelope validation) BEFORE any
        /// destructive work, then load the gameplay scene and commit the restore. A save
        /// that does not validate is reported and never replaces live state.
        /// </summary>
        UniTask<SessionTransitionResult> LoadGameAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Tear the live session down: unload gameplay content, close session UI, stop
        /// simulation and complete the session's outstanding world-event waiters.
        /// Persistent infrastructure (bus, TCP, persistence systems) stays alive.
        /// </summary>
        UniTask<SessionTransitionResult> ReturnToTitleAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// P12A — builds the fresh state a New Game starts from. The prototype adapter
    /// (<see cref="PrototypeStarterStateFactory"/>) reuses <c>MockSectData</c>; keeping it
    /// behind an interface means the starter state can become authored content later
    /// without touching the coordinator.
    /// </summary>
    public interface IStarterStateFactory
    {
        /// <summary>Build a detached snapshot of the fresh start state. Never aliases live state.</summary>
        SectSessionSnapshot CreateStarterSnapshot();
    }

    /// <summary>
    /// P12A — the seam that closes session-scoped UI when a session ends. Production
    /// closes the interactive session panels through <see cref="Xianxia.Sect.UI.UIService"/>;
    /// persistent HUD infrastructure is deliberately kept alive. Tests inject a fake.
    /// </summary>
    public interface ISessionUiCloser
    {
        /// <summary>Close session-scoped UI. Returns how many panels were actually closed.</summary>
        int CloseSessionUi();
    }

    /// <summary>
    /// P12A — the additive-scene operations the coordinator drives. Implemented by the
    /// existing <see cref="SceneLoader"/> so there is exactly ONE gameplay-scene loader
    /// in the project; tests inject a deterministic fake instead of touching Unity scenes.
    /// </summary>
    public interface IGameplaySceneLoader
    {
        UniTask LoadGameplayScene(string sceneName);
        UniTask UnloadCurrentGameplayScene();
        string GetCurrentGameplayScene();
    }
}
