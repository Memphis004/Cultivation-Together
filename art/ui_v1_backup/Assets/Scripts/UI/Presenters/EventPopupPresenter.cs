using System.Collections.Generic;
using System.Linq;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.UI
{
    public class EventPopupOpenArgs
    {
        public string EventId;
        public string Description;
        public List<EventChoiceInfo> Choices;
    }

    public class EventPopupPresenter : UIPresenter<EventPopupView>
    {
        // Fixed from the original spec draft: this must call DecisionExecutor
        // directly (in-process), not publish through IPublisher<ExecuteDecisionMessage>.
        // DecisionLogger only listens on IDistributedSubscriber<string,
        // ExecuteDecisionMessage> (the interprocess/bridge channel) - a plain
        // in-memory IPublisher<ExecuteDecisionMessage> publish would go to a
        // completely different, unlistened channel and silently do nothing.
        private readonly DecisionExecutor _decisionExecutor;

        private string _currentEventId;

        public EventPopupPresenter(DecisionExecutor decisionExecutor)
        {
            _decisionExecutor = decisionExecutor;
        }

        protected override void OnViewBound()
        {
            View.ChoiceClicked += OnChoiceClicked;
        }

        public override void OnOpen(object args)
        {
            var openArgs = args as EventPopupOpenArgs;
            if (openArgs == null) return;

            _currentEventId = openArgs.EventId;
            View.SetEvent(openArgs.EventId, openArgs.Description);

            var choices = (openArgs.Choices ?? new List<EventChoiceInfo>())
                .Select(c => new EventChoiceViewData { ChoiceId = c.ChoiceId, Label = c.Label })
                .ToList();
            View.SetChoices(choices);
        }

        private void OnChoiceClicked(string choiceId)
        {
            _decisionExecutor.Execute(_currentEventId, choiceId);
            View.Hide();
        }

        public override void Dispose()
        {
            View.ChoiceClicked -= OnChoiceClicked;
        }
    }
}
