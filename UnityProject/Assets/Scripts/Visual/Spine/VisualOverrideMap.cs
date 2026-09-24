using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xianxia.Sect.Visual
{ // NOTE: spine atlases under Resources/Avatar/Spine ship STRAIGHT-alpha PNGs (un-premultiplied) — keep materials on _STRAIGHT_ALPHA_INPUT=1.
    /// <summary>
    /// Q5 (spec §6.3 tail — "ตัวละครเนื้อเรื่อง (rig เฉพาะ)"): maps discipleId →
    /// SkeletonDataAsset Resources path for PER-CHARACTER Spine rigs that bypass the
    /// shared chibi_base mix-and-match path entirely. The real rigs imported under
    /// Resources/Avatar/Spine (male 1113103_1, female 1123102_1) are assembled
    /// single-piece characters — their atlases carry only fixed anatomy regions
    /// (tou/shenti/houfa/zuobi/...), NOT selectable per-slot skins — so they can never
    /// mix parts from AvatarPartPool and MUST take this override path.
    ///
    /// PRODUCTION (not DEV-ONLY — unlike DemoSpineRigMap): the JSON table is shipped
    /// data, loaded with the same JsonUtility + Resources.Load pattern (L4, never
    /// throws). Consulted by VisualSpineBootstrap ONLY after the S4 license gate
    /// passes — this loader creates no hooks by itself and never sets
    /// SpineActivationRequested / SpineEnabled (no hidden bypass of the gate).
    /// Lives in the Visual.Spine assembly — skeleton asset paths are a Spine-only
    /// concern; Core consults it exclusively through the bootstrap-assigned probe.
    /// </summary>
    [Serializable]
    public class VisualOverrideMap
    {
        [Serializable]
        public class Entry
        {
            public string discipleId;                 // our DiscipleState.DiscipleId, e.g. "d000"
            public string skeletonDataResourcePath;   // Resources path of the SkeletonDataAsset, e.g. "Avatar/Spine/male/1113103_1"
            public ActivityAnim[] activityToAnimation = new ActivityAnim[0]; // per-RIG ChibiActivity→animation names
        }

        [Serializable]
        public class ActivityAnim
        {
            public string activity;   // ChibiActivity name, e.g. "Idle", "Walking", "Running"
            public string animation;  // animation name in THIS rig, e.g. "idle1", "walk", "run"
        }

        [Serializable]
        private class Root
        {
            public Entry[] overrides = new Entry[0];
        }

        private Root _root = new Root(); // non-readonly: lazy-loaded from Resources
        private readonly Dictionary<string, string> _map =
            new Dictionary<string, string>(8, StringComparer.Ordinal);
        // per-rig activity→animation, keyed "discipleId|activity" (real rigs name
        // animations their own way — lowercase idle1/walk/run — so the mapping is a
        // per-rig concern living in THIS table, not in the shared chibi_activity_map)
        private readonly Dictionary<string, string> _activityMap =
            new Dictionary<string, string>(16, StringComparer.Ordinal);
        private readonly string _resourcePath;
        private bool _loaded;

        public VisualOverrideMap(string resourcePath = "Data/visual_overrides")
        {
            _resourcePath = resourcePath;
        }

        /// <summary>Lazy-load once — safe to construct early, disk touched on first use.</summary>
        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            var text = Resources.Load<TextAsset>(_resourcePath);
            if (text == null)
            {
                Debug.LogWarning("[VisualOverrideMap] Resources/" + _resourcePath +
                                 " not found — no story-character skeleton overrides (every disciple takes the §7 allocation path)");
                return;
            }

            var root = JsonUtility.FromJson<Root>(text.text);
            if (root == null || root.overrides == null)
            {
                Debug.LogWarning("[VisualOverrideMap] " + _resourcePath +
                                 " parsed to nothing — no skeleton overrides");
                return;
            }

            _root = root;
            for (int i = 0; i < root.overrides.Length; i++)
            {
                var e = root.overrides[i];
                if (e == null || string.IsNullOrEmpty(e.discipleId) ||
                    string.IsNullOrEmpty(e.skeletonDataResourcePath)) continue;
                _map[e.discipleId] = e.skeletonDataResourcePath;

                if (e.activityToAnimation == null) continue;
                for (int a = 0; a < e.activityToAnimation.Length; a++)
                {
                    var m = e.activityToAnimation[a];
                    if (m == null || string.IsNullOrEmpty(m.activity) ||
                        string.IsNullOrEmpty(m.animation)) continue;
                    _activityMap[e.discipleId + "|" + m.activity] = m.animation;
                }
            }

            Debug.Log("[VisualOverrideMap] loaded " + _map.Count +
                      " story-character skeleton overrides (Q5 — per-character rigs, no part mixing) + " +
                      _activityMap.Count + " per-rig activity mappings");
        }

        /// <summary>
        /// SkeletonDataAsset Resources path for a disciple — empty string when the
        /// disciple has NO override (caller falls through to the §7 allocation path).
        /// </summary>
        public string ResolveOverride(string discipleId)
        {
            if (string.IsNullOrEmpty(discipleId)) return string.Empty;
            EnsureLoaded();
            string path;
            return _map.TryGetValue(discipleId, out path) ? path : string.Empty;
        }

        /// <summary>True when the disciple has a skeleton override (probe seam for Core).</summary>
        public bool HasOverride(string discipleId)
        {
            return !string.IsNullOrEmpty(ResolveOverride(discipleId));
        }

        /// <summary>
        /// Per-rig animation for a ChibiActivity — empty string when the disciple or
        /// the activity is unmapped (caller falls through to ChibiActivityMap / the
        /// "Idle" fallback, L9). Consulted by SpineChibiVisual BEFORE the shared map.
        /// </summary>
        public string ResolveAnimationForActivity(string discipleId, string activity)
        {
            if (string.IsNullOrEmpty(discipleId) || string.IsNullOrEmpty(activity)) return string.Empty;
            EnsureLoaded();
            string anim;
            return _activityMap.TryGetValue(discipleId + "|" + activity, out anim) ? anim : string.Empty;
        }

        /// <summary>True when the table resolved at least one override entry.</summary>
        public bool HasAny()
        {
            EnsureLoaded();
            return _map.Count > 0;
        }

        /// <summary>Number of resolved override entries (diagnostics/logging).</summary>
        public int Count
        {
            get { EnsureLoaded(); return _map.Count; }
        }
    }
}
