using VContainer.Unity;

namespace Xianxia.Sect.UI
{
    // Opens the always-visible HUD panel at game start. EventPopup doesn't
    // need this - it opens reactively via WorldEventUISystem instead.
    public class UIBootstrap : IStartable
    {
        private readonly UIService _uiService;

        public UIBootstrap(UIService uiService)
        {
            _uiService = uiService;
        }

        public void Start()
        {
            _uiService.Open("ResourceHud");
            _uiService.Open("LogWindow");
            // ทดสอบเปิดหน้าจอแต่งตัวศิษย์ d001
            _uiService.Open("AvatarCustomization", new AvatarCustomizationPayload("d001"));
        }
    }
}
