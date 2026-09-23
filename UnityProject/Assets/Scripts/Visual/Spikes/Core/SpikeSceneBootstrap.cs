using System;
using UnityEngine;

namespace Xianxia.Sect.Visual.Spikes
{
    /// <summary>
    /// Phase-0 spike shared helpers (no Spine dependency — Core, C3).
    /// Measurement runners live in SpikeRunners.cs; each spike scene carries
    /// its own SpikeSceneLauncher / SpineSpikeSceneLauncher component, so
    /// nothing auto-loads anywhere (C2) and no reflection is used (C6).
    /// </summary>
    public class SpikeSceneBootstrap : MonoBehaviour
    {
        public static readonly string[] SlotNames = { "hair_back", "body", "head", "face_marking", "hair_front", "accessory" };

        public static float CameraHalfHeight { get { return 6f; } }
        public static float CameraHalfWidth { get { return 6f * 16f / 9f; } }

        public static void EnsureSceneCamera()
        {
            if (Camera.main != null) return;
            var camGo = new GameObject("Spike Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.14f, 0.18f, 1f);
            cam.orthographic = true;
            cam.orthographicSize = CameraHalfHeight;
            cam.transform.position = new Vector3(0f, 1.5f, -10f);
            camGo.AddComponent<AudioListener>();
        }
    }

    /// <summary>Marks a spike scene so the Editor constraint verifier can find it. Inert.</summary>
    public sealed class SpikeSceneMarker : MonoBehaviour { }
}
