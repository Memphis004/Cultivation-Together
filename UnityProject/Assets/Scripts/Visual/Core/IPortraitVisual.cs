using UnityEngine;
using Xianxia.Sect.UI; // AvatarFraming ประกาศใน AvatarRenderer.cs (UI namespace) — same assembly

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// UI (Portrait) side of the visual system (แผน §5/§6.1).
    /// Phase 1: AvatarRenderer implements this; Bind/SetFraming map ตรงเข้า
    /// SetAppearance/SetFraming เดิม — ไม่เปลี่ยนภาพ (C2)
    /// </summary>
    public interface IPortraitVisual
    {
        void Bind(AvatarAppearance appearance);
        void SetFraming(AvatarFraming framing);
    }
}
