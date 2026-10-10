using System.Collections.Generic;
using Xianxia.Sect.Messages;
using Xianxia.Sect.UI;

namespace Xianxia.Sect
{
    /// <summary>
    /// P12A — the production <see cref="ISessionUiCloser"/>. It closes the session's
    /// interactive panels through the existing <see cref="UIService"/> (each close runs the
    /// presenter's OnClose/Dispose and destroys the panel instance).
    ///
    /// The always-on HUD panels (WalletHud / LogWindow / BottomMenu / TimeControl) are
    /// treated as PERSISTENT infrastructure: they are opened once by <c>UIBootstrap</c>,
    /// survive additive scene transitions by design, and their presenters hold the wiring
    /// to the bottom-menu buttons. Destroying them here would silently detach that wiring
    /// with no re-open path (there is no Title prefab yet), so they are deliberately kept
    /// alive. Every other open panel — the transient, session-scoped interaction UI — is
    /// closed. <see cref="SessionPhaseChangedMessage"/> is the hook a future Title UI can
    /// subscribe to for its own visibility.
    /// </summary>
    public sealed class SessionUiCloser : ISessionUiCloser
    {
        /// <summary>Panel ids that are persistent infrastructure, never closed by a session end.</summary>
        public static readonly string[] PersistentPanelIds =
        {
            "WalletHud",
            "LogWindow",
            "BottomMenu",
            "TimeControl",
        };

        private static readonly HashSet<string> PersistentPanels =
            new HashSet<string>(PersistentPanelIds);

        private readonly UIService _ui;

        public SessionUiCloser(UIService ui)
        {
            _ui = ui;
        }

        public int CloseSessionUi()
        {
            return _ui == null ? 0 : _ui.CloseAllExcept(PersistentPanels);
        }
    }
}
