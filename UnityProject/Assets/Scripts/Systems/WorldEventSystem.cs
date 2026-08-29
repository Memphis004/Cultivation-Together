using System.Linq;
using UnityEngine;
using VContainer.Unity;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    // Picks events from Luban-generated data (LubanEventPool) instead of a
    // hardcoded string array or hand-created ScriptableObject assets. See
    // Assets/Scripts/Data/LubanEventPool.cs and DataTables/ at the workspace
    // root for the actual event/choice source data.
    public class WorldEventSystem : ITickable
    {
        private const float IntervalSeconds = 15f;

        private readonly TimeSystem _timeSystem;
        private readonly LubanEventPool _eventPool;
        private float _timer;

        public WorldEventSystem(TimeSystem timeSystem, LubanEventPool eventPool)
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
            var eventRow = _eventPool.GetRandomEvent();
            if (eventRow == null) return; // LubanEventPool already logged why

            var choices = _eventPool.GetChoices(eventRow.Id)
                .Select(c => new EventChoiceInfo { ChoiceId = c.ChoiceId, Label = c.Label })
                .ToList();

            _timeSystem.RaiseWorldEvent(eventRow.Id, eventRow.Description, eventRow.RequiresDecision, choices);
        }
    }
}
