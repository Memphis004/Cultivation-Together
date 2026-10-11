using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using Xianxia.Sect;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;
using Xianxia.Sect.UI;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// P12B §4/§8 — while the session phase is NOT Playing: the time-control bar (and therefore its
    /// Space / 1 / 2 / 3 hotkeys), the event chip, the event popup and the session HUD must not be
    /// on screen, and the P12A <see cref="ISessionUiCloser"/> — not a parallel closer — is what
    /// drives the HUD half.
    ///
    /// Visibility here means the actual GameObject state, and "Playing" always means the phase the
    /// coordinator reports — never a parameter a test could pass in by hand.
    /// </summary>
    public class SessionUiVisibilityTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);

        private GameObject _uiRootGo;
        private UIRoot _uiRoot;
        private UIPanelCatalog _catalog;
        private IObjectResolver _container;

        [SetUp]
        public void SetUp()
        {
            _uiRootGo = new GameObject("SessionUiTest_UIRoot", typeof(RectTransform));
            _uiRoot = _uiRootGo.AddComponent<UIRoot>();
            _catalog = AssetDatabase.LoadAssetAtPath<UIPanelCatalog>("Assets/Panel Catalog/MainPanelCatalog.asset");
            Assert.IsNotNull(_catalog, "the panel catalog must exist (run the panel generators)");
        }

        [TearDown]
        public void TearDown()
        {
            if (_uiRootGo != null)
            {
                foreach (Transform child in _uiRootGo.transform) UnityEngine.Object.DestroyImmediate(child.gameObject);
                UnityEngine.Object.DestroyImmediate(_uiRootGo);
                _uiRootGo = null;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  TimeControls + hotkeys
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void TimeControlBar_IsHiddenAndSilent_OutsidePlaying_AndReturnsWhilePlaying()
        {
            var time = NewTimeSystem();
            var coordinator = new FakeCoordinator { Phase = GameSessionPhase.Title };
            var phaseSub = new FakeSubscriber<SessionPhaseChangedMessage>();

            var view = BuildTimeControlView(out var viewGo);
            var presenter = new TimeControlPresenter(
                time, new FakeSubscriber<TimeSpeedChangedMessage>(), coordinator, phaseSub);

            try
            {
                presenter.Bind(view);

                Assert.IsFalse(viewGo.activeSelf, "boot is Title → the bar is not on screen");
                Assert.IsFalse(view.HotkeysAllowed,
                    "Space / 1 / 2 / 3 must be inert: an inactive GameObject gets no Update, and the guard agrees");

                // …Playing: the phase message is the cue, the coordinator is the source of truth.
                coordinator.Phase = GameSessionPhase.Playing;
                phaseSub.Deliver(new SessionPhaseChangedMessage
                {
                    Previous = GameSessionPhase.StartingNewGame,
                    Current = GameSessionPhase.Playing,
                });

                Assert.IsTrue(viewGo.activeSelf, "a live session shows the bar again");
                Assert.IsTrue(view.HotkeysAllowed);

                // …and back out: ReturningToTitle is already "not Playing".
                coordinator.Phase = GameSessionPhase.ReturningToTitle;
                phaseSub.Deliver(new SessionPhaseChangedMessage
                {
                    Previous = GameSessionPhase.Playing,
                    Current = GameSessionPhase.ReturningToTitle,
                });

                Assert.IsFalse(viewGo.activeSelf);
                Assert.IsFalse(view.HotkeysAllowed);
            }
            finally
            {
                presenter.Dispose();
                UnityEngine.Object.DestroyImmediate(viewGo);
            }
        }

        [Test]
        public void TimeControlBar_RefusesToShow_WhileNoSessionIsPlaying()
        {
            var view = BuildTimeControlView(out var viewGo);
            try
            {
                view.SetSessionActive(false);

                view.Show(); // e.g. UIService.Open re-showing an existing instance

                Assert.IsFalse(viewGo.activeSelf, "the session rule wins over a caller that asks it to show");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(viewGo);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  the P12A closer owns the HUD half
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void SessionUiCloser_KeepsTitleMenuPersistent_ButOutOfTheSessionHud()
        {
            CollectionAssert.Contains(SessionUiCloser.PersistentPanelIds, "TitleMenu",
                "the Title must survive a Return to Title instead of being closed");

            CollectionAssert.DoesNotContain(SessionUiCloser.SessionHudPanelIds, "TitleMenu",
                "the Title is shown exactly while the session HUD is hidden");
            CollectionAssert.DoesNotContain(SessionUiCloser.SessionHudPanelIds, "IngameMenu",
                "the in-game menu is session-scoped interaction UI: it is CLOSED with the session");
            CollectionAssert.DoesNotContain(SessionUiCloser.PersistentPanelIds, "IngameMenu");

            CollectionAssert.Contains(SessionUiCloser.SessionHudPanelIds, "TimeControl");
            CollectionAssert.Contains(SessionUiCloser.SessionHudPanelIds, "WalletHud");
        }

        [Test]
        public void SessionUiCloser_HidesAnOpenSessionBar_AndThePresenterOwnsShowingItAgain()
        {
            var time = NewTimeSystem();
            var coordinator = new FakeCoordinator { Phase = GameSessionPhase.Title };
            var phaseSub = new FakeSubscriber<SessionPhaseChangedMessage>();

            var builder = new ContainerBuilder();
            builder.RegisterInstance(time);
            builder.RegisterInstance<ISubscriber<TimeSpeedChangedMessage>>(new FakeSubscriber<TimeSpeedChangedMessage>());
            builder.RegisterInstance<IGameSessionCoordinator>(coordinator);
            builder.RegisterInstance<ISubscriber<SessionPhaseChangedMessage>>(phaseSub);
            // UIService.Open resolves the presenter from the container exactly like UIInstaller's
            // registrations — a missing one is a hard error AND leaves a half-built panel behind.
            builder.Register<TimeControlPresenter>(Lifetime.Transient);
            _container = builder.Build();

            var uiService = new UIService(_container, _catalog, _uiRoot);
            var closer = new SessionUiCloser(uiService);

            try
            {
                var handle = uiService.Open("TimeControl");
                var barGo = handle.View.GameObject;

                Assert.IsFalse(barGo.activeSelf, "opened while Title → the presenter keeps it hidden");

                // The closer alone cannot fake it visible: the presenter has not been told the
                // session started, so the bar refuses (and reports the refusal honestly).
                Assert.AreEqual(0, closer.SetSessionUiVisible(true));
                Assert.IsFalse(barGo.activeSelf);

                // The phase message is what hands visibility to the bar…
                coordinator.Phase = GameSessionPhase.Playing;
                phaseSub.Deliver(new SessionPhaseChangedMessage
                {
                    Previous = GameSessionPhase.StartingNewGame,
                    Current = GameSessionPhase.Playing,
                });
                Assert.IsTrue(barGo.activeSelf);

                // …and the closer is what takes it away again on the next session boundary.
                Assert.AreEqual(1, closer.SetSessionUiVisible(false), "the open HUD panel was hidden");
                Assert.IsFalse(barGo.activeSelf);
                Assert.IsFalse(closer.SessionUiVisible);

                uiService.Close("TimeControl");
            }
            finally
            {
                closer.SetSessionUiVisible(true);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  event UI (chip + popup)
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void EventPopupAndChip_StayOffScreen_OutsidePlaying_AndOpenWhilePlaying()
        {
            var time = NewTimeSystem();
            var coordinator = new FakeCoordinator { Phase = GameSessionPhase.Title };
            var phaseSub = new FakeSubscriber<SessionPhaseChangedMessage>();
            var triggerSub = new FakeSubscriber<WorldEventTriggeredMessage>();

            var builder = new ContainerBuilder();
            builder.RegisterInstance(time);
            builder.RegisterInstance<ISectStateProvider>(NewProvider(new MutableClock(T0)));
            builder.Register<DecisionExecutor>(Lifetime.Singleton);
            builder.RegisterInstance<IPublisher<DecisionExecutedMessage>>(new BufferPublisher<DecisionExecutedMessage>());
            builder.RegisterInstance<ISubscriber<DecisionExecutedMessage>>(new FakeSubscriber<DecisionExecutedMessage>());
            builder.RegisterInstance<IGameSessionCoordinator>(coordinator);
            builder.RegisterInstance<ISubscriber<SessionPhaseChangedMessage>>(phaseSub);
            builder.Register<EventPopupPresenter>(Lifetime.Transient);
            builder.Register<WorldEventChipPresenter>(Lifetime.Transient);
            _container = builder.Build();

            var uiService = new UIService(_container, _catalog, _uiRoot);
            var system = new WorldEventUISystem(triggerSub, uiService, time, coordinator, phaseSub);

            try
            {
                system.Start();
                Assert.AreEqual(0, PopupCount(), "no popup while the phase is Title");

                // A decision-requiring event arrives while the phase is Title.
                time.RaiseWorldEvent("bandit_raid_001", "Bandits", true, new List<EventChoiceInfo>
                {
                    new EventChoiceInfo { ChoiceId = "pay_tribute", Label = "จ่ายส่วย" },
                });
                triggerSub.Deliver(new WorldEventTriggeredMessage());

                Assert.IsTrue(time.HasPendingDecision, "the event is pending in the model");
                Assert.AreEqual(0, PopupCount(), "…but no popup may appear outside Playing");
                Assert.IsFalse(ChipActive(), "and the chip is not the visible signal either");

                // Playing: the same pending event now surfaces through the real UI.
                coordinator.Phase = GameSessionPhase.Playing;
                phaseSub.Deliver(new SessionPhaseChangedMessage
                {
                    Previous = GameSessionPhase.Title,
                    Current = GameSessionPhase.Playing,
                });

                Assert.AreEqual(1, PopupCount(), "Inside Playing the popup opens from authoritative state");
                Assert.IsNotNull(time.PendingEventId);
            }
            finally
            {
                system.Dispose();
                uiService.Close("EventPopup");
                uiService.Close("EventChip");
            }
        }

        private int PopupCount()
            => _uiRootGo.GetComponentsInChildren<EventPopupView>(true).Length;

        private bool ChipActive()
        {
            var chips = _uiRootGo.GetComponentsInChildren<WorldEventChipView>(true);
            return chips.Length > 0 && chips[0].gameObject.activeSelf;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  coordinator: the authoritative half (boot flag + HUD visibility)
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void Coordinator_BootsAtTitle_WithTheSessionHudHidden_AndNoSceneLoaded()
        {
            var session = NewCoordinator(new SessionRuntimeConfig { StartAtTitle = true });

            session.Coordinator.Start();

            Assert.AreEqual(GameSessionPhase.Title, session.Coordinator.Phase);
            Assert.AreEqual(0, session.Loader.LoadCount, "StartAtTitle must not auto-start a session");
            Assert.IsFalse(session.Ui.SessionUiVisible, "the session HUD is hidden at the Title screen");
            Assert.IsFalse(session.Time.IsSessionActive, "no session runs while the phase is Title");
            Assert.IsFalse(session.Coordinator.IsTransitioning);
        }

        [Test]
        public void Coordinator_QuickStart_IsPreserved_WhenStartAtTitleIsFalse()
        {
            var session = NewCoordinator(new SessionRuntimeConfig { StartAtTitle = false });

            session.Coordinator.Start();

            Assert.AreEqual(1, session.Loader.LoadCount, "the pre-P12B quick start still loads immediately");
            Assert.AreEqual(GameSessionPhase.Playing, session.Coordinator.Phase);
            Assert.IsTrue(session.Ui.SessionUiVisible, "Playing shows the session HUD");
        }

        [Test]
        public void Coordinator_HidesTheHudAgain_OnReturnToTitle()
        {
            var session = NewCoordinator(new SessionRuntimeConfig { StartAtTitle = true });

            var started = session.Coordinator.StartNewGameAsync().GetAwaiter().GetResult();
            Assert.IsTrue(started.Success, started.Error);
            Assert.IsTrue(session.Ui.SessionUiVisible);

            var returned = session.Coordinator.ReturnToTitleAsync().GetAwaiter().GetResult();

            Assert.IsTrue(returned.Success, returned.Error);
            Assert.AreEqual(GameSessionPhase.Title, session.Coordinator.Phase);
            Assert.IsFalse(session.Ui.SessionUiVisible, "back at Title → the session HUD is hidden");
            Assert.IsTrue(session.Ui.CloseCount > 0, "and the session's interaction UI was closed (P12A)");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  harness
        // ══════════════════════════════════════════════════════════════════════

        private static TimeControlView BuildTimeControlView(out GameObject go)
        {
            var root = new GameObject("TimeControlTestRoot", typeof(RectTransform));
            root.SetActive(false);

            Button MakeButton(string name)
            {
                var child = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
                child.transform.SetParent(root.transform, false);
                var img = child.GetComponent<Image>();
                child.GetComponent<Button>().targetGraphic = img;
                return child.GetComponent<Button>();
            }

            var pause = MakeButton("PauseButton");
            var s1 = MakeButton("Speed1Button");
            var s2 = MakeButton("Speed2Button");
            var s3 = MakeButton("Speed3Button");
            var menu = MakeButton("MenuButton");

            var statusGo = new GameObject("StatusText", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
            statusGo.transform.SetParent(root.transform, false);

            var view = root.AddComponent<TimeControlView>();
            var so = new SerializedObject(view);
            so.FindProperty("pauseButton").objectReferenceValue = pause;
            so.FindProperty("speed1Button").objectReferenceValue = s1;
            so.FindProperty("speed2Button").objectReferenceValue = s2;
            so.FindProperty("speed3Button").objectReferenceValue = s3;
            so.FindProperty("menuButton").objectReferenceValue = menu;
            so.FindProperty("statusText").objectReferenceValue = statusGo.GetComponent<TMPro.TMP_Text>();
            so.ApplyModifiedPropertiesWithoutUndo();

            root.SetActive(true);
            go = root;
            return view;
        }

        private static TimeSystem NewTimeSystem()
        {
            return new TimeSystem(
                new BufferPublisher<TimeSpeedChangedMessage>(),
                new BufferPublisher<WorldEventTriggeredMessage>());
        }

        private static SessionObjects NewCoordinator(SessionRuntimeConfig config)
        {
            var clock = new MutableClock(T0);
            var time = NewTimeSystem();
            var provider = NewProvider(clock);
            var snapshots = new SessionSnapshotService(
                provider, time, new BufferPublisher<SessionRestoredMessage>(), clock);
            var loader = new FakeSceneLoader();
            var ui = new RecordingUiCloser();

            return new SessionObjects
            {
                Clock = clock,
                Time = time,
                Loader = loader,
                Ui = ui,
                Coordinator = new GameSessionCoordinator(
                    loader, snapshots, time, new PrototypeStarterStateFactory(),
                    new SessionTransitionTracker(), null, null, null, null, ui, null, config),
            };
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

        // ---------- test doubles ----------

        private sealed class SessionObjects
        {
            public MutableClock Clock;
            public TimeSystem Time;
            public FakeSceneLoader Loader;
            public RecordingUiCloser Ui;
            public GameSessionCoordinator Coordinator;
        }

        private sealed class FakeCoordinator : IGameSessionCoordinator
        {
            public GameSessionPhase Phase { get; set; } = GameSessionPhase.Title;
            public bool IsPlaying => Phase == GameSessionPhase.Playing;
            public bool IsTransitioning => false;
            public string LastTransitionError => string.Empty;
            public int SessionGeneration => 0;
            public int CompletedTransitions => 0;

            public UniTask<SessionTransitionResult> StartNewGameAsync(CancellationToken cancellationToken = default)
                => UniTask.FromResult(SessionTransitionResult.Ok(GameSessionPhase.Playing, GameSessionPhase.Title));

            public UniTask<SessionTransitionResult> LoadGameAsync(CancellationToken cancellationToken = default)
                => UniTask.FromResult(SessionTransitionResult.Ok(GameSessionPhase.Playing, GameSessionPhase.Title));

            public UniTask<SessionTransitionResult> ReturnToTitleAsync(CancellationToken cancellationToken = default)
                => UniTask.FromResult(SessionTransitionResult.Ok(GameSessionPhase.Title, GameSessionPhase.Playing));
        }

        private sealed class FakeSceneLoader : IGameplaySceneLoader
        {
            public string Current;
            public int LoadCount;
            public int UnloadCount;

            public UniTask LoadGameplayScene(string sceneName)
            {
                LoadCount++;
                Current = sceneName;
                return UniTask.CompletedTask;
            }

            public UniTask UnloadCurrentGameplayScene()
            {
                UnloadCount++;
                Current = null;
                return UniTask.CompletedTask;
            }

            public string GetCurrentGameplayScene() => Current ?? string.Empty;
        }

        private sealed class RecordingUiCloser : ISessionUiCloser
        {
            public int CloseCount;
            public bool SessionUiVisible = true;

            public int CloseSessionUi() { CloseCount++; return 1; }
            public int SetSessionUiVisible(bool visible) { SessionUiVisible = visible; return 1; }
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

            public IDisposable Subscribe(IMessageHandler<T> handler, params MessageHandlerFilter<T>[] filters)
            {
                Handler = handler;
                return new Subscription(this);
            }

            public void Deliver(T message) => Handler?.Handle(message);

            private sealed class Subscription : IDisposable
            {
                private readonly FakeSubscriber<T> _owner;
                public Subscription(FakeSubscriber<T> owner) { _owner = owner; }
                public void Dispose() { _owner.Handler = null; }
            }
        }
    }
}
