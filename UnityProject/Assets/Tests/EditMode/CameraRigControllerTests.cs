using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Xianxia.Sect.Messages;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    // Fakes for the CameraRigController ports (CameraRigPorts.cs). Everything
    // is synchronous: the fake bus dispatches handlers inline, the fake main
    // thread queue completes immediately, and the fake clock completes
    // NextFrame instantly — so transitions run to completion inside the
    // Dispatch call and assertions can be made right after.

    internal sealed class FakeRigBus : IRigMessageBus
    {
        public Action<SceneLoadedMessage> SceneLoaded;
        public Action<SceneUnloadedMessage> SceneUnloaded;
        public Action<BuildModeStartedMessage> BuildStarted;
        public Action<BuildModeEndedMessage> BuildEnded;

        public List<BuildModeStartedMessage> PublishedStarted = new List<BuildModeStartedMessage>();
        public List<BuildModeEndedMessage> PublishedEnded = new List<BuildModeEndedMessage>();

        public IDisposable SubscribeSceneLoaded(Action<SceneLoadedMessage> handler)
        {
            SceneLoaded += handler;
            return new Subscription(() => SceneLoaded -= handler);
        }

        public IDisposable SubscribeSceneUnloaded(Action<SceneUnloadedMessage> handler)
        {
            SceneUnloaded += handler;
            return new Subscription(() => SceneUnloaded -= handler);
        }

        public IDisposable SubscribeBuildModeStarted(Action<BuildModeStartedMessage> handler)
        {
            BuildStarted += handler;
            return new Subscription(() => BuildStarted -= handler);
        }

        public IDisposable SubscribeBuildModeEnded(Action<BuildModeEndedMessage> handler)
        {
            BuildEnded += handler;
            return new Subscription(() => BuildEnded -= handler);
        }

        public void PublishBuildModeStarted(string sourceId, string ghostId)
        {
            PublishedStarted.Add(new BuildModeStartedMessage { SourceId = sourceId, GhostId = ghostId });
        }

        public void PublishBuildModeEnded(string sourceId, bool confirmed)
        {
            PublishedEnded.Add(new BuildModeEndedMessage { SourceId = sourceId, Confirmed = confirmed });
        }

        public void DispatchBuildStarted(BuildModeStartedMessage message) => BuildStarted?.Invoke(message);
        public void DispatchBuildEnded(BuildModeEndedMessage message) => BuildEnded?.Invoke(message);
        public void DispatchSceneLoaded(SceneLoadedMessage message) => SceneLoaded?.Invoke(message);
        public void DispatchSceneUnloaded(SceneUnloadedMessage message) => SceneUnloaded?.Invoke(message);

        private sealed class Subscription : IDisposable
        {
            private Action _dispose;
            public Subscription(Action dispose) { _dispose = dispose; }
            public void Dispose() { _dispose?.Invoke(); _dispose = null; }
        }
    }

    internal sealed class FakeEnvironment : ICameraRigEnvironment
    {
        public bool SceneLoaded;
        public ICameraRigCameraView Camera;
        public RigGridSnapshot Grid = RigGridSnapshot.None;
        public Dictionary<string, IRigGhost> Ghosts = new Dictionary<string, IRigGhost>();

        public bool IsSceneLoaded(string sceneName) => SceneLoaded;

        public bool TryGetSceneCamera(string sceneName, out ICameraRigCameraView camera, out RigGridSnapshot grid)
        {
            camera = Camera;
            grid = Grid;
            return Camera != null;
        }

        public bool TryFindGhost(string ghostId, out IRigGhost ghost)
        {
            return Ghosts.TryGetValue(ghostId, out ghost);
        }
    }

    internal sealed class FakeSpriteMetrics : ICellSpriteMetrics
    {
        public int WidthPx = 128;
        public int HeightPx = 66;
        public int Ppu = 100;

        public bool TryGetCellSprite(string spriteName, out int widthPx, out int heightPx, out int ppu)
        {
            widthPx = WidthPx;
            heightPx = HeightPx;
            ppu = Ppu;
            return WidthPx > 0;
        }
    }

    internal sealed class FakeMainThreadQueue : IMainThreadQueue
    {
        public UniTask SwitchToMainThread() => UniTask.CompletedTask;
    }

    internal sealed class FakeRigClock : IRigClock
    {
        public float UnscaledDeltaTime { get; set; } = 1f / 60f;
        public int NextFrameCalls;

        public UniTask NextFrame(CancellationToken token)
        {
            NextFrameCalls++;
            return UniTask.CompletedTask;
        }
    }

    internal sealed class FakeCameraView : ICameraRigCameraView
    {
        public bool Orthographic { get; set; }
        public float Aspect { get; set; } = 16f / 9f;
        public float OrthographicSize { get; set; }
        public Vector3 Position { get; set; } = new Vector3(4f, 3f, -10f);
        public Quaternion Rotation { get; set; } = Quaternion.identity;
        public string SceneName { get; set; } = "TestGameplayScene";
        public bool IsValid { get; set; } = true;
    }

    internal sealed class FakeGhost : IRigGhost
    {
        public string Name { get; set; } = "Ghost";
        public Vector3 Position { get; set; }
        public bool IsValid { get; set; } = true;
    }

    /// <summary>
    /// EditMode tests for the refactored CameraRigController: it reacts to bus
    /// messages only through IRigMessageBus, resolves the camera/ghost/grid
    /// through ICameraRigEnvironment, measures framing through
    /// ICellSpriteMetrics, and animates through IRigClock — every port faked.
    /// </summary>
    public class CameraRigControllerTests
    {
        private FakeRigBus _bus;
        private FakeEnvironment _environment;
        private FakeSpriteMetrics _sprites;
        private FakeRigClock _clock;
        private CameraFramingConfig _framing;
        private CameraRigController _rig;

        [SetUp]
        public void SetUp()
        {
            // Quiet diagnostics for clean test output; individual tests swap
            // in collectors when they assert on log content.
            CameraRigLogger.Info = _ => { };
            CameraRigLogger.Warn = _ => { };
            _bus = new FakeRigBus();
            _environment = new FakeEnvironment();
            _sprites = new FakeSpriteMetrics();
            _clock = new FakeRigClock();
            _framing = new CameraFramingConfig();
            _rig = CreateRig();
        }

        [TearDown]
        public void TearDown()
        {
            CameraRigLogger.Info = message => Debug.Log(message);
            CameraRigLogger.Warn = message => Debug.LogWarning(message);
            _rig?.Dispose();
        }

        private CameraRigController CreateRig()
        {
            return new CameraRigController(
                _bus, _environment, _sprites,
                new FakeMainThreadQueue(), _clock, _framing);
        }

        private void LoadGameplaySceneWithCamera(float aspect)
        {
            _environment.SceneLoaded = true;
            _environment.Camera = new FakeCameraView { Aspect = aspect };
            _bus.DispatchSceneLoaded(new SceneLoadedMessage { SceneName = "TestGameplayScene" });
        }

        // -------------------------------------------------------------
        // Subscriptions
        // -------------------------------------------------------------

        [Test]
        public void Start_SubscribesToAllFourBusChannels()
        {
            _rig.Start();
            Assert.IsNotNull(_bus.SceneLoaded);
            Assert.IsNotNull(_bus.SceneUnloaded);
            Assert.IsNotNull(_bus.BuildStarted);
            Assert.IsNotNull(_bus.BuildEnded);
        }

        [Test]
        public void Dispose_UnsubscribesEverything()
        {
            _rig.Start();
            _rig.Dispose();
            Assert.IsNull(_bus.SceneLoaded);
            Assert.IsNull(_bus.SceneUnloaded);
            Assert.IsNull(_bus.BuildStarted);
            Assert.IsNull(_bus.BuildEnded);
        }

        // -------------------------------------------------------------
        // Scene loading → framing measurement
        // -------------------------------------------------------------

        [Test]
        public void SceneLoaded_MeasuresSpriteIntoFramingConfig()
        {
            _rig.Start();
            _environment.Grid = new RigGridSnapshot(true, new Vector3(1f, 1f, 1f), false);
            LoadGameplaySceneWithCamera(3.048f);

            Assert.IsTrue(_framing.IsMeasured);
            Assert.AreEqual(128, _framing.GridSpriteWidthPx);
            Assert.AreEqual(66, _framing.GridSpriteHeightPx);
            Assert.AreEqual(100, _framing.GridSpritePpu);
            Assert.AreEqual(1.28f, _framing.TileWidthWorld, 1e-6f);
            Assert.AreEqual(0.66f, _framing.TileHeightWorld, 1e-6f);
            Assert.IsFalse(_framing.GridIsIsometric);
        }

        [Test]
        public void SceneLoaded_SetsOverviewOrthoSize_FromFormula()
        {
            _rig.Start();
            LoadGameplaySceneWithCamera(3.048f);

            float expected = _framing.ComputeOrthoSize(CameraFramingConfig.OverviewVisibleTiles, 3.048f);
            Assert.AreEqual(expected, _environment.Camera.OrthographicSize, 1e-5f);
        }

        [Test]
        public void SceneLoaded_ForcesOrthographic_AndStoresRotation()
        {
            _rig.Start();
            var rotation = Quaternion.Euler(30f, 45f, 0f);
            _environment.SceneLoaded = true;
            _environment.Camera = new FakeCameraView { Rotation = rotation };
            _bus.DispatchSceneLoaded(new SceneLoadedMessage { SceneName = "TestGameplayScene" });

            Assert.IsTrue(_environment.Camera.Orthographic);

            // Tick re-locks the stored rotation after any drift.
            _environment.Camera.Rotation = Quaternion.identity;
            _rig.Tick();
            Assert.AreEqual(rotation, _environment.Camera.Rotation);
        }

        [Test]
        public void SceneLoaded_OtherSceneName_IsIgnored()
        {
            _rig.Start();
            _environment.SceneLoaded = true;
            _environment.Camera = new FakeCameraView();
            _bus.DispatchSceneLoaded(new SceneLoadedMessage { SceneName = "SomeOtherScene" });
            Assert.IsFalse(_framing.IsMeasured);
        }

        [Test]
        public void SceneLoadedWithoutCamera_LogsWarningAndDoesNotThrow()
        {
            var warnings = new List<string>();
            CameraRigLogger.Warn = warnings.Add;
            _rig.Start();

            Assert.DoesNotThrow(() =>
                _bus.DispatchSceneLoaded(new SceneLoadedMessage { SceneName = "TestGameplayScene" }));

            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("No enabled Camera", warnings[0]);
            Assert.IsFalse(_framing.IsMeasured);
        }

        // -------------------------------------------------------------
        // Build mode: enter/exit publish through the bus (single code path)
        // -------------------------------------------------------------

        [Test]
        public void EnterPlacement_PublishesStartedMessageThroughBus()
        {
            _rig.EnterPlacementAsync("ghost_1").Forget();

            Assert.AreEqual(1, _bus.PublishedStarted.Count);
            Assert.AreEqual("CameraRigController", _bus.PublishedStarted[0].SourceId);
            Assert.AreEqual("ghost_1", _bus.PublishedStarted[0].GhostId);
            Assert.AreEqual(0, _bus.PublishedEnded.Count);
        }

        [Test]
        public void EnterPlacement_WithoutGhost_PublishesNullGhostId()
        {
            _rig.EnterPlacementAsync().Forget();

            Assert.AreEqual(1, _bus.PublishedStarted.Count);
            Assert.IsNull(_bus.PublishedStarted[0].GhostId);
        }

        [Test]
        public void ExitPlacement_PublishesEndedMessageConfirmed()
        {
            _rig.ExitPlacementAsync().Forget();

            Assert.AreEqual(1, _bus.PublishedEnded.Count);
            Assert.AreEqual("CameraRigController", _bus.PublishedEnded[0].SourceId);
            Assert.IsTrue(_bus.PublishedEnded[0].Confirmed);
        }

        // -------------------------------------------------------------
        // Build mode: camera transitions driven by bus messages
        // -------------------------------------------------------------

        [Test]
        public void BuildModeStarted_TransitionsCameraToPlacementPreset()
        {
            _rig.Start();
            LoadGameplaySceneWithCamera(3.048f);

            float overviewSize = _environment.Camera.OrthographicSize;
            _bus.DispatchBuildStarted(new BuildModeStartedMessage { SourceId = "test" });

            // Zooming in = smaller orthoSize (10 visible tiles vs 25).
            float expected = _framing.ComputeOrthoSize(CameraFramingConfig.PlacementVisibleTiles, 3.048f);
            Assert.AreEqual(expected, _environment.Camera.OrthographicSize, 1e-5f);
            Assert.Less(_environment.Camera.OrthographicSize, overviewSize);
        }

        [Test]
        public void BuildModeEnded_TransitionsCameraBackToOverviewPreset()
        {
            _rig.Start();
            LoadGameplaySceneWithCamera(3.048f);

            float overviewSize = _environment.Camera.OrthographicSize;
            _bus.DispatchBuildStarted(new BuildModeStartedMessage { SourceId = "test" });
            _bus.DispatchBuildEnded(new BuildModeEndedMessage { SourceId = "test", Confirmed = true });

            Assert.AreEqual(overviewSize, _environment.Camera.OrthographicSize, 1e-5f);
        }

        [Test]
        public void BuildModeStarted_ResolvesGhostById_AndFollowsItOnTick()
        {
            _rig.Start();
            LoadGameplaySceneWithCamera(3.048f);
            var ghost = new FakeGhost { Name = "ghost_x", Position = new Vector3(2f, 5f, 0f) };
            _environment.Ghosts["ghost_x"] = ghost;

            _bus.DispatchBuildStarted(new BuildModeStartedMessage { SourceId = "test", GhostId = "ghost_x" });

            // Transition completed inline (fake clock): camera sits at the ghost.
            Assert.AreEqual(2f, _environment.Camera.Position.x, 0.05f);
            Assert.AreEqual(5f, _environment.Camera.Position.y, 0.05f);
            Assert.AreEqual(-10f, _environment.Camera.Position.z, 1e-4f); // Z stays with camera

            // Moving the ghost drags the camera on subsequent Ticks.
            ghost.Position = new Vector3(6f, 5f, 0f);
            for (int i = 0; i < 60; i++) _rig.Tick();
            Assert.AreEqual(6f, _environment.Camera.Position.x, 0.05f);

            // After build mode ends the follow stops (camera back at overview).
            _bus.DispatchBuildEnded(new BuildModeEndedMessage { SourceId = "test" });
            float xAfterEnd = _environment.Camera.Position.x;
            for (int i = 0; i < 10; i++) _rig.Tick();
            Assert.AreEqual(xAfterEnd, _environment.Camera.Position.x, 1e-5f);
        }

        [Test]
        public void BuildModeStarted_WithoutCamera_AppliesWhenSceneLoadsLater()
        {
            _rig.Start(); // no scene/camera in the fake environment yet
            _environment.Ghosts["ghost_late"] = new FakeGhost { Name = "ghost_late" };

            Assert.DoesNotThrow(() =>
                _bus.DispatchBuildStarted(new BuildModeStartedMessage { SourceId = "test", GhostId = "ghost_late" }));
            Assert.IsFalse(_framing.IsMeasured); // nothing to frame yet

            // The pending placement applies once the scene (with camera) loads.
            LoadGameplaySceneWithCamera(3.048f);
            Assert.AreEqual(_framing.ComputeOrthoSize(CameraFramingConfig.PlacementVisibleTiles, 3.048f),
                _environment.Camera.OrthographicSize, 1e-5f);
        }

        [Test]
        public void BuildModeStarted_UnknownGhostId_DoesNotThrow()
        {
            _rig.Start();
            LoadGameplaySceneWithCamera(3.048f);

            Assert.DoesNotThrow(() =>
                _bus.DispatchBuildStarted(new BuildModeStartedMessage { SourceId = "test", GhostId = "missing" }));
            // Placement preset still applies even without a ghost to follow.
            Assert.AreEqual(_framing.ComputeOrthoSize(CameraFramingConfig.PlacementVisibleTiles, 3.048f),
                _environment.Camera.OrthographicSize, 1e-5f);
        }

        // -------------------------------------------------------------
        // Scene unload cleanup
        // -------------------------------------------------------------

        [Test]
        public void SceneUnloaded_OfCameraScene_ClearsCameraState()
        {
            _rig.Start();
            LoadGameplaySceneWithCamera(3.048f);
            Assert.IsTrue(_framing.IsMeasured);

            var camera = (FakeCameraView)_environment.Camera;
            camera.IsValid = false; // destroyed with the scene
            _bus.DispatchSceneUnloaded(new SceneUnloadedMessage { SceneName = "TestGameplayScene" });

            // The controller dropped its camera: Tick and build-mode messages
            // are safe no-ops on the camera path.
            Assert.DoesNotThrow(() => _rig.Tick());
            Assert.DoesNotThrow(() =>
                _bus.DispatchBuildStarted(new BuildModeStartedMessage { SourceId = "test" }));
        }

        [Test]
        public void SceneUnloaded_OfOtherScene_IsIgnored()
        {
            _rig.Start();
            LoadGameplaySceneWithCamera(3.048f);
            Assert.IsTrue(_framing.IsMeasured);

            _bus.DispatchSceneUnloaded(new SceneUnloadedMessage { SceneName = "AnotherScene" });
            Assert.IsTrue(_framing.IsMeasured); // untouched
        }

        // -------------------------------------------------------------
        // Tick behavior
        // -------------------------------------------------------------

        [Test]
        public void Tick_WithoutCamera_IsNoOp()
        {
            _rig.Start();
            Assert.DoesNotThrow(() => _rig.Tick());
        }

        [Test]
        public void Tick_FollowSharpness_ConvergesTowardGhost()
        {
            _rig.Start();
            _environment.SceneLoaded = true;
            _environment.Camera = new FakeCameraView { Position = new Vector3(0f, 0f, -10f), Aspect = 3.048f };
            var ghost = new FakeGhost { Name = "g", Position = new Vector3(10f, 0f, 0f) };
            _environment.Ghosts["g"] = ghost;
            _bus.DispatchSceneLoaded(new SceneLoadedMessage { SceneName = "TestGameplayScene" });

            _bus.DispatchBuildStarted(new BuildModeStartedMessage { SourceId = "test", GhostId = "g" });
            for (int i = 0; i < 200; i++) _rig.Tick();

            Vector3 pos = _environment.Camera.Position;
            Assert.AreEqual(10f, pos.x, 0.05f);  // converged to ghost X
            Assert.AreEqual(-10f, pos.z, 1e-4f); // Z stays with the camera
        }
    }
}
