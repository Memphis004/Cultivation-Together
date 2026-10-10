using System;

namespace Xianxia.Sect.UI
{
    /// <summary>Open args for the chip — the opener owns the popup, so it supplies both the
    /// "is the popup on screen right now" probe and the click action.</summary>
    public sealed class WorldEventChipArgs
    {
        public Func<bool> IsPopupVisible;
        public Action Clicked;
    }

    /// <summary>
    /// E3 — chip presenter. "Pending" is read from <see cref="TimeSystem"/>'s
    /// authoritative state (E2-lite), never inferred from a message, so a chip whose
    /// panel is opened late still shows the right thing. The visibility rule:
    /// pending AND (popup not on screen OR a modal is blocking it).
    /// </summary>
    public class WorldEventChipPresenter : UIPresenter<WorldEventChipView>
    {
        public const string PendingLabel = "มีเหตุการณ์รอตัดสินใจ";
        public const float HighlightSeconds = 1.2f;

        private readonly TimeSystem _timeSystem;

        private WorldEventChipArgs _args;
        private string _lastSeenEventId;

        public WorldEventChipPresenter(TimeSystem timeSystem)
        {
            _timeSystem = timeSystem;
        }

        /// <summary>Pure rule, so the three cases (pending+hidden, pending+modal, none) are testable directly.</summary>
        public static bool ShouldShow(bool hasPendingDecision, bool popupVisible, bool modalOpen)
            => hasPendingDecision && (!popupVisible || modalOpen);

        protected override void OnViewBound()
        {
            View.Clicked += OnChipClicked;
        }

        public override void OnOpen(object args)
        {
            _args = args as WorldEventChipArgs;
            View.SetText(PendingLabel);
            Refresh();
        }

        /// <summary>
        /// Pushed by the orchestrator (every frame from its canvas hook, and on every
        /// trigger message) so the chip tracks the popup's real state without polling
        /// from inside the presenter.
        /// </summary>
        public void Refresh()
        {
            bool pending = _timeSystem.HasPendingDecision;
            bool show = ShouldShow(pending, _args?.IsPopupVisible?.Invoke() ?? false, DiscipleModalScope.IsOpen);

            if (!pending)
            {
                _lastSeenEventId = null; // the event ended - forget it
                View.SetShown(false);
                return;
            }

            View.SetShown(show);

            // An event is remembered as "announced" only once the chip was actually the
            // visible signal. One that arrived while the popup was up (nothing to flash)
            // therefore still flashes the moment it becomes the chip's job.
            if (!show || _timeSystem.PendingEventId == _lastSeenEventId) return;

            _lastSeenEventId = _timeSystem.PendingEventId;
            View.FlashHighlight(HighlightSeconds);
        }

        private void OnChipClicked() => _args?.Clicked?.Invoke();

        public override void Dispose()
        {
            if (View != null) View.Clicked -= OnChipClicked;
            _args = null;
            _lastSeenEventId = null;
        }
    }
}
