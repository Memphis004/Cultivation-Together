using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// P12B — the Title screen panel: New Game / Continue / Quit + one status line.
    ///
    /// The view is pure presentation: it renders what the presenter tells it and raises clicks.
    /// It never reads save state, never enables Continue on its own, and never decides when the
    /// screen is visible — <see cref="TitleMenuPresenter"/> derives all of that from the
    /// authoritative coordinator / save metadata.
    ///
    /// The panel is camera-independent on purpose: it draws its OWN opaque full-screen Ink
    /// background under a Paper window, so the Title screen looks intentional whether or not a
    /// camera is rendering (see the P12B survey note — the core scene's Camera GameObject is
    /// inactive and the gameplay backdrop lives in the additive gameplay scene, which is
    /// unloaded at Title).
    /// </summary>
    public class TitleMenuView : UIViewBase
    {
        [SerializeField] private Button newGameButton;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private TMP_Text statusText;

        public event Action NewGameClicked;
        public event Action ContinueClicked;
        public event Action QuitClicked;

        private bool _wired;

        /// <summary>Wired/unwired by the presenter (TaskAssignmentView / TimeControlView pattern).</summary>
        public void WireButtons()
        {
            if (_wired) return;
            if (newGameButton != null) newGameButton.onClick.AddListener(OnNewGame);
            if (continueButton != null) continueButton.onClick.AddListener(OnContinue);
            if (quitButton != null) quitButton.onClick.AddListener(OnQuit);
            _wired = true;
        }

        public void UnwireButtons()
        {
            if (!_wired) return;
            if (newGameButton != null) newGameButton.onClick.RemoveListener(OnNewGame);
            if (continueButton != null) continueButton.onClick.RemoveListener(OnContinue);
            if (quitButton != null) quitButton.onClick.RemoveListener(OnQuit);
            _wired = false;
        }

        private void OnDestroy()
        {
            UnwireButtons();
            NewGameClicked = null;
            ContinueClicked = null;
            QuitClicked = null;
        }

        private void OnNewGame() => NewGameClicked?.Invoke();
        private void OnContinue() => ContinueClicked?.Invoke();
        private void OnQuit() => QuitClicked?.Invoke();

        public void SetStatus(string text)
        {
            if (statusText != null) statusText.text = text ?? string.Empty;
        }

        /// <summary>
        /// P12B — the ONE place that decides the three buttons' state, so "Continue needs a save"
        /// and "nothing works mid-transition" cannot be applied inconsistently. Continue is a
        /// separate flag from the other two because it additionally requires metadata.
        /// </summary>
        public void SetButtonsInteractable(bool newGame, bool canContinue, bool quit)
        {
            SetInteractable(newGameButton, newGame);
            SetInteractable(continueButton, canContinue);
            SetInteractable(quitButton, quit);
        }

        private static void SetInteractable(Button button, bool interactable)
        {
            if (button == null) return;
            button.interactable = interactable;

            // Generators build these buttons with Transition.None (InkWidgets.OutlineButton), so the
            // disabled look is NOT automatic — tint the Paper plate so a disabled button reads as
            // disabled instead of just not clicking.
            var paper = button.transform.Find("Paper");
            var image = paper != null ? paper.GetComponent<Image>() : null;
            if (image != null) image.color = interactable ? UiPalette.Paper : UiPalette.Disabled;
        }

        /// <summary>Full-screen: the whole screen is the Title, so it is safe to stretch.</summary>
        public override void ApplyDefaultLayout()
        {
            var rt = GetComponent<RectTransform>();
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // ---- test accessors (EditMode tests, no reflection — C12) ----
        public bool NewGameInteractableForTest => newGameButton != null && newGameButton.interactable;
        public bool ContinueInteractableForTest => continueButton != null && continueButton.interactable;
        public bool QuitInteractableForTest => quitButton != null && quitButton.interactable;
        public string StatusForTest => statusText != null ? statusText.text : null;
        public void InvokeNewGameForTest() => OnNewGame();
        public void InvokeContinueForTest() => OnContinue();
        public void InvokeQuitForTest() => OnQuit();
    }
}
