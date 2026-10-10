using UnityEngine;
using VContainer;
using VContainer.Unity;
using Xianxia.Sect.Installers;
using Xianxia.Sect.Tests;

namespace Xianxia.Sect
{
    // Composition root for the whole game framework (see the architecture
    // diagram: this is the "Game manager · VContainer composition root" box).
    // Registers the internal MessagePipe bus, the MessagePipe.Interprocess
    // TCP transport to the external MCP bridge process, and the four
    // gameplay subsystems as VContainer entry points.
    //
    // PHASE 1 refactor: ตัว registration ถูกแตกเป็น static installer extensions
    // ใน Core/Installers/ (Building/Visual/UI/Interprocess/Gameplay) — ไฟล์นี้
    // เหลือหน้าที่ Injector/Awake/OnDestroy + ลำดับการเรียก installer.
    // ⚠️ ลำดับการเรียกต้องตรงกับลำดับ registration เดิมทุกบรรทัด เพราะ VContainer
    // รัน entry point ตามลำดับ registration (BuildingPlacementUISystem ก่อน
    // ChibiFrameClock ตามเดิม เป็นต้น)
    public class GameLifetimeScope : LifetimeScope
    {
        /// <summary>
        /// Static access point to this scope's container (Editor verify tools only).
        /// Set in Awake, cleared in OnDestroy — never used by production gameplay code
        /// (those get constructor injection). Kept here instead of reflection so tools
        /// stay reflection-free (C12).
        /// </summary>
        public static IObjectResolver Injector { get; private set; }

        [SerializeField] private string interprocessHost = "127.0.0.1";
        [SerializeField] private int interprocessPort = 3215;

        [Header("UI (Xianxia.UI.MVP Lite)")]
        [SerializeField] private Xianxia.Sect.UI.UIRoot uiRoot;
        [SerializeField] private Xianxia.Sect.UI.UIPanelCatalog uiPanelCatalog;

        protected override void Awake()
        {
            base.Awake();
            Injector = Container;
            DontDestroyOnLoad(gameObject);
        }

        protected override void OnDestroy()
        {
            Injector = null;
            base.OnDestroy();
        }

        protected override void Configure(IContainerBuilder builder)
        {
            // Scene management
            // P12A — SceneLoader ไม่ auto-load เองอีกต่อไป (GameSessionCoordinator เป็น
            // เจ้าของ lifecycle). ยัง register เป็น entry point + AsSelf (Editor verify
            // runners resolve concrete) และเพิ่ม As<IGameplaySceneLoader> ให้ coordinator
            // ฉีด seam เดียวกัน — ยังมี loader ตัวเดียวในโปรเจกต์
            builder.RegisterEntryPoint<SceneLoader>(Lifetime.Singleton)
                   .AsSelf()
                   .As<IGameplaySceneLoader>();
            builder.RegisterComponentInHierarchy<AdditiveSceneTest>();
            // Design-time data now sourced from Luban (see DataTables/ at
            // the workspace root and Assets/Scripts/Data/LubanEventPool.cs),
            // not a ScriptableObject dragged into the Inspector - so this is
            // constructed directly rather than serialized.
            builder.RegisterInstance(new LubanEventPool());

            // Avatar part definitions loaded from Resources/Data/avatar_parts.json
            builder.Register<AvatarPartPool>(Lifetime.Singleton);

            // ลำดับด้านล่าง = ลำดับ registration เดิมทุกบรรทัด (Building → Visual → UI →
            // interprocess → rig ports/gameplay entries) — ห้ามสลับ (ดูหัวไฟล์)
            builder.RegisterBuildingSystem();
            builder.RegisterVisualSystem();
            builder.RegisterUI(uiRoot, uiPanelCatalog);
            builder.RegisterInterprocess(interprocessHost, interprocessPort);
            builder.RegisterGameplaySystems();
        }
    }
}
