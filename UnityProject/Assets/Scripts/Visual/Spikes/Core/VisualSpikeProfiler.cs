using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Unity.Profiling;

namespace Xianxia.Sect.Visual.Spikes
{
    /// <summary>
    /// Phase-0 spike metrics collector (C9): CPU ms/frame (median + p95),
    /// draw calls / batches, GC alloc per frame and per operation (e.g. one
    /// SetSkin rebuild), steady-state GC after warm-up.
    /// Writes CSV to Application.persistentDataPath/visual_spikes/&lt;timestamp&gt;.csv
    /// and prints a console summary.
    ///
    /// Metric sources (Unity 6):
    ///  - CPU: ProfilerRecorder "CPU Main Thread Frame Time" (ms per frame,
    ///    real CPU cost) — falls back to frame-to-frame wall time if the
    ///    counter is unavailable; the CSV row cpu_metric records which mode.
    ///  - Draw calls / batches: UnityEditor.UnityStats (works without the
    ///    Profiler window; ProfilerRecorder render counters return 0 unless
    ///    the Profiler captures).
    ///  - GC: ProfilerRecorder "GC Allocated In Frame" — falls back to
    ///    GC.GetTotalMemory deltas (which quantize to heap-growth blocks).
    /// All measurements are editor-only; in player builds they compile out.
    /// </summary>
    public sealed class VisualSpikeProfiler : IDisposable
    {
        public const string EditorMenuRoot = "Xianxia/Visual Spikes/";

        private readonly string _label;
        private readonly List<float> _frameMs = new List<float>(4096);
        private readonly List<long> _gcPerFrame = new List<long>(4096);
        private readonly List<KeyValuePair<string, long>> _opAllocs =
            new List<KeyValuePair<string, long>>(64);

        private static readonly System.Diagnostics.Stopwatch Watch = System.Diagnostics.Stopwatch.StartNew();

        private ProfilerRecorder _cpuRecorder;
        private ProfilerRecorder _gcRecorder;
        private bool _cpuCounterValid;
        private long _monoUsedStart;
        private long _monoUsedLast;
        private float _sampleStartTime;
        private float _lastSampleTime;
        private string _csvPath;
        private readonly StringBuilder _csv = new StringBuilder();

        public VisualSpikeProfiler(string label)
        {
            _label = label;
            _csvPath = string.Empty;
            long used = GC.GetTotalMemory(false);
            _monoUsedStart = used;
            _monoUsedLast = used;
        }

        /// <summary>Call once after spawning / set-up, right before the timed window starts.</summary>
        public void MarkWarmupEnd()
        {
#if UNITY_EDITOR
            _cpuRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Internal, "CPU Main Thread Frame Time");
            _cpuCounterValid = _cpuRecorder.Valid;
            _gcRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory, "GC Allocated In Frame");

