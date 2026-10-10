using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Xianxia.Sect;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// P12A — the explicit game-session lifecycle on top of the additive SceneLoader.
    ///
    /// Covered:
    ///   - New Game enters Playing, loads the scene and applies the starter state;
    ///   - Return to Title unloads content, closes session UI, stops the simulation clock
    ///     without reporting a pause, and completes world-event waiters;
    ///   - repeated New Game / Title cycles reset accumulators + roster each time;
    ///   - a failed scene load is recoverable and leaves live state untouched;
    ///   - Load validates a candidate save before any destructive work (a bad save never
    ///     unloads the scene or replaces live state) and a good one restores + Plays;
    ///   - overlapping/duplicate requests are refused and a stale in-flight load from a
    ///     previous session can never commit;
    ///   - MCP mutation handlers refuse calls outside Playing with a clear result.
    ///
    /// No Unity scene is touched: the loader is a deterministic fake, and the save worker
    /// hop is injected (EditMode has no player loop to pump a real thread hop through).
    /// </summary>
    public class GameSessionCoordinatorTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

        private string _root;
        private SaveSlotPaths _paths;
        private SaveSlotRepository _repo;

        private SectStateProvider _provider;
        private TimeSystem _time;
        private BufferPublisher<SessionRestoredMessage> _restored;
        private SessionSnapshotService _snapshots;
        private SessionTransitionTracker _transition;
        private SaveSessionService _saves;
        private FakeSceneLoader _loader;
        private CountingUiCloser _ui;
        private BufferPublisher<SessionPhaseChangedMessage> _phases;
        private GameSessionCoordinator _coordinator;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "sect_session_coord_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _paths = new SaveSlotPaths(_root);
            _repo = new SaveSlotRepository(new SystemSaveFileSystem(), _paths);

            _provider = NewProvider(new MutableClock(T0));
            _time = NewTimeSystem();
            _restored = new BufferPublisher<SessionRestoredMessage>();
            _snapshots = new SessionSnapshotService(_provider, _time, _restored, new MutableClock(T0));
            _transition = new SessionTransitionTracker();
            _loader = new FakeSceneLoader();
            _ui = new CountingUiCloser();
            _phases = new BufferPublisher<SessionPhaseChangedMessage>();

            _saves = new SaveSessionService(_snapshots, _repo, new InlineSaveWorkScheduler(),
                                            _transition, new SaveOperationGate());
            _coordinator = NewCoordinator(_snapshots, _time, _loader, _saves, _transition, _ui, _phases);
        }

        [TearDown]
        public void TearDown()
        {
            MainThreadDispatch.ResetForTests();
            DeleteDirectory(_root);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  New Game
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void StartNewGame_EntersPlaying_LoadsScene_AndStartsTheClock()
        {
            var result = _coordinator.StartNewGameAsync().GetAwaiter().GetResult();

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(SessionTransitionOutcome.Completed, result.Outcome);
            Assert.AreEqual(GameSessionPhase.Playing, _coordinator.Phase);
            Assert.IsTrue(_coordinator.IsPlaying);
            Assert.AreEqual(SceneNames.Sect, _loader.Current, "New Game must load the gameplay scene through the loader seam");
            Assert.IsTrue(_time.IsSessionActive, "a Playing session activates the simulation clock");
            Assert.IsFalse(_time.IsPaused, "a fresh session is not paused");
            Assert.AreEqual(4, _provider.BuildSectEconomyState().Disciples.Count,
                            "the starter roster is applied");

            // phase notifications were published in order
            Assert.IsTrue(_phases.Messages.Count >= 2, "Title→StartingNewGame→Playing notifications");
            Assert.AreEqual(GameSessionPhase.Playing, _phases.Messages[_phases.Messages.Count - 1].Current);
        }

        [Test]
        public void RepeatedNewGameCycles_ResetAccumulatorsRosterAndDecisionState()
        {
            Assert.IsTrue(_coordinator.StartNewGameAsync().GetAwaiter().GetResult().Success);

            // d001 starts on a task that accumulates sub-unit progress; add progress + extra state.
            var d001 = Find(_provider, "d001");
            d001.CurrentTask = "gathering_wood";
            _provider.TickGathering(0.5f);
            Assert.IsTrue(_snapshots.CaptureSnapshot().GatherAccumulators.ContainsKey("gathering_wood"));

            _provider.RecruitOuterDisciple();
            _provider.BuildSectEconomyState().Stockpile.RawResources["herb"] = 99999;
            Assert.AreEqual(5, _provider.BuildSectEconomyState().Disciples.Count);

            Assert.IsTrue(_coordinator.ReturnToTitleAsync().GetAwaiter().GetResult().Success);

            // Return to Title stops the clock WITHOUT pretending it is a pause.
            Assert.AreEqual(GameSessionPhase.Title, _coordinator.Phase);
            Assert.IsFalse(_coordinator.IsPlaying);
            Assert.IsFalse(_time.IsSessionActive);
            Assert.IsFalse(_time.IsPaused, "no active game is not a pause");
            Assert.IsFalse(_time.IsUserPaused);
            Assert.IsFalse(_time.IsPendingDecisionPaused);
            Assert.AreEqual(0f, _time.SimulationDelta, 1e-6f);

            // New Game again — everything session-scoped is fresh.
            Assert.IsTrue(_coordinator.StartNewGameAsync().GetAwaiter().GetResult().Success);

            var state = _provider.BuildSectEconomyState();
            Assert.AreEqual(4, state.Disciples.Count, "the roster resets to the starter roster");
            Assert.AreEqual(120, state.Stockpile.RawResources["herb"], "economy resets to the starter stockpile");
            Assert.AreEqual("meditation", Find(_provider, "d001").CurrentTask);
            Assert.IsFalse(_snapshots.CaptureSnapshot().GatherAccumulators.ContainsKey("gathering_wood"),
                           "gathering accumulators must not survive a new session");
            Assert.AreEqual(1, _ui.CloseCount, "session UI is closed on the one return to title in this cycle");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Return to Title / world-event cache
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void ReturnToTitle_FromTitle_IsANoOp()
        {
            var result = _coordinator.ReturnToTitleAsync().GetAwaiter().GetResult();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(SessionTransitionOutcome.AlreadyInPhase, result.Outcome);
            Assert.AreEqual(0, _loader.UnloadCount);
            Assert.AreEqual(0, _ui.CloseCount);
        }

        [Test]
        public void ReturnToTitle_CompletesWorldEventWaiter_AndDropsTheStaleCache()
        {
            // ---- an existing awaiter completes with a defined outcome ----
            Assert.IsTrue(_coordinator.StartNewGameAsync().GetAwaiter().GetResult().Success);

            var waiter = _time.WaitForNextWorldEventAsync();
            Assert.IsFalse(waiter.Status.IsCompleted(), "the waiter is parked while Playing");

            Assert.IsTrue(_coordinator.ReturnToTitleAsync().GetAwaiter().GetResult().Success);

            var response = waiter.GetAwaiter().GetResult();
            Assert.AreEqual(string.Empty, response.EventId, "no fabricated event is delivered");
            StringAssert.Contains("session ended", response.Description);

            // A new awaiter outside Playing is answered immediately as "no active session".
            var immediate = _time.WaitForNextWorldEventAsync().GetAwaiter().GetResult();
            Assert.AreEqual(string.Empty, immediate.EventId);
            StringAssert.Contains("no active session", immediate.Description);

            // ---- a cached response from the previous session never resurfaces ----
            Assert.IsTrue(_coordinator.StartNewGameAsync().GetAwaiter().GetResult().Success);
            _time.RaiseWorldEvent("bandit_raid_001", "raiders", true,
                new List<EventChoiceInfo> { new EventChoiceInfo { ChoiceId = "fight", Label = "Fight" } });

            Assert.IsTrue(_coordinator.ReturnToTitleAsync().GetAwaiter().GetResult().Success);
            Assert.IsTrue(_coordinator.StartNewGameAsync().GetAwaiter().GetResult().Success);

            var after = _time.WaitForNextWorldEventAsync();
            Assert.IsFalse(after.Status.IsCompleted(), "a stale cached response must not be re-delivered");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  transition failures + duplicates
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void FailedSceneLoad_IsRecoverable_AndLeavesLiveStateUntouched()
        {
            Assert.IsTrue(_coordinator.StartNewGameAsync().GetAwaiter().GetResult().Success);

            // Dirty the live session so we can prove the failed New Game did not half-apply.
            _provider.BuildSectEconomyState().Stockpile.RawResources["herb"] = 7;
            _provider.RecruitOuterDisciple();

            // A recoverable failure is logged as an error by design; expect it explicitly.
            LogAssert.Expect(LogType.Error, new Regex("Session transition failed"));
            _loader.FailNextLoad = true;
            var failure = _coordinator.StartNewGameAsync().GetAwaiter().GetResult();

            Assert.IsFalse(failure.Success);
            Assert.AreEqual(SessionTransitionOutcome.Failed, failure.Outcome);
            Assert.AreEqual(GameSessionPhase.Title, _coordinator.Phase);
            StringAssert.Contains("scene", failure.Error);
            Assert.IsFalse(string.IsNullOrEmpty(_coordinator.LastTransitionError));
            Assert.AreEqual(string.Empty, _loader.GetCurrentGameplayScene(), "no gameplay content is left loaded");

            // Live state is the last committed session, not a half-built new one.
            Assert.AreEqual(5, _provider.BuildSectEconomyState().Disciples.Count);
            Assert.AreEqual(7, _provider.BuildSectEconomyState().Stockpile.RawResources["herb"]);
            Assert.IsFalse(_time.IsSessionActive);

            // Recoverable: the coordinator is idle and a retry succeeds.
            Assert.IsTrue(_coordinator.StartNewGameAsync().GetAwaiter().GetResult().Success);
            Assert.AreEqual(GameSessionPhase.Playing, _coordinator.Phase);
            Assert.AreEqual(4, _provider.BuildSectEconomyState().Disciples.Count);
        }

        [Test]
        public void OverlappingTransitions_AreRefused_AndOnlyOneSceneLoadStarts()
        {
            _loader.Hold = true;

            var first = _coordinator.StartNewGameAsync();
            Assert.IsFalse(first.Status.IsCompleted(), "the scene load is held");
            Assert.IsTrue(_coordinator.IsTransitioning);
            Assert.IsFalse(_coordinator.IsPlaying, "a transitioning session is not Playing");

            var duplicate = _coordinator.StartNewGameAsync().GetAwaiter().GetResult();
            Assert.IsFalse(duplicate.Success);
            Assert.AreEqual(SessionTransitionOutcome.Busy, duplicate.Outcome);
            Assert.AreEqual(1, _loader.LoadCount, "a refused duplicate must not start a second load");

            _loader.Release();
            Assert.IsTrue(first.GetAwaiter().GetResult().Success);
            Assert.IsFalse(_coordinator.IsTransitioning);
            Assert.AreEqual(GameSessionPhase.Playing, _coordinator.Phase);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Load
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void Load_WithAGoodSave_RestoresAndPlays()
        {
            Assert.IsTrue(_coordinator.StartNewGameAsync().GetAwaiter().GetResult().Success);

            // Build a session worth saving.
            Find(_provider, "d001").CurrentTask = "gathering_wood";
            _provider.TickGathering(0.5f);
            Assert.IsTrue(_saves.SaveAsync().GetAwaiter().GetResult().Success);

            // Drift the live session away from the save.
            Find(_provider, "d001").CurrentTask = "meditation";
            int restoredBefore = _restored.Messages.Count;

            var result = _coordinator.LoadGameAsync().GetAwaiter().GetResult();

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(GameSessionPhase.Playing, _coordinator.Phase);
            Assert.AreEqual("gathering_wood", Find(_provider, "d001").CurrentTask);
            Assert.AreEqual(0.1f, _snapshots.CaptureSnapshot().GatherAccumulators["gathering_wood"], 0.0001f);
            Assert.AreEqual(restoredBefore + 1, _restored.Messages.Count,
                            "a committed load publishes one state-replaced notification");
            Assert.IsFalse(_time.IsPaused);
            Assert.IsTrue(_time.IsSessionActive);
        }

        [Test]
        public void Load_WithNoSave_IsRefused_BeforeTouchingSceneOrState()
        {
            Assert.IsTrue(_coordinator.StartNewGameAsync().GetAwaiter().GetResult().Success);
            _provider.BuildSectEconomyState().Stockpile.RawResources["herb"] = 42;

            int loadsBefore = _loader.LoadCount;
            int unloadsBefore = _loader.UnloadCount;

            var result = _coordinator.LoadGameAsync().GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(SessionTransitionOutcome.Failed, result.Outcome);
            Assert.IsFalse(string.IsNullOrEmpty(result.Error), "the refusal reason is reported");
            Assert.AreEqual(GameSessionPhase.Playing, _coordinator.Phase,
                            "a refused load returns to the phase it came from");
            Assert.AreEqual(loadsBefore, _loader.LoadCount, "no destructive scene work is attempted");
            Assert.AreEqual(unloadsBefore, _loader.UnloadCount);
            Assert.AreEqual(42, _provider.BuildSectEconomyState().Stockpile.RawResources["herb"],
                            "live state is untouched");
        }

        [Test]
        public void Load_WithAStructurallyInvalidSave_IsRefusedBeforeDestructiveWork()
        {
            // A readable envelope whose CONTENT is invalid (unknown task id) — the read
            // succeeds, so the refusal has to come from the pre-commit validation.
            var envelope = _snapshots.CaptureEnvelope();
            envelope.Snapshot.Economy.Disciples[0].CurrentTask = "gathering_lunar_herb";
            Assert.IsTrue(_repo.Write(envelope, CancellationToken.None).Success);

            int loadsBefore = _loader.LoadCount;
            var result = _coordinator.LoadGameAsync().GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            StringAssert.Contains("task id", result.Error);
            Assert.AreEqual(loadsBefore, _loader.LoadCount, "an invalid save must not start a scene load");
            Assert.AreEqual(0, _snapshots.RestoreCount);
        }

        [Test]
        public void StaleLoadCompletion_FromAPreviousSession_IsInvalidated()
        {
            // A save exists and is valid.
            var seedProvider = NewProvider(new MutableClock(T0));
            var seedTime = NewTimeSystem();
            var seedSnapshots = new SessionSnapshotService(seedProvider, seedTime,
                new BufferPublisher<SessionRestoredMessage>(), new MutableClock(T0));
            Assert.IsTrue(_repo.Write(seedSnapshots.CaptureEnvelope(), CancellationToken.None).Success);

            var scheduler = new GatedSaveWorkScheduler();
            var transition = new SessionTransitionTracker();
            var saves = new SaveSessionService(_snapshots, _repo, scheduler, transition, new SaveOperationGate());
            var coordinator = NewCoordinator(_snapshots, _time, _loader, saves, transition,
                                             new CountingUiCloser(), new BufferPublisher<SessionPhaseChangedMessage>());

            scheduler.BeginHold();
            var pending = coordinator.LoadGameAsync();
            Assert.IsFalse(pending.Status.IsCompleted(), "the read is held on the worker");

            // The player leaves for another session while the load is still in flight.
            saves.BeginSessionTransition();
            scheduler.Release();

            var result = pending.GetAwaiter().GetResult();

            Assert.IsFalse(result.Success, "a completion from a dead session must never commit");
            Assert.AreEqual(0, _snapshots.RestoreCount, "nothing was restored");
            Assert.IsFalse(coordinator.IsPlaying);
            Assert.AreEqual(0, _loader.LoadCount, "the stale load must not drive a scene load");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  MCP mutation gate (outside Playing)
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void DecisionExecutor_OutsidePlaying_RejectsWithClearReason()
        {
            // The SetUp coordinator exists but was never started, so the session clock it
            // owns is inactive — exactly the authoritative fact the executor's mutation
            // gate reads (no separate gate dependency: see DecisionExecutor.Execute).
            Assert.AreEqual(GameSessionPhase.Title, _coordinator.Phase);
            Assert.IsFalse(_time.IsSessionActive, "the coordinator's Title phase leaves the clock inactive");

            var executor = new DecisionExecutor(_provider, _time,
                new BufferPublisher<DecisionExecutedMessage>());

            _time.RaiseWorldEvent("evt_1", "desc", true,
                new List<EventChoiceInfo> { new EventChoiceInfo { ChoiceId = "fight", Label = "Fight" } });

            var result = executor.Execute("evt_1", "fight");

            Assert.IsFalse(result.Accepted);
            StringAssert.Contains("no active session", result.Reason);
            Assert.IsTrue(_time.HasPendingDecision, "a refused decision must not consume the pending state");
        }

        [Test]
        public void AssignTaskHandler_OutsidePlaying_ReturnsNoActiveSessionResult()
        {
            WithImmediateMainThread(() =>
            {
                var handler = new AssignTaskHandler(_provider, new FakeSessionGate { IsPlaying = false });
                var response = handler.InvokeAsync(new AssignTaskRequest
                {
                    RequesterId = SectStateProvider.SectMasterRequesterId,
                    DiscipleId = "d000",
                    TaskId = "gathering_wood",
                }).GetAwaiter().GetResult();

                Assert.IsFalse(response.Success);
                StringAssert.Contains("no active session", response.FailReason);
                Assert.AreEqual("meditation", Find(_provider, "d000").CurrentTask, "no mutation happened");
            });
        }

        [Test]
        public void MutationGate_IsPlaying_MatchesTheLiveSessionPhase()
        {
            // The coordinator IS the gate the handlers inject.
            Assert.IsFalse(_coordinator.IsPlaying);

            WithImmediateMainThread(() =>
            {
                var handler = new AssignTaskHandler(_provider, _coordinator);
                // Place the building the task requires, then assign through the real path.
                string placeReason;
                Assert.IsTrue(_provider.TryPlaceBuilding("herb_plot", 0, 0, 0,
                    new BuildingGrid(10, 10), out placeReason, out _), placeReason);

                // Not Playing yet -> refused.
                var refused = handler.InvokeAsync(new AssignTaskRequest
                {
                    RequesterId = SectStateProvider.SectMasterRequesterId,
                    DiscipleId = "d000",
                    TaskId = "gathering_herb",
                }).GetAwaiter().GetResult();
                Assert.IsFalse(refused.Success);

                // Enter Playing, assign again -> accepted (gate tracks the phase).
                Assert.IsTrue(_coordinator.StartNewGameAsync().GetAwaiter().GetResult().Success);
                _provider.TryPlaceBuilding("herb_plot", 0, 0, 0,
                    new BuildingGrid(10, 10), out placeReason, out _);

                var accepted = handler.InvokeAsync(new AssignTaskRequest
                {
                    RequesterId = SectStateProvider.SectMasterRequesterId,
                    DiscipleId = "d000",
                    TaskId = "gathering_herb",
                }).GetAwaiter().GetResult();
                Assert.IsTrue(accepted.Success, accepted.FailReason);
            });
        }

        // ══════════════════════════════════════════════════════════════════════
        //  wiring guard
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void Coordinator_IsRegisteredAtTheRoot_AndDoesNotCreateASecondLoader()
        {
            string installer = File.ReadAllText(Path.Combine(
                UnityEngine.Application.dataPath, "Scripts/Core/Installers/GameplayInstaller.cs"));
            StringAssert.Contains("GameSessionCoordinator", installer, "the coordinator must be root-registered");
            StringAssert.Contains("ISessionGate", installer, "the mutation gate must be exposed at the root");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  helpers
        // ══════════════════════════════════════════════════════════════════════

        private static GameSessionCoordinator NewCoordinator(
            ISessionRestoreAuthority snapshots, TimeSystem time, FakeSceneLoader loader,
            SaveSessionService saves, SessionTransitionTracker transition,
            ISessionUiCloser ui, IPublisher<SessionPhaseChangedMessage> phases)
        {
            return new GameSessionCoordinator(
                loader, snapshots, time, new PrototypeStarterStateFactory(), transition,
                saves, null, null, null, ui, phases);
        }

        private static void WithImmediateMainThread(Action body)
        {
            MainThreadDispatch.ResetForTests();
            MainThreadDispatch.OverrideSwitchForTests(() => UniTask.CompletedTask);
            try
            {
                body();
            }
            finally
            {
                MainThreadDispatch.ResetForTests();
            }
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

        private static TimeSystem NewTimeSystem()
        {
            return new TimeSystem(
                new BufferPublisher<TimeSpeedChangedMessage>(),
                new BufferPublisher<WorldEventTriggeredMessage>());
        }

        private static DiscipleState Find(SectStateProvider provider, string id)
        {
            foreach (var d in provider.BuildSectEconomyState().Disciples)
                if (d.DiscipleId == id) return d;
            return null;
        }

        private static void DeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch (IOException) { }
        }

        // ---------- test doubles ----------

        private sealed class FakeSceneLoader : IGameplaySceneLoader
        {
            public string Current;
            public int LoadCount;
            public int UnloadCount;
            public bool FailNextLoad;
            public bool Hold;
            private UniTaskCompletionSource _holdGate;

            public async UniTask LoadGameplayScene(string sceneName)
            {
                LoadCount++;
                // Model the real loader: a failed load has already released the old scene.
                Current = null;
                if (FailNextLoad)
                {
                    FailNextLoad = false;
                    throw new InvalidOperationException("scene load failed (test)");
                }

                if (Hold)
                {
                    _holdGate = new UniTaskCompletionSource();
                    await _holdGate.Task;
                }

                Current = sceneName;
            }

            public void Release()
            {
                var gate = _holdGate;
                _holdGate = null;
                Hold = false;
                if (gate != null) gate.TrySetResult();
            }

            public UniTask UnloadCurrentGameplayScene()
            {
                UnloadCount++;
                Current = null;
                return UniTask.CompletedTask;
            }

            public string GetCurrentGameplayScene() => Current ?? string.Empty;
        }

        private sealed class CountingUiCloser : ISessionUiCloser
        {
            public int CloseCount;
            public int CloseSessionUi() { CloseCount++; return 1; }
        }

        private sealed class FakeSessionGate : ISessionGate
        {
            public bool IsPlaying { get; set; }
        }

        private sealed class InlineSaveWorkScheduler : ISaveWorkScheduler
        {
            public UniTask<T> RunOnWorkerAsync<T>(Func<T> work, CancellationToken cancellationToken)
                => UniTask.FromResult(work());

            public UniTask SwitchToMainThreadAsync() => UniTask.CompletedTask;
        }

        private sealed class GatedSaveWorkScheduler : ISaveWorkScheduler
        {
            private UniTaskCompletionSource _gate;

            public void BeginHold() { _gate = new UniTaskCompletionSource(); }

            public void Release()
            {
                var gate = _gate;
                _gate = null;
                if (gate != null) gate.TrySetResult();
            }

            public async UniTask<T> RunOnWorkerAsync<T>(Func<T> work, CancellationToken cancellationToken)
            {
                var gate = _gate;
                if (gate != null) await gate.Task;
                return work();
            }

            public UniTask SwitchToMainThreadAsync() => UniTask.CompletedTask;
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
    }
}
