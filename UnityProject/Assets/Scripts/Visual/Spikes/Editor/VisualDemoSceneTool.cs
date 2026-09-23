using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;             // IObjectResolverExtensions.Resolve<T>
using Xianxia.Sect;           // SceneLoader, GameLifetimeScope
using Xianxia.Sect.Visual; // ChibiSceneRoot

namespace Xianxia.Sect.Visual.Spikes.EditorTools
{
    /// <summary>
    /// ██████ DEV-ONLY — VISUAL DEMO SCENE TOOLING — DO NOT SHIP ██████
    ///
    /// Menu "Xianxia/Visual Demo/Play Demo" (T4):
    ///   1. Guard checks (T8): demo scene NOT in Build Settings + DevSpineOverride
    ///      touched by no code outside VisualDemoSpineEnabler (grep-level).
    ///   2. Builds/updates the demo scene scaffold (T3): ortho camera, ChibiSceneRoot
    ///      (grid sized for the 4 mock disciples), VisualDemoSpineEnabler (C3 seam),
    ///      VisualDemoHud (T5 buttons).
    ///   3. Opens the REAL boot scene (SampleScene — GameLifetimeScope + SceneLoader)
    ///      and enters play mode; a watcher then drives SceneLoader.LoadGameplayScene
    ///      additively so the demo walks the full production path (C2): boot →
    ///      SceneLoadedMessage → DiscipleVisualSystem scans → ChibiSceneRoot → spawn.
    ///      NO parallel spawner exists anywhere in the demo.
    /// </summary>
    public static class VisualDemoSceneTool
    {
        private const string DemoScenePath = "Assets/Scenes/VisualDemo/VisualDemoScene.unity";
        private const string DemoSceneName = "VisualDemoScene";
        private const string BootScenePath = "Assets/Scenes/SampleScene.unity";
        private const string EnablerSourceFile =
            "Assets/Scripts/Visual/Spikes/Spine/VisualDemoSpineEnabler.cs";
        private const string LoaderMarker = "Library/visual_demo_load_requested.txt";

        [MenuItem("Xianxia/Visual Demo/Play Demo")]
        public static void PlayDemo()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[VisualDemo] still playing — stop first, then re-run");
                return;
            }

            // ---- T8 guards (fail fast, never enter play) ----
            var problems = RunGuards();
            if (problems.Count > 0)
            {
                var sb = new StringBuilder("[VisualDemo] GUARD FAILED:\n");
                for (int i = 0; i < problems.Count; i++) sb.Append("  ✗ ").AppendLine(problems[i]);
                Debug.LogError(sb.ToString());
                EditorUtility.DisplayDialog("Visual Demo — guard failed",
                    "Fix the guard violations first (see Console).\n" + string.Join("\n", problems),
                    "OK");
                return;
            }
            Debug.Log("[VisualDemo] guards OK (scene not in Build Settings; DevSpineOverride writers = enabler only)");

            // ---- T3 scaffold (create or refresh) ----
            BuildOrUpdateScene();
            Debug.Log("[VisualDemo] scaffold ready: " + DemoScenePath);

