using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Xianxia.Sect.Visual.Spikes.EditorTools
{
    /// <summary>
    /// Phase-0 spike launcher (T1): menu items under "Xianxia/Visual Spikes/...".
    /// Spike scenes are separate, never in Build Settings, never auto-loaded (C2);
    /// entering play mode in a spike scene runs the measurement via its launcher
    /// component — no FindObjectOfType, no reflection (C6).
    /// </summary>
    public static class VisualSpikesEditor
    {
        private const string MenuRoot = "Xianxia/Visual Spikes/";
        private const string SpikeSceneFolder = "Assets/Scenes/Spikes";
        private const string S2ScenePath = SpikeSceneFolder + "/S2_SpriteSpike.unity";
        private const string S1ScenePath = SpikeSceneFolder + "/S1_SpineSpike.unity";
        private const string S3ScenePath = SpikeSceneFolder + "/S3_SizeProbe.unity";

        // ------------------------------------------------------------------
        // S2 — Sprite layered spike (Core asmdef — compiles without Spine)
        // ------------------------------------------------------------------

        [MenuItem(MenuRoot + "S2 Sprite Layered (300) — Open Scene")]
        public static void OpenS2()
        {
            OpenOrCreateSpikeScene(S2ScenePath, "S2_SpriteSpike", false, false);
        }

        // ------------------------------------------------------------------
        // S1 — Spine shared rig spike (Spine asmdef)
        // ------------------------------------------------------------------

        [MenuItem(MenuRoot + "S1 Spine Shared Rig (20) — Open Scene")]
        public static void OpenS1()
        {
            OpenOrCreateSpikeScene(S1ScenePath, "S1_SpineSpike", false, false);
        }

        // ------------------------------------------------------------------
        // S3 — cell size probe
        // ------------------------------------------------------------------

        [MenuItem(MenuRoot + "S3 Size Probe (64/96/128) — Open Scene")]
        public static void OpenS3()
        {
            OpenOrCreateSpikeScene(S3ScenePath, "S3_SizeProbe", false, true);
        }

        // ------------------------------------------------------------------
        // Pipeline verification + CSV folder
        // ------------------------------------------------------------------

        [MenuItem(MenuRoot + "Verify Phase-0 Pipeline Constraints")]
        public static void VerifyConstraints()
        {
            var lines = new List<string>();

            var buildScenes = EditorBuildSettings.scenes;
            bool leaked = buildScenes.Any(s => s.path.Replace('\\', '/').IndexOf("/Spikes/", StringComparison.OrdinalIgnoreCase) >= 0);
            lines.Add((leaked ? "[FAIL]" : "[OK]") + " C2: spike scenes in Build Settings = " + (leaked ? "LEAK" : "none"));

            const string coreAsmdef = "Assets/Scripts/Visual/Spikes/Core/Visual.Core.Spikes.asmdef";
            const string spineAsmdef = "Assets/Scripts/Visual/Spikes/Spine/Visual.Spine.Spikes.asmdef";
            bool coreExists = File.Exists(CoreRelative(coreAsmdef));
            bool spineExists = File.Exists(CoreRelative(spineAsmdef));
            lines.Add((coreExists && spineExists ? "[OK]" : "[FAIL]") + " C3: asmdefs exist (Core=" + coreExists + ", Spine=" + spineExists + ")");
            if (coreExists)
            {
                string json = File.ReadAllText(CoreRelative(coreAsmdef));
                bool refsSpine = json.IndexOf("spine", StringComparison.OrdinalIgnoreCase) >= 0;
                lines.Add((refsSpine ? "[FAIL]" : "[OK]") + " C3: Core asmdef references Spine = " + refsSpine);
            }

            lines.Add("[INFO] C4: confirm 'git diff -- Shared/' is empty (checked in phase0 report)");

            Debug.Log("=== Phase-0 pipeline verification ===\n" + string.Join("\n", lines.ToArray()));
        }

        [MenuItem(MenuRoot + "Open CSV Folder")]
        public static void OpenCsvFolder()
        {
            string dir = Path.Combine(Application.persistentDataPath, "visual_spikes");
            Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir);
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------

        private static string CoreRelative(string assetsRelative)
        {
            string assets = Application.dataPath;
            return Path.GetFullPath(Path.Combine(assets, "..", assetsRelative));
        }

        private static void OpenOrCreateSpikeScene(string scenePath, string sceneName, bool empty, bool sizeProbes)
        {
            Directory.CreateDirectory(SpikeSceneFolder);
            if (File.Exists(CoreRelative(scenePath)))
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                return;
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!empty)
            {
                var host = new GameObject("SpikeHost");
                host.AddComponent<Xianxia.Sect.Visual.Spikes.SpikeSceneBootstrap>();
                if (sizeProbes)
                {
                    host.AddComponent<Xianxia.Sect.Visual.Spikes.SizeProbeSceneLauncher>();
                }
                else
                {
                    host.AddComponent<Xianxia.Sect.Visual.Spikes.SpikeSceneLauncher>();
                }
            }
            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log("[VisualSpikes] created spike scene: " + scenePath + " (not added to Build Settings — C2)");
        }
    }
}
