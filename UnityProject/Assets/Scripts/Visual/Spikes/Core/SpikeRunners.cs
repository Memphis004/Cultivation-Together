using System.Collections.Generic;
using UnityEngine;

namespace Xianxia.Sect.Visual.Spikes
{
    /// <summary>
    /// S2 — 300 layered sprite instances driven by ONE central frame clock (C7/L3),
    /// sprites from ONE baked atlas (L7), flip via parent localScale.x only (L8).
    /// Runs when its component exists in a spike scene only (no auto-load — C2).
    /// </summary>
    public sealed class SpriteLayerSpikeRunner : MonoBehaviour
    {
        public const int DefaultCount = 300;
        public const int MeasureSecondsAt60Fps = 900;

        private const int LayersPerInstance = 4; // hair_back, body, head, hair_front
        private const float Fps = 10f;

        private Transform _root;
        private float _clock;
        private int _lastAppliedFrame = -1;
        private int _warmupFrames;
        private int _sampleFrames;
        private VisualSpikeProfiler _profiler;
        private Sprite[][] _slotSheets;
        private Transform[] _flipPivots;
        private SpriteRenderer[] _layerRenderers;
        private int _instanceCount = DefaultCount;
        private int _warmupTarget = 120;
        private int _measureTarget = MeasureSecondsAt60Fps;
        private bool _finished;

        public void Configure(int count, int warmupFrames, int measureFrames)
        {
            _instanceCount = count;
            _warmupTarget = warmupFrames;
            _measureTarget = measureFrames;
        }

        private void Start()
        {
            SpikeSceneBootstrap.EnsureSceneCamera();

            _root = new GameObject("SpriteInstances").transform;
            var baked = PlaceholderSpriteBaker.GetOrCreate();
            _slotSheets = new Sprite[SpikeSceneBootstrap.SlotNames.Length][];
            for (int s = 0; s < SpikeSceneBootstrap.SlotNames.Length; s++)
            {
                Sprite[] sheet;
                baked.SheetsBySlot.TryGetValue(SpikeSceneBootstrap.SlotNames[s], out sheet);
                _slotSheets[s] = sheet;
            }

            var sharedMat = SharedSpriteMaterial();
            var container = new GameObject("Instances").transform;
            container.SetParent(_root, false);
            _flipPivots = new Transform[_instanceCount];
            _layerRenderers = new SpriteRenderer[_instanceCount * LayersPerInstance];

            var rnd = new System.Random(12345);
            int halfW = Mathf.FloorToInt(SpikeSceneBootstrap.CameraHalfWidth) - 2;
            int halfH = Mathf.FloorToInt(SpikeSceneBootstrap.CameraHalfHeight) - 2;

            for (int i = 0; i < _instanceCount; i++)
            {
                var pivotGo = new GameObject("chibi_" + i.ToString("D3"));
                pivotGo.transform.SetParent(container, false);
                pivotGo.transform.localPosition = new Vector3(
                    (float)rnd.Next(-halfW, halfW + 1),
                    (float)rnd.Next(-halfH, halfH + 1),
                    0f);
                bool faceRight = rnd.Next(2) == 0;
                pivotGo.transform.localScale = new Vector3(faceRight ? 1f : -1f, 1f, 1f); // L8
                _flipPivots[i] = pivotGo.transform;

                for (int l = 0; l < LayersPerInstance; l++)
                {
                    var layerGo = new GameObject("layer_" + SpikeSceneBootstrap.SlotNames[LayerToSlot(l)]);
                    layerGo.transform.SetParent(pivotGo.transform, false);
                    layerGo.transform.localPosition = Vector3.zero;
                    var sr = layerGo.AddComponent<SpriteRenderer>();
                    sr.sharedMaterial = sharedMat;
                    sr.sortingOrder = l;
                    _layerRenderers[i * LayersPerInstance + l] = sr;
                }
            }

            ApplyFrame(0);

            _profiler = new VisualSpikeProfiler("S2_SpriteLayered_" + _instanceCount);
            _profiler.MarkWarmupEnd();
            // Prove the per-frame spike code itself is zero-alloc (C7): measure
            // one ApplyFrame pass directly, isolated from editor-loop noise.
            _profiler.MeasureOperation("ApplyFrame_" + _instanceCount + "x" + LayersPerInstance,
                () => ApplyFrame(1));
        }

