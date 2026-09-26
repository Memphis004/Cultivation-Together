namespace Xianxia.Sect.UI
{
    public enum UIPresenterKind
    {
        EventPopup,
        ResourceHud,
        LogWindow,
        AvatarCustomization,
        DiscipleList, // not implemented yet - resolving this kind throws
        DiscipleDetail, // Phase 4 — click chibi → detail panel (appended last: catalog assets store this enum as int)
        BottomMenu, // persistent bottom bar (สร้าง / ศิษย์) — appended last to keep existing int values stable
        ResourcePopup, // คลังสินค้า popup (read-only stockpile) — appended last to keep existing int values stable
        BuildingMenu, // เมนูสร้างอาคาร (4 หมวด + กริดการ์ด) — appended last to keep existing int values stable
    }
}
