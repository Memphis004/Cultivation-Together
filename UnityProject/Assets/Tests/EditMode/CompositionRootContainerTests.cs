using System;
using NUnit.Framework;
using UnityEngine;
using VContainer;
using Xianxia.Sect.Installers;
using Xianxia.Sect.UI;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// Composition-root layout guard at the REAL DI-registration level (Phase 2 refactor).
    ///
    /// Complement ของ CompositionRootTests (ตัวนั้นสแกนซอร์สด้วย regex): ที่นี่เรียก
    /// installer ตัวจริง — <c>BuildingInstaller.RegisterBuildingSystem</c>,
    /// <c>VisualInstaller.RegisterVisualSystem</c>, <c>UIInstaller.RegisterUI</c>,
    /// <c>GameplayInstaller.RegisterGameplaySystems</c> — และ
    /// <c>SectSceneLifetimeScope.RegisterSceneBoundServices</c> ลงบน
    /// <see cref="ContainerBuilder"/> จริง แล้วตรวจด้วย <c>Exists</c> ว่า root
    /// "ไม่" register บริการที่ผูกกับฉาก แต่ child scope register ครบ
    ///
    /// ⚠️ ทำไมตรวจที่ registration ไม่ <c>Build()</c>: การ Build root จริงจะเปิด
    /// MessagePipe TCP interprocess listener (port 3215) และ dispatch entry point
    /// ทุกตัว (Start() ทำงานทันที — WorldEventSystem ยิง event, UIBootstrap แตะ
    /// UIRoot) ซึ่งไม่ควรเกิดใน EditMode test. <c>ContainerBuilder.Exists</c> อ่าน
    /// ชุด registration ที่ installer เขียนจริงโดยไม่ build — จึงครอบ invariant นี้
    /// ได้ตรงไปตรงมาและไม่มี side effect
    ///
    /// Invariant: บริการที่ผูกกับฉาก (rig ports + กล้อง/overlay/backdrop) ต้องอยู่ที่
    /// SectSceneLifetimeScope เท่านั้น — สำเนาที่ root เคยทำให้ entry point เกิด
    /// 2 instance ต่อการโหลดฉาก
    /// </summary>
    public class CompositionRootContainerTests
    {
        /// <summary>ต้องตรงกับ CompositionRootTests.SceneBoundTypes</summary>
        private static readonly Type[] SceneBoundTypes =
        {
            typeof(CameraRigController),
            typeof(GridOverlayRenderer),
            typeof(TerrainBackdropRenderer),
            typeof(IRigMessageBus),
            typeof(ICameraRigEnvironment),
            typeof(ICellSpriteMetrics),
            typeof(IMainThreadQueue),
            typeof(IRigClock),
        };

        // RegisterVisualSystem เขียน VisualRuntimeConfig (static) ค่า production —
        // snapshot/restore เพื่อไม่ให้รั่วไปโดนเทสต์อื่น
        private bool _spriteSheetEnabled;
        private bool _spineActivationRequested;
        private string _spineSkeletonResourcePath;
        private bool _devSpineOverride;

        // RegisterUI ต้องการ instance จริง (VContainer RegisterInstance(null) โยน NRE) —
        // สร้างชั่วคราวต่อเทสต์แล้วทำลายใน TearDown
        private GameObject _uiRootGo;
        private UIRoot _uiRoot;
        private UIPanelCatalog _uiPanelCatalog;

        [SetUp]
        public void SnapshotVisualConfig()
        {
            VisualRuntimeConfig cfg = VisualRuntimeConfig.Instance;
            _spriteSheetEnabled = cfg.SpriteSheetEnabled;
            _spineActivationRequested = cfg.SpineActivationRequested;
            _spineSkeletonResourcePath = cfg.SpineSkeletonResourcePath;
            _devSpineOverride = cfg.DevSpineOverride;

            _uiRootGo = new GameObject("CompositionTest_UIRoot");
            _uiRoot = _uiRootGo.AddComponent<UIRoot>();
            _uiPanelCatalog = ScriptableObject.CreateInstance<UIPanelCatalog>();
        }

        [TearDown]
        public void RestoreVisualConfig()
        {
            VisualRuntimeConfig cfg = VisualRuntimeConfig.Instance;
            cfg.SpriteSheetEnabled = _spriteSheetEnabled;
            cfg.SpineActivationRequested = _spineActivationRequested;
            cfg.SpineSkeletonResourcePath = _spineSkeletonResourcePath;
            cfg.DevSpineOverride = _devSpineOverride;

            if (_uiRootGo != null) UnityEngine.Object.DestroyImmediate(_uiRootGo);
            if (_uiPanelCatalog != null) UnityEngine.Object.DestroyImmediate(_uiPanelCatalog);
        }

        // ---- root = บริการระดับเกมเท่านั้น ----

        [Test]
        public void RootRegistrationSet_DoesNotRegisterAnySceneBoundService()
        {
            IContainerBuilder root = BuildRootRegistrationSet();

            foreach (Type type in SceneBoundTypes)
            {
                Assert.IsFalse(IsRegistered(root, type),
                    "root registration set ต้องไม่ register '" + type.Name + "' — " +
                    "ของที่ผูกกับฉากอยู่ที่ SectSceneLifetimeScope เท่านั้น");
            }
        }

        [Test]
        public void RootRegistrationSet_ActuallyRegistersRootOwnedServices()
        {
            // sanity: installer ทำงานจริง (ไม่ใช่ no-op) — กันเทสต์ด้านบนผ่านแบบหลอก
            IContainerBuilder root = BuildRootRegistrationSet();

            Assert.IsTrue(root.Exists(typeof(CameraFramingConfig)),
                "root ต้องมี CameraFramingConfig (GameplayInstaller)");
            Assert.IsTrue(root.Exists(typeof(SectStateProvider)),
                "root ต้องมี SectStateProvider (GameplayInstaller)");
            Assert.IsTrue(root.Exists(typeof(TimeSystem)),
                "root ต้องมี TimeSystem entry point (GameplayInstaller)");
        }

        // ---- child scope = บริการที่ผูกกับฉาก ----

        [Test]
        public void ChildScopeRegistrationSet_RegistersEverySceneBoundService()
        {
            IContainerBuilder child = new ContainerBuilder();
            SectSceneLifetimeScope.RegisterSceneBoundServices(child);

            foreach (Type type in SceneBoundTypes)
            {
                Assert.IsTrue(IsRegistered(child, type),
                    "SectSceneLifetimeScope ต้อง register '" + type.Name + "'");
            }
        }

        [Test]
        public void ChildScope_AddsSceneBoundServicesOnTopOfRoot()
        {
            IContainerBuilder combined = BuildRootRegistrationSet();
            SectSceneLifetimeScope.RegisterSceneBoundServices(combined);

            foreach (Type type in SceneBoundTypes)
            {
                Assert.IsTrue(IsRegistered(combined, type),
                    "root + child รวมกันต้องมี '" + type.Name + "' (child เป็นเจ้าของ)");
            }
        }

        [Test]
        public void SceneBoundServices_AreResolvableOnlyThroughTheChildScope()
        {
            // จำลองลำดับจริง: root installers มาก่อน (ผลคือ Exists=false) แล้ว
            // child scope เพิ่มเข้ามา (ผลคือ Exists=true) — ทั้งคู่บน builder เดียวกัน
            IContainerBuilder builder = new ContainerBuilder();
            builder.RegisterBuildingSystem();
            builder.RegisterVisualSystem();
            builder.RegisterUI(_uiRoot, _uiPanelCatalog);
            builder.RegisterGameplaySystems();

            Type rig = typeof(CameraRigController);
            Assert.IsFalse(IsRegistered(builder, rig),
                "ก่อน child scope: root ต้อง resolve กล้อง rig ไม่ได้");

            SectSceneLifetimeScope.RegisterSceneBoundServices(builder);
            Assert.IsTrue(IsRegistered(builder, rig),
                "หลัง child scope: ต้อง resolve กล้อง rig ได้");
        }

        // ---- helpers ----

        /// <summary>
        /// ชุด registration ของ root scope — 4 installer ที่ GameLifetimeScope.Configure
        /// เรียก (ข้าม RegisterInterprocess เจตนา: TCP transport ไม่เกี่ยวกับ
        /// บริการที่ผูกกับฉาก และเราไม่ build อยู่แล้ว)
        /// </summary>
        private IContainerBuilder BuildRootRegistrationSet()
        {
            var builder = new ContainerBuilder();
            builder.RegisterBuildingSystem();
            builder.RegisterVisualSystem();
            builder.RegisterUI(_uiRoot, _uiPanelCatalog);
            builder.RegisterGameplaySystems();
            return builder;
        }

        private static bool IsRegistered(IContainerBuilder builder, Type type)
        {
            // includeInterfaceTypes: interface registration (เช่น IRigMessageBus →
            // MessagePipeRigBus) เก็บ type จริงของ impl ไว้ที่ ImplementationType
            return builder.Exists(type, includeInterfaceTypes: true);
        }
    }
}
