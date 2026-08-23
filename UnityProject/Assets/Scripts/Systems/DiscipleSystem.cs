using UnityEngine;
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

        public DiscipleSystem(ISectStateProvider stateProvider)
        {
            _stateProvider = stateProvider;
        }

        public void Tick()
        {
            _stateProvider.TickGathering(Time.deltaTime);
        }
    }
}
