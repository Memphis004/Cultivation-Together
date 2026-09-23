using MessagePipe;
using VContainer.Unity;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// Opens persistent UI panels when CoreScene starts.
    /// These panels survive scene transitions (GameplayScene add/unload).
    ///
    /// Persistent panels (loaded here):
    ///   - ResourceHud: top resource bar
    ///   - LogWindow: decision/event log
    ///   - AvatarCustomization: avatar editor
    ///
    /// Scene-specific panels (loaded reactively, NOT here):
    ///   - EventPopup: opened by WorldEventUISystem when a world event fires
    ///   - DialoguePanel: opened when talking to an NPC
    /// </summary>
    public class UIBootstrap : IStartable
    {
        private readonly UIService _uiService;
        private readonly ISubscriber<SceneLoadedMessage> _sceneLoadedSubscriber;
        private System.IDisposable _subscription;

        public UIBootstrap(UIService uiService, ISubscriber<SceneLoadedMessage> sceneLoadedSubscriber)
        {
            _uiService = uiService;
            _sceneLoadedSubscriber = sceneLoadedSubscriber;
        }

        public void Start()
        {
            // --- Persistent UI (stays across scene transitions) ---
            _uiService.Open("ResourceHud");
            _uiService.Open("LogWindow");
            // _uiService.Open("AvatarCustomization", new AvatarCustomizationPayload("d001"));

            // Subscribe to scene loads for scene-specific UI setup if needed.
            // Currently scene-specific UI (EventPopup) is handled by
            // WorldEventUISystem, but this hook exists for future panels
            // that should open based on which scene just loaded.
            _subscription = _sceneLoadedSubscriber.Subscribe(OnSceneLoaded);
        }

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
