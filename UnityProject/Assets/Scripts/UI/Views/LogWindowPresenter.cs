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

        private IDisposable _discipleSub;
        private IDisposable _eventSub;
        private IDisposable _decisionSub;

        public LogWindowPresenter(
            ISubscriber<DiscipleRecruitedMessage> discipleRecruitedSubscriber,
            ISubscriber<WorldEventTriggeredMessage> worldEventSubscriber,
            ISubscriber<DecisionExecutedMessage> decisionExecutedSubscriber)
        {
            _discipleRecruitedSubscriber = discipleRecruitedSubscriber;
            _worldEventSubscriber = worldEventSubscriber;
            _decisionExecutedSubscriber = decisionExecutedSubscriber;
        }

        protected override void OnViewBound()
        {
            _discipleSub = _discipleRecruitedSubscriber.Subscribe(OnDiscipleRecruited);
            _eventSub = _worldEventSubscriber.Subscribe(OnWorldEvent);
            _decisionSub = _decisionExecutedSubscriber.Subscribe(OnDecisionExecuted);

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

        public override void Dispose()
        {
            _discipleSub?.Dispose();
            _eventSub?.Dispose();
            _decisionSub?.Dispose();
        }
    }
}
