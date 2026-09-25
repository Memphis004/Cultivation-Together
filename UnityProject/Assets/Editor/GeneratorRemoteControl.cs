#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Xianxia.EditorTools
{
    /// <summary>
    /// Marker-file remote control for Assembly-CSharp-Editor tools (AvatarPrefabGenerator),
    /// mirroring VisualSpikeRemoteControl: the agent writes "gen_avatars" into
    /// Library/generator_command.txt; this watches and executes, then answers in
    /// Library/generator_result.txt. Needed because menu execution cannot pass
    /// parameters in this session — this is the only non-interactive trigger.
    /// </summary>
    [InitializeOnLoad]
    public static class GeneratorRemoteControl
    {
        private const string FlagPath = "Library/generator_command.txt";
        private const string ResultPath = "Library/generator_result.txt";

        static GeneratorRemoteControl()
        {
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (!File.Exists(FlagPath)) return;

            string command;
            try
            {
                command = File.ReadAllText(FlagPath).Trim();
                File.Delete(FlagPath);
            }
            catch (IOException)
            {
                return; // agent still writing; retry next tick
            }

            switch (command)
            {
                case "gen_avatars":
                    try
                    {
                        AvatarPrefabGenerator.GenerateAll();
                        WriteResult("generated_avatars");
                    }
                    catch (System.Exception ex)
                    {
                        WriteResult("error:" + ex.Message);
                    }
                    break;
                case "bake_icons":
                    try
                    {
                        Xianxia.Sect.EditorTools.AvatarIconBaker.GenerateAll();
                        WriteResult("baked_icons");
                    }
                    catch (System.Exception ex)
                    {
                        WriteResult("error:" + ex.Message);
                    }
                    break;
                case "gen_disciple_list":
                    try
                    {
                        Xianxia.Sect.EditorTools.DiscipleListPanelGenerator.Generate();
                        WriteResult("generated_disciple_list");
                    }
                    catch (System.Exception ex)
                    {
                        WriteResult("error:" + ex.Message);
                    }
                    break;
                case "gen_disciple_detail":
                    try
                    {
                        Xianxia.Sect.EditorTools.DiscipleDetailPanelGenerator.Generate();
                        WriteResult("generated_disciple_detail");
                    }
                    catch (System.Exception ex)
                    {
                        WriteResult("error:" + ex.Message);
                    }
                    break;
                case "restore":
                    // Reopen the first Build Settings scene (agent cannot pass paths through open_scene).
                    if (EditorBuildSettings.scenes.Length > 0)
                    {
                        EditorSceneManager.OpenScene(EditorBuildSettings.scenes[0].path, OpenSceneMode.Single);
                        WriteResult("restored:" + EditorBuildSettings.scenes[0].path);
                    }
                    else
                    {
                        WriteResult("no_build_scenes");
                    }
                    break;
                case "ping":
                    WriteResult("pong");
                    break;
                default:
                    WriteResult("unknown_command:" + command);
                    break;
            }
        }

        private static void WriteResult(string text)
        {
            try
            {
                File.WriteAllText(ResultPath, text);
            }
            catch (System.Exception)
            {
                // best-effort; result file is advisory only
            }
        }
    }
}
#endif
