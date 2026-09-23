using System.Collections.Generic;
using Spine;
using Spine.Unity;
using UnityEngine;
using Xianxia.Sect.Visual.Spikes;

namespace Xianxia.Sect.Visual.Spines
{
    /// <summary>
    /// S1 — Spine shared-rig spike (one SkeletonDataAsset, N SkeletonAnimation
    /// instances with mixed skins — C8/L4). Uses the FREE "mix-and-match"
    /// example skeleton bundled in "Assets/Spine Examples" as a stand-in for the
    /// future shared chibi rig (no real chibi asset exists yet — see T2 report).
    ///
    /// Runtime note: this project ships spine-unity 3.8, so the current-track API
    /// is GetCurrent(track) — GetTrack(track) only exists on 4.x (L10 deviation,
    /// approved by user for Phase 0; recorded in the Phase-0 results report).
    /// L8 flip: Skeleton.ScaleX only — Transform.localScale is never touched.
    /// L9: unknown animation names fall back to "idle" with a one-time warning;
    /// animation list logged once per instance at spawn.
    /// </summary>
    public sealed class SpineSpikeRunner : MonoBehaviour
    {
        public const int DefaultCount = 20;
        public const float DefaultDuration = 15f;
        private const string SkeletonDataAssetEditorPath =
            "Assets/Spine Examples/Spine Skeletons/mix-and-match/mix-and-match-pro_SkeletonData.asset";
        private const string SkeletonDataResourcePath =
            "Spine Examples/Spine Skeletons/mix-and-match/mix-and-match-pro_SkeletonData";
        private const string FallbackAnimation = "idle"; // L9 (3.8 example asset naming)

        private readonly List<SkeletonAnimation> _instances = new List<SkeletonAnimation>(DefaultCount);
        private readonly List<bool> _facing = new List<bool>(DefaultCount);
        private readonly List<string> _instanceLog = new List<string>(DefaultCount);

        private Transform _root;
        private Spine.Unity.SkeletonDataAsset _skeletonDataAsset;
        private VisualSpikeProfiler _profiler;
        private int _warmupFrames;
        private int _sampleFrames;
        private int _warmupTarget = 120;
        private int _measureTarget = 600;
        private float _skinSwapTimer;
        private float _skinSwapInterval = 2f;
        private int _swapRound;
        private bool _finished;

        public void Configure(int count, float duration, int warmupFrames)
        {
            _targetCount = count;
            _measureTarget = Mathf.Max(60, (int)(duration * 60f));
            _warmupTarget = warmupFrames;
        }

        private int _targetCount = DefaultCount;

        private void Start()
        {
            SpikeSceneBootstrap.EnsureSceneCamera();

            _skeletonDataAsset = LoadSkeletonDataAsset();
            if (_skeletonDataAsset == null)
            {
                Debug.LogError("[SpineSpikeRunner] SkeletonDataAsset not found (editor path: " +
                               SkeletonDataAssetEditorPath + ") — import 'Spine Examples' first (free assets).");
                enabled = false;
                return;
            }

            _root = new GameObject("SpineInstances").transform;
            _root.SetParent(transform, false);

            var rnd = new System.Random(999);
            var partSkinNames = CollectPartSkinNames();
            int halfW = Mathf.FloorToInt(SpikeSceneBootstrap.CameraHalfWidth) - 2;
            int halfH = Mathf.FloorToInt(SpikeSceneBootstrap.CameraHalfHeight) - 2;

            for (int i = 0; i < _targetCount; i++)
            {
                // One shared rig, N instances — never a rig per instance (C8/L4).
                var sa = SkeletonAnimation.NewSkeletonAnimationGameObject(_skeletonDataAsset);
                sa.gameObject.name = "spine_chibi_" + i.ToString("D2");
                sa.transform.SetParent(_root, false);
                sa.transform.localPosition = new Vector3(
                    (float)rnd.Next(-halfW, halfW + 1),
                    (float)rnd.Next(-halfH, halfH + 1),
                    0f);

                // Random mix of the example asset's part-skins, mimicking the
                // production design of slot → skin mapping (C8/L4).
                var skin = new Skin("spike_" + i);
                skin.AddSkin(_skeletonDataAsset.GetSkeletonData(true).FindSkin("skin-base")); // body base
                int picks = 2 + rnd.Next(3); // 2..4 part skins per instance
                for (int p = 0; p < picks; p++)
                {
                    string partSkin = partSkinNames[rnd.Next(partSkinNames.Count)];
                    skin.AddSkin(_skeletonDataAsset.GetSkeletonData(true).FindSkin(partSkin));
                }
                sa.skeleton.SetSkin(skin);
                sa.skeleton.SetSlotsToSetupPose();
                sa.LateUpdate();

                sa.state.SetAnimation(0, ResolveAnimationName(sa, "idle"), true);

                _facing.Add(rnd.Next(2) == 0);
                sa.skeleton.ScaleX = _facing[i] ? 1f : -1f; // L8 flip here only
                LogAnimationsOnce(sa); // L9: one-time animation list per instance

                _instances.Add(sa);
            }

            // GC cost of a full custom-skin rebuild per instance (per-operation metric, C9).
            _profiler = new VisualSpikeProfiler("S1_SpineSharedRig_" + _instances.Count);
            for (int i = 0; i < _instances.Count; i++)
            {
                var inst = _instances[i];
                _profiler.MeasureOperation("SetSkin_instance_" + i.ToString("D2"), delegate
                {
                    var s = new Skin("swap_" + i);
                    s.AddSkin(_skeletonDataAsset.GetSkeletonData(true).FindSkin("skin-base"));
                    for (int p = 0; p < 3; p++)
                        s.AddSkin(_skeletonDataAsset.GetSkeletonData(true)
                            .FindSkin(partSkinNames[p % partSkinNames.Count]));
                    inst.skeleton.SetSkin(s);
                    inst.skeleton.SetSlotsToSetupPose();
                });
            }

            _profiler.MarkWarmupEnd();
        }

