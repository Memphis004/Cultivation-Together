using VContainer;
using VContainer.Unity;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Installers
{
    /// <summary>
    /// PHASE 1 refactor: gameplay subsystem entry points + ISectStateProvider
    /// ย้ายมาจาก GameLifetimeScope.Configure() ครบทุกบรรทัด — หมายเหตุ Phase 2:
    /// camera rig ports (test seams) + entry points ที่ผูกกับฉาก (กล้อง/overlay/
    /// backdrop) ย้ายไป SectSceneLifetimeScope แล้ว (เดิมอยู่ root ทำให้ entry point
    /// เกิด 2 instance ต่อการโหลดฉาก — log/action ทำงานซ้ำ)
    /// </summary>
    internal static class GameplayInstaller
    {
        public static void RegisterGameplaySystems(this IContainerBuilder builder)
        {
            // --- gameplay subsystems, started/ticked by VContainer ---
            // (camera rig ports + scene-bound entry points ย้ายไป
            // SectSceneLifetimeScope แล้ว — เดิมค้างอยู่ที่ root ทำให้เกิด
            // instance คู่ต่อการโหลดฉาก: log/transition/backdrop ทำงานซ้ำ)
            builder.RegisterEntryPoint<TimeSystem>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint<DiscipleSystem>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint<ResourceCraftingSystem>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint<Xianxia.Sect.BuildingSystem>(Lifetime.Singleton).AsSelf();

            // Camera framing: plain-C# config (task forbids ScriptableObject
            // here); sprite/PPU/grid values are measured at runtime.
            builder.Register<CameraFramingConfig>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint<DecisionLogger>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint<WorldEventSystem>(Lifetime.Singleton).AsSelf();

            // Aggregates the subsystems above into one SectEconomyState for
            // SectStateQueryHandler to serve. See ISectStateProvider.
            builder.Register<ISectStateProvider, Xianxia.Sect.SectStateProvider>(Lifetime.Singleton);
        }
    }
}
