---
title: LogWindowView.cs
type: snippet
source: UnityProject/Assets/Scripts/UI/Views/LogWindowView.cs
related:
  - "[[concepts/log-window]]"
  - "[[concepts/mvp-ui]]"
created: 2026-09-01
updated: 2026-09-01
confidence: high
tags: [ui, view, log, scroll, ugui, tmp]
---

# LogWindowView.cs

> Pure visual component — a scrolling text log. Each `AddLine()` call
> instantiates a `TMP_Text` entry under a `ScrollRect`, with FIFO
> eviction past `maxEntries`.

**Path**: `UnityProject/Assets/Scripts/UI/Views/LogWindowView.cs`
**Namespace**: `Xianxia.Sect.UI`

## Public API

| Member | Signature | Notes |
|---|---|---|
| `AddLine` | `void AddLine(string text)` | Instantiates entry, enqueues, evicts oldest if over cap, auto-scrolls |

## Serialized Fields

| Field | Type | Default | Purpose |
|---|---|---|---|
| `content` | `RectTransform` | — | Parent container for instantiated entries |
| `logEntryPrefab` | `TMP_Text` | — | Prefab cloned per entry |
| `scrollRect` | `ScrollRect` | — | Auto-scrolls to bottom on new entry |
| `maxEntries` | `int` | 50 | Max visible entries before FIFO eviction |

## Dependencies

| Dependency | Purpose |
|---|---|
| `TMPro.TMP_Text` | Entry prefab (TextMeshPro text component) |
| `UnityEngine.UI.ScrollRect` | Scroll container |
| `UIViewBase` | Base class (Show/Hide via `SetActive`) |

## Code

```csharp
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
```

## Key Implementation Detail

`Canvas.ForceUpdateCanvases()` is called before
`scrollRect.verticalNormalizedPosition = 0f`. Without this, the scroll
rect reads stale content height (before the new entry's layout pass).

## Known Issues

- None currently. See [[sources/bug-log]] for historical bugs.
