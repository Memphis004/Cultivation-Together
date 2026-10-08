using System;
using System.Collections.Generic;
using MessagePack;

namespace Xianxia.Sect
{
    /// <summary>
    /// P5A — membership status of a viewer in the sect.
    /// Pending = ได้ยื่นคำขอ แต่ยังไม่ผ่านการอนุมัติ — ห้ามถือเป็นสมาชิก active
    /// (ตามสเปก P5A §4: unapproved applicant ≠ active member)
    /// Append-only enum: old saves deserialize Unknown → fail-closed ใน invariant checks.
    /// </summary>
    public enum ViewerMembershipStatus
    {
        /// <summary>ค่า default ของ state เก่า / uninitiated record — ไม่ใช่สมาชิก</summary>
        Unknown = 0,
        /// <summary>ยื่นคำขอเข้าสำนักแล้ว รออนุมัติ — NOT active (P5A §4)</summary>
        Pending = 1,
        /// <summary>สมาชิก active — ต้องมี BoundDiscipleId ที่ agree กับ DiscipleState.OwnerId</summary>
        Active = 2,
        /// <summary>อดีตสมาชิก (ออกเอง/ถูกไล่ในอนาคต) — record เก็บไว้แต่ไม่ผูกศิษย์</summary>
        Left = 3,
    }

    /// <summary>
    /// P5A — สมุดทะเบียนสมาชิกผู้ชม 1 คน (data contract เท่านั้น — ยังไม่มี join/expulsion
    /// /change-permission behavior ตามสเปก §7; ผู้เขียนคนเดียวตอนนี้คือ test fixtures /
    /// dev harness อนาคต P5B+)
    ///
    /// ⚠️ Invariants (P5A §3 — mutate ต้องอัปเดตทั้งคู่พร้อมกัน):
    ///   1. one active viewer membership → one matching disciple:
    ///      Status == Active ⇒ BoundDiscipleId non-empty และ
    ///      DiscipleState[BoundDiscipleId].OwnerType == Viewer และ OwnerId == ViewerId
    ///   2. reverse: DiscipleState.OwnerType == Viewer ⇒ มี record ที่ Status == Active
    ///      และ ViewerId == disciple.OwnerId และ BoundDiscipleId == disciple.DiscipleId
    ///   3. Status != Active ⇒ BoundDiscipleId ว่าง (ไม่ผูกศิษย์ค้างไว้)
    ///   4. หนึ่ง ViewerId มี record เดียว; หนึ่ง disciple ผูกได้กับ active record เดียว
    ///
    /// OwnerType/OwnerId บน DiscipleState ยังเป็น control fields เดิม (P5A §3) —
    /// record นี้อ่าน/เขียนคู่กันเสมอ ไม่แทนที่
    /// </summary>
    [MessagePackObject]
    public class ViewerRecord
    {
        /// <summary>Stable viewer identity (เช่น Twitch user id ในอนาคต P5B+; ตอนนี้ synthetic convention เดียวกับ P4)</summary>
        [Key(0)] public string ViewerId { get; set; } = string.Empty;

        /// <summary>Cached login/display name — naming conventions เดียวกับ repository (alpha prefix + [A-Za-z0-9_], ไม่มี whitespace)</summary>
        [Key(1)] public string DisplayName { get; set; } = string.Empty;

        /// <summary>ศิษย์ที่ผูกไว้ (ว่าง = ไม่ผูก) — ต้อง agree กับ DiscipleState.OwnerType/OwnerId ตาม invariants ข้างบน</summary>
        [Key(2)] public string BoundDiscipleId { get; set; } = string.Empty;

        /// <summary>สถานะสมาชิก — Pending ≠ active member (P5A §4)</summary>
        [Key(3)] public ViewerMembershipStatus Status { get; set; } = ViewerMembershipStatus.Unknown;

        /// <summary>เวลา active ล่าสุด (UTC เท่านั้น — ห้าม persist ค่า stopwatch/process-local, P5A §6)</summary>
        [Key(4)] public DateTime LastActiveAtUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Invariant #1 (forward): active record ต้องผูกศิษย์; non-active ต้องไม่ผูก.
        /// Pure data check — ไม่อ่าน roster, ใช้ตรวจ record เดี่ยว (ดู SectViewerRoster.VerifyAgainst สำหรับ cross-check)
        /// </summary>
        public bool IsSelfConsistent()
        {
            if (Status == ViewerMembershipStatus.Active)
                return !string.IsNullOrEmpty(BoundDiscipleId);
            return string.IsNullOrEmpty(BoundDiscipleId);
        }

        /// <summary>ตรวจ naming convention เดียวกับ SectStateProvider.OwnerIdPattern (alpha prefix, [A-Za-z0-9_])</summary>
        public bool HasValidIdentityConvention()
        {
            if (string.IsNullOrEmpty(ViewerId)) return false;
            return System.Text.RegularExpressions.Regex.IsMatch(ViewerId, "^[A-Za-z][A-Za-z0-9]*(_[A-Za-z0-9]+)*$");
        }
    }

    /// <summary>
    /// P5A — คำขอเข้าสำนักที่ยังไม่อนุมัติ (แยกจาก active membership ชัดเจน ตาม P5A §4:
    /// unapproved applicant ไม่เคยปรากฏเป็น active member จนกว่าจะมี mutation อนุมัติ
    /// ในอนาคตที่อัปเดตทั้ง record + disciple ownership พร้อมกัน)
    /// </summary>
    [MessagePackObject]
    public class PendingViewerApplication
    {
        [Key(0)] public string ViewerId { get; set; } = string.Empty;
        [Key(1)] public string DisplayName { get; set; } = string.Empty;
        /// <summary>เวลาที่ยื่นคำขอ (UTC)</summary>
        [Key(2)] public DateTime AppliedAtUtc { get; set; } = DateTime.MinValue;
    }

