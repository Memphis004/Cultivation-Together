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

            // E1 — time/pause runtime config (plain-C# singleton, same convention
            // as VisualRuntimeConfig). Injected into TimeSystem so
            // AutoPauseOnDecisionEvent is a real config, not a hard-coded constant.
            builder.RegisterInstance(TimeRuntimeConfig.Instance);

            // E2-lite — world-event spawner config (same plain-C# singleton
            // convention). Injected into WorldEventSystem so the start grace is a
            // real config, not a hard-coded constant.
            builder.RegisterInstance(WorldEventRuntimeConfig.Instance);
            builder.RegisterEntryPoint<DecisionLogger>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint<WorldEventSystem>(Lifetime.Singleton).AsSelf();

            // P9B — utility AI. Re-tasks Auto NPC disciples on a simulation-time
            // interval, through the P9A auto-assignment entry point (ownership/Auto/
            // availability/cooldown re-checked at commit). Pure scoring lives in
            // AutoTaskScoring; this is only the Unity scheduler. Root-scoped, so it
            // runs independently of the loaded scene.
            builder.RegisterEntryPoint<AutoTaskScheduler>(Lifetime.Singleton).AsSelf();

            // P5B: real-time clock for the viewer inactivity/activity rule. Registered
            // explicitly (rather than relying on the ctor's default) so production and
            // tests both get a deterministic answer for what "now" means.
            builder.Register<Xianxia.Sect.IClock, Xianxia.Sect.UtcClock>(Lifetime.Singleton);

            // Aggregates the subsystems above into one SectEconomyState for
            // SectStateQueryHandler to serve. See ISectStateProvider.
            // P4: DiscipleOwnerChangedMessage publisher (in-memory only — ownership
            // assignment is a dev-harness concern until real account linking, P5B+).
            builder.Register<ISectStateProvider, Xianxia.Sect.SectStateProvider>(Lifetime.Singleton);

            // P4 observability: records DiscipleOwnerChangedMessage into a ring
            // buffer for the bridge's read-only get_ownership_log tool. ENTRY POINT
            // (not plain Register) so it is activated + subscribed at container
            // build — a lazy singleton would miss every change before the first
            // query. Singleton at ROOT (in-memory bus is root-scoped too).
            // Read-only — no write path.
            builder.RegisterEntryPoint<OwnershipObservabilityBuffer>(Lifetime.Singleton).AsSelf();

            // P5B observability: records DiscipleTaskChangedMessage into a ring
            // buffer for the bridge's read-only get_task_change_log tool. ENTRY
            // POINT for the same reason as the ownership buffer (a lazy singleton
            // would miss every change before the first query). Read-only.
            builder.RegisterEntryPoint<TaskChangeObservabilityBuffer>(Lifetime.Singleton).AsSelf();

            // P11A — the single orchestration owner of full-session restore. Registered
            // as ISessionRestoreAuthority so the membership-slice persistence below can
            // refuse its automatic import once a full session is authoritative (one
            // owner; no competing automatic restore). No save file I/O here — P11B adds
            // the storage layer on top of the same envelope.
            builder.Register<ISessionRestoreAuthority, SessionSnapshotService>(Lifetime.Singleton);

            // P11B — the local save slot. RegisterGameplaySystems() runs from
            // GameLifetimeScope.Configure (i.e. Awake), so this is the UNITY MAIN THREAD:
            // Application.persistentDataPath is resolved exactly here, once, and handed in
            // as data — the repository itself is plain C#/System.IO and runs on a worker
            // thread (Common-Rules 4: background I/O sees detached data only).
            // MVP scope: ONE manual slot + ONE backup + metadata for Continue (no
            // multi-slot browser, no cloud sync, no autosave, no Title UI yet).
            var savePaths = SaveSlotPaths.UnderPersistentDataPath();
            var saveFileSystem = new SystemSaveFileSystem();
            builder.RegisterInstance(savePaths);
            builder.RegisterInstance<ISaveFileSystem>(saveFileSystem);
            // SaveSlotRepository ยังมี ctor (fileSystem, paths, maxSaveFileBytes) ไว้ให้
            // test ปรับเพดานขนาดไฟล์ — VContainer เลือก ctor ที่มี parameters มากที่สุด
            // เสมอ (TypeAnalyzer) แล้วพยายาม resolve System.Int32 จึงต้อง pin ctor
            // ผ่าน factory ที่ composition root เหมือน BuildingGrid ไม่งั้น build scope ล้ม
            builder.Register<SaveSlotRepository>(
                resolver => new SaveSlotRepository(saveFileSystem, savePaths),
                Lifetime.Singleton);
            builder.Register<SessionTransitionTracker>(Lifetime.Singleton);
            builder.Register<SaveOperationGate>(Lifetime.Singleton);
            builder.Register<ISaveWorkScheduler, UnitySaveWorkScheduler>(Lifetime.Singleton);

            // P11B — the application service the (future) save/Continue UI calls. Lazy
            // singleton: nothing is read or written until something asks it to.
            builder.Register<SaveSessionService>(Lifetime.Singleton);

            // P5B persistence: restores the viewer-membership slice on Start and
            // re-saves it periodically + on scope dispose, so membership status and
            // LastActiveAtUtc survive a session. ENTRY POINT so the load happens at
            // container build (before anything can query protection). Local file
            // only — no MCP tool, nothing on the interprocess wire.
            // P11A: its import is gated by ISessionRestoreAuthority — a full-session
            // load always wins and the slice can never overwrite it.
            builder.RegisterEntryPoint<ViewerMembershipPersistenceSystem>(Lifetime.Singleton).AsSelf();

            // P12A — explicit game-session lifecycle (Title / StartingNewGame / LoadingGame /
            // Playing / ReturningToTitle) layered on the EXISTING SceneLoader and persistence
            // services. It does not create a second composition root or message bus.
            //
            // Registered LAST so every entry point it resets (AutoTaskScheduler) and every
            // buffer it clears is already constructed + subscribed before its own Start()
            // drives the boot session. Exposed as ISessionGate so the MCP mutation handlers
            // (registered above) can refuse calls outside Playing.
            builder.Register<IStarterStateFactory, PrototypeStarterStateFactory>(Lifetime.Singleton);
            builder.Register<ISessionUiCloser, SessionUiCloser>(Lifetime.Singleton);
            builder.RegisterEntryPoint<GameSessionCoordinator>(Lifetime.Singleton)
                   .AsSelf()
                   .As<IGameSessionCoordinator>()
                   .As<ISessionGate>();
        }
    }
}
