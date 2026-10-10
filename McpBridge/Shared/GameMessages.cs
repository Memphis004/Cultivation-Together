using System;
using System.Collections.Generic;
using MessagePack;
using Xianxia.Sect;

namespace Xianxia.Sect.Messages
{
    // These are lightweight event/delta messages carried over MessagePipe.
    // Deliberately separate from SectEconomyState (economy.proto): the proto
    // state is a full snapshot used for MCP state queries, these are small
    // deltas broadcast on every change so subscribers don't need to diff
    // full state themselves.
    //
    // [MessagePackObject]/[Key] are required because MessagePipe.Interprocess
    // serializes with MessagePack when a message crosses the TCP boundary to
    // the MCP bridge process. In-memory-only messages don't strictly need
    // this, but keeping it consistent means any message can be promoted to
    // interprocess later without a rewrite.

    [MessagePackObject]
    public class DiscipleRecruitedMessage
    {
        [Key(0)] public string DiscipleId { get; set; }
        [Key(1)] public string DisplayName { get; set; }
    }

    [MessagePackObject]
    public class DiscipleRankChangedMessage
    {
        [Key(0)] public string DiscipleId { get; set; }
        [Key(1)] public DiscipleRank NewRank { get; set; }
    }

    [MessagePackObject]
    public class SectResourceChangedMessage
    {
        [Key(0)] public string ResourceId { get; set; }
        [Key(1)] public int Delta { get; set; }
        [Key(2)] public int NewTotal { get; set; }
    }

    [MessagePackObject]
    public class ContributionEarnedMessage
    {
        [Key(0)] public string DiscipleId { get; set; }
        [Key(1)] public long Amount { get; set; }
        [Key(2)] public string Reason { get; set; }
    }

    // Scene lifecycle message (in-memory only — additive scenes don't cross TCP)
    [MessagePackObject]
    public class SceneLoadedMessage
    {
        [Key(0)] public string SceneName { get; set; }
    }

    // Raised on random world events. RequiresDecision marks the ones that
    // should auto-pause the game and wait for the AI GM / vote window.
    // In-memory bus only now (not registered on the interprocess broker) -
    // the bridge learns about world events via AwaitWorldEventRequest/
    // Response (request-response) instead, since IDistributedSubscriber
    // can't be used from the bridge side (see the comment on
    // AwaitWorldEventRequest below). Still useful in-process for e.g. a
    // future Unity-side UI popup that wants to react to the same event.
    [MessagePackObject]
    public class WorldEventTriggeredMessage
    {
        [Key(0)] public string EventId { get; set; }
        [Key(1)] public bool RequiresDecision { get; set; }
        [Key(2)] public string Description { get; set; }
        [Key(3)] public List<EventChoiceInfo> Choices { get; set; } = new List<EventChoiceInfo>();
    }

    // Internal-only (not registered on the interprocess bus) - the UI and
    // other in-Unity systems care about this, the MCP bridge doesn't.
    [MessagePackObject]
    public class TimeSpeedChangedMessage
    {
        [Key(0)] public int Speed { get; set; }
        [Key(1)] public bool Paused { get; set; }
    }

    // Request/response pair: the MCP bridge asks Unity for a full state
    // snapshot on demand (e.g. when the AI GM calls the get_sect_state tool).
    [MessagePackObject]
    public class SectStateQuery
    {
        [Key(0)] public string RequestId { get; set; }
    }

    [MessagePackObject]
    public class SectStateSnapshot
    {
        [Key(0)] public string RequestId { get; set; }
        [Key(1)] public byte[] EconomyStateBytes { get; set; } // SectEconomyState, MessagePack-serialized
    }

    // Request/response pair for await_next_world_event.
    //
    // This is deliberately request-response, not pub/sub, even though
    // conceptually it's "wait for the next event". Confirmed root cause:
    // MessagePipe.Interprocess's TcpDistributedSubscriber unconditionally
    // calls worker.StartReceiver() (binds its own listening socket)
    // regardless of HostAsServer - so a non-hub process (the bridge) can
    // never safely use IDistributedSubscriber over this TCP transport, it
    // collides with the hub's (Unity's) already-bound port. Request-response
    // only ever uses the client connection (Connect, not Listen), which is
    // exactly what get_sect_state already proved works fine. The handler on
    // the Unity side just holds the response open (via a UniTaskCompletionSource)
    // until an event actually fires - same observed behavior as a
    // subscription, without the broken transport.
    [MessagePackObject]
    public class AwaitWorldEventRequest
    {
        [Key(0)] public string RequestId { get; set; }
    }

