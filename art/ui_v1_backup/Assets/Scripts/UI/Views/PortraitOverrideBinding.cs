using UnityEngine;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// Portrait v2: context component บอก <see cref="AvatarRenderer"/> ว่าตัวเองกำลัง
    /// render ศิษย์คนไหน เพื่อให้ renderer ปรึกษา <see cref="Visual.PortraitOverrideMap"/>
    /// ได้ (IPortraitVisual.Bind/SetAppearance รับแค่ AvatarAppearance — ไม่มี discipleId
    /// และห้ามเปลี่ยน signature เดิม)
    ///
    /// ไม่มี binding / ไม่มี discipleId → renderer ทำงานเป็น layered ล้วนตามเดิม
    /// (เช่น AvatarCustomization preview ที่ draft ไม่ผูกกับศิษย์คนใด)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PortraitOverrideBinding : MonoBehaviour
    {
        [SerializeField] private string discipleId = string.Empty; // เริ่มเป็น "" เสมอ — null เดินทางไม่ได้ผ่าน Equals/lookup

        public string DiscipleId
        {
            get { return discipleId; }
        }

        /// <summary>
        /// ตั้งค่าศิษย์ที่ renderer นี้แสดง — เรียกได้บ่อย (rail reuse);
        /// renderer สังเกตการเปลี่ยนเองผ่าน signature ไม่ต้อง notify
        /// </summary>
        public void SetDiscipleId(string id)
        {
            if (string.Equals(discipleId, id, System.StringComparison.Ordinal)) return;
            discipleId = id ?? string.Empty;
        }
    }
}
