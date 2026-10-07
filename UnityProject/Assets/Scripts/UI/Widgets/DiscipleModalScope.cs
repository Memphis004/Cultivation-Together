using UnityEngine;
namespace Xianxia.Sect.UI
{
    public sealed class DiscipleModalScope : MonoBehaviour
    {
        private static int count;
        public static bool IsOpen => count > 0;
        private void OnEnable() => count++;
        private void LateUpdate()
        {
            var window = transform.Find("Window") as RectTransform;
            var canvas = GetComponentInParent<Canvas>();
            if (window == null || canvas == null) return;
            var canvasRect = (RectTransform)canvas.rootCanvas.transform;
            float scale = Mathf.Min(1f, (canvasRect.rect.height - 220f) / 790f, (canvasRect.rect.width - 48f) / 1400f);
            window.localScale = Vector3.one * Mathf.Max(.1f, scale);
        }
        private void OnDisable() => count = Mathf.Max(0, count - 1);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetVisibilityCount() => count = 0;
    }
}
