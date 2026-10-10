using System.Threading;

namespace Xianxia.Sect
{
    /// <summary>
    /// P11B §4 — the single-flight gate for the save/load application operations: at most
    /// one is ever in flight, so two writes can never interleave on the same slot.
    ///
    /// An overlapping request is REFUSED with <see cref="SaveIoErrorKind.Busy"/> rather
    /// than queued: queueing a stale save/load is exactly the failure mode §5 warns about
    /// (an old completion landing after the player already began another game), and the
    /// caller (a button) can simply retry. Thread-safe: the flag is also readable while an
    /// operation is in flight.
    /// </summary>
    public sealed class SaveOperationGate
    {
        private int _busy;

        /// <summary>True while a save/load application operation is in flight.</summary>
        public bool IsBusy => Volatile.Read(ref _busy) != 0;

        /// <summary>Enters the critical section. False when another operation already holds it.</summary>
        public bool TryEnter()
        {
            return Interlocked.CompareExchange(ref _busy, 1, 0) == 0;
        }

        /// <summary>Leaves the critical section. Safe to call only after a successful <see cref="TryEnter"/>.</summary>
        public void Exit()
        {
            Volatile.Write(ref _busy, 0);
        }
    }
}
