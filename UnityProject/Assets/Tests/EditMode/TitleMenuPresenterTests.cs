using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Xianxia.Sect;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;
using Xianxia.Sect.UI;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// P12B §2/§8 — the Title presenter: Continue from save metadata only, every button dead
    /// during a transition, a corrupt save reported as corrupt with the file left alone, and New
    /// Game asked for exactly once.
    ///
    /// The save layer is the REAL <see cref="SaveSessionService"/> over an isolated temporary
    /// directory (the SessionSavePersistenceTests convention), because "the file is untouched" is
    /// only meaningful against a real file. The session coordinator is a fake: this file is about
    /// what the panel asks for, and GameSessionCoordinatorTests already covers what the coordinator
    /// does with it.
    /// </summary>
    public class TitleMenuPresenterTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);

        private string _root;
        private SaveSlotPaths _paths;
        private SaveSlotRepository _repository;
        private SaveSessionService _saves;
        private FakeCoordinator _coordinator;
        private FakeSubscriber<SessionPhaseChangedMessage> _phaseSubscriber;

        private GameObject _viewGo;
        private TitleMenuView _view;
        private TitleMenuPresenter _presenter;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "sect_title_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _paths = new SaveSlotPaths(_root);
            _repository = new SaveSlotRepository(new SystemSaveFileSystem(), _paths);
            _saves = NewSaveService(_repository);
            _coordinator = new FakeCoordinator();
            _phaseSubscriber = new FakeSubscriber<SessionPhaseChangedMessage>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_presenter != null) _presenter.Dispose();
            if (_viewGo != null) UnityEngine.Object.DestroyImmediate(_viewGo);
            DeleteDirectory(_root);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Continue: metadata decides the button
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void Continue_IsDisabledWithoutASave_AndEnabledByMetadata()
        {
            Bind();

            Assert.IsTrue(_view.NewGameInteractableForTest, "New Game must work with no save at all");
            Assert.IsFalse(_view.ContinueInteractableForTest, "no save → Continue is not clickable");
            StringAssert.Contains("ไม่มีไฟล์บันทึก", _view.StatusForTest);
            StringAssert.Contains("เล่นใหม่ได้", _view.StatusForTest, "a missing save is not an error state");
            Assert.IsTrue(_view.QuitInteractableForTest);

            WriteValidSlot();

            _presenter.RefreshSaveMetadataAsync().GetAwaiter().GetResult();

            Assert.IsTrue(_view.ContinueInteractableForTest, "metadata says a save exists → Continue is enabled");
            StringAssert.Contains("บันทึก:", _view.StatusForTest);
        }

        [Test]
        public void Continue_WithASave_AsksTheCoordinatorToLoad()
        {
            WriteValidSlot();
            Bind();
            _presenter.RefreshSaveMetadataAsync().GetAwaiter().GetResult();

            _view.InvokeContinueForTest();

            Assert.AreEqual(1, _coordinator.LoadCalls, "Continue goes through LoadGameAsync (which validates the whole save)");
            Assert.AreEqual(0, _coordinator.NewGameCalls, "Continue must never be a New Game");
            Assert.AreEqual(GameSessionPhase.Playing, _coordinator.Phase);
            Assert.IsFalse(_viewGo.activeSelf, "once Playing, the Title hands the screen over");
        }

        [Test]
        public void CorruptSave_IsReported_AndNothingIsLoadedOrStarted_AndTheFileIsUntouched()
        {
            // A file that EXISTS but cannot be read as a session envelope.
            File.WriteAllBytes(_paths.SlotPath, new byte[] { 0xC1, 0x00, 0x42, 0x7F });
            byte[] before = File.ReadAllBytes(_paths.SlotPath);

            Bind();
            _presenter.RefreshSaveMetadataAsync().GetAwaiter().GetResult();

            Assert.IsFalse(_view.ContinueInteractableForTest, "a corrupt save cannot be continued");
            StringAssert.Contains("เสียหาย", _view.StatusForTest, "the corrupt reason is shown as itself");
            Assert.IsFalse(_view.StatusForTest.Contains("เล่นใหม่ได้"),
                "a corrupt save must NOT be described like a missing one (§2)");

            _view.InvokeContinueForTest();

            Assert.AreEqual(0, _coordinator.LoadCalls, "the panel refuses before asking, so no destructive work starts");
            Assert.AreEqual(0, _coordinator.NewGameCalls, "a corrupt save must never silently become a New Game");
            CollectionAssert.AreEqual(before, File.ReadAllBytes(_paths.SlotPath),
                "nothing may delete or rewrite the player's save");
        }

        [Test]
        public void FailedContinue_ShowsTheReason_AndNeverStartsANewGame()
        {
            WriteValidSlot();
            _coordinator.LoadResult = SessionTransitionResult.Failed(
                GameSessionPhase.Title, GameSessionPhase.Title, "the save was valid but refused at commit (test)");
            Bind();
            _presenter.RefreshSaveMetadataAsync().GetAwaiter().GetResult();

            _view.InvokeContinueForTest();

            Assert.AreEqual(1, _coordinator.LoadCalls);
            Assert.AreEqual(0, _coordinator.NewGameCalls, "a failed Continue never falls back to a New Game");
            StringAssert.Contains("refused at commit (test)", _view.StatusForTest, "the failure reason is shown");
            Assert.IsTrue(_view.NewGameInteractableForTest, "the buttons come back after the failure");
            Assert.IsTrue(_viewGo.activeSelf, "a failed load leaves the Title on screen");
        }

        [Test]
        public void DescribeSaveError_MapsEveryP11Kind_AndIsNeverEmpty()
        {
            StringAssert.Contains("ไม่มีไฟล์บันทึก", TitleMenuPresenter.DescribeSaveError(SaveIoErrorKind.Missing, ""));
            StringAssert.Contains("เสียหาย", TitleMenuPresenter.DescribeSaveError(SaveIoErrorKind.Corrupt, ""));
            StringAssert.Contains("เวอร์ชันอื่น", TitleMenuPresenter.DescribeSaveError(SaveIoErrorKind.UnsupportedVersion, ""));
            StringAssert.Contains("อ่านไฟล์", TitleMenuPresenter.DescribeSaveError(SaveIoErrorKind.Io, ""));
            StringAssert.Contains("กำลังบันทึก", TitleMenuPresenter.DescribeSaveError(SaveIoErrorKind.Busy, ""));

            // The four "cannot continue" kinds all say so, and a detail line is appended verbatim.
            Assert.IsTrue(TitleMenuPresenter.DescribeSaveError(SaveIoErrorKind.Missing, "").Contains("เล่นต่อไม่ได้"));
            Assert.IsTrue(TitleMenuPresenter.DescribeSaveError(SaveIoErrorKind.Corrupt, "").Contains("เล่นต่อไม่ได้"));
            Assert.IsTrue(TitleMenuPresenter.DescribeSaveError(SaveIoErrorKind.UnsupportedVersion, "").Contains("เล่นต่อไม่ได้"));
            Assert.IsTrue(TitleMenuPresenter.DescribeSaveError(SaveIoErrorKind.Io, "").Contains("เล่นต่อไม่ได้"));
            StringAssert.Contains("disk on fire", TitleMenuPresenter.DescribeSaveError(SaveIoErrorKind.Io, "disk on fire"));

            Assert.IsNotEmpty(TitleMenuPresenter.DescribeSaveError((SaveIoErrorKind)999, ""));
            Assert.IsNotEmpty(TitleMenuPresenter.DescribeTransitionFailure(null));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  transitions: one request at a time, buttons dead while it runs
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void NewGame_InvokesTheCoordinatorOnce_AndDisablesEverythingWhileItRuns()
        {
            Bind();
            _coordinator.Hold = true; // the transition never finishes (like a slow scene load)

            _view.InvokeNewGameForTest();

            Assert.AreEqual(1, _coordinator.NewGameCalls);
            Assert.IsFalse(_view.NewGameInteractableForTest);
            Assert.IsFalse(_view.ContinueInteractableForTest);
            Assert.IsFalse(_view.QuitInteractableForTest);
            StringAssert.Contains("กำลังเริ่มเกมใหม่", _view.StatusForTest);

            // Duplicate click: ignored, not queued (§2).
            _view.InvokeNewGameForTest();
            Assert.AreEqual(1, _coordinator.NewGameCalls, "a second click must not ask the coordinator again");
        }

        [Test]
        public void Buttons_AreDisabled_WhileATransitionIsInFlight_EvenFromAnotherPath()
        {
            WriteValidSlot();
            Bind();
            _presenter.RefreshSaveMetadataAsync().GetAwaiter().GetResult();
            Assert.IsTrue(_view.ContinueInteractableForTest);

            // A transition started elsewhere (e.g. an MCP-driven call) — the panel must follow.
            _coordinator.Transitioning = true;
            _coordinator.Phase = GameSessionPhase.StartingNewGame;
            _phaseSubscriber.Deliver(new SessionPhaseChangedMessage
            {
                Previous = GameSessionPhase.Title,
                Current = GameSessionPhase.StartingNewGame,
            });

            Assert.IsFalse(_view.NewGameInteractableForTest);
            Assert.IsFalse(_view.ContinueInteractableForTest, "Continue must not race an in-flight transition");
            Assert.IsFalse(_view.QuitInteractableForTest);

            _view.InvokeContinueForTest();
            Assert.AreEqual(0, _coordinator.LoadCalls);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  visibility follows the authoritative phase
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void TitlePanel_IsHiddenWhilePlaying_AndReturnsAtTitle()
        {
            WriteValidSlot();
            Bind();
            _presenter.RefreshSaveMetadataAsync().GetAwaiter().GetResult();
            Assert.IsTrue(_viewGo.activeSelf, "the Title is on screen while the phase is Title");

            _coordinator.Phase = GameSessionPhase.Playing;
            _phaseSubscriber.Deliver(new SessionPhaseChangedMessage
            {
                Previous = GameSessionPhase.StartingNewGame,
                Current = GameSessionPhase.Playing,
            });
            Assert.IsFalse(_viewGo.activeSelf, "no session UI on top of gameplay");

            _coordinator.Phase = GameSessionPhase.Title;
            _phaseSubscriber.Deliver(new SessionPhaseChangedMessage
            {
                Previous = GameSessionPhase.ReturningToTitle,
                Current = GameSessionPhase.Title,
            });
            Assert.IsTrue(_viewGo.activeSelf);
            Assert.IsTrue(_view.ContinueInteractableForTest, "metadata is re-read on reaching Title");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  harness
        // ══════════════════════════════════════════════════════════════════════

        private void Bind()
        {
            _viewGo = new GameObject("TitleMenuTestRoot", typeof(RectTransform));
            _viewGo.SetActive(false); // wire while inactive so no Awake/OnEnable side effects matter

            Button MakeButton(string name)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(_viewGo.transform, false);
                var paper = new GameObject("Paper", typeof(RectTransform), typeof(Image));
                paper.transform.SetParent(go.transform, false);
                var img = go.GetComponent<Image>();
                go.GetComponent<Button>().targetGraphic = img;
                return go.GetComponent<Button>();
            }

            var newGame = MakeButton("NewGameButton");
            var continueButton = MakeButton("ContinueButton");
            var quit = MakeButton("QuitButton");

            var statusGo = new GameObject("StatusText", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
            statusGo.transform.SetParent(_viewGo.transform, false);

            _view = _viewGo.AddComponent<TitleMenuView>();
            var so = new SerializedObject(_view);
            so.FindProperty("newGameButton").objectReferenceValue = newGame;
            so.FindProperty("continueButton").objectReferenceValue = continueButton;
            so.FindProperty("quitButton").objectReferenceValue = quit;
            so.FindProperty("statusText").objectReferenceValue = statusGo.GetComponent<TMPro.TextMeshProUGUI>();
            so.ApplyModifiedPropertiesWithoutUndo();

            _viewGo.SetActive(true);

            _presenter = new TitleMenuPresenter(_coordinator, _saves, _phaseSubscriber);
            _presenter.Bind(_view);
        }

        private void WriteValidSlot()
        {
            var clock = new MutableClock(T0);
            var provider = NewProvider(clock);
            var time = new TimeSystem(
                new BufferPublisher<TimeSpeedChangedMessage>(),
                new BufferPublisher<WorldEventTriggeredMessage>());
            var envelope = new SessionSnapshotService(
                provider, time, new BufferPublisher<SessionRestoredMessage>(), clock).CaptureEnvelope();

            var result = _repository.Write(envelope, CancellationToken.None);
            Assert.IsTrue(result.Success, result.Error);
        }

        private static SaveSessionService NewSaveService(SaveSlotRepository repository)
        {
            var clock = new MutableClock(T0);
            var provider = NewProvider(clock);
            var time = new TimeSystem(
                new BufferPublisher<TimeSpeedChangedMessage>(),
                new BufferPublisher<WorldEventTriggeredMessage>());
            var snapshots = new SessionSnapshotService(
                provider, time, new BufferPublisher<SessionRestoredMessage>(), clock);

            return new SaveSessionService(
                snapshots, repository, new InlineSaveWorkScheduler(),
                new SessionTransitionTracker(), new SaveOperationGate());
        }

        private static SectStateProvider NewProvider(IClock clock)
        {
            return new SectStateProvider(
                new BufferPublisher<DiscipleRecruitedMessage>(),
                new BufferPublisher<SectResourceChangedMessage>(),
                new BufferPublisher<AvatarEquipmentChangedMessage>(),
                new BufferPublisher<DiscipleChibiBackendChangedMessage>(),
                new AvatarPartPool(),
                VisualRuntimeConfig.Instance,
                new DefaultEntitlementProvider(),
                new BuildingDefPool(),
                new BufferPublisher<BuildingPlacedMessage>(),
                new BufferPublisher<DiscipleTaskChangedMessage>(),
                new BufferPublisher<DiscipleOwnerChangedMessage>(),
                clock);
        }

        private static void DeleteDirectory(string path)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (Directory.Exists(path)) Directory.Delete(path, true);
                    return;
                }
                catch (IOException) { Thread.Sleep(25); }
                catch (UnauthorizedAccessException) { Thread.Sleep(25); }
            }
        }

        // ---------- test doubles ----------

        private sealed class FakeCoordinator : IGameSessionCoordinator
        {
            public GameSessionPhase Phase { get; set; } = GameSessionPhase.Title;

            /// <summary>Set externally to model a transition started by some other path.</summary>
            public bool Transitioning;

            /// <summary>When true an in-flight transition never completes (and keeps Transitioning).</summary>
            public bool Hold;

            public SessionTransitionResult LoadResult;
            public SessionTransitionResult NewGameResult;

            public int NewGameCalls;
            public int LoadCalls;
            public int ReturnCalls;

            private UniTaskCompletionSource _hold;

            public bool IsPlaying => Phase == GameSessionPhase.Playing && !IsTransitioning;
            public bool IsTransitioning => Transitioning;
            public string LastTransitionError => string.Empty;
            public int SessionGeneration => 0;
            public int CompletedTransitions => 0;

            private UniTask HoldGate()
            {
                if (_hold == null) _hold = new UniTaskCompletionSource();
                return _hold.Task;
            }

            public async UniTask<SessionTransitionResult> StartNewGameAsync(CancellationToken cancellationToken = default)
            {
                NewGameCalls++;
                Transitioning = true;
                try
                {
                    if (Hold) await HoldGate();
                    if (NewGameResult != null)
                    {
                        Phase = NewGameResult.Phase;
                        return NewGameResult;
                    }

                    Phase = GameSessionPhase.Playing;
                    return SessionTransitionResult.Ok(GameSessionPhase.Playing, GameSessionPhase.Title);
                }
                finally
                {
                    Transitioning = false;
                }
            }

            public async UniTask<SessionTransitionResult> LoadGameAsync(CancellationToken cancellationToken = default)
            {
                LoadCalls++;
                Transitioning = true;
                try
                {
                    if (Hold) await HoldGate();
                    if (LoadResult != null)
                    {
                        Phase = LoadResult.Phase;
                        return LoadResult;
                    }

                    Phase = GameSessionPhase.Playing;
                    return SessionTransitionResult.Ok(GameSessionPhase.Playing, GameSessionPhase.Title);
                }
                finally
                {
                    Transitioning = false;
                }
            }

            public async UniTask<SessionTransitionResult> ReturnToTitleAsync(CancellationToken cancellationToken = default)
            {
                ReturnCalls++;
                Transitioning = true;
                try
                {
                    if (Hold) await HoldGate();
                    Phase = GameSessionPhase.Title;
                    return SessionTransitionResult.Ok(GameSessionPhase.Title, GameSessionPhase.Playing);
                }
                finally
                {
                    Transitioning = false;
                }
            }
        }

        private sealed class MutableClock : IClock
        {
            public MutableClock(DateTime now) { UtcNow = now; }
            public DateTime UtcNow { get; set; }
        }

        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public readonly List<T> Messages = new List<T>();
            public void Publish(T message) => Messages.Add(message);
        }

        private sealed class FakeSubscriber<T> : ISubscriber<T>
        {
            public IMessageHandler<T> Handler;

            public IDisposable Subscribe(IMessageHandler<T> handler,
                                         params MessageHandlerFilter<T>[] filters)
            {
                Handler = handler;
                return new Subscription(this);
            }

            /// <summary>Push a message to the presenter the way MessagePipe would.</summary>
            public void Deliver(T message) => Handler?.Handle(message);

            private sealed class Subscription : IDisposable
            {
                private readonly FakeSubscriber<T> _owner;
                public Subscription(FakeSubscriber<T> owner) { _owner = owner; }
                public void Dispose() { _owner.Handler = null; }
            }
        }

        private sealed class InlineSaveWorkScheduler : ISaveWorkScheduler
        {
            public UniTask<T> RunOnWorkerAsync<T>(Func<T> work, CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                    return UniTask.FromException<T>(new OperationCanceledException(cancellationToken));
                return UniTask.FromResult(work());
            }

            public UniTask SwitchToMainThreadAsync() => UniTask.CompletedTask;
        }
    }
}
