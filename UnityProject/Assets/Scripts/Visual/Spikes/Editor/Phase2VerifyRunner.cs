#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using VContainer;

namespace Xianxia.Sect.Visual.Spikes.EditorTools
{
    /// <summary>
    /// Phase 2 acceptance verification (spec REPORT BACK #3) — driven by the marker-file
    /// remote control: "phase2_verify" in Library/visual_spike_command.txt.
    ///
    /// Survives the play-mode domain reload the same way VisualSpikeRemoteControl does:
    /// an [InitializeOnLoad] update poll re-registers after every reload and drives the
    /// step machine while EditorApplication.isPlaying is true and the session marker
    /// file exists. All cross-reload state lives on disk, not in statics.
    ///
    /// Static access only (C11/C12 — no FindObjectOfType anywhere):
    ///   - VisualRuntimeConfig.Instance          → backend flags
    ///   - GameLifetimeScope.Injector            → typed resolves (ISectStateProvider, SceneLoader)
    ///   - Object.FindObjectsByType&lt;SpriteChibiVisual&gt; → READ-ONLY inspection for
    ///     acceptance verification (Editor tool; production systems never look things up this way)
    ///
    /// Output: Library/phase2_verify_report.txt — quoted verbatim in the Phase 2 report.
    /// </summary>
    [InitializeOnLoad]
    public static class Phase2VerifyRunner
    {
        private const string ReportPath = "Library/phase2_verify_report.txt";
        private const string SessionMarker = "Library/phase2_verify_live.txt";
        private const string ArmFlag = "Library/phase2_verify_armed.txt";

        static Phase2VerifyRunner()
        {
            EditorApplication.update += SessionUpdate;
        }

        // ---- entry (editor context, before play) ----

        public static void RunVerify()
        {
            if (EditorApplication.isPlaying)
            {
                // EditorSceneManager.OpenScene throws during play — stop first;
                // the [InitializeOnLoad] poll will not start a session without the
                // marker, so re-issue "phase2_verify" after play mode has exited.
                Log("[Play] still playing — stopping, re-issue the command");
                EditorApplication.isPlaying = false;
                return;
            }

            Log("=== Phase 2 verify start ===");
            File.Delete(ReportPath);

            var cfg = VisualRuntimeConfig.Instance;
            Log("[Gate] SpriteSheetEnabled=" + cfg.SpriteSheetEnabled +
                " SpineEnabled=" + cfg.SpineEnabled + " SpineBudget=" + cfg.SpineBudget);
            if (!cfg.SpriteSheetEnabled) { Fail("SpriteSheetEnabled is false"); return; }
            if (cfg.SpineEnabled) { Fail("SpineEnabled must stay false in Phase 2 (C1)"); return; }

            string dir = Path.Combine("Assets", "Resources", "Data", "Arts", "Avatar", "Chibi");
            int sheets = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.png").Length : 0;
            Log("[Art] chibi sheets on disk: " + sheets);
            if (sheets == 0) { Fail("no chibi sheets — run bake_chibi first"); return; }

            EnsureChibiSceneRoot("Assets/Scenes/TestGameplayScene.unity");
            EnsureChibiSceneRoot("Assets/Scenes/TestGameplayScene2.unity");
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);

            try
            {
                File.WriteAllText(SessionMarker, "requested");
                File.WriteAllText(ArmFlag, "1"); // consumed by Phase2VerifyDriver.Awake
            }
            catch (IOException) { }

            EditorApplication.isPlaying = true;
            Log("[Play] entering play mode (SampleScene)…");
        }

        // ---- session poll (survives domain reload) ----

        private static bool _session;
        private static int _step;
        private static double _stepUntil;

        private static ISectStateProvider _provider;
        private static SceneLoader _sceneLoader;
        private static int _rosterCount0;
        private static int _spawnBeforeRecruit, _countBeforeSwap;
        private static SpriteChibiVisual _targetVisual;
        private static readonly List<SpriteChibiVisual> _probeVisuals = new List<SpriteChibiVisual>(128);
        private static int _instanceId0;
        private static string _discipleId;
        private static bool _okSceneSpawn, _okSpawn, _okNoRespawn, _okSwap;

        private static bool _inMeasure;
        private static int _frameCount;
        private static readonly List<float> CpuMs = new List<float>(2400);
        private static double _gcSum;
        private static long _gcSamples;

