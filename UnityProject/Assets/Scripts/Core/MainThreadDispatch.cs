using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xianxia.Sect
{
    /// <summary>
    /// T1 — the ONE main-thread hop for every callback that MessagePipe.Interprocess
    /// dispatches on its TCP receive thread.
    ///
    /// Why this exists: <c>TcpWorker</c>'s receive loop publishes on a background thread
    /// (and does not await the publish), so an <c>IAsyncRequestHandler</c> registered with
    /// <c>RegisterTcpRemoteRequestHandler</c> runs off the Unity main thread. Reading live
    /// gameplay state (<c>BuildSectEconomyState().ToByteArray()</c>), mutating it, publishing
    /// in-memory messages and touching Unity APIs are all main-thread-only.
    ///
    /// The hop is exactly the E1-pre pattern (<c>await UniTask.SwitchToMainThread()</c>, see
    /// DecisionLogger) — deliberately NOT the scene-scoped <c>IMainThreadQueue</c> seam from
    /// CameraRigPorts.cs, which is registered only in SectSceneLifetimeScope (a child scope)
    /// and is therefore not resolvable from these root-scoped handlers.
    ///
    /// <see cref="RunAsync{TResponse}(string,string,Func{TResponse},Func{string,TResponse})"/> also
    /// gives every handler the same reliability contract, so no handler copy-pastes try/catch:
    /// a failure is logged with the handler name + request ids and converted into that
    /// handler's OWN existing failure response, so the bridge is never left waiting forever.
    /// </summary>
    public static class MainThreadDispatch
    {
        /// <summary>Reason text handed to a handler's existing failure response when no main thread is available.</summary>
        public const string ShuttingDownReason =
            "unity main thread unavailable: the application/session is shutting down";

        private static Func<UniTask> _switchToMainThread = DefaultSwitch;

        /// <summary>
        /// True once the app/session started shutting down. A hop queued after that would never be
        /// pumped (no player loop), so handlers fail fast instead of running late.
        /// The root scope is disposed as part of the same shutdown, so "scope disposed" is covered here.
        /// </summary>
        public static bool IsShuttingDown { get; private set; }

        /// <summary>Managed thread id that the last successful hop resumed on (test-observable).</summary>
        public static int LastHopThreadId { get; private set; }

        /// <summary>
        /// New play session / new play mode: clear the shutdown flag and re-hook the quit signal.
        /// (SubsystemRegistration runs once per play session; EditMode tests call ResetForTests.)
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void HookApplicationQuitting()
        {
            IsShuttingDown = false;
            LastHopThreadId = 0;
            Application.quitting -= MarkShuttingDown;
            Application.quitting += MarkShuttingDown;
        }

        /// <summary>Marks the process as shutting down — every later hop fails fast.</summary>
        public static void MarkShuttingDown()
        {
            IsShuttingDown = true;
        }

        /// <summary>
        /// Hop to the Unity main thread. Throws <see cref="MainThreadUnavailableException"/> when the
        /// process is already shutting down, or when it started shutting down while queued.
        /// </summary>
        public static async UniTask EnterAsync()
        {
            if (IsShuttingDown) throw new MainThreadUnavailableException();
            await _switchToMainThread();
            LastHopThreadId = Thread.CurrentThread.ManagedThreadId;
            if (IsShuttingDown) throw new MainThreadUnavailableException();
        }

        /// <summary>
        /// Runs a synchronous body on the main thread and answers with the handler's own
        /// response shape on any failure (never throws to the MessagePipe worker).
        /// Bodies must stay synchronous so main-thread work is FIFO with arrival order.
        /// </summary>
        public static async UniTask<TResponse> RunAsync<TResponse>(
            string handlerName,
            string requestContext,
            Func<TResponse> onMainThread,
            Func<string, TResponse> onFailure)
        {
            try
            {
                await EnterAsync();
                return onMainThread();
            }
            catch (MainThreadUnavailableException)
            {
                Debug.LogWarning(
                    $"[{handlerName}] {requestContext} — refused while shutting down: {ShuttingDownReason}.");
                return onFailure(ShuttingDownReason);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[{handlerName}] {requestContext} — unhandled exception on the main thread: {ex}");
                return onFailure(ex.Message);
            }
        }

        /// <summary>
        /// Same contract as <see cref="RunAsync{TResponse}(string,string,Func{TResponse},Func{string,TResponse})"/>
        /// for a body that hops first and then AWAITS kept-open work (await-world-event): the await
        /// suspends without blocking the main thread.
        /// </summary>
        public static async UniTask<TResponse> RunAwaitingAsync<TResponse>(
            string handlerName,
            string requestContext,
            Func<UniTask<TResponse>> onMainThread,
            Func<string, TResponse> onFailure)
        {
            try
            {
                await EnterAsync();
                return await onMainThread();
            }
            catch (MainThreadUnavailableException)
            {
                Debug.LogWarning(
                    $"[{handlerName}] {requestContext} — refused while shutting down: {ShuttingDownReason}.");
                return onFailure(ShuttingDownReason);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[{handlerName}] {requestContext} — unhandled exception on the main thread: {ex}");
                return onFailure(ex.Message);
            }
        }

        // UniTask.SwitchToMainThread() returns SwitchToMainThreadAwaitable, so it is awaited here
        // to keep the seam a plain Func<UniTask> (the exact E1-pre pattern, wrapped once).
        private static async UniTask DefaultSwitch()
        {
            await UniTask.SwitchToMainThread();
        }

        // ---------- test seam (T1) ----------
        // A test replaces the hop with a manual queue so it can prove that nothing happens
        // before the queued continuation is pumped, and that it then resumes on the pumped
        // thread. Game code never calls these.

        /// <summary>Replaces the hop with a test-controlled one.</summary>
        public static void OverrideSwitchForTests(Func<UniTask> switchToMainThread)
        {
            _switchToMainThread = switchToMainThread ?? DefaultSwitch;
        }

        /// <summary>Restores the production wiring (the test fixture instance is reused between tests).</summary>
        public static void ResetForTests()
        {
            _switchToMainThread = DefaultSwitch;
            IsShuttingDown = false;
            LastHopThreadId = 0;
        }
    }

    /// <summary>
    /// Thrown internally when a TCP-dispatched handler cannot reach the Unity main thread
    /// because the application/session is shutting down. Never escapes a handler: the shared
    /// runner converts it into that handler's existing failure response.
    /// </summary>
    public sealed class MainThreadUnavailableException : Exception
    {
        public MainThreadUnavailableException()
            : base(MainThreadDispatch.ShuttingDownReason)
        {
        }
    }
}
