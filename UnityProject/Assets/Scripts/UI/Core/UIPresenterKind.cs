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
        TaskAssignment, // P3: มอบหมายงานศิษย์ (draft/confirm) — appended last to keep existing int values stable
        TimeControl, // E1: แถบควบคุมเวลา (pause/1x/2x/3x — E1.1 ตัดปุ่ม Play ออก) — appended last to keep existing int values stable
        EventChip, // E3: ชิป "มีเหตุการณ์รอตัดสินใจ" ใต้แถบเวลา — appended last to keep existing int values stable
    }
}
