using System;
using System.Collections.Generic;
using MessagePipe;
using VContainer.Unity;
using Xianxia.Sect.Messages;
using UnityEngine;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// E2-lite + E3 — the single owner of the world-event UI: the popup and the
    /// pending chip. Everything is derived from TimeSystem's AUTHORITATIVE pending
    /// state, never from a message payload alone, so a trigger raised before this
    /// system (or its panels) existed is never lost.
    ///
    /// Rules:
    ///   - no pending event            → popup closed, chip hidden
    ///   - pending, no modal, not hidden by the player → popup open, chip hidden
    ///   - pending, popup hidden by the player ("ซ่อน"/Escape) → chip visible, no
    ///     auto-reopen until the chip is clicked or a NEW event arrives
    ///   - pending while another modal is open → never forced over the modal; the
    ///     chip is the visible signal, and the popup opens once the modal closes
    /// </summary>
    public class WorldEventUISystem : IStartable, IDisposable
    {
        private const string PopupPanelId = "EventPopup";
        private const string ChipPanelId = "EventChip";

        private readonly ISubscriber<WorldEventTriggeredMessage> _subscriber;
        private readonly UIService _uiService;
        private readonly TimeSystem _timeSystem;
        private IDisposable _subscription;

        private UIPanelHandle _popup;
        private UIPanelHandle _chip;
        private string _hiddenEventId;   // the player hid the popup for THIS event
        private bool _chipRaised;

        public WorldEventUISystem(
            ISubscriber<WorldEventTriggeredMessage> subscriber,
            UIService uiService,
            TimeSystem timeSystem)
        {
            _subscriber = subscriber;
            _uiService = uiService;
            _timeSystem = timeSystem;
        }

        public void Start()
        {
            _subscription = _subscriber.Subscribe(_ => Refresh());

            OpenChip();   // the chip outlives individual events; Start() order cannot matter

            // A trigger raised before this system existed must not be lost: derive
            // from authoritative state instead of trusting the message.
            Refresh();

            // Per-frame re-check covers the two states that have no message at all:
            // another modal opening/closing (DiscipleModalScope), and the pending
            // state changing while the popup is closed.
            Canvas.willRenderCanvases += Refresh;
        }

        private void Refresh()
        {
            bool pending = _timeSystem.HasPendingDecision;

            if (!pending)
            {
                if (_popup != null) ClosePopup();
                _hiddenEventId = null;   // the event ended - nothing is "hidden" any more
            }
            else if (_popup == null
                     && !DiscipleModalScope.IsOpen                                         // never over a modal
                     && _hiddenEventId != _timeSystem.PendingEventId)                     // the player hid this one
            {
                OpenPopup();
            }

            RefreshChip(pending);
        }

        private void RefreshChip(bool pending)
        {
            if (_chip == null) return;

            bool modalOpen = DiscipleModalScope.IsOpen;

            if (_chip.Presenter is WorldEventChipPresenter chip) chip.Refresh();

            // While a modal blocks the popup the chip is the only visible signal, so it
            // is raised above the modal (opened later, therefore drawn earlier). Done on
            // the state flip only — not per frame, which would dirty the layout.
            bool shouldRaise = pending && modalOpen;
            if (!shouldRaise || _chipRaised) return;

            _chipRaised = true;
            if (_chip.View.GameObject != null) _chip.View.GameObject.transform.SetAsLastSibling();
        }

        private void OpenPopup()
        {
            try
            {
                _popup = _uiService.Open(PopupPanelId, new EventPopupOpenArgs
                {
                    EventId = _timeSystem.PendingEventId,
                    Description = _timeSystem.PendingDescription,
                    Choices = new List<EventChoiceInfo>(_timeSystem.PendingChoices),
                    CloseCallback = OnPopupCloseRequested,
                });
            }
            catch (Exception ex)
            {
                // MessagePipe subscription callbacks can swallow exceptions silently
                // depending on how they're invoked - log loudly so "the popup just
                // doesn't show up" isn't a silent failure.
                Debug.LogError($"[WorldEventUISystem] Failed to open EventPopup for event '{_timeSystem.PendingEventId}'. " +
                               "Run menu: Xianxia → Generate Event Popup Panel (once, in edit mode). " + ex);
            }
        }

        /// <summary>
        /// The popup asked to close: the player hid it ("ซ่อน"/Escape) or it finished its
        /// "resolved elsewhere" note. Hiding never chooses, never resumes time and never
        /// clears the pending event - it only records that this event must not be
        /// auto-reopened. (If it was resolved, nothing is pending and nothing is recorded.)
        /// </summary>
        private void OnPopupCloseRequested()
        {
            if (_timeSystem.HasPendingDecision) _hiddenEventId = _timeSystem.PendingEventId;
            ClosePopup();
        }

        private void ClosePopup()
        {
            _popup = null;
            try
            {
                _uiService.Close(PopupPanelId);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WorldEventUISystem] Failed to close {PopupPanelId}: {ex}");
            }
        }

        private void OpenChip()
        {
            try
            {
                _chip = _uiService.Open(ChipPanelId, new WorldEventChipArgs
                {
                    // "on screen right now": open AND not yielded to a modal.
                    IsPopupVisible = () => _popup != null && !DiscipleModalScope.IsOpen,
                    Clicked = OnChipClicked,
                });
            }
            catch (Exception ex)
            {
                // Same "generator not run yet" guard as UIBootstrap's panels.
                Debug.LogWarning("[WorldEventUISystem] EventChip panel is not in the catalog yet - " +
                                 "run menu: Xianxia → Generate Event Popup Panel (once, in edit mode). " +
                                 "(" + ex.Message + ")");
            }
        }

        private void OnChipClicked()
        {
            // The player asked for the popup back: clear "hidden" and let Refresh open it
            // (still blocked by a modal? then it opens as soon as the modal closes).
            _hiddenEventId = null;
            Refresh();
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
            Canvas.willRenderCanvases -= Refresh;
        }
    }
}