            _monoUsedStart = GC.GetTotalMemory(false);
            _monoUsedLast = _monoUsedStart;
            _sampleStartTime = NowSeconds();
            _lastSampleTime = _sampleStartTime;
#endif
        }

        /// <summary>Call once per rendered frame inside the timed window.</summary>
        public void SampleFrame()
        {
#if UNITY_EDITOR
            if (_cpuCounterValid)
            {
                // Counter raw value is nanoseconds (Unity 6 CPU module counters);
                // convert to ms for reporting.
                _frameMs.Add(_cpuRecorder.LastValue / 1000000f);
            }
            else
            {
                float now = NowSeconds();
                _frameMs.Add((now - _lastSampleTime) * 1000f);
                _lastSampleTime = now;
            }

            if (_gcRecorder.Valid)
            {
                _gcPerFrame.Add(_gcRecorder.LastValue);
            }
            else
            {
                long used = GC.GetTotalMemory(false);
                _gcPerFrame.Add(used - _monoUsedLast);
                _monoUsedLast = used;
            }
#endif
        }

        /// <summary>Measure GC allocation of a single operation (e.g. one SetSkin rebuild).</summary>
        public void MeasureOperation(string opName, Action op)
        {
#if UNITY_EDITOR
            if (op == null) return;
            if (_gcRecorder.Valid)
            {
                // GC Allocated In Frame accumulates within the current frame;
                // the before/after delta isolates this operation's allocation.
                long before = _gcRecorder.LastValue;
                op();
                long delta = _gcRecorder.LastValue - before;
                if (delta < 0) delta = 0; // frame boundary crossed mid-measure
                _opAllocs.Add(new KeyValuePair<string, long>(opName, delta));
            }
            else
            {
                long before = GC.GetTotalMemory(false);
                op();
                long after = GC.GetTotalMemory(false);
                _opAllocs.Add(new KeyValuePair<string, long>(opName, after - before));
            }
#endif
        }

        /// <summary>Finish the window: flush CSV + console summary. Returns the CSV path (editor).</summary>
        public string Finish()
        {
#if UNITY_EDITOR
            float elapsed = Mathf.Max(0.001f, NowSeconds() - _sampleStartTime);
            double fps = _frameMs.Count / elapsed;

            float median, p95;
            ComputePercentile(_frameMs, out median, out p95);
            long gcPerFrameMedian = Median(_gcPerFrame);

            // UnityStats reflects the last rendered Game View frame; works
            // without the Profiler window (unlike render ProfilerRecorders).
            int drawCalls = UnityEditor.UnityStats.drawCalls;
            int batches = UnityEditor.UnityStats.batches;

            long steadyGc = GC.GetTotalMemory(false) - _monoUsedStart;
            string cpuMetric = _cpuCounterValid ? "cpu_main_thread_frame_time" : "frame_delta_wall_time";
            string gcMetric = _gcRecorder.Valid ? "gc_allocated_in_frame" : "mono_heap_delta";

            var sb = new StringBuilder();
            sb.AppendLine("=== VisualSpikeProfiler: " + _label + " ===");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "frames={0}  duration={1:F1}s  fps={2:F0}", _frameMs.Count, elapsed, fps));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "CPU ms/frame [{0}]: median={1:F3}  p95={2:F3}", cpuMetric, median, p95));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Draw calls={0}  Batches={1}", drawCalls, batches));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "GC per-frame median [{0}]={1} B  steady-state delta over window={2} B",
                gcMetric, gcPerFrameMedian, steadyGc));
            if (_opAllocs.Count > 0)
            {
                sb.AppendLine("GC per operation:");
                for (int i = 0; i < _opAllocs.Count; i++)
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0}: {1} B", _opAllocs[i].Key, _opAllocs[i].Value));
            }
            Debug.Log(sb.ToString());

            string dir = Path.Combine(Application.persistentDataPath, "visual_spikes");
            Directory.CreateDirectory(dir);
            _csvPath = Path.Combine(dir,
                _label.Replace(" ", "_") + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv");

            _csv.Length = 0;
            _csv.AppendLine("label,metric,value,unit");
            AppendRow("frames", _frameMs.Count.ToString(), "count");
            AppendRow("duration", elapsed.ToString("F1", CultureInfo.InvariantCulture), "s");
            AppendRow("fps", fps.ToString("F0", CultureInfo.InvariantCulture), "fps");
            AppendRow("cpu_metric", cpuMetric, "mode");
            AppendRow("cpu_ms_median", median.ToString("F3", CultureInfo.InvariantCulture), "ms");
            AppendRow("cpu_ms_p95", p95.ToString("F3", CultureInfo.InvariantCulture), "ms");
            AppendRow("draw_calls", drawCalls.ToString(), "count");
            AppendRow("batches", batches.ToString(), "count");
            AppendRow("gc_metric", gcMetric, "mode");
            AppendRow("gc_per_frame_median", gcPerFrameMedian.ToString(), "B");
            AppendRow("gc_steady_state_delta", steadyGc.ToString(), "B");
            for (int i = 0; i < _opAllocs.Count; i++)
                AppendRow("gc_op_" + _opAllocs[i].Key, _opAllocs[i].Value.ToString(), "B");
            File.WriteAllText(_csvPath, _csv.ToString());
            Debug.Log("[VisualSpikeProfiler] CSV written: " + _csvPath);

            DisposeRecorders();
            return _csvPath;
#else
            return string.Empty;
#endif
        }

        public void Dispose()
        {
            DisposeRecorders();
        }

        // ---- internals ----

        private void DisposeRecorders()
        {
#if UNITY_EDITOR
            _cpuRecorder.Dispose();
            _gcRecorder.Dispose();
#endif
        }

        private void AppendRow(string metric, string value, string unit)
        {
            _csv.Append(_label.Replace(",", ";")).Append(',').Append(metric).Append(',')
                .Append(value).Append(',').AppendLine(unit);
        }

        private static void ComputePercentile(List<float> values, out float median, out float p95)
        {
            if (values == null || values.Count == 0) { median = 0f; p95 = 0f; return; }
            var sorted = new List<float>(values);
            sorted.Sort();
            median = sorted[sorted.Count / 2];
            int idx = Mathf.Clamp(Mathf.CeilToInt(sorted.Count * 0.95f) - 1, 0, sorted.Count - 1);
            p95 = sorted[idx];
        }

        private static long Median(List<long> values)
        {
            if (values == null || values.Count == 0) return 0;
            var sorted = new List<long>(values);
            sorted.Sort();
            return sorted[sorted.Count / 2];
        }

        private static float NowSeconds()
        {
            return (float)(Watch.ElapsedTicks / (double)System.TimeSpan.TicksPerSecond);
        }
    }
}
