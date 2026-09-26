using System;
using UnityEngine;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// Input seam สำหรับ pan กล้อง (คลิกขวาค้าง+ลาก) — static delegate ที่ composition
    /// root (GameLifetimeScope) wire ให้ play mode เท่านั้น; ยังไม่ wire = pan ปิด
    /// (EditMode test ไม่แตะ Input เลย — แพทเทิร์นเดียวกับ gate ของ VisualRuntimeConfig:
    /// การเปิดใช้ตัดสินใจที่ composition root จุดเดียว)
    /// </summary>
    public static class RigPanInput
    {
        public static Func<int, bool> GetMouseButton { get; private set; }
        public static Func<Vector3> MousePosition { get; private set; }

        public static bool IsWired => GetMouseButton != null && MousePosition != null;

        public static void Wire(Func<int, bool> getMouseButton, Func<Vector3> mousePosition)
        {
            GetMouseButton = getMouseButton;
            MousePosition = mousePosition;
        }

        public static void Unwire()
        {
            GetMouseButton = null;
            MousePosition = null;
        }
    }
}
