using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// E3 — the "an event is waiting" chip that sits just under the E1 time-control
    /// bar. It is shown only while a decision event is pending AND the popup is not
    /// actually on screen (hidden by the player, or blocked by another modal), so the
    /// player always has a visible signal and a way back into the popup.
    ///
    /// The view only renders state handed to it by <see cref="WorldEventChipPresenter"/>.
    /// </summary>
    public class WorldEventChipView : UIViewBase
    {
        [SerializeField] private Button chipButton;
        [SerializeField] private Image background;
        [SerializeField] private TMP_Text label;

        public event Action Clicked;

        /// <summary>Matches the E1 time bar plate so the two read as one cluster.</summary>
        private static readonly Color IdleColor = new Color(0f, 0f, 0f, 0.55f);
        /// <summary>The E1 active-speed gold — reused so the project keeps one palette.</summary>
        private static readonly Color HighlightColor = new Color(0.95f, 0.78f, 0.30f);
        private static readonly Color IdleTextColor = UiPalette.LightText;
        private static readonly Color HighlightTextColor = UiPalette.Ink;

        private float _highlightRemaining;
        private bool _wired;

        /// <summary>
        /// Below the E1 time bar (560x56 at (12,-110)) — the same E0 free region,
        /// clear of WalletHud (top-right), LogWindow (bottom-left) and BottomMenu.
        /// </summary>
        public override void ApplyDefaultLayout()
        {
            var rt = transform as RectTransform;
            if (rt == null) return;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(400f, 48f);
            rt.anchoredPosition = new Vector2(12f, -176f);
        }

        public override void Show()
        {
            WireButton();
            gameObject.SetActive(true);
            RefreshTint();
        }

        public void SetShown(bool shown)
        {
            if (shown) Show();
            else Hide();
        }

        public void SetText(string text)
        {
            if (label != null) label.text = text ?? string.Empty;
        }

        /// <summary>Brief emphasis so a newly arrived event is noticed.</summary>
        public void FlashHighlight(float seconds)
        {
            _highlightRemaining = Mathf.Max(0f, seconds);
            RefreshTint();
        }

        private void Update() => Tick(Time.unscaledDeltaTime);

        /// <summary>Timer step, split out so EditMode tests can drive it without a frame.</summary>
        public void Tick(float deltaSeconds)
        {
            if (_highlightRemaining <= 0f) return;

            _highlightRemaining -= deltaSeconds;
            if (_highlightRemaining > 0f) return;

            _highlightRemaining = 0f;
            RefreshTint();
        }

        private void RefreshTint()
        {
            bool highlighted = _highlightRemaining > 0f;
            if (background != null) background.color = highlighted ? HighlightColor : IdleColor;
            if (label != null) label.color = highlighted ? HighlightTextColor : IdleTextColor;
        }

        private void WireButton()
        {
            if (_wired || chipButton == null) return;
            chipButton.onClick.AddListener(OnClicked);
            if (chipButton.targetGraphic == null) chipButton.targetGraphic = background;
            _wired = true;
        }

        private void OnClicked() => Clicked?.Invoke();

        private void OnDestroy()
        {
            if (chipButton != null && _wired) chipButton.onClick.RemoveListener(OnClicked);
            _wired = false;
            Clicked = null;
        }

        // ---- test accessors (EditMode tests, no reflection — C12) ----
        public bool ShownForTest => gameObject.activeSelf;
        public bool IsHighlightedForTest => _highlightRemaining > 0f;
        public string LabelForTest => label != null ? label.text : null;
        public Color BackgroundColorForTest => background != null ? background.color : default;
        public void InvokeClickForTest() => OnClicked();
        public void AdvanceForTest(float deltaSeconds) => Tick(deltaSeconds);
    }
}
