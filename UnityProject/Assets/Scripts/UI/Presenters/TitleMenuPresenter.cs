using System;
using Cysharp.Threading.Tasks;
using MessagePipe;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// P12B — the Title screen presenter: New Game / Continue / Quit plus one status line.
    ///
    /// Rules it exists to enforce (§2):
    ///   * Continue is enabled from SAVE METADATA only. The click still goes through
    ///     <see cref="IGameSessionCoordinator.LoadGameAsync"/>, which re-reads and validates the
    ///     whole save before anything destructive happens — metadata decides the button, the
    ///     full validation decides the load.
    ///   * A missing / corrupt / unsupported-version / I/O save is reported as ITSELF
    ///     (<see cref="DescribeSaveError"/>), never turned into a silent New Game and never
    ///     followed by a delete.
    ///   * Every button is dead while a transition is in flight, and a duplicate click is ignored
    ///     before it can ask the coordinator a second time.
    ///
    /// State is derived from the authoritative coordinator (phase / IsTransitioning) on bind AND
    /// on every <see cref="SessionPhaseChangedMessage"/>; the message is only the refresh trigger,
    /// so subscription/start order cannot lose the phase (Common-Rules 13).
    /// </summary>
    public class TitleMenuPresenter : UIPresenter<TitleMenuView>
    {
        public const string StartingNewGameLabel = "กำลังเริ่มเกมใหม่...";
        public const string LoadingLabel = "กำลังโหลดบันทึก...";
        public const string QuittingLabel = "กำลังออกจากเกม...";
        public const string SaveReadyFormat = "บันทึก: {0} ศิษย์ · {1} อาคาร";
        public const string RecoveredFromBackupNote = " (กู้คืนจากไฟล์สำรอง)";

        public const string NoSaveLabel = "ไม่มีไฟล์บันทึก — เล่นใหม่ได้ แต่เล่นต่อไม่ได้";
        public const string CorruptSaveLabel = "ไฟล์บันทึกเสียหาย — เล่นต่อไม่ได้ (ไฟล์เดิมยังอยู่ครบ)";
        public const string UnsupportedVersionLabel = "ไฟล์บันทึกจากเวอร์ชันอื่น — เล่นต่อไม่ได้";
        public const string IoErrorLabel = "อ่านไฟล์บันทึกไม่สำเร็จ — เล่นต่อไม่ได้";
        public const string BusySaveLabel = "กำลังบันทึก/โหลดอยู่ — ลองอีกครั้ง";
        public const string UnknownSaveErrorLabel = "ใช้ไฟล์บันทึกไม่ได้ — เล่นต่อไม่ได้";
        public const string NoServiceLabel = "ยังไม่พร้อมใช้งาน (ไม่พบบริการเซสชัน/บันทึก)";
        public const string UnknownFailureLabel = "เริ่มเซสชันไม่สำเร็จ (ไม่ทราบสาเหตุ)";

        private readonly IGameSessionCoordinator _coordinator;
        private readonly SaveSessionService _saves;
        private readonly ISubscriber<SessionPhaseChangedMessage> _phaseSubscriber;

        private IDisposable _phaseSubscription;
        private bool _transitionInFlight;
        private bool _hasSave;

        public TitleMenuPresenter(
            IGameSessionCoordinator coordinator,
            SaveSessionService saves = null,
            ISubscriber<SessionPhaseChangedMessage> phaseSubscriber = null)
        {
            _coordinator = coordinator;
            _saves = saves;
            _phaseSubscriber = phaseSubscriber;
        }

        protected override void OnViewBound()
        {
            View.NewGameClicked += HandleNewGameClicked;
            View.ContinueClicked += HandleContinueClicked;
            View.QuitClicked += HandleQuitClicked;
            View.WireButtons();

            _phaseSubscription = _phaseSubscriber?.Subscribe(OnPhaseChanged);

            // Authoritative read first (Show/Hide + button state + the current phase's status),
            // then the one piece of state that needs I/O.
            Refresh();
            RefreshSaveMetadataAsync().Forget();
        }

        // ---------- button handlers (the view's real path) ----------

        private void HandleNewGameClicked() => RunNewGameAsync().Forget();

        private void HandleContinueClicked() => RunContinueAsync().Forget();

        private void HandleQuitClicked()
        {
            if (_transitionInFlight) return; // duplicate click while a transition runs

            View.SetStatus(QuittingLabel);

#if UNITY_EDITOR
            // In the Editor there is no player to quit: leaving Play Mode is the equivalent, and
            // it is what a dev iterating through Title expects the button to do.
            UnityEditor.EditorApplication.isPlaying = false;
#else
            UnityEngine.Application.Quit();
#endif
        }

        // ---------- transitions (also the deterministic seams tests drive) ----------

        /// <summary>New Game — invokes the coordinator exactly once per click-in-flight.</summary>
        public async UniTask RunNewGameAsync()
        {
            if (!TryBeginTransition(StartingNewGameLabel)) return;
            var result = await _coordinator.StartNewGameAsync();
            EndTransition(result);
        }

        /// <summary>
        /// Continue — refuses to even ask when metadata says there is no usable save, so a corrupt
        /// file can never be laundered into "start a new game".
        /// </summary>
        public async UniTask RunContinueAsync()
        {
            if (!_hasSave)
            {
                // Not a silent New Game: say why and stay put.
                View.SetStatus(NoSaveLabel);
                Refresh();
                return;
            }

            if (!TryBeginTransition(LoadingLabel)) return;

            if (_coordinator == null)
            {
                _transitionInFlight = false;
                View.SetStatus(NoServiceLabel);
                Refresh();
                return;
            }

            var result = await _coordinator.LoadGameAsync();
            EndTransition(result);
        }

        private bool TryBeginTransition(string statusLabel)
        {
            if (_coordinator == null)
            {
                View.SetStatus(NoServiceLabel);
                Refresh();
                return false;
            }

            // Duplicate click / already transitioning: ignored, not queued (§2).
            if (_transitionInFlight || _coordinator.IsTransitioning) return false;

            _transitionInFlight = true;
            View.SetStatus(statusLabel);
            Refresh();
            return true;
        }

        private void EndTransition(SessionTransitionResult result)
        {
            _transitionInFlight = false;

            if (result != null && result.Success)
            {
                // Success ⇒ the phase becomes Playing and this panel hides itself; clear the
                // transient label so the next Title visit starts from the save line.
                View.SetStatus(string.Empty);
                Refresh();
                RefreshSaveMetadataAsync().Forget();
                return;
            }

            View.SetStatus(DescribeTransitionFailure(result));
            Refresh();
            // Refresh the buttons from metadata but LEAVE the failure reason on screen — the
            // detailed P11B text ("... is corrupt: could not be deserialized ...") is more useful
            // than the summary line, and §2 forbids hiding why Continue did not work.
            RefreshSaveMetadataAsync(updateStatusText: false).Forget();
        }

        // ---------- metadata (§2 — Continue is enabled from this alone) ----------

        /// <summary>
        /// Read the save metadata and apply it. Public so a test can await the read instead of
        /// racing a fire-and-forget continuation.
        /// </summary>
        public async UniTask RefreshSaveMetadataAsync(bool updateStatusText = true)
        {
            if (_saves == null)
            {
                _hasSave = false;
                if (updateStatusText) View.SetStatus(NoServiceLabel);
                Refresh();
                return;
            }

            var metadata = await _saves.ReadMetadataAsync();
            _hasSave = metadata != null && metadata.HasSave;

            if (updateStatusText)
            {
                if (_hasSave)
                {
                    View.SetStatus(string.Format(SaveReadyFormat,
                        metadata.DiscipleCount, metadata.BuildingCount)
                        + (metadata.FromBackup ? RecoveredFromBackupNote : string.Empty));
                }
                else
                {
                    View.SetStatus(DescribeSaveError(
                        metadata != null ? metadata.ErrorKind : SaveIoErrorKind.Missing,
                        metadata != null ? metadata.Error : string.Empty));
                }
            }

            Refresh();
        }

        /// <summary>
        /// P12B §2 — the Continue line for a save that cannot be used. Pure + static so the
        /// missing / corrupt / unsupported-version / I/O mapping is testable without a view, and
        /// never empty: a silent failure is exactly what this phase forbids.
        /// </summary>
        public static string DescribeSaveError(SaveIoErrorKind kind, string detail)
        {
            string label;
            switch (kind)
            {
                case SaveIoErrorKind.Missing: label = NoSaveLabel; break;
                case SaveIoErrorKind.Corrupt: label = CorruptSaveLabel; break;
                case SaveIoErrorKind.UnsupportedVersion: label = UnsupportedVersionLabel; break;
                case SaveIoErrorKind.Io: label = IoErrorLabel; break;
                case SaveIoErrorKind.Busy: label = BusySaveLabel; break;
                default: label = UnknownSaveErrorLabel; break;
            }

            return string.IsNullOrEmpty(detail) ? label : label + " — " + detail;
        }

        /// <summary>
        /// The reason a New Game / Continue did not complete. Never returns empty (an unexplained
        /// failure would look like a dead button).
        /// </summary>
        public static string DescribeTransitionFailure(SessionTransitionResult result)
        {
            if (result == null) return UnknownFailureLabel;
            // P11A/P11B/P12A already produce a readable reason; prefer it over a generic label.
            return string.IsNullOrEmpty(result.Error) ? UnknownFailureLabel : result.Error;
        }

        // ---------- phase-derived state ----------

        /// <summary>
        /// Phase changed: the message is only a trigger — every value is re-read from the
        /// coordinator. Reaching Title (from a session, or after a failed transition) also re-reads
        /// the save metadata, because a save may have been written during the session we left and
        /// metadata is the only authority for Continue.
        /// </summary>
        private void OnPhaseChanged(SessionPhaseChangedMessage message)
        {
            var phase = CurrentPhase();

            if (phase == GameSessionPhase.Playing)
            {
                _transitionInFlight = false;
                Refresh();
                return;
            }

            if (phase == GameSessionPhase.Title)
            {
                _transitionInFlight = false;
                RefreshSaveMetadataAsync().Forget();
                return;
            }

            Refresh();
        }

        private GameSessionPhase CurrentPhase()
        {
            return _coordinator != null ? _coordinator.Phase : GameSessionPhase.Title;
        }

        /// <summary>
        /// Show/hide + button state, derived from the authoritative phase. The panel stays visible
        /// through a transition (so "กำลัง..." is actually readable) and only yields the screen
        /// once a session is Playing.
        /// </summary>
        private void Refresh()
        {
            bool playing = CurrentPhase() == GameSessionPhase.Playing;
            bool transitioning = _transitionInFlight
                                 || (_coordinator != null && _coordinator.IsTransitioning);

            if (playing) View.Hide();
            else View.Show();

            View.SetButtonsInteractable(
                newGame: !transitioning,
                canContinue: !transitioning && _hasSave,
                quit: !transitioning);
        }

        public override void Dispose()
        {
            _phaseSubscription?.Dispose();
            _phaseSubscription = null;

            View.NewGameClicked -= HandleNewGameClicked;
            View.ContinueClicked -= HandleContinueClicked;
            View.QuitClicked -= HandleQuitClicked;
            View.UnwireButtons();
        }
    }
}
