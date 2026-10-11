using System;
using System.Collections.Generic;
using System.Linq;
using MessagePipe;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.UI
{
    public class EventPopupOpenArgs
    {
        /// <summary>
        /// P13A — the pending INSTANCE this popup is showing. A decision always targets it, so
        /// a popup left open for an occurrence that is no longer pending can only produce a
        /// rejection, never a wrong decision.
        /// </summary>
        public string InstanceId;

        public string EventId;
        public string Description;
        public List<EventChoiceInfo> Choices;

        /// <summary>
        /// P13B — optional speaker (the disciple whose portrait the panel renders). Empty for an
        /// event that has no speaker; carried through so a restored event rebuilds the same
        /// portrait instead of a generic panel.
        /// </summary>
        public string SpeakerDiscipleId;

        /// <summary>Display name shown beside the speaker portrait; empty when there is no speaker.</summary>
        public string SpeakerDisplayName;

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

        /// <summary>
        /// P13B — used ONLY to resolve the optional speaker's appearance (the same roster read
        /// DiscipleDetailPresenter does). Null in a construction site that wires no roster: the
        /// panel then renders no portrait rather than failing to open.
        /// </summary>
        private readonly ISectStateProvider _stateProvider;

        private IDisposable _subscription;
        private Action _closeCallback;
        private string _currentInstanceId;
        private string _currentEventId;
        private bool _processing;

        public EventPopupPresenter(
            DecisionExecutor decisionExecutor,
            TimeSystem timeSystem,
            ISubscriber<DecisionExecutedMessage> decisionExecutedSubscriber,
            ISectStateProvider stateProvider = null)
        {
            _decisionExecutor = decisionExecutor;
            _timeSystem = timeSystem;
            _decisionExecutedSubscriber = decisionExecutedSubscriber;
            _stateProvider = stateProvider;
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

            // What the OPENER handed over is what this panel shows, deliberately: the opener is
            // the authority on the current event (WorldEventUISystem reads TimeSystem, never a
            // message payload), and if it ever hands over content that contradicts the pending
            // state the click is REJECTED with the reason shown inline rather than silently
            // reconciled. Re-deriving here would hide that contradiction from the player.
            //
            // Re-derivation happens instead on every orchestrator sweep, through
            // RefreshFromAuthority, which re-targets the panel when the CURRENT occurrence is no
            // longer the one on screen.
            Show(openArgs?.InstanceId, openArgs?.EventId, openArgs?.Description,
                 openArgs?.Choices ?? new List<EventChoiceInfo>(),
                 openArgs?.SpeakerDiscipleId, openArgs?.SpeakerDisplayName);
        }

        /// <summary>
        /// P13B — re-derive the WHOLE panel from the authoritative pending state. Called by the
        /// orchestrator on its per-frame sweep, so the panel can never keep showing an occurrence
        /// that has been resolved or superseded: this is a no-op unless the CURRENT pending
        /// instance differs from the one on screen, so it does not rebuild the choice buttons
        /// every frame.
        ///
        /// This is the ONLY place the panel adopts authoritative content on its own — see
        /// <see cref="OnOpen"/> for why an open deliberately trusts the opener instead.
        /// </summary>
        public void RefreshFromAuthority()
        {
            if (!_timeSystem.HasPendingDecision) return;

            string currentInstanceId = _timeSystem.PendingInstanceId;
            if (string.IsNullOrEmpty(currentInstanceId) || currentInstanceId == _currentInstanceId) return;

            PendingWorldEvent pending;
            if (!_timeSystem.TryGetPendingDecision(currentInstanceId, out pending)) return;

            Show(pending.InstanceId, pending.EventId, pending.Description,
                 new List<EventChoiceInfo>(pending.Choices),
                 pending.SpeakerDiscipleId, pending.SpeakerDisplayName);
        }

        /// <summary>
        /// Render one occurrence (identity + instance-specific payload). Re-enables the choices
        /// and clears any stale status: whatever was on screen before belongs to a DIFFERENT
        /// occurrence, so carrying its error text over would be a lie about this one.
        /// </summary>
        private void Show(string instanceId, string eventId, string description, List<EventChoiceInfo> choices,
                          string speakerDiscipleId, string speakerDisplayName)
        {
            _currentInstanceId = instanceId;
            _currentEventId = eventId;
            _processing = false;

            View.SetEvent(eventId, description);
            View.ClearStatus();

            var viewChoices = (choices ?? new List<EventChoiceInfo>())
                .Where(c => c != null)
                .Select(c => new EventChoiceViewData { ChoiceId = c.ChoiceId, Label = c.Label })
                .ToList();
            View.SetChoices(viewChoices);
            View.SetChoicesEnabled(true);

            View.SetSpeaker(speakerDisplayName, ResolveSpeakerAppearance(speakerDiscipleId));
        }

        /// <summary>
        /// The speaker's appearance, from the authoritative roster — the portrait is RE-DERIVED on
        /// load, never serialized (§4). No speaker, an unknown disciple, or no roster wired all
        /// resolve to null, which the view renders as "no speaker area".
        /// </summary>
        private AvatarAppearance ResolveSpeakerAppearance(string discipleId)
        {
            if (string.IsNullOrEmpty(discipleId) || _stateProvider == null) return null;

            var disciples = _stateProvider.BuildSectEconomyState()?.Disciples;
            if (disciples == null) return null;

            for (int i = 0; i < disciples.Count; i++)
            {
                var d = disciples[i];
                if (d == null || d.DiscipleId != discipleId) continue;
                return d.Avatar != null ? d.Avatar.Clone() : new AvatarAppearance();
            }

            return null; // the speaker is not in the roster (left / not spawned) - no portrait
        }

        private void OnChoiceClicked(string choiceId)
        {
            if (_processing) return;                 // one click → exactly one decision
            _processing = true;
            View.SetChoicesEnabled(false);            // disabled until the executor returns

            DecisionResult result;
            try
            {
                // P13A — the decision names the instance this popup is showing, so a click can
                // only ever resolve THAT occurrence (a stale popup is rejected, not guessed at).
                result = _decisionExecutor.Execute(_currentInstanceId, _currentEventId, choiceId);
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

        /// <summary>
        /// The instance this panel is currently showing (null when none). Lets the orchestrator
        /// tell "the panel is already on this occurrence" from "the panel is stale".
        /// </summary>
        public string ShownInstanceId => _currentInstanceId;

        /// <summary>True while a decision is in flight (choices are locked).</summary>
        public bool IsProcessing => _processing;

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

            // P13A — match the INSTANCE the message names (exact identity, so two occurrences
            // of one definition are never confused). A publisher that only knew the definition
            // id sends no instance id; then the event id is the only thing to compare.
            bool isOurs = !string.IsNullOrEmpty(message.InstanceId)
                ? message.InstanceId == _currentInstanceId
                : message.EventId == _currentEventId;
            if (!isOurs) return;

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
            _currentInstanceId = null;
            _currentEventId = null;
            _processing = false;
        }
    }
}
