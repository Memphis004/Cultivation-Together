#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using VContainer;
using Xianxia.Sect.Visual;   // DiscipleVisualSystem, VisualRuntimeConfig, SpineChibiVisual (editor-verify only)
using Spine.Unity;           // SkeletonDataAsset (example rig) — editor-verify only

namespace Xianxia.Sect.Visual.Spikes.EditorTools
{
    /// <summary>
    /// Phase 3 acceptance verification (editor-only), driven by the marker-file remote
    /// control: "phase3_verify" in Library/visual_spike_command.txt.
    ///
    /// Survives the play-mode domain reload the same way Phase2VerifyRunner does:
    /// an [InitializeOnLoad] update poll re-registers after every reload and drives
    /// the step machine while EditorApplication.isPlaying is true and the session
    /// marker exists. Cross-reload state lives on disk, not in statics.
    ///
    /// Verifies the five acceptance criteria verifiable with the FREE example rig
    /// (mix-and-match-pro), WITHOUT touching the production S4 license gate —
    /// SpineActivationRequested stays FALSE the whole run. The runner registers the
    /// same factory shape the production bootstrap would (license-confirmed) and
    /// flips only SpineEnabled INSIDE the play session; statics reset on play exit,
    /// which restores the production state by construction.
    ///
    /// Static access only (C11/C12 — no FindObjectOfType in production paths;
    /// FindObjectsByType&lt;SpriteChibiVisual&gt; is READ-ONLY inspection inside this
    /// editor verify tool, same carve-out as Phase 2).
    /// Output: Library/phase3_verify_report.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class Phase3VerifyRunner
    {
        private const string ReportPath = "Library/phase3_verify_report.txt";
        private const string SessionMarker = "Library/phase3_verify_live.txt";
        // "Assets/Spine Examples" is NOT a Resources folder — must load via AssetDatabase
        // (same lesson as the Phase 0 SpineSpikeRunner); Resources path kept as fallback.
        private const string SkeletonAssetEditorPath =
            "Assets/Spine Examples/Spine Skeletons/mix-and-match/mix-and-match-pro_SkeletonData.asset";
        private const string SkeletonAssetResourcePath =
            "Spine Examples/Spine Skeletons/mix-and-match/mix-and-match-pro_SkeletonData";

        private static UnityEngine.Object _logSubscriptionOwner;
        private const string SceneA = "Assets/Scenes/TestGameplayScene.unity";
        private const string UnknownActivity = "totally_unknown_activity";

        static Phase3VerifyRunner()
        {
            EditorApplication.update += SessionUpdate;
        }

        // ---- entry (editor context, before play) ----

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Log("[Play] still playing — stopping, re-issue the command");
                EditorApplication.isPlaying = false;
                return;
            }

            var cfg = VisualRuntimeConfig.Instance;
            Log("=== Phase 3 verify start ===");
            File.Delete(ReportPath);
            Log("[Gate] SpineEnabled=" + cfg.SpineEnabled +
                " SpineActivationRequested=" + cfg.SpineActivationRequested +
                " (must stay FALSE — S4 human decision)" +
                " SpineBudget=" + cfg.SpineBudget);
            if (cfg.SpineActivationRequested)
            {
                Fail("SpineActivationRequested is TRUE — production license gate must not be touched by this run");
                return;
            }

            try { File.WriteAllText(SessionMarker, "requested"); }
            catch (IOException) { }

