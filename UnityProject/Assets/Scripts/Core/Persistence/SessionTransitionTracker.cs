using System.Threading;

namespace Xianxia.Sect
{
    /// <summary>
    /// P11B §5 — the session generation, the one fact that says "which game is live".
    ///
    /// Every asynchronous save/load records the generation it started in and refuses to
    /// finish once that generation is no longer current, so a completion from the previous
    /// session can never land in the new session's slot (or restore over it).
    ///
    /// Thread-safe by design: it is READ from a worker thread (the repository's commit
    /// guard) and WRITTEN on the main thread when a session transition starts. This is
    /// plain C# state, not a Unity API, so reading it off-thread is allowed.
    /// </summary>
    public sealed class SessionTransitionTracker
    {
        private int _generation;

        /// <summary>The live session generation. Starts at 0; strictly increases.</summary>
        public int SessionGeneration => Volatile.Read(ref _generation);

        /// <summary>
        /// Marks the start of a new session (New Game / Load). Everything an in-flight
        /// completion captured before this call is now obsolete. Returns the new generation.
        /// </summary>
        public int BeginTransition()
        {
            return Interlocked.Increment(ref _generation);
        }
    }
}
