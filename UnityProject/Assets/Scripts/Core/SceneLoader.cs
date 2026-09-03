using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using MessagePipe;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    /// <summary>
    /// Singleton scene loader for additive scene architecture.
    /// Registered in GameLifetimeScope.Configure() as Singleton.
    /// CoreScene loads single (persistent), GameplayScenes load additive.
    /// </summary>
    public class SceneLoader
    {
        private readonly IPublisher<SceneLoadedMessage> _sceneLoadedPublisher;
        private string _currentGameplayScene;

        public SceneLoader(IPublisher<SceneLoadedMessage> sceneLoadedPublisher)
        {
            _sceneLoadedPublisher = sceneLoadedPublisher;
        }

        /// <summary>
        /// Load a gameplay scene additively, unloading the previous one.
        /// After loading, checks for and removes duplicate EventSystem/AudioListener
        /// to ensure only the CoreScene singletons survive.
        /// </summary>
        public async UniTask LoadGameplayScene(string sceneName)
        {
            if (!string.IsNullOrEmpty(_currentGameplayScene))
            {
                await UnloadCurrentGameplayScene();
            }

            var asyncOp = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            await asyncOp.ToUniTask();

            _currentGameplayScene = sceneName;

            // Remove duplicate EventSystem/AudioListener from the loaded scene.
            // CoreScene owns the canonical instances; gameplay scenes must not add more.
            RemoveDuplicateSingletons(sceneName);

            _sceneLoadedPublisher.Publish(new SceneLoadedMessage { SceneName = sceneName });
        }

        /// <summary>
        /// Unload the current gameplay scene if loaded.
        /// </summary>
        public async UniTask UnloadCurrentGameplayScene()
        {
            if (string.IsNullOrEmpty(_currentGameplayScene))
            {
                return;
            }

            var scene = SceneManager.GetSceneByName(_currentGameplayScene);
            if (scene.IsValid())
            {
                await SceneManager.UnloadSceneAsync(scene);
            }

            _currentGameplayScene = null;
        }

        /// <summary>
        /// Get the name of the currently loaded gameplay scene.
        /// </summary>
        public string GetCurrentGameplayScene() => _currentGameplayScene ?? "";

        /// <summary>
        /// Scan the newly-loaded scene for EventSystem or AudioListener components.
        /// If any exist in the gameplay scene, destroy them — the canonical
        /// instances live in CoreScene and must remain the only ones active.
        /// </summary>
        private static void RemoveDuplicateSingletons(string sceneName)
        {
            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid()) return;

            var rootObjects = scene.GetRootGameObjects();

            // Collect all EventSystems and AudioListeners in the scene
            var eventSystems = new List<EventSystem>();
            var audioListeners = new List<AudioListener>();

            foreach (var root in rootObjects)
            {
                eventSystems.AddRange(root.GetComponentsInChildren<EventSystem>(true));
                audioListeners.AddRange(root.GetComponentsInChildren<AudioListener>(true));
            }

            // Destroy duplicates (CoreScene already has the canonical instances)
            foreach (var es in eventSystems)
            {
                Debug.LogWarning($"[SceneLoader] Removed duplicate EventSystem from scene '{sceneName}': {es.gameObject.name}");
                Object.Destroy(es.gameObject);
            }

            foreach (var al in audioListeners)
            {
                Debug.LogWarning($"[SceneLoader] Removed duplicate AudioListener from scene '{sceneName}': {al.gameObject.name}");
                Object.Destroy(al.gameObject);
            }
        }
    }
}