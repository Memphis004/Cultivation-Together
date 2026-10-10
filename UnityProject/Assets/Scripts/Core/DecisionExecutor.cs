using MessagePipe;
using UnityEngine;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    /// <summary>
    /// E2-lite — outcome of a decision attempt. The in-game UI path reads it to
    /// decide whether to close the popup; the bridge path is fire-and-forget and
    /// ignores it (its rejections only show up in the Unity Console).
    /// </summary>
    public readonly struct DecisionResult
    {
        public bool Accepted { get; }
        public string Reason { get; }

        private DecisionResult(bool accepted, string reason)
        {
            Accepted = accepted;
            Reason = reason;
        }

        public static DecisionResult Accept() => new DecisionResult(true, null);
        public static DecisionResult Reject(string reason) => new DecisionResult(false, reason);
    }

    // Shared entry point for "a decision was made for the current world
    // event" - used by both DecisionLogger (decisions arriving from the
    // bridge over MessagePipe.Interprocess) and EventPopupPresenter
    // (decisions made directly by clicking a button in the in-game UI).
    //
    // Exists specifically so in-process UI code calls this directly instead
    // of round-tripping through MessagePipe.Interprocess pub/sub, which is
    // for cross-process communication only and isn't listened to by
    // anything for purely in-process calls.
    //
    // E2-lite: this is the ONE production path a decision can take. It validates
    // against TimeSystem's authoritative pending state before touching anything,
    // so a duplicate, stale, unknown or invalid-choice decision changes nothing -
    // there is no unvalidated bypass left for production code.
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

        /// <summary>
        /// Applies a decision to the currently pending world event, at most once.
        /// Valid only when a decision is pending, <paramref name="eventId"/> equals
        /// the pending event id, and <paramref name="choiceId"/> is one of the pending
        /// choices; otherwise nothing changes and the rejection (with its reason) is
        /// logged as a warning. After a valid decision: apply the consequence, clear
        /// the pending state, and resume <see cref="TimePauseReason.PendingDecision"/>
        /// ONLY (a player pause survives).
        /// </summary>
        public DecisionResult Execute(string eventId, string choiceId)
        {
            // Single validation path (shared by the UI click and the bridge path).
            // Clearing the pending state on success is what makes a duplicate a
            // rejection: the second call finds nothing pending.
            if (!_timeSystem.TryResolvePendingDecision(eventId, choiceId, out string reason))
            {
                Debug.LogWarning(
                    $"[DecisionExecutor] Rejected decision - eventId={eventId} choiceId={choiceId}: {reason}");
                return DecisionResult.Reject(reason);
            }

            _stateProvider.ApplyDecisionConsequence(eventId, choiceId);
            // E1 — resolving a decision clears ONLY the PendingDecision reason; a
            // player pause (User) is preserved.
            _timeSystem.Resume(TimePauseReason.PendingDecision);

            // Both DecisionLogger (bridge) and EventPopupPresenter (UI)
            // funnel through here, so anything that wants to react to "a
            // decision just happened" - like the log window - only needs
            // to listen here once, regardless of where the decision came from.
            _decisionExecutedPublisher.Publish(new DecisionExecutedMessage { EventId = eventId, ChoiceId = choiceId });
            return DecisionResult.Accept();
        }
    }
}
