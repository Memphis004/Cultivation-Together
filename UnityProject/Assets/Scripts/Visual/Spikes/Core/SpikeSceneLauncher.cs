using UnityEngine;

namespace Xianxia.Sect.Visual.Spikes
{
    /// <summary>
    /// Scene launcher for S2/S3 scenes: configures and starts the runner placed
    /// on the same GameObject. Kept in its own file so scene files reference a
    /// stable script GUID (missing-script risk when multiple MonoBehaviours
    /// share one .cs file whose GUID Unity re-slots after recompile).
    /// </summary>
    [ExecuteInEditMode]
    public sealed class SpikeSceneLauncher : MonoBehaviour
    {
        public int SpriteInstanceCount = SpriteLayerSpikeRunner.DefaultCount;
        public bool SpawnSizeProbesInstead = false;

        private void Start()
        {
            if (Application.isPlaying)
            {
                if (SpawnSizeProbesInstead) return; // S3 scene spawns probes via its own component
                AddRunner();
            }
        }

        private void AddRunner()
        {
            var runner = gameObject.AddComponent<SpriteLayerSpikeRunner>();
            runner.Configure(SpriteInstanceCount, 120, SpriteLayerSpikeRunner.MeasureSecondsAt60Fps);
        }
    }
}
