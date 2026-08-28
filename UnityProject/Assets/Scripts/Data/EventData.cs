using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xianxia.Sect
{
    // Design-time event definition - author these as .asset files in the
    // Editor (right-click in Project window > Create > Xianxia Sect >
    // Event Data). Replaces the hardcoded string array that used to live
    // in WorldEventSystem.
    [CreateAssetMenu(fileName = "NewEventData", menuName = "Xianxia Sect/Event Data")]
    public class EventData : ScriptableObject
    {
        [Tooltip("Stable id - must match a case in SectStateProvider.ApplyDecisionConsequence")]
        public string eventId;

        [TextArea(2, 5)]
        public string description;

        public bool requiresDecision = true;

        [Min(0f), Tooltip("Relative weight for random selection - higher picks more often. All-equal weights = uniform random.")]
        public float weight = 1f;

        public List<EventChoiceOption> choices = new List<EventChoiceOption>();
    }

    // Editor-facing choice option. Distinct from Messages.EventChoiceInfo
    // (the MessagePack wire type) on purpose - this one is Unity-serialized
    // authoring data, that one crosses the interprocess bus. WorldEventSystem
    // converts between them.
    [Serializable]
    public class EventChoiceOption
    {
        [Tooltip("Matches a choiceId check in SectStateProvider.ApplyDecisionConsequence, where relevant")]
        public string choiceId;
        public string label;
    }
}
