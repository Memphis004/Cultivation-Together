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
        private readonly IGameSessionCoordinator _coordinator;
        private readonly ISubscriber<SessionPhaseChangedMessage> _phaseSubscriber;
        private IDisposable _subscription;
        private IDisposable _phaseSubscription;

        private UIPanelHandle _popup;
        private string _popupInstanceId;  // the pending INSTANCE the open popup is showing
        private UIPanelHandle _chip;

        // P13A — the player hides the popup for a specific OCCURRENCE, not for a definition:
        // hiding one bandit raid must not keep the popup closed for the next one.
        private string _hiddenInstanceId;
        private bool _chipRaised;
        private bool _chipHidden;
        private bool _chipOpenAttempted;

        public WorldEventUISystem(
            ISubscriber<WorldEventTriggeredMessage> subscriber,
            UIService uiService,
            TimeSystem timeSystem,
            IGameSessionCoordinator coordinator = null,
            ISubscriber<SessionPhaseChangedMessage> phaseSubscriber = null)
        {
            _subscriber = subscriber;
            _uiService = uiService;
            _timeSystem = timeSystem;
            _coordinator = coordinator;
            _phaseSubscriber = phaseSubscriber;
        }

        public void Start()
        {
            _subscription = _subscriber.Subscribe(_ => Refresh());
            // P12B §4 — the phase decides whether any of this UI may be on screen, and the
            // message is only the cue to re-derive that from the coordinator.
            _phaseSubscription = _phaseSubscriber?.Subscribe(_ => Refresh());

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
            // P12B §4 — outside Playing there is no session event UI at all. The popup is CLOSED
            // (not merely left on screen) and the chip is hidden, because a pending decision
            // cannot exist without a live session: the pending state is cleared at the session
            // boundary, so nothing here can lose a real event.
            if (!SessionPlaying)
            {
                if (_popup != null) ClosePopup();
                _hiddenInstanceId = null;
                _chipRaised = false;
                SetChipHidden(true);
                return;
            }

            // The chip is session UI: CloseSessionUi destroys it when a session ends, so the
            // handle must be re-opened rather than used through a destroyed instance.
            EnsureChipOpen();
            SetChipHidden(false);

            bool pending = _timeSystem.HasPendingDecision;
            // P13A — the CURRENT event is the oldest undecided one; every decision below is
            // keyed on its instance id, never on the definition id.
            string currentInstanceId = _timeSystem.PendingInstanceId;

            if (!pending)
            {
                if (_popup != null) ClosePopup();
                _hiddenInstanceId = null;   // the event ended - nothing is "hidden" any more
            }
            else
            {
                // P13A — while several decisions are outstanding the popup must always show the
                // CURRENT (oldest undecided) instance. `_popup != null` for a DIFFERENT instance
                // means a stale event is still on screen, so this re-targets it: UIService.Open
                // feeds the existing panel instance the new args. That keeps the resolved
                // event's choices from lingering (a click from them could only be rejected)
                // without destroying and rebuilding the panel.
                bool needsOpen = (_popup == null || _popupInstanceId != currentInstanceId)
                                 && !DiscipleModalScope.IsOpen                // never over a modal
                                 && _hiddenInstanceId != currentInstanceId;   // the player hid this one
                if (needsOpen) OpenPopup();

                // P13B — the panel is the AUTHORITATIVE view of the current occurrence: it
                // re-derives itself from the pending state rather than trusting the args it was
                // opened with, so a description/choice change under it (or a resolution it was
                // not told about) cannot leave a stale event on screen. A no-op unless the
                // current instance actually changed.
                if (_popup != null && _popup.Presenter is EventPopupPresenter popupPresenter)
                    popupPresenter.RefreshFromAuthority();
            }

            RefreshChip(pending);
        }

        /// <summary>
        /// P12B — is a live session Playing? Read from the authoritative coordinator (never from
        /// the message payload). No coordinator means "no session gate wired" (existing
        /// construction sites / tests), which behaves exactly as before.
        /// </summary>
        private bool SessionPlaying =>
            _coordinator == null || _coordinator.Phase == GameSessionPhase.Playing;

        /// <summary>True only while the chip's instance is still alive (a session end destroys it).</summary>
        private bool ChipAlive =>
            _chip != null && _chip.IsAlive;

        /// <summary>
        /// Re-open the chip after a session boundary destroyed it. A panel missing from the
        /// catalog (generator not run) is attempted ONCE — this runs per frame, and repeating a
        /// failing Open would flood the console.
        /// </summary>
        private void EnsureChipOpen()
        {
            if (ChipAlive) return;
            if (_chipOpenAttempted && _chip == null) return;

            _chipOpenAttempted = true;
            _chip = null;
            _chipHidden = false;
            OpenChip();
        }

        /// <summary>Hide/show the whole chip without touching its own pending/visible logic.</summary>
        private void SetChipHidden(bool hidden)
        {
            if (hidden == _chipHidden) return;
            _chipHidden = hidden;
            if (!ChipAlive) return;

            if (hidden) _chip.View.Hide();
            else _chip.View.Show();
        }

        private void RefreshChip(bool pending)
        {
            if (!ChipAlive) return;

            bool modalOpen = DiscipleModalScope.IsOpen;

            if (_chip.Presenter is WorldEventChipPresenter chip) chip.Refresh();

            // While a modal blocks the popup the chip is the only visible signal, so it
            // is raised above the modal (opened later, therefore drawn earlier). Done on
            // the state flip only — not per frame, which would dirty the layout.
            bool shouldRaise = pending && modalOpen;
            if (!shouldRaise || _chipRaised) return;

            _chipRaised = true;
            _chip.View.GameObject.transform.SetAsLastSibling();   // ChipAlive was checked by the caller
        }

        private void OpenPopup()
        {
            try
            {
                string instanceId = _timeSystem.PendingInstanceId;

                // P13B — the instance-specific payload rides with the open: the optional speaker
                // is what makes a restored event render the same panel (portrait + name) instead
                // of a generic one. The portrait ITSELF is not carried anywhere — the presenter
                // re-derives it from the roster.
                PendingWorldEvent pending;
                _timeSystem.TryGetPendingDecision(instanceId, out pending);

                _popup = _uiService.Open(PopupPanelId, new EventPopupOpenArgs
                {
                    InstanceId = instanceId,
                    EventId = _timeSystem.PendingEventId,
                    Description = _timeSystem.PendingDescription,
                    Choices = new List<EventChoiceInfo>(_timeSystem.PendingChoices),
                    SpeakerDiscipleId = pending?.SpeakerDiscipleId,
                    SpeakerDisplayName = pending?.SpeakerDisplayName,
                    CloseCallback = OnPopupCloseRequested,
                });
                _popupInstanceId = instanceId;
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
            if (_timeSystem.HasPendingDecision) _hiddenInstanceId = _timeSystem.PendingInstanceId;
            ClosePopup();
        }

        private void ClosePopup()
        {
            _popup = null;
            _popupInstanceId = null;
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
            _hiddenInstanceId = null;
            Refresh();
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
            _phaseSubscription?.Dispose();
            _phaseSubscription = null;
            Canvas.willRenderCanvases -= Refresh;
        }
    }
}
