using UnityEngine;
using Xianxia.Sect.Visual.Spikes;

namespace Xianxia.Sect.Visual.Spines
{
    /// <summary>
    /// Scene launcher for S1: spawns SpineSpikeRunner at play start when this
    /// component exists in the spike scene only (no auto-load — C2).
    /// </summary>
    [ExecuteInEditMode]
    public sealed class SpineSpikeSceneLauncher : MonoBehaviour
    {
        public int InstanceCount = SpineSpikeRunner.DefaultCount;

        private void Start()
        {
            if (Application.isPlaying)
            {
                var runner = gameObject.AddComponent<SpineSpikeRunner>();
                runner.Configure(InstanceCount, 15f, 120); // count, duration=15s, warmup=120 frames
            }
        }
    }
}
