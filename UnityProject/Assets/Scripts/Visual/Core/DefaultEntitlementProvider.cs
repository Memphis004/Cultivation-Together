using System;
using System.Collections.Generic;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// Phase 5 — rule-based IVisualEntitlementProvider (placeholder จนกว่า Q1 จะตัดสิน:
    /// ระบบซื้อเกม / Twitch id / purchase account ยังไม่มี — ห้าม implement แทน
    /// §12/§14 ของ disciple-visual-system.md)
    ///
    /// Rules (ตาม plan §8 + prompt Phase 5):
    /// - "" → ผ่านเสมอ (free part)
    /// - "owner" → เช็คจาก DiscipleRank >= Elder เป็น PROXY ชั่วคราว — comment
    ///   ชัดเจนว่า placeholder รอ Q1 (เมื่อมี viewer/purchase id จริง ให้แทน lookup
    ///   นี้โดยไม่แตะ call sites — interface คงเดิม)
    /// - "dlc:<packId>" → placeholder owned-DLC list ว่างเปล่า → ปฏิเสธเสมอตอนนี้
    ///   (ห้าม hardcode true เพื่อ "ให้ผ่านไปก่อน")
    /// - unknown token → ปฏิเสธ + warn ครั้งเดียวต่อ token (fail-closed)
    /// </summary>
    public sealed class DefaultEntitlementProvider : IVisualEntitlementProvider
    {
        /// <summary>
        /// Rank lookup ของศิษย์ — bind โดย SectStateProvider หลัง ctor (ดู comment
        /// ที่ SectStateProvider: ผูกตรง ๆ ผ่าน DI จะเกิด cycle — SectStateProvider
        /// → provider → SectStateProvider) ก่อน bind คืน Unspecified = ปฏิเสธ owner
        /// (fail-closed ตามสเปก)
        /// </summary>
        private Func<string, DiscipleRank> _rankLookup = _ => DiscipleRank.Unspecified;

        /// <summary>Placeholder owned-DLC packs — ว่างจนมีระบบซื้อจริง (Q1)</summary>
        private readonly HashSet<string> _ownedDlcPacks = new HashSet<string>(StringComparer.Ordinal);

        private readonly HashSet<string> _warnedTokens = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Called by SectStateProvider once at construction — swaps the rank source.</summary>
        public void BindRankLookup(Func<string, DiscipleRank> rankLookup)
        {
            _rankLookup = rankLookup ?? (_ => DiscipleRank.Unspecified);
        }

        public bool CanUse(string discipleId, string entitlement)
        {
            // "" หรือ null = free part — ผ่านเสมอ (regression guard: ทุก part ปัจจุบันเป็น "")
            if (string.IsNullOrEmpty(entitlement)) return true;

            // "owner" → rank proxy ชั่วคราว (PLACEHOLDER รอ Q1 — ห้ามถือว่า final)
            if (entitlement == "owner")
            {
                return _rankLookup(discipleId) >= DiscipleRank.Elder;
            }

            // "dlc:<packId>" → owned-DLC list ว่าง = ปฏิเสธเสมอ (ไม่มี DLC จริง)
            if (entitlement.StartsWith("dlc:", StringComparison.Ordinal))
            {
                var packId = entitlement.Substring(4);
                return _ownedDlcPacks.Contains(packId);
            }

            // unknown token — fail-closed + warn once per token (L9 spirit)
            if (_warnedTokens.Add(entitlement))
            {
                UnityEngine.Debug.LogWarning("[DefaultEntitlementProvider] unknown entitlement token '" +
                                             entitlement + "' — denying (fail-closed)");
            }
            return false;
        }

        public int GetPriority(string discipleId)
        {
            // §7 priority stub — rank-mapped ชั่วคราว (ให้ผลเดียวกับ CompareSpawnPriority
            // เดิม); เมื่อ Q1 มา จะแทนด้วย purchaser/viewer priority จริง
            var rank = _rankLookup(discipleId);
            switch (rank)
            {
                case DiscipleRank.SectMaster: return 40;
                case DiscipleRank.Elder: return 30;
                case DiscipleRank.InnerDisciple: return 20;
                case DiscipleRank.OuterDisciple: return 10;
                default: return 0;
            }
        }
    }
}
