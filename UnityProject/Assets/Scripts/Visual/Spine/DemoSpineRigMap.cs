using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// DEV-ONLY (Visual Demo Scene — LLMWiki/wiki/sources/visual-demo-scene.md):
    /// maps OUR avatar slots / ChibiActivity names onto the FREE Spine example rig
    /// (mix-and-match-pro) so the demo can exercise the production Spine pipeline
    /// without polluting production data — example skin names NEVER enter
    /// avatar_parts.json (C2 of the demo spec).
    ///
    /// Consulted ONLY when <see cref="VisualRuntimeConfig.DevSpineOverride"/> is true
    /// (the demo scene's enabler component); ship builds never load it. Loader mirrors
    /// ChibiActivityMap (JsonUtility + Resources.Load, L4 data-driven, never throws).
    /// Lives in the Visual.Spine assembly — example-rig names are a Spine-only concern.
    /// </summary>
    [Serializable]
    public class DemoSpineRigMap
    {
        [Serializable]
        public class SlotSkin
        {
            public string slot;   // our AvatarSlots name, e.g. "hair"
            public string skin;   // example-rig skin name, e.g. "hair/blue"
        }

        [Serializable]
        public class PartSkin
        {
            public string part;   // our partId, e.g. "hair_short"
            public string skin;   // example-rig skin name — wins over the slot-level mapping
        }

        [Serializable]
        public class ActivityAnim
        {
            public string activity;   // ChibiActivity, e.g. "Walking"
            public string animation;  // animation in the EXAMPLE rig, e.g. "walk"
        }

        [Serializable]
        private class Root
        {
            public string skeletonDataAssetPath = string.Empty;     // editor path (AssetDatabase)
            public string skeletonDataResourcePath = string.Empty;  // Resources fallback
            public SlotSkin[] slotToSkin = new SlotSkin[0];
            public PartSkin[] partToSkin = new PartSkin[0];
            public ActivityAnim[] activityToAnimation = new ActivityAnim[0];
            public string intentionalMissingAnimation = string.Empty; // demo fallback test target
        }

        private Root _root = new Root(); // non-readonly: lazy-loaded from Resources (dev-only)
        private readonly Dictionary<string, string> _slotToSkin =
            new Dictionary<string, string>(8, StringComparer.Ordinal);
        private readonly Dictionary<string, string> _partToSkin =
            new Dictionary<string, string>(8, StringComparer.Ordinal);
        private readonly Dictionary<string, string> _activityToAnimation =
            new Dictionary<string, string>(8, StringComparer.Ordinal);
        private readonly string _resourcePath;
        private bool _loaded;

        public DemoSpineRigMap(string resourcePath = "Data/demo_spine_rig_map")
        {
            _resourcePath = resourcePath;
        }

        /// <summary>Lazy-load once — safe to construct early, disk touched on first use (dev-only path).</summary>
        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            var text = Resources.Load<TextAsset>(_resourcePath);
            if (text == null)
            {
                Debug.LogWarning("[DemoSpineRigMap] Resources/" + _resourcePath +
                                 " not found — demo rig mapping unavailable (skins/animations fall through to resolver defaults)");
                return;
            }

            var root = JsonUtility.FromJson<Root>(text.text);
            if (root == null)
            {
                Debug.LogWarning("[DemoSpineRigMap] " + _resourcePath + " parsed to nothing — demo mapping unavailable");
                return;
            }

            _root = root;
            for (int i = 0; i < root.slotToSkin.Length; i++)
            {
                var e = root.slotToSkin[i];
                if (e == null || string.IsNullOrEmpty(e.slot) || string.IsNullOrEmpty(e.skin)) continue;
                _slotToSkin[e.slot] = e.skin;
            }
            for (int i = 0; i < root.partToSkin.Length; i++)
            {
                var e = root.partToSkin[i];
                if (e == null || string.IsNullOrEmpty(e.part) || string.IsNullOrEmpty(e.skin)) continue;
                _partToSkin[e.part] = e.skin;
            }
            for (int i = 0; i < root.activityToAnimation.Length; i++)
            {
                var e = root.activityToAnimation[i];
                if (e == null || string.IsNullOrEmpty(e.activity) || string.IsNullOrEmpty(e.animation)) continue;
                _activityToAnimation[e.activity] = e.animation;
            }

            Debug.Log("[DemoSpineRigMap] loaded " + _slotToSkin.Count + " slot→skin + " +
                      _activityToAnimation.Count + " activity→animation mappings (DEV-ONLY, example rig '" +
                      _root.skeletonDataAssetPath + "')");
        }

        public string SkeletonDataAssetPath
        {
            get { EnsureLoaded(); return _root.skeletonDataAssetPath; }
        }

        public string SkeletonDataResourcePath
        {
            get { EnsureLoaded(); return _root.skeletonDataResourcePath; }
        }

        /// <summary>Example-rig skin for one of our slots — empty string when unmapped (caller falls through).</summary>
        public string ResolveSkinForSlot(string slot)
        {
            if (string.IsNullOrEmpty(slot)) return string.Empty;
            EnsureLoaded();
            string skin;
            return _slotToSkin.TryGetValue(slot, out skin) ? skin : string.Empty;
        }

        /// <summary>Example-rig skin for a specific partId — empty when unmapped (caller falls back to slot-level).</summary>
        public string ResolveSkinForPart(string partId)
        {
            if (string.IsNullOrEmpty(partId)) return string.Empty;
            EnsureLoaded();
            string skin;
            return _partToSkin.TryGetValue(partId, out skin) ? skin : string.Empty;
        }

        /// <summary>
        /// Example-rig animation for a ChibiActivity — empty string when unmapped
        /// (caller falls through to ChibiActivityMap / the "Idle" fallback, L9).
        /// </summary>
        public string ResolveAnimationForActivity(string activity)
        {
            if (string.IsNullOrEmpty(activity)) return string.Empty;
            EnsureLoaded();
            string anim;
            return _activityToAnimation.TryGetValue(activity, out anim) ? anim : string.Empty;
        }

        /// <summary>Animation name that is deliberately absent from the rig — demo fallback test target.</summary>
        public string IntentionalMissingAnimation
        {
            get { EnsureLoaded(); return _root.intentionalMissingAnimation; }
        }
    }
}
