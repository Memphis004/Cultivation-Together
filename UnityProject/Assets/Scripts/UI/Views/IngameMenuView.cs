using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// P12B — the in-game menu panel (opened by the menu button next to the time-control bar):
    /// Save, Return to Title, a status line, and the Return-to-Title confirmation block.
    ///
    /// The confirmation lives INSIDE this panel rather than in a second modal prefab: it is two
    /// extra buttons and a label, and "always ask before returning to Title" needs no new
    /// visibility system. Escape is deliberately NOT bound here — the event popup already owns it.
    /// </summary>
    public class IngameMenuView : UIViewBase
    {
        [SerializeField] private Button saveButton;
        [SerializeField] private Button returnToTitleButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private GameObject confirmRoot;
        [SerializeField] private Button confirmYesButton;
        [SerializeField] private Button confirmNoButton;

        public event Action SaveClicked;
        public event Action ReturnToTitleClicked;
        public event Action CloseClicked;
        public event Action ConfirmYesClicked;
        public event Action ConfirmNoClicked;

        private bool _wired;

        public void WireButtons()
        {
            if (_wired) return;
            if (saveButton != null) saveButton.onClick.AddListener(OnSave);
            if (returnToTitleButton != null) returnToTitleButton.onClick.AddListener(OnReturnToTitle);
            if (closeButton != null) closeButton.onClick.AddListener(OnClose);
            if (confirmYesButton != null) confirmYesButton.onClick.AddListener(OnConfirmYes);
            if (confirmNoButton != null) confirmNoButton.onClick.AddListener(OnConfirmNo);
            _wired = true;
        }

        public void UnwireButtons()
        {
            if (!_wired) return;
            if (saveButton != null) saveButton.onClick.RemoveListener(OnSave);
            if (returnToTitleButton != null) returnToTitleButton.onClick.RemoveListener(OnReturnToTitle);
            if (closeButton != null) closeButton.onClick.RemoveListener(OnClose);
            if (confirmYesButton != null) confirmYesButton.onClick.RemoveListener(OnConfirmYes);
            if (confirmNoButton != null) confirmNoButton.onClick.RemoveListener(OnConfirmNo);
            _wired = false;
        }

        private void OnDestroy()
        {
            UnwireButtons();
            SaveClicked = null;
            ReturnToTitleClicked = null;
            CloseClicked = null;
            ConfirmYesClicked = null;
            ConfirmNoClicked = null;
        }

        private void OnSave() => SaveClicked?.Invoke();
        private void OnReturnToTitle() => ReturnToTitleClicked?.Invoke();
        private void OnClose() => CloseClicked?.Invoke();
        private void OnConfirmYes() => ConfirmYesClicked?.Invoke();
        private void OnConfirmNo() => ConfirmNoClicked?.Invoke();

        public void SetStatus(string text)
        {
            if (statusText != null) statusText.text = text ?? string.Empty;
        }

        public void SetConfirmationVisible(bool visible)
        {
            if (confirmRoot != null && confirmRoot.activeSelf != visible) confirmRoot.SetActive(visible);
        }

        /// <summary>
        /// P12B — one rule for the whole panel: Save/Return-to-Title are dead while a transition
        /// is in flight, and the confirmation pair only responds while the confirmation is the
        /// thing on screen (so a stray click on the panel's own buttons cannot skip the prompt).
        /// </summary>
        public void SetButtonsInteractable(bool save, bool returnToTitle, bool confirm)
        {
            SetInteractable(saveButton, save);
            SetInteractable(closeButton, save);
            SetInteractable(returnToTitleButton, returnToTitle);
            SetInteractable(confirmYesButton, confirm && IsConfirmationVisible);
            SetInteractable(confirmNoButton, confirm && IsConfirmationVisible);
        }

        public bool IsConfirmationVisible => confirmRoot != null && confirmRoot.activeSelf;

        private static void SetInteractable(Button button, bool interactable)
        {
            if (button == null) return;
            button.interactable = interactable;
            var paper = button.transform.Find("Paper");
            var image = paper != null ? paper.GetComponent<Image>() : null;
            if (image != null) image.color = interactable ? UiPalette.Paper : UiPalette.Disabled;
        }

        /// <summary>Centred window; the panel root stretches over the screen as its own backdrop.</summary>
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
        public bool SaveInteractableForTest => saveButton != null && saveButton.interactable;
        public bool ReturnToTitleInteractableForTest => returnToTitleButton != null && returnToTitleButton.interactable;
        public bool ConfirmYesInteractableForTest => confirmYesButton != null && confirmYesButton.interactable;
        public string StatusForTest => statusText != null ? statusText.text : null;
        public void InvokeSaveForTest() => OnSave();
        public void InvokeReturnToTitleForTest() => OnReturnToTitle();
        public void InvokeConfirmYesForTest() => OnConfirmYes();
        public void InvokeConfirmNoForTest() => OnConfirmNo();
        public void InvokeCloseForTest() => OnClose();
    }
}
