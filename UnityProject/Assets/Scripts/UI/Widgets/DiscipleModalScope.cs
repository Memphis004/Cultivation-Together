using UnityEngine;
namespace Xianxia.Sect.UI
{
    public sealed class DiscipleModalScope : MonoBehaviour
    {
        private static int count;
        public static bool IsOpen => count > 0;

        /// <summary>
        /// Marks a blocking modal as open. OnEnable/OnDisable are the production path; they
        /// are the ONLY thing that must pair with <see cref="PopBlocker"/>.
        ///
        /// Why this is public: Unity only fires OnEnable/OnDisable in the editor for
        /// [ExecuteAlways] components, so an EditMode test cannot make a real modal count
        /// itself open. Pushing the same counter keeps one source of truth for "is a modal
        /// blocking the screen" (E3 uses it for the popup/chip interplay).
        /// </summary>
        public static void PushBlocker() => count++;

        public static void PopBlocker() => count = Mathf.Max(0, count - 1);

        private void OnEnable() => PushBlocker();

        private void LateUpdate()
        {
            var window = transform.Find("Window") as RectTransform;
            var canvas = GetComponentInParent<Canvas>();
            if (window == null || canvas == null) return;
            var canvasRect = (RectTransform)canvas.rootCanvas.transform;
            float scale = Mathf.Min(1f, (canvasRect.rect.height - 220f) / 790f, (canvasRect.rect.width - 48f) / 1400f);
            window.localScale = Vector3.one * Mathf.Max(.1f, scale);
        }

        private void OnDisable() => PopBlocker();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetVisibilityCount() => count = 0;
    }
}
