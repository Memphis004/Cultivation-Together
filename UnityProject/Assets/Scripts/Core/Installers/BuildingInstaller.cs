using VContainer;
using VContainer.Unity;
using Xianxia.Sect.Building;
using Xianxia.Sect.UI;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Installers
{
    /// <summary>
    /// PHASE 1 refactor: บล็อก "Building Phase 1" ย้ายมาจาก GameLifetimeScope.Configure()
    /// ครบทุกบรรทัด (comment ต้นฉบับคงเดิม) — ลำดับการ Register ต้องเรียกตามลำดับเดิม
    /// เพราะ VContainer รัน entry point ตามลำดับ registration
    /// (BuildingPlacementUISystem ต้อง start ก่อน ChibiFrameClock/DiscipleVisualSystem ตามเดิม)
    /// </summary>
    internal static class BuildingInstaller
    {
        public static void RegisterBuildingSystem(this IContainerBuilder builder)
        {
            // --- Building Phase 1 (grid placement engine — building-system.md) ---
            // Q1 default: defs from Resources/Data/building_defs.json (hand-written,
            // AvatarPartPool pattern); Q3 default: player-only, no interprocess registration
            // BuildingGrid มี ctor (int,int) ไว้ให้ test — VContainer เลือก ctor ที่มี
            // parameters มากที่สุดเสมอ (TypeAnalyzer) แล้วพยายาม resolve System.Int32
            // จึงต้อง pin ขนาดผ่าน factory ที่ composition root แทน:
            // 24×24 centered origin (−12,−12) — logical cell ตรงกับ GridOverlayRenderer
            // (cell (0,0) = กึ่งกลางแบ็คกราว, cell ติดลบได้ทั้งสองแกน)
            builder.Register<BuildingGrid>(
                resolver => new BuildingGrid(
                    GridOverlayRenderer.GridExtent,
                    GridOverlayRenderer.GridExtent,
                    -GridOverlayRenderer.GridExtent / 2,
                    -GridOverlayRenderer.GridExtent / 2),
                Lifetime.Singleton);
            builder.Register<BuildingDefPool>(Lifetime.Singleton);
            builder.Register<PlacementController>(Lifetime.Singleton);
            builder.Register<BuildingMenuPresenter>(Lifetime.Transient);
            // เจ้าของโหมดวางจริง (persistent) — ปุ่มลอย/BuildMode* message ต้องรอดจาก
            // การปิดเมนู (presenter เป็น Transient ตายพร้อมเมนู จึงถือ flow ไม่ได้)
            builder.RegisterEntryPoint<BuildingPlacementUISystem>(Lifetime.Singleton).AsSelf();
            // BuildingSystem entry point มีอยู่แล้ว (RegisterEntryPoint<BuildingSystem> ด้านล่าง) — ไม่เพิ่มซ้ำ
        }
    }
}
