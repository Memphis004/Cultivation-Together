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
//     TimeSystem pending decision          event id / description / choices /
//                                          pause flag (captured safely — §6)
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

        /// <summary>True when a decision-requiring event was outstanding and undecided. Derived, never serialized.</summary>
        [IgnoreMember]
        public bool HasPendingDecision => !string.IsNullOrEmpty(PendingEventId);
    }
}
