using System;
using Cysharp.Threading.Tasks;
using MessagePipe;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.UI
{
    /// <summary>P12B — open args for the in-game menu (<c>UIService.Open("IngameMenu", args)</c>).</summary>
    public sealed class IngameMenuArgs
    {
        /// <summary>The panel asked to close (its own X button); the owner closes it through UIService.</summary>
        public Action CloseCallback;
    }

    /// <summary>
    /// P12B §3 — the in-game menu: Save and Return to Title, wired ONLY to the P11/P12A services
    /// (<see cref="SaveSessionService"/> and <see cref="IGameSessionCoordinator"/>). It invents no
    /// persistence and no scene handling of its own.
    ///
    /// Return to Title ALWAYS asks for confirmation — there is deliberately no dirty-state
    /// tracking (§3), so the prompt is unconditional rather than "smart". The prompt lives inside
    /// this panel, so no second modal and no Esc binding is involved (the event popup already owns
    /// Escape).
    ///
    /// Save reports success or the failure reason from the P11B result (never a bare "failed"), and
    /// every button is dead while a transition is in flight.
    /// </summary>
    public class IngameMenuPresenter : UIPresenter<IngameMenuView>
    {
        public const string SavingLabel = "กำลังบันทึก...";
        public const string ReturningLabel = "กำลังกลับหน้าจอชื่อเกม...";
        public const string ReturnToTitlePromptLabel = "กลับหน้าจอชื่อเกม? ความคืบหน้าที่ไม่ได้บันทึกจะหายไป";
        public const string SaveSuccessFormat = "บันทึกสำเร็จ ({0} ศิษย์ · {1} อาคาร)";
        public const string SaveFailurePrefix = "บันทึกไม่สำเร็จ: ";
        public const string NoSaveServiceLabel = "บันทึกไม่ได้ — ไม่พบบริการบันทึก";
        public const string NoSessionServiceLabel = "กลับหน้าจอชื่อเกมไม่ได้ — ไม่พบบริการเซสชัน";

        private readonly SaveSessionService _saves;
        private readonly IGameSessionCoordinator _coordinator;
        private readonly ISubscriber<SessionPhaseChangedMessage> _phaseSubscriber;

        private IDisposable _phaseSubscription;
        private Action _closeCallback;
        private bool _busy;
        private bool _confirming;

        public IngameMenuPresenter(
            SaveSessionService saves = null,
            IGameSessionCoordinator coordinator = null,
            ISubscriber<SessionPhaseChangedMessage> phaseSubscriber = null)
        {
            _saves = saves;
            _coordinator = coordinator;
            _phaseSubscriber = phaseSubscriber;
        }

        public override void OnOpen(object args)
        {
            _closeCallback = (args as IngameMenuArgs)?.CloseCallback;
            _busy = false;
            _confirming = false;
            View.SetStatus(string.Empty);
            Refresh();
        }

        protected override void OnViewBound()
        {
            View.SaveClicked += HandleSaveClicked;
            View.ReturnToTitleClicked += HandleReturnToTitleClicked;
            View.CloseClicked += HandleCloseClicked;
            View.ConfirmYesClicked += HandleConfirmYesClicked;
            View.ConfirmNoClicked += HandleConfirmNoClicked;
            View.WireButtons();

            _phaseSubscription = _phaseSubscriber?.Subscribe(_ => Refresh());
            Refresh();
        }

        // ---------- handlers (the view's real path) ----------

        private void HandleSaveClicked() => RunSaveAsync().Forget();

        private void HandleReturnToTitleClicked()
        {
            if (IsTransitioning) return;

            // §3 — ALWAYS ask. No dirty check, no "saved recently" shortcut.
            _confirming = true;
            View.SetStatus(ReturnToTitlePromptLabel);
            Refresh();
        }

        private void HandleConfirmYesClicked() => RunReturnToTitleAsync().Forget();

        private void HandleConfirmNoClicked()
        {
            if (_busy) return;
            _confirming = false;
            View.SetStatus(string.Empty);
            Refresh();
        }

        private void HandleCloseClicked()
        {
            if (IsTransitioning) return; // a click during a transition cannot close the panel
            _closeCallback?.Invoke();
        }

        // ---------- transitions (also the deterministic seams tests drive) ----------

        /// <summary>Save to the P11B slot; report success or the P11B failure reason.</summary>
        public async UniTask RunSaveAsync()
        {
            if (IsTransitioning) return;

            if (_saves == null)
            {
                View.SetStatus(NoSaveServiceLabel);
                Refresh();
                return;
            }

            _busy = true;
            View.SetStatus(SavingLabel);
            Refresh();

            var result = await _saves.SaveAsync();

            _busy = false;
            View.SetStatus(result != null && result.Success
                ? string.Format(SaveSuccessFormat,
                    result.Metadata != null ? result.Metadata.DiscipleCount : 0,
                    result.Metadata != null ? result.Metadata.BuildingCount : 0)
                : DescribeSaveFailure(result));
            Refresh();
        }

        /// <summary>
        /// Return to Title — refuses unless the confirmation is currently showing, so the prompt
        /// cannot be bypassed by anything that reaches this method directly.
        /// </summary>
        public async UniTask RunReturnToTitleAsync()
        {
            if (!_confirming) return;
            if (IsTransitioning) return;

            if (_coordinator == null)
            {
                View.SetStatus(NoSessionServiceLabel);
                Refresh();
                return;
            }

            _busy = true;
            _confirming = false;
            View.SetStatus(ReturningLabel);
            Refresh();

            var result = await _coordinator.ReturnToTitleAsync();

            _busy = false;
            if (result == null || !result.Success)
                View.SetStatus(TitleMenuPresenter.DescribeTransitionFailure(result));
            Refresh();
        }

        /// <summary>
        /// P12B §3 — the save-failure line. Built from the P11B <see cref="SaveIoErrorKind"/> so a
        /// busy / cancelled / obsolete / I/O refusal reads as itself instead of "failed".
        /// </summary>
        public static string DescribeSaveFailure(SaveSlotResult result)
        {
            if (result == null) return SaveFailurePrefix + TitleMenuPresenter.UnknownSaveErrorLabel;
            return SaveFailurePrefix + TitleMenuPresenter.DescribeSaveError(result.ErrorKind, result.Error);
        }

        // ---------- state ----------

        private bool IsTransitioning =>
            _busy || (_coordinator != null && _coordinator.IsTransitioning);

        private void Refresh()
        {
            // A session that is no longer Playing cannot be left again; drop the prompt so a
            // returning panel never opens on a stale confirmation.
            if (_coordinator != null && _coordinator.Phase != GameSessionPhase.Playing) _confirming = false;

            bool transitioning = IsTransitioning;

            View.SetConfirmationVisible(_confirming);
            View.SetButtonsInteractable(
                save: !transitioning && _saves != null,
                returnToTitle: !transitioning && _coordinator != null,
                confirm: !transitioning);
        }

        public override void Dispose()
        {
            _phaseSubscription?.Dispose();
            _phaseSubscription = null;
            _closeCallback = null;

            View.SaveClicked -= HandleSaveClicked;
            View.ReturnToTitleClicked -= HandleReturnToTitleClicked;
            View.CloseClicked -= HandleCloseClicked;
            View.ConfirmYesClicked -= HandleConfirmYesClicked;
            View.ConfirmNoClicked -= HandleConfirmNoClicked;
            View.UnwireButtons();
        }
    }
}
