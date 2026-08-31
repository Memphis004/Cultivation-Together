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
