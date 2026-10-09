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
    public class TimeSystem : IStartable
    {
        private readonly IPublisher<TimeSpeedChangedMessage> _speedPublisher;
        private readonly IPublisher<WorldEventTriggeredMessage> _worldEventPublisher;

        /// <summary>Lowest supported simulation speed (1x).</summary>
        public const int MinSpeed = 1;
        /// <summary>Highest supported simulation speed (3x).</summary>
        public const int MaxSpeed = 3;

        // Initialised to 1x (SetSpeed only publishes the UI-facing change message).
        private int _speed = 1;
        private bool _paused;

        // Completed (and replaced with a fresh one) every time a world
        // event fires - see WaitForNextWorldEventAsync()/RaiseWorldEvent().
        private UniTaskCompletionSource<AwaitWorldEventResponse> _pendingEventSource =
            new UniTaskCompletionSource<AwaitWorldEventResponse>();

        // If a decision-requiring event fired before anyone called
        // await_next_world_event, hand it back immediately on the next call
        // instead of making a late caller wait for a completely new event.
        // Cleared once handed out - a second call with nothing new pending
        // goes back to waiting normally.
        private AwaitWorldEventResponse _cachedPendingEvent;

        public TimeSystem(
            IPublisher<TimeSpeedChangedMessage> speedPublisher,
            IPublisher<WorldEventTriggeredMessage> worldEventPublisher)
        {
            _speedPublisher = speedPublisher;
            _worldEventPublisher = worldEventPublisher;
        }

        public void Start()
        {
            _speedPublisher.Publish(new TimeSpeedChangedMessage { Speed = _speed, Paused = _paused });
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            _speedPublisher.Publish(new TimeSpeedChangedMessage { Speed = _speed, Paused = _paused });
        }

        public void SetSpeed(int speed)
        {
            _speed = speed;
            _speedPublisher.Publish(new TimeSpeedChangedMessage { Speed = _speed, Paused = _paused });
        }

        public bool IsPaused => _paused;

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
            get { return ComputeSimulationDelta(_paused, _speed, Time.deltaTime); }
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
        // Remember: while _paused is true (a decision-requiring event is
        // outstanding), RaiseWorldEvent never fires again - WorldEventSystem
        // checks IsPaused and skips. Call execute_decision first to unpause,
        // or this will time out waiting for an event that can't happen yet.
        public UniTask<AwaitWorldEventResponse> WaitForNextWorldEventAsync()
        {
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
        public void RaiseWorldEvent(string eventId, string description, bool requiresDecision, List<EventChoiceInfo> choices)
        {
            Debug.Log($"[TimeSystem] World event raised: {eventId} (requiresDecision={requiresDecision})");

            if (requiresDecision) SetPaused(true);

            _worldEventPublisher.Publish(new WorldEventTriggeredMessage
            {
                EventId = eventId,
                RequiresDecision = requiresDecision,
                Description = description,
                Choices = choices ?? new List<EventChoiceInfo>(),
            });

            var response = new AwaitWorldEventResponse
            {
                EventId = eventId,
                RequiresDecision = requiresDecision,
                Description = description,
                Choices = choices ?? new List<EventChoiceInfo>(),
            };

            if (requiresDecision)
            {
                _cachedPendingEvent = response;
            }

            var previous = _pendingEventSource;
            _pendingEventSource = new UniTaskCompletionSource<AwaitWorldEventResponse>();
            previous.TrySetResult(response);
        }
    }

    // Answers AwaitWorldEventRequest coming in over the interprocess bus.
    // Request-response, not pub/sub - see the comment on AwaitWorldEventRequest
    // in GameMessages.cs for why.
    public class AwaitWorldEventHandler : IAsyncRequestHandler<AwaitWorldEventRequest, AwaitWorldEventResponse>
    {
        private readonly TimeSystem _timeSystem;

        public AwaitWorldEventHandler(TimeSystem timeSystem)
        {
            _timeSystem = timeSystem;
        }

        public UniTask<AwaitWorldEventResponse> InvokeAsync(AwaitWorldEventRequest request, CancellationToken cancellationToken = default)
        {
            return _timeSystem.WaitForNextWorldEventAsync();
        }
    }

    // Answers SectStateQuery requests coming in over the interprocess bus
    // from the MCP bridge. Aggregates whatever the subsystems currently hold
    // into the SectEconomyState shape from economy.proto.
    public class SectStateQueryHandler : IAsyncRequestHandler<SectStateQuery, SectStateSnapshot>
    {
        private readonly ISectStateProvider _stateProvider;

        public SectStateQueryHandler(ISectStateProvider stateProvider)
        {
            _stateProvider = stateProvider;
        }

        public UniTask<SectStateSnapshot> InvokeAsync(SectStateQuery request, CancellationToken cancellationToken = default)
        {
            var state = _stateProvider.BuildSectEconomyState();
            var snapshot = new SectStateSnapshot
            {
                RequestId = request.RequestId,
                // MessagePack, committed choice (not a protobuf stub
                // anymore - see project_summary.md for why).
                EconomyStateBytes = state.ToByteArray()
            };
            return UniTask.FromResult(snapshot);
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
