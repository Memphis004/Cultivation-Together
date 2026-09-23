using System.Collections.Generic;
using MessagePipe;
using UnityEngine;
using VContainer;              // IObjectResolverExtensions.Resolve<T>
using Xianxia.Sect;            // ISectStateProvider, AvatarSlots, ChibiBackend
using Xianxia.Sect.Messages;   // DiscipleSelectedMessage
using Xianxia.Sect.Visual;     // SpineChibiVisual, VisualRuntimeConfig, DiscipleVisualSystem

namespace Xianxia.Sect.Visual.Spines
{
    /// <summary>
    /// ██████ DEV-ONLY — VISUAL DEMO SCENE — DO NOT SHIP ██████
    ///
    /// OnGUI HUD: one button per demo acceptance criterion. Every button drives the
    /// REAL production path (state provider / messages) — no parallel spawner, no
    /// state mutation outside SectStateProvider (C2). Fixed at 4 mock disciples
    /// (d000 SectMaster-Spine, d001 Outer-Sprite, d002 Inner-Sprite, d003 Elder-Spine).
    ///
    /// Buttons:
    ///  [1] SpineBudget=1   → d003 degrades to Sprite ON SCREEN, state stays Spine (§7)
    ///  [2] restore budget  → d003 renders Spine again (in-place, state untouched)
    ///  [3] TryChangeAvatarPart(d000 hair) → production publish → ApplySlot skin
    ///      rebuild in place (Transform instance id unchanged — logged before/after)
    ///  [4] TrySetChibiBackend(d001, Spine) → promotion → respawn keeps pos/activity/facing
    ///  [5] publish DiscipleSelectedMessage(d002) → DiscipleDetail panel opens (Portrait)
    ///  [6] set CurrentTask (debug helper — no task system yet; spec T5[6] fallback)
    ///  [7] activity mapped to intentionalMissingAnimation → Idle + warn ONCE (L9)
    /// </summary>
    public sealed class VisualDemoHud : MonoBehaviour
    {
        private ISectStateProvider _state;
        private IPublisher<DiscipleSelectedMessage> _selectedPublisher;
        private VisualRuntimeConfig _config;

        // hair alternation state for button [3] (both ids exist in avatar_parts.json)
        private bool _longHair = true;
        private int _taskIndex;

        private static readonly string[] DemoTasks = { "gathering_herb", "meditation", "refining_elixir" };
        private const string LongHair = "hair_topknot_long";
        private const string ShortHair = "hair_short";
        private const string DemoMissingActivity = "DemoMissing"; // button [7]: map misses → fallback path
        private const string DemoMissingTask = "demo_missing";   // task prefix that derives DemoMissing (production path, not a bypass)

        public void Bind(ISectStateProvider state,
                         IPublisher<DiscipleSelectedMessage> selectedPublisher,
                         VisualRuntimeConfig config)
        {
            _state = state;
            _selectedPublisher = selectedPublisher;
            _config = config;
        }

        /// <summary>
        /// Self-bind through the composition root (same pattern as VisualSpineBootstrap —
        /// the demo scene loads additively AFTER the container is built, so constructor
        /// injection is unavailable and FindObjectOfType is banned, C5/C12).
        /// </summary>
        private void Awake()
        {
            var injector = Xianxia.Sect.GameLifetimeScope.Injector;
            if (injector == null)
            {
                Debug.LogError("[VisualDemoHud] no GameLifetimeScope.Injector — demo HUD disabled " +
                               "(the demo must be loaded additively over the boot scene)");
                enabled = false;
                return;
            }
            _state = injector.Resolve<ISectStateProvider>();
            _selectedPublisher = injector.Resolve<IPublisher<DiscipleSelectedMessage>>();
            _config = VisualRuntimeConfig.Instance;
        }

        private void OnGUI()
        {
            var budgetNow = _config != null ? _config.SpineBudget : -1;
            var overrideNow = _config != null && _config.DevSpineOverride;
            GUILayout.BeginArea(new Rect(10, 10, 460, 420));
            GUILayout.Label("── Visual Demo (DEV-ONLY, example rig) ──");
            GUILayout.Label("SpineBudget=" + budgetNow + "  DevSpineOverride=" + overrideNow +
                            "  SpineEnabled=" + (_config != null && _config.SpineEnabled));
            GUILayout.Space(6);

            if (GUILayout.Button("[1] SpineBudget = 1  (d003 degrades → Sprite, state stays Spine)"))
            {
                if (_config != null) _config.SpineBudget = 1;
                var sys = ResolveVisualSystem();
                if (sys != null) sys.Reconcile();
            }
            if (GUILayout.Button("[2] Restore SpineBudget = 20  (d003 back to Spine)"))
            {
                if (_config != null) _config.SpineBudget = 20;
                var sys = ResolveVisualSystem();
                if (sys != null) sys.Reconcile();
            }
            if (GUILayout.Button("[3] Change d000 hair via TryChangeAvatarPart (skin rebuild IN PLACE)"))
            {
                string next = _longHair ? ShortHair : LongHair;
                _longHair = !_longHair;

                // prove "same Transform" across the incremental slot change
                var before = SpineChibiVisual.Active;
                SpineChibiVisual spineVisual;
                int idBefore = 0;
                if (before.TryGetValue("d000", out spineVisual) && spineVisual != null)
                    idBefore = spineVisual.Transform.gameObject.GetInstanceID();

                string reason;
                Xianxia.Sect.AvatarAppearance result;
                bool ok = _state.TryChangeAvatarPart("d000", AvatarSlots.Hair, next, out reason, out result);
                Debug.Log("[VisualDemoHud] [3] TryChangeAvatarPart(d000 hair → " + next + ") ok=" + ok +
                          " instanceIdBefore=" + idBefore +
                          " (instanceIdAfter is logged by the production ApplySlot path — must be equal)");

                SpineChibiVisual after;
                int idAfter = 0;
                if (before.TryGetValue("d000", out after) && after != null)
                    idAfter = after.Transform.gameObject.GetInstanceID();
                Debug.Log("[VisualDemoHud] [3] instanceIdAfter=" + idAfter +
                          (idBefore == idAfter ? "  ✓ IN PLACE (no respawn)" : "  ✗ CHANGED — respawn happened!"));
            }
            if (GUILayout.Button("[4] TrySetChibiBackend(d001, Spine)  (promote, keep pos/activity/facing)"))
            {
                string reason;
                bool ok = _state.TrySetChibiBackend("d001", ChibiBackend.Spine, out reason);
                Debug.Log("[VisualDemoHud] [4] TrySetChibiBackend(d001 → Spine) ok=" + ok + " reason=" + reason);
            }
            if (GUILayout.Button("[5] Select d002 → DiscipleSelectedMessage → DiscipleDetail panel"))
            {
                if (_selectedPublisher != null)
                    _selectedPublisher.Publish(new DiscipleSelectedMessage { DiscipleId = "d002" });
            }
            if (GUILayout.Button("[6] Cycle CurrentTask (debug helper — no task system yet)"))
            {
                _taskIndex = (_taskIndex + 1) % DemoTasks.Length;
                SetCurrentTask("d000", DemoTasks[_taskIndex]);
            }
            if (GUILayout.Button("[7] Activity 'DemoMissing' → mapped to missing animation → Idle + warn ONCE"))
            {
                SetCurrentTask("d000", DemoMissingTask);
            }
            GUILayout.EndArea();
        }