        private static readonly List<string> SessionLog = new List<string>(512);
        private static bool _logAttached;

        private static void SessionUpdate()
        {
            bool marker = File.Exists(SessionMarker);

            if (!_session && marker && EditorApplication.isPlaying)
            {
                StartSession();
                return;
            }

            if (_session)
            {
                if (!EditorApplication.isPlaying)
                {
                    // play mode ended unexpectedly (or externally) — finalize with what we have
                    Finish(partial: true);
                    return;
                }
                StepMachine();
            }
        }

        private static void StartSession()
        {
            _session = true;
            _step = 0;
            _stepUntil = 0;
            _inMeasure = false;
            _frameCount = 0;
            CpuMs.Clear();
            _gcSum = 0;
            _gcSamples = 0;
            _okSceneSpawn = _okSpawn = _okNoRespawn = _okSwap = false;
            _targetVisual = null;
            _probeVisuals.Clear();

            SessionLog.Clear();
            if (!_logAttached)
            {
                Application.logMessageReceived += OnLogMessage;
                _logAttached = true;
            }

            // measurement probe (self-destroys if the arm flag was not found — race-safe:
            // the flag was written before play, deleted on first Awake only)
            var probe = new GameObject("Phase2VerifyDriver");
            probe.AddComponent<Spikes.Phase2VerifyDriver>();

            Log("[Session] started (play mode)");
        }

