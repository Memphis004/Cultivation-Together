// P11A — versioned full-session snapshot contract (in-memory + local persistence).
//
// Shared/ is the authoritative copy. Edit here, then run ./sync-shared.sh.
// Plain C# — no UnityEngine API (Shared is also consumed by the McpBridge
// console app, which has no engine).
//
// ── State inventory (P11A §1) ────────────────────────────────────────────────
// Authoritative gameplay state lives in SectStateProvider (SectEconomyState +
// private work accumulators) and in TimeSystem (simulation speed + pending
// decision). Classification of every field that survives a session:
//
//   PERSISTED (captured in SectSessionSnapshot)
//     SectEconomyState.Disciples           roster: id/name/rank/sex/wallet/
//                                          inventory/task/avatar/backend/
//                                          ownership/Manual-Auto/attributes
//     SectEconomyState.Stockpile           raw resources + crafted goods
//     SectEconomyState.PlacedBuildings     building placement
//     SectEconomyState.ViewerRegistry      membership records + pending apps
//     SectStateProvider._gatherAccumulators  sub-unit gathering progress
//     SectStateProvider._craftProgress       per-disciple craft progress
//     TimeSystem.Speed                     simulation speed (1x..3x)
//     TimeSystem pending decisions         P13B — the WHOLE outstanding queue, each
//                                          occurrence with its instance id, event id,
//                                          description, choices, optional speaker and
//                                          the pending-decision pause flag (§6). No
//                                          instance-specific payload is left behind:
//                                          anything a decision needs after a load is in
//                                          PendingEventSnapshot.
//
//   DERIVED / REBUILT (never serialized; recomputed on load)
//     DiscipleAttributes.SkillLevel        pure fn of SkillXp
//     BuildingGrid occupancy               rebuilt from PlacedBuildings
//     BuildingSystem markers/ghosts        rebuilt on SessionRestoredMessage
//     DiscipleVisualSystem chibi instances reconciled on SessionRestoredMessage
//     AutoTaskScheduler spine allocation   recomputed by VisualTierPolicy
//     UI values                            re-read from authoritative state
//
//   INTENTIONALLY RESET ON LOAD (reason documented per field)
//     SectStateProvider._taskChangeLastAtUtc  per-disciple real-UTC task-change
//         cooldown. The cooldown throttles live task thrash; carrying a stale
//         timestamp across a session would either instantly block the first
//         post-load change or (with a clock jump) silently lapse. Reset so a
//         fresh session starts un-throttled — no progress is lost.
//     SectStateProvider._workTick             per-tick scratch (one outcome per
//         disciple per tick). Rebuilt from scratch by BeginWorkTick every tick;
//         a stale record could double-apply one tick's attribute change.
//     TimeSystem._cachedPendingEvent          bridge handoff cache for
//         await_next_world_event. Consumed-on-read, tied to a live wait; a save
//         must never resurrect a delivery. The authoritative pending decision
//         above IS restored.
//     TimeSystem pause reasons                a fresh session is unpaused; the
//         pending-decision pause is re-derived from the restored pending state.
//     TimeSystem._nextEventInstanceNumber      P13B — the instance-id counter is
//         re-seeded PAST every restored instance id, so a restored occurrence keeps
//         its own id (a decision already addressed to it stays valid across the
//         save/load) while a NEWLY raised event can never collide with one that was
//         saved. SessionRestored subscribers rebuild the decision UI, not the state.
//     TimeSystem._pendingEventSource / _cachedPendingEvent   live waiter + bridge
//         handoff. Never serialized (§4): writing a UniTaskCompletionSource to disk
//         would resurrect a delivery that already happened. The awaiter is rebuilt
//         fresh by the constructor and the cache stays cleared on load.
//     WorldEventSystem._timer/_grace/_started spawn cadence restarts with its
//         grace window; events are not scheduled state.
//     AutoTaskScheduler scheduling maps       evaluation cadence/dwell is a
//         presentation cadence, not progression; restarting it can never lose an
//         assignment or a reward.
//
// ── Missing content ids (P11A §4) ────────────────────────────────────────────
//   - Unknown disciple CurrentTask        → restore FAILS (explicit reason).
//   - Unknown PlacedBuilding.DefId        → restore FAILS (explicit reason).
//   - Unknown item def ids in inventories → preserved verbatim (never
//     substituted; item definitions are content, not a live directive).
// No path silently substitutes an unrelated task or item.

