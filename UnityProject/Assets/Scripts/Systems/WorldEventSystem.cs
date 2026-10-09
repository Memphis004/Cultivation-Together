using System.Linq;
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
            // P10 — the same shared simulation delta as every other gameplay tick
            // (pause = 0, speed applied once inside TimeSystem).
            Advance(_timeSystem.SimulationDelta);
        }

        /// <summary>
        /// Advance the event timer by a simulation delta and raise an event when the
        /// interval elapses. Split out of <see cref="Tick"/> (same pattern as
        /// AutoTaskScheduler.Advance) so the cadence is testable without a frame.
        /// The pause early-return is NOT redundant with SimulationDelta returning 0:
        /// at the moment a pause begins the timer may already be at the threshold, and
        /// while a decision is outstanding no further event may fire.
        /// </summary>
        public void Advance(float deltaTimeSeconds)
        {
            // Already waiting on a decision (TimeSystem paused itself when
            // it last raised an event) - don't pile up more events. Nothing
            // fires again until execute_decision unpauses.
            if (_timeSystem.IsPaused) return;

            _timer += deltaTimeSeconds;
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
