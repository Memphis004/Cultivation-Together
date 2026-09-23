using Spine.Unity;
using UnityEngine;
using VContainer; // IObjectResolverExtensions.Resolve<T>
using Xianxia.Sect; // GameLifetimeScope

namespace Xianxia.Sect.Visual.Spines
{
    /// <summary>
    /// ██████ DEV-ONLY — VISUAL DEMO SCENE — DO NOT SHIP ██████
    ///
    /// The ONLY component in the project allowed to write
    /// <see cref="VisualRuntimeConfig.DevSpineOverride"/> (demo spec C3).
    ///
    /// R1/S4 NOTE: the Spine license decision is STILL OPEN (phase0-results §4).
    /// This enabler does NOT touch the production S4 gate
    /// (SpineActivationRequested stays false; VisualSpineBootstrap stays INERT).
    /// Instead it registers the DEMO SpineVisualFactory itself — the same enum→factory
    /// seam (C12) production uses — pointing at the FREE example rig assigned in the
    /// demo scene (serialized field; Assets/Spine Examples is NOT a Resources folder).
    /// Example-rig skin/animation names resolve through DemoSpineRigMap, so production
    /// data (avatar_parts.json / chibi_activity_map.json) stays untouched (T2).
    ///
    /// Lifecycle: Awake → override true + factory registered; OnDestroy → both
    /// restored, so the Spine path closes exactly when the demo scene unloads.
    /// </summary>
    public sealed class VisualDemoSpineEnabler : MonoBehaviour
    {
        [Tooltip("DEV-ONLY: free Spine example rig (mix-and-match-pro) standing in for the future chibi rig.")]
        [SerializeField] private SkeletonDataAsset skeletonDataAsset;

        private static System.Func<DiscipleState, Transform, IChibiVisual> _productionFactory;

        private void Awake()
        {
            VisualRuntimeConfig.Instance.DevSpineOverride = true;
            Debug.Log("[VisualDemoSpineEnabler] DEV-ONLY: DevSpineOverride=true " +
                      "(R1/S4 license still undecided — example rig stand-in, demo scene only)");

            if (skeletonDataAsset == null)
            {
                Debug.LogError("[VisualDemoSpineEnabler] no skeletonDataAsset assigned on the demo scene — " +
                               "Spine tier will degrade to SpriteSheet (visual demo incomplete)");
                return;
            }

            RegisterDemoFactory();
        }

        private void OnDestroy()
        {
            // restore whatever the production bootstrap had (null while S4 is open)
            DiscipleVisualSystem.SpineVisualFactory = _productionFactory;
            VisualRuntimeConfig.Instance.DevSpineOverride = false;
            Debug.Log("[VisualDemoSpineEnabler] DEV-ONLY: demo factory removed, DevSpineOverride=false (demo scene unloaded)");
        }

        /// <summary>
        /// enum→factory map (C12 — no reflection, no FindObjectOfType). Same shape as
        /// VisualSpineBootstrap.CreateSpineVisual but bound to the example rig + demo map.
        /// </summary>
        private void RegisterDemoFactory()
        {
            var injector = GameLifetimeScope.Injector;
            if (injector == null)
            {
                Debug.LogError("[VisualDemoSpineEnabler] no GameLifetimeScope.Injector — demo factory not registered");
                return;
            }

            var resolver = injector.Resolve<AppearanceResolver>();
            var pool = injector.Resolve<AvatarPartPool>();
            if (resolver == null || pool == null)
            {
                Debug.LogError("[VisualDemoSpineEnabler] visual deps unresolvable — demo factory not registered");
                return;
            }

            var activityMap = new ChibiActivityMap();           // production table (activities, not rig names)
            var demoRigMap = new DemoSpineRigMap();             // DEV-ONLY example-rig names (T2)
            var config = VisualRuntimeConfig.Instance;
            var rig = skeletonDataAsset;

            _productionFactory = DiscipleVisualSystem.SpineVisualFactory; // usually null; restore on unload
            DiscipleVisualSystem.SpineVisualFactory = (d, parent) =>
                SpineChibiVisual.Create(rig, parent, resolver, pool, activityMap, config, demoRigMap);

            Debug.Log("[VisualDemoSpineEnabler] demo SpineVisualFactory registered — shared example rig '" +
                      rig.name + "', SpineBudget=" + config.SpineBudget +
                      " (S4 production gate untouched: SpineActivationRequested=" + config.SpineActivationRequested + ")");
        }
    }
}
