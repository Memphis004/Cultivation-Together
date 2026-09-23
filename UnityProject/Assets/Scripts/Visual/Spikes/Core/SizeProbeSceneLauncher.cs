using UnityEngine;

namespace Xianxia.Sect.Visual.Spikes
{
    /// <summary>Scene launcher for S3 (size probe only).</summary>
    [ExecuteInEditMode]
    public sealed class SizeProbeSceneLauncher : MonoBehaviour
    {
        private void Start()
        {
            if (Application.isPlaying)
            {
                gameObject.AddComponent<CellSizeProbeRunner>();
            }
        }
    }
}
