using VContainer;
using VContainer.Unity;
using Xianxia.Sect.UI;

namespace Xianxia.Sect.Installers
{
    /// <summary>
    /// PHASE 1 refactor: บล็อก "UI (Xianxia.UI.MVP Lite)" ย้ายมาจาก
    /// GameLifetimeScope.Configure() ครบทุกบรรทัด — uiRoot/uiPanelCatalog
    /// ยังเป็น serialized field บน GameLifetimeScope (อ้างอิงอยู่ใน SampleScene)
    /// จึงส่งเข้ามาเป็นพารามิเตอร์แทนการย้าย field
    /// </summary>
    internal static class UIInstaller
    {
        public static void RegisterUI(
            this IContainerBuilder builder,
            Xianxia.Sect.UI.UIRoot uiRoot,
            UIPanelCatalog uiPanelCatalog)
        {
            // --- UI (Xianxia.UI.MVP Lite) ---
            // SectHudView is gone - replaced by the WalletHud panel below,
            // which gets its data from SectResourceChangedMessage instead of
            // polling ISectStateProvider on a timer.
            builder.RegisterInstance(uiRoot);
            builder.RegisterInstance(uiPanelCatalog);
            builder.Register<UIService>(Lifetime.Singleton);
            builder.Register<DecisionExecutor>(Lifetime.Singleton);
            builder.Register<EventPopupPresenter>(Lifetime.Transient);
            builder.Register<WalletHudPresenter>(Lifetime.Transient);
            builder.Register<LogWindowPresenter>(Lifetime.Transient);
            builder.Register<AvatarCustomizationPresenter>(Lifetime.Transient);
            builder.Register<Xianxia.Sect.Visual.TaskActivityMapper>(Lifetime.Singleton); // Phase 4: CurrentTask→activity (data-driven)
            builder.Register<DiscipleDetailPresenter>(Lifetime.Transient); // Phase 4: click chibi → detail
            builder.Register<BottomMenuPresenter>(Lifetime.Transient); // persistent bottom bar (สร้าง / ศิษย์)
            builder.Register<ResourcePopupPresenter>(Lifetime.Transient); // คลังสินค้า popup (read-only stockpile)
            builder.Register<DiscipleListPresenter>(Lifetime.Transient); // รายชื่อศิษย์ (cards + baked icons)
            builder.Register<TaskAssignmentPresenter>(Lifetime.Transient); // P3: มอบหมายงาน (draft/confirm)
            builder.RegisterEntryPoint<DiscipleDetailUISystem>(Lifetime.Singleton); // Phase 4: message → panel
            builder.RegisterEntryPoint<WorldEventUISystem>(Lifetime.Singleton);
            builder.RegisterEntryPoint<UIBootstrap>(Lifetime.Singleton);
        }
    }
}
