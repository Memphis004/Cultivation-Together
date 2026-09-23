using System;
using System.Collections.Generic;

namespace Xianxia.Sect.Visual
{    /// <summary>
    /// §7 — decides the EFFECTIVE render backend per disciple at spawn/reconcile time:
    /// entitled Spine AND under SpineBudget → Spine, else SpriteSheet.
    ///
    /// Entitlement (DiscipleState.ChibiBackend) is never mutated here — degrade is a
    /// render-only decision (acceptance: budget=1 → d003 renders Sprite while state
    /// stays Spine). Also owns the degrade priority ordering (rank desc, then
    /// DiscipleId asc — §7 table) so DiscipleVisualSystem embeds none of it.
    ///
    /// Plain C# singleton like VisualRuntimeConfig; registered in GameLifetimeScope.
    /// </summary>
    public sealed class VisualTierPolicy
    {
        public static readonly VisualTierPolicy Instance = new VisualTierPolicy();

        private VisualTierPolicy() { }

        /// <summary>
        /// Effective backend for one disciple given the CURRENT spine-active count.
        /// Pure function of (entitlement, budget, count) — no side effects.
        /// </summary>
        public VisualBackend EffectiveBackend(DiscipleState d, int currentSpineActiveCount)
        {
            if (d == null) return VisualBackend.SpriteSheet;

            bool spineEntitled = d.ChibiBackend == ChibiBackend.Spine;
            bool spineAllowed = spineEntitled
                && (VisualRuntimeConfig.Instance.SpineEnabled || VisualRuntimeConfig.Instance.DevSpineOverride)
                && currentSpineActiveCount < VisualRuntimeConfig.Instance.SpineBudget;

            return spineAllowed ? VisualBackend.Spine : VisualBackend.SpriteSheet;
        }

        /// <summary>
        /// §7 — allocate the limited Spine slots by priority (rank desc, then
        /// DiscipleId asc) and return the set of discipleIds that should render
        /// as Spine (reused set — no per-call allocation). Budget-limited
        /// allocation lives HERE so DiscipleVisualSystem embeds none of the
        /// ordering logic (VisualTierPolicy owns §7).
        /// Entitlement in state is never touched — allocation is render-only.
        /// </summary>
        public void AllocateSpineSlots(
            List<DiscipleState> disciples,
            Func<DiscipleState, string> idOf,
            HashSet<string> results)
        {
            results.Clear();
            if (disciples == null || disciples.Count == 0) return;
            if (!(VisualRuntimeConfig.Instance.SpineEnabled || VisualRuntimeConfig.Instance.DevSpineOverride)
                || VisualRuntimeConfig.Instance.SpineBudget <= 0)
            {
                return; // Spine off (or dev-only override off) or zero budget → nobody renders Spine (state untouched)
            }

            _eligible.Clear();
            for (int i = 0; i < disciples.Count; i++)
            {
                var d = disciples[i];
                if (d != null && d.ChibiBackend == ChibiBackend.Spine) _eligible.Add(d);
            }
            if (_eligible.Count == 0) return;

            SortBySpawnPriority(_eligible);

            int budget = VisualRuntimeConfig.Instance.SpineBudget;
            for (int i = 0; i < _eligible.Count && results.Count < budget; i++)
            {
                results.Add(idOf(_eligible[i]));
            }
        }

        /// <summary>CompareSpawnPriority over a reusable list — allocation scratch, no per-call alloc.</summary>
        private readonly List<DiscipleState> _eligible = new List<DiscipleState>(16);

        /// <summary>
        /// §7 degrade/promotion ordering: rank descending first (SectMaster &gt; Elder &gt;
        /// Inner &gt; Outer), then DiscipleId ascending for determinism. Callers spawn in
        /// this order while counting against SpineBudget, so higher-priority disciples
        /// win the limited Spine slots.
        /// </summary>
        public void SortBySpawnPriority(List<DiscipleState> disciples)
        {
            if (disciples == null || disciples.Count < 2) return;
            disciples.Sort(CompareSpawnPriority);
        }

        public int CompareSpawnPriority(DiscipleState a, DiscipleState b)
        {
            int rankA = a != null ? (int)a.Rank : 0;
            int rankB = b != null ? (int)b.Rank : 0;
            int byRank = rankB.CompareTo(rankA); // descending
            if (byRank != 0) return byRank;
            return string.CompareOrdinal(a != null ? a.DiscipleId : string.Empty,
                                         b != null ? b.DiscipleId : string.Empty);
        }
    }
}