    [MessagePackObject]
    public class AwaitWorldEventResponse
    {
        [Key(0)] public string EventId { get; set; }
        [Key(1)] public bool RequiresDecision { get; set; }
        [Key(2)] public string Description { get; set; }
        [Key(3)] public List<EventChoiceInfo> Choices { get; set; } = new List<EventChoiceInfo>();
    }

    // One selectable option for a world event - authored per-event in the
    // EventData ScriptableObject, carried over the wire so the AI GM sees
    // real choices instead of just a bare event id.
    [MessagePackObject]
    public class EventChoiceInfo
    {
        [Key(0)] public string ChoiceId { get; set; }
        [Key(1)] public string Label { get; set; }
    }

    // Sent from the MCP bridge back into Unity when the AI GM (or a vote
    // result) picks an option for the current world event. Completes the
    // write path that SectActionTools.ExecuteDecision was a stub for.
    [MessagePackObject]
    public class ExecuteDecisionMessage
    {
        [Key(0)] public string EventId { get; set; }
        [Key(1)] public string ChoiceId { get; set; }
    }

    // Published in-memory by DecisionExecutor (not sent over interprocess -
    // this is the "it already happened" notification for local listeners
    // like the in-game log window, distinct from ExecuteDecisionMessage
    // which is the incoming command from the bridge). Whether the decision
    // originated from the bridge or from clicking a choice in the UI,
    // DecisionExecutor is the single place both paths funnel through, so
    // this fires exactly once per decision regardless of source.
    [MessagePackObject]
    public class DecisionExecutedMessage
    {
        [Key(0)] public string EventId { get; set; }
        [Key(1)] public string ChoiceId { get; set; }
    }

    // Internal-only: entering build (placement) mode. CameraRigController
    // zooms to the Placement preset and GridOverlayRenderer shows the grid;
    // Payload carries the ghost transform when one exists (may be null).
    [MessagePackObject]
    public class BuildModeStartedMessage
    {
        [Key(0)] public string SourceId { get; set; }
        [Key(1)] public string GhostId { get; set; }
    }

    // Internal-only: leaving build mode (confirm, cancel, or system stop).
    [MessagePackObject]
    public class BuildModeEndedMessage
    {
        [Key(0)] public string SourceId { get; set; }
        [Key(1)] public bool Confirmed { get; set; }
    }

    // Request/response pair: a disciple buying an item from the sect
    // stockpile (CraftedGoods) with their own contribution. Request-response
    // rather than a fire-and-forget message because the bridge needs to
    // know immediately whether the purchase actually succeeded (enough
    // contribution, enough stock) - same reasoning as SectStateQuery.
    [MessagePackObject]
    public class PurchaseItemRequest
    {
        [Key(0)] public string DiscipleId { get; set; }
        [Key(1)] public string ItemDefId { get; set; }
        [Key(2)] public int Grade { get; set; }
        [Key(3)] public int Quantity { get; set; }
    }

    [MessagePackObject]
    public class PurchaseItemResponse
    {
        [Key(0)] public bool Success { get; set; }
        [Key(1)] public string Message { get; set; }
        [Key(2)] public long RemainingContribution { get; set; }
    }

    // ---------- Building placed (Phase 1 — grid placement engine) ----------
    // Published in-memory by SectStateProvider.TryPlaceBuilding after a
    // successful commit. BuildingSystem listens and rebuilds grid occupancy +
    // visuals. ⚠️ Deliberately NOT added to InterprocessTopics — building
    // placement is player-only in Phase 1 (building-system.md §8 Q3 default),
    // so the MCP bridge never needs it on the wire.
    [MessagePackObject]
    public class BuildingPlacedMessage
    {
        [Key(0)] public string InstanceId { get; set; } = string.Empty;
        [Key(1)] public string DefId { get; set; } = string.Empty;
        [Key(2)] public int GridX { get; set; }
        [Key(3)] public int GridZ { get; set; }
        [Key(4)] public int Rotation { get; set; }
    }

