using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Xianxia.Sect
{
    /// <summary>
    /// P11B §6 — where a save/load application operation runs.
    ///
    /// The production implementation keeps every Unity API on the main thread and sends
    /// ONLY the detached envelope to a worker (Common-Rules 4: background file I/O may
    /// operate only on detached data). Tests inject a deterministic implementation that
    /// runs the worker body inline — EditMode has no player loop to pump a real thread
    /// hop through, exactly like <c>MainThreadDispatch.OverrideSwitchForTests</c>.
    /// </summary>
    public interface ISaveWorkScheduler
    {
        /// <summary>
        /// Runs <paramref name="work"/> (which must be pure, detached-data work) off the
        /// main thread and then returns to the main thread.
        /// </summary>
        UniTask<T> RunOnWorkerAsync<T>(Func<T> work, CancellationToken cancellationToken);

        /// <summary>Returns to the Unity main thread (no-op when already there).</summary>
        UniTask SwitchToMainThreadAsync();
    }

    /// <summary>
    /// P11B — the Unity implementation of <see cref="ISaveWorkScheduler"/>: uses
    /// <c>UniTask.RunOnThreadPool</c> for the worker hop (which returns to the main thread
    /// afterwards) and <c>UniTask.SwitchToMainThread</c> for the plain hop.
    /// </summary>
    public sealed class UnitySaveWorkScheduler : ISaveWorkScheduler
    {
        public UniTask<T> RunOnWorkerAsync<T>(Func<T> work, CancellationToken cancellationToken)
        {
            // configureAwait: true → the awaited continuation comes back to the main thread,
            // so the service can commit a load / publish its result on the main thread.
            return UniTask.RunOnThreadPool(work, true, cancellationToken);
        }

        public async UniTask SwitchToMainThreadAsync()
        {
            await UniTask.SwitchToMainThread();
        }
    }
}