        private static int LayerToSlot(int layer)
        {
            switch (layer)
            {
                case 0: return 0; // hair_back
                case 1: return 1; // body
                case 2: return 2; // head
                default: return 4; // hair_front
            }
        }

        private void Update()
        {
            if (_finished) return;

            _clock += Time.deltaTime * Fps;
            int frame = ((int)_clock) % PlaceholderSpriteBaker.FramesPerSheet;
            if (frame != _lastAppliedFrame)
            {
                ApplyFrame(frame);
                _lastAppliedFrame = frame;
            }

            int flipIdx = (Time.frameCount / 30) % _flipPivots.Length;
            var p = _flipPivots[flipIdx].localScale;
            _flipPivots[flipIdx].localScale = new Vector3(-p.x, p.y, p.z); // L8

            if (_warmupFrames < _warmupTarget)
            {
                _warmupFrames++;
                return;
            }
            _profiler.SampleFrame();
            _sampleFrames++;
            if (_sampleFrames >= _measureTarget)
            {
                _profiler.Finish();
                _finished = true;
                enabled = false;
            }
        }

        private void ApplyFrame(int frame)
        {
            for (int i = 0; i < _instanceCount; i++)
            {
                for (int l = 0; l < LayersPerInstance; l++)
                {
                    var sheet = _slotSheets[LayerToSlot(l)];
                    if (sheet == null) continue;
                    _layerRenderers[i * LayersPerInstance + l].sprite = sheet[frame];
                }
            }
        }

        private static Material SharedSpriteMaterial()
        {
            var shader = Shader.Find("Sprites/Default");
            return new Material(shader) { name = "SpikeSharedSpriteMaterial" };
        }
    }

    /// <summary>
    /// S3 — spawn one placeholder chibi per candidate cell size (64/96/128) for
    /// side-by-side comparison, then capture a screenshot.
    /// </summary>
    public sealed class CellSizeProbeRunner : MonoBehaviour
    {
        private static readonly int[] Cells = { 64, 96, 128 };

        private void Start()
        {
            SpikeSceneBootstrap.EnsureSceneCamera();
            var baked = PlaceholderSpriteBaker.GetOrCreate();
            if (baked == null || baked.Body == null)
            {
                Debug.LogError("[CellSizeProbeRunner] no baked body sprite");
                enabled = false;
                return;
            }

            for (int i = 0; i < Cells.Length; i++)
            {
                var container = new GameObject("SizeProbe_" + Cells[i]).transform;
                container.SetParent(transform, false);
                container.localPosition = new Vector3(-6f + i * 6f, -2f, 0f);

                float worldHeight = 1.5f;                     // sprite baked height
                float scale = Cells[i] / 64f / worldHeight;   // render height = cell/64 units

                var go = new GameObject("probe_chibi_" + Cells[i]);
                go.transform.SetParent(container, false);
                go.transform.localScale = new Vector3(scale, scale, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = baked.Body;
                sr.sortingOrder = 10;
                Debug.Log("[CellSizeProbeRunner] cell=" + Cells[i] +
                          " rendered height=" + (worldHeight * scale).ToString("F3") + " units");
            }

#if UNITY_EDITOR
            string dir = System.IO.Path.Combine(Application.persistentDataPath, "visual_spikes");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "S3_size_probe_" +
                System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            UnityEngine.ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[CellSizeProbeRunner] screenshot queued: " + path);
#endif
            enabled = false;
        }
    }
}