    // ---------- Chibi backend entitlement change (Phase 3) ----------
    // Published in-memory by SectStateProvider.TrySetChibiBackend after mutating
    // DiscipleState.ChibiBackend. DiscipleVisualSystem respawns the visual IN PLACE
    // (same position/activity/facing) on this message.
    // ⚠️ Deliberately NOT added to InterprocessTopics — visual-tier churn is
    // irrelevant to the MCP bridge; keep it off the TCP broker (same rule as
    // every DiscipleVisualSystem message).
    [MessagePackObject]
    public class DiscipleChibiBackendChangedMessage
    {
        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
        [Key(1)] public ChibiBackend Old { get; set; }
        [Key(2)] public ChibiBackend New { get; set; }
    }

    // ---------- Disciple selected (Phase 4 — chibi click) ----------
    // Published in-memory by ChibiClickTarget when the player clicks a chibi
    // in the gameplay scene. DiscipleDetailUISystem opens the DiscipleDetail
    // panel on this message. ⚠️ Deliberately NOT added to InterprocessTopics —
    // same in-memory-only rule as every DiscipleVisualSystem message.
    [MessagePackObject]
    public class DiscipleSelectedMessage
    {
        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
    }

    // ---------- Avatar equipment change ----------
    // Broadcast after a successful avatar part change (pub/sub, goes to UI + MCP client)
    [MessagePackObject]
    public class AvatarEquipmentChangedMessage
    {
        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
        [Key(1)] public string Slot { get; set; } = string.Empty;   // AvatarSlots.*
        [Key(2)] public string OldPartId { get; set; } = string.Empty;
        [Key(3)] public string NewPartId { get; set; } = string.Empty;
    }

    // Request/response pair for changing an avatar part.
    // Request-response (not fire-and-forget) because the bridge needs
    // to know immediately whether the PartId is valid or the disciple
    // id is wrong - same reasoning as PurchaseItemRequest.
    [MessagePackObject]
    public class ChangeAvatarPartRequest
    {
        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
        [Key(1)] public string Slot { get; set; } = string.Empty;
        [Key(2)] public string PartId { get; set; } = string.Empty;   // "" = back to default
    }

    [MessagePackObject]
    public class ChangeAvatarPartResponse
    {
        [Key(0)] public bool Success { get; set; }
        [Key(1)] public string FailReason { get; set; } = string.Empty;
        [Key(2)] public AvatarAppearance ResultAvatar { get; set; }
    }

    // ---------- Task assignment (task-system-v2.md §6) ----------
    // Request/response pair: the MCP bridge (or AI GM) asks Unity to assign a
    // task to a disciple. Request-response because the caller needs the
    // permission/validity verdict immediately - same reasoning as
    // PurchaseItemRequest. RequesterId drives the ownership check
    // ("SECT_MASTER" may assign anyone; others only their own disciples).
    [MessagePackObject]
    public class AssignTaskRequest
    {
        [Key(0)] public string RequesterId { get; set; } = string.Empty;
        [Key(1)] public string DiscipleId { get; set; } = string.Empty;
        [Key(2)] public string TaskId { get; set; } = string.Empty;
    }

    [MessagePackObject]
    public class AssignTaskResponse
    {
        [Key(0)] public bool Success { get; set; }
        [Key(1)] public string FailReason { get; set; } = string.Empty;
    }

    // Published in-memory by SectStateProvider.TryAssignTask after a successful
    // commit. TaskActivityMapper / visual systems react to the new task.
    // ⚠️ Deliberately NOT added to InterprocessTopics — same in-memory-only
    // rule as DecisionExecutedMessage / BuildingPlacedMessage.
    [MessagePackObject]
    public class DiscipleTaskChangedMessage
    {
        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
        [Key(1)] public string TaskId { get; set; } = string.Empty;
    }

