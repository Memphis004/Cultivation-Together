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
    /// P12B §3/§8 — the in-game menu: Save reports success or the P11B reason, Return to Title
    /// ALWAYS needs the confirmation, and every button is dead while a transition is in flight.
    /// </summary>
    public class IngameMenuPresenterTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);

        private string _root;
        private SaveSlotPaths _paths;
        private SaveSlotRepository _repository;
        private SaveSessionService _saves;
        private FakeCoordinator _coordinator;
        private int _closeRequests;

        private GameObject _viewGo;
        private IngameMenuView _view;
        private IngameMenuPresenter _presenter;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "sect_ingame_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _paths = new SaveSlotPaths(_root);
            _repository = new SaveSlotRepository(new SystemSaveFileSystem(), _paths);
            _saves = NewSaveService(_repository);
            _coordinator = new FakeCoordinator { Phase = GameSessionPhase.Playing };
        }

        [TearDown]
        public void TearDown()
        {
            if (_presenter != null) _presenter.Dispose();
            if (_viewGo != null) UnityEngine.Object.DestroyImmediate(_viewGo);
            DeleteDirectory(_root);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Save
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void Save_WritesTheSlot_AndReportsSuccess()
        {
            Bind();

            _view.InvokeSaveForTest();

            Assert.IsTrue(File.Exists(_paths.SlotPath), "Save must actually write the P11B slot");
            Assert.IsFalse(File.Exists(_paths.TempPath), "no temp file may survive a successful save");
            StringAssert.Contains("บันทึกสำเร็จ", _view.StatusForTest);
            Assert.AreEqual(1, _saves.SaveCount);
        }

        [Test]
        public void SaveFailure_ShowsTheP11BReason_InsteadOfABareFailure()
        {
            // No slot path configured: the repository refuses before any I/O with Rejected, which is
            // a real P11B failure kind — the panel must show WHY.
            _saves = NewSaveService(new SaveSlotRepository(new SystemSaveFileSystem(), new SaveSlotPaths(string.Empty)));
            Bind();

            _view.InvokeSaveForTest();

            StringAssert.StartsWith(IngameMenuPresenter.SaveFailurePrefix, _view.StatusForTest);
            StringAssert.Contains("No save slot path is configured", _view.StatusForTest);
            Assert.AreEqual(0, _saves.SaveCount);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Return to Title: always confirmed
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void ReturnToTitle_RequiresConfirmation_BeforeItTouchesTheCoordinator()
        {
            Bind();

            _view.InvokeReturnToTitleForTest();

            Assert.AreEqual(0, _coordinator.ReturnCalls, "the first click only asks");
            Assert.IsTrue(_view.IsConfirmationVisible, "the confirmation block is shown");
            StringAssert.Contains("กลับหน้าจอชื่อเกม?", _view.StatusForTest);

            _view.InvokeConfirmYesForTest();

            Assert.AreEqual(1, _coordinator.ReturnCalls);
            Assert.AreEqual(GameSessionPhase.Title, _coordinator.Phase);
            Assert.IsFalse(_view.IsConfirmationVisible);
        }

        [Test]
        public void Confirmation_Cancel_DoesNotReturnToTitle()
        {
            Bind();

            _view.InvokeReturnToTitleForTest();
            _view.InvokeConfirmNoForTest();

            Assert.AreEqual(0, _coordinator.ReturnCalls);
            Assert.IsFalse(_view.IsConfirmationVisible);
            Assert.AreEqual(string.Empty, _view.StatusForTest, "cancelling clears the prompt line");
        }

        [Test]
        public void ReturnToTitle_CannotBeBypassed_ByCallingTheTransitionDirectly()
        {
            Bind();

            _presenter.RunReturnToTitleAsync().GetAwaiter().GetResult();

            Assert.AreEqual(0, _coordinator.ReturnCalls, "without the prompt there is nothing to confirm");
        }

        [Test]
        public void RunReturnToTitleAsync_StillRefusesWithoutTheConfirmation()
        {
            Bind();

            // Opening the prompt IS the confirmation path; the transition itself must also check
            // that the prompt is the thing on screen.
            _presenter.RunReturnToTitleAsync().GetAwaiter().GetResult();
            Assert.AreEqual(0, _coordinator.ReturnCalls);

            _view.InvokeReturnToTitleForTest();
            _presenter.RunReturnToTitleAsync().GetAwaiter().GetResult();
            Assert.AreEqual(1, _coordinator.ReturnCalls);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  transitions lock the panel + close wiring
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void Buttons_AreDisabled_WhileATransitionIsInFlight()
        {
            Bind();
            _coordinator.Hold = true; // ReturnToTitle never finishes

            _view.InvokeReturnToTitleForTest();
            _view.InvokeConfirmYesForTest();

            Assert.AreEqual(1, _coordinator.ReturnCalls);
            Assert.IsFalse(_view.SaveInteractableForTest, "Save must not start during a transition");
            Assert.IsFalse(_view.ReturnToTitleInteractableForTest);

            _view.InvokeSaveForTest();
            Assert.AreEqual(0, _saves.SaveCount, "a click during a transition must be ignored");

            _view.InvokeReturnToTitleForTest(); // duplicate: prompt is already consumed
            Assert.IsFalse(_view.IsConfirmationVisible, "no second prompt may appear mid-transition");
        }

        [Test]
        public void CloseButton_AsksTheOwnerToClose_ThroughTheArgs()
        {
            Bind();

            _view.InvokeCloseForTest();

            Assert.AreEqual(1, _closeRequests, "the panel closes through UIService (owner), like BuildingMenu");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  harness
        // ══════════════════════════════════════════════════════════════════════

        private void Bind()
        {
            _viewGo = new GameObject("IngameMenuTestRoot", typeof(RectTransform));
            _viewGo.SetActive(false);

            Button MakeButton(Transform parent, string name)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(parent, false);
                var paper = new GameObject("Paper", typeof(RectTransform), typeof(Image));
                paper.transform.SetParent(go.transform, false);
                var img = go.GetComponent<Image>();
                go.GetComponent<Button>().targetGraphic = img;
                return go.GetComponent<Button>();
            }

            TMPro.TMP_Text MakeText(Transform parent, string name)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
                go.transform.SetParent(parent, false);
                return go.GetComponent<TMPro.TMP_Text>();
            }

            var save = MakeButton(_viewGo.transform, "SaveButton");
            var returnToTitle = MakeButton(_viewGo.transform, "ReturnToTitleButton");
            var close = MakeButton(_viewGo.transform, "CloseButton");
            var status = MakeText(_viewGo.transform, "StatusText");

            var confirmRoot = new GameObject("ConfirmRoot", typeof(RectTransform));
            confirmRoot.transform.SetParent(_viewGo.transform, false);
            var yes = MakeButton(confirmRoot.transform, "ConfirmYesButton");
            var no = MakeButton(confirmRoot.transform, "ConfirmNoButton");

            _view = _viewGo.AddComponent<IngameMenuView>();
            var so = new SerializedObject(_view);
            so.FindProperty("saveButton").objectReferenceValue = save;
            so.FindProperty("returnToTitleButton").objectReferenceValue = returnToTitle;
            so.FindProperty("closeButton").objectReferenceValue = close;
            so.FindProperty("statusText").objectReferenceValue = status;
            so.FindProperty("confirmRoot").objectReferenceValue = confirmRoot;
            so.FindProperty("confirmYesButton").objectReferenceValue = yes;
            so.FindProperty("confirmNoButton").objectReferenceValue = no;
            so.ApplyModifiedPropertiesWithoutUndo();

            _viewGo.SetActive(true);

            _presenter = new IngameMenuPresenter(_saves, _coordinator, null);
            _presenter.Bind(_view);
            _presenter.OnOpen(new IngameMenuArgs { CloseCallback = () => _closeRequests++ });
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
            public GameSessionPhase Phase { get; set; } = GameSessionPhase.Playing;
            public bool Transitioning;
            public bool Hold;

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

            public UniTask<SessionTransitionResult> StartNewGameAsync(CancellationToken cancellationToken = default)
            {
                NewGameCalls++;
                return UniTask.FromResult(SessionTransitionResult.Ok(GameSessionPhase.Playing, GameSessionPhase.Title));
            }

            public UniTask<SessionTransitionResult> LoadGameAsync(CancellationToken cancellationToken = default)
            {
                LoadCalls++;
                return UniTask.FromResult(SessionTransitionResult.Ok(GameSessionPhase.Playing, GameSessionPhase.Title));
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
