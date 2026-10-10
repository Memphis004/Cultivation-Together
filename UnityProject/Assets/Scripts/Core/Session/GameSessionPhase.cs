namespace Xianxia.Sect
{
    /// <summary>
    /// P12A — the explicit lifecycle of ONE game session, layered on top of the
    /// additive <see cref="SceneLoader"/>. This is NOT a pause flag: a session can be
    /// in a non-Playing phase (Title / transitioning) while the pause reasons
    /// (User / PendingDecision) are all clear, and vice versa.
    /// </summary>
    public enum GameSessionPhase
    {
        /// <summary>No gameplay session is live; gameplay content is unloaded.</summary>
        Title = 0,

        /// <summary>A brand-new session is being created (fresh starter state + scene load).</summary>
        StartingNewGame = 1,

        /// <summary>A saved session is being read, validated and loaded.</summary>
        LoadingGame = 2,

        /// <summary>A live session exists: gameplay ticks, mutations are accepted.</summary>
        Playing = 3,

        /// <summary>The live session is being torn down (unload scene, close session UI).</summary>
        ReturningToTitle = 4,
    }

    /// <summary>P12A — the terminal outcome of a session-transition request.</summary>
    public enum SessionTransitionOutcome
    {
        /// <summary>The requested transition completed; the session is now Playing (or Title).</summary>
        Completed = 0,

        /// <summary>The request was a no-op (already in the requested phase).</summary>
        AlreadyInPhase = 1,

        /// <summary>Another transition is in flight; this duplicate/overlapping request was refused.</summary>
        Busy = 2,

        /// <summary>The request ran and failed; the coordinator is idle and recoverable.</summary>
        Failed = 3,

        /// <summary>The request was cancelled before it completed; live state is unchanged.</summary>
        Cancelled = 4,
    }

    /// <summary>
    /// P12A — result of a session-transition request. Never throws for a normal failure:
    /// the caller inspects <see cref="Success"/> / <see cref="Outcome"/>.
    /// </summary>
    public sealed class SessionTransitionResult
    {
        public bool Success { get; private set; }
        public SessionTransitionOutcome Outcome { get; private set; }

        /// <summary>Phase the coordinator ended up in (may be Title after a recoverable failure).</summary>
        public GameSessionPhase Phase { get; private set; }

        /// <summary>Phase the coordinator was in when the request began.</summary>
        public GameSessionPhase PreviousPhase { get; private set; }

        /// <summary>Human-readable reason for a refusal/failure; empty on success.</summary>
        public string Error { get; private set; } = string.Empty;

        public static SessionTransitionResult Ok(GameSessionPhase phase, GameSessionPhase previous)
        {
            return new SessionTransitionResult
            {
                Success = true,
                Outcome = SessionTransitionOutcome.Completed,
                Phase = phase,
                PreviousPhase = previous,
            };
        }

        public static SessionTransitionResult AlreadyInPhase(GameSessionPhase phase)
        {
            return new SessionTransitionResult
            {
                Success = true,
                Outcome = SessionTransitionOutcome.AlreadyInPhase,
                Phase = phase,
                PreviousPhase = phase,
            };
        }

        public static SessionTransitionResult Busy(GameSessionPhase phase, string error)
        {
            return new SessionTransitionResult
            {
                Success = false,
                Outcome = SessionTransitionOutcome.Busy,
                Phase = phase,
                PreviousPhase = phase,
                Error = error ?? string.Empty,
            };
        }

        public static SessionTransitionResult Failed(GameSessionPhase phase, GameSessionPhase previous, string error)
        {
            return new SessionTransitionResult
            {
                Success = false,
                Outcome = SessionTransitionOutcome.Failed,
                Phase = phase,
                PreviousPhase = previous,
                Error = error ?? string.Empty,
            };
        }

        public static SessionTransitionResult Cancelled(GameSessionPhase phase, GameSessionPhase previous)
        {
            return new SessionTransitionResult
            {
                Success = false,
                Outcome = SessionTransitionOutcome.Cancelled,
                Phase = phase,
                PreviousPhase = previous,
                Error = "The session transition was cancelled.",
            };
        }
    }
}