    // ---------- Disciple ownership change (P4 — local ownership test harness) ----------
    // Published in-memory by SectStateProvider.TrySetDiscipleOwner after a real
    // successful change (no-op when the values are already identical). The local
    // dev harness is the only intended caller — there is no public viewer command.
    // ⚠️ Deliberately NOT added to InterprocessTopics — ownership assignment is a
    // development/debug concern until real account linking exists (P5B+).
    // Observability: the MCP bridge can READ the change log via the
    // OwnershipObservabilityQuery/Snapshot request-response pair below (same
    // mechanism as SectStateQuery — IDistributedSubscriber is unusable from the
    // bridge, see AwaitWorldEventRequest). The bridge can never WRITE ownership:
    // no assignment request/response or tool exists on the wire.
    [MessagePackObject]
    public class DiscipleOwnerChangedMessage
    {
        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
        [Key(1)] public DiscipleOwnerType OldType { get; set; }
        [Key(2)] public string OldOwnerId { get; set; } = string.Empty;
        [Key(3)] public DiscipleOwnerType NewType { get; set; }
        [Key(4)] public string NewOwnerId { get; set; } = string.Empty;
    }

    // ---------- P9A — explicit Manual/Auto control-mode change ----------
    // Published in-memory by SectStateProvider when a disciple's ControlMode actually
    // changes: an explicit player toggle, an ownership change that invalidates Auto,
    // or a successful manual task assignment taking a disciple out of Auto.
    // ⚠️ Deliberately NOT added to InterprocessTopics — autonomy is a local
    // player/UI concern, same in-memory-only rule as DiscipleOwnerChangedMessage.
    [MessagePackObject]
    public class DiscipleControlModeChangedMessage
    {
        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
        [Key(1)] public DiscipleControlMode OldMode { get; set; }
        [Key(2)] public DiscipleControlMode NewMode { get; set; }
    }

    // Request/response pair: the MCP bridge reads the disciples' ownership-change
    // log (filled by OwnershipObservabilityBuffer, one entry per real change).
    // Observability ONLY — deliberately no request/response pair or MCP tool that
    // assigns ownership: the only writer stays the local dev harness / fixtures.
    [MessagePackObject]
    public class OwnershipObservabilityQuery
    {
        [Key(0)] public string RequestId { get; set; }
    }

    [MessagePackObject]
    public class OwnershipObservabilitySnapshot
    {
        [Key(0)] public string RequestId { get; set; }
        [Key(1)] public List<DiscipleOwnerChangedMessage> Events { get; set; } = new List<DiscipleOwnerChangedMessage>();
    }

    // ---------- P5B observability: task-change log + protection state (READ-ONLY) ----------
    // Both pairs are pulled on demand by the MCP bridge (request-response, same
    // mechanism as OwnershipObservabilityQuery — the bridge cannot
    // IDistributedSubscriber over the TCP transport). Neither pair has a write
    // path: task assignment keeps its own AssignTaskRequest/Response, and these
    // are observation only. DiscipleTaskChangedMessage stays in-memory-only; the
    // bridge pulls the buffered log instead.
    [MessagePackObject]
    public class TaskChangeObservabilityQuery
    {
        [Key(0)] public string RequestId { get; set; }
    }

    [MessagePackObject]
    public class TaskChangeObservabilitySnapshot
    {
        [Key(0)] public string RequestId { get; set; }
        [Key(1)] public List<DiscipleTaskChangedMessage> Events { get; set; } = new List<DiscipleTaskChangedMessage>();
    }

    [MessagePackObject]
    public class TaskProtectionQuery
    {
        [Key(0)] public string RequestId { get; set; }
    }

    /// <summary>
    /// One Viewer-owned disciple (the set the hybrid protection policy governs),
    /// with the read-only answers the bridge needs: is the owner still protected,
    /// how long is left, is the membership data consistent, and may the SectMaster
    /// / the owner change its task right now (with the authority's reason).
    /// Computed from ISectStateProvider.CheckTaskPermission — the same authority
    /// TryAssignTask revalidates, so this view can never disagree with a commit.
    /// </summary>
    [MessagePackObject]
    public class TaskProtectionEntry
    {
        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
        [Key(1)] public string CurrentTask { get; set; } = string.Empty;
        [Key(2)] public DiscipleOwnerType OwnerType { get; set; }
        [Key(3)] public string OwnerId { get; set; } = string.Empty;
        [Key(4)] public bool OwnerProtected { get; set; }
        [Key(5)] public float OwnerProtectionRemainingSeconds { get; set; }
        [Key(6)] public DateTime LastActiveAtUtc { get; set; }
        [Key(7)] public bool MembershipConsistent { get; set; }
        [Key(8)] public bool SectMasterMayChange { get; set; }
        [Key(9)] public string SectMasterReason { get; set; } = string.Empty;
        [Key(10)] public bool OwnerMayChange { get; set; }
        [Key(11)] public string OwnerReason { get; set; } = string.Empty;
    }

