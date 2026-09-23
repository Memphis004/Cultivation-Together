using Unity.Profiling;
using UnityEngine;

namespace Xianxia.Sect.Visual.Spikes
{
    /// <summary>
    /// Play-mode measurement probe for the Phase 2 acceptance verify (report #3).
    /// Created ONLY by the Editor-only Phase2VerifyRunner (which writes the arm flag
    /// file before entering play mode); it never auto-loads in gameplay (C2) and
    /// destroys itself when not armed.
    ///
    /// Counters match VisualSpikeProfiler exactly (CPU Main Thread Frame Time +
    /// GC Allocated In Frame) so numbers are directly comparable to the Phase 0
    /// S2 gate. Draw calls/batches are read editor-side (UnityStats) by the runner.
    /// </summary>
    public sealed class Phase2VerifyDriver : MonoBehaviour
    {
        private const string ArmFlag = "Library/phase2_verify_armed.txt";

        private ProfilerRecorder _cpu;
        private ProfilerRecorder _gc;
        private bool _recordersCreated;
        private System.Reflection.MethodInfo _notify;

        private void Awake()
        {
            bool armed = false;
            try
            {
                armed = System.IO.File.Exists(ArmFlag);
                if (armed) System.IO.File.Delete(ArmFlag);
            }
            catch (System.IO.IOException) { }

            if (!armed)
            {
                Destroy(gameObject);
                return;
            }
            DontDestroyOnLoad(gameObject); // survive gameplay scene swaps during the verify
        }

        private void OnEnable()
        {
            _cpu = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Main Thread Frame Time");
            _gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            _recordersCreated = true;

            var runnerType = System.Type.GetType(
                "Xianxia.Sect.Visual.Spikes.EditorTools.Phase2VerifyRunner, Xianxia.Sect.Visual.Spikes.Editor");
            if (runnerType != null)
            {
                _notify = runnerType.GetMethod("NotifyFrameMeasured",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            }
        }

        private void Update()
        {
            if (_notify == null || !_recordersCreated) return;

            // Editor reflection hand-off (documented exception — no FindObjectOfType,
            // no interprocess; in-editor measurement only)
            _notify.Invoke(null, new object[]
            {
                (float)_cpu.LastValue,      // ms/frame, same counter as spike S2
                (long)_gc.LastValue,        // bytes allocated this frame
            });
        }

        private void OnDisable()
        {
            if (_recordersCreated)
            {
                _cpu.Dispose();
                _gc.Dispose();
                _recordersCreated = false;
            }
        }
    }
}
