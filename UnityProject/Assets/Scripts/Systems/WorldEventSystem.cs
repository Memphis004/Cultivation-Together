using System.Linq;
using VContainer.Unity;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    // Picks events from Luban-generated data (LubanEventPool) instead of a
    // hardcoded string array or hand-created ScriptableObject assets. See
    // Assets/Scripts/Data/LubanEventPool.cs and DataTables/ at the workspace
    // root for the actual event/choice source data.
    //
    // E2-lite: NO event is raised from the constructor. The first event is raised
    // from the tick path after a configurable start grace measured in SIMULATION
    // time (WorldEventRuntimeConfig.StartGraceSeconds), so the UI exists first and
    // the order of VContainer entry-point Start() calls cannot matter.
    public class WorldEventSystem : ITickable
    {
        private const float IntervalSeconds = 15f;

        private readonly TimeSystem _timeSystem;
        private readonly LubanEventPool _eventPool;
        private readonly WorldEventRuntimeConfig _config;

        private float _timer;
        private float _graceRemaining;
        private bool _started;

        public WorldEventSystem(TimeSystem timeSystem, LubanEventPool eventPool, WorldEventRuntimeConfig config = null)
        {
            _timeSystem = timeSystem;
            _eventPool = eventPool;
            _config = config ?? WorldEventRuntimeConfig.Instance;
            _graceRemaining = _config.StartGraceSeconds;
        }

        public void Tick()
        {
            // P10 — the same shared simulation delta as every other gameplay tick
            // (pause = 0, speed applied once inside TimeSystem).
            Advance(_timeSystem.SimulationDelta);
        }

        /// <summary>
        /// Advance the event clock by a simulation delta. Before the start grace
        /// elapses nothing fires; then the first event is raised, after which events
        /// follow the 15s interval. The pause early-return is NOT redundant with
        /// SimulationDelta returning 0: at the moment a pause begins the timer may
        /// already be at the threshold, and neither timer may advance while paused.
        /// Split out of <see cref="Tick"/> (same pattern as AutoTaskScheduler.Advance)
        /// so the cadence is testable without a frame.
        /// </summary>
        public void Advance(float deltaTimeSeconds)
        {
            // While paused (a decision is outstanding, or the player paused) the
            // spawner does nothing and no timer advances.
            if (_timeSystem.IsPaused) return;

            if (!_started)
            {
                _graceRemaining -= deltaTimeSeconds;
                if (_graceRemaining > 0f) return;
                _started = true;
                _timer = 0f;
                RaiseFromPool();
                return;
            }

            _timer += deltaTimeSeconds;
            if (_timer < IntervalSeconds) return;

            _timer = 0f;
            RaiseFromPool();
        }

        private void RaiseFromPool()
        {
            // E2-lite: while a decision is pending, only non-decision events may
            // fire. The pool excludes decision events; if nothing is eligible it
            // returns null and this interval is simply skipped (no loop, no spin).
            var eventRow = _eventPool.GetRandomEvent(excludeDecisionEvents: _timeSystem.HasPendingDecision);
            if (eventRow == null) return; // LubanEventPool already logged why

            var choices = _eventPool.GetChoices(eventRow.Id)
                .Select(c => new EventChoiceInfo { ChoiceId = c.ChoiceId, Label = c.Label })
                .ToList();

            _timeSystem.RaiseWorldEvent(eventRow.Id, eventRow.Description, eventRow.RequiresDecision, choices);
        }
    }
}
