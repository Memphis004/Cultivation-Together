using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// E1.1 — the ONE button the highlight marks for a given live state. There is no
    /// "Play" member: play is not a state, it is the absence of a pause reason.
    /// </summary>
    public enum TimeControlActiveButton
    {
        Pause,
        Speed1,
        Speed2,
        Speed3,
    }

    /// <summary>
    /// E1 — time-control HUD. Pause / 1x / 2x / 3x + a status label.
    /// The presenter reads pause reasons and speed DIRECTLY from TimeSystem; this
    /// view only renders what it is told. Buttons are wired/unwired by the
    /// presenter (WireButtons/UnwireButtons, TaskAssignmentView pattern).
    /// </summary>
    public class TimeControlView : UIViewBase
    {
        [SerializeField] private Button pauseButton;
        [SerializeField] private Button speed1Button;
        [SerializeField] private Button speed2Button;
        [SerializeField] private Button speed3Button;
        [SerializeField] private TMP_Text statusText;

        public event Action PauseClicked;
        public event Action ToggleClicked;   // Space hotkey — same rule as Pause
        public event Action<int> SpeedClicked;

        private bool _wired;

        /// <summary>
        /// E1.1 — THE highlight rule, pure so tests can call it without a view:
        /// active ANY pause reason (player or pending decision) highlights Pause and
        /// NO speed button; while running the highlight follows the speed. It takes
        /// only the resolved state, so a decision event that is pending while
        /// AutoPauseOnDecisionEvent is false (game still running) keeps the speed
        /// button highlighted.
        /// </summary>
        public static TimeControlActiveButton ActiveButton(bool isPaused, int speed)
        {
            if (isPaused) return TimeControlActiveButton.Pause;
            if (speed <= 1) return TimeControlActiveButton.Speed1;
            if (speed == 2) return TimeControlActiveButton.Speed2;
            return TimeControlActiveButton.Speed3;
        }

        public void WireButtons()
        {
            if (_wired) return;
            if (pauseButton != null) pauseButton.onClick.AddListener(OnPause);
            if (speed1Button != null) speed1Button.onClick.AddListener(OnSpeed1);
            if (speed2Button != null) speed2Button.onClick.AddListener(OnSpeed2);
            if (speed3Button != null) speed3Button.onClick.AddListener(OnSpeed3);
            _wired = true;
        }

        public void UnwireButtons()
        {
            if (!_wired) return;
            if (pauseButton != null) pauseButton.onClick.RemoveListener(OnPause);
            if (speed1Button != null) speed1Button.onClick.RemoveListener(OnSpeed1);
            if (speed2Button != null) speed2Button.onClick.RemoveListener(OnSpeed2);
            if (speed3Button != null) speed3Button.onClick.RemoveListener(OnSpeed3);
            _wired = false;
        }

        private void OnDestroy()
        {
            UnwireButtons();
            PauseClicked = null;
            ToggleClicked = null;
            SpeedClicked = null;
        }

        private void OnPause() => PauseClicked?.Invoke();
        private void OnSpeed1() => SpeedClicked?.Invoke(1);
        private void OnSpeed2() => SpeedClicked?.Invoke(2);
        private void OnSpeed3() => SpeedClicked?.Invoke(3);

        public void SetStatus(string text)
        {
            if (statusText != null) statusText.text = text;
        }

        /// <summary>
        /// E1.1 — renders <see cref="ActiveButton"/> for the live state. When paused,
        /// no speed button is highlighted.
        /// </summary>
        public void SetHighlight(bool isPaused, int speed)
        {
            var active = ActiveButton(isPaused, speed);
            SetButtonHighlight(pauseButton, active == TimeControlActiveButton.Pause);
            SetButtonHighlight(speed1Button, active == TimeControlActiveButton.Speed1);
            SetButtonHighlight(speed2Button, active == TimeControlActiveButton.Speed2);
            SetButtonHighlight(speed3Button, active == TimeControlActiveButton.Speed3);
        }

        private static readonly Color ActiveSpeedColor = new Color(0.95f, 0.78f, 0.30f);
        private static readonly Color InactiveSpeedColor = new Color(0.80f, 0.80f, 0.80f);

        private static void SetButtonHighlight(Button button, bool active)
        {
            if (button == null) return;
            var img = button.targetGraphic as Image;
            if (img != null) img.color = active ? ActiveSpeedColor : InactiveSpeedColor;
        }

        /// <summary>
        /// E1 hotkeys — legacy Input Manager (already used across this project:
        /// DiscipleListView/DiscipleDetailView Escape, Input.GetKeyDown). No new
        /// package. Guarded so keystrokes never fire while a text input has focus.
        /// </summary>
        private void Update()
        {
            if (IsTextInputFocused()) return;

            if (Input.GetKeyDown(KeyCode.Space)) ToggleClicked?.Invoke();
            if (Input.GetKeyDown(KeyCode.Alpha1)) OnSpeed1();
            if (Input.GetKeyDown(KeyCode.Alpha2)) OnSpeed2();
            if (Input.GetKeyDown(KeyCode.Alpha3)) OnSpeed3();
        }

        private static bool IsTextInputFocused()
        {
            var es = EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            if (go == null) return false;
            return go.GetComponent<TMP_InputField>() != null
                || go.GetComponentInParent<TMP_InputField>() != null;
        }

        /// <summary>
        /// E0 free region: top-left, just BELOW the 100px WalletHud bar (top strip).
        /// Clear of WalletHud (top-right cluster), LogWindow (bottom-left 400x300)
        /// and BottomMenu (bottom bar). 560x56 at anchored (12, -110).
        /// </summary>
        public override void ApplyDefaultLayout()
        {
            var rt = GetComponent<RectTransform>();
            if (rt == null) return;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(560f, 56f);
            rt.anchoredPosition = new Vector2(12f, -110f);
        }

        // ---- test accessors (EditMode tests, no reflection — C12) ----
        public string StatusTextForTest => statusText != null ? statusText.text : null;
        public void InvokePauseForTest() => OnPause();
        public void InvokeToggleForTest() => ToggleClicked?.Invoke();
        public void InvokeSpeedForTest(int speed)
        {
            if (speed == 1) OnSpeed1();
            else if (speed == 2) OnSpeed2();
            else if (speed == 3) OnSpeed3();
        }

        public bool IsPauseActiveForTest
        {
            get
            {
                var img = pauseButton != null ? pauseButton.targetGraphic as Image : null;
                return img != null && img.color == ActiveSpeedColor;
            }
        }

        public bool IsSpeedActiveForTest(int speed)
        {
            var button = speed == 1 ? speed1Button : speed == 2 ? speed2Button : speed3Button;
            var img = button != null ? button.targetGraphic as Image : null;
            return img != null && img.color == ActiveSpeedColor;
        }
    }
}
