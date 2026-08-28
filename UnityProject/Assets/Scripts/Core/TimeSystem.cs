using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using UnityEngine;
using VContainer.Unity;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    // Example subsystem wired through the bus instead of holding direct
    // references to other systems. Compare to the earlier plain GameManager
    // sketch - the difference is TimeSystem doesn't know DiscipleSystem or
    // BuildingSystem exist; it just publishes, and whoever cares subscribes.
    public class TimeSystem : IStartable, ITickable
    {
        private readonly IPublisher<TimeSpeedChangedMessage> _speedPublisher;
        private readonly IPublisher<WorldEventTriggeredMessage> _worldEventPublisher;

        private int _speed = 1;
        private bool _paused;

        // Completed (and replaced with a fresh one) every time a world
        // event fires - see WaitForNextWorldEventAsync()/RaiseWorldEvent().
        private UniTaskCompletionSource<AwaitWorldEventResponse> _pendingEventSource =
            new UniTaskCompletionSource<AwaitWorldEventResponse>();

        // If a decision-requiring event fired before anyone called
        // await_next_world_event, hand it back immediately on the next call
        // instead of making a late caller wait for a completely new event.
        // Cleared once handed out - a second call with nothing new pending
        // goes back to waiting normally.
        private AwaitWorldEventResponse _cachedPendingEvent;

        public TimeSystem(
            IPublisher<TimeSpeedChangedMessage> speedPublisher,
            IPublisher<WorldEventTriggeredMessage> worldEventPublisher)
        {
            _speedPublisher = speedPublisher;
            _worldEventPublisher = worldEventPublisher;
        }

        public void Start()
        {
            _speedPublisher.Publish(new TimeSpeedChangedMessage { Speed = _speed, Paused = _paused });
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            _speedPublisher.Publish(new TimeSpeedChangedMessage { Speed = _speed, Paused = _paused });
        }

        public void SetSpeed(int speed)
        {
            _speed = speed;
            _speedPublisher.Publish(new TimeSpeedChangedMessage { Speed = _speed, Paused = _paused });
        }

        public void Tick()
        {
            if (_paused) return;
            // TODO: advance world clock by _speed * UnityEngine.Time.deltaTime
        }

        public bool IsPaused => _paused;

        // Resolved by AwaitWorldEventHandler - the bridge's await_next_world_event
        // tool call blocks on this until the next RaiseWorldEvent(), unless
        // there's already a cached one waiting (see _cachedPendingEvent).
        //
        // Remember: while _paused is true (a decision-requiring event is
        // outstanding), RaiseWorldEvent never fires again - WorldEventSystem
        // checks IsPaused and skips. Call execute_decision first to unpause,
        // or this will time out waiting for an event that can't happen yet.
        public UniTask<AwaitWorldEventResponse> WaitForNextWorldEventAsync()
        {
            if (_cachedPendingEvent != null)
            {
                Debug.Log("[TimeSystem] Returning cached world event immediately.");
                var cached = _cachedPendingEvent;
                _cachedPendingEvent = null;
                return UniTask.FromResult(cached);
            }

            return _pendingEventSource.Task;
        }

        // Called by whatever system decides a world event fired (new
        // applicant, monster incursion, ...). requiresDecision auto-pauses -
        // this is the "checkpoint" the AI GM / vote window waits on, and
        // stays paused until execute_decision is called.
        //
        // Not sent over the interprocess bus as pub/sub (see the comment on
        // AwaitWorldEventRequest in GameMessages.cs for why) - completing
        // _pendingEventSource is what actually delivers this to the bridge,
        // via the request-response AwaitWorldEventHandler below. The
        // in-memory Publish() call is just for any other in-Unity listener.
        public void RaiseWorldEvent(string eventId, string description, bool requiresDecision, List<EventChoiceInfo> choices)
        {
            Debug.Log($"[TimeSystem] World event raised: {eventId} (requiresDecision={requiresDecision})");

            if (requiresDecision) SetPaused(true);

            _worldEventPublisher.Publish(new WorldEventTriggeredMessage { EventId = eventId, RequiresDecision = requiresDecision });

            var response = new AwaitWorldEventResponse
            {
                EventId = eventId,
                RequiresDecision = requiresDecision,
                Description = description,
                Choices = choices ?? new List<EventChoiceInfo>(),
            };

            if (requiresDecision)
            {
                _cachedPendingEvent = response;
            }

            var previous = _pendingEventSource;
            _pendingEventSource = new UniTaskCompletionSource<AwaitWorldEventResponse>();
            previous.TrySetResult(response);
        }
    }

    // Answers AwaitWorldEventRequest coming in over the interprocess bus.
    // Request-response, not pub/sub - see the comment on AwaitWorldEventRequest
    // in GameMessages.cs for why.
    public class AwaitWorldEventHandler : IAsyncRequestHandler<AwaitWorldEventRequest, AwaitWorldEventResponse>
    {
        private readonly TimeSystem _timeSystem;

        public AwaitWorldEventHandler(TimeSystem timeSystem)
        {
            _timeSystem = timeSystem;
        }

        public UniTask<AwaitWorldEventResponse> InvokeAsync(AwaitWorldEventRequest request, CancellationToken cancellationToken = default)
        {
            return _timeSystem.WaitForNextWorldEventAsync();
        }
    }

    // Answers SectStateQuery requests coming in over the interprocess bus
    // from the MCP bridge. Aggregates whatever the subsystems currently hold
    // into the SectEconomyState shape from economy.proto.
    public class SectStateQueryHandler : IAsyncRequestHandler<SectStateQuery, SectStateSnapshot>
    {
        private readonly ISectStateProvider _stateProvider;

        public SectStateQueryHandler(ISectStateProvider stateProvider)
        {
            _stateProvider = stateProvider;
        }

        public UniTask<SectStateSnapshot> InvokeAsync(SectStateQuery request, CancellationToken cancellationToken = default)
        {
            var state = _stateProvider.BuildSectEconomyState();
            var snapshot = new SectStateSnapshot
            {
                RequestId = request.RequestId,
                // MessagePack, committed choice (not a protobuf stub
                // anymore - see project_summary.md for why).
                EconomyStateBytes = state.ToByteArray()
            };
            return UniTask.FromResult(snapshot);
        }
    }

    // Thin seam so SectStateQueryHandler doesn't need to know about every
    // subsystem directly - implement this on a small aggregator class that
    // does hold references (it's allowed to, it's not part of the bus).
    public interface ISectStateProvider
    {
        SectEconomyState BuildSectEconomyState();
        void ApplyDecisionConsequence(string eventId, string choiceId);
        void TickGathering(float deltaTimeSeconds);
        void TickCrafting(float deltaTimeSeconds);
        void RecruitOuterDisciple();
        PurchaseItemResponse TryPurchaseItem(string discipleId, string itemDefId, int grade, int quantity);
    }
}
