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

        // Data-driven layout (open question #13) - moved verbatim from
        // UIRoot.ApplyEventPopupLayout.
        public override void ApplyDefaultLayout()
        {
            var rt = GetComponent<RectTransform>();
            if (rt == null) return;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(600f, 400f);
            rt.anchoredPosition = Vector2.zero;

            var vlg = GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.padding           = new RectOffset(20, 20, 20, 20);
                vlg.spacing           = 15f;
                vlg.childAlignment    = TextAnchor.UpperCenter;
                vlg.childControlWidth  = true;
                vlg.childControlHeight = false;
                vlg.childForceExpandWidth  = true;
                vlg.childForceExpandHeight = false;
            }

            var csf = GetComponent<ContentSizeFitter>();
            if (csf != null)
            {
                csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
            }
        }

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
