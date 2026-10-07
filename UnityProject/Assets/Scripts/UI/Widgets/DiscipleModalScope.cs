using UnityEngine;
namespace Xianxia.Sect.UI
{
    public sealed class DiscipleModalScope : MonoBehaviour
    {
        private static int count;
        public static bool IsOpen => count > 0;
        private void OnEnable() => count++;
        private void OnDisable() => count = Mathf.Max(0, count - 1);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => count = 0;
    }
}
