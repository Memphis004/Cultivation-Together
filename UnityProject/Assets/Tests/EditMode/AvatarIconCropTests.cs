using NUnit.Framework;
using UnityEngine;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// §1 Baker — crop math ต้องอิง normalized canvas coords และคง invariant
    /// head-center (128, 262 y-up บน canvas 256×384) อยู่ใน crop เสมอ:
    /// ถ้าเทสต์พัง = crop เพี้ยน หัวหาย — แก้ const ใน AvatarIconCrop เท่านั้น
    /// (ห้ามพอร์ต pivotOffset/scale ของ AvatarFraming.Bust ซึ่งเป็นหน่วย local
    /// ของ layerRoot ที่ขึ้นกับขนาด rect ของ AvatarRoot + letterbox)
    /// </summary>
    public class AvatarIconCropTests
    {
        [Test]
        public void HeadCenter_MatchesBakerInvariant()
        {
            var hc = AvatarIconCrop.HeadCenterNormalized();
            Assert.AreEqual(0.5f, hc.x, 0.0001f, "head-center x = 128/256");
            Assert.AreEqual(262f / 384f, hc.y, 0.0001f, "head-center y = 262/384 (y-up)");
        }

        [Test]
        public void CropRect_ContainsHeadCenter()
        {
            Assert.IsTrue(AvatarIconCrop.ContainsHeadCenter(),
                "square crop must contain the head-center invariant (128, 262)");
        }

        [Test]
        public void Crop_IsSquareInSourcePixels()
        {
            // "จัตุรัส" ต้องวัดใน pixel ของ source canvas (x คูณ W, y คูณ H)
            // — normalized space ล้วน ๆ จะ x=คูณ 256 แต่ y=คูณ 384 ไม่เท่ากัน
            var crop = AvatarIconCrop.NormalizedRect();
            var widthPx = crop.width * AvatarIconCrop.RefCanvasW;
            var heightPx = crop.height * AvatarIconCrop.RefCanvasH;

            Assert.AreEqual(AvatarIconCrop.CropSidePx, widthPx, 0.001f);
            Assert.AreEqual(AvatarIconCrop.CropSidePx, heightPx, 0.001f);
        }

        [Test]
        public void Crop_KeepsVerticalSpan()
        {
            var crop = AvatarIconCrop.NormalizedRect();

            // ครอบหัว→กลางอก: ยอด crop คงที่ (เผื่อ accessory), ก้นต่ำกว่าหัว
            Assert.AreEqual(AvatarIconCrop.CropTop, crop.yMax, 0.0001f);
            Assert.Less(crop.yMin, AvatarIconCrop.HeadCenterNormalized().y);
            Assert.Greater(crop.yMax, AvatarIconCrop.HeadCenterNormalized().y);
        }

        [Test]
        public void SquareCrop_OnSourceCanvas_CoversHeadShoulders()
        {
            // แปลง crop เป็นพิกัด pixel บน placeholder canvas 256×384:
            // กว้าง/สูงต้องเท่ากันในหน่วย pixel (จัตุรัสจริง ไม่ยืดสัดส่วน)
            var square = AvatarIconCrop.NormalizedRect();
            var heightPx = square.height * AvatarIconCrop.RefCanvasH;
            var widthPx = square.width * AvatarIconCrop.RefCanvasW;

            Assert.AreEqual(heightPx, widthPx, 0.5f, "square on the destination = heightPx == widthPx on 2:3 canvas");

            // ตำแหน่งหัวในเฟรม: bust/head-and-shoulders ปกติวางตาอยู่ราว 55-60%
            // ของความสูงเฟรม (ไม่ใช่ 1/3 บนแบบ full-body) — bound นี้กันสองขั้ว:
            // หัวโดนตัดขอบบน (relY < 0.3) และเฟรมกลางเป็นอก/ทอร์โซ (relY > 0.65)
            var hc = AvatarIconCrop.HeadCenterNormalized();
            var relY = (hc.y - square.yMin) / square.height;
            Assert.Greater(relY, 0.3f, "head too close to the top edge — risk of cutting hair/accessory");
            Assert.Less(relY, 0.65f, "head too low — crop centers the torso instead of head-and-shoulders");
        }
    }
}
