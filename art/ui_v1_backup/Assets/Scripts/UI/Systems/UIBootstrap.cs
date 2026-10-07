using MessagePipe;
using UnityEngine;
using VContainer.Unity;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// Opens persistent UI panels when CoreScene starts.
    /// These panels survive scene transitions (GameplayScene add/unload).
    ///
    /// Persistent panels (loaded here):        ///   - WalletHud: Sect Master wallet bar (SpiritStones + Contribution only)
        ///   - LogWindow: decision/event log
        ///   - AvatarCustomization: avatar editor
        ///   - BottomMenu: bottom bar (สร้าง / ศิษย์)
    ///
    /// Scene-specific panels (loaded reactively, NOT here):
    ///   - EventPopup: opened by WorldEventUISystem when a world event fires
    ///   - DialoguePanel: opened when talking to an NPC
    /// </summary>
    public class UIBootstrap : IStartable
    {
        private readonly UIService _uiService;
        private readonly ISubscriber<SceneLoadedMessage> _sceneLoadedSubscriber;
        private readonly BuildingPlacementUISystem _placementUI;
        private System.IDisposable _subscription;

        public UIBootstrap(
            UIService uiService,
            ISubscriber<SceneLoadedMessage> sceneLoadedSubscriber,
            BuildingPlacementUISystem placementUI)
        {
            _uiService = uiService;
            _sceneLoadedSubscriber = sceneLoadedSubscriber;
            _placementUI = placementUI;
        }

        public void Start()
        {
            // --- Persistent UI (stays across scene transitions) ---
            // WalletHud (formerly ResourceHud): top-left floating bar showing
            // only the Sect Master's personal wallet (SpiritStones +
            // Contribution). The 4 stockpile slots were removed - stockpile
            // numbers live in the warehouse ResourcePopup instead. Prefab
            // renamed WalletHudPrefab, PanelId renamed in MainPanelCatalog.
            _uiService.Open("WalletHud");
            _uiService.Open("LogWindow");
            _uiService.Open("BottomMenu");
            // _uiService.Open("AvatarCustomization", new AvatarCustomizationPayload("d001"));

            WireResourcePopup();
            WireDiscipleList();
            WireBuildModeToggle();

            // Subscribe to scene loads for scene-specific UI setup if needed.
            // Currently scene-specific UI (EventPopup) is handled by
            // WorldEventUISystem, but this hook exists for future panels
            // that should open based on which scene just loaded.
            _subscription = _sceneLoadedSubscriber.Subscribe(OnSceneLoaded);
        }

        /// <summary>
        /// ปุ่ม "สร้าง" บนแถบล่าง toggle เปิด/ปิด BuildingMenu (Building Phase 1) —
        /// ใช้ event BuildClicked เดิมของ BottomMenuPresenter (ไม่สร้างปุ่มใหม่).
        /// BuildModeStarted/EndedMessage publish จาก BuildingMenuPresenter เอง
        /// (ครอบทุก path ที่เมนูปิด: เลือกการ์ด / ปุ่มปิดใน panel / toggle ปุ่มนี้) —
        /// CameraRigController (Placement preset) และ GridOverlayRenderer ยัง
        /// subscribe ข้อความเดิมเหมือนเดิม
        /// </summary>
        private void WireBuildModeToggle()
        {
            var bottomMenu = _uiService.Open("BottomMenu");
            if (bottomMenu.Presenter is BottomMenuPresenter bottomPresenter)
            {
                bottomPresenter.BuildClicked += ToggleBuildMode;
            }
        }

        private void ToggleBuildMode()
        {
            // UIService.Open dedupes by panelId; closing runs presenter.Dispose
            // which cancels any live ghost + publishes BuildModeEnded (presenter-owned)
            if (_buildMenuOpen)
            {
                _uiService.Close("BuildingMenu");
                _buildMenuOpen = false;
                // toggle ปิดระหว่างวางค้าง = ยกเลิก ghost + เก็บปุ่มลอยด้วย
                // (flow การวางอยู่กับ persistent system ไม่ใช่ presenter)
                if (_placementUI != null) _placementUI.OnBuildModeEndedExternally();
            }
            else
            {
                try
                {
                    _uiService.Open("BuildingMenu", new BuildingMenuArgs
                    {
                        CloseCallback = OnBuildingMenuCloseRequested,
                    });
                    _buildMenuOpen = true;
                }
                catch (System.Exception ex)
                {
                    // BuildingMenu ยังไม่อยู่ใน catalog (ครั้งแรกหลัง merge — ลืมรัน
                    // generator) — แจ้งวิธีแก้ตรง ๆ แทน exception พังกลาง play mode
                    Debug.LogWarning("[UIBootstrap] BuildingMenu panel is not in the catalog yet - " +
                                     "run menu: Xianxia → Generate BuildingMenu Panel (once, in edit mode). " +
                                     "(" + ex.Message + ")");
                }
            }
        }

        private void OnBuildingMenuCloseRequested()
        {
            // BuildingMenuPresenter ขอปิดเมนูเอง (เลือกการ์ดสำเร็จ / ปุ่มปิดใน panel) —
            // ปิดผ่าน UIService จริงเหมือน ResourcePopup/DiscipleList (ไม่งั้นเมนูค้าง
            // ทับ placement mode — bug ที่เคยเกิด: เลือกการ์ดแล้วเมนูยังเปิดค้าง)
            _uiService.Close("BuildingMenu");
            _buildMenuOpen = false;
        }

        private bool _buildMenuOpen;

        /// <summary>
        /// Resource popup (คลังสินค้า) is opened by the bottom-menu warehouse
        /// button and closed by its own close button. The popup itself stays
        /// scene-agnostic: this bootstrap only supplies the open/close
        /// callbacks - no data-layer involvement (read-only stockpile).
        /// </summary>
        private void WireResourcePopup()
        {
            var bottomMenu = _uiService.Open("BottomMenu");
            if (bottomMenu.Presenter is BottomMenuPresenter bottomPresenter &&
                bottomPresenter.WarehouseButton != null)
            {
                bottomPresenter.WarehouseButton.onClick.AddListener(ToggleResourcePopup);
            }
        }

        private void ToggleResourcePopup()
        {
            // UIService.Open dedupes by panelId (re-Shows the existing
            // instance), so toggling is: open if missing, close if present.
            if (_resourcePopupOpen)
            {
                _uiService.Close("ResourcePopup");
                _resourcePopupOpen = false;
            }
            else
            {
                _uiService.Open("ResourcePopup", new ResourcePopupArgs
                {
                    CloseCallback = OnResourcePopupCloseRequested,
                });
                _resourcePopupOpen = true;
            }
        }

        private void OnResourcePopupCloseRequested()
        {
            _uiService.Close("ResourcePopup");
            _resourcePopupOpen = false;
        }

        private bool _resourcePopupOpen;

        /// <summary>
        /// Disciple list ("ศิษย์") is opened by the bottom-menu disciple button
        /// and closed by its own close button — wire pattern identical to
        /// WireResourcePopup: subscribe DiscipleClicked, toggle open/close, and
        /// pass DiscipleListArgs with a CloseCallback so the panel's own close
        /// button closes through UIService while _open stays in sync.
        /// </summary>
        private void WireDiscipleList()
        {
            var bottomMenu = _uiService.Open("BottomMenu");
            if (bottomMenu.Presenter is BottomMenuPresenter bottomPresenter)
            {
                // DiscipleClicked มีอยู่แล้วใน presenter และไม่มีใครฟัง —
                // ไม่เพิ่ม property ปุ่มใหม่ (ต่างจาก WarehouseButton ที่เป็น
                // placeholder ยังไม่มี event คู่)
                bottomPresenter.DiscipleClicked += ToggleDiscipleList;
            }
        }

        private void ToggleDiscipleList()
        {
            // UIService.Open dedupes by panelId (re-Shows the existing
            // instance), so toggling is: open if missing, close if present.
            if (_discipleListOpen)
            {
                _uiService.Close("DiscipleList");
                _discipleListOpen = false;
            }
            else
            {
                _uiService.Open("DiscipleList", new DiscipleListArgs
                {
                    CloseCallback = OnDiscipleListCloseRequested,
                });
                _discipleListOpen = true;
            }
        }

        private void OnDiscipleListCloseRequested()
        {
            _uiService.Close("DiscipleList");
            _discipleListOpen = false;
        }

        private bool _discipleListOpen;

        private void OnSceneLoaded(SceneLoadedMessage msg)
        {
            // Future: open scene-specific panels here based on msg.SceneName
            // e.g., if (msg.SceneName == "SectHall") _uiService.Open("BuildingPanel");
        }

        public void Dispose()
        {
            _subscription?.Dispose();
        }
    }
}
