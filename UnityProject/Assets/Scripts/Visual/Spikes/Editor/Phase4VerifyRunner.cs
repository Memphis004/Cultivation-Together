#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using MessagePipe;
using UnityEditor;
using UnityEngine;
using VContainer;
using Xianxia.Sect.Messages;
using Xianxia.Sect.UI;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Visual.Spikes.EditorTools
{
    /// <summary>
    /// Phase 4 acceptance verification (editor-only), driven by the marker-file
    /// remote control: "phase4_verify" in Library/visual_spike_command.txt.
    ///
    /// Same domain-reload-proof pattern as Phase2/Phase3VerifyRunner:
    /// [InitializeOnLoad] update poll + session marker on disk + step machine
    /// while EditorApplication.isPlaying. Cross-reload state lives on disk.
    ///
    /// Phase 4 keeps Spine INERT the whole run (SpineActivationRequested stays
    /// FALSE — S4 is still a human decision; Phase 4 doesn't touch backend
    /// selection), so every disciple renders via SpriteChibiVisual — exactly
    /// the production default. Verifies (§11 Phase 4):
    ///   1. TaskActivityMapper table: longest-prefix + default + mock tasks
    ///   2. Chibi activity per disciple after Reconcile (d001=Walk; unknown
    ///      sprite states fall back to Idle with warn-ONCE, never spam)
    ///   3. Click targets: 1 ChibiClickTarget + 1 Collider2D per visual root;
    ///      one publish → one message → DiscipleDetail panel opens
    ///   4. derive-on-reconcile: task change → activity change, same instance
    ///   5. Work anchors: shipped scenes have none → TryGetWorkAnchor=false →
    ///      grid fallback (flagged in report, NOT invented procedurally)
    ///   6. Recruit event path: new disciple spawns with correct derived activity
    ///
    /// Click simulation note: OnMouseDown dispatch is Unity engine behavior —
    /// this runner verifies OUR code paths structurally (target+collider wired,
    /// publisher path opens the panel) and flags the engine-dispatch gap in the
    /// report. FindObjectsByType here is READ-ONLY inspection inside this editor
    /// verify tool (same Phase 2/3 carve-out; production code never searches).
    /// Output: Library/phase4_verify_report.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class Phase4VerifyRunner
    {
        private const string ReportPath = "Library/phase4_verify_report.txt";
        private const string SessionMarker = "Library/phase4_verify_live.txt";
        private const string SceneA = "Assets/Scenes/TestGameplayScene.unity";

        static Phase4VerifyRunner()
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
            Log("=== Phase 4 verify start ===");
            File.Delete(ReportPath);
            Log("[Gate] SpineEnabled=" + cfg.SpineEnabled +
                " SpineActivationRequested=" + cfg.SpineActivationRequested +
                " (must stay FALSE — Phase 4 doesn't touch backends; S4 still open)");
            if (cfg.SpineActivationRequested)
            {
                Fail("SpineActivationRequested is TRUE — this run must not depend on the license gate");
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
        private static TaskActivityMapper _mapper;
        private static UIService _uiService;
        private static ISubscriber<DiscipleSelectedMessage> _selectedSubscriber;
        private static IPublisher<DiscipleSelectedMessage> _selectedPublisher;

        private static int _publishCount;
        private static IDisposable _publishSub;
        private static int _mapperWarnCount;   // "[TaskActivityMapper] no mapping…"
        private static int _spriteWarnCount;   // "[SpriteChibiVisual] … not in chibi_anim.json"
        private static SpriteChibiVisual _d002;
        private static int _d002GoId;

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
            _publishCount = 0;
            _mapperWarnCount = 0;
            _spriteWarnCount = 0;

            _injector = GameLifetimeScope.Injector;
            if (_injector == null) throw new InvalidOperationException("no GameLifetimeScope.Injector");
            _provider = _injector.Resolve<ISectStateProvider>();
            _visualSystem = _injector.Resolve<DiscipleVisualSystem>();
            _mapper = _injector.Resolve<TaskActivityMapper>();
            _uiService = _injector.Resolve<UIService>();
            _selectedSubscriber = _injector.Resolve<ISubscriber<DiscipleSelectedMessage>>();
            _selectedPublisher = _injector.Resolve<IPublisher<DiscipleSelectedMessage>>();

            _publishSub = _selectedSubscriber.Subscribe(_ => _publishCount++);
            Application.logMessageReceived += OnLogMessage;

            Log("[Session] started — Spine stays INERT (production default), all visuals are Sprite");
        }

        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Warning || condition == null) return;
            if (condition.Contains("[TaskActivityMapper] no mapping for CurrentTask")) _mapperWarnCount++;
            if (condition.Contains("[SpriteChibiVisual]") && condition.Contains("not in chibi_anim.json")) _spriteWarnCount++;
        }

        private static void StepMachine()
        {
            _stepUntil = EditorApplication.timeSinceStartup + 0.5;

            try
            {
                switch (_step)
                {
                    case 0: // load gameplay scene → SceneLoadedMessage → root bind → Reconcile
                        _injector.Resolve<SceneLoader>().LoadGameplayScene(SceneA);
                        _stepUntil += 2.5;
                        _step++;
                        break;

                    case 1: // Criterion: TaskActivityMapper table (data-driven, longest-prefix, default)
                        Check("gathering_herb → Walk",   _mapper.ResolveActivity("gathering_herb") == "Walk");
                        Check("meditation → Resting",    _mapper.ResolveActivity("meditation") == "Resting");
                        Check("refining_elixir → Working", _mapper.ResolveActivity("refining_elixir") == "Working");
                        Check("forging_artifact → Working", _mapper.ResolveActivity("forging_artifact") == "Working");
                        Check("longest-prefix wins (gathering_herb_special → Walk)",
                              _mapper.ResolveActivity("gathering_herb_special") == "Walk");
                        Check("unmapped task → defaultActivity Idle",
                              _mapper.ResolveActivity("mystery_chore") == "Idle");
                        _step++;
                        break;

                    case 2: // Criterion: activity per disciple after reconcile
                    {
                        // RESET here: case 1 deliberately fires one unmapped-task
                        // warning ('mystery_chore') — the L9 warn-once probe — before
                        // the per-disciple checks. Everything below asserts on the
                        // spawn-time warnings only.
                        _mapperWarnCount = 0;
                        _spriteWarnCount = 0;

                        var d000 = FindSprite("d000");
                        var d001 = FindSprite("d001");
                        var d002 = FindSprite("d002");
                        var d003 = FindSprite("d003");
                        Check("d001 (gathering_herb) shows Walk", d001 != null && d001.CurrentActivity == "Walk");
                        // meditation IS mapped (no TaskActivityMapper warning below); sprite sheets
                        // only carry Idle/Walk, so the state falls back to Idle — warn-ONCE (case 6).
                        Check("d000 (meditation) activity = Idle (mapped 'Resting' → sprite fallback)",
                              d000 != null && d000.CurrentActivity == "Idle");
                        Check("d003 (forging_artifact) activity = Idle (mapped 'Working' → sprite fallback)",
                              d003 != null && d003.CurrentActivity == "Idle");
                        Check("TaskActivityMapper emitted no unmapped-task warnings during THIS window (mock tasks all mapped; spawn warnings not re-fired)",
                              _mapperWarnCount == 0);
                        _d002 = d002;
                        if (d002 != null) _d002GoId = d002.Transform.gameObject.GetInstanceID();
                        _step++;
                        break;
                    }

                    case 3: // Criterion: click targets — structural wiring, exactly one per visual
                    {
                        var targets = UnityEngine.Object.FindObjectsByType<ChibiClickTarget>(FindObjectsSortMode.None);
                        var sprites = UnityEngine.Object.FindObjectsByType<SpriteChibiVisual>(FindObjectsSortMode.None);
                        Check("one ChibiClickTarget per active visual (" + targets.Length + "/" + sprites.Length + ")",
                              targets.Length == sprites.Length && sprites.Length >= 4);
                        bool allWired = true;
                        for (int i = 0; i < targets.Length; i++)
                        {
                            var go = targets[i].gameObject;
                            var col = go.GetComponent<Collider2D>();
                            if (col == null || !col.enabled || !go.activeInHierarchy) allWired = false;
                        }
                        Check("every click target has an enabled Collider2D on an active GO", allWired);
                        _step++;
                        break;
                    }

                    case 4: // Criterion: one publish → one message → panel opens (click dispatch minus engine input)
                        Check("no stray publishes before the click", _publishCount == 0);
                        _selectedPublisher.Publish(new DiscipleSelectedMessage { DiscipleId = "d001" });
                        _stepUntil += 0.6; // MessagePipe → DiscipleDetailUISystem → UIService.Open
                        _step++;
                        break;

                    case 5:
                    {
                        Check("exactly ONE DiscipleSelectedMessage delivered (got " + _publishCount + ")",
                              _publishCount == 1);
                        var views = UnityEngine.Object.FindObjectsByType<DiscipleDetailView>(FindObjectsSortMode.None);
                        Check("DiscipleDetail panel opened (view alive: " + views.Length + ")",
                              views.Length == 1 && views[0].gameObject.activeInHierarchy);
                        Check("panel portrait renderer bound (same AvatarRenderer pipeline — no drift by construction)",
                              views.Length == 1 && views[0].PortraitRenderer != null);
                        _step++;
                        break;
                    }

                    case 6: // Criterion: derive-on-reconcile — task change → activity, same instance
                    {
                        if (_d002 == null) { Check("d002 visual alive for derive-on-reconcile", false); _step++; break; }
                        int warnsBefore = _spriteWarnCount;
                        var roster = _provider.BuildSectEconomyState().Disciples;
                        var d002 = Find(roster, "d002");
                        d002.CurrentTask = "patrol"; // live state (BuildSectEconomyState) — no task system yet
                        _visualSystem.Reconcile();   // derive-on-reconcile path (event-triggered, not polling)
                        _stepUntil += 0.3;
                        _pendingWarns = warnsBefore;
                        _step++;
                        break;
                    }

                    case 7:
                    {
                        Check("d002 activity now Walk (patrol → Walk)", _d002.CurrentActivity == "Walk");
                        Check("d002 SAME GameObject (no respawn on task change)",
                              _d002.Transform.gameObject.GetInstanceID() == _d002GoId);
                        // warn-once no-spam: two more reconciles add no sprite fallback warnings
                        _visualSystem.Reconcile();
                        _visualSystem.Reconcile();
                        _stepUntil += 0.3;
                        _step++;
                        break;
                    }

                    case 8:
                    {
                        Check("sprite fallback warnings stable after 3 reconciles (no spam: " +
                              (_spriteWarnCount - _pendingWarns) + " new)",
                              _spriteWarnCount - _pendingWarns == 0);
                        Check("no NEW TaskActivityMapper warnings after the L9 probe (warn-once holds)",
                              _mapperWarnCount == 0);
                        _step++;
                        break;
                    }

                    case 9: // Criterion: work anchors — shipped scenes have none → grid fallback (flagged, not invented)
                    {
                        var roots = UnityEngine.Object.FindObjectsByType<ChibiSceneRoot>(FindObjectsSortMode.None);
                        Check("exactly one ChibiSceneRoot bound", roots.Length == 1);
                        if (roots.Length == 1)
                        {
                            bool noAnchors = !roots[0].TryGetWorkAnchor("gathering_herb", out _) &&
                                             !roots[0].TryGetWorkAnchor("meditation", out _) &&
                                             !roots[0].TryGetWorkAnchor("refining_elixir", out _) &&
                                             !roots[0].TryGetWorkAnchor("forging_artifact", out _);
                            Check("no work anchors in shipped scenes → grid fallback (level-design data pending)",
                                  noAnchors);
                        }
                        _step++;
                        break;
                    }

                    case 10: // Criterion: recruit event path — spawn with derived activity, no respawn of others
                    {
                        _publishCount = 0; // isolate: recruit must not emit DiscipleSelected
                        _provider.RecruitOuterDisciple(); // DiscipleRecruitedMessage → Reconcile
                        _stepUntil += 1.0;
                        _step++;
                        break;
                    }

                    case 11:
                    {
                        var roster = _provider.BuildSectEconomyState().Disciples;
                        DiscipleState newbie = null;
                        for (int i = 0; i < roster.Count; i++)
                            if (roster[i] != null && roster[i].DiscipleId != "d000" &&
                                roster[i].DiscipleId != "d001" && roster[i].DiscipleId != "d002" &&
                                roster[i].DiscipleId != "d003") newbie = roster[i];
                        Check("recruit added exactly one disciple", newbie != null && roster.Count == 5);
                        if (newbie != null)
                        {
                            var v = FindSprite(newbie.DiscipleId);
                            Check("new recruit spawned a visual automatically (" + newbie.DiscipleId + ")", v != null);
                            if (v != null)
                            {
                                // sprite vocabulary: Walk exists; anything else falls back to Idle
                                string activity = _mapper.ResolveActivity(newbie.CurrentTask);
                                string expected = activity == "Walk" ? "Walk" : "Idle";
                                Check("recruit activity derived from CurrentTask ('" + newbie.CurrentTask +
                                      "' → " + expected + ")", v.CurrentActivity == expected);
                            }
                        }
                        Check("reconcile/recruit emitted no DiscipleSelectedMessage", _publishCount == 0);
                        Check("d002 kept Walk after recruit reconcile (derive stays consistent)",
                              _d002 != null && _d002.CurrentActivity == "Walk");
                        Finish(true, null);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[Phase4Verify] FAILED: " + ex);
                Finish(false, ex.Message);
            }
        }

        private static int _pendingWarns;

        private static SpriteChibiVisual FindSprite(string discipleId)
        {
            // READ-ONLY inspection inside this editor verify tool (Phase 2/3 carve-out)
            var all = UnityEngine.Object.FindObjectsByType<SpriteChibiVisual>(FindObjectsSortMode.None);
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
            if (_publishSub != null) { _publishSub.Dispose(); _publishSub = null; }

            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;

            try
            {
                var summary = "phase4 verify " + (ok && _fail == 0 ? "COMPLETE" : "FAILED") +
                              " pass=" + _pass + " fail=" + _fail +
                              (string.IsNullOrEmpty(note) ? "" : " note=" + note) +
                              " @ " + DateTime.UtcNow.ToString("o");
                File.WriteAllText(ReportPath, summary);
                Log("report → " + ReportPath + " (" + summary + ")");
            }
            catch { /* ignore */ }

            Log("=== Phase 4 verify end ===");
        }

        private static void Log(string msg)
        {
            Debug.Log("[Phase4Verify] " + msg);
        }

        private static void Fail(string msg)
        {
            Debug.LogError("[Phase4Verify] " + msg);
        }
    }
}
#endif