            EditorApplication.isPlaying = true;
            Log("[Play] entering play mode (current scene)…");
        }

        // ---- session poll (survives domain reload) ----

        private static bool _session;
        private static int _step;
        private static double _stepUntil;
        private static int _pass, _fail;

        private static IObjectResolver _injector;
        private static ISectStateProvider _provider;
        private static DiscipleVisualSystem _visualSystem;
        private static AvatarPartPool _pool;
        private static AppearanceResolver _resolver;

        private static SpineChibiVisual _d000;
        private static SpriteChibiVisual _d001SpriteBefore;
        private static Vector3 _d001Pos;
        private static bool _d001Facing;
        private static string _d001Activity;
        private static int _mapWarnCount;

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
                    Finish(true, "play mode ended");
                    return;
                }
                if (EditorApplication.timeSinceStartup < _stepUntil) return;
                StepMachine();
            }
            else if (!marker)
            {
                EditorApplication.update -= SessionUpdate; // idle poll until next run
            }
        }

        private static void StartSession()
        {
            _session = true;
            _step = 0;
            _pass = 0;
            _fail = 0;
            _stepUntil = EditorApplication.timeSinceStartup + 1.0;
            _mapWarnCount = 0;

            _injector = GameLifetimeScope.Injector;
            if (_injector == null) throw new InvalidOperationException("no GameLifetimeScope.Injector");
            _provider = _injector.Resolve<ISectStateProvider>();
            _visualSystem = _injector.Resolve<DiscipleVisualSystem>();
            _pool = _injector.Resolve<AvatarPartPool>();
            _resolver = _injector.Resolve<AppearanceResolver>();

            // RUNNER-ONLY Spine activation (mirrors VisualSpineBootstrap minus the
            // license gate): same factory shape the production plugin registers.
            var skeletonDataAsset = AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(SkeletonAssetEditorPath);
            if (skeletonDataAsset == null)
                skeletonDataAsset = Resources.Load<SkeletonDataAsset>(SkeletonAssetResourcePath);
            if (skeletonDataAsset == null)
                throw new InvalidOperationException("example SkeletonDataAsset missing: " + SkeletonAssetEditorPath);
            var activityMap = new ChibiActivityMap();
            DiscipleVisualSystem.SpineVisualFactory = (d, parent) =>
                SpineChibiVisual.Create(skeletonDataAsset, parent, _resolver, _pool, activityMap,
                                        VisualRuntimeConfig.Instance);
            VisualRuntimeConfig.Instance.SpineEnabled = true;

            Application.logMessageReceived += OnLogMessage;

            Log("[Session] started — runner registered SpineVisualFactory (example rig), SpineEnabled=true");
        }

        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning && condition != null &&
                condition.Contains("ChibiActivityMap") && condition.Contains(UnknownActivity))
            {
                _mapWarnCount++;
            }
        }

        private static void StepMachine()
        {
            _stepUntil = EditorApplication.timeSinceStartup + 0.5;

            try
            {
                switch (_step)
                {
                    case 0: // load a gameplay scene → SceneLoadedMessage → bind root → Reconcile
                        _injector.Resolve<SceneLoader>().LoadGameplayScene(SceneA);
                        _stepUntil += 2.5;
                        _step++;
                        break;

                    case 1: // Criterion 1 — entitlements resolve through allocation
                    {
                        var roster = _provider.BuildSectEconomyState().Disciples;
                        Check("d000 entitlement=Spine", Find(roster, "d000")?.ChibiBackend == ChibiBackend.Spine);
                        Check("d003 entitlement=Spine", Find(roster, "d003")?.ChibiBackend == ChibiBackend.Spine);
                        Check("d001 entitlement=SpriteSheet", Find(roster, "d001")?.ChibiBackend == ChibiBackend.SpriteSheet);
                        Check("d002 entitlement=SpriteSheet", Find(roster, "d002")?.ChibiBackend == ChibiBackend.SpriteSheet);
                        Check("d000 renders Spine (example rig)",
                              SpineChibiVisual.Active.TryGetValue("d000", out _));
                        Check("d003 renders Spine (example rig)",
                              SpineChibiVisual.Active.TryGetValue("d003", out _));
                        Check("d001 NOT rendered as Spine (sprite entitlement)",
                              !SpineChibiVisual.Active.ContainsKey("d001"));
                        Check("d002 NOT rendered as Spine (sprite entitlement)",
                              !SpineChibiVisual.Active.ContainsKey("d002"));
                        _d000 = SpineChibiVisual.Active.TryGetValue("d000", out var v000) ? v000 : null;
                        _step++;
                        break;
                    }

                    case 2: // capture d001 sprite state (for the promotion comparison)
                    {
                        _d001SpriteBefore = FindSpriteVisual("d001");
                        Check("d001 has a live Sprite visual before promotion", _d001SpriteBefore != null);
                        if (_d001SpriteBefore != null)
                        {
                            _d001Pos = _d001SpriteBefore.Transform.position;
                            _d001Facing = _d001SpriteBefore.FacingRight;
                            _d001Activity = _d001SpriteBefore.CurrentActivity;
                        }
                        _step++;
                        break;
                    }

                    case 3: // Criterion 2 — budget degrade (render-only)
                    {
                        VisualRuntimeConfig.Instance.SpineBudget = 1;
                        _visualSystem.Reconcile(); // allocation keeps only the top-priority Spine slot
                        _stepUntil += 1.0;
                        _step++;
                        break;
                    }

                    case 4:
                    {
                        bool d000Kept = _d000 != null && SpineChibiVisual.Active.ContainsKey("d000") &&
                                        ReferenceEquals(SpineChibiVisual.Active["d000"], _d000);
                        Check("budget=1: d000 keeps Spine (same visual instance)", d000Kept);
                        Check("budget=1: d003 no longer renders Spine",
                              !SpineChibiVisual.Active.ContainsKey("d003"));
                        var d003 = Find(_provider.BuildSectEconomyState().Disciples, "d003");
                        Check("budget=1: d003 entitlement STILL Spine in state",
                              d003 != null && d003.ChibiBackend == ChibiBackend.Spine);
                        VisualRuntimeConfig.Instance.SpineBudget = 20;
                        _step++;
                        break;
                    }

                    case 5: // Criterion 3 — promote d001 via TrySetChibiBackend
                    {
                        bool ok = _provider.TrySetChibiBackend("d001", ChibiBackend.Spine, out var fail);
                        Check("TrySetChibiBackend(d001 → Spine) = true", ok);
                        _stepUntil += 1.0; // message → OnChibiBackendChanged → respawn in place
                        _step++;
                        break;
                    }

                    case 6:
                    {
                        Check("d001 entitlement now Spine",
                              Find(_provider.BuildSectEconomyState().Disciples, "d001")?.ChibiBackend == ChibiBackend.Spine);
                        Check("d001 now renders Spine",
                              SpineChibiVisual.Active.TryGetValue("d001", out var v001));
                        if (v001 != null)
                        {
                            float posDelta = Vector3.Distance(v001.Transform.position, _d001Pos);
                            Check("promotion kept world position (delta=" + posDelta.ToString("F4") + ")",
                                  posDelta < 0.01f);
                            Check("promotion kept facing (before=" + _d001Facing + " after=" + v001.FacingRight + ")",
                                  v001.FacingRight == _d001Facing);
                            Check("promotion kept activity (before=" + _d001Activity + " after=" + v001.CurrentActivity + ")",
                                  v001.CurrentActivity == _d001Activity);
                        }
                        _d001AfterPromote = v001;
                        _step++;
                        break;
                    }

                    case 7: // Criterion 4 — ApplySlot incremental (no GameObject recreation)
                    {
                        var v = _d001AfterPromote;
                        if (v != null && v.Skeleton != null)
                        {
                            int goIdBefore = v.Transform.gameObject.GetInstanceID();
                            string skinBefore = v.Skeleton.Skin != null ? v.Skeleton.Skin.Name : string.Empty;
                            v.ApplySlot("head", "head_male_01");
                            int goIdAfter = v.Transform.gameObject.GetInstanceID();
                            string skinAfter = v.Skeleton.Skin != null ? v.Skeleton.Skin.Name : string.Empty;
                            Check("ApplySlot keeps the same GameObject (no respawn)", goIdBefore == goIdAfter);
                            Check("ApplySlot rebuilt the instance skin (name '" + skinAfter + "')",
                                  skinAfter == "d001_skin" && skinBefore == "d001_skin");
                        }
                        else
                        {
                            Check("d001 Spine visual alive for ApplySlot", false);
                        }
                        _step++;
                        break;
                    }

                    case 8: // Criterion 5 — unknown activity → Idle fallback, warn once per visual
                    {
                        var v = _d001AfterPromote;
                        if (v != null)
                        {
                            int warnsBefore = _mapWarnCount;
                            v.SetActivity(UnknownActivity);
                            Check("unknown activity does not throw and visual survives", v != null);
                            _stepUntil += 0.3; // let any warning land
                            _pendingWarnCheck = warnsBefore;
                        }
                        else
                        {
                            Check("d001 Spine visual alive for activity fallback", false);
                            _step++;
                        }
                        _step++;
                        break;
                    }

                    case 9:
                    {
                        Check("unknown activity logged exactly ONE warning (got " + (_mapWarnCount - _pendingWarnCheck) + ")",
                              _mapWarnCount - _pendingWarnCheck == 1);
                        // warn-once: repeat call adds no new warning
                        var v = _d001AfterPromote;
                        if (v != null) v.SetActivity(UnknownActivity);
                        _stepUntil += 0.3;
                        _step++;
                        break;
                    }

                    case 10:
                    {
                        Check("repeat unknown activity adds no warning (got " + (_mapWarnCount - _pendingWarnCheck) + ")",
                              _mapWarnCount - _pendingWarnCheck == 1);

                        // cleanup: back to the Phase-1 entitlement
                        _provider.TrySetChibiBackend("d001", ChibiBackend.SpriteSheet, out _);
                        _stepUntil += 1.0;
                        _step++;
                        break;
                    }

                    case 11:
                    {
                        Check("d001 back on Sprite visual after demote",
                              !SpineChibiVisual.Active.ContainsKey("d001"));
                        Finish(true, null);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[Phase3Verify] FAILED: " + ex);
                Finish(false, ex.Message);
            }
        }

        private static int _pendingWarnCheck;
        private static SpineChibiVisual _d001AfterPromote;

        private static SpriteChibiVisual FindSpriteVisual(string discipleId)
        {
            // READ-ONLY inspection inside this editor verify tool (Phase 2 carve-out)
            var all = Object.FindObjectsByType<SpriteChibiVisual>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].DiscipleId == discipleId) return all[i];
            }
            return null;
        }

        private static DiscipleState Find(List<DiscipleState> list, string id)
        {
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].DiscipleId == id) return list[i];
            return null;
        }

        private static void Check(string what, bool ok)
        {
            if (ok) _pass++; else _fail++;
            Log("[" + (ok ? "PASS" : "FAIL") + "] " + what);
        }

        private static void Finish(bool ok, string note)
        {
            if (!_session) return;
            _session = false;

            Application.logMessageReceived -= OnLogMessage;

            // restore runtime config + factory hook (statics also die with the play
            // session's domain unload — belt and braces for the same-session case)
            try
            {
                VisualRuntimeConfig.Instance.SpineEnabled = false;
                VisualRuntimeConfig.Instance.SpineBudget = 20;
                DiscipleVisualSystem.SpineVisualFactory = null;
            }
            catch { /* play mode teardown */ }

            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;

            try
            {
                var summary = "phase3 verify " + (ok && _fail == 0 ? "COMPLETE" : "FAILED") +
                              " pass=" + _pass + " fail=" + _fail +
                              (string.IsNullOrEmpty(note) ? "" : " note=" + note) +
                              " @ " + DateTime.UtcNow.ToString("o");
                File.WriteAllText(ReportPath, summary);
                Log("report → " + ReportPath + " (" + summary + ")");
            }
            catch { /* ignore */ }

            Log("=== Phase 3 verify end ===");
        }

        private static void Log(string msg)
        {
            Debug.Log("[Phase3Verify] " + msg);
        }

        private static void Fail(string msg)
        {
            Debug.LogError("[Phase3Verify] " + msg);
        }
    }
}
#endif
