using System;
using System.Collections.Generic;
using System.Linq;
using MessagePipe;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.UI
{
    public class EventPopupOpenArgs
    {
        public string EventId;
        public string Description;
        public List<EventChoiceInfo> Choices;

        /// <summary>
        /// Supplied by the opener (<see cref="WorldEventUISystem"/>) so the panel never
        /// needs a UIService reference of its own — same shape as
        /// <see cref="DiscipleListArgs.CloseCallback"/>. Invoked both when the player
        /// hides the popup and when it closes itself after the "resolved elsewhere" note.
        /// </summary>
        public Action CloseCallback;
    }

    /// <summary>
    /// E3 — popup presenter. Decision flow (item 6):
    ///   - one click disables every choice, then calls DecisionExecutor ONCE
    ///   - accepted  → the opener closes the popup
    ///   - rejected  → the reason is shown inline; choices re-enable only while the
    ///                 event is still pending (stale/duplicate nothing to retry)
    ///   - resolved elsewhere while open → note for a few seconds, then close
    /// DecisionExecutor stays the single decision path; no new pub/sub is added.
    /// </summary>
    public class EventPopupPresenter : UIPresenter<EventPopupView>
    {
        /// <summary>How long "ตัดสินใจแล้วจากที่อื่น" stays on screen before the popup closes.</summary>
        public const float ResolvedNoteSeconds = 3f;

        // Fixed from the original spec draft: this must call DecisionExecutor
        // directly (in-process), not publish through IPublisher<ExecuteDecisionMessage>.
        // DecisionLogger only listens on IDistributedSubscriber<string,
        // ExecuteDecisionMessage> (the interprocess/bridge channel) - a plain
        // in-memory IPublisher<ExecuteDecisionMessage> publish would go to a
        // completely different, unlistened channel and silently do nothing.
        private readonly DecisionExecutor _decisionExecutor;
        private readonly TimeSystem _timeSystem;
        private readonly ISubscriber<DecisionExecutedMessage> _decisionExecutedSubscriber;

        private IDisposable _subscription;
        private Action _closeCallback;
        private string _currentEventId;
        private bool _processing;

        public EventPopupPresenter(
            DecisionExecutor decisionExecutor,
            TimeSystem timeSystem,
            ISubscriber<DecisionExecutedMessage> decisionExecutedSubscriber)
        {
            _decisionExecutor = decisionExecutor;
            _timeSystem = timeSystem;
            _decisionExecutedSubscriber = decisionExecutedSubscriber;
        }

        protected override void OnViewBound()
        {
            View.ChoiceClicked += OnChoiceClicked;
            View.HideRequested += OnHideRequested;
            View.ResolvedNoteFinished += OnResolvedNoteFinished;

            // Trigger only: "someone else (the AI GM over MCP) resolved this event".
            // The pending state itself is read from TimeSystem, never inferred here.
            _subscription = _decisionExecutedSubscriber.Subscribe(OnDecisionExecuted);
        }

        public override void OnOpen(object args)
        {
            var openArgs = args as EventPopupOpenArgs;
            _closeCallback = openArgs?.CloseCallback;
            _currentEventId = openArgs?.EventId;
            _processing = false;

            View.SetEvent(openArgs?.EventId, openArgs?.Description);
            View.ClearStatus();

            var choices = (openArgs?.Choices ?? new List<EventChoiceInfo>())
                .Select(c => new EventChoiceViewData { ChoiceId = c.ChoiceId, Label = c.Label })
                .ToList();
            View.SetChoices(choices);
            View.SetChoicesEnabled(true);
        }

        private void OnChoiceClicked(string choiceId)
        {
            if (_processing) return;                 // one click → exactly one decision
            _processing = true;
            View.SetChoicesEnabled(false);            // disabled until the executor returns

            DecisionResult result;
            try
            {
                result = _decisionExecutor.Execute(_currentEventId, choiceId);
            }
            finally
            {
                _processing = false;
            }

            if (result.Accepted)
            {
                View.ClearStatus();
                _closeCallback?.Invoke();
                return;
            }

            // Rejection (stale / duplicate / invalid / nothing pending): DecisionExecutor
            // already logged the warning - show the same reason inline. Re-enable only if
            // the event is still pending, otherwise there is nothing left to answer.
            View.SetStatus(result.Reason);
            View.SetChoicesEnabled(_timeSystem.HasPendingDecision);
        }

        /// <summary>The player hid the popup: no decision, no resume, no clearing of the event.</summary>
        private void OnHideRequested()
        {
            _closeCallback?.Invoke();
        }

        private void OnResolvedNoteFinished()
        {
            _closeCallback?.Invoke();
        }

        private void OnDecisionExecuted(DecisionExecutedMessage message)
        {
            // Ignore our own click: Execute publishes synchronously, and that path
            // already handled the close above.
            if (_processing) return;
            if (message.EventId != _currentEventId) return;
            if (_timeSystem.HasPendingDecision) return; // still waiting on something else

            View.ShowResolvedElsewhereNote(ResolvedNoteSeconds);
        }

        public override void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;

            if (View != null)
            {
                View.ChoiceClicked -= OnChoiceClicked;
                View.HideRequested -= OnHideRequested;
                View.ResolvedNoteFinished -= OnResolvedNoteFinished;
            }

            _closeCallback = null;
            _currentEventId = null;
            _processing = false;
        }
    }
}
