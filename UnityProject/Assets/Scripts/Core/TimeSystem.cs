using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using UnityEngine;
using VContainer.Unity;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    // Example subsystem wired through the bus instead of holding direct
    // references to other systems. Compare to the earlier plain GameManager
    // sketch - the difference is TimeSystem doesn't know DiscipleSystem or
    // BuildingSystem exist; it just publishes, and whoever cares subscribes.
    //
    // NOTE (P10 simulation time): TimeSystem deliberately does NOT implement ITickable.
    // Simulation time is derived on demand by SimulationDelta, and game speed is applied
    // ONLY there — there is intentionally no per-frame hook a second speed multiply could
    // be added to later.
    /// <summary>
    /// E1 — explicit pause owners. A flags set, not one boolean: a decision pause
    /// can never be mistaken for a player pause, and clearing one never clears the
    /// other.
    ///
    /// P13A defines <see cref="SessionTransition"/> (held by
    /// <see cref="GameSessionCoordinator"/> for the duration of a transition), so the
    /// event pause releasing can never resume a transition that is still in flight.
    /// </summary>
    [System.Flags]
    public enum TimePauseReason
    {
        None = 0,
        /// <summary>The player paused (TimeControl HUD / Space hotkey).</summary>
        User = 1,
        /// <summary>A decision-requiring world event is outstanding.</summary>
        PendingDecision = 2,
        /// <summary>P13A — a session transition is in flight (GameSessionCoordinator owns this one).</summary>
        SessionTransition = 4,
    }

    /// <summary>
    /// P13A — ONE outstanding, undecided world event.
    ///
    /// <see cref="InstanceId"/> identifies this OCCURRENCE; <see cref="EventId"/> is the
    /// authored definition it was raised from, and many instances can share one definition.
    /// A decision is addressed to an instance, never to a definition, so a replayed, stale
    /// or duplicate decision can never resolve a different occurrence of the same event.
    /// Instances are only ever created by <see cref="TimeSystem.RaiseWorldEvent"/> (or by a
    /// restore, which re-mints one) and are removed only by resolving or clearing them.
    /// </summary>
    public sealed class PendingWorldEvent
    {
        public PendingWorldEvent(string instanceId, string eventId, string description, List<EventChoiceInfo> choices)
        {
            InstanceId = instanceId;
            EventId = eventId;
            Description = description;
            Choices = choices ?? new List<EventChoiceInfo>();
        }

        /// <summary>Unique per raised occurrence (never reused), e.g. <c>evt_000001</c>.</summary>
        public string InstanceId { get; }

        /// <summary>The authored event definition id (<c>cfg.game.EventDef.Id</c>).</summary>
        public string EventId { get; }

        /// <summary>Authoring text shown to the player / the AI GM.</summary>
        public string Description { get; }

        /// <summary>The choices this occurrence offers (never null).</summary>
        public IReadOnlyList<EventChoiceInfo> Choices { get; }

        /// <summary>
        /// Exact choice-id match against THIS occurrence's choices — never a substring or
        /// prefix test, and never a lookup into the definition's authored rows, so a choice
        /// that was not offered here can never be accepted.
        /// </summary>
        public bool HasChoice(string choiceId)
        {
            for (int i = 0; i < Choices.Count; i++)
            {
                var choice = Choices[i];
                if (choice != null && choice.ChoiceId == choiceId) return true;
            }
            return false;
        }

        public override string ToString() => $"{InstanceId}({EventId})";
    }

    public class TimeSystem : IStartable
    {
        private readonly IPublisher<TimeSpeedChangedMessage> _speedPublisher;
        private readonly IPublisher<WorldEventTriggeredMessage> _worldEventPublisher;
        private readonly TimeRuntimeConfig _config;

        /// <summary>Lowest supported simulation speed (1x).</summary>
        public const int MinSpeed = 1;
        /// <summary>Highest supported simulation speed (3x).</summary>
        public const int MaxSpeed = 3;

        // Initialised to 1x (SetSpeed only publishes the UI-facing change message).
        private int _speed = 1;
        private TimePauseReason _reasons = TimePauseReason.None;

        // P12A — whether a live gameplay session is active. Deliberately a SEPARATE fact
        // from the pause reasons: "no active game" (Title / session transition) is not a
        // player pause and not a pending-decision pause, so SimulationDelta freezes for
        // either, while IsPaused / IsUserPaused / IsPendingDecisionPaused stay honest.
        // Defaults to active so a TimeSystem built outside the session coordinator
        // (tests / legacy construction) keeps its previous behaviour.
        private bool _sessionActive = true;

        // Completed (and replaced with a fresh one) every time a world
        // event fires - see WaitForNextWorldEventAsync()/RaiseWorldEvent().
        private UniTaskCompletionSource<AwaitWorldEventResponse> _pendingEventSource =
            new UniTaskCompletionSource<AwaitWorldEventResponse>();

        // If a decision-requiring event fired before anyone called
        // await_next_world_event, hand it back immediately on the next call
        // instead of making a late caller wait for a completely new event.
        // Cleared once handed out - a second call with nothing new pending
        // goes back to waiting normally.
        //
        // NOTE (E2-lite): this cache is the BRIDGE's handoff and its semantics are
        // deliberately unchanged (cleared on read). The authoritative pending
        // decision below is a SEPARATE concern and is never cleared by a read.
        //
        // P13A: this stays a single slot ON PURPOSE. It is "the newest event to hand a
        // late awaiter", not pending-state storage — that is _pendingDecisions below,
        // which is bounded and whose entries are never overwritten.
        private AwaitWorldEventResponse _cachedPendingEvent;

        /// <summary>
        /// P13A — how many undecided events may be outstanding at once. The bound is
        /// deliberate: the pending state is authoritative and must not grow without limit.
        /// At the cap a further decision event is REFUSED, loudly (see
        /// <see cref="RaiseWorldEvent"/>); an already-pending decision is never evicted,
        /// replaced or overwritten to make room for a newer one.
        /// </summary>
        public const int MaxPendingDecisions = 8;

        // P13A — authoritative pending decisions in ARRIVAL ORDER (oldest first). The head
        // (index 0) is the CURRENT event: it is what the UI opens, what the bridge handoff
        // cache carries, and what a snapshot captures. Any pending instance may be resolved
        // by its own instance id, which removes exactly that one.
        //
        // This replaces E2-lite's single-slot fields, which silently overwrote an
        // outstanding decision as soon as a second one was raised.
        private readonly List<PendingWorldEvent> _pendingDecisions = new List<PendingWorldEvent>();
        private int _nextEventInstanceNumber = 1;
        private static readonly List<EventChoiceInfo> NoChoices = new List<EventChoiceInfo>();

        public TimeSystem(
            IPublisher<TimeSpeedChangedMessage> speedPublisher,
            IPublisher<WorldEventTriggeredMessage> worldEventPublisher,
            TimeRuntimeConfig config = null)
        {
            _speedPublisher = speedPublisher;
            _worldEventPublisher = worldEventPublisher;
            _config = config ?? TimeRuntimeConfig.Instance;
        }

        public void Start()
        {
            // Unconditional initial publish so any listener that subscribes after
            // Start still gets one refresh cue (the HUD also reads state on bind).
            PublishState();
        }

        /// <summary>Adds a pause reason (idempotent). Publishes only on a real change.</summary>
        public void Pause(TimePauseReason reason) => SetReason(reason, true);

        /// <summary>Clears a pause reason. Never touches any other reason.</summary>
        public void Resume(TimePauseReason reason) => SetReason(reason, false);

        /// <summary>
        /// Player "Play": clears BOTH User and PendingDecision. This is what the HUD
        /// Play button / Space hotkey call, so the player can never be locked out by a
        /// missing decision popup.
        /// </summary>
        public void ResumeByPlayer() => SetReasons(TimePauseReason.None);

        /// <summary>Legacy single-flag API — maps to the User reason (not a decision pause).</summary>
        public void SetPaused(bool paused)
        {
            if (paused) Pause(TimePauseReason.User);
            else Resume(TimePauseReason.User);
        }

        /// <summary>
        /// P12A — toggles whether a live gameplay session is active (set by
        /// <see cref="GameSessionCoordinator"/> on every phase change). Publishing on the
        /// effective change keeps any speed/pause listener refreshed without a new bus.
        /// </summary>
        public void SetSessionActive(bool active)
        {
            if (_sessionActive == active) return;
            _sessionActive = active;
            PublishState();
        }

        /// <summary>
        /// P12A — end the current session's simulation clock exactly once at the seam
        /// between two sessions: freeze simulation (session inactive), clear every pause
        /// reason (a fresh session starts unpaused), clear the authoritative pending
        /// decision, and complete any outstanding world-event awaiter with a defined
        /// "session ended" response. Real-time (UTC) viewer/cooldown clocks are
        /// independent of this class and are not affected.
        /// </summary>
        public void EndSession(string reason)
        {
            SetSessionActive(false);
            ClearPendingDecisions();

            // P13A — clear exactly the reasons a session boundary OWNS: the player pause
            // (a fresh session starts unpaused) and the decision pause (its events are
            // gone). A SessionTransition pause is PRESERVED: the transition that holds it
            // is the very caller of this method and is still in flight, so ending the
            // session must not resume a transition that has not finished.
            SetReasons(_reasons & TimePauseReason.SessionTransition);
            CancelPendingWorldEventWait(reason);
        }

        /// <summary>
        /// P12A — never retain a stale cached world-event response from a prior session,
        /// and never strand an existing awaiter: the handoff cache is dropped and the
        /// current wait is completed with an empty event id (the response contract's
        /// clearest "no event" answer) plus an explicit reason.
        /// </summary>
        public void CancelPendingWorldEventWait(string reason)
        {
            _cachedPendingEvent = null;

            var previous = _pendingEventSource;
            _pendingEventSource = new UniTaskCompletionSource<AwaitWorldEventResponse>();
            previous.TrySetResult(new AwaitWorldEventResponse
            {
                EventId = string.Empty,
                Description = string.IsNullOrEmpty(reason)
                    ? "session ended before a world event fired"
                    : "session ended before a world event fired: " + reason,
            });
        }

        /// <summary>
        /// P13A — the SINGLE decision-validation path, reused by the in-game UI click
        /// (EventPopupPresenter) and by the bridge decision (DecisionLogger ->
        /// DecisionExecutor). A decision is valid only when a decision is pending, it names
        /// that pending INSTANCE, <paramref name="eventId"/> agrees with the instance's
        /// definition, and <paramref name="choiceId"/> is one of the choices THAT INSTANCE
        /// offered (exact id match). On success exactly that instance is removed, so a
        /// duplicate / replayed / stale / unknown decision can never be applied twice. On any
        /// failure nothing changes (not even the current event), false is returned, and
        /// <paramref name="reason"/> says why.
        /// </summary>
        /// <param name="instanceId">
        /// The target occurrence. Null/empty is accepted for a legacy caller that only knows
        /// the definition id: the decision then applies only when EXACTLY ONE pending instance
        /// has that definition id. An ambiguous match is refused rather than guessed at, so a
        /// bare event id can never resolve the wrong occurrence of a repeated event.
        /// </param>
        public bool TryResolvePendingDecision(string instanceId, string eventId, string choiceId, out string reason)
        {
            if (!HasPendingDecision)
            {
                // P13A — a duplicate/replayed decision lands here once its occurrence is gone, so
                // the rejection still names what was asked for instead of a bare "nothing pending".
                reason = string.IsNullOrEmpty(instanceId)
                    ? "no decision event is pending"
                    : $"instanceId '{instanceId}' is not a pending decision (none is pending)";
                return false;
            }

            PendingWorldEvent target;
            if (!string.IsNullOrEmpty(instanceId))
            {
                if (!TryGetPendingDecision(instanceId, out target))
                {
                    reason = $"instanceId '{instanceId}' is not a pending decision";
                    return false;
                }
                if (eventId != target.EventId)
                {
                    reason = $"instanceId '{instanceId}' is event '{target.EventId}', not '{eventId}'";
                    return false;
                }
            }
            else
            {
                target = null;
                int matches = 0;
                for (int i = 0; i < _pendingDecisions.Count; i++)
                {
                    if (_pendingDecisions[i].EventId != eventId) continue;
                    target = _pendingDecisions[i];
                    matches++;
                }
                if (matches == 0)
                {
                    reason = $"eventId '{eventId}' is not a pending event";
                    return false;
                }
                if (matches > 1)
                {
                    reason = $"eventId '{eventId}' matches {matches} pending instances - " +
                             "a decision must name the instance id";
                    return false;
                }
            }

            if (!target.HasChoice(choiceId))
            {
                reason = $"choiceId '{choiceId}' is not one of the choices of {target}";
                return false;
            }

            _pendingDecisions.Remove(target);
            reason = null;
            return true;
        }

        /// <summary>
        /// Legacy two-argument form (definition id only) — see the four-argument overload for
        /// the compatibility rule it follows.
        /// </summary>
        public bool TryResolvePendingDecision(string eventId, string choiceId, out string reason)
            => TryResolvePendingDecision(null, eventId, choiceId, out reason);

        /// <summary>
        /// P13A — drop every outstanding pending decision. Used at a session boundary and by a
        /// restore (which replaces the pending state outright); never by a decision.
        /// </summary>
        public void ClearPendingDecisions() => _pendingDecisions.Clear();

        /// <summary>
        /// P13A — look up one outstanding pending decision by its instance id. Read-only; a
        /// null/empty id never matches (instance ids are never empty).
        /// </summary>
        public bool TryGetPendingDecision(string instanceId, out PendingWorldEvent pending)
        {
            if (!string.IsNullOrEmpty(instanceId))
            {
                for (int i = 0; i < _pendingDecisions.Count; i++)
                {
                    if (_pendingDecisions[i].InstanceId != instanceId) continue;
                    pending = _pendingDecisions[i];
                    return true;
                }
            }

            pending = null;
            return false;
        }

        /// <summary>
        /// P13A — store one newly raised pending decision and mint its instance id. Returns
        /// null when <see cref="MaxPendingDecisions"/> is already reached: the raise is then
        /// refused (logged here) and NOTHING was stored — no pending decision is ever replaced
        /// or evicted, and the caller must not report the event as pending.
        /// </summary>
        private PendingWorldEvent EnqueuePendingDecision(string eventId, string description, List<EventChoiceInfo> choices)
        {
            if (_pendingDecisions.Count >= MaxPendingDecisions)
            {
                Debug.LogWarning(
                    $"[TimeSystem] Refused to queue world event '{eventId}': {_pendingDecisions.Count} decisions are " +
                    $"already outstanding (cap {MaxPendingDecisions}). The oldest pending decision " +
                    $"('{PendingInstanceId}') is kept - an outstanding decision is never overwritten to make room.");
                return null;
            }

            var pending = new PendingWorldEvent($"evt_{_nextEventInstanceNumber++:000000}", eventId, description, choices);
            _pendingDecisions.Add(pending);
            return pending;
        }

        /// <summary>
        /// P11A — restore simulation state from a validated session snapshot, on the
        /// Unity main thread. Sets the (pre-validated) speed, then re-establishes or
        /// clears the authoritative pending decision. A restored pending decision is
        /// re-published as a world-event message so the UI/AI can react; the bridge
        /// handoff cache is deliberately NOT restored (it is consumed-on-read
        /// transport state — see SectSessionSnapshot.cs classification). Nothing here
        /// touches gameplay state; SectStateProvider owns that half.
        /// </summary>
        public void RestoreSimulationState(int speed, string pendingEventId, string pendingDescription,
                                           List<EventChoiceInfo> pendingChoices, bool pendingDecisionPaused)
        {
            SetSpeed(speed);

            // P13A — a restore replaces the pending state outright (it describes the
            // session being loaded), so whatever the session being left had queued goes first.
            ClearPendingDecisions();

            if (string.IsNullOrEmpty(pendingEventId))
            {
                Resume(TimePauseReason.PendingDecision); // a fresh session is not decision-paused
                return;
            }

            // P13A — the restored decision is stored under a FRESH instance id. Instance ids
            // identify a LIVE occurrence and are deliberately not persisted (see the reset
            // list in SectSessionSnapshot.cs): a decision addressed to a pre-save instance is
            // stale after a load and is refused, while the restored event itself stays fully
            // answerable under its new id.
            var restored = EnqueuePendingDecision(pendingEventId, pendingDescription,
                                                  pendingChoices != null ? new List<EventChoiceInfo>(pendingChoices) : null);
            if (restored == null) return; // capped state cannot occur from a validated snapshot

            // The snapshot's pause flag is authoritative: a restore that says "not paused"
            // must not leave the clock frozen on a pause whose event no longer exists.
            if (pendingDecisionPaused) Pause(TimePauseReason.PendingDecision);
            else Resume(TimePauseReason.PendingDecision);

            _worldEventPublisher.Publish(new WorldEventTriggeredMessage
            {
                EventId = restored.EventId,
                InstanceId = restored.InstanceId,
                RequiresDecision = true,
                Description = restored.Description,
                Choices = new List<EventChoiceInfo>(restored.Choices),
            });
        }

        /// <summary>Sets the simulation speed, validated to <see cref="MinSpeed"/>..<see cref="MaxSpeed"/>.</summary>
        public void SetSpeed(int speed)
        {
            int clamped = ClampSpeed(speed);
            if (clamped == _speed) return; // effective change only
            _speed = clamped;
            PublishState();
        }

        /// <summary>True while ANY pause reason is active.</summary>
        public bool IsPaused => _reasons != TimePauseReason.None;

        /// <summary>
        /// P12A — true while a live gameplay session is active. When false (Title or a
        /// session transition) <see cref="SimulationDelta"/> is exactly 0, but that is NOT
        /// reported as a pause (see <see cref="IsPaused"/>). UTC/viewer clocks are
        /// unaffected — they never read simulation time.
        /// </summary>
        public bool IsSessionActive => _sessionActive;

        /// <summary>True when the player has paused (User reason).</summary>
        public bool IsUserPaused => HasReason(TimePauseReason.User);

        /// <summary>True when a decision-requiring event is outstanding (the pause reason).</summary>
        public bool IsPendingDecisionPaused => HasReason(TimePauseReason.PendingDecision);

        // ---------- P13A: authoritative pending-decision state ----------
        // Separate from IsPendingDecisionPaused: the player can clear the pause
        // (ResumeByPlayer) while the event is still pending and undecided.

        /// <summary>True while at least one decision-requiring event is outstanding and undecided.</summary>
        public bool HasPendingDecision => _pendingDecisions.Count > 0;

        /// <summary>How many decisions are outstanding right now (0..<see cref="MaxPendingDecisions"/>).</summary>
        public int PendingDecisionCount => _pendingDecisions.Count;

        /// <summary>
        /// The CURRENT pending decision — the OLDEST undecided one — or null when none. This is
        /// the single ordering every consumer reads (UI, bridge handoff, snapshot), so "which
        /// event am I looking at" can never differ between them.
        /// </summary>
        public PendingWorldEvent PendingDecision =>
            _pendingDecisions.Count > 0 ? _pendingDecisions[0] : null;

        /// <summary>Instance id of the current pending decision, or null when none.</summary>
        public string PendingInstanceId => PendingDecision?.InstanceId;

        /// <summary>Authored definition id of the current pending decision, or null when none.</summary>
        public string PendingEventId => PendingDecision?.EventId;

        /// <summary>Description of the current pending decision, or null when none.</summary>
        public string PendingDescription => PendingDecision?.Description;

        /// <summary>Choices of the current pending decision (never null; empty when none).</summary>
        public IReadOnlyList<EventChoiceInfo> PendingChoices =>
            PendingDecision?.Choices ?? (IReadOnlyList<EventChoiceInfo>)NoChoices;

        /// <summary>Current (validated) simulation speed.</summary>
        public int Speed => _speed;

        private bool HasReason(TimePauseReason reason) => (_reasons & reason) == reason;

        private void SetReason(TimePauseReason reason, bool active)
        {
            if (reason == TimePauseReason.None) return;
            SetReasons(active ? (_reasons | reason) : (_reasons & ~reason));
        }

        private void SetReasons(TimePauseReason next)
        {
            if (next == _reasons) return; // no effective change - no message
            _reasons = next;
            PublishState();
        }

        // Refresh trigger only (Paused = IsPaused). The HUD reads reasons/speed
        // directly from TimeSystem; it never infers them from this message.
        private void PublishState()
        {
            _speedPublisher.Publish(new TimeSpeedChangedMessage { Speed = _speed, Paused = IsPaused });
        }

        /// <summary>
        /// Simulation delta for gameplay progression, in seconds — the ONLY place game
        /// speed is applied. Returns 0 while paused, so a paused game freezes every
        /// consumer without each one having to re-implement the pause rule.
        /// <para>Main thread only: reads <see cref="UnityEngine.Time.deltaTime"/>.</para>
        /// </summary>
        /// <remarks>
        /// Time.timeScale is deliberately never written by this class: it is already
        /// folded into Time.deltaTime, so multiplying by speed here as well would apply
        /// game speed twice. Speed below <see cref="MinSpeed"/> is clamped up to 1x and
        /// above <see cref="MaxSpeed"/> down to 3x, so a stale/out-of-range speed (or a
        /// forgotten SetSpeed) can never yield a zero or unbounded delta while unpaused.
        /// </remarks>
        public float SimulationDelta
        {
            // P12A — gameplay progression requires BOTH an unpaused clock and an active
            // Playing session. The two are independent: a stale "no session" state can
            // never be mistaken for, or clear, a player/decision pause.
            get { return ComputeSimulationDelta(IsPaused || !_sessionActive, _speed, Time.deltaTime); }
        }

        /// <summary>
        /// Pure core of <see cref="SimulationDelta"/> — the single speed application,
        /// split out so the multiplier can be asserted without a rendered frame.
        /// </summary>
        public static float ComputeSimulationDelta(bool paused, int speed, float frameDelta)
        {
            if (paused) return 0f;
            return ClampSpeed(speed) * frameDelta;
        }

        private static int ClampSpeed(int speed)
        {
            if (speed < MinSpeed) return MinSpeed;
            if (speed > MaxSpeed) return MaxSpeed;
            return speed;
        }

        // Resolved by AwaitWorldEventHandler - the bridge's await_next_world_event
        // tool call blocks on this until the next RaiseWorldEvent(), unless
        // there's already a cached one waiting (see _cachedPendingEvent).
        //
        // Remember: while a pause reason is active (e.g. a decision-requiring
        // event is outstanding), RaiseWorldEvent never fires again -
        // WorldEventSystem checks IsPaused and skips. Resolve the decision (or
        // ResumeByPlayer) first, or this will time out waiting for an event that
        // can't happen yet.
        public UniTask<AwaitWorldEventResponse> WaitForNextWorldEventAsync()
        {
            // P12A — outside a Playing session there is no event stream to wait on, so a
            // new awaiter is answered immediately with a clear "no active session" result
            // instead of being parked until some future session fires an event.
            if (!_sessionActive)
            {
                return UniTask.FromResult(new AwaitWorldEventResponse
                {
                    EventId = string.Empty,
                    Description = "no active session: gameplay is not in a Playing state",
                });
            }

            if (_cachedPendingEvent != null)
            {
                Debug.Log("[TimeSystem] Returning cached world event immediately.");
                var cached = _cachedPendingEvent;
                _cachedPendingEvent = null;
                return UniTask.FromResult(cached);
            }

            return _pendingEventSource.Task;
        }

        // Called by whatever system decides a world event fired (new
        // applicant, monster incursion, ...). requiresDecision auto-pauses -
        // this is the "checkpoint" the AI GM / vote window waits on, and
        // stays paused until execute_decision is called.
        //
        // Not sent over the interprocess bus as pub/sub (see the comment on
        // AwaitWorldEventRequest in GameMessages.cs for why) - completing
        // _pendingEventSource is what actually delivers this to the bridge,
        // via the request-response AwaitWorldEventHandler below. The
        // in-memory Publish() call is just for any other in-Unity listener.
        //
        // P13A: a decision-requiring event is stored as its OWN pending instance,
        // queued behind anything already outstanding and identified by a unique id.
        /// <returns>
        /// False only when a decision-requiring event could not be stored because
        /// <see cref="MaxPendingDecisions"/> is reached: nothing is pending for it then, no
        /// message is published, no awaiter is completed, and no outstanding decision was
        /// touched. Always true for a non-decision event.
        /// </returns>
        public bool RaiseWorldEvent(string eventId, string description, bool requiresDecision, List<EventChoiceInfo> choices)
        {
            Debug.Log($"[TimeSystem] World event raised: {eventId} (requiresDecision={requiresDecision})");

            var effectiveChoices = choices ?? new List<EventChoiceInfo>();
            string instanceId = null;

            if (requiresDecision)
            {
                bool alreadyPending = HasPendingDecision;

                var pending = EnqueuePendingDecision(eventId, description, effectiveChoices);
                if (pending == null) return false; // cap reached - nothing was stored, nothing changed

                instanceId = pending.InstanceId;
                effectiveChoices = new List<EventChoiceInfo>(pending.Choices);

                // E2-lite/P13A: the pause reason is added only for the event that turns an
                // EMPTY pending state into a non-empty one - if any decision is still
                // outstanding (e.g. the player pressed Play but has not decided yet),
                // raising further events must NOT re-add the pause...
                if (!alreadyPending && _config.AutoPauseOnDecisionEvent)
                    Pause(TimePauseReason.PendingDecision);
                // ...and must never clear it either: another instance is still undecided.
            }

            _worldEventPublisher.Publish(new WorldEventTriggeredMessage
            {
                EventId = eventId,
                InstanceId = instanceId,
                RequiresDecision = requiresDecision,
                Description = description,
                Choices = effectiveChoices,
            });

            var response = new AwaitWorldEventResponse
            {
                EventId = eventId,
                InstanceId = instanceId,
                RequiresDecision = requiresDecision,
                Description = description,
                Choices = effectiveChoices,
            };

            if (requiresDecision)
            {
                _cachedPendingEvent = response;
            }

            var previous = _pendingEventSource;
            _pendingEventSource = new UniTaskCompletionSource<AwaitWorldEventResponse>();
            previous.TrySetResult(response);
            return true;
        }
    }

    // Answers AwaitWorldEventRequest coming in over the interprocess bus.
    // Request-response, not pub/sub - see the comment on AwaitWorldEventRequest
    // in GameMessages.cs for why.
    //
    // THREADING (T1): InvokeAsync runs on the MessagePipe.Interprocess TCP background
    // thread, and both things it touches - the one-slot cache (_cachedPendingEvent) and
    // the UniTaskCompletionSource - are main-thread state. It therefore hops FIRST and
    // then runs the existing wait unchanged: a cached event is consumed on the main
    // thread, and a genuinely pending wait is only resumed when RaiseWorldEvent
    // completes the source on the main thread. Awaiting never blocks the main thread.
    public class AwaitWorldEventHandler : IAsyncRequestHandler<AwaitWorldEventRequest, AwaitWorldEventResponse>
    {
        private readonly TimeSystem _timeSystem;

        public AwaitWorldEventHandler(TimeSystem timeSystem)
        {
            _timeSystem = timeSystem;
        }

        public UniTask<AwaitWorldEventResponse> InvokeAsync(AwaitWorldEventRequest request, CancellationToken cancellationToken = default)
        {
            return MainThreadDispatch.RunAwaitingAsync(
                nameof(AwaitWorldEventHandler),
                $"requestId={request?.RequestId}",
                WaitOnMainThreadAsync,
                reason => new AwaitWorldEventResponse
                {
                    // The response contract has no failure field: an empty event id is the
                    // clearest available answer (the reason is in the Console log line).
                    EventId = string.Empty,
                    Description = "unavailable: " + reason,
                });
        }

        /// <summary>
        /// The existing wait, unchanged, entered on the main thread: cache check first, then the
        /// pending completion source. Kept as its own method so the kept-open await is explicit.
        /// </summary>
        private async UniTask<AwaitWorldEventResponse> WaitOnMainThreadAsync()
        {
            var waitForEvent = _timeSystem.WaitForNextWorldEventAsync();
            return await waitForEvent;
        }
    }

    // Answers SectStateQuery requests coming in over the interprocess bus
    // from the MCP bridge. Aggregates whatever the subsystems currently hold
    // into the SectEconomyState shape from economy.proto.
    //
    // THREADING (T1): InvokeAsync runs on the MessagePipe.Interprocess TCP background
    // thread, and BuildSectEconomyState() enumerates every live disciple/resource while
    // the main thread keeps ticking (ToByteArray() then serializes that graph). That is
    // a read race, so the snapshot is produced after a hop to the Unity main thread.
    public class SectStateQueryHandler : IAsyncRequestHandler<SectStateQuery, SectStateSnapshot>
    {
        private readonly ISectStateProvider _stateProvider;

        public SectStateQueryHandler(ISectStateProvider stateProvider)
        {
            _stateProvider = stateProvider;
        }

        public UniTask<SectStateSnapshot> InvokeAsync(SectStateQuery request, CancellationToken cancellationToken = default)
        {
            return MainThreadDispatch.RunAsync(
                nameof(SectStateQueryHandler),
                $"requestId={request?.RequestId}",
                () =>
                {
                    var state = _stateProvider.BuildSectEconomyState();
                    return new SectStateSnapshot
                    {
                        RequestId = request.RequestId,
                        // MessagePack, committed choice (not a protobuf stub
                        // anymore - see project_summary.md for why).
                        EconomyStateBytes = state.ToByteArray()
                    };
                },
                reason => new SectStateSnapshot
                {
                    // The snapshot contract has no failure field: no bytes means "no state"
                    // (the bridge's decode fails loudly) and the reason is in the log line.
                    RequestId = request?.RequestId,
                    EconomyStateBytes = null
                });
        }
    }

    // ---------- P5B (Hybrid Permissions) — read-only permission contract ----------
    // The UI must never infer permission from OwnerType != Npc on its own; it asks
    // the authority (SectStateProvider) and renders the answer. Same evaluation the
    // mutation path revalidates, so display and commit can never disagree.

    /// <summary>Three-way outcome of a task permission evaluation.</summary>
    public enum TaskPermissionOutcome
    {
        /// <summary>Requester may control this disciple right now.</summary>
        Allowed = 0,
        /// <summary>Requester is a known identity that simply does not have permission.</summary>
        Denied = 1,
        /// <summary>Membership/ownership data is missing or contradictory — fail closed, NEVER permission.</summary>
        ConsistencyError = 2,
    }

    /// <summary>
    /// P5B — read-only result of a task permission evaluation. Never mutates state;
    /// the mutation path (TryAssignTask) revalidates the same rules before writing.
    /// </summary>
    public sealed class TaskPermissionResult
    {
        public TaskPermissionOutcome Outcome { get; private set; }
        public string Reason { get; private set; } = string.Empty;

        /// <summary>True when the requested task already is the disciple's current task (a valid no-op).</summary>
        public bool IsNoOp { get; private set; }

        /// <summary>True when a viewer owner is inside the protection window (SectMaster override not yet allowed).</summary>
        public bool OwnerProtected { get; private set; }

        /// <summary>Seconds left in the owner protection window (0 unless OwnerProtected).</summary>
        public float OwnerProtectionRemainingSeconds { get; private set; }

        public bool Allowed => Outcome == TaskPermissionOutcome.Allowed;

        public static TaskPermissionResult Allow()
            => new TaskPermissionResult { Outcome = TaskPermissionOutcome.Allowed };

        public static TaskPermissionResult Denied(string reason)
            => new TaskPermissionResult { Outcome = TaskPermissionOutcome.Denied, Reason = reason ?? string.Empty };

        public static TaskPermissionResult ConsistencyError(string reason)
            => new TaskPermissionResult { Outcome = TaskPermissionOutcome.ConsistencyError, Reason = reason ?? string.Empty };

        internal TaskPermissionResult WithNoOp(bool isNoOp) { IsNoOp = isNoOp; return this; }

        internal TaskPermissionResult WithProtection(float remainingSeconds)
        {
            OwnerProtected = true;
            OwnerProtectionRemainingSeconds = remainingSeconds < 0f ? 0f : remainingSeconds;
            return this;
        }
    }

    // Thin seam so SectStateQueryHandler doesn't need to know about every
    // subsystem directly - implement this on a small aggregator class that
    // does hold references (it's allowed to, it's not part of the bus).
    public interface ISectStateProvider
    {
        SectEconomyState BuildSectEconomyState();
        void ApplyDecisionConsequence(string eventId, string choiceId);
        void TickGathering(float deltaTimeSeconds);
        void TickCrafting(float deltaTimeSeconds);
        void RecruitOuterDisciple(DiscipleSex sex = DiscipleSex.Unspecified);
        PurchaseItemResponse TryPurchaseItem(string discipleId, string itemDefId, int grade, int quantity);
        bool TryChangeAvatarPart(string discipleId, string slot, string partId,
                                out string failReason, out AvatarAppearance result);

        /// <summary>
        /// Task System v2 (§6) + P5B — assign a task to a disciple after a permission
        /// and validity check. Permission follows CheckTaskPermission (revalidated
        /// here): "SECT_MASTER" may control unowned NPCs and player-controlled
        /// disciples, and may override a Viewer disciple only when the owner has been
        /// inactive for strictly more than 10 real-time minutes; any other requester
        /// only their own valid active membership. Empty/invalid requesters and
        /// missing/conflicting membership data fail closed. A request for the task
        /// already assigned is a no-op — no event, no progress reset (it may refresh
        /// the requester's own activity). Actual task changes honour the configurable
        /// cooldown. Unknown disciple or task fails closed with a reason. On success
        /// sets CurrentTask and publishes DiscipleTaskChangedMessage (in-memory only).
        /// </summary>
        bool TryAssignTask(string requesterId, string discipleId, string taskId, out string failReason);

        /// <summary>
        /// P9A — explicitly set a disciple's Manual/Auto control mode. Ownership and
        /// autonomy are different: this never changes ownership, and only an Npc-owned
        /// disciple is eligible for Auto in this MVP. A Player/Viewer-owned disciple is
        /// rejected even when its owner has been inactive past the protection window —
        /// hybrid inactivity only lets the SectMaster override a task, it does not
        /// authorize a brain. Empty/unknown disciple and undefined enum fail closed;
        /// idempotent (no message when the mode is already set). Publishes
        /// DiscipleControlModeChangedMessage (in-memory only) on a real change.
        /// </summary>
        bool TrySetDiscipleControlMode(string discipleId, DiscipleControlMode mode, out string failReason);

        /// <summary>
        /// P9A — dedicated authoritative entry point for the future DiscipleBrain. NOT
        /// TryAssignTask(SectMasterRequesterId, ...): it takes no requester identity, so
        /// the brain can never impersonate the player or inherit the SectMaster
        /// override. Rechecks NPC ownership AND Auto mode immediately before commit, then
        /// shares TryAssignTask's known-task/building/cooldown validation and mutation
        /// (one implementation). Fails closed with a reason when either eligibility
        /// precondition no longer holds.
        /// </summary>
        bool TryAutoAssignTask(string discipleId, string taskId, out string failReason);

        /// <summary>
        /// P5B (Hybrid Permissions) — read-only permission query for UI. Evaluates the
        /// SAME rules TryAssignTask revalidates on mutation, and never mutates state:
        /// a valid requester may control its own active membership; "SECT_MASTER" may
        /// control unowned NPCs and player-controlled disciples, and may override a
        /// Viewer disciple only when the owner has been inactive for STRICTLY more than
        /// 10 real-time minutes (injected clock). Empty/invalid requesters fail closed;
        /// missing or conflicting membership data returns a ConsistencyError — never
        /// automatic permission.
        /// </summary>
        TaskPermissionResult CheckTaskPermission(string requesterId, string discipleId, string taskId);

        /// <summary>
        /// P5B persistence — the membership slice worth surviving a session: the
        /// viewer registry (status / binding / LastActiveAtUtc) TOGETHER with each
        /// disciple's ownership, so the registry ⇄ ownership invariants hold after a
        /// load. Read-only — the live state is not modified by exporting.
        /// </summary>
        Xianxia.Sect.Messages.SectViewerMembershipSave ExportViewerMembership();

        /// <summary>
        /// P5B persistence — restore a previously exported slice. Fully validated
        /// BEFORE any mutation (version, unknown disciples, registry internal
        /// consistency, and registry ⇄ ownership agreement); anything invalid fails
        /// closed with the mock start state still in place. Ownership and registry are
        /// applied together, so a half-restored state can never exist.
        /// </summary>
        bool TryImportViewerMembership(Xianxia.Sect.Messages.SectViewerMembershipSave save, out string failReason);

        /// <summary>
        /// P11A — capture a detached full-session snapshot of the authoritative
        /// economy + work accumulators (roster/economy/buildings/appearance/
        /// attributes/ownership/membership). Read-only; the returned object never
        /// aliases live state. Simulation speed / pending decision live in TimeSystem
        /// and are merged by SessionSnapshotService.
        /// </summary>
        SectSessionSnapshot CaptureSessionSnapshot();

        /// <summary>
        /// P11A — validate a candidate full-session snapshot BEFORE any live state is
        /// replaced. Fail-closed: invalid data returns false with a reason and leaves
        /// the running session unchanged. Never mutates the candidate or live state.
        /// </summary>
        bool TryValidateSessionSnapshot(SectSessionSnapshot snapshot, out string failReason);

        /// <summary>
        /// P11A — atomically replace live economy + work accumulators from a snapshot
        /// that already passed TryValidateSessionSnapshot. The candidate is deep-cloned
        /// on the way in, so the caller's object never aliases live state.
        /// </summary>
        bool TryApplySessionSnapshot(SectSessionSnapshot snapshot);

        /// <summary>
        /// Task building-requirement gate (§6 addendum). Reads the live
        /// PlacedBuildings list — true when the task is known and either has no
        /// required building or that building def id already exists in state.
        /// Unknown task fails with its usual reason; failClosed — never mutates.
        /// </summary>
        bool IsTaskAvailable(string taskId, out string failReason);

        /// <summary>
        /// P3 (Task Assignment UI) — the known-task set TryAssignTask validates
        /// against, in stable order (gathering, crafting, meditation). The SAME
        /// source of truth as the assignment gate — the UI lists these directly,
        /// so no second task list can drift from what assignment accepts.
        /// Read-only; never mutates.
        /// </summary>
        System.Collections.Generic.IReadOnlyList<string> GetKnownTaskIds();

        /// <summary>
        /// P9B (utility AI) — read-only metadata for a known task, derived from the SAME
        /// gathering/crafting tables the assignment gate validates against
        /// (gathering: produced resource + rate; crafting: produced item + input costs;
        /// otherwise the no-production meditation fallback). False for an unknown/empty
        /// task. Never mutates; no second task-definition pipeline.
        /// </summary>
        bool TryGetTaskInfo(string taskId, out SectTaskInfo info);

        /// <summary>
        /// P4 (local ownership test harness) — dev-only ownership assignment.
        /// NOT exposed as a public viewer command or MCP tool; the intended caller
        /// is the Editor debug harness / test fixtures. Validates fully before any
        /// mutation (fail-closed): disciple exists, enum value defined, Npc
        /// normalizes OwnerId to empty, non-Npc identities satisfy the identity
        /// convention, and a Viewer identity cannot bind to two disciples.
        /// Publishes DiscipleOwnerChangedMessage (in-memory) only on a real change.
        /// P5B: bind/release keeps SectViewerRegistry in lock-step with the disciple
        /// row (active viewer ⇔ matching active record, invariant #1/#2/#3) and
        /// refreshes the owner's LastActiveAtUtc on a successful bind/reclaim.
        /// </summary>
        bool TrySetDiscipleOwner(string discipleId, Xianxia.Sect.DiscipleOwnerType ownerType,
                                 string ownerId, out string failReason);

        /// <summary>
        /// Mutate DiscipleState.ChibiBackend (entitlement) + publish
        /// DiscipleChibiBackendChangedMessage (in-memory). DiscipleVisualSystem respawns
        /// the visual in place on that message — position/activity/facing preserved (§7).
        /// </summary>
        bool TrySetChibiBackend(string discipleId, ChibiBackend backend, out string failReason);

        /// <summary>
        /// Building Phase 1 — place a building on the sect grid (player-only, §8 Q3 default).
        /// Validation order per building-system.md §3.2: def lookup → occupancy → cost,
        /// then all-or-nothing resource deduction through the AdjustAndNotify choke point,
        /// state append, and BuildingPlacedMessage publish (in-memory only).
        /// Caller must call CanAffordBuilding / grid.CanPlace first for ghost preview;
        /// this re-validates everything and fails closed with a reason.
        /// </summary>
        bool TryPlaceBuilding(string defId, int gridX, int gridZ, int rotation,
                              BuildingGrid grid, out string failReason,
                              out PlacedBuildingState placed);

        /// <summary>Ghost preview check: can the sect pay this def's cost right now?
        /// Read-only — no state mutation (occupied-cells check lives on BuildingGrid).</summary>
        bool CanAffordBuilding(BuildingDef def);
    }
}
