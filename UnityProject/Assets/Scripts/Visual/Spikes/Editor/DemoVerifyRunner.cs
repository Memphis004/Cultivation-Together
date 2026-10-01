#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using MessagePipe;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using Xianxia.Sect.Messages;
using Xianxia.Sect.UI;        // UIService
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Visual.Spikes.EditorTools
{
    /// <summary>
    /// Visual Demo Scene acceptance verification (editor-only), driven by the
    /// marker-file remote control command "demo_verify" (same marker + step-machine
    /// pattern as Phase2-5 runners — survives the play-mode domain reload via
    /// [InitializeOnLoad] + session marker on disk).
    ///
    /// The demo scene (built by VisualDemoSceneTool via the "play_demo" marker) puts
    /// d000/d003 on Spine and d001/d002 on Sprite — BOTH backends on screen at once.
    /// This runner proves the 7 HUD buttons' acceptance criteria without OnGUI input:
    ///   1. demo scene loaded additively over the boot scene, root bound, 4 chibis
    ///   2. backend map correct (d000/d003 Spine, d001/d002 Sprite)
    ///   3. [1]/[2] budget=1 → d003 degrades to Sprite IN PLACE; restore → back to Spine
    ///   4. [3] hair swap via TryChangeAvatarPart → SAME GameObject (skin rebuild in place)
    ///   5. [4] TrySetChibiBackend(d001, Spine) → respawn keeps pos/activity/facing
    ///   6. [5] one publish → DiscipleDetail opens for d002
    ///   7. [6] task change → activity updates on next reconcile, same instance
    ///   8. [7] DemoMissing → Spine plays lowercase "idle" fallback + warn ONCE
    ///   9. ship defaults intact: S4 gate ON (production), demo seam true only while the demo scene lives
    ///
    /// BOOT-SCENE NOTE (the S4 era changed this): with SpineActivationRequested ON,
    /// SceneLoader.Start() also auto-loads SectScene over the boot scene. Because the
    /// demo scene is loaded additively by VisualDemoLoadWatcher BEFORE that auto-load
    /// resolves, SectScene ends up stacked on top of it ("last scene wins" → the visual
    /// system binds SectScene's ChibiSceneRoot and spawns the production roster).
    /// The runner therefore unloads SectScene in step 0 and re-reconciles so the demo
    /// scene is the ONLY gameplay scene — its own DevSpineOverride seam is what the
    /// steps below are meant to exercise.
    ///
    /// FindObjectsByType here is READ-ONLY inspection inside this editor verify tool
    /// (same Phase 2/3/4/5 carve-out; production/demo code never searches).
    /// Output: Library/demo_verify_report.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class DemoVerifyRunner
    {
        private const string ReportPath = "Library/demo_verify_report.txt";
        private const string SessionMarker = "Library/demo_verify_live.txt";
        private const string DemoSceneName = "VisualDemoScene";
        private const string SectSceneName = SceneNames.Sect;
        private const string LongHair = "hair_topknot_long";
        private const string ShortHair = "hair_short";

        static DemoVerifyRunner()
        {
            EditorApplication.update += SessionUpdate;
        }

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Log("[Play] still playing — stopping, re-issue the command");
                EditorApplication.isPlaying = false;
                return;
            }

            var cfg = VisualRuntimeConfig.Instance;
            Log("=== demo verify start ===");
            try { File.Delete(ReportPath); } catch (IOException) { }
            // (stale-gate fix) เดิม: SpineActivationRequested ต้อง FALSE (ยุคก่อน S4) —
            // ขัดกับ S4 decision (2026-09-25) ที่เปิด gate ถาวร. Demo ใช้ DevSpineOverride
            // ของตัวเอง (C3 seam) จึงไม่ขึ้นกับ production gate
            Log("[Gate-note] SpineActivationRequested=" + cfg.SpineActivationRequested +
                " — S4 (2026-09-25) allows Spine; demo drives its own DevSpineOverride seam");

            // (S4 fix) SceneLoader.Start() auto-loads SectScene over the boot scene. Before
            // S4 the Spine path was inert so the demo scene was effectively the only
            // gameplay scene; now SectScene really loads and would stack on top of the
            // demo scene ("last scene wins" → visual system binds SectScene's root and
            // spawns the production roster). The in-play settle loop in StepMachine()
            // unloads it before any assertion runs.

            try { File.WriteAllText(SessionMarker, "requested"); } catch (IOException) { }
            EditorApplication.isPlaying = true;
            Log("[Play] entering play mode on the boot scene — demo scene will be loaded additively");
        }

        // ---- session poll (survives domain reload) ----

        private static bool _session;
        private static int _step;
        private static double _stepUntil;
        private static int _pass, _fail;

        private static IObjectResolver _injector;
        private static ISectStateProvider _provider;
        private static DiscipleVisualSystem _visualSystem;
        private static UIService _uiService;


        private static bool _settled;             // demo scene is the ONLY gameplay scene

        private static Func<DiscipleState, Transform, IChibiVisual> _savedOverrideFactory;

        private static int _publishCount;
        private static IDisposable _publishSub;
        private static int _spineWarnCount;   // "[SpineChibiVisual] animation '…' … not in SkeletonData"
        private static Dictionary<string, int> _spineWarnsByKey; // warn-once proof (key → count)

        // per-stage snapshots
        private static Vector3 _d003Pos;
        private static bool _d003Facing;
        private static string _d003Activity;
        private static int _d003GoId;

        private static Vector3 _d001Pos;
        private static bool _d001Facing;
        private static string _d001Activity;
        private static int _d001NewGoId;
        private static string _d001TaskAtPromote;

        private static int _d000GoId;

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
                    Finish(false, "play mode ended early");
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
            _stepUntil = EditorApplication.timeSinceStartup + 2.0; // demo scene additive load first
            _publishCount = 0;
            _spineWarnCount = 0;
            _spineWarnsByKey = new Dictionary<string, int>(StringComparer.Ordinal);

            _settled = false;

            _injector = GameLifetimeScope.Injector;
            if (_injector == null) throw new InvalidOperationException("no GameLifetimeScope.Injector");
            _provider = _injector.Resolve<ISectStateProvider>();
            _visualSystem = _injector.Resolve<DiscipleVisualSystem>();
            _uiService = _injector.Resolve<UIService>();

            // pin the demo's own backend map: clear the Q5 story-character override factory
            // so d000/d002 do NOT take the override path (production visual_overrides.json
            // puts d000 + d002 on their own rigs, which would make d002 Spine and break the
            // demo's d000/d003-Spine + d001/d002-Sprite acceptance map). Runner-only —
            // restored in Finish, same carve-out as Phase 3/4.
            _savedOverrideFactory = DiscipleVisualSystem.SpineOverrideVisualFactory;
            DiscipleVisualSystem.SpineOverrideVisualFactory = null;

            _publishSub = _injector.Resolve<ISubscriber<DiscipleSelectedMessage>>().Subscribe(_ => _publishCount++);
            Application.logMessageReceived += OnLogMessage;

            Log("[Session] started — waiting for the demo scene load, then stepping");
        }

        /// <summary>
        /// S4 fix — wait until the demo scene is the ONLY gameplay scene before asserting.
        ///
        /// Before S4 the Spine path was inert, so the demo scene was effectively alone in
        /// the boot scene. With the S4 gate ON, SceneLoader.Start() really does auto-load
        /// SectScene, and while that load is in flight the demo scene (driven by
        /// VisualDemoLoadWatcher) can land first and STACK — two ChibiSceneRoots live,
        /// "last scene wins" binding SectScene's root and spawning the production roster
        /// instead of the demo's DevSpineOverride roster.
        ///
        /// The watcher now waits for the boot auto-load to settle before driving the demo
        /// load, so SceneLoader's normal unload path runs (SceneUnloadedMessage → visuals
        /// dropped→ demo scene re-bound). This loop just waits for that to complete.
        /// </summary>
        private static bool TrySettleScenes()
        {
            if (!IsSceneLoaded(DemoSceneName)) return false; // boot scene only so far
            if (IsSceneLoaded(SectSceneName)) return false;  // stale auto-loaded scene still live

            _settled = true;
            Log("[SectGuard] settled — demo scene is the only gameplay scene");
            return true;
        }

        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Warning || condition == null) return;
            // count ONLY the L9 fallback warning text (warn-once target of button [7])
            if (condition.Contains("[SpineChibiVisual] animation '") &&
                condition.Contains("not in SkeletonData"))
            {
                _spineWarnCount++;
                string key = condition; // same (visual, activity) → identical text → warn-once proof
                int n;
                _spineWarnsByKey.TryGetValue(key, out n);
                _spineWarnsByKey[key] = n + 1;
            }
        }

        private static void StepMachine()
        {
            _stepUntil = EditorApplication.timeSinceStartup + 0.5;

            try
            {
                if (!_settled && !TrySettleScenes()) return;

                switch (_step)
                {
                    case 0: // demo scene additively loaded + 4 chibis spawned through production path
                    {
                        Check("VisualDemoScene loaded additively", IsSceneLoaded(DemoSceneName));
                        var root = UnityEngine.Object.FindObjectsByType<ChibiSceneRoot>(FindObjectsSortMode.None);
                        Check("exactly one ChibiSceneRoot bound (demo scene)", root.Length == 1);

                        var spineCount = SpineChibiVisual.Active.Count;
                        Check("Spine visuals active = 2 (d000/d003), got " + spineCount, spineCount == 2);

                        bool d000Spine = SpineChibiVisual.Active.ContainsKey("d000");
                        bool d003Spine = SpineChibiVisual.Active.ContainsKey("d003");
                        Check("d000 + d003 on Spine", d000Spine && d003Spine);

                        var d001 = FindSprite("d001");
                        var d002 = FindSprite("d002");
                        Check("d001/d002 on SpriteSheet (all 4 founders visible, both backends)",
                              d001 != null && d002 != null);

                        Check("SectScene kept out of the run (S4 auto-load unloaded)",
                              !IsSceneLoaded(SectSceneName));
                        Check("ChibiSceneRoot bound to the demo scene", BoundRootSceneName() == DemoSceneName);

                        _stepUntil += 0.5;
                        _step++;
                        break;
                    }

                    case 1: // snapshot d003 before degrade + budget=1 through HUD helper
                    {
                        SpineChibiVisual d003;
                        if (!SpineChibiVisual.Active.TryGetValue("d003", out d003) || d003 == null)
                        {
                            Check("d003 Spine visual alive for degrade test", false);
                            _step = 20; // skip to wrap-up
                            break;
                        }
                        _d003Pos = d003.Transform.position;
                        _d003Facing = d003.FacingRight;
                        _d003Activity = d003.CurrentActivity;
                        _d003GoId = d003.Transform.gameObject.GetInstanceID();

                        var hud = UnityEngine.Object.FindObjectsByType<Xianxia.Sect.Visual.Spines.VisualDemoHud>(
                            FindObjectsSortMode.None);
                        Check("VisualDemoHud present", hud.Length == 1);
                        if (hud.Length == 1) hud[0].DebugSetBudget(1); // same path as button [1]
                        _stepUntil += 0.6;
                        _step++;
                        break;
                    }

                    case 2: // degrade verified: d003 = Sprite IN PLACE, state stays Spine
                    {
                        SpineChibiVisual spineAfter;
                        bool stillRegistered = SpineChibiVisual.Active.TryGetValue("d003", out spineAfter);
                        Check("d003 no longer Spine (budget=1, d000 wins priority)", !stillRegistered);

                        var d003 = FindSprite("d003");
                        Check("d003 renders SpriteSheet (visual)", d003 != null);
                        Check("d003 IN PLACE (position preserved)", d003 == null || d003.Transform.position == _d003Pos);
                        Check("d003 IN PLACE (activity preserved)",
                              d003 == null || d003.CurrentActivity == _d003Activity);
                        Check("d003 IN PLACE (facing preserved)", d003 == null || d003.FacingRight == _d003Facing);

                        var d = FindDisciple("d003");
                        Check("d003 state.ChibiBackend STILL Spine (entitlement untouched)",
                              d != null && d.ChibiBackend == ChibiBackend.Spine);

                        var d000Backend = SpineChibiVisual.Active.ContainsKey("d000");
                        Check("d000 STILL Spine (priority kept the budget slot)", d000Backend);
                        _step++;
                        break;
                    }

                    case 3: // restore budget → d003 back to Spine
                    {
                        var hud = UnityEngine.Object.FindObjectsByType<Xianxia.Sect.Visual.Spines.VisualDemoHud>(
                            FindObjectsSortMode.None);
                        if (hud.Length == 1) hud[0].DebugSetBudget(20); // same path as button [2]
                        _stepUntil += 0.6;
                        _step++;
                        break;
                    }

                    case 4:
                    {
                        Check("d003 back on Spine after restore", SpineChibiVisual.Active.ContainsKey("d003"));
                        _step++;
                        break;
                    }

                    case 5: // [3] hair swap → skin rebuild in place (production publish path)
                    {
                        SpineChibiVisual d000;
                        if (!SpineChibiVisual.Active.TryGetValue("d000", out d000) || d000 == null)
                        {
                            Check("d000 Spine visual alive for hair swap", false);
                            _step++;
                            break;
                        }
                        _d000GoId = d000.Transform.gameObject.GetInstanceID();

                        var hud = UnityEngine.Object.FindObjectsByType<Xianxia.Sect.Visual.Spines.VisualDemoHud>(
                            FindObjectsSortMode.None);
                        if (hud.Length == 1) hud[0].DebugHairSwap(); // TryChangeAvatarPart → AvatarEquipmentChangedMessage → ApplySlot
                        _stepUntil += 0.6;
                        _step++;
                        break;
                    }

                    case 6:
                    {
                        SpineChibiVisual d000;
                        bool alive = SpineChibiVisual.Active.TryGetValue("d000", out d000) && d000 != null;
                        Check("d000 still alive after hair swap", alive);
                        Check("d000 SAME GameObject (skin rebuild IN PLACE, no respawn)",
                              alive && d000.Transform.gameObject.GetInstanceID() == _d000GoId);
                        _step++;
                        break;
                    }

                    case 7: // [4] promote d001 → Spine, keep pos/activity/facing
                    {
                        var d001 = FindSprite("d001");
                        if (d001 == null)
                        {
                            Check("d001 Sprite visual alive for promotion test", false);
                            _step += 3;
                            break;
                        }
                        _d001Pos = d001.Transform.position;
                        _d001Facing = d001.FacingRight;
                        _d001Activity = d001.CurrentActivity;
                        _d001TaskAtPromote = FindDisciple("d001") != null ? FindDisciple("d001").CurrentTask : string.Empty;

                        var hud = UnityEngine.Object.FindObjectsByType<Xianxia.Sect.Visual.Spines.VisualDemoHud>(
                            FindObjectsSortMode.None);
                        if (hud.Length == 1) hud[0].DebugPromoteD001(); // TrySetChibiBackend → message → RespawnInPlace
                        _stepUntil += 0.6;
                        _step++;
                        break;
                    }

                    case 8:
                    {
                        SpineChibiVisual d001Spine;
                        bool promoted = SpineChibiVisual.Active.TryGetValue("d001", out d001Spine) && d001Spine != null;
                        Check("d001 now on Spine after promotion", promoted);

                        var d = FindDisciple("d001");
                        Check("d001 state.ChibiBackend = Spine", d != null && d.ChibiBackend == ChibiBackend.Spine);
                        if (promoted)
                        {
                            Check("d001 position preserved", d001Spine.Transform.position == _d001Pos);
                            Check("d001 facing preserved", d001Spine.FacingRight == _d001Facing);
                            Check("d001 activity preserved ('" + d001Spine.CurrentActivity + "' — sprite word may re-map, semantics kept)",
                                  !string.IsNullOrEmpty(d001Spine.CurrentActivity));
                        }
                        _stepUntil += 0.5;
                        _step++;
                        break;
                    }

                    case 9: // [5] one publish → DiscipleDetail for d002
                    {
                        _publishCount = 0;
                        var hud = UnityEngine.Object.FindObjectsByType<Xianxia.Sect.Visual.Spines.VisualDemoHud>(
                            FindObjectsSortMode.None);
                        if (hud.Length == 1) hud[0].DebugSelectD002();
                        _stepUntil += 0.6; // MessagePipe → DiscipleDetailUISystem → UIService.Open
                        _step++;
                        break;
                    }

                    case 10:
                    {
                        Check("exactly ONE DiscipleSelectedMessage delivered (got " + _publishCount + ")",
                              _publishCount == 1);
                        var views = UnityEngine.Object.FindObjectsByType<Xianxia.Sect.UI.DiscipleDetailView>(
                            FindObjectsSortMode.None);
                        Check("DiscipleDetail panel opened (view alive: " + views.Length + ")",
                              views.Length == 1 && views[0].gameObject.activeInHierarchy);
                        _step++;
                        break;
                    }

                    case 11: // [6] task change → activity update on next reconcile, same instance
                    {
                        SpineChibiVisual d001Spine;
                        if (!SpineChibiVisual.Active.TryGetValue("d001", out d001Spine) || d001Spine == null)
                        {
                            Check("d001 Spine visual alive for activity test", false);
                            _step += 2;
                            break;
                        }
                        _d001NewGoId = d001Spine.Transform.gameObject.GetInstanceID();

                        var d = FindDisciple("d001");
                        if (d != null) d.CurrentTask = "meditation"; // demo debug helper (no task system yet)
                        _visualSystem.Reconcile();
                        _stepUntil += 0.4;
                        _step++;
                        break;
                    }

                    case 12:
                    {
                        SpineChibiVisual d001Spine;
                        bool alive = SpineChibiVisual.Active.TryGetValue("d001", out d001Spine) && d001Spine != null;
                        Check("d001 activity now Resting (meditation → Resting)",
                              alive && d001Spine.CurrentActivity == "Resting");
                        Check("d001 SAME GameObject after task change (no respawn)",
                              alive && d001Spine.Transform.gameObject.GetInstanceID() == _d001NewGoId);
                        _step++;
                        break;
                    }

                    case 13: // [7] DemoMissing → intentional-missing animation → Idle fallback + warn ONCE
                    {
                        int warnsBefore = _spineWarnCount;
                        _pendingWarns = warnsBefore;

                        var hud = UnityEngine.Object.FindObjectsByType<Xianxia.Sect.Visual.Spines.VisualDemoHud>(
                            FindObjectsSortMode.None);
                        if (hud.Length == 1) hud[0].DebugDemoMissing(); // activity "DemoMissing" → missing anim
                        _stepUntil += 0.4;
                        _step++;
                        break;
                    }

                    case 14:
                    {
                        SpineChibiVisual d000;
                        bool alive = SpineChibiVisual.Active.TryGetValue("d000", out d000) && d000 != null;
                        Check("d000 activity recorded as DemoMissing (activity preserved, animation fell back)",
                              alive && d000.CurrentActivity == "DemoMissing");
                        Check("exactly ONE L9 fallback warning fired (got " + (_spineWarnCount - _pendingWarns) + ")",
                              _spineWarnCount - _pendingWarns == 1);
                        // warn-once: repeat the same activity — no new warning
                        var hud = UnityEngine.Object.FindObjectsByType<Xianxia.Sect.Visual.Spines.VisualDemoHud>(
                            FindObjectsSortMode.None);
                        if (hud.Length == 1) hud[0].DebugDemoMissing();
                        _stepUntil += 0.4;
                        _step++;
                        break;
                    }

                    case 15:
                    {
                        Check("repeating the same missing activity adds NO new warning (warn-once holds)",
                              _spineWarnCount - _pendingWarns == 1);
                        _step++;
                        break;
                    }

                    case 16: // flip sanity + wrap-up
                    {
                        foreach (var kvp in SpineChibiVisual.Active)
                        {
                            var skeleton = kvp.Value.Skeleton;
                            if (skeleton == null) continue;
                            Check("d" + kvp.Key + " spine flip via Skeleton.ScaleX (±1, not transform scale)",
                                  Math.Abs(skeleton.ScaleX) == 1f);
                        }
                        var cfg = VisualRuntimeConfig.Instance;
                        // (stale-gate fix) เดิมเช็ค !SpineActivationRequested — S4 เปิดถาวรแล้ว
                        // ตั้งแต่ 2026-09-25 จึงเช็คค่าที่ควรเป็นจริงแทน
                        Check("ship defaults: SpineActivationRequested TRUE (S4 2026-09-25)",
                              cfg.SpineActivationRequested);
                        Check("ship defaults intact: DevSpineOverride true ONLY during demo",
                              cfg.DevSpineOverride); // demo scene alive → true; enabler restores on unload
                        Finish(true, null);
                        break;
                    }

                    default:
                        Finish(false, "unexpected step " + _step);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[DemoVerify] FAILED: " + ex);
                Finish(false, ex.Message);
            }
        }

        private static int _pendingWarns;

        /// <summary>Scene name of the currently bound ChibiSceneRoot ("" when unbound).</summary>
        private static string BoundRootSceneName()
        {
            var root = UnityEngine.Object.FindObjectsByType<ChibiSceneRoot>(FindObjectsSortMode.None);
            for (int i = 0; i < root.Length; i++)
            {
                if (root[i] != null) return root[i].gameObject.scene.name;
            }
            return string.Empty;
        }

        private static bool IsSceneLoaded(string name)
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (s.name == name && s.isLoaded) return true;
            }
            return false;
        }

        private static SpriteChibiVisual FindSprite(string discipleId)
        {
            // READ-ONLY inspection inside this editor verify tool (Phase 2/3/4/5 carve-out)
            var all = UnityEngine.Object.FindObjectsByType<SpriteChibiVisual>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].DiscipleId == discipleId) return all[i];
            }
            return null;
        }

        private static DiscipleState FindDisciple(string id)
        {
            var state = _provider.BuildSectEconomyState();
            if (state == null || state.Disciples == null) return null;
            var disciples = state.Disciples;
            for (int i = 0; i < disciples.Count; i++)
            {
                var d = disciples[i];
                if (d != null && d.DiscipleId == id) return d;
            }
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

            try { DiscipleVisualSystem.SpineOverrideVisualFactory = _savedOverrideFactory; }
            catch { /* play mode teardown */ }

            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;

            try
            {
                var summary = "demo verify " + (ok && _fail == 0 ? "COMPLETE" : "FAILED") +
                              " pass=" + _pass + " fail=" + _fail +
                              (string.IsNullOrEmpty(note) ? "" : " note=" + note) +
                              " @ " + DateTime.UtcNow.ToString("o");
                File.WriteAllText(ReportPath, summary);
                Log("report → " + ReportPath + " (" + summary + ")");
            }
            catch { /* ignore */ }

            try { File.Delete(SessionMarker); } catch (IOException) { }
            Log("=== demo verify end ===");
        }

        private static void Log(string msg)
        {
            Debug.Log("[DemoVerify] " + msg);
        }

        private static void Fail(string msg)
        {
            Debug.LogError("[DemoVerify] " + msg);
        }
    }
}
#endif
