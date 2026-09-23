using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Xianxia.Sect.Visual.Spikes.EditorTools
{
    /// <summary>
    /// Scene paths + create/open helpers shared by the menu and the marker-file
    /// remote control. Spike scenes live under Assets/Scenes/Spikes and are
    /// never added to Build Settings (C2).
    /// </summary>
    public static class VisualSpikeScenes
    {
        public const string SpikeSceneFolder = "Assets/Scenes/Spikes";
        public const string S2ScenePath = SpikeSceneFolder + "/S2_SpriteSpike.unity";
        public const string S1ScenePath = SpikeSceneFolder + "/S1_SpineSpike.unity";
        public const string S3ScenePath = SpikeSceneFolder + "/S3_SizeProbe.unity";

        public static void OpenS2Scene() { OpenOrCreateSpikeScene(S2ScenePath, "S2_SpriteSpike", SpikeLauncherKind.Sprite); }
        public static void OpenS1Scene() { OpenOrCreateSpikeScene(S1ScenePath, "S1_SpineSpike", SpikeLauncherKind.Spine); }
        public static void OpenS3Scene() { OpenOrCreateSpikeScene(S3ScenePath, "S3_SizeProbe", SpikeLauncherKind.SizeProbe); }

        public enum SpikeLauncherKind { Sprite, Spine, SizeProbe }

        public static void OpenOrCreateSpikeScene(string scenePath, string sceneName, SpikeLauncherKind kind)
        {
            Directory.CreateDirectory(SpikeSceneFolder);
            if (File.Exists(WorkspaceRelative(scenePath)))
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                return;
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var host = new GameObject("SpikeHost");
            host.AddComponent<Xianxia.Sect.Visual.Spikes.SpikeSceneBootstrap>();
            switch (kind)
            {
                case SpikeLauncherKind.Sprite:
                    host.AddComponent<Xianxia.Sect.Visual.Spikes.SpikeSceneLauncher>();
                    break;
                case SpikeLauncherKind.Spine:
                    // S1 uses the free mix-and-match example rig mapped onto the
                    // 6 production slots approximately (no real chibi rig yet — T2).
                    host.AddComponent<Xianxia.Sect.Visual.Spines.SpineSpikeSceneLauncher>();
                    Debug.Log("[VisualSpikeScenes] S1 launcher attached (Spine example asset 'mix-and-match-pro', slots mapped approximately)");
                    break;
                case SpikeLauncherKind.SizeProbe:
                    host.AddComponent<Xianxia.Sect.Visual.Spikes.SizeProbeSceneLauncher>();
                    break;
            }
            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log("[VisualSpikeScenes] created spike scene: " + scenePath + " (not added to Build Settings — C2)");
        }

        public static string WorkspaceRelative(string assetsRelative)
        {
            string assets = Application.dataPath;
            return Path.GetFullPath(Path.Combine(assets, "..", assetsRelative));
        }
    }
}
