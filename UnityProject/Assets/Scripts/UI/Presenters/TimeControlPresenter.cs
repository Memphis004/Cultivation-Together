using System;
using MessagePipe;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// E1.1 — time-control HUD presenter. Reads pause reasons and speed directly from
    /// <see cref="TimeSystem"/> (never infers them from the message); the
    /// <see cref="TimeSpeedChangedMessage"/> is used only as a refresh trigger, so
    /// it is honored even when IsPaused itself does not change (e.g. a second
    /// reason becomes active while already paused).
    ///
    /// There is no Play button: a speed button IS "play at Nx", so pressing a speed
    /// both picks the speed and clears the pause.
    /// </summary>
    public class TimeControlPresenter : UIPresenter<TimeControlView>
    {
        public const string PausedByPlayerLabel = "หยุดโดยผู้เล่น";
        public const string AwaitingDecisionLabel = "หยุด - รอการตัดสินใจ";
        public const string RunningLabelFormat = "กำลังเดิน {0}x";

        private readonly TimeSystem _timeSystem;
        private readonly ISubscriber<TimeSpeedChangedMessage> _speedSubscriber;
        private IDisposable _subscription;

        public TimeControlPresenter(TimeSystem timeSystem, ISubscriber<TimeSpeedChangedMessage> speedSubscriber)
        {
            _timeSystem = timeSystem;
            _speedSubscriber = speedSubscriber;
        }

        protected override void OnViewBound()
        {
            View.PauseClicked += OnPauseClicked;
            View.ToggleClicked += OnToggleClicked;
            View.SpeedClicked += OnSpeedClicked;
            View.WireButtons();

            _subscription = _speedSubscriber.Subscribe(_ => RefreshView());

            // Read current state once on bind; afterwards the trigger message is
            // the only refresh cue (no per-frame polling).
            RefreshView();
        }

        // Handlers only MUTATE. The refresh cue is exclusively the
        // TimeSpeedChangedMessage that TimeSystem publishes on an effective
        // change, so the HUD never refreshes outside the trigger (and never polls).

        /// <summary>
        /// Pause is a toggle. Pausing adds the User reason; resuming from ANY reason
        /// uses ResumeByPlayer (which clears both reasons), so a pending decision can
        /// never lock the player out. The resume keeps the current speed.
        /// </summary>
        private void OnPauseClicked()
        {
            if (_timeSystem.IsPaused) _timeSystem.ResumeByPlayer();
            else _timeSystem.Pause(TimePauseReason.User);
        }

        // Space: exactly the Pause button's rule.
        private void OnToggleClicked() => OnPauseClicked();

        /// <summary>
        /// E1.1 — Nx: SetSpeed FIRST, then resume if paused, so no frame runs at the
        /// old speed. ResumeByPlayer clears both reasons; the pending event stays
        /// pending (the chip stays visible) because the pending state is separate from
        /// the PendingDecision pause reason. Already running at Nx → SetSpeed is a
        /// no-op and nothing is paused, so the click does nothing.
        /// </summary>
        private void OnSpeedClicked(int speed)
        {
            _timeSystem.SetSpeed(speed);
            if (_timeSystem.IsPaused) _timeSystem.ResumeByPlayer();
        }

        /// <summary>
        /// Reads live state. Awaiting-decision wording wins when both reasons are
        /// active. The highlight is the shared pure rule (TimeControlView.ActiveButton),
        /// so a pending decision that does NOT pause (AutoPauseOnDecisionEvent false)
        /// leaves the speed button highlighted.
        /// </summary>
        private void RefreshView()
        {
            string status;
            if (_timeSystem.IsPendingDecisionPaused) status = AwaitingDecisionLabel;
            else if (_timeSystem.IsUserPaused) status = PausedByPlayerLabel;
            else status = string.Format(RunningLabelFormat, _timeSystem.Speed);

            View.SetStatus(status);
            View.SetHighlight(_timeSystem.IsPaused, _timeSystem.Speed);
        }

        public override void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;

            View.PauseClicked -= OnPauseClicked;
            View.ToggleClicked -= OnToggleClicked;
            View.SpeedClicked -= OnSpeedClicked;
            View.UnwireButtons();
        }
    }
}
