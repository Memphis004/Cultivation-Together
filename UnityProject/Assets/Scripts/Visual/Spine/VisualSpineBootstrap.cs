using Spine.Unity;
using UnityEngine;
using VContainer; // IObjectResolverExtensions.Resolve<T>
using Xianxia.Sect; // GameLifetimeScope

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// Phase 3 activation plugin (Visual.Spine assembly — the ONLY production place
    /// spine-unity is referenced; Core/Sprite compile with zero Spine dependency —
    /// the C3 split proven in Phase 0).
    ///
    /// Self-activating via [RuntimeInitializeOnLoadMethod] because the Runtime
    /// assembly (GameLifetimeScope) must NOT reference this assembly — that would
    /// drag spine-unity back into the core compile. Dependencies come from
    /// GameLifetimeScope.Injector (the static container hook — composition-root
    /// consumer, not gameplay logic; no FindObjectOfType, no reflection — C12).
    ///
    /// HARD GATES — ALL must pass before ANY Spine runtime object is created:
    ///  1. S4 LICENSE: <see cref="VisualRuntimeConfig.SpineActivationRequested"/> == true.
    ///     Settable ONLY by a human decision after the phase0-results §4 checklist
    ///     passes (L12). Default false → this plugin is a no-op and the Spine path
    ///     stays inert (entitled disciples render as SpriteSheet, state untouched).
    ///  2. RIG: <see cref="VisualRuntimeConfig.SpineSkeletonResourcePath"/> must load a
    ///     real SkeletonDataAsset from Resources — null-checked here with a warning,
    ///     never a hard crash (spec Phase 3).
    ///
    /// On success: assigns <see cref="DiscipleVisualSystem.SpineVisualFactory"/>
    /// (enum→factory map, C12), flips SpineEnabled = true, and calls the visual
    /// system's public Reconcile so already-spawned entitled disciples promote
    /// IN PLACE (position/activity/facing preserved by that path).
    /// </summary>
    public static class VisualSpineBootstrap
    {
        private static bool _activated;
        private static SkeletonDataAsset _skeletonDataAsset; // shared rig — one asset, all instances (Q4)
        private static ChibiActivityMap _activityMap;
        private static AppearanceResolver _resolver;
        private static AvatarPartPool _pool;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void TryActivate()
        {
            var config = VisualRuntimeConfig.Instance;

            if (_activated) return;
            if (!config.SpineActivationRequested)
            {
                Debug.Log("[VisualSpineBootstrap] S4 license gate not confirmed " +
                          "(SpineActivationRequested=false) — Spine backend stays INERT " +
                          "(entitled disciples render as SpriteSheet, entitlement untouched).");
                return;
            }

            var injector = GameLifetimeScope.Injector;
            if (injector == null)
            {
                Debug.LogWarning("[VisualSpineBootstrap] no GameLifetimeScope.Injector — Spine stays INERT.");
                return;
            }

            var path = config.SpineSkeletonResourcePath;
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("[VisualSpineBootstrap] SpineActivationRequested=true but " +
                                 "SpineSkeletonResourcePath is empty — Spine stays INERT (fix config).");
                return;
            }

            _skeletonDataAsset = Resources.Load<SkeletonDataAsset>(path);
            if (_skeletonDataAsset == null)
            {
                Debug.LogWarning("[VisualSpineBootstrap] SkeletonDataAsset not found at Resources/" +
                                 path + " — Spine stays INERT (rig missing; scene keeps running).");
                return;
            }

            _resolver = injector.Resolve<AppearanceResolver>();
            _pool = injector.Resolve<AvatarPartPool>();
            var visualSystem = injector.Resolve<DiscipleVisualSystem>();
            if (_resolver == null || _pool == null || visualSystem == null)
            {
                Debug.LogWarning("[VisualSpineBootstrap] visual dependencies not resolvable — Spine stays INERT.");
                return;
            }

            _activityMap = new ChibiActivityMap(); // Resources/Data/chibi_activity_map.json (L4 data-driven)

            DiscipleVisualSystem.SpineVisualFactory = CreateSpineVisual;
            config.SpineEnabled = true;
            _activated = true;

            Debug.Log("[VisualSpineBootstrap] Spine backend ACTIVE — shared rig '" +
                      _skeletonDataAsset.name + "', SpineBudget=" + config.SpineBudget +
                      " (budget measured on the Phase-0 example rig, not a real chibi rig — see Q6).");

            // Promote already-spawned entitled disciples in place (public reconcile —
            // allocation now returns them, respawn-in-place keeps pos/activity/facing).
            visualSystem.Reconcile();
        }

        /// <summary>enum→factory map target (C12). Returning null = caller degrades to SpriteSheet.</summary>
        private static IChibiVisual CreateSpineVisual(DiscipleState d, Transform parent)
        {
            return SpineChibiVisual.Create(_skeletonDataAsset, parent, _resolver, _pool, _activityMap,
                                           VisualRuntimeConfig.Instance);
        }
    }
}
