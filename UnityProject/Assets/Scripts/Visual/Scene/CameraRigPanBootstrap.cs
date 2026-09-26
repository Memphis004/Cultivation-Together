using UnityEngine;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// Wire RigPanInput เฉพาะ play mode (self-activating — แพทเทิร์นเดียวกับ
    /// VisualSpineBootstrap): EditMode test จะไม่มี delegate → CameraRigController
    /// ข้าม pan input ทั้งหมด (ไม่แตะ UnityEngine.Input ใน EditMode)
    /// </summary>
    internal static class CameraRigPanBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void WirePanInput()
        {
            RigPanInput.Wire(
                button => Input.GetMouseButton(button),
                () => Input.mousePosition);
        }
    }
}
