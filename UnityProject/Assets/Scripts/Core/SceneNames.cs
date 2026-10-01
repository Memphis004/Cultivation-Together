namespace Xianxia.Sect
{
    /// <summary>
    /// ชื่อ/พาธของฉากที่โค้ดอ้างอิง — จุดเดียวที่ต้องแก้เมื่อฉากถูกเปลี่ยนชื่อ
    /// (Phase 3: rename ฉากเกมเพลย์เป็น SectScene)
    ///
    /// หมายเหตุ: TestGameplayScene2 คงชื่อเดิมโดยเจตนา — เป็นฉากทดสอบสำหรับ
    /// verify การสลับฉาก (scene swap) ไม่ใช่ฉากเกมเพลย์จริง จึงไม่อยู่ในคลาสนี้
    /// </summary>
    public static class SceneNames
    {
        /// <summary>ฉากเกมเพลย์จริง — child scope + กล้อง + overlay + backdrop อยู่ที่นี่</summary>
        public const string Sect = "SectScene";

        /// <summary>พาธ asset ของฉากเกมเพลย์ (สำหรับ Editor runner ที่ต้องเปิดไฟล์ตรง ๆ)</summary>
        public const string SectAssetPath = "Assets/Scenes/" + Sect + ".unity";
    }
}
