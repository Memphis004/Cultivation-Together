using VContainer;
using VContainer.Unity;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect
{
    /// <summary>
    /// PHASE 2 refactor: child LifetimeScope ผูกกับฉากเกมเพลย์ SectScene
    /// ย้ายมาจาก GameLifetimeScope.Configure() ที่เคยอยู่ root.
    ///
    /// ของที่อยู่ที่นี่ = สิ่งที่ผูกกับฉากเท่านั้น (rig ports + กล้อง/overlay/backdrop)
    /// — ยืนยันด้วย grep ทั้งโปรเจกต์ (รวม Tests/ และ Editor/) แล้วว่าไม่มีใคร
    /// resolve สิ่งเหล่านี้จาก root: EditMode tests ใช้ fake ของตัวเอง
    /// (CameraRigControllerTests), verify runners ใช้แค่ ISectStateProvider/
    /// SceneLoader/UIService/visual core (root ยังอยู่ครบ)
    ///
    /// ⚠️ ข้อจำกัด: กด Play จากฉากนี้ "เดี่ยว ๆ" จะไม่มี parent scope → กล้อง/overlay/
    /// backdrop จะไม่ถูกสร้าง (ไม่มี MessagePipeRigBus ฯลฯ) — ต้อง Play จาก
    /// SampleScene เสมอ (SceneLoader จะ EnqueueParent แล้วโหลดฉากนี้ให้)
    ///
    /// Parent reference: ผูกผ่าน LifetimeScope.EnqueueParent(root) ใน SceneLoader
    /// ตอนโหลด — Parent Reference (Type) บน Inspector ปล่อยว่าง
    /// </summary>
    public sealed class SectSceneLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            // --- camera rig ports (test seams; see CameraRigPorts.cs) ---
            // The rig talks to the bus/scene/Resources/Time only through these
            // interfaces, so EditMode tests can drive it with fakes.
            // (ย้ายมาจาก GameLifetimeScope.Configure ตอน Phase 2 — ผูกกับฉาก)
            builder.Register<IRigMessageBus, MessagePipeRigBus>(Lifetime.Singleton);
            builder.Register<ICameraRigEnvironment, SceneEnvironment>(Lifetime.Singleton);
            builder.Register<ICellSpriteMetrics, ResourcesCellSpriteMetrics>(Lifetime.Singleton);
            builder.Register<IMainThreadQueue, UniTaskMainThreadQueue>(Lifetime.Singleton);
            builder.Register<IRigClock, UnityRigClock>(Lifetime.Singleton);
            // pan input (คลิกขวาค้าง+ลาก) — ผูกกับ Unity Input เฉพาะ play mode ผ่าน
            // CameraRigPanBootstrap ([RuntimeInitializeOnLoadMethod]); EditMode test
            // ไม่มี wire → CameraRigController ข้าม input ทั้งหมด (test-safe)

            // --- scene-bound entry points (กล้อง + grid overlay + backdrop) ---
            builder.RegisterEntryPoint<CameraRigController>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint<GridOverlayRenderer>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint<TerrainBackdropRenderer>(Lifetime.Singleton).AsSelf();
        }
    }
}
