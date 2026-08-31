using MessagePipe;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    // Shared entry point for "a decision was made for the current world
    // event" - used by both DecisionLogger (decisions arriving from the
    // bridge over MessagePipe.Interprocess) and EventPopupPresenter
    // (decisions made directly by clicking a button in the in-game UI).
    //
    // Exists specifically so in-process UI code calls this directly instead
    // of round-tripping through MessagePipe.Interprocess pub/sub, which is
    // for cross-process communication only and isn't listened to by
    // anything for purely in-process calls.
    public class DecisionExecutor
    {
        private readonly ISectStateProvider _stateProvider;
        private readonly TimeSystem _timeSystem;
        private readonly IPublisher<DecisionExecutedMessage> _decisionExecutedPublisher;

        public DecisionExecutor(
            ISectStateProvider stateProvider,
            TimeSystem timeSystem,
            IPublisher<DecisionExecutedMessage> decisionExecutedPublisher)
        {
            _stateProvider = stateProvider;
            _timeSystem = timeSystem;
            _decisionExecutedPublisher = decisionExecutedPublisher;
        }

        public void Execute(string eventId, string choiceId)
        {
            _stateProvider.ApplyDecisionConsequence(eventId, choiceId);
            _timeSystem.SetPaused(false);

            // Both DecisionLogger (bridge) and EventPopupPresenter (UI)
            // funnel through here, so anything that wants to react to "a
            // decision just happened" - like the log window - only needs
            // to listen here once, regardless of where the decision came from.
            _decisionExecutedPublisher.Publish(new DecisionExecutedMessage { EventId = eventId, ChoiceId = choiceId });
        }
    }
}
