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
        private readonly IPublisher<SceneUnloadedMessage> _sceneUnloadedPublisher;
        private string _currentGameplayScene;

        public SceneLoader(IPublisher<SceneLoadedMessage> sceneLoadedPublisher,
                           IPublisher<SceneUnloadedMessage> sceneUnloadedPublisher)
        {
            _sceneLoadedPublisher = sceneLoadedPublisher;
            _sceneUnloadedPublisher = sceneUnloadedPublisher;
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

#if UNITY_EDITOR
            // DEV-ONLY demo path (VisualDemoScene, see LLMWiki/wiki/sources/visual-demo-scene.md):
            // Unity 6 refuses LoadSceneAsync for scenes outside the build-profile scene list
            // ("not been added to the active build profile"), but editor play mode can still
            // open such scenes additively via EditorSceneManager. Downstream flow is
            // IDENTICAL (duplicate-singleton cleanup + SceneLoadedMessage), and this whole
            // branch is compiled out of player builds — the demo scene never ships.
            var sceneAssetPath = FindSceneAssetPathInEditor(sceneName);
            if (!string.IsNullOrEmpty(sceneAssetPath) &&
                SceneUtility.GetBuildIndexByScenePath(sceneAssetPath) < 0)
            {
                // LoadSceneAsyncInPlayMode is the ONLY play-mode-safe way to open a scene
                // that is outside the build-profile list (OpenScene throws during play;
                // plain LoadSceneAsync is refused by the Unity 6 build profile). Same
                // downstream flow as the normal path below. Editor-only — compiled out
                // of player builds, and the demo scene never ships (see guards).
                var editorOp = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                    sceneAssetPath, new LoadSceneParameters(LoadSceneMode.Additive));
                await editorOp.ToUniTask();
                _currentGameplayScene = sceneName;
                RemoveDuplicateSingletons(sceneName);
                _sceneLoadedPublisher.Publish(new SceneLoadedMessage { SceneName = sceneName });
                Debug.Log("[SceneLoader] loaded '" + sceneName +
                          "' additively outside the build profile (editor-only dev path)");
                return;
            }
#endif
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

            var sceneName = _currentGameplayScene;
            var scene = SceneManager.GetSceneByName(sceneName);
            if (scene.IsValid())
            {
                await SceneManager.UnloadSceneAsync(scene);
            }

            _currentGameplayScene = null;

            // Phase 2 (C4): publish after the unload actually completed so
            // subscribers (DiscipleVisualSystem) never see half-destroyed scenes.
            // SceneUnloadedMessage lives in SceneMessages.cs (Unity-only) - NOT in
            // Shared/GameMessages.cs, which sync-shared.sh would copy to the bridge
            // and dirty all three Shared locations (C2). In-memory only (C11).
            _sceneUnloadedPublisher.Publish(new SceneUnloadedMessage { SceneName = sceneName });
        }

        /// <summary>
        /// Get the name of the currently loaded gameplay scene.
        /// </summary>
        public string GetCurrentGameplayScene() => _currentGameplayScene ?? "";

#if UNITY_EDITOR
        /// <summary>Editor-only: locate a scene asset path by its file name (dev/demo loading).</summary>
        private static string FindSceneAssetPathInEditor(string sceneName)
        {
            var guids = UnityEditor.AssetDatabase.FindAssets("t:SceneAsset", new[] { "Assets/Scenes" });
            for (int i = 0; i < guids.Length; i++)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(path), sceneName,
                                  System.StringComparison.Ordinal))
                {
                    return path;
                }
            }
            return null;
        }
#endif

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