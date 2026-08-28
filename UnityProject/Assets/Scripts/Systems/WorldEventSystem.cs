using System.Linq;
using UnityEngine;
using VContainer.Unity;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    // Picks events from an author-able EventPool (ScriptableObject) instead
    // of a hardcoded string array. See Assets/Scripts/Data/EventData.cs and
    // EventPool.cs. EventPool is assigned via GameLifetimeScope's Inspector
    // field, not hunted down at runtime.
    public class WorldEventSystem : ITickable
    {
        private const float IntervalSeconds = 15f;

        private readonly TimeSystem _timeSystem;
        private readonly EventPool _eventPool;
        private float _timer;

        public WorldEventSystem(TimeSystem timeSystem, EventPool eventPool)
        {
            _timeSystem = timeSystem;
            _eventPool = eventPool;

            // Fire one event immediately on startup instead of making the
            // first test always wait a full IntervalSeconds.
            RaiseInitialEvent();
        }

        private void RaiseInitialEvent()
        {
            if (_timeSystem.IsPaused) return;
            RaiseFromPool();
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
            RaiseFromPool();
        }

        private void RaiseFromPool()
        {
            var eventData = _eventPool.GetRandomEvent();
            if (eventData == null) return; // EventPool already logged why

            var choices = eventData.choices
                .Select(c => new EventChoiceInfo { ChoiceId = c.choiceId, Label = c.label })
                .ToList();

            _timeSystem.RaiseWorldEvent(eventData.eventId, eventData.description, eventData.requiresDecision, choices);
        }
    }
}
