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
    ///
    /// Q5 story-character overrides: when <see cref="VisualRuntimeConfig"/>.
    /// VisualOverridesPath points at a visual_overrides.json, the SAME activation
    /// pass also assigns <see cref="DiscipleVisualSystem.SpineOverrideProbe"/> so
    /// DiscipleVisualSystem can render overridden disciples from their OWN rig
    /// (per-character, no part mixing) — bypassing the §7 allocation/SpineBudget
    /// (a decision recorded at the CreateVisual override branch, not a default).
    /// The probe hooks are assigned HERE, inside the same license-gated block —
    /// they are never a hidden bypass of the S4 gate.
    /// </summary>
    public static class VisualSpineBootstrap
    {
        private static bool _activated;
        private static SkeletonDataAsset _skeletonDataAsset; // shared rig — one asset, all instances (Q4)
        private static ChibiActivityMap _activityMap;
        private static AppearanceResolver _resolver;
        private static AvatarPartPool _pool;
        private static VisualOverrideMap _overrideMap;       // Q5: discipleId → per-character rig path
        private static SkeletonDataAsset _overrideSkeleton;  // first cached per-character rig (single table entry today)

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

            // Q5 story-character overrides — same license-gated block, never a bypass:
            // the map is consulted ONLY from here on (SpineEnabled is true from this line).
            _overrideMap = new VisualOverrideMap(config.VisualOverridesPath);
            if (_overrideMap.HasAny())
            {
                DiscipleVisualSystem.SpineOverrideProbe = ResolveOverridePath;
                DiscipleVisualSystem.SpineOverrideVisualFactory = CreateOverrideVisual;
            }

            config.SpineEnabled = true;
            _activated = true;

            Debug.Log("[VisualSpineBootstrap] Spine backend ACTIVE — shared rig '" +
                      _skeletonDataAsset.name + "', SpineBudget=" + config.SpineBudget +
                      " (budget measured on the Phase-0 example rig, not a real chibi rig — see Q6)." +
                      (_overrideMap.HasAny()
                          ? " Story-character overrides: " + _overrideMap.Count + " (Q5 — per-character rigs, outside SpineBudget)."
                          : ""));

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

        // ---- Q5 story-character overrides (per-character rigs, outside §7 budget) ----

        /// <summary>Probe target — path only; asset load + null-check live in DiscipleVisualSystem.</summary>
        private static string ResolveOverridePath(string discipleId)
        {
            return _overrideMap.ResolveOverride(discipleId);
        }

        /// <summary>
        /// Override factory target (same IChibiVisual contract as the shared-rig
        /// factory): build a SpineChibiVisual from the disciple's OWN SkeletonDataAsset.
        /// Returning null = caller degrades to SpriteSheet (never hard-crash). The rig
        /// is assembled single-piece, so RebuildSkin's FindSkin loop simply no-ops and
        /// the default skin renders — same class, no SpineChibiVisual change.
        /// </summary>
        private static IChibiVisual CreateOverrideVisual(DiscipleState d, Transform parent)
        {
            var asset = LoadOverrideSkeleton(DiscipleVisualSystem.SpineOverrideProbe(d.DiscipleId));
            if (asset == null) return null; // caller warns + degrades to SpriteSheet
            // overrideActivityMap: per-rig activity→animation names from the SAME table
            // (real rigs name animations lowercase — idle1/walk/run); demoRigMap stays null.
            return SpineChibiVisual.Create(asset, parent, _resolver, _pool, _activityMap,
                                           VisualRuntimeConfig.Instance, null, _overrideMap);
        }

        /// <summary>
        /// Resources.Load the per-character rig — cached (the table maps every
        /// overridden disciple to its own asset; the first entry is cached because the
        /// shipped table is single-rig-per-sex with distinct paths — a multi-entry table
        /// needs a small path→asset dictionary before it ships).
        /// </summary>
        private static SkeletonDataAsset LoadOverrideSkeleton(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath)) return null;
            if (_overrideSkeleton != null && _overrideSkeletonPath == resourcePath) return _overrideSkeleton;

            var asset = Resources.Load<SkeletonDataAsset>(resourcePath);
            if (asset == null)
            {
                Debug.LogWarning("[VisualSpineBootstrap] override SkeletonDataAsset not found at Resources/" +
                                 resourcePath + " — disciple degrades to SpriteSheet (scene keeps running)");
                return null;
            }

            _overrideSkeletonPath = resourcePath;
            _overrideSkeleton = asset;
            return asset;
        }

        private static string _overrideSkeletonPath = string.Empty;
    }
}
