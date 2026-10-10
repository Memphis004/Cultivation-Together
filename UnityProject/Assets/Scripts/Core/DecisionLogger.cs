using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using UnityEngine;
using VContainer.Unity;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    // Subscribes to ExecuteDecision messages coming from the MCP bridge and
    // logs them to Unity's Console - this is the visible confirmation that
    // the write path (bridge -> Unity) works, mirroring the read path
    // (Unity -> bridge) that get_sect_state already proved out.
    //
    // Also resumes time if it was auto-paused for a decision, closing the
    // loop conceptually: TimeSystem.RaiseWorldEvent(requiresDecision: true)
    // pauses, this is what un-pauses once a decision arrives.
    //
    // THREADING (E1-pre): the MessagePipe.Interprocess TCP receive loop
    // dispatches this handler on a background thread, and TcpWorker's
    // RunReceiveLoop does NOT await the publish (TcpWorker.cs: "publisher.
    // Publish(message, message, CancellationToken.None)"), so two decisions
    // can even overlap. DecisionExecutor.Execute mutates gameplay state,
    // publishes in-memory messages and runs UI subscribers
    // (LogWindowPresenter -> LogWindowView.AddLine -> Instantiate), all of
    // which are main-thread-only. This class therefore hops to the Unity main
    // thread BEFORE calling Execute, and pumps arrivals through a single FIFO
    // so decisions run strictly in arrival order and never concurrently.
    //
    // The synchronous in-process UI path (EventPopupPresenter ->
    // DecisionExecutor.Execute) is unaffected: DecisionExecutor stays
    // synchronous and thread-agnostic (E1-pre #2).
    //
    // NOTE (E1-pre #1): the earlier project note about a main-thread hop in
    // "DecisionExecutor.ExecuteAsync" is stale - that method does not exist
    // in the current source, so there is nothing to reuse there. The hop is
    // applied here with the repo's existing pattern (await
    // UniTask.SwitchToMainThread()) rather than the scene-scoped
    // IMainThreadQueue seam, which is registered only in SectSceneLifetimeScope
    // (a child scope) and is not resolvable from this root-scope entry point.
    public class DecisionLogger : IStartable, IDisposable
    {
        private readonly IDistributedSubscriber<string, ExecuteDecisionMessage> _subscriber;
        private readonly DecisionExecutor _decisionExecutor;

        // Arrival queue + single-pump guard. Locked because OnDecisionReceived
        // runs on the TCP thread while the pump drains on the main thread.
        private readonly object _gate = new object();
        private readonly Queue<ExecuteDecisionMessage> _pending = new Queue<ExecuteDecisionMessage>();
        private bool _pumping;
        private bool _disposed;

        private CancellationTokenSource _lifetime;

        public DecisionLogger(
            IDistributedSubscriber<string, ExecuteDecisionMessage> subscriber,
            DecisionExecutor decisionExecutor)
        {
            _subscriber = subscriber;
            _decisionExecutor = decisionExecutor;
        }

        public void Start()
        {
            _lifetime = new CancellationTokenSource();
            SubscribeAsync(_lifetime.Token).Forget();
        }

        private async UniTaskVoid SubscribeAsync(CancellationToken cancellationToken)
        {
            try
            {
                await using var subscription = await _subscriber.SubscribeAsync(
                    InterprocessTopics.ExecuteDecision,
                    OnDecisionReceived,
                    cancellationToken);

                // Keep this entry point's Start() alive for the app lifetime -
                // the subscription itself lives as long as this task does.
                await UniTask.WaitUntilCanceled(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown (scope disposed) - not an error.
            }
        }

        // Runs on the MessagePipe TCP receive thread. Never touch Unity here -
        // just queue, then let the main-thread pump execute in arrival order.
        private void OnDecisionReceived(ExecuteDecisionMessage message)
        {
            Debug.Log(
                $"[DecisionLogger] Received decision from bridge - " +
                $"eventId={message.EventId} choiceId={message.ChoiceId}");

            lock (_gate)
            {
                if (_disposed) return;
                _pending.Enqueue(message);
                if (_pumping) return;
                _pumping = true; // this caller owns the pump; start it exactly once
            }

            PumpAsync().Forget();
        }

        // Drains arrivals in order on the main thread. Exactly one pump runs at
        // a time (_pumping), so decisions execute sequentially, in arrival
        // order, and never concurrently. Stops - and stops accepting work -
        // once the scope is disposed.
        private async UniTaskVoid PumpAsync()
        {
            while (true)
            {
                // Hop first: DecisionExecutor.Execute and every downstream
                // subscriber (state mutation, pause change, message publish,
                // UI) expect the Unity main thread.
                await UniTask.SwitchToMainThread();

                ExecuteDecisionMessage message;
                lock (_gate)
                {
                    if (_disposed || _pending.Count == 0)
                    {
                        _pumping = false;
                        return;
                    }
                    message = _pending.Dequeue();
                }

                try
                {
                    _decisionExecutor.Execute(message.EventId, message.ChoiceId);
                }
                catch (Exception ex)
                {
                    // Never swallow silently - include which decision failed.
                    Debug.LogError(
                        $"[DecisionLogger] Decision execution failed on main thread - " +
                        $"eventId={message.EventId} choiceId={message.ChoiceId}: {ex}");
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _pending.Clear();
            }

            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _lifetime = null;
        }
    }
}
