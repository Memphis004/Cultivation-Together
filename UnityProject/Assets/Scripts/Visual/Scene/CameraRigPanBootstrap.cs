using UnityEngine;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// Wire RigPanInput + RigZoomInput เฉพาะ play mode (self-activating —
    /// แพทเทิร์นเดียวกับ VisualSpineBootstrap): EditMode test จะไม่มี delegate →
    /// CameraRigController ข้าม pan/zoom input ทั้งหมด (ไม่แตะ UnityEngine.Input ใน EditMode)
    /// </summary>
    internal static class CameraRigPanBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void WireRigInputs()
        {
            RigPanInput.Wire(
                button => Input.GetMouseButton(button),
                () => Input.mousePosition);
            RigZoomInput.Wire(() => Input.mouseScrollDelta.y);
        }
    }
}
