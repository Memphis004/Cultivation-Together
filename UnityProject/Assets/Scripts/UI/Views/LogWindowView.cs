using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    // Scrolling text log - each AddLine() call spawns one entry, oldest
    // entries get destroyed past maxEntries so this doesn't grow forever
    // over a long play session.
    public class LogWindowView : UIViewBase
    {
        [SerializeField] private RectTransform content;
        [SerializeField] private TMP_Text logEntryPrefab;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private int maxEntries = 50;

        // Data-driven layout (open question #13) - moved verbatim from
        // UIRoot.ApplyLogWindowLayout.
        public override void ApplyDefaultLayout()
        {
            var rt = GetComponent<RectTransform>();
            if (rt == null) return;
            // Bottom-left anchored log window: 400x300
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot     = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(400f, 300f);
            rt.anchoredPosition = new Vector2(10f, 10f);
        }

        private readonly Queue<GameObject> _entries = new Queue<GameObject>();

        public void AddLine(string text)
        {
            if (content == null || logEntryPrefab == null) return;

            var entry = Instantiate(logEntryPrefab, content);
            entry.text = text;
            _entries.Enqueue(entry.gameObject);

            while (_entries.Count > maxEntries)
            {
                var oldest = _entries.Dequeue();
                if (oldest != null) Destroy(oldest);
            }

            if (scrollRect != null)
            {
                // Force the layout to rebuild before snapping to bottom,
                // otherwise this reads the content height from before the
                // new entry was added.
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }
    }
}
