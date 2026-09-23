namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// §8 — ใครใส่ part ไหนได้ `entitlement` คือ field บน AvatarPartDef (Phase 1):
    /// "" = free ทุกคน (ส่วนใหญ่ตอนนี้), "owner" = ผู้ซื้อเกม/ตัวละครสำคัญ,
    /// "dlc:&lt;packId&gt;" = แพ็ก DLC ที่ซื้อแล้ว
    ///
    /// Phase 5 ยังไม่มีระบบซื้อ/Twitch id จริง (§14 Q1 นอกขอบเขต) — implementation
    /// จริงคือ DefaultEntitlementProvider แบบ rule-based (owner = rank proxy ชั่วคราว)
    ///
    /// GetPriority หนุน §7 degrade ordering (rank-mapped stub — เปลี่ยนตาม Q1)
    /// </summary>
    public interface IVisualEntitlementProvider
    {
        /// <summary>true = disciple นี้ใช้ part ที่ติด entitlement นี้ได้</summary>
        bool CanUse(string discipleId, string entitlement);

        /// <summary>ความสำคัญตอน SpineBudget ไม่พอ (ยิ่งมากยิ่งได้ก่อน) — §7</summary>
        int GetPriority(string discipleId);
    }
}