        private static void StepMachine()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < _stepUntil) return;

            switch (_step)
            {
                case 0:
                    _step = 1; _stepUntil = now + 2.0; // let DI + Start() settle
                    break;

                case 1:
                    {
                        var inj = GameLifetimeScope.Injector;
                        if (inj == null) { Fail("GameLifetimeScope.Injector is null"); return; }
                        _provider = inj.Resolve<ISectStateProvider>();
                        _sceneLoader = inj.Resolve<SceneLoader>();
                        if (_provider == null || _sceneLoader == null) { Fail("resolve failed"); return; }
                        _rosterCount0 = _provider.BuildSectEconomyState().Disciples.Count;
                        Log("[Step1] provider=" + (_provider != null) + " loader=" + (_sceneLoader != null) +
                            " roster=" + _rosterCount0);
                        _sceneLoader.LoadGameplayScene("TestGameplayScene"); // UniTask fire-and-forget
                        Log("[Step1] loading TestGameplayScene…");
                        Next(4.0);
                        break;
                    }
                case 2: // scene load → ChibiSceneRoot bound → Reconcile spawns the roster
                    {
                        int after = CountChibis();
                        _okSceneSpawn = after == _rosterCount0;
                        Log("[Step2] " + (_okSceneSpawn ? "PASS" : "FAIL") +
                            " — scene load spawned chibis=" + after + " (roster=" + _rosterCount0 + ")");
                        _spawnBeforeRecruit = after;
                        _provider.RecruitOuterDisciple();
                        Log("[Step2] RecruitOuterDisciple() sent");
                        Next(1.5);
                        break;
                    }
                case 3:
                    {
                        int after = CountChibis();
                        _okSpawn = after == _spawnBeforeRecruit + 1;
                        Log("[Step3] " + (_okSpawn ? "PASS" : "FAIL") +
                            " — after recruit chibis=" + after + " (before=" + _spawnBeforeRecruit + ")");
                        Next(0.5);
                        break;
                    }
                case 4:
                    {
                        _targetVisual = Object.FindObjectsByType<SpriteChibiVisual>(
                            FindObjectsSortMode.None).OrderBy(v => v.GetInstanceID()).LastOrDefault();
                        if (_targetVisual == null) { Log("[Step4] no visual found — FAIL"); _okNoRespawn = false; Next(0.5); break; }
                        _discipleId = _targetVisual.DiscipleId;
                        _instanceId0 = _targetVisual.GetInstanceID();
                        string targetPart = PickOtherHair(_discipleId);
                        bool ok = _provider.TryChangeAvatarPart(_discipleId, "hair", targetPart,
                            out string failReason, out _);
                        Log("[Step4] TryChangeAvatarPart('" + _discipleId + "', hair → '" + targetPart + "') = " +
                            ok + (ok ? "" : " (" + failReason + ")"));
                        Next(1.0);
                        break;
                    }
                case 5:
                    {
                        bool alive = _targetVisual != null; // UnityEngine.Object implicit bool
                        bool same = alive && _targetVisual.GetInstanceID() == _instanceId0;
                        _okNoRespawn = same;
                        Log("[Step5] " + (same ? "PASS" : "FAIL") +
                            " — visual instance unchanged (id " + _instanceId0 + " → " +
                            (alive ? _targetVisual.GetInstanceID().ToString() : "DESTROYED") + ")");
                        Next(0.5);
                        break;
                    }
                case 6:
                    {
                        _countBeforeSwap = CountChibis();
                        Log("[Step6] swap TestGameplayScene → TestGameplayScene2 (chibis before=" + _countBeforeSwap + ")");
                        _sceneLoader.LoadGameplayScene("TestGameplayScene2"); // UniTask fire-and-forget
                        Next(4.0);
                        break;
                    }
                case 7:
                    {
                        int after = CountChibis();
                        _okSwap = after == _countBeforeSwap;
                        Log("[Step7] " + (_okSwap ? "PASS" : "FAIL") +
                            " — chibis after swap=" + after + " (expected " + _countBeforeSwap + ")");
                        Next(0.5);
                        break;
                    }
                case 8:
                    {
                        // 100-instance probe: clone d000's appearance under the live
                        // ChibiSceneRoot. NOTE: _targetVisual died with the old scene in
                        // the step-6 swap — take the parent from any LIVE chibi instead.
                        var inj = GameLifetimeScope.Injector;
                        var resolver = inj.Resolve<AppearanceResolver>();
                        var bank = inj.Resolve<ChibiFrameBank>();
                        var clock = inj.Resolve<ChibiFrameClock>();
                        var pool = inj.Resolve<AvatarPartPool>();
                        var anyLive = Object.FindObjectsByType<SpriteChibiVisual>(FindObjectsSortMode.None)
                            .FirstOrDefault();
                        Transform parent = anyLive != null ? anyLive.Transform.parent : null;
                        if (parent == null) { Fail("no live chibi parent for probes"); return; }

                        var avatar = _provider.BuildSectEconomyState().Disciples[0].Avatar.Clone();
                        _probeVisuals.Clear();
                        for (int i = 0; i < 100; i++)
                        {
                            var v = SpriteChibiVisual.Create(parent, resolver, bank, clock, pool);
                            v.Transform.localPosition = new Vector3((i % 10) * 1.2f, (i / 10) * -1.2f, 0);
                            v.Bind("probe_" + i, avatar, DiscipleSex.Male);
                            v.SetFacing(i % 2 == 0);
                            v.SetSortingBase(100 + i);
                            _probeVisuals.Add(v);
                        }
                        Log("[Step8] spawned " + _probeVisuals.Count + " probe chibis (total " + CountChibis() + ")");

                        _inMeasure = true;
                        _frameCount = 0;
                        CpuMs.Clear();
                        _gcSum = 0;
                        _gcSamples = 0;
                        Log("[Step8] measuring 100 chibis for ~12s…");
                        Next(12.0);
                        break;
                    }
                case 9:
                    {
                        _inMeasure = false;
                        ReportMeasure(CountChibis());
                        Next(0.5);
                        break;
                    }
                case 10:
                    {
                        for (int i = 0; i < _probeVisuals.Count; i++) _probeVisuals[i].Dispose();
                        _probeVisuals.Clear();
                        Log("[Step10] probes disposed (remaining " + CountChibis() + ")");
                        Next(0.5);
                        break;
                    }
                case 11:
                    {
                        // Destroy is end-of-frame deferred — the 0.5 s delay above makes
                        // this the real post-despawn count.
                        int remaining = CountChibis();
                        Log("[Step11] post-despawn chibis=" + remaining + " (expected roster " + _rosterCount0 + " + 1 recruit)");
                        Finish(partial: false);
                        break;
                    }
            }
        }

        // ---- driver hand-off (called via reflection from Phase2VerifyDriver) ----

        public static void NotifyFrameMeasured(float cpuMs, long gcBytesThisFrame)
        {
            if (!_inMeasure) return;
            _frameCount++;
            CpuMs.Add(cpuMs);
            _gcSum += gcBytesThisFrame;
            if (gcBytesThisFrame > 0) _gcSamples++;
        }

        // ---- helpers ----

        private static void EnsureChibiSceneRoot(string scenePath)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var existing = Object.FindObjectsByType<Xianxia.Sect.Visual.ChibiSceneRoot>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (existing.Length > 0) { Log("[ScenePrep] " + scenePath + " already has ChibiSceneRoot"); return; }

            var go = new GameObject("ChibiSceneRoot");
            go.AddComponent<Xianxia.Sect.Visual.ChibiSceneRoot>();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, scenePath);
            Log("[ScenePrep] added ChibiSceneRoot → " + scenePath);
        }

        private static void Next(double dur) { _step++; _stepUntil = EditorApplication.timeSinceStartup + dur; }

        private static int CountChibis()
        {
            return Object.FindObjectsByType<SpriteChibiVisual>(FindObjectsSortMode.None).Length;
        }

        private static string PickOtherHair(string discipleId)
        {
            var state = _provider.BuildSectEconomyState();
            var d = state.Disciples.First(x => x.DiscipleId == discipleId);
            string current = d.Avatar.GetSlot("hair");

            var pool = GameLifetimeScope.Injector.Resolve<AvatarPartPool>();
            foreach (var h in pool.GetPartsForSlot("hair"))
            {
                if (h.id != current && h.Supports(Xianxia.Sect.Visual.VisualBackend.SpriteSheet))
                    return h.id;
            }
            return current;
        }

        private static void ReportMeasure(int chibiCount)
        {
            if (CpuMs.Count == 0) { Log("[Perf] no frames captured"); return; }
            var sorted = CpuMs.OrderBy(x => x).ToList();
            // Recorder returns NANOSECONDS (Phase 0 lesson — VisualSpikeProfiler made the
            // same discovery); convert to ms before reporting.
            double median = sorted[sorted.Count / 2] / 1000000.0;
            double p95 = sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * 0.95f))] / 1000000.0;
            double gcAvg = _gcSum / CpuMs.Count;

            Log("[Perf] chibis=" + chibiCount +
                " frames=" + CpuMs.Count +
                " median=" + median.ToString("F3") + " ms" +
                " p95=" + p95.ToString("F3") + " ms" +
                " gc_avg=" + (gcAvg / 1024.0).ToString("F2") + " KB/frame" +
                " gc_frames_with_alloc=" + _gcSamples + "/" + CpuMs.Count);
            Log("[Stats] draw calls=" + UnityEditor.UnityStats.drawCalls +
                " batches=" + UnityEditor.UnityStats.batches);
        }

        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (condition != null && condition.StartsWith("[Phase2Verify]")) return; // avoid self-feed
            if (SessionLog.Count < 4096) SessionLog.Add("[" + type + "] " + condition);
        }

        private static void Finish(bool partial)
        {
            _session = false;
            if (_logAttached) { Application.logMessageReceived -= OnLogMessage; _logAttached = false; }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== Phase 2 acceptance verify ===" + (partial ? " (PARTIAL — play mode ended early)" : ""));
            sb.AppendLine("scene_load_spawns_roster:  " + (_okSceneSpawn ? "PASS" : "FAIL"));
            sb.AppendLine("recruit_auto_spawn:        " + (_okSpawn ? "PASS" : "FAIL"));
            sb.AppendLine("hair_change_no_respawn:    " + (_okNoRespawn ? "PASS" : "FAIL"));
            sb.AppendLine("scene_swap_despawn_respawn:" + (_okSwap ? "PASS" : "FAIL"));
            sb.AppendLine("--- session log (last 80) ---");
            int start = Math.Max(0, SessionLog.Count - 80);
            for (int i = start; i < SessionLog.Count; i++) sb.AppendLine(SessionLog[i]);

            try
            {
                File.WriteAllText(ReportPath, sb.ToString());
                File.Delete(SessionMarker);
            }
            catch (IOException) { }

            Log("report → " + ReportPath);
            Log("=== Phase 2 verify end ===");
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }

        private static void Fail(string why)
        {
            Log("[FAIL] " + why);
            try
            {
                File.WriteAllText(ReportPath, "=== Phase 2 verify ===\nFAIL: " + why + "\n");
                File.Delete(SessionMarker);
            }
            catch (IOException) { }
            _session = false;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }

        private static void Log(string msg) { Debug.Log("[Phase2Verify] " + msg); }
    }
}
#endif