        // "Assets/Spine Examples" is not a Resources folder — load via AssetDatabase
        // in the editor (spikes are editor-only per C2); Resources fallback kept for parity.
        private static Spine.Unity.SkeletonDataAsset LoadSkeletonDataAsset()
        {
#if UNITY_EDITOR
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<Spine.Unity.SkeletonDataAsset>(SkeletonDataAssetEditorPath);
            if (asset != null) return asset;
#endif
            return Resources.Load<Spine.Unity.SkeletonDataAsset>(SkeletonDataResourcePath);
        }

        private void Update()
        {
            if (_finished) return;

            // Rebuild one custom skin per round (amortized SetSkin cost while animating).
            _skinSwapTimer += Time.deltaTime;
            if (_skinSwapTimer >= _skinSwapInterval)
            {
                _skinSwapTimer = 0f;
                int idx = _swapRound % _instances.Count;
                _swapRound++;
                var inst = _instances[idx];
                var skin = new Skin("live_swap_" + idx);
                skin.AddSkin(_skeletonDataAsset.GetSkeletonData(true).FindSkin("skin-base"));
                skin.AddSkin(_skeletonDataAsset.GetSkeletonData(true).FindSkin("hair/short-red"));
                skin.AddSkin(_skeletonDataAsset.GetSkeletonData(true).FindSkin("legs/boots-red"));
                inst.skeleton.SetSkin(skin);
                inst.skeleton.SetSlotsToSetupPose();
            }

            // L8 flip exercise: one instance flips each second.
            int flipIdx = Time.frameCount % _instances.Count;
            _facing[flipIdx] = !_facing[flipIdx];
            _instances[flipIdx].skeleton.ScaleX = _facing[flipIdx] ? 1f : -1f;

            if (_warmupFrames < _warmupTarget)
            {
                _warmupFrames++;
            }
            else
            {
                _profiler.SampleFrame();
                _sampleFrames++;
                if (_sampleFrames >= _measureTarget)
                {
                    _profiler.Finish();
                    _finished = true;
                }
            }
        }

        /// <summary>L9: names are data-driven — unknown names fall back with a one-time warning.</summary>
        private string ResolveAnimationName(SkeletonAnimation sa, string wanted)
        {
            var data = _skeletonDataAsset.GetSkeletonData(true);
            if (data.FindAnimation(wanted) != null) return wanted;
            Debug.LogWarning("[SpineSpikeRunner] animation '" + wanted + "' missing on '" +
                             sa.gameObject.name + "' — falling back to '" + FallbackAnimation + "' (one-time warning, L9)");
            return FallbackAnimation;
        }

        private void LogAnimationsOnce(SkeletonAnimation sa)
        {
            var data = _skeletonDataAsset.GetSkeletonData(true);
            var names = new List<string>(data.Animations.Count);
            var anims = data.Animations;
            for (int i = 0; i < anims.Count; i++) names.Add(anims.Items[i].Name);
            string line = sa.gameObject.name + " animations: " + string.Join(", ", names.ToArray());
            _instanceLog.Add(line);
            Debug.Log("[SpineSpikeRunner] " + line);
        }

        private static List<string> CollectPartSkinNames()
        {
            // Part skins of the mix-and-match example asset (verified against the
            // asset's JSON); "default" and "full-skins/*" excluded on purpose —
            // production maps part-level skins per slot only.
            return new List<string>
            {
                "accessories/backpack", "accessories/bag", "hair/blue", "legs/boots-pink",
                "legs/boots-red", "hair/brown", "accessories/cape-blue", "accessories/cape-red",
                "clothes/dress-blue", "clothes/dress-green", "eyes/eyes-blue", "eyes/green",
                "eyes/violet", "eyes/yellow", "eyelids/girly", "eyelids/semiclosed",
                "accessories/hat-pointy-blue-yellow", "accessories/hat-red-yellow",
                "clothes/hoodie-blue-and-scarf", "clothes/hoodie-orange", "nose/long",
                "nose/short", "hair/long-blue-with-scarf", "legs/pants-green",
                "legs/pants-jeans", "hair/pink", "accessories/scarf", "hair/short-red"
            };
        }
    }
}