using System;
using System.Collections.Generic;
using MessagePack;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    /// <summary>
    /// P11A — the versioned envelope written to (and read from) a session save.
    /// <see cref="Version"/> is append-only and fail-closed: a value other than
    /// <see cref="CurrentVersion"/> is refused by the restore path, never guessed
    /// at. Version defaults to 0 (not CurrentVersion) so an envelope that predates
    /// the key — or one whose key did not survive — reads as "unknown" and is
    /// rejected rather than silently treated as current.
    /// </summary>
    [MessagePackObject]
    public class SectSessionSnapshotEnvelope
    {
        /// <summary>Only this shape is accepted. Bump only on an incompatible change.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Snapshot format version. 0 = missing/pre-versioned → refused.</summary>
        [Key(0)] public int Version { get; set; }

        /// <summary>When the snapshot was captured (UTC) — informational.</summary>
        [Key(1)] public DateTime SavedAtUtc { get; set; }

        /// <summary>The detached payload. Never aliases live state.</summary>
        [Key(2)] public SectSessionSnapshot Snapshot { get; set; } = new SectSessionSnapshot();
    }

    /// <summary>
    /// P11A — the detached, serializable full-session payload. Every nested
    /// object is a copy (the capture path deep-clones), so a snapshot can be
    /// serialized, handed to a worker thread, or mutated by a caller without
    /// touching live state.
    /// </summary>
    [MessagePackObject]
    public class SectSessionSnapshot
    {
        /// <summary>Roster + economy + buildings + viewer membership/ownership, in ONE snapshot.</summary>
        [Key(0)] public SectEconomyState Economy { get; set; } = new SectEconomyState();

        /// <summary>Sub-unit gathering accumulators, keyed by task id (never per-disciple).</summary>
        [Key(1)] public Dictionary<string, float> GatherAccumulators { get; set; } = new Dictionary<string, float>();

        /// <summary>Per-disciple craft progress in seconds, keyed by disciple id.</summary>
        [Key(2)] public Dictionary<string, float> CraftProgress { get; set; } = new Dictionary<string, float>();

        /// <summary>Simulation speed (TimeSystem.Speed; validated to MinSpeed..MaxSpeed on restore).</summary>
        [Key(3)] public int SimulationSpeed { get; set; } = 1;

        /// <summary>Authoritative pending-decision event id, or null/empty when none (§6).</summary>
        [Key(4)] public string PendingEventId { get; set; }

        /// <summary>Pending-decision description (authoring text).</summary>
        [Key(5)] public string PendingDescription { get; set; }

        /// <summary>Pending-decision choices — captured so a decision is never silently lost.</summary>
        [Key(6)] public List<EventChoiceInfo> PendingChoices { get; set; } = new List<EventChoiceInfo>();

        /// <summary>True when the PendingDecision pause was engaged when captured.</summary>
        [Key(7)] public bool PendingDecisionPaused { get; set; }

        /// <summary>
        /// P13B — the FULL outstanding pending-decision queue in arrival order (oldest
        /// first), each entry carrying its own instance id and instance-specific payload.
        ///
        /// APPENDED key: an older save reads back an empty list, and the restore then
        /// falls back to the Keys 4-7 summary below as a single occurrence (see
        /// <see cref="SectSessionSnapshot.PendingEventId"/>), so an old save still restores
        /// its pending event instead of silently dropping it.
        ///
        /// When this list is non-empty it is AUTHORITATIVE and Keys 4-7 are only a
        /// convenience mirror for readers that predate this key.
        /// </summary>
        [Key(8)] public List<PendingEventSnapshot> PendingEvents { get; set; } = new List<PendingEventSnapshot>();

        /// <summary>True when a decision-requiring event was outstanding and undecided. Derived, never serialized.</summary>
        [IgnoreMember]
        public bool HasPendingDecision =>
            (PendingEvents != null && PendingEvents.Count > 0) || !string.IsNullOrEmpty(PendingEventId);
    }

    /// <summary>
    /// P13B — ONE persisted outstanding pending decision: the occurrence's identity plus
    /// everything needed to REBUILD its decision UI after a load, and nothing else.
    ///
    /// What is deliberately absent (P13B §4): the live UniTaskCompletionSource waiter, the
    /// bridge hand-off cache, MessagePipe subscriptions and every Unity object. Those are
    /// rebuilt by the systems that own them on <see cref="SessionRestoredMessage"/>, never
    /// serialized here.
    ///
    /// The instance id IS persisted (P13B reverses the earlier P13A reset): a decision
    /// addressed to an occurrence survives a save/load, so a click that was in flight, or an
    /// AI GM that decided while the player was saving, still lands exactly once. Applying a
    /// restored decision never re-applies its consequence — the consequence is applied by
    /// the decision, not by the restore (see TimeSystem.TryResolvePendingDecision).
    /// </summary>
    [MessagePackObject]
    public class PendingEventSnapshot
    {
        /// <summary>Unique per raised occurrence (<c>evt_000001</c>); stable across a save/load.</summary>
        [Key(0)] public string InstanceId { get; set; }

        /// <summary>The authored event definition id (<c>cfg.game.EventDef.Id</c>).</summary>
        [Key(1)] public string EventId { get; set; }

        /// <summary>Authoring text the player / the AI GM is shown.</summary>
        [Key(2)] public string Description { get; set; }

        /// <summary>The choices THIS occurrence offered — captured so a decision is never silently lost.</summary>
        [Key(3)] public List<EventChoiceInfo> Choices { get; set; } = new List<EventChoiceInfo>();

        /// <summary>Optional speaker (the disciple whose portrait the popup renders); empty = no speaker.</summary>
        [Key(4)] public string SpeakerDiscipleId { get; set; }

        /// <summary>Display name for <see cref="SpeakerDiscipleId"/>; empty when there is no speaker.</summary>
        [Key(5)] public string SpeakerDisplayName { get; set; }

        /// <summary>True when this occurrence has a speaker portrait to draw. Derived, never serialized.</summary>
        [IgnoreMember]
        public bool HasSpeaker => !string.IsNullOrEmpty(SpeakerDiscipleId);
    }
}
