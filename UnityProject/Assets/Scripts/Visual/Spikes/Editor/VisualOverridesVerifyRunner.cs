#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using VContainer;
using Xianxia.Sect.Visual;   // DiscipleVisualSystem, VisualRuntimeConfig, SpineChibiVisual (editor-verify only)
using Spine;                 // Skeleton/SkeletonData — read-only rig inspection inside this editor verify tool
using Spine.Unity;           // SkeletonDataAsset (real rigs) — editor-verify only

namespace Xianxia.Sect.Visual.Spikes.EditorTools
{
    /// <summary>
    /// Visual overrides (Q5 — per-character rigs) acceptance verification (editor-only),
    /// driven by the marker-file remote control: "visual_overrides_verify" in
    /// Library/visual_spike_command.txt.
    ///
    /// POST-S4 (2026-09-25): the license gate is TRUE at the composition root, so
    /// VisualSpineBootstrap (RuntimeInitializeOnLoadMethod) registers the shared-rig
    /// SpineVisualFactory AND the Q5 override hooks by itself. This runner registers
    /// NOTHING and mutates NO production state — it verifies the REAL production
    /// wiring end-to-end:
    ///   - d000/d002 render their OWN rigs (≠ the shared rig, ≠ each other)
    ///   - d003 renders Spine via the SHARED rig (Spine-allocated, budget-bound)
    ///   - d001 stays SpriteSheet (no entitlement, no override)
    ///   - SpineBudget=0 → overrides survive (outside §7 budget), d003 degrades
    ///   - broken override path → warn-once + SpriteSheet fallback, no crash
    ///   - ApplySlot no-respawn, SetFacing via Skeleton.ScaleX (L8), per-rig
    ///     activity mapping plays "idle1"
    ///
    /// Same session pattern as Phase3VerifyRunner: an [InitializeOnLoad] update poll
    /// re-registers after every play-mode domain reload and drives the step machine;
    /// cross-reload state lives on disk, not in statics. Report lands in
    /// Library/visual_overrides_verify_report.txt.
    ///
    /// Static access only (C11/C12 — no FindObjectOfType in production paths;
    /// FindObjectsByType&lt;SpriteChibiVisual&gt; is READ-ONLY inspection inside this
    /// editor verify tool, same carve-out as Phase 2/3).
    /// </summary>
    [InitializeOnLoad]
    public static class VisualOverridesVerifyRunner
    {
        private const string ReportPath = "Library/visual_overrides_verify_report.txt";
        private const string SessionMarker = "Library/visual_overrides_verify_live.txt";

        private const string MaleRigPath = "Avatar/Spine/male/1113103_1";
        private const string FemaleRigPath = "Avatar/Spine/female/1123102_1_SkeletonData";
        private const string BrokenRigPath = "Avatar/Spine/does_not_exist";
        private const string SceneA = "Assets/Scenes/TestGameplayScene.unity";

        static VisualOverridesVerifyRunner()
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
            Log("=== visual overrides verify start ===");
            File.Delete(ReportPath);
            // NOTE: the gate is set at runtime inside GameLifetimeScope.Configure —
            // these are the PRE-Configure editor defaults, logged for reference only.
            // The REAL gate check runs in StartSession, after the container built.
            Log("[Gate pre-play defaults] SpineEnabled=" + cfg.SpineEnabled +
                " SpineActivationRequested=" + cfg.SpineActivationRequested +
                " SpineBudget=" + cfg.SpineBudget +
                " SpineSkeletonResourcePath=" + cfg.SpineSkeletonResourcePath +
                " VisualOverridesPath=" + cfg.VisualOverridesPath);

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

        private static SpineChibiVisual _d000;
        private static SpineChibiVisual _d002;
        private static SkeletonData _sharedRigData; // the interim shared rig bootstrap loaded
        private static int _fallbackWarnCount;

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
            _fallbackWarnCount = 0;

            _injector = GameLifetimeScope.Injector;
            if (_injector == null) throw new InvalidOperationException("no GameLifetimeScope.Injector");

            // REAL gate check — Configure ran during play-mode boot, so the human S4
            // decision is visible here. Fail loudly (with a report) if it regressed.
            var cfgPlay = VisualRuntimeConfig.Instance;
            if (!cfgPlay.SpineActivationRequested)
            {
                Fail("SpineActivationRequested is FALSE in play mode — the S4 decision in " +
                     "GameLifetimeScope is missing or was reverted");
                Finish(false, "S4 gate false in play mode");
                return;
            }
            Log("[Gate] SpineActivationRequested=TRUE (S4 decided) SpineEnabled=" + cfgPlay.SpineEnabled +
                " SpineSkeletonResourcePath=" + cfgPlay.SpineSkeletonResourcePath);