            // ---- T4 boot path: real CoreScene, then additive demo via SceneLoader ----
            EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Single);
            File.WriteAllText(LoaderMarker, DemoSceneName);
            EditorApplication.isPlaying = true;
            Debug.Log("[VisualDemo] boot scene loaded, play mode on — loader will additively load '" +
                      DemoSceneName + "' through SceneLoader (production path)");
        }

        [MenuItem("Xianxia/Visual Demo/Run Guard Checks Only")]
        public static void GuardChecksMenu()
        {
            var problems = RunGuards();
            if (problems.Count == 0) Debug.Log("[VisualDemo] all guards OK");
            else
            {
                var sb = new StringBuilder("[VisualDemo] GUARD FAILED:\n");
                for (int i = 0; i < problems.Count; i++) sb.Append("  ✗ ").AppendLine(problems[i]);
                Debug.LogError(sb.ToString());
            }
        }

        // ---- T8 guards ----

        /// <summary>Returns a list of violations (empty = all guards pass).</summary>
        public static List<string> RunGuards()
        {
            var problems = new List<string>();

            // (a) demo scene must NEVER be in Build Settings (C1)
            var scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i] != null && scenes[i].path.Replace('\\', '/').EndsWith(DemoSceneName + ".unity",
                        StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add("VisualDemoScene is IN Build Settings (index " + i + ") — remove it (C1)");
                }
            }

            // (b) DevSpineOverride must be written ONLY by VisualDemoSpineEnabler (C3)
            // Paths are relative to the Unity project (Editor cwd = UnityProject/).
            var dirs = new[] { "Assets/Scripts" };
            for (int d = 0; d < dirs.Length; d++)
            {
                foreach (var file in Directory.GetFiles(dirs[d], "*.cs", SearchOption.AllDirectories))
                {
                    var norm = file.Replace('\\', '/');
                    if (norm.EndsWith(EnablerSourceFile, StringComparison.OrdinalIgnoreCase)) continue;
                    if (norm.EndsWith("VisualRuntimeConfig.cs", StringComparison.OrdinalIgnoreCase)) continue;
                    if (norm.Contains("/Spikes/Editor/Phase") && norm.EndsWith("VerifyRunner.cs")) continue;
                    if (norm.EndsWith("VisualDemoSceneTool.cs", StringComparison.OrdinalIgnoreCase)) continue;

                    string text;
                    try { text = File.ReadAllText(file); }
                    catch (IOException) { continue; }
                    if (text.IndexOf("DevSpineOverride", StringComparison.Ordinal) < 0) continue;

                    bool writes = text.Contains(".DevSpineOverride =") || text.Contains(".DevSpineOverride=");
                    if (writes)
                    {
                        problems.Add("DevSpineOverride WRITTEN outside the enabler: " + norm +
                                     " (C3 — only VisualDemoSpineEnabler may set it)");
                    }
                }
            }
            return problems;
        }

        /// <summary>
        /// Marker-command "demo_verify": full automated acceptance pass in ONE command —
        /// guards → scaffold → boot scene → play → runner steps. Never touches the S4 gate.
        /// </summary>
        public static void DemoVerify()
        {
            var problems = RunGuards();
            if (problems.Count > 0)
            {
                var sb = new StringBuilder("[VisualDemo] GUARD FAILED:\n");
                for (int entry = 0; entry < problems.Count; entry++) sb.Append("  ✗ ").AppendLine(problems[entry]);
                Debug.LogError(sb.ToString());
                return;
            }

            BuildOrUpdateScene();
            EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Single);
            File.WriteAllText(LoaderMarker, DemoSceneName);
            DemoVerifyRunner.Run(); // sets its own session marker and enters play mode
            Debug.Log("[VisualDemo] demo_verify: scaffold + boot + runner armed");
        }

        // ---- T3 scene scaffold ----

        private static void BuildOrUpdateScene()
        {
            if (File.Exists(DemoScenePath))
            {
                var existing = EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Single);
                Refresh(existing);
                EditorSceneManager.SaveScene(existing, DemoScenePath);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(DemoScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Orthographic 2D camera (same setup philosophy as SpikeSceneBootstrap.EnsureSceneCamera).
            // ⚠ NO AudioListener here: SceneLoader.RemoveDuplicateSingletons DESTROYS any
            // GameObject carrying one on additive load (CoreScene owns the canonical one) —
            // with a listener the demo camera itself was destroyed and the boot camera
            // (ortho size 0.0866) rendered instead → empty frame.
            var camGo = new GameObject("Main Camera", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 4.5f; // 4 chibis in a 2×2 grid, clearly visible
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.14f, 0.18f, 1f);
            cam.depth = 10f; // render ABOVE the boot-scene camera (depth 0) — demo owns the frame
            camGo.transform.position = new Vector3(0f, 1.2f, -10f);

            // demo objects — scripts from the Spike assemblies (C1: demo lives in spikes)
            var rootGo = new GameObject("ChibiSceneRoot", typeof(ChibiSceneRoot));
            var root = rootGo.GetComponent<ChibiSceneRoot>();
            SetPrivate(root, "spawnOrigin", new Vector3(-1.2f, 0f, 0f));
            SetPrivate(root, "spacing", new Vector2(1.6f, 1.2f));
            SetPrivate(root, "perRow", 2); // 2×2 grid → 4 founders clearly visible

            new GameObject("VisualDemoSpineEnabler", typeof(Spines.VisualDemoSpineEnabler));
            new GameObject("VisualDemoHud", typeof(Spines.VisualDemoHud));
            EnsureGlobalLight2D();
            AssignRigToEnabler();

            EditorSceneManager.SaveScene(scene, DemoScenePath);
        }

        /// <summary>
        /// Global Light2D for the demo scene — the project uses URP with the 2D Renderer
        /// and Sprite-Lit materials, so a scene without any Light2D renders every sprite
        /// black. Dev-only scaffold piece (C1); production scenes own their lighting.
        /// </summary>
        private static void EnsureGlobalLight2D()
        {
            if (GameObject.Find("Global Light2D") != null) return;
            var lightGo = new GameObject("Global Light2D", typeof(UnityEngine.Rendering.Universal.Light2D));
            var light = lightGo.GetComponent<UnityEngine.Rendering.Universal.Light2D>();
            light.lightType = UnityEngine.Rendering.Universal.Light2D.LightType.Global;
            light.color = Color.white;
            light.intensity = 1f;
        }

        private static void Refresh(Scene scene)
        {
            // keep the scene authoritative but self-heal if someone deleted a demo object
            if (GameObject.Find("Main Camera") == null)
            {
                // Same rules as BuildOrUpdateScene: no AudioListener (dedupe would destroy it), depth 10 (over boot camera).
                var camGo = new GameObject("Main Camera", typeof(Camera));
                var cam = camGo.GetComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 4.5f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.12f, 0.14f, 0.18f, 1f);
                cam.depth = 10f;
                camGo.transform.position = new Vector3(0f, 1.2f, -10f);
            }
            if (GameObject.Find("VisualDemoSpineEnabler") == null)
                new GameObject("VisualDemoSpineEnabler", typeof(Spines.VisualDemoSpineEnabler));
            if (GameObject.Find("VisualDemoHud") == null)
                new GameObject("VisualDemoHud", typeof(Spines.VisualDemoHud));
            EnsureGlobalLight2D();
            AssignRigToEnabler();
        }

        /// <summary>
        /// Serialized rig reference on the enabler — runtime has no AssetDatabase, so
        /// the scene must carry the reference. Path comes from demo_spine_rig_map.json
        /// (T2 single source of the example-rig location).
        /// </summary>
        private static void AssignRigToEnabler()
        {
            var enabler = GameObject.Find("VisualDemoSpineEnabler");
            if (enabler == null) return;

            var rigPath = new Xianxia.Sect.Visual.DemoSpineRigMap().SkeletonDataAssetPath;
            var rig = AssetDatabase.LoadAssetAtPath<Spine.Unity.SkeletonDataAsset>(rigPath);
            if (rig == null)
            {
                Debug.LogError("[VisualDemo] example rig not found at '" + rigPath + "' — assign it manually or fix demo_spine_rig_map.json");
                return;
            }

            var so = new SerializedObject(enabler.GetComponent<Spines.VisualDemoSpineEnabler>());
            var prop = so.FindProperty("skeletonDataAsset");
            if (prop == null)
            {
                Debug.LogError("[VisualDemo] enabler has no 'skeletonDataAsset' field — script out of sync?");
                return;
            }
            prop.objectReferenceValue = rig;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Serialized-field setter without reflection (C5) — SerializedObject is the editor-sanctioned path.</summary>
        private static void SetPrivate(UnityEngine.Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogWarning("[VisualDemo] missing serialized field: " + field); return; }

            if (value is Vector3 v3) prop.vector3Value = v3;
            else if (value is Vector2 v2) prop.vector2Value = v2;
            else if (value is int i) prop.intValue = i;
            else if (value is float f) prop.floatValue = f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>
    /// Runtime-side helper (editor player loop): when VisualDemoSceneTool asked for the
    /// demo, watch for the container to be built in the boot scene, then drive
    /// SceneLoader.LoadGameplayScene(VisualDemoScene) — the REAL production load path
    /// (additive + SceneLoadedMessage → DiscipleVisualSystem bind/spawn, C2). Never
    /// instantiates or spawns anything itself. Survives the play-mode domain reload
    /// via [InitializeOnLoad] (same pattern as the Phase 3/4/5 verify runners).
    /// </summary>
    [InitializeOnLoad]
    public static class VisualDemoLoadWatcher
    {
        private const string LoaderMarker = "Library/visual_demo_load_requested.txt";

        static VisualDemoLoadWatcher()
        {
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (!File.Exists(LoaderMarker)) return;
            if (!EditorApplication.isPlaying) return; // wait for play mode
            if (!EditorApplication.isPlayingOrWillChangePlaymode) return;

            var injector = GameLifetimeScope.Injector;
            if (injector == null) return; // container not built yet — retry next tick

            string sceneName;
            try { sceneName = File.ReadAllText(LoaderMarker).Trim(); }
            catch (IOException) { return; }
            try { File.Delete(LoaderMarker); } catch (IOException) { }

            if (sceneName != "VisualDemoScene") return;
            if (SceneManager.GetSceneByName("VisualDemoScene").isLoaded)
            {
                Debug.Log("[VisualDemo] scene already loaded — skipping loader");
                return;
            }

            var loader = injector.Resolve<SceneLoader>();
            Debug.Log("[VisualDemo] driving SceneLoader.LoadGameplayScene('VisualDemoScene') — production additive path");
            _ = loader.LoadGameplayScene("VisualDemoScene");
        }
    }
}
