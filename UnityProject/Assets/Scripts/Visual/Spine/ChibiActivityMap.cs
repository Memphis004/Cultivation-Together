using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// §6.3 — data-driven ChibiActivity → Spine animation name lookup, loaded from
    /// Resources/Data/chibi_activity_map.json (same JsonUtility+Resources.Load pattern
    /// as ChibiFrameBank/chibi_anim.json — L4: state names live in DATA, not code).
    ///
    /// Unknown activity → warn ONCE per (visual, activity) and fall back to "Idle"
    /// (L9 pattern, mirrors SpriteChibiVisual.SetActivity). Never throws.
    ///
    /// Lives in the Spine assembly (Visual.Spine) — animation names are a Spine-only
    /// concern; the Sprite backend reads chibi_anim.json instead.
    /// </summary>
    [Serializable]
    public class ChibiActivityMap
    {
        /// <summary>Animation name used when an activity has no mapping (L9).</summary>
        public const string FallbackState = "Idle";

        [Serializable]
        public class Entry
        {
            public string activity;          // e.g. "Idle", "Working" (ChibiActivity names)
            public string animation;         // Spine animation name in the rig
        }

        [Serializable]
        private class EntryList
        {
            public Entry[] entries = new Entry[0];
        }

        private readonly Dictionary<string, string> _map = new Dictionary<string, string>(8, StringComparer.Ordinal);
        private readonly string _resourcePath;

        // (visualId, activity) pairs already warned — warn once per visual per activity (L9)
        private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        public ChibiActivityMap(string resourcePath = "Data/chibi_activity_map")
        {
            _resourcePath = resourcePath;
            Load();
        }

        private void Load()
        {
            var text = Resources.Load<TextAsset>(_resourcePath);
            if (text == null)
            {
                // No map file yet — every lookup falls back to Idle with one warning.
                Debug.LogWarning("[ChibiActivityMap] Resources/" + _resourcePath +
                                 " not found — all activities will fall back to '" + FallbackState + "'");
                return;
            }

            var list = JsonUtility.FromJson<EntryList>(text.text);
            if (list == null || list.entries == null)
            {
                Debug.LogWarning("[ChibiActivityMap] " + _resourcePath + " parsed to nothing — falling back to Idle");
                return;
            }

            for (int i = 0; i < list.entries.Length; i++)
            {
                var e = list.entries[i];
                if (e == null || string.IsNullOrEmpty(e.activity) || string.IsNullOrEmpty(e.animation)) continue;
                _map[e.activity] = e.animation;
            }

            var names = new string[_map.Count];
            _map.Keys.CopyTo(names, 0);
            Debug.Log("[ChibiActivityMap] loaded " + _map.Count + " activity mappings: " + string.Join(", ", names));
        }

        /// <summary>
        /// Resolve an activity to a rig animation name. Unknown activity → warn once per
        /// (visualId, activity) and return FallbackState. Known activity whose animation
        /// is missing from the SkeletonData is handled by the caller (SpineChibiVisual),
        /// mirroring how SpriteChibiVisual handles missing state rows.
        /// </summary>
        public string ResolveAnimation(string visualId, string activity)
        {
            if (string.IsNullOrEmpty(activity)) return FallbackState;

            string anim;
            if (_map.TryGetValue(activity, out anim)) return anim;

            if (_warned.Add(visualId + "|" + activity))
            {
                Debug.LogWarning("[ChibiActivityMap] activity '" + activity + "' has no mapping for visual '" +
                                 visualId + "' — falling back to '" + FallbackState + "' (warn once, L9)");
            }
            return FallbackState;
        }
    }
}
