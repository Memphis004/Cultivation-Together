using MessagePipe;
using VContainer.Unity;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// Opens persistent UI panels when CoreScene starts.
    /// These panels survive scene transitions (GameplayScene add/unload).
    ///
    /// Persistent panels (loaded here):        ///   - ResourceHud: top resource bar
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
        private readonly IPublisher<BuildModeStartedMessage> _buildStartedPublisher;
        private readonly IPublisher<BuildModeEndedMessage> _buildEndedPublisher;
        private System.IDisposable _subscription;

        public UIBootstrap(
            UIService uiService,
            ISubscriber<SceneLoadedMessage> sceneLoadedSubscriber,
            IPublisher<BuildModeStartedMessage> buildStartedPublisher,
            IPublisher<BuildModeEndedMessage> buildEndedPublisher)
        {
            _uiService = uiService;
            _sceneLoadedSubscriber = sceneLoadedSubscriber;
            _buildStartedPublisher = buildStartedPublisher;
            _buildEndedPublisher = buildEndedPublisher;
        }

        public void Start()
        {
            // --- Persistent UI (stays across scene transitions) ---
            _uiService.Open("ResourceHud");
            _uiService.Open("LogWindow");
            _uiService.Open("BottomMenu");
            // _uiService.Open("AvatarCustomization", new AvatarCustomizationPayload("d001"));

            WireResourcePopup();

            // Subscribe to scene loads for scene-specific UI setup if needed.
            // Currently scene-specific UI (EventPopup) is handled by
            // WorldEventUISystem, but this hook exists for future panels
            // that should open based on which scene just loaded.
            _subscription = _sceneLoadedSubscriber.Subscribe(OnSceneLoaded);

            WireBuildModeToggle();
        }

        /// <summary>
        /// ปุ่ม "สร้าง" บนแถบล่าง toggle เข้า/ออก build mode ผ่าน event bus -
        /// CameraRigController (Placement preset) และ GridOverlayRenderer
        /// ทั้งคู่ subscribe BuildModeStarted/EndedMessage อยู่แล้ว ระบบ ghost
        /// จริง (BuildingSystem) ยังไม่มี - เมื่อมีแล้วให้ส่ง GhostId มากับ
        /// BuildModeStartedMessage เพื่อให้กล้องโฟกัสตาม ghost
        /// </summary>
        private void WireBuildModeToggle()
        {
            var bottomMenu = _uiService.Open("BottomMenu");
            if (bottomMenu.Presenter is BottomMenuPresenter bottomPresenter &&
                bottomPresenter.BuildButton != null)
            {
                bottomPresenter.BuildButton.onClick.AddListener(ToggleBuildMode);
            }
        }

        private void ToggleBuildMode()
        {
            if (_buildModeActive)
            {
                _buildModeActive = false;
                _buildEndedPublisher.Publish(new BuildModeEndedMessage
                {
                    SourceId = "UIBootstrap",
                    Confirmed = false,
                });
            }
            else
            {
                _buildModeActive = true;
                _buildStartedPublisher.Publish(new BuildModeStartedMessage
                {
                    SourceId = "UIBootstrap",
                    GhostId = null, // ghost system (BuildingSystem) not built yet
                });
            }
        }

        private bool _buildModeActive;

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
