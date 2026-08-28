using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Xianxia.Sect
{
    // Holds the set of EventData assets WorldEventSystem picks from.
    // Author one .asset (Create > Xianxia Sect > Event Pool), drag your
    // EventData assets into its "events" list in the Inspector, then assign
    // that pool to GameLifetimeScope's "Event Pool" field.
    [CreateAssetMenu(fileName = "EventPool", menuName = "Xianxia Sect/Event Pool")]
    public class EventPool : ScriptableObject
    {
        public List<EventData> events = new List<EventData>();

        // Weighted random pick. Returns null (with a warning) if the pool
        // is empty - callers should handle that rather than assume an
        // event always comes back.
        public EventData GetRandomEvent()
        {
            if (events == null || events.Count == 0)
            {
                Debug.LogWarning("[EventPool] No events assigned - add EventData assets in the Inspector.");
                return null;
            }

            var totalWeight = events.Sum(e => Mathf.Max(0f, e.weight));
            if (totalWeight <= 0f)
            {
                return events[Random.Range(0, events.Count)];
            }

            var roll = Random.Range(0f, totalWeight);
            var cumulative = 0f;
            foreach (var eventData in events)
            {
                cumulative += Mathf.Max(0f, eventData.weight);
                if (roll <= cumulative) return eventData;
            }

            return events[^1];
        }
    }
}
