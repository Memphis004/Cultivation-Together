using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// Drop this on any GameObject in CoreScene (SampleScene) to test additive
    /// scene loading + scene swapping in Play mode. Provides Inspector buttons
    /// for all SceneLoader operations and logs results to the Console.
    ///
    /// Usage:
    ///   1. Enter Play mode with SampleScene (CoreScene) loaded.
    ///   2. Select this GameObject — you'll see Test buttons in the Inspector.
    ///   3. Click Load A (TestGameplayScene), then Load B (TestGameplayScene2)
    ///      to verify the A→B swap (B unloads A, CoreScene UI persists).
    ///   4. Watch the Console for SceneLoader output + duplicate detection.
    ///   5. Click Unload to remove the gameplay scene.
    ///
    /// Both gameplay scenes must exist in Build Settings (File → Build Settings
    /// → Add Open Scenes) for async loading to succeed.
    /// </summary>
    public class AdditiveSceneTest : MonoBehaviour
    {
        [Header("Gameplay scenes (must be in Build Settings)")]
        [Tooltip("Scene A — the first gameplay scene to load additively.")]
        [SerializeField] private string testSceneName = "TestGameplayScene";
        [Tooltip("Scene B — loading this while A is active swaps A→B.")]
        [SerializeField] private string testSceneBName = "TestGameplayScene2";

        [Header("Status (read-only)")]
        [SerializeField] private string currentScene = "(none)";
        [SerializeField] private bool isLoading;

        private SceneLoader _sceneLoader;

        [Inject]
        private void Inject(SceneLoader sceneLoader)
        {
            _sceneLoader = sceneLoader;
        }

        private void Start()
        {
            // If VContainer is set up on the parent scope, this will be injected.
            // Otherwise fall back to manual resolve for testing.
            if (_sceneLoader == null)
            {
                Debug.LogWarning("[AdditiveSceneTest] SceneLoader not injected. " +
                    "Make sure this GameObject is inside the GameLifetimeScope's scene " +
                    "or add a LocalMonoInstaller to inject it.");
            }
        }

        // --- Inspector callbacks (called by Editor buttons) ---

        /// <summary>
        /// Load scene A additively.
        /// </summary>
        public void LoadSceneA()
        {
            LoadScene(testSceneName);
        }

        /// <summary>
        /// Load scene B additively. If A is currently loaded, SceneLoader
        /// unloads it first — this is the A→B swap test.
        /// </summary>
        public void LoadSceneB()
        {
            LoadScene(testSceneBName);
        }

        /// <summary>
        /// Unload the current gameplay scene.
        /// </summary>
        public void UnloadTestScene()
        {
            if (_sceneLoader == null)
            {
                Debug.LogError("[AdditiveSceneTest] SceneLoader is null. Cannot unload.");
                return;
            }

            var current = _sceneLoader.GetCurrentGameplayScene();
            if (string.IsNullOrEmpty(current))
            {
                Debug.LogWarning("[AdditiveSceneTest] No gameplay scene loaded.");
                return;
            }

            UnloadTestSceneAsync(current).Forget();
        }

        /// <summary>
        /// Swap: unload current and load a different scene.
        /// </summary>
        public void SwapScene(string newSceneName)
        {
            LoadScene(newSceneName);
        }

        private void LoadScene(string sceneName)
        {
            if (_sceneLoader == null)
            {
                Debug.LogError("[AdditiveSceneTest] SceneLoader is null. Cannot load.");
                return;
            }

            if (isLoading)
            {
                Debug.LogWarning("[AdditiveSceneTest] Already loading. Please wait.");
                return;
            }

            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("[AdditiveSceneTest] Scene name is empty.");
                return;
            }

            LoadSceneAsync(sceneName).Forget();
        }

        // UniTask (non-generic) has no .ContinueWith(...) - that's a Task-style
        // fluent API, not how UniTask does continuations. The idiomatic UniTask
        // pattern is a plain async local method + .Forget() at the call site,
        // same as everywhere else in this codebase (DecisionExecutor, DecisionLogger).
        private async UniTaskVoid LoadSceneAsync(string sceneName)
        {
            Debug.Log($"[AdditiveSceneTest] Loading '{sceneName}' additively...");
            isLoading = true;

            await _sceneLoader.LoadGameplayScene(sceneName);

            isLoading = false;
            currentScene = sceneName;
            Debug.Log($"[AdditiveSceneTest] ✓ Loaded '{sceneName}' successfully.");
            Debug.Log($"[AdditiveSceneTest] Current gameplay scene: {_sceneLoader.GetCurrentGameplayScene()}");
        }

        private async UniTaskVoid UnloadTestSceneAsync(string current)
        {
            Debug.Log($"[AdditiveSceneTest] Unloading '{current}'...");

            await _sceneLoader.UnloadCurrentGameplayScene();

            currentScene = "(none)";
            Debug.Log("[AdditiveSceneTest] ✓ Unloaded successfully.");
            Debug.Log($"[AdditiveSceneTest] Current gameplay scene: {_sceneLoader.GetCurrentGameplayScene()}");
        }

        // --- For Editor button (simple) ---

#if UNITY_EDITOR
        private void OnGUI()
        {
            // Simple runtime GUI for quick testing without Inspector
            if (!Application.isPlaying) return;

            GUILayout.BeginArea(new Rect(10, 10, 320, 260));
            GUILayout.Label("<b>Additive Scene Test</b>", new GUIStyle(GUI.skin.label) { fontSize = 16 });
            GUILayout.Space(10);

            GUILayout.Label($"Current: {currentScene}");
            GUILayout.Label($"Loading: {isLoading}");
            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            GUILayout.Label("A:", GUILayout.Width(20));
            testSceneName = GUILayout.TextField(testSceneName);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("B:", GUILayout.Width(20));
            testSceneBName = GUILayout.TextField(testSceneBName);
            GUILayout.EndHorizontal();

            GUILayout.Space(5);

            if (GUILayout.Button("Load A (additive)", GUILayout.Height(28)))
            {
                LoadSceneA();
            }

            if (GUILayout.Button("Load B (swap A\u2192B)", GUILayout.Height(28)))
            {
                LoadSceneB();
            }

            if (GUILayout.Button("Unload Scene", GUILayout.Height(28)))
            {
                UnloadTestScene();
            }

            GUILayout.Space(10);
            GUILayout.Label("Scene Loader Status:", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            GUILayout.Label(_sceneLoader != null ? "✓ Connected" : "✗ Not injected");

            GUILayout.EndArea();
        }
#endif
    }
}
