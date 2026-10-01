using System;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// Input seam สำหรับ mouse-wheel zoom กล้อง — static delegate ที่ composition
    /// root wire ให้ play mode เท่านั้น (แพทเทิร์นเดียวกับ RigPanInput); ยังไม่ wire =
    /// zoom ปิด (EditMode test ไม่แตะ UnityEngine.Input เลย และยังคุมได้ด้วย fake
    /// ผ่าน Wire ตัวเดียวกัน)
    /// </summary>
    public static class RigZoomInput
    {
        /// <summary>ค่า scroll ดิบของเฟรมนี้ (เช่น Input.mouseScrollDelta.y) —
        /// บวก = zoom เข้า (เห็นน้อยลง), ลบ = zoom ออก</summary>
        public static Func<float> GetScrollDelta { get; private set; }

        public static bool IsWired => GetScrollDelta != null;

        public static void Wire(Func<float> scrollDelta)
        {
            GetScrollDelta = scrollDelta;
        }

        public static void Unwire()
        {
            GetScrollDelta = null;
        }
    }
}
