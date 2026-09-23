using System;
using System.Collections.Generic;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// Phase 5 (§8) — entitlement-aware random pick (reservoir-1, uniform, no
    /// extra allocation). Extracted from AvatarCustomizationPresenter.OnRandomize
    /// so the "never picks a locked part" guarantee is EditMode-testable —
    /// ปุ่ม Randomize/Reroll ของ free tier ใช้ตัวนี้เป็นทางเดียว (Q2 lean:
    /// Preset+Reroll = ปุ่มเดิม + filter นี้)
    /// </summary>
    public static class EntitlementRandom
    {
        /// <summary>
        /// Uniform pick among candidates whose entitlement passes
        /// IVisualEntitlementProvider.CanUse — คืน null ถ้าไม่มีตัวผ่านเลย
        /// (slot ล็อกหมด → caller ข้าม slot นั้นเงียบ ๆ ตามสเปก Phase 5)
        /// </summary>
        public static AvatarPartDef Pick(
            IReadOnlyList<AvatarPartDef> candidates,
            string discipleId,
            IVisualEntitlementProvider provider,
            Random rng)
        {
            if (candidates == null || candidates.Count == 0 || rng == null) return null;

            AvatarPartDef pick = null;
            int allowed = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (c == null) continue;
                if (provider != null && !provider.CanUse(discipleId, c.entitlement)) continue;

                allowed++;
                if (rng.Next(allowed) == 0) pick = c; // reservoir-1: uniform, single pass
            }
            return pick;
        }
    }
}
