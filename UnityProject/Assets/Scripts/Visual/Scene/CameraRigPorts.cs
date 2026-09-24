using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using UnityEngine;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.Visual
{
    // --- Ports (test seams) for CameraRigController -------------------------
    //
    // Everything the rig needs from Unity or the message bus is expressed as a
    // small interface here, so EditMode tests can drive the controller with
    // fakes: no scene, no Camera, no Resources, no PlayerLoop. Production
    // implementations live at the bottom of this file and are registered in
    // GameLifetimeScope; MessagePipe itself never leaks into the controller.

    /// <summary>
    /// All bus traffic the rig participates in (subscriptions + publishing).
    /// The production adapter wraps MessagePipe; tests record/dispatch instead.
    /// </summary>
    public interface IRigMessageBus
    {
        IDisposable SubscribeSceneLoaded(Action<SceneLoadedMessage> handler);
        IDisposable SubscribeSceneUnloaded(Action<SceneUnloadedMessage> handler);
        IDisposable SubscribeBuildModeStarted(Action<BuildModeStartedMessage> handler);
        IDisposable SubscribeBuildModeEnded(Action<BuildModeEndedMessage> handler);

        /// <summary>Publishes BuildModeStartedMessage (enter placement).</summary>
        void PublishBuildModeStarted(string sourceId, string ghostId);

        /// <summary>Publishes BuildModeEndedMessage (confirm/cancel/exit).</summary>
        void PublishBuildModeEnded(string sourceId, bool confirmed);
    }

    /// <summary>The placement ghost the camera follows (transforms, not GameObjects).</summary>
    public interface IRigGhost
    {
        string Name { get; }
        Vector3 Position { get; }
        /// <summary>False once the backing transform is destroyed.</summary>
        bool IsValid { get; }
    }

    /// <summary>Read-only Grid info the rig needs for framing (no Grid component dependency).</summary>
    public readonly struct RigGridSnapshot
    {
        public readonly bool HasGrid;
        public readonly Vector3 CellSize;
        public readonly bool IsIsometric;

        public RigGridSnapshot(bool hasGrid, Vector3 cellSize, bool isIsometric)
        {
            HasGrid = hasGrid;
            CellSize = cellSize;
            IsIsometric = isIsometric;
        }

        public static RigGridSnapshot None => new RigGridSnapshot(false, Vector3.one, false);
    }

    /// <summary>Scene-level queries the rig performs on the gameplay scene.</summary>
    public interface ICameraRigEnvironment
    {
        /// <summary>True when a scene with this name is currently loaded.</summary>
        bool IsSceneLoaded(string sceneName);

        /// <summary>
        /// Resolves the gameplay scene's enabled camera and Grid snapshot.
        /// False when the scene is not loaded or has no enabled camera
        /// (multiple cameras log a warning and keep the first).
        /// </summary>
        bool TryGetSceneCamera(string sceneName, out ICameraRigCameraView camera, out RigGridSnapshot grid);

        /// <summary>Finds a named ghost object in the gameplay scene.</summary>
        bool TryFindGhost(string ghostId, out IRigGhost ghost);
    }

    /// <summary>Cell-sprite measurement for framing (gridblock_0).</summary>
    public interface ICellSpriteMetrics
    {
        /// <summary>
        /// Loads the cell sprite named <paramref name="spriteName"/> under
        /// <see cref="CameraFramingConfig.GridSpritePath"/>. False when absent
        /// (out params keep their defaults: 0/0 px and 100 PPU fallback).
        /// </summary>
        bool TryGetCellSprite(string spriteName, out int widthPx, out int heightPx, out int ppu);
    }

    /// <summary>Main-thread hop for handlers that may arrive off-thread.</summary>
    public interface IMainThreadQueue
    {
        UniTask SwitchToMainThread();
    }

    /// <summary>Frame clock + smoothed frame timing (no Time.* in the controller).</summary>
    public interface IRigClock
    {
        float UnscaledDeltaTime { get; }

        /// <summary>
        /// Waits for the next player-loop frame. Tests complete this instantly,
        /// which makes transitions run to completion synchronously.
        /// </summary>
        UniTask NextFrame(CancellationToken token);
    }

    /// <summary>The minimal camera controls the rig actually drives.</summary>
    public interface ICameraRigCameraView
    {
        bool Orthographic { get; set; }
        float Aspect { get; }
        float OrthographicSize { get; set; }
        Vector3 Position { get; set; }
        Quaternion Rotation { get; set; }
        /// <summary>Scene the camera lives in (unload filtering).</summary>
        string SceneName { get; }
        /// <summary>False once the backing camera is destroyed (scene unload).</summary>
        bool IsValid { get; }
    }

    /// <summary>Static Log indirection so tests can quiet/observe diagnostics.</summary>
    public static class CameraRigLogger
    {
        public static Action<string> Warn = message => Debug.LogWarning(message);
        public static Action<string> Info = message => Debug.Log(message);
    }

    // --- Production adapters ------------------------------------------------

    /// <summary>MessagePipe-backed bus adapter (the only MessagePipe touchpoint).</summary>
    public sealed class MessagePipeRigBus : IRigMessageBus
    {
        private readonly ISubscriber<SceneLoadedMessage> _sceneLoadedSubscriber;
        private readonly ISubscriber<SceneUnloadedMessage> _sceneUnloadedSubscriber;
        private readonly ISubscriber<BuildModeStartedMessage> _buildStartedSubscriber;
        private readonly ISubscriber<BuildModeEndedMessage> _buildEndedSubscriber;
        private readonly IPublisher<BuildModeStartedMessage> _buildStartedPublisher;
        private readonly IPublisher<BuildModeEndedMessage> _buildEndedPublisher;

        public MessagePipeRigBus(
            ISubscriber<SceneLoadedMessage> sceneLoadedSubscriber,
            ISubscriber<SceneUnloadedMessage> sceneUnloadedSubscriber,
            ISubscriber<BuildModeStartedMessage> buildStartedSubscriber,
            ISubscriber<BuildModeEndedMessage> buildEndedSubscriber,
            IPublisher<BuildModeStartedMessage> buildStartedPublisher,
            IPublisher<BuildModeEndedMessage> buildEndedPublisher)
        {
            _sceneLoadedSubscriber = sceneLoadedSubscriber;
            _sceneUnloadedSubscriber = sceneUnloadedSubscriber;
            _buildStartedSubscriber = buildStartedSubscriber;
            _buildEndedSubscriber = buildEndedSubscriber;
            _buildStartedPublisher = buildStartedPublisher;
            _buildEndedPublisher = buildEndedPublisher;
        }

        public IDisposable SubscribeSceneLoaded(Action<SceneLoadedMessage> handler)
        {
            return _sceneLoadedSubscriber.Subscribe(handler);
        }

        public IDisposable SubscribeSceneUnloaded(Action<SceneUnloadedMessage> handler)
        {
            return _sceneUnloadedSubscriber.Subscribe(handler);
        }

        public IDisposable SubscribeBuildModeStarted(Action<BuildModeStartedMessage> handler)
        {
            return _buildStartedSubscriber.Subscribe(handler);
        }

        public IDisposable SubscribeBuildModeEnded(Action<BuildModeEndedMessage> handler)
        {
            return _buildEndedSubscriber.Subscribe(handler);
        }

        public void PublishBuildModeStarted(string sourceId, string ghostId)
        {
            _buildStartedPublisher?.Publish(new BuildModeStartedMessage
            {
                SourceId = sourceId,
                GhostId = ghostId,
            });
        }

        public void PublishBuildModeEnded(string sourceId, bool confirmed)
        {
            _buildEndedPublisher?.Publish(new BuildModeEndedMessage
            {
                SourceId = sourceId,
                Confirmed = confirmed,
            });
        }
    }

    /// <summary>SceneManager/GameObject-backed environment adapter.</summary>
    public sealed class SceneEnvironment : ICameraRigEnvironment
    {
        public bool IsSceneLoaded(string sceneName)
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene.name == sceneName) return true;
            }
            return false;
        }

        public bool TryGetSceneCamera(string sceneName, out ICameraRigCameraView camera, out RigGridSnapshot grid)
        {
            camera = null;
            grid = RigGridSnapshot.None;

            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded) return false;

            Camera foundCamera = null;
            Grid foundGrid = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Camera[] cameras = roots[i].GetComponentsInChildren<Camera>(true);
                for (int c = 0; c < cameras.Length; c++)
                {
                    if (cameras[c] == null || !cameras[c].enabled) continue;
                    if (foundCamera != null)
                    {
                        CameraRigLogger.Warn("[CameraRigController] Multiple enabled Cameras in gameplay scene '" +
                                             scene.name + "'; using the first one.");
                        continue;
                    }
                    foundCamera = cameras[c];
                }

                if (foundGrid == null) foundGrid = roots[i].GetComponentInChildren<Grid>(true);
            }

            if (foundCamera == null) return false;

            camera = new UnityCameraView(foundCamera);
            grid = foundGrid != null
                ? new RigGridSnapshot(true, foundGrid.cellSize, foundGrid.cellLayout == GridLayout.CellLayout.Isometric)
                : RigGridSnapshot.None;
            return true;
        }

        public bool TryFindGhost(string ghostId, out IRigGhost ghost)
        {
            GameObject go = string.IsNullOrEmpty(ghostId) ? null : GameObject.Find(ghostId);
            if (go == null)
            {
                ghost = null;
                return false;
            }
            ghost = new UnityGhostHandle(go.transform);
            return true;
        }
    }

    /// <summary>Resources-backed cell sprite metrics adapter.</summary>
    public sealed class ResourcesCellSpriteMetrics : ICellSpriteMetrics
    {
        public bool TryGetCellSprite(string spriteName, out int widthPx, out int heightPx, out int ppu)
        {
            widthPx = 0;
            heightPx = 0;
            ppu = 100;

            Sprite[] sprites = Resources.LoadAll<Sprite>(CameraFramingConfig.GridSpritePath);
            if (sprites == null || sprites.Length == 0) return false;

            Sprite cellSprite = sprites[0];
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] != null && sprites[i].name == spriteName)
                {
                    cellSprite = sprites[i];
                    break;
                }
            }

            if (cellSprite == null || cellSprite.texture == null) return false;

            widthPx = cellSprite.rect.width >= 1f
                ? Mathf.RoundToInt(cellSprite.rect.width)
                : cellSprite.texture.width;
            heightPx = cellSprite.rect.height >= 1f
                ? Mathf.RoundToInt(cellSprite.rect.height)
                : cellSprite.texture.height;
            ppu = Mathf.RoundToInt(cellSprite.pixelsPerUnit);
            return true;
        }
    }

    /// <summary>UniTask main-thread hop adapter.</summary>
    public sealed class UniTaskMainThreadQueue : IMainThreadQueue
    {
        // UniTask.SwitchToMainThread() returns a SwitchToMainThreadAwaitable
        // (not a UniTask) in this UniTask version, so wrap it in an async
        // method to normalize the port's return type.
        public async UniTask SwitchToMainThread()
        {
            await UniTask.SwitchToMainThread();
        }
    }

    /// <summary>Time-based frame clock adapter (player-loop Update yields).</summary>
    public sealed class UnityRigClock : IRigClock
    {
        public float UnscaledDeltaTime => Time.unscaledDeltaTime;

        public UniTask NextFrame(CancellationToken token)
        {
            return UniTask.Yield(PlayerLoopTiming.Update, cancellationToken: token);
        }
    }

    /// <summary>Live-Camera view adapter.</summary>
    public sealed class UnityCameraView : ICameraRigCameraView
    {
        private readonly Camera _camera;

        public UnityCameraView(Camera camera)
        {
            _camera = camera;
        }

        public bool Orthographic
        {
            get => _camera.orthographic;
            set => _camera.orthographic = value;
        }

        public float Aspect => _camera.aspect;

        public float OrthographicSize
        {
            get => _camera.orthographicSize;
            set => _camera.orthographicSize = value;
        }

        public Vector3 Position
        {
            get => _camera.transform.position;
            set => _camera.transform.position = value;
        }

        public Quaternion Rotation
        {
            get => _camera.transform.rotation;
            set => _camera.transform.rotation = value;
        }

        public string SceneName => _camera != null ? _camera.gameObject.scene.name : null;

        public bool IsValid => _camera != null;
    }

    /// <summary>Transform-backed ghost handle.</summary>
    public sealed class UnityGhostHandle : IRigGhost
    {
        private readonly Transform _transform;

        public UnityGhostHandle(Transform transform)
        {
            _transform = transform;
        }

        public string Name => _transform != null ? _transform.name : null;
        public Vector3 Position => _transform != null ? _transform.position : Vector3.zero;
        public bool IsValid => _transform != null;
    }
}
