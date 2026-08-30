using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    // View-layer DTO, deliberately separate from Messages.EventChoiceInfo
    // (the wire type) - same split reasoning as EventData/EventChoiceOption
    // vs EventChoiceInfo from the Luban migration.
    public class EventChoiceViewData
    {
        public string ChoiceId;
        public string Label;
    }

    public class EventPopupView : UIViewBase
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private Transform choicesRoot;
        [SerializeField] private Button choiceButtonPrefab;

        public event Action<string> ChoiceClicked;

        private readonly List<GameObject> _spawnedButtons = new List<GameObject>();

        public void SetEvent(string eventId, string description)
        {
            if (titleText != null) titleText.text = eventId;
            if (descriptionText != null) descriptionText.text = description;
        }

        public void SetChoices(List<EventChoiceViewData> choices)
        {
            ClearChoices();

            if (choicesRoot == null || choiceButtonPrefab == null) return;

            foreach (var choice in choices)
            {
                var button = Instantiate(choiceButtonPrefab, choicesRoot);
                var label = button.GetComponentInChildren<TMP_Text>();
                if (label != null) label.text = choice.Label;

                var choiceId = choice.ChoiceId; // capture for the closure below
                button.onClick.AddListener(() => ChoiceClicked?.Invoke(choiceId));

                _spawnedButtons.Add(button.gameObject);
            }
        }

        private void ClearChoices()
        {
            foreach (var go in _spawnedButtons)
            {
                if (go != null) Destroy(go);
            }
            _spawnedButtons.Clear();
        }

        public override void Hide()
        {
            base.Hide();
            ClearChoices(); // don't leak stale choice buttons into the next event
        }
    }
}