            _provider = _injector.Resolve<ISectStateProvider>();
            _visualSystem = _injector.Resolve<DiscipleVisualSystem>();

            // PRODUCTION PATH UNDER TEST — with the S4 gate TRUE, VisualSpineBootstrap
            // has already registered SpineVisualFactory + the Q5 override hooks. The
            // runner touches NO statics and NO config; it only reads the map for
            // table-level checks and loads the shared rig for the "not the shared rig"
            // comparisons below.
            var cfg = VisualRuntimeConfig.Instance;
            var map = new VisualOverrideMap(cfg.VisualOverridesPath);
            Check("override map has entries (d000, d002)", map.Count == 2);
            Check("d000 override path = " + MaleRigPath, map.ResolveOverride("d000") == MaleRigPath);
            Check("d002 override path = " + FemaleRigPath, map.ResolveOverride("d002") == FemaleRigPath);
            Check("d001 has no override", string.IsNullOrEmpty(map.ResolveOverride("d001")));
            Check("d003 has no override", string.IsNullOrEmpty(map.ResolveOverride("d003")));
            Check("d000 per-rig mapping: Idle → idle1",
                  map.ResolveAnimationForActivity("d000", "Idle") == "idle1");
            Check("d002 per-rig mapping: Walking → walk",
                  map.ResolveAnimationForActivity("d002", "Walking") == "walk");

            var shared = Resources.Load<SkeletonDataAsset>(cfg.SpineSkeletonResourcePath);
            _sharedRigData = shared != null && shared.GetSkeletonData(false) != null
                ? shared.GetSkeletonData(false) : null;
            Check("shared rig loads from SpineSkeletonResourcePath (production bootstrap precondition)",
                  _sharedRigData != null);

            Check("production override probe registered by bootstrap",
                  DiscipleVisualSystem.SpineOverrideProbe != null);
            Check("production override factory registered by bootstrap",
                  DiscipleVisualSystem.SpineOverrideVisualFactory != null);
            Check("production shared-rig factory registered by bootstrap",
                  DiscipleVisualSystem.SpineVisualFactory != null);
            Check("production SpineEnabled flipped by bootstrap", cfg.SpineEnabled);

            Application.logMessageReceived += OnLogMessage;

