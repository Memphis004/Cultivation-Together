using VContainer.Unity;

namespace Xianxia.Sect
{
    // Passive resource gathering loop. Disciples assigned a "gathering_*"
    // CurrentTask passively produce raw resources every tick - see
    // SectStateProvider.TickGathering() for the actual rates.
    //
    // Recruiting (adding new disciples) is NOT here - it happens via
    // SectStateProvider.RecruitOuterDisciple(), triggered through the
    // new_disciple_applicant world event's execute_decision consequence.
    // Keeping it there (rather than here) avoids DiscipleSystem needing to
    // know anything about world events / decisions at all.
    public class DiscipleSystem : ITickable
    {
        private readonly ISectStateProvider _stateProvider;
        private readonly TimeSystem _timeSystem;

        public DiscipleSystem(ISectStateProvider stateProvider, TimeSystem timeSystem)
        {
            _stateProvider = stateProvider;
            _timeSystem = timeSystem;
        }

        public void Tick()
        {
            // P10 — one shared simulation delta for all gameplay progression: pause
            // freezes gathering and game speed is applied exactly once, inside
            // TimeSystem (never here).
            _stateProvider.TickGathering(_timeSystem.SimulationDelta);
        }
    }
}
