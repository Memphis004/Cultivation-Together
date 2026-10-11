using System.Collections.Generic;
using Xianxia.Sect.Messages;
using Xianxia.Sect.UI;

namespace Xianxia.Sect
{
    /// <summary>
    /// P12A (extended by P12B) — the production <see cref="ISessionUiCloser"/>. It closes the
    /// session's interactive panels through the existing <see cref="UIService"/> (each close runs
    /// the presenter's OnClose/Dispose and destroys the panel instance) and shows/hides the
    /// always-on session HUD as the phase changes.
    ///
    /// The always-on HUD panels (WalletHud / LogWindow / BottomMenu / TimeControl) are treated as
    /// PERSISTENT infrastructure: they are opened once by <c>UIBootstrap</c>, survive additive
    /// scene transitions by design, and their presenters hold the wiring to the bottom-menu
    /// buttons. Destroying them here would silently detach that wiring with no re-open path, so
    /// they are deliberately kept alive — P12B HIDES them while the phase is not Playing instead
    /// (see <see cref="SetSessionUiVisible"/>), which is the same trade-off one step further.
    ///
    /// <c>TitleMenu</c> is persistent for the same reason (it must outlive a Return to Title), but
    /// it is NOT part of the session HUD: it is the session-independent panel, and its own
    /// presenter owns its visibility. <see cref="SessionPhaseChangedMessage"/> is what drives both.
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
            "TitleMenu",
        };

        /// <summary>
        /// P12B — the subset of the persistent panels that is session HUD: visible while a
        /// session is Playing, hidden otherwise. <c>TitleMenu</c> is deliberately absent (it is
        /// shown exactly when this list is hidden); <c>IngameMenu</c> is deliberately absent
        /// because it is session-scoped interaction UI and is CLOSED with the session.
        /// </summary>
        public static readonly string[] SessionHudPanelIds =
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

        /// <summary>What <see cref="SetSessionUiVisible"/> last asked for (diagnostics/tests).</summary>
        public bool SessionUiVisible { get; private set; } = true;

        public int SetSessionUiVisible(bool visible)
        {
            SessionUiVisible = visible;
            if (_ui == null) return 0;

            int changed = 0;
            for (int i = 0; i < SessionHudPanelIds.Length; i++)
            {
                if (_ui.TrySetVisible(SessionHudPanelIds[i], visible)) changed++;
            }
            return changed;
        }
    }
}
