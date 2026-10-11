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
    //
    // P13A: a decision targets a specific pending INSTANCE (see
    // TimeSystem.TryResolvePendingDecision). Validating and applying happen on the Unity
    // main thread for both production callers - the in-game UI click (already main thread)
    // and the bridge path (DecisionLogger pumps onto the main thread before calling this);
    // Execute itself stays synchronous and thread-agnostic, so it must only ever be called
    // from one of those two paths.
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
        /// Legacy form: decide <paramref name="eventId"/> without naming an instance. Follows
        /// the compatibility rule of <see cref="TimeSystem.TryResolvePendingDecision"/> —
        /// accepted only when exactly one outstanding instance has that definition id.
        /// </summary>
        public DecisionResult Execute(string eventId, string choiceId)
            => Execute(null, eventId, choiceId);

        /// <summary>
        /// Applies a decision to ONE pending world event instance, at most once. Valid only
        /// when a decision is outstanding, <paramref name="instanceId"/> names it (when
        /// supplied), <paramref name="eventId"/> agrees with that instance's definition, and
        /// <paramref name="choiceId"/> is one of the choices THAT instance offered; otherwise
        /// nothing changes and the rejection (with its reason) is logged as a warning. After a
        /// valid decision: apply the consequence, drop exactly that instance, and resume
        /// <see cref="TimePauseReason.PendingDecision"/> ONLY when no decision is left
        /// outstanding (a player pause and a session-transition pause always survive).
        /// </summary>
        public DecisionResult Execute(string instanceId, string eventId, string choiceId)
        {
            // P12A — a decision is a mutation: outside a Playing session nothing is
            // pending that a decision could legitimately resolve, so refuse clearly.
            // The authoritative "a live session is Playing" fact already lives on the
            // injected simulation clock (GameSessionCoordinator flips it once per session
            // boundary), so no extra dependency is taken on purpose: VContainer always
            // picks the widest constructor and ignores default parameter values, so a new
            // injectable type here would have to be registered by every container that
            // resolves this class (including hand-built test containers).
            if (_timeSystem != null && !_timeSystem.IsSessionActive)
            {
                Debug.LogWarning(
                    $"[DecisionExecutor] Rejected decision — {SessionGate.NoActiveSessionReason} " +
                    $"(eventId={eventId} choiceId={choiceId}).");
                return DecisionResult.Reject(SessionGate.NoActiveSessionReason);
            }

            // Single validation path (shared by the UI click and the bridge path).
            // Removing exactly the named instance on success is what makes a duplicate (or a
            // replay of an id whose occurrence is gone) a rejection: the second call finds no
            // such instance pending.
            if (!_timeSystem.TryResolvePendingDecision(instanceId, eventId, choiceId, out string reason))
            {
                Debug.LogWarning(
                    $"[DecisionExecutor] Rejected decision - instanceId={instanceId} eventId={eventId} " +
                    $"choiceId={choiceId}: {reason}");
                return DecisionResult.Reject(reason);
            }

            _stateProvider.ApplyDecisionConsequence(eventId, choiceId);

            // E1/P13A — resolving a decision clears ONLY the PendingDecision reason, and only
            // once NOTHING is left undecided: while another instance is still outstanding the
            // event pause is still its pause. A player pause (User) and a session-transition
            // pause are never touched here.
            if (!_timeSystem.HasPendingDecision)
                _timeSystem.Resume(TimePauseReason.PendingDecision);

            // Both DecisionLogger (bridge) and EventPopupPresenter (UI)
            // funnel through here, so anything that wants to react to "a
            // decision just happened" - like the log window - only needs
            // to listen here once, regardless of where the decision came from.
            // P13A: InstanceId is carried so a listener can tell which occurrence was decided;
            // it is empty when the caller only knew the definition id (legacy/bridge caller),
            // and such a listener falls back to EventId.
            _decisionExecutedPublisher.Publish(new DecisionExecutedMessage
            {
                InstanceId = instanceId,
                EventId = eventId,
                ChoiceId = choiceId,
            });
            return DecisionResult.Accept();
        }
    }
}
