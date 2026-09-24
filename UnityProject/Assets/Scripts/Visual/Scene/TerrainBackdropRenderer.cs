using System;
using Cysharp.Threading.Tasks;
using MessagePipe;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// Painted terrain backdrop (mountain1.png, 4096x3328 @ PPU100, single
    /// slice with pivot 0,0) rendered as a world-space SpriteRenderer under
    /// the grid overlay and every gameplay sprite.
    /// Created at runtime from Resources so the additive gameplay scene stays
    /// free of hand-written YAML (the scene's Grid block was dropped by Unity
    /// because of a wrong class ID - this class deliberately avoids scene
    /// serialization entirely).
    /// </summary>
    public sealed class TerrainBackdropRenderer : IStartable, IDisposable
    {
        private const string GameplaySceneName = "TestGameplayScene";

        private readonly ISubscriber<SceneLoadedMessage> _sceneLoadedSubscriber;
        private System.IDisposable _sceneLoadedSubscription;

        private Transform _backdrop;
        private bool _disposed;

        public TerrainBackdropRenderer(ISubscriber<SceneLoadedMessage> sceneLoadedSubscriber)
        {
            _sceneLoadedSubscriber = sceneLoadedSubscriber;
        }

        public void Start()
        {
            _sceneLoadedSubscription = _sceneLoadedSubscriber.Subscribe(OnSceneLoaded);

            // Gameplay scene may already be loaded when the persistent scope starts.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene.name == GameplaySceneName)
                {
                    EnsureBackdrop();
                    break;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sceneLoadedSubscription?.Dispose();
            if (_backdrop != null)
                UnityEngine.Object.Destroy(_backdrop.gameObject);
            _backdrop = null;
        }

        private void OnSceneLoaded(SceneLoadedMessage message)
        {
            HandleSceneLoadedAsync(message).Forget();
        }

        private async UniTask HandleSceneLoadedAsync(SceneLoadedMessage message)
        {
            // SceneLoaded can be forwarded from an interprocess-driven flow.
            await UniTask.SwitchToMainThread();
            if (_disposed || _backdrop != null) return;
            if (message.SceneName != GameplaySceneName) return;

            EnsureBackdrop();
        }

        private void EnsureBackdrop()
        {
            if (_backdrop != null) return;

            Sprite map = Resources.Load<Sprite>(CameraFramingConfig.MapBackdropPath);
            if (map == null)
            {
                Debug.LogWarning("[TerrainBackdropRenderer] Backdrop sprite missing at Resources/" +
                                 CameraFramingConfig.MapBackdropPath);
                return;
            }

            var go = new GameObject("TerrainBackdrop");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = map;
            sr.sortingLayerName = "Default";
            sr.sortingOrder = -100; // behind grid overlay (GridOverlay layer) and gameplay sprites

            // Slice pivot is (0,0): offset by half size so the image centers
            // on the world origin, matching the camera's overview position.
            Bounds bounds = map.bounds;
            go.transform.position = new Vector3(-bounds.center.x, -bounds.center.y, 0f);

            _backdrop = go.transform;
            Debug.Log("[TerrainBackdropRenderer] Backdrop placed: " + map.name +
                      " world=" + bounds.size.x.ToString("0.00") + "x" + bounds.size.y.ToString("0.00") +
                      " center=(0,0), sortingOrder=-100");
        }
    }
}
