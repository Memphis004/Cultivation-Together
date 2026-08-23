using UnityEngine;
using VContainer.Unity;

namespace Xianxia.Sect
{
    // Minimal placeholder event source - fixed interval + hardcoded id list.
    // Swap for a real event pool (EventData ScriptableObjects, weighted by
    // sect state) once that system exists. Purpose right now is just to
    // close the loop end to end: without something calling
    // TimeSystem.RaiseWorldEvent, await_next_world_event has nothing to
    // ever return and execute_decision has nothing to respond to.
    public class WorldEventSystem : ITickable
    {
        private const float IntervalSeconds = 15f;

        private static readonly string[] PendingEvents =
        {
            "bandit_raid_001",
            "new_disciple_applicant",
            "herb_garden_bloom",
            "wandering_merchant",
        };

        private readonly TimeSystem _timeSystem;
        private float _timer;

        public WorldEventSystem(TimeSystem timeSystem)
        {
            _timeSystem = timeSystem;

            // Fire one event immediately on startup instead of making the
            // first test always wait a full IntervalSeconds.
            RaiseInitialEvent();
        }

        private void RaiseInitialEvent()
        {
            if (_timeSystem.IsPaused) return;

            var eventId = PendingEvents[Random.Range(0, PendingEvents.Length)];
            _timeSystem.RaiseWorldEvent(eventId, requiresDecision: true);
        }

        public void Tick()
        {
            // Already waiting on a decision (TimeSystem paused itself when
            // it last raised an event) - don't pile up more events. Nothing
            // fires again until execute_decision unpauses.
            if (_timeSystem.IsPaused) return;

            _timer += Time.deltaTime;
            if (_timer < IntervalSeconds) return;

            _timer = 0f;
            var eventId = PendingEvents[Random.Range(0, PendingEvents.Length)];
            _timeSystem.RaiseWorldEvent(eventId, requiresDecision: true);
        }
    }
}
