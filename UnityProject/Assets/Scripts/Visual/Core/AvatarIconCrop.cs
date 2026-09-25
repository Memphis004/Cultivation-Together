using UnityEngine;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// Crop ของ portrait canvas สำหรับ "head-and-shoulders" icon (การ์ด DiscipleList)
    /// นิยามเป็น <b>normalized พิกัดบน canvas ของ sprite</b> — ตั้งใจไม่ผูกกับหน่วย local
    /// ของ <c>layerRoot</c> (pivotOffset/scale ของ <c>AvatarFraming.Bust</c> ขึ้นกับขนาด rect
    /// ของ AvatarRoot + letterbox จาก preserveAspect ของ layer Image ใน DiscipleDetail
    /// จึง <b>ห้ามพอร์ตเลข pixel ของ preset มาใช้ตรง ๆ</b> — ดู §1 ของแผน)
    ///
    /// ระบบพิกัด: (0,0) = มุม<b>ล่างซ้าย</b>ของ canvas, (1,1) = มุมบนขวา (y-up เหมือน
    /// invariant "head-center = (128, 262)" ของ baker — head-center ที่ normalized
    /// คือ (0.5, 0.6823) บน canvas 256×384 ทุกไฟล์)
    ///
    /// รูปทรง: crop เป็น <b>สี่เหลี่ยมจัตุรัสในหน่วย pixel ของ source canvas</b>
    /// (กว้าง x คูณ W, สูง y คูณ H ต้องเท่ากัน — คัดลอกตรงไปยังปลายทางจัตุรัส
    /// โดยไม่ยืดสัดส่วน) ขนาด 200×200px บน canvas อ้างอิง 256×384:
    /// ครอบหัวเต็ม (y 206..346) + ไหล่/ปกเสื้อ (y ~148..206) ตัดข้างพองาม
    /// </summary>
    public static class AvatarIconCrop
    {
        /// <summary>Aspect ratio ของ source canvas (portrait placeholder 256×384 = 2:3)</summary>
        public const float SourceAspect = 2f / 3f;

        /// <summary>Aspect ratio ของปลายทาง (การ์ด RT สี่เหลี่ยมจัตุรัส)</summary>
        public const float TargetAspect = 1f;

        /// <summary>ขนาดด้านของ crop ในหน่วย pixel ของ canvas อ้างอิง 256×384</summary>
        public const float CropSidePx = 200f;

        /// <summary>ขอบบนของ crop (normalized, y-up) — เผื่อมงกุฎ/ปิ่น (accessory วาดถึง y≈340 → 0.885)</summary>
        public const float CropTop = 0.92f;

        /// <summary>ความกว้าง canvas อ้างอิงของ placeholder portrait</summary>
        public const float RefCanvasW = 256f;

        /// <summary>ความสูง canvas อ้างอิงของ placeholder portrait</summary>
        public const float RefCanvasH = 384f;

        /// <summary>
        /// Crop rect แบบ normalized บน canvas ของ sprite (y-up, จัตุรัสใน pixel):
        /// ด้าน 200px, ขอบบน y=0.92, กึ่งกลางแนวนอนบน head-center x=0.5 —
        /// บน canvas 256×384 = x 28..228px, y 154..353px
        /// </summary>
        public static Rect NormalizedRect()
        {
            var w = CropSidePx / RefCanvasW;   // เศษของความกว้าง canvas
            var h = CropSidePx / RefCanvasH;   // เศษของความสูง canvas (w*W == h*H == 200px)
            var cx = 0.5f;                     // head-center x
            return new Rect(cx - w * 0.5f, CropTop - h, w, h);
        }

        /// <summary>
        /// จุดอ้างอิง "หัวกลาง" (invariant ทุกไฟล์ วาดเสมอที่ (128, 262) บน canvas 256×384)
        /// แปลงเป็น normalized — ใช้ตรวจว่า crop ไม่เพี้ยน
        /// </summary>
        public static Vector2 HeadCenterNormalized()
        {
            return new Vector2(128f / RefCanvasW, 262f / RefCanvasH);
        }

        /// <summary>
        /// Sanity check ที่ใช้ร่วมกัน (baker + tests): head-center ต้องอยู่ใน crop —
        /// ถ้า crop เพี้ยนหัวหาย ให้ปรับค่า const ข้างบนเท่านั้น
        /// </summary>
        public static bool ContainsHeadCenter()
        {
            var hc = HeadCenterNormalized();
            var rect = NormalizedRect();
            return rect.xMin <= hc.x && hc.x <= rect.xMax
                                      && rect.yMin <= hc.y && hc.y <= rect.yMax;
        }
    }
}
