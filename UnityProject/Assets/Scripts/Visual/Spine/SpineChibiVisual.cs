using System;
using System.Collections.Generic;
using Spine;
using Spine.Unity;
using UnityEngine;
using Xianxia.Sect; // AvatarSlots

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// Phase 3 — Spine backend visual (D6/L4: shared rig + skin mix-and-match).
    ///
    /// Shared-rig pattern: ONE SkeletonDataAsset is shared by every instance; each
    /// instance builds its own <see cref="Spine.Skin"/> by mixing the per-part skin
    /// names resolved through <see cref="AppearanceResolver.Resolve"/> with
    /// VisualBackend.Spine. The rig's default skin is mixed in first, so slots the
    /// placeholder rig defines render even before part spineSkins exist.
    ///
    /// API notes (spine-unity 3.8 — phase0-results §3, L10 deviation):
    ///  - Track access is <c>AnimationState.GetCurrent(track)</c> — GetTrack() does not
    ///    exist in 3.8 (used only for diagnostics, never in the hot path).
    ///  - Facing flip is <c>Skeleton.ScaleX</c> — Transform.localScale is never touched
    ///    on a Spine object (L8 — it breaks mesh bounds).
    ///
    /// License gate: only instantiate this after the S4 license decision is confirmed
    /// by a human (phase0-results §4) and a SkeletonDataAsset path is verified —
    /// DiscipleVisualSystem degrades to SpriteSheet otherwise (state untouched).
    /// </summary>
    public sealed class SpineChibiVisual : IChibiVisual
    {
        private const int MainTrack = 0;

        /// <summary>
        /// Live Spine visuals by disciple id — diagnostics/verify/descriptor access.
        /// Populated by Create, removed by Dispose. Small (≤ SpineBudget); keyed by id.
        /// </summary>
        public static readonly Dictionary<string, SpineChibiVisual> Active =
            new Dictionary<string, SpineChibiVisual>(StringComparer.Ordinal);

        private readonly GameObject _go;
        private readonly Transform _goTransform;
        private readonly SkeletonAnimation _skeletonAnimation;
        private readonly AppearanceResolver _resolver;
        private readonly AvatarPartPool _pool;
        private readonly ChibiActivityMap _activityMap;
        private readonly VisualRuntimeConfig _config;
        private readonly DemoSpineRigMap _demoRigMap; // DEV-ONLY: example-rig mapping, null in production
        private readonly VisualOverrideMap _overrideActivityMap; // Q5 per-rig activity→animation, null on shared-rig instances

        // instance skin state — the per-instance mixed skin is rebuilt from scratch on
        // every change (rig source skins are never mutated).
        private Skin _instanceSkin;
        private AvatarAppearance _lastAppearance;
        private bool _hasAppearance;

        // visual state (IChibiVisual contract)
        private string _discipleId = string.Empty;
        private string _currentActivity = ChibiActivityMap.FallbackState;
        private bool _facingRight = true;
        private int _sortingBase;

        // warn-once guards (L9)
        private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);
        private bool _disposed;

        private SpineChibiVisual(
            GameObject go,
            SkeletonAnimation skeletonAnimation,
            AppearanceResolver resolver,
            AvatarPartPool pool,
            ChibiActivityMap activityMap,
            VisualRuntimeConfig config,
            DemoSpineRigMap demoRigMap = null,
            VisualOverrideMap overrideActivityMap = null)
        {
            _go = go;
            _goTransform = go.transform;
            _skeletonAnimation = skeletonAnimation;
            _resolver = resolver;
            _pool = pool;
            _activityMap = activityMap;
            _config = config;
            _demoRigMap = demoRigMap; // null in production — example-rig mapping is dev-only
            _overrideActivityMap = overrideActivityMap; // null unless created through the Q5 override path
        }

        /// <summary>
        /// Create a Spine visual under <paramref name="parent"/> from the shared rig.
        /// Returns null (with one warning) when the skeleton asset is missing or fails
        /// to initialize — callers degrade to SpriteSheet instead of hard-crashing.
        /// </summary>
        public static SpineChibiVisual Create(
            SkeletonDataAsset skeletonDataAsset,
            Transform parent,
            AppearanceResolver resolver,
            AvatarPartPool pool,
            ChibiActivityMap activityMap,
            VisualRuntimeConfig config,
            DemoSpineRigMap demoRigMap = null,
            VisualOverrideMap overrideActivityMap = null)
        {
            if (skeletonDataAsset == null)
            {
                Debug.LogWarning("[SpineChibiVisual] no SkeletonDataAsset — degrading to SpriteSheet");
                return null;
            }

            var go = new GameObject("Chibi_Spine");
            go.transform.SetParent(parent, false);

            // Shared rig: SkeletonAnimation.AddToGameObject uses the asset's shared
            // SkeletonData while each instance owns its own Skeleton/AnimationState.
            var skeletonAnimation = SkeletonAnimation.AddToGameObject(go, skeletonDataAsset);
            if (skeletonAnimation == null)
            {
                Debug.LogWarning("[SpineChibiVisual] SkeletonAnimation.AddToGameObject failed for '" +
                                 skeletonDataAsset.name + "' — degrading to SpriteSheet");
                UnityEngine.Object.Destroy(go);
                return null;
            }

            skeletonAnimation.Initialize(false); // 3.8: explicit init before skeleton access
            if (skeletonAnimation.skeleton == null || skeletonAnimation.AnimationState == null)
            {
                Debug.LogWarning("[SpineChibiVisual] init failed (skeleton/AnimationState null) for '" +
                                 skeletonDataAsset.name + "' — degrading to SpriteSheet");
                UnityEngine.Object.Destroy(go);
                return null;
            }

            var visual = new SpineChibiVisual(go, skeletonAnimation, resolver, pool, activityMap, config, demoRigMap, overrideActivityMap);
            visual.ApplySorting();
            visual.ApplyFacing();
            visual.LogRigContentsOnce(); // L9: log slots/skins/animations once per instance (Marooned lesson)
            // NOTE: do NOT register in Active here — _discipleId is still empty until
            // Bind() runs, and a premature registration leaks a "" key forever (Dispose
            // only removes the real id). Bind registers under the real id.
            return visual;
        }

        // ---- IChibiVisual ----

        public Transform Transform { get { return _goTransform; } }

        public VisualBackend Backend { get { return VisualBackend.Spine; } }

        public string DiscipleId { get { return _discipleId; } }

        public int SortingBase { get { return _sortingBase; } }

        public bool FacingRight { get { return _facingRight; } }

        public string CurrentActivity { get { return _currentActivity; } }

        /// <summary>Skeleton access — diagnostics/verify only; production code never pokes the skeleton directly.</summary>
        public Skeleton Skeleton { get { return _skeletonAnimation != null ? _skeletonAnimation.skeleton : null; } }

        /// <summary>AnimationState access — diagnostics/verify only (e.g. assert the currently playing animation).</summary>
        public Spine.AnimationState AnimationState { get { return _skeletonAnimation != null ? _skeletonAnimation.AnimationState : null; } }

        /// <summary>
        /// L9 (Marooned lesson): log the rig's slots/skins/animations once per
        /// instance at creation. The skins/animations lists are DEV-ONLY diagnostics
        /// for the demo — production paths stay allocation-free and never enumerate them.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")] // dev-diagnostic only — compiled out of builds
        private void LogRigContentsOnce()
        {
            if (_skeletonAnimation == null || _skeletonAnimation.skeleton == null) return;
            var data = _skeletonAnimation.skeleton.Data;
            if (data == null) return;

            var animations = data.Animations;
            var names = new string[animations.Count];
            for (int i = 0; i < animations.Count; i++) names[i] = animations.Items[i].Name;
            Debug.Log("[SpineChibiVisual] rig '" + data.Name + "' animations [" + animations.Count + "]: " +
                      string.Join(", ", names) + " (" + _discipleId + ")");
        }

        public void Bind(string discipleId, AvatarAppearance appearance, DiscipleSex sex)
        {
            _discipleId = discipleId ?? string.Empty;
            Active[_discipleId] = this; // keep registry in sync (id set before skin build uses it)
            RebuildSkin(appearance);
        }

        /// <summary>
        /// Incremental slot update (acceptance criterion): rebuild ONLY that slot's
        /// contribution in the instance skin and refresh via SetSlotsToSetupPose —
        /// the GameObject/SkeletonAnimation/Skeleton are never destroyed (no respawn).
        /// </summary>
        public void ApplySlot(string slot, string partId)
        {
            if (_disposed || !_hasAppearance) return;

            _lastAppearance.Parts[slot] = partId; // choke point stays AvatarAppearance (L1)
            RebuildSkin(_lastAppearance);
       }

        public void SetFacing(bool faceRight)
        {
            _facingRight = faceRight;
            ApplyFacing();
        }

        /// <summary>L8: flip via Skeleton.ScaleX — Transform.localScale is never touched.</summary>
        private void ApplyFacing()
        {
            if (_skeletonAnimation == null || _skeletonAnimation.skeleton == null) return;
            _skeletonAnimation.skeleton.ScaleX = _facingRight ? 1f : -1f;
        }

        public void SetActivity(string activityName)
        {
            if (_disposed || _skeletonAnimation == null ||
                _skeletonAnimation.AnimationState == null ||
                _skeletonAnimation.skeleton == null) return;

            var skeletonData = _skeletonAnimation.skeleton.Data; // spine 3.8: Skeleton.Data (SkeletonData)

            var animName = string.Empty;
            // 1) Q5 per-rig mapping (visual_overrides.json) — real rigs name animations
            //    their own way (idle1/walk/run), so the mapping ships with the rig table.
            //    Production path — consulted BEFORE the shared ChibiActivityMap.
            if (_overrideActivityMap != null)
            {
                animName = _overrideActivityMap.ResolveAnimationForActivity(_discipleId, activityName);
            }
            // 2) DEV-ONLY demo override: while DevSpineOverride is live, the demo rig map
            //    (example-rig animation names) wins over ChibiActivityMap — never true in ship.
            //    (Unchanged for demo instances: they are created without an override map.)
            if (string.IsNullOrEmpty(animName) && _config != null && _config.DevSpineOverride && _demoRigMap != null)
            {
                animName = _demoRigMap.ResolveAnimationForActivity(activityName);
            }
            // 3) shared ChibiActivityMap (chibi_activity_map.json)
            if (string.IsNullOrEmpty(animName))
            {
                animName = _activityMap.ResolveAnimation(_discipleId, activityName);
            }

            if (skeletonData.FindAnimation(animName) == null)
            {
                if (_warned.Add("anim:" + activityName)) // warn once per (visual, activity) — L9
                {
                    Debug.LogWarning("[SpineChibiVisual] animation '" + animName + "' (activity '" + activityName +
                                     "') not in SkeletonData — falling back to '" + ChibiActivityMap.FallbackState +
                                     "' (" + _discipleId + ")");
                }
                animName = ChibiActivityMap.FallbackState;

                // Q5 per-rig mapping for the fallback activity too ("Idle" → "idle1" on
                // the real rigs) — otherwise the literal "Idle" misses and the chain
                // ends with no pose on production rigs.
                if (_overrideActivityMap != null)
                {
                    var rigFallback = _overrideActivityMap.ResolveAnimationForActivity(
                        _discipleId, ChibiActivityMap.FallbackState);
                    if (!string.IsNullOrEmpty(rigFallback)) animName = rigFallback;
                }

                // DEV-ONLY: while the demo rig map is live, resolve the fallback
                // through it too — example rigs name animations lowercase ("idle"),
                // so the literal "Idle" would miss and end the chain with no pose.
                // Production rigs are expected to ship "Idle"; this branch never runs.
                if (_config != null && _config.DevSpineOverride && _demoRigMap != null)
                {
                    var demoFallback = _demoRigMap.ResolveAnimationForActivity(ChibiActivityMap.FallbackState);
                    if (!string.IsNullOrEmpty(demoFallback)) animName = demoFallback;
                }
            }

            if (skeletonData.FindAnimation(animName) == null)
            {
                if (_warned.Add("idle-missing"))
                {
                    Debug.LogWarning("[SpineChibiVisual] 'Idle' also missing in SkeletonData — keeping current pose (" +
                                     _discipleId + ")");
                }
                return;
            }

            _currentActivity = activityName ?? ChibiActivityMap.FallbackState; // store the ACTIVITY (not animation) so in-place respawn re-resolves correctly
            _skeletonAnimation.AnimationState.SetAnimation(MainTrack, animName, true);
        }

        public void SetSortingBase(int baseOrder)
        {
            _sortingBase = baseOrder;
            ApplySorting();
        }

        private void ApplySorting()
        {
            if (_skeletonAnimation == null) return;
            var mr = _skeletonAnimation.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = _sortingBase;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_instanceSkin != null)
            {
                _instanceSkin.Clear(); // release mixed attachments — skin is per-instance state
                _instanceSkin = null;
            }

            if (_go != null) UnityEngine.Object.Destroy(_go); // the GameObject, never a component (Phase 2 lesson)

            if (!string.IsNullOrEmpty(_discipleId) && Active.TryGetValue(_discipleId, out var registered) && registered == this)
            {
                Active.Remove(_discipleId);
            }
        }

        // ---- skin mixing ----

        /// <summary>
        /// Full rebuild of the per-instance mixed skin from the resolved Spine layers
        /// (rig default skin mixed in first, then one attachment-skin per resolved
        /// part). Called by Bind and ApplySlot — never destroys scene objects.
        /// </summary>
        private void RebuildSkin(AvatarAppearance appearance)
        {
            if (_skeletonAnimation == null || _skeletonAnimation.skeleton == null) return;

            _lastAppearance = appearance;
            _hasAppearance = appearance != null;
            if (appearance == null || appearance.Parts == null) return;

            var skeleton = _skeletonAnimation.skeleton;
            var skeletonData = skeleton.Data; // spine 3.8: Skeleton.Data (SkeletonData)

            _instanceSkin = new Skin(_discipleId + "_skin");

            // 1) rig default skin first — placeholder rigs render their setup pose even
            //    before per-part spineSkins exist.
            if (skeletonData.DefaultSkin != null) // spine 3.8: SkeletonData.DefaultSkin
            {
                _instanceSkin.AddSkin(skeletonData.DefaultSkin);
            }

            // 2) one mixed skin per resolved part (resolver already dropped slots the
            //    rig/backend can't support — fallback chain ran there, D4).
            //
            //    DEV-ONLY demo exception: production avatar_parts.json carries NO
            //    spineSkin yet (T2 — example-rig names must never pollute production
            //    data), so with DevSpineOverride live the resolver legitimately drops
            //    every slot before the demo map can translate them. When the resolved
            //    list comes back EMPTY and the demo map is live, drive the mix from the
            //    demo map's slot table instead (applied over the DEFAULT part of each
            //    slot — the demo stands in for future spineSkin data, it never writes
            //    to it). Ship builds never take this branch (DevSpineOverride=false).
            var layers = _resolver.Resolve(appearance, VisualBackend.Spine);
            if (layers.Count == 0 && _config != null && _config.DevSpineOverride && _demoRigMap != null)
            {
                layers = BuildDemoLayers();
            }
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                // DEV-ONLY demo override: example-rig skin per OUR part (wins) then
                // per OUR slot while the demo map is live — example skin names never
                // come from avatar_parts.json (production data stays clean).
                var skinName = string.Empty;
                if (_config != null && _config.DevSpineOverride && _demoRigMap != null)
                {
                    skinName = _demoRigMap.ResolveSkinForPart(layer.PartId);
                    if (string.IsNullOrEmpty(skinName)) skinName = _demoRigMap.ResolveSkinForSlot(layer.Slot);
                }
                if (string.IsNullOrEmpty(skinName)) skinName = layer.SpineSkin;
                if (string.IsNullOrEmpty(skinName)) skinName = layer.PartId;

                var partSkin = skeletonData.FindSkin(skinName);
                if (partSkin == null)
                {
                    if (_warned.Add("skin:" + skinName))
                    {
                        Debug.LogWarning("[SpineChibiVisual] skin '" + skinName + "' (slot '" + layer.Slot +
                                         "', part '" + layer.PartId + "') not in SkeletonData — skipped (" +
                                         _discipleId + ")");
                    }
                    continue;
                }

                _instanceSkin.AddSkin(partSkin); // 3.8: mixes attachments; later entries override same slot
            }

            // 3) apply — SetSkin + SetSlotsToSetupPose is the required 3.8 pair after
            //    rebuilding a skin; LateUpdate regenerates the mesh this frame.
            skeleton.SetSkin(_instanceSkin);
            skeleton.SetSlotsToSetupPose();
            _skeletonAnimation.LateUpdate();
        }

        /// <summary>
        /// DEV-ONLY: fallback layer list for the demo — one main layer per slot the
        /// demo rig map can translate, using each slot's DEFAULT production part as
        /// PartId (resolver fallback L2 ran nowhere because every slot was dropped).
        /// Ship builds never call this (guarded by DevSpineOverride at the caller).
        /// </summary>
        private List<ResolvedLayer> BuildDemoLayers()
        {
            var layers = new List<ResolvedLayer>(8);
            foreach (var slot in AvatarSlots.Equippable)
            {
                var skinName = _demoRigMap.ResolveSkinForSlot(slot);
                if (string.IsNullOrEmpty(skinName)) continue; // slot not mapped in the demo — skip silently

                var def = _pool.GetDefaultForSlot(slot);
                if (def == null) continue;

                layers.Add(new ResolvedLayer
                {
                    Slot = def.slot,
                    PartId = def.id,
                    Asset = string.Empty,
                    SpineSkin = skinName, // demo map already resolved the example-rig skin
                    Order = def.chibiOrder,
                    IsBack = false,
                    Tint = Color.white,
                });
            }
            layers.Sort((x, y) => x.Order.CompareTo(y.Order));
            return layers;
        }
    }
}
