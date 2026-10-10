using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using MessagePipe;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Xianxia.Sect.Messages;
using VContainer;
using VContainer.Unity;

namespace Xianxia.Sect
{
    /// <summary>
    /// Singleton scene loader for additive scene architecture.
    /// Registered in GameLifetimeScope.Configure() as Singleton entry point.
    /// CoreScene loads single (persistent), GameplayScenes load additive.
    /// </summary>
    public class SceneLoader : IStartable, IGameplaySceneLoader
    {
        /// <summary>ฉากเกมเพลย์ที่โหลดอัตโนมัติตอนเริ่มเกม (additive บน CoreScene) —
        /// เดิมต้องกดปุ่ม "Load A (additive)" ใน AdditiveSceneTest เองจึงจะเห็นฉาก</summary>
        public const string DefaultGameplayScene = SceneNames.Sect;
        private readonly IPublisher<SceneLoadedMessage> _sceneLoadedPublisher;
        private readonly IPublisher<SceneUnloadedMessage> _sceneUnloadedPublisher;
        // root scope = LifetimeScope base type — VContainer.InstallTo ลงทะเบียนให้ตัว
        // scope เองอยู่แล้ว (RegisterInstance<LifetimeScope>(this).AsSelf()) ห้าม
        // RegisterInstance(GameLifetimeScope) ซ้ำใน Configure (VContainerException
        // Conflict implementation type — เคยทำให้ container build fail ทั้งเกม)
        private readonly LifetimeScope _rootScope;
        private string _currentGameplayScene;

        public SceneLoader(IPublisher<SceneLoadedMessage> sceneLoadedPublisher,
                           IPublisher<SceneUnloadedMessage> sceneUnloadedPublisher,
                           LifetimeScope rootScope)
        {
            _sceneLoadedPublisher = sceneLoadedPublisher;
            _sceneUnloadedPublisher = sceneUnloadedPublisher;
            _rootScope = rootScope;
        }

        /// <summary>
        /// P12A — the INITIAL gameplay load is owned by <see cref="GameSessionCoordinator"/>
        /// (the explicit Title → StartingNewGame → Playing lifecycle), so SceneLoader no
        /// longer auto-loads on Start. It stays a pure additive-scene service: it loads and
        /// unloads on request, publishes SceneLoadedMessage/SceneUnloadedMessage exactly as
        /// before, and never decides when a session starts. Kept as an entry point so the
        /// loader is constructed at composition-root build (and implementable as
        /// <see cref="IGameplaySceneLoader"/>); there is still exactly one loader.
        /// </summary>
        public void Start()
        {
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
                //
                // PHASE 2: EnqueueParent(root) ครอบทั้ง load → child LifetimeScope ในฉาก
                // (SectSceneLifetimeScope) เชื้อกับ root ผ่าน GlobalOverrideParents ตอน
                // Awake ที่ fire ระหว่าง async load — unwrap ใน finally เสมอ
                using (EnqueueRootAsParent())
                {
                    var editorOp = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                        sceneAssetPath, new LoadSceneParameters(LoadSceneMode.Additive));
                    await editorOp.ToUniTask();
                }
                _currentGameplayScene = sceneName;
                RemoveDuplicateSingletons(sceneName);
                _sceneLoadedPublisher.Publish(new SceneLoadedMessage { SceneName = sceneName });
                Debug.Log("[SceneLoader] loaded '" + sceneName +
                          "' additively outside the build profile (editor-only dev path)");
                return;
            }
#endif
            // PHASE 2: EnqueueParent(root) ครอบทั้ง load — child LifetimeScope ในฉาก
            // (SectSceneLifetimeScope) จะเชื้อกับ root ทันทีที่ Awake ระหว่าง async load
            // (build + entry points Start หลัง await จบ) — unwrap ใน finally เสมอ
            using (EnqueueRootAsParent())
            {
                var asyncOp = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                await asyncOp.ToUniTask();
            }

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

        /// <summary>
        /// PHASE 2: ครอบช่วงโหลดฉากด้วย LifetimeScope.EnqueueParent(root) — child
        /// LifetimeScope ที่มาพร้อมฉาก (SectSceneLifetimeScope) จะได้ root เป็น parent
        /// ผ่าน GlobalOverrideParents ตอน Awake (ซึ่ง fire ระหว่าง async load ก่อน
        /// await จบ) — unwrap ใน finally เสมอ (using) กัน stack ค้างกระทบโหลดครั้งถัดไป
        /// ถ้า root หาไม่ได้ (เช่น test harness) คืน no-op disposable แทน — ฉากยังโหลดได้
        /// เพียงแต่ child scope จะไม่มี parent (ข้อจำกัดบันทึกไว้ที่หัวคลาส SectSceneLifetimeScope)
        /// </summary>
        private System.IDisposable EnqueueRootAsParent()
        {
            if (_rootScope == null)
            {
                Debug.LogWarning("[SceneLoader] root scope unavailable — scene child scope will have no parent");
                return new NoopDisposable();
            }
            return LifetimeScope.EnqueueParent(_rootScope);
        }

        private sealed class NoopDisposable : System.IDisposable
        {
            public void Dispose() { }
        }

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