using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MessagePipe;
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
        private readonly IDistributedPublisher<string, WorldEventTriggeredMessage> _worldEventPublisher;

        private int _speed = 1;
        private bool _paused;

        public TimeSystem(
            IPublisher<TimeSpeedChangedMessage> speedPublisher,
            IDistributedPublisher<string, WorldEventTriggeredMessage> worldEventPublisher)
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

        // Called by whatever system decides a world event fired (new
        // applicant, monster incursion, ...). requiresDecision auto-pauses -
        // this is the "checkpoint" the AI GM / vote window waits on.
        public async Task RaiseWorldEvent(string eventId, bool requiresDecision, CancellationToken ct = default)
        {
            if (requiresDecision) SetPaused(true);

            await _worldEventPublisher.PublishAsync(
                InterprocessTopics.WorldEvent,
                new WorldEventTriggeredMessage { EventId = eventId, RequiresDecision = requiresDecision },
                ct);
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
                // MessagePack stub for now - see SectEconomyState.ToByteArray().
                // Swap for real protobuf bytes once economy.proto is compiled.
                EconomyStateProtobuf = state.ToByteArray()
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
    }
}
