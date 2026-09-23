using UnityEngine;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// In-scene chibi visual (plan §5) — implemented per backend.
    /// Phase 2: SpriteChibiVisual. Phase 3: SpineChibiVisual.
    ///
    /// Phase 3 additions: the read-side surface (Backend/DiscipleId/SortingBase/
    /// FacingRight/CurrentActivity) needed for in-place backend swaps — preserve
    /// position/activity/facing/sorting across promote/demote without respawn.
    /// CurrentActivity stores the ACTIVITY (idle/walk/talk — animation state, never
    /// PoseId, L5/L6), so an in-place swap re-resolves it on the new backend.
    /// </summary>
    public interface IChibiVisual : System.IDisposable
    {
        VisualBackend Backend { get; }
        Transform Transform { get; }

        /// <summary>Disciple this visual renders — diagnostics/in-place-swap state reads.</summary>
        string DiscipleId { get; }

        /// <summary>Sorting base this visual was given — preserved across in-place swaps.</summary>
        int SortingBase { get; }

        /// <summary>true = facing +X. Flip lives at parent localScale.x (Sprite) / Skeleton.ScaleX (Spine) — L8.</summary>
        bool FacingRight { get; }

        /// <summary>Current activity name (animation state — never PoseId, L6). Preserved across in-place swaps.</summary>
        string CurrentActivity { get; }

        /// <summary>Full apply — (re)build every layer from appearance.</summary>
        void Bind(string discipleId, AvatarAppearance appearance, DiscipleSex sex);

        /// <summary>Incremental slot update (from AvatarEquipmentChangedMessage) — must NOT respawn the whole visual.</summary>
        void ApplySlot(string slot, string partId);

        /// <summary>Activity (idle/walk/talk…) is an animation state — never PoseId (L6).</summary>
        void SetActivity(string activityState);

        void SetFacing(bool faceRight);
        void SetSortingBase(int order);
    }
}
