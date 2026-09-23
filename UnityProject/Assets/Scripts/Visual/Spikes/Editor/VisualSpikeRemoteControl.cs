using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Xianxia.Sect.Visual.Spikes.EditorTools
{
    /// <summary>
    /// Marker-file remote control for agent-driven Phase-0 runs, because this
    /// session cannot pass parameters through the eval/menu-execute tools.
    /// An EditorUpdate loop watches Library/visual_spike_command.txt for:
    ///   play_s2 | play_s1 | play_s3 | verify | fetch_csv
    /// and acts exactly like the corresponding menu item. After a play command,
    /// it auto-exits play mode once a new CSV/PNG artifact appears in
    /// persistentDataPath/visual_spikes and copies artifacts into Library/ for
    /// the agent to read. Gameplay scenes never contain spike components and
    /// nothing auto-loads — this only reacts to an explicit flag file (C2).
    /// </summary>
    [InitializeOnLoad]
    public static class VisualSpikeRemoteControl
    {
        private const string FlagPath = "Library/visual_spike_command.txt";
        private const string ResultPath = "Library/visual_spike_result.txt";
        private const string MenuRoot = "Xianxia/Visual Spikes/Remote/";

        private static bool _watching;
        private static DateTime? _playStartUtc;

        [MenuItem(MenuRoot + "Start Remote Control Watch")]
        public static void StartWatch()
        {
            _watching = true;
            WriteResult("watching");
        }

        static VisualSpikeRemoteControl()
        {
            _watching = true; // auto-start; the menu item is a manual fallback
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (!_watching) return;

            // Auto-exit: a measurement artifact newer than play start means done.
            if (_playStartUtc.HasValue && EditorApplication.isPlaying)
            {
                if (HasNewArtifact(_playStartUtc.Value))
                {
                    _playStartUtc = null;
                    EditorApplication.isPlaying = false;
                    WriteResult("measured\n" + FetchCsvIndex());
                    return;
                }
            }

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
                case "play_s2":
                    VisualSpikeScenes.OpenS2Scene(); // creates the scene on first run
                    _playStartUtc = DateTime.UtcNow;
                    EditorApplication.isPlaying = true;
                    WriteResult("playing_s2");
                    break;
                case "play_s1":
                    VisualSpikeScenes.OpenS1Scene();
                    _playStartUtc = DateTime.UtcNow;
                    EditorApplication.isPlaying = true;
                    WriteResult("playing_s1");
                    break;
                case "play_s3":
                    VisualSpikeScenes.OpenS3Scene();
                    _playStartUtc = DateTime.UtcNow;
                    EditorApplication.isPlaying = true;
                    WriteResult("playing_s3");
                    break;
                case "verify":
                    VisualSpikesEditor.VerifyConstraints();
                    WriteResult("verified");
                    break;
                case "validate":
                    Xianxia.Sect.EditorTools.VisualCoverageValidator.Validate();
                    WriteResult("validated");
                    break;
                case "bake_chibi":
                    Xianxia.Sect.EditorTools.ChibiSheetBaker.GenerateAll();
                    WriteResult("baked_chibi");
                    break;
                case "phase2_verify":
                    Phase2VerifyRunner.RunVerify(); // async: report lands in Library/phase2_verify_report.txt
                    WriteResult("phase2_verify_started");
                    break;
                case "phase3_verify":
                    Phase3VerifyRunner.Run(); // async: report lands in Library/phase3_verify_report.txt
                    WriteResult("phase3_verify_started");
                    break;
                case "phase4_verify":
                    Phase4VerifyRunner.Run(); // async: report lands in Library/phase4_verify_report.txt
                    WriteResult("phase4_verify_started");
                    break;
                case "phase5_verify":
                    Phase5VerifyRunner.Run(); // async: report lands in Library/phase5_verify_report.txt
                    WriteResult("phase5_verify_started");
                    break;
                case "play_demo":
                    VisualDemoSceneTool.PlayDemo(); // guards + scaffold + boot scene + play; loader drives the additive demo load
                    WriteResult("play_demo_started");
                    break;
                case "demo_verify":
                    VisualDemoSceneTool.DemoVerify(); // guards + scaffold + boot + play + runner — report lands in Library/demo_verify_report.txt
                    WriteResult("demo_verify_started");
                    break;
                case "fetch_csv":
                    WriteResult(FetchCsvIndex());
                    break;
                case "restore":
                    // Reopen the first gameplay scene from Build Settings after spikes
                    // (agent cannot pass parameters through open_scene in this session).
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
                default:
                    WriteResult("unknown_command:" + command);
                    break;
            }
        }

        private static bool HasNewArtifact(DateTime sinceUtc)
        {
            string dir = Path.Combine(Application.persistentDataPath, "visual_spikes");
            if (!Directory.Exists(dir)) return false;
            foreach (var f in Directory.GetFiles(dir))
            {
                if (File.GetLastWriteTimeUtc(f) > sinceUtc) return true;
            }
            return false;
        }

        private static string FetchCsvIndex()
        {
            string dir = Path.Combine(Application.persistentDataPath, "visual_spikes");
            if (!Directory.Exists(dir)) return "no_csv_dir";
            var files = Directory.GetFiles(dir, "*.csv");
            var pngs = Directory.GetFiles(dir, "*.png");
            var lines = new System.Collections.Generic.List<string>
            {
                "csv_count=" + files.Length,
                "png_count=" + pngs.Length
            };
            foreach (var f in files) lines.Add(Path.GetFileName(f) + "|" + new FileInfo(f).Length + "B");
            foreach (var f in pngs) lines.Add(Path.GetFileName(f) + "|" + new FileInfo(f).Length + "B");

            string dest = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "Library", "visual_spike_artifacts");
            Directory.CreateDirectory(dest);
            foreach (var f in files) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
            foreach (var f in pngs) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
            lines.Add("copied_to=" + dest);
            return string.Join("\n", lines.ToArray());
        }

        private static void WriteResult(string text)
        {
            try { File.WriteAllText(ResultPath, text); } catch (IOException) { }
        }
    }
}