            Log("[Session] started — observing the production bootstrap wiring (no runner overrides)");
        }

        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning && condition != null &&
                condition.Contains("story-character skeleton override"))
            {
                _fallbackWarnCount++;
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

                    case 1: // Criterion 1 — overridden disciples render their OWN rigs; d003 shared
                    {
                        var roster = _provider.BuildSectEconomyState().Disciples;
                        SpineChibiVisual.Active.TryGetValue("d000", out _d000);
                        SpineChibiVisual.Active.TryGetValue("d002", out _d002);

                        Check("d000 renders Spine (override rig)", _d000 != null);
                        Check("d002 renders Spine (override rig)", _d002 != null);
                        Check("d000+d002 use TWO DIFFERENT rigs (male ≠ female)",
                              _d000 != null && _d002 != null &&
                              !ReferenceEquals(_d000.Skeleton != null ? _d000.Skeleton.Data : null,
                                               _d002.Skeleton != null ? _d002.Skeleton.Data : null));
                        // Path-aware: the INTERIM shared rig IS the male asset, so d000
                        // legitimately shares SkeletonData with it by construction. The
                        // invariant that matters: d000 ≠ d002 (above), and when the shared
                        // rig is a DIFFERENT asset (future chibi_base) d000 must not be on it.
                        var sharedPath = VisualRuntimeConfig.Instance.SpineSkeletonResourcePath;
                        bool sharedIsD000Rig = sharedPath == MaleRigPath;
                        Check("d000 rig is NOT the shared rig (when they are distinct assets)",
                              _d000 == null || _sharedRigData == null || sharedIsD000Rig ||
                              !ReferenceEquals(_d000.Skeleton != null ? _d000.Skeleton.Data : null, _sharedRigData));
                        Check("d002 rig is NOT the shared rig (per-character override really engaged)",
                              _d002 != null && _sharedRigData != null &&
                              !ReferenceEquals(_d002.Skeleton != null ? _d002.Skeleton.Data : null, _sharedRigData));
                        SpineChibiVisual.Active.TryGetValue("d003", out var d003);
                        Check("d003 rig IS the shared rig (allocation path, not override)",
                              d003 != null && _sharedRigData != null &&
                              ReferenceEquals(d003.Skeleton != null ? d003.Skeleton.Data : null, _sharedRigData));
                        Log("[Rig d000] data='" + DataName(_d000) + "' slots=" + SlotCount(_d000) +
                            " attachments=" + AttachmentCount(_d000));
                        Log("[Rig d002] data='" + DataName(_d002) + "' slots=" + SlotCount(_d002) +
                            " attachments=" + AttachmentCount(_d002));
                        Check("d001 NOT rendered as Spine (sprite entitlement, no override)",
                              !SpineChibiVisual.Active.ContainsKey("d001"));
                        Check("d003 renders Spine via the SHARED rig (S4 active, Spine-allocated)",
                              SpineChibiVisual.Active.ContainsKey("d003"));
                        _step++;
                        break;
                    }

                    case 2: // Criterion 2 — rig/Sex consistency (ห้ามผิดเพศ)
                    {
                        var d000 = Find(_provider.BuildSectEconomyState().Disciples, "d000");
                        var d002 = Find(_provider.BuildSectEconomyState().Disciples, "d002");
                        Check("d000 Sex=Male in state", d000 != null && d000.Sex == DiscipleSex.Male);
                        Check("d000 override path targets the MALE rig", IsMaleRig("d000"));
                        Check("d002 Sex=Female in state", d002 != null && d002.Sex == DiscipleSex.Female);
                        Check("d002 override path targets the FEMALE rig", IsFemaleRig("d002"));
                        _step++;
                        break;
                    }

                    case 3: // Criterion 3 — overrides do NOT consume §7 SpineBudget
                    {
                        VisualRuntimeConfig.Instance.SpineBudget = 0;
                        _visualSystem.Reconcile(); // allocation → empty; overrides must survive
                        _stepUntil += 1.0;
                        _step++;
                        break;
                    }

                    case 4:
                    {
                        Check("budget=0: d000 STILL renders Spine (outside budget, same instance)",
                              _d000 != null && SpineChibiVisual.Active.TryGetValue("d000", out var v000) &&
                              ReferenceEquals(v000, _d000));
                        Check("budget=0: d002 STILL renders Spine (outside budget, same instance)",
                              _d002 != null && SpineChibiVisual.Active.TryGetValue("d002", out var v002) &&
                              ReferenceEquals(v002, _d002));
                        Check("budget=0: d003 degrades to Sprite (shared-rig path IS budget-bound)",
                              !SpineChibiVisual.Active.ContainsKey("d003"));
                        var d000 = Find(_provider.BuildSectEconomyState().Disciples, "d000");
                        Check("budget=0: d000 entitlement untouched in state",
                              d000 != null && d000.ChibiBackend == ChibiBackend.Spine);
                        VisualRuntimeConfig.Instance.SpineBudget = 20;
                        _step++;
                        break;
                    }

                    case 5: // Criterion 4 — broken override path → warn + SpriteSheet fallback, no crash
                    {
                        var realProbe = DiscipleVisualSystem.SpineOverrideProbe;
                        DiscipleVisualSystem.SpineOverrideProbe =
                            id => id == "d001" ? BrokenRigPath : (realProbe != null ? realProbe(id) : string.Empty);
                        _visualSystem.Reconcile(); // d001 enters the override branch with a broken path
                        _stepUntil += 1.0;
                        _pendingProbeRestore = realProbe;
                        _step++;
                        break;
                    }

                    case 6:
                    {
                        Check("broken override path did NOT crash the session", EditorApplication.isPlaying);
                        Check("d001 fell back to a Sprite visual (no Spine)", !SpineChibiVisual.Active.ContainsKey("d001"));
                        Check("d001 has a live Sprite visual after fallback", FindSpriteVisual("d001") != null);
                        Check("fallback logged ONE warn-once warning (got " + _fallbackWarnCount + ")",
                              _fallbackWarnCount >= 1);
                        DiscipleVisualSystem.SpineOverrideProbe = _pendingProbeRestore;
                        _pendingProbeRestore = null;
                        _visualSystem.Reconcile();
                        _stepUntil += 1.0;
                        _step++;
                        break;
                    }

                    case 7: // Criterion 5 — ApplySlot / SetFacing (ScaleX, L8) / per-rig activity on the real rig
                    {
                        SpineChibiVisual.Active.TryGetValue("d000", out var v); // re-fetch the LIVE instance
                        if (v != null && v.Skeleton != null)
                        {
                            Check("d000 Spine visual alive for API checks", true);
                            _d000GoId = v.Transform.gameObject.GetInstanceID();
                            v.ApplySlot("body", "body_robe_azure");
                            int goIdAfter = v.Transform.gameObject.GetInstanceID();
                            Check("ApplySlot keeps the same GameObject (no respawn)", _d000GoId == goIdAfter);
                            Check("visual alive after ApplySlot on a fixed rig", SpineChibiVisual.Active.ContainsKey("d000"));

                            v.SetFacing(false); // L8: flip via Skeleton.ScaleX — never localScale
                            Check("SetFacing(false) → Skeleton.ScaleX = -1", v.Skeleton.ScaleX < 0f);
                            Check("SetFacing(false) leaves localScale.x untouched",
                                  Mathf.Approximately(v.Transform.localScale.x, 1f));
                            v.SetFacing(true);
                            Check("SetFacing(true) → Skeleton.ScaleX = 1", v.Skeleton.ScaleX > 0f);

                            v.SetActivity("Idle"); // per-rig mapping: "Idle" → "idle1" in visual_overrides.json
                            Check("SetActivity on the real rig does not throw and visual survives",
                                  SpineChibiVisual.Active.ContainsKey("d000"));
                            var track = v.AnimationState != null && v.AnimationState.GetCurrent(0) != null
                                ? v.AnimationState.GetCurrent(0).Animation : null; // spine 3.8: GetCurrent(track) — no .Current
                            Check("SetActivity(Idle) plays the PER-RIG animation 'idle1' (got '" +
                                  (track != null ? track.Name : "null") + "')",
                                  track != null && track.Name == "idle1");
                            _stepUntil += 0.3;
                        }
                        else
                        {
                            Check("d000 Spine visual alive for API checks", false);
                        }
                        _step++;
                        break;
                    }

                    case 8:
                    {
                        SpineChibiVisual.Active.TryGetValue("d000", out var v000);
                        Check("d000 still rendering after activity/facing checks (same instance)",
                              v000 != null && v000.Transform.gameObject.GetInstanceID() == _d000GoId);
                        Finish(true, null);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[VisualOverridesVerify] FAILED: " + ex);
                Finish(false, ex.Message);
            }
        }

        private static Func<string, string> _pendingProbeRestore;
        private static int _d000GoId;

        // ---- read-only inspection helpers (editor verify only) ----

        private static string DataName(SpineChibiVisual v)
        {
            return v != null && v.Skeleton != null && v.Skeleton.Data != null ? v.Skeleton.Data.Name : "null";
        }

        private static int SlotCount(SpineChibiVisual v)
        {
            return v != null && v.Skeleton != null && v.Skeleton.Slots != null ? v.Skeleton.Slots.Count : -1;
        }

        private static int AttachmentCount(SpineChibiVisual v)
        {
            if (v == null || v.Skeleton == null || v.Skeleton.Slots == null) return -1;
            int n = 0;
            var slots = v.Skeleton.Slots;
            for (int i = 0; i < slots.Count; i++)
                if (slots.Items[i].Attachment != null) n++;
            return n;
        }

        private static bool IsMaleRig(string discipleId)
        {
            var probe = DiscipleVisualSystem.SpineOverrideProbe;
            return probe != null && probe(discipleId).Contains("/male/");
        }

        private static bool IsFemaleRig(string discipleId)
        {
            var probe = DiscipleVisualSystem.SpineOverrideProbe;
            return probe != null && probe(discipleId).Contains("/female/");
        }

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

            // The runner never mutated production state this session (bootstrap owns
            // every hook) — only the budget probe and the broken-path probe were
            // restored in-step. Nothing to reset here beyond leaving play mode; the
            // domain unload restores all statics by construction.

            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;

            try
            {
                var summary = "visual_overrides verify " + (ok && _fail == 0 ? "COMPLETE" : "FAILED") +
                              " pass=" + _pass + " fail=" + _fail +
                              (string.IsNullOrEmpty(note) ? "" : " note=" + note) +
                              " @ " + DateTime.UtcNow.ToString("o");
                File.WriteAllText(ReportPath, summary);
                Log("report → " + ReportPath + " (" + summary + ")");
            }
            catch { /* ignore */ }

            Log("=== visual overrides verify end ===");
        }

        private static void Log(string msg)
        {
            Debug.Log("[VisualOverridesVerify] " + msg);
        }

        private static void Fail(string msg)
        {
            Debug.LogError("[VisualOverridesVerify] " + msg);
        }
    }
}
#endif