    /// <summary>
    /// P5A — เวลาแบบ injectable สำหรับ future inactivity checks (P5A §6):
    /// production ผูก UtcClock (DateTime.UtcNow), tests ผูก fake ได้.
    /// กฎ: persist เฉพาะ UTC timestamps (LastActiveAtUtc/AppliedAtUtc) —
    /// ห้ามเก็บค่า process-local stopwatch ลง state
    /// </summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    /// <summary>Production clock — DateTime.UtcNow ทุกครั้ง (ไม่ cache กัน drift ตอนหยุดเกม)</summary>
    public sealed class UtcClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    /// <summary>
    /// P5A — สมุดทะเบียนรวม: active/past membership records + คำขอที่รออนุมัติ
    /// (แยก list ชัดเจน — คำขอไม่เคยปนกับสมาชิก, P5A §4)
    /// Wire เข้า SectEconomyState [Key(3)]/[Key(4)] append-only
    /// </summary>
    [MessagePackObject]
    public class SectViewerRegistry
    {
        /// <summary>สมาชิกทุกสถานะ (Active/Left/Pending-backlink) — ViewerId ต้อง unique</summary>
        [Key(0)] public List<ViewerRecord> Records { get; set; } = new List<ViewerRecord>();

        /// <summary>คำขอที่ยังไม่อนุมัติ — ViewerId ห้ามซ้ำกันและห้ามซ้ำกับ Records ที่ Active</summary>
        [Key(1)] public List<PendingViewerApplication> PendingApplications { get; set; } = new List<PendingViewerApplication>();

        /// <summary>หา record ของ viewer (null = ไม่มี)</summary>
        public ViewerRecord Find(string viewerId)
        {
            if (string.IsNullOrEmpty(viewerId)) return null;
            for (int i = 0; i < Records.Count; i++)
            {
                var r = Records[i];
                if (r != null && r.ViewerId == viewerId) return r;
            }
            return null;
        }

        /// <summary>หา record active ที่ผูกศิษย์นี้อยู่ (null = ไม่มี)</summary>
        public ViewerRecord FindByBoundDisciple(string discipleId)
        {
            if (string.IsNullOrEmpty(discipleId)) return null;
            for (int i = 0; i < Records.Count; i++)
            {
                var r = Records[i];
                if (r != null && r.Status == ViewerMembershipStatus.Active && r.BoundDiscipleId == discipleId) return r;
            }
            return null;
        }

        /// <summary>
        /// Invariants #1/#3/#4 self-check ทั้ง registry (single-source helper สำหรับ tests):
        /// - ViewerId unique ทั้ง Records และ PendingApplications
        /// - ทุก record IsSelfConsistent (active ⇒ ผูก, non-active ⇒ ว่าง)
        /// - BoundDiscipleId หนึ่ง id ถูกผูกโดย active record ได้ตัวเดียว
        /// </summary>
        public bool IsInternallyConsistent()
        {
            var seen = new HashSet<string>();
            var boundDisciples = new HashSet<string>();
            for (int i = 0; i < Records.Count; i++)
            {
                var r = Records[i];
                if (r == null || string.IsNullOrEmpty(r.ViewerId) || !seen.Add(r.ViewerId)) return false;
                if (!r.IsSelfConsistent()) return false;
                if (r.Status == ViewerMembershipStatus.Active && !boundDisciples.Add(r.BoundDiscipleId)) return false;
            }
            var pendingSeen = new HashSet<string>();
            for (int i = 0; i < PendingApplications.Count; i++)
            {
                var a = PendingApplications[i];
                if (a == null || string.IsNullOrEmpty(a.ViewerId) || !pendingSeen.Add(a.ViewerId)) return false;
                if (seen.Contains(a.ViewerId)) return false; // คำขอห้ามซ้ำกับ record
            }
            return true;
        }

        /// <summary>
        /// Invariant #2 (reverse, cross-check กับ roster จริง): ทุก disciple Viewer-owned
        /// ต้องมี active record ที่ BoundDiscipleId ตรงและ ViewerId == disciple.OwnerId,
        /// และทุก active record ต้องชี้ disciple ที่มีจริง + agree กัน — อ่านอย่างเดียว
        /// (disciples list ผ่านเป็น parameter เพื่อไม่ให้ registry จับ object SectEconomyState)
        /// </summary>
        public bool VerifyAgainst(IList<DiscipleState> disciples)
        {
            if (disciples == null) return false;

            // forward: active record ⇔ disciple Viewer-owned with matching OwnerId
            for (int i = 0; i < Records.Count; i++)
            {
                var r = Records[i];
                if (r == null || !r.IsSelfConsistent()) return false;
                if (r.Status != ViewerMembershipStatus.Active) continue;

                DiscipleState match = null;
                for (int j = 0; j < disciples.Count; j++)
                {
                    var d = disciples[j];
                    if (d != null && d.DiscipleId == r.BoundDiscipleId) { match = d; break; }
                }
                if (match == null) return false;                          // ผูกศิษย์ที่ไม่มีจริง
                if (match.OwnerType != DiscipleOwnerType.Viewer) return false;
                if (match.OwnerId != r.ViewerId) return false;            // #1 ต้อง agree
            }

            // reverse: ทุก disciple Viewer-owned ต้องมี active record กลับมา
            for (int j = 0; j < disciples.Count; j++)
            {
                var d = disciples[j];
                if (d == null || d.OwnerType != DiscipleOwnerType.Viewer) continue;
                var r = FindByBoundDisciple(d.DiscipleId);
                if (r == null || r.ViewerId != d.OwnerId) return false;   // #2 ต้อง agree
            }
            return true;
        }
    }
}
