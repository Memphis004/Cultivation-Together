using System.Threading;
using System.Threading.Tasks;
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
    public class DecisionLogger : IStartable
    {
        private readonly IDistributedSubscriber<string, ExecuteDecisionMessage> _subscriber;
        private readonly TimeSystem _timeSystem;

        public DecisionLogger(
            IDistributedSubscriber<string, ExecuteDecisionMessage> subscriber,
            TimeSystem timeSystem)
        {
            _subscriber = subscriber;
            _timeSystem = timeSystem;
        }

        public void Start()
        {
            SubscribeAsync().Forget();
        }

        private async UniTaskVoid SubscribeAsync()
        {
            await using var subscription = await _subscriber.SubscribeAsync(
                InterprocessTopics.ExecuteDecision,
                OnDecisionReceived);

            // Keep this entry point's Start() alive for the app lifetime -
            // the subscription itself lives as long as this task does.
            await UniTask.WaitUntilCanceled(CancellationToken.None);
        }

        private void OnDecisionReceived(ExecuteDecisionMessage message)
        {
            Debug.Log(
                $"[DecisionLogger] Received decision from bridge - " +
                $"eventId={message.EventId} choiceId={message.ChoiceId}");

            _timeSystem.SetPaused(false);
        }
    }
}