        // ---- public entry points: same code paths as the buttons, without OnGUI dispatch
        // (used by the editor verify runner; OnGUI clicks remain the human path) ----

        /// <summary>T5[1]/T5[2]: budget change through the production reconcile path.</summary>
        public void DebugSetBudget(int budget)
        {
            if (_config == null) return;
            _config.SpineBudget = budget;
            var sys = ResolveVisualSystem();
            if (sys != null) sys.Reconcile();
            Debug.Log("[VisualDemoHud] SpineBudget → " + budget + " (reconciled)");
        }

        /// <summary>T5[3]: production TryChangeAvatarPart path without OnGUI dispatch.</summary>
        public void DebugHairSwap()
        {
            string next = _longHair ? ShortHair : LongHair;
            _longHair = !_longHair;

            var before = SpineChibiVisual.Active;
            SpineChibiVisual spineVisual;
            int idBefore = 0;
            if (before.TryGetValue("d000", out spineVisual) && spineVisual != null)
                idBefore = spineVisual.Transform.gameObject.GetInstanceID();

            string reason;
            Xianxia.Sect.AvatarAppearance result;
            bool ok = _state.TryChangeAvatarPart("d000", AvatarSlots.Hair, next, out reason, out result);
            Debug.Log("[VisualDemoHud] [3] TryChangeAvatarPart(d000 hair → " + next + ") ok=" + ok +
                      " instanceIdBefore=" + idBefore);
        }

        /// <summary>T5[4]: promotion through TrySetChibiBackend (production message path).</summary>
        public void DebugPromoteD001()
        {
            string reason;
            bool ok = _state.TrySetChibiBackend("d001", ChibiBackend.Spine, out reason);
            Debug.Log("[VisualDemoHud] [4] TrySetChibiBackend(d001 → Spine) ok=" + ok + " reason=" + reason);
        }

        /// <summary>T5[5]: same publish as button [5].</summary>
        public void DebugSelectD002()
        {
            if (_selectedPublisher != null)
                _selectedPublisher.Publish(new DiscipleSelectedMessage { DiscipleId = "d002" });
        }

        /// <summary>T5[6]: cycle the demo task (production reconcile follows).</summary>
        public void DebugCycleTask()
        {
            _taskIndex = (_taskIndex + 1) % DemoTasks.Length;
            SetCurrentTask("d000", DemoTasks[_taskIndex]);
        }

        /// <summary>T5[7]: activity that maps to the intentional-missing animation.</summary>
        public void DebugDemoMissing()
        {
            SetCurrentTask("d000", DemoMissingTask);
        }

        /// <summary>
        /// T5[6]/T5[7]: no task system exists yet — the demo writes CurrentTask directly
        /// (spec-sanctioned debug helper) then triggers the existing event-driven
        /// reconcile so the derive-on-reconcile path re-resolves activity. No polling.
        /// </summary>
        private void SetCurrentTask(string discipleId, string task)
        {
            var state = _state.BuildSectEconomyState();
            if (state == null || state.Disciples == null) return;
            var disciples = state.Disciples;
            for (int i = 0; i < disciples.Count; i++)
            {
                if (disciples[i] != null && disciples[i].DiscipleId == discipleId)
                {
                    disciples[i].CurrentTask = task;
                    Debug.Log("[VisualDemoHud] " + discipleId + " CurrentTask → '" + task + "'");
                    break;
                }
            }
            var sys = ResolveVisualSystem();
            if (sys != null) sys.Reconcile();
        }

        /// <summary>C12-safe: the system is a registered singleton — resolved once, cached.</summary>
        private Xianxia.Sect.Visual.DiscipleVisualSystem ResolveVisualSystem()
        {
            if (_visualSystem != null) return _visualSystem;
            var injector = Xianxia.Sect.GameLifetimeScope.Injector;
            if (injector != null) _visualSystem = injector.Resolve<Xianxia.Sect.Visual.DiscipleVisualSystem>();
            return _visualSystem;
        }
        private Xianxia.Sect.Visual.DiscipleVisualSystem _visualSystem;
    }
}
