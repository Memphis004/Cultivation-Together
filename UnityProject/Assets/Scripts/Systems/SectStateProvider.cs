using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Xianxia.Sect
{
    // Implements the seam TimeSystem.cs defines (ISectStateProvider).
    //
    // IMPORTANT: this holds ONE live state instance for the process
    // lifetime, created once from mock data. Earlier versions called
    // MockSectData.Create() fresh on every query, which meant any mutation
    // (e.g. from ApplyDecisionConsequence) was invisible on the next
    // get_sect_state call - it was building a brand new object every time,
    // not reading back the one that got mutated. Swap _state's origin for
    // real DiscipleSystem/ResourceCraftingSystem aggregation once those
    // exist - the interface doesn't need to change.
    public class SectStateProvider : ISectStateProvider
    {
        private readonly SectEconomyState _state = MockSectData.Create();

        public SectEconomyState BuildSectEconomyState()
        {
            return _state;
        }

        // Placeholder consequence rules keyed by event id - not real game
        // balance, just enough to prove ExecuteDecision actually mutates
        // state instead of only logging. Real weighted/authored
        // consequences belong with the EventData ScriptableObject work
        // (deferred - see project_summary.md).
        public void ApplyDecisionConsequence(string eventId, string choiceId)
        {
            var resources = _state.Stockpile.RawResources;

            switch (eventId)
            {
                case "bandit_raid_001":
                    Adjust(resources, "provisions", -20);
                    break;

                case "herb_garden_bloom":
                    Adjust(resources, "herb", 30);
                    break;

                case "wandering_merchant":
                    Adjust(resources, "ore", -20);
                    Adjust(resources, "provisions", 40);
                    break;

                case "new_disciple_applicant":
                    // Real recruiting is DiscipleSystem's job (deferred) -
                    // just a small resource cost for the welcome feast so
                    // this event isn't a total no-op in the meantime.
                    Adjust(resources, "provisions", -5);
                    break;

                default:
                    Debug.LogWarning($"[SectStateProvider] No consequence rule for event '{eventId}' - state unchanged.");
                    return;
            }

            Debug.Log($"[SectStateProvider] Applied consequence for event={eventId} choice={choiceId}. " +
                      $"Stockpile now: {string.Join(", ", resources.Keys.Select(k => $"{k}={resources[k]}"))}");
        }

        private static void Adjust(Dictionary<string, int> resources, string key, int delta)
        {
            var current = resources.TryGetValue(key, out var value) ? value : 0;
            resources[key] = Math.Max(0, current + delta);
        }
    }
}