    [MessagePackObject]
    public class TaskProtectionSnapshot
    {
        [Key(0)] public string RequestId { get; set; }
        [Key(1)] public List<TaskProtectionEntry> Entries { get; set; } = new List<TaskProtectionEntry>();
    }

    // ---------- P5B persistence: viewer membership slice (local disk, not on the wire) ----------
    // Persisted together so the registry ⇄ disciple ownership pair can never be
    // restored half-way (invariants #1/#2/#3 hold across sessions). Deliberately
    // a SLICE, not the whole SectEconomyState: only membership + ownership +
    // activity survive; economy/roster still come from MockSectData.
    [MessagePackObject]
    public class SectViewerMembershipSave
    {
        /// <summary>Only this shape is accepted; anything else is ignored on load (fail-closed to mock start).</summary>
        public const int CurrentVersion = 1;

        /// <summary>Bumped only if the shape changes incompatibly; a mismatch is ignored on load (fail-closed to mock start).</summary>
        [Key(0)] public int Version { get; set; } = CurrentVersion;

        /// <summary>When the file was written (UTC) — informational.</summary>
        [Key(1)] public DateTime SavedAtUtc { get; set; }

        [Key(2)] public List<ViewerRecord> Records { get; set; } = new List<ViewerRecord>();
        [Key(3)] public List<PendingViewerApplication> PendingApplications { get; set; } = new List<PendingViewerApplication>();

        /// <summary>discipleId → owner, captured in the SAME snapshot so ownership and registry agree on load.</summary>
        [Key(4)] public List<SectSavedOwnership> OwnerByDisciple { get; set; } = new List<SectSavedOwnership>();
    }

    [MessagePackObject]
    public class SectSavedOwnership
    {
        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
        [Key(1)] public DiscipleOwnerType OwnerType { get; set; }
        [Key(2)] public string OwnerId { get; set; } = string.Empty;
    }

    // ---------- P11A — session restored notification ----------
    // Published in-memory AFTER a full-session snapshot has been committed to live
    // state (SectStateProvider + TimeSystem). Derived systems react by rebuilding
    // caches and dropping stale session references:
    //   - BuildingSystem       rebuilds grid occupancy + placed markers
    //   - DiscipleVisualSystem reconciles (despawn gone / spawn restored)
    //   - AutoTaskScheduler    clears its simulation-time scheduling maps
    //   - WalletHudPresenter   re-reads the authoritative wallet/stockpile
    // ⚠️ Deliberately NOT added to InterprocessTopics — session load is a local
    // player concern; the bridge observes state via SectStateQuery, not this.
    // Never published on a failed restore (invalid data leaves the session unchanged).
    [MessagePackObject]
    public class SessionRestoredMessage
    {
        /// <summary>True when a full-session snapshot was committed (as opposed to a legacy slice).</summary>
        [Key(0)] public bool FullSession { get; set; }

        /// <summary>UTC time the restore committed.</summary>
        [Key(1)] public DateTime RestoredAtUtc { get; set; }

        /// <summary>Roster size after the restore (diagnostic / UI priming).</summary>
        [Key(2)] public int DiscipleCount { get; set; }

        /// <summary>Placed-building count after the restore (diagnostic / UI priming).</summary>
        [Key(3)] public int BuildingCount { get; set; }
    }

    // Topic keys for the keyed (IDistributedPublisher<TKey,TMessage>) channels.
    public static class InterprocessTopics
    {
        public const string DiscipleRecruited = "sect.disciple_recruited";
        public const string DiscipleRankChanged = "sect.disciple_rank_changed";
        public const string ResourceChanged = "sect.resource_changed";
        public const string ContributionEarned = "sect.contribution_earned";
        public const string WorldEvent = "sect.world_event";
        public const string ExecuteDecision = "sect.execute_decision";
        public const string AvatarEquipmentChanged = "sect.avatar_equipment_changed";
    }
}
