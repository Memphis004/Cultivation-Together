namespace Xianxia.Sect.UI
{
    /// <summary>payload ที่ส่งเข้า UIService.Open("AvatarCustomization", payload)</summary>
    public sealed class AvatarCustomizationPayload
    {
        public string DiscipleId;

        public AvatarCustomizationPayload(string discipleId)
        {
            DiscipleId = discipleId;
        }
    }

    /// <summary>ข้อมูลปุ่มแท็บ category (ใบหน้า / ลักษณะ / ร่างกาย)</summary>
    public struct AvatarCategoryTabViewData
    {
        public string Category;      // "ใบหน้า", "ลักษณะ", "ร่างกาย"
        public string DisplayName;
        public bool   IsActive;
    }

    /// <summary>ข้อมูลปุ่มแท็บ slot (body / head / hair / accessory)</summary>
    public struct AvatarSlotTabViewData
    {
        public string Slot;          // AvatarSlots.*
        public string DisplayName;   // "เสื้อผ้า", "ใบหน้า", ...
        public bool   IsActive;
    }

    /// <summary>ข้อมูลปุ่มชิ้นส่วน 1 ชิ้นในกริดด้านขวา</summary>
    public struct AvatarPartOptionViewData
    {
        public string PartId;        // "" = ถอด/ใช้ default
        public string DisplayName;
        public string SpritePath;    // path ใต้ Resources/ สำหรับ thumbnail
        public bool   IsSelected;
        public bool   IsLocked;      // เผื่ออนาคต: ปลดล็อกด้วย contribution
    }
}
