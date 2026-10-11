using System;
using MessagePipe;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.UI
{
    // Scope for v1: narrative-worthy events only (recruit, world event,
    // decision made) - deliberately NOT SectResourceChangedMessage, which
    // fires on every gathering/crafting tick and would flood the log with
    // numbers instead of the story beats a stream audience actually cares
    // about. Add a subscription here later if resource-level detail turns
    // out to be wanted too.
    public class LogWindowPresenter : UIPresenter<LogWindowView>
    {
        private readonly ISubscriber<DiscipleRecruitedMessage> _discipleRecruitedSubscriber;
        private readonly ISubscriber<WorldEventTriggeredMessage> _worldEventSubscriber;
        private readonly ISubscriber<DecisionExecutedMessage> _decisionExecutedSubscriber;
        // P14B — a committed breakthrough is a story beat (one line per stage), unlike the
        // per-tick attribute/resource traffic this log deliberately ignores. In-memory only.
        private readonly ISubscriber<DiscipleCultivationChangedMessage> _cultivationChangedSubscriber;

        private IDisposable _discipleSub;
        private IDisposable _eventSub;
        private IDisposable _decisionSub;
        private IDisposable _cultivationSub;

        public LogWindowPresenter(
            ISubscriber<DiscipleRecruitedMessage> discipleRecruitedSubscriber,
            ISubscriber<WorldEventTriggeredMessage> worldEventSubscriber,
            ISubscriber<DecisionExecutedMessage> decisionExecutedSubscriber,
            ISubscriber<DiscipleCultivationChangedMessage> cultivationChangedSubscriber = null)
        {
            _discipleRecruitedSubscriber = discipleRecruitedSubscriber;
            _worldEventSubscriber = worldEventSubscriber;
            _decisionExecutedSubscriber = decisionExecutedSubscriber;
            // Optional (null) so existing construction sites stay valid; production resolves it.
            _cultivationChangedSubscriber = cultivationChangedSubscriber;
        }


        protected override void OnViewBound()
        {
            _discipleSub = _discipleRecruitedSubscriber.Subscribe(OnDiscipleRecruited);
            _eventSub = _worldEventSubscriber.Subscribe(OnWorldEvent);
            _decisionSub = _decisionExecutedSubscriber.Subscribe(OnDecisionExecuted);
            if (_cultivationChangedSubscriber != null)
                _cultivationSub = _cultivationChangedSubscriber.Subscribe(OnCultivationChanged);

            View.AddLine("Sect log started.");
        }

        private void OnDiscipleRecruited(DiscipleRecruitedMessage message)
        {
            View.AddLine($"A new disciple, {message.DisplayName}, has joined the sect.");
        }

        private void OnWorldEvent(WorldEventTriggeredMessage message)
        {
            View.AddLine($"Event: {message.Description}");
        }

        private void OnDecisionExecuted(DecisionExecutedMessage message)
        {
            View.AddLine($"Decision made for '{message.EventId}': {message.ChoiceId}");
        }

        /// <summary>
        /// P14B — one narrative line per committed breakthrough. The stage is reported with its
        /// Thai display name (from the shared table) as well as its id, so the log stays readable
        /// without a second lookup table in the UI layer.
        /// </summary>
        private void OnCultivationChanged(DiscipleCultivationChangedMessage message)
        {
            View.AddLine($"{message.DiscipleId} broke through to " +
                         $"{CultivationConfig.DisplayName(message.NewStageId)} ({message.NewStageId}).");
        }

        public override void Dispose()
        {
            _discipleSub?.Dispose();
            _eventSub?.Dispose();
            _decisionSub?.Dispose();
            _cultivationSub?.Dispose();
        }
    }
}
