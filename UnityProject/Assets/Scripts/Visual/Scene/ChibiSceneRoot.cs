using UnityEngine;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// Named "work spot" for a task family (Phase 4). taskType is matched as a
    /// PREFIX of DiscipleState.CurrentTask (e.g. "gathering" matches
    /// "gathering_herb", "meditation" matches "meditation") — same prefix
    /// convention as chibi_activity_task_map.json. Positions are art/level-design
    /// decisions, NOT procedural — scenes ship this array EMPTY until humans place
    /// spots; everything then falls back to the grid (open question, spec §11).
    /// </summary>
    [System.Serializable]
    public class TaskAnchor
    {
        public string taskType = string.Empty;
        public Vector3 offset; // world-space offset from the root's position
    }

    /// <summary>
    /// Gameplay-scene anchor for chibis (plan §5, C3). GameplayScene has no
    /// LifetimeScope of its own, so this MonoBehaviour cannot be constructor-
    /// injected — DiscipleVisualSystem finds it by SCANNING the loaded scene's
    /// root objects on SceneLoadedMessage (same pattern as
    /// SceneLoader.RemoveDuplicateSingletons), never FindObjectOfType (C12).
    ///
    /// CoreScene scenes don't have one; every gameplay scene must include a
    /// GameObject with this component at its root.
    /// </summary>
    public class ChibiSceneRoot : MonoBehaviour
    {
        [Header("Chibi spawn anchor")]
        [Tooltip("Local offset from the root where the chibi grid starts.")]
        [SerializeField] private Vector3 spawnOrigin = new Vector3(-8f, 0f, 0f);
        [Tooltip("Grid spacing between chibis (world units).")]
        [SerializeField] private Vector2 spacing = new Vector2(1.5f, 1.0f);
        [Tooltip("Chibis per row before wrapping.")]
        [SerializeField] private int perRow = 10;
        [Tooltip("Starting sorting order for the lowest chibi layer.")]
        [SerializeField] private int sortingBase = 0;

        [Header("Work anchors (Phase 4 — optional, art-decided)")]
        [Tooltip("Named spots per task family. Empty = everything uses the grid.")]
        [SerializeField] private TaskAnchor[] taskAnchors = new TaskAnchor[0];

        public Vector3 SpawnOrigin { get { return spawnOrigin; } }
        public Vector2 Spacing { get { return spacing; } }
        public int PerRow { get { return perRow; } }
        public int SortingBase { get { return sortingBase; } }

        /// <summary>World position for chibi #index in the grid.</summary>
        public Vector3 PositionFor(int index)
        {
            int row = index / Mathf.Max(1, PerRow);
            int col = index % Mathf.Max(1, PerRow);
            return transform.position + spawnOrigin + new Vector3(col * spacing.x, row * spacing.y, 0f);
        }

        /// <summary>
        /// Phase 4 — additive work-anchor lookup: longest taskType that is a prefix
        /// of the disciple's CurrentTask wins. Returns false when no anchor matches
        /// (or the array is empty) → caller keeps the plain PositionFor(index) grid
        /// slot. Pure additive — grid behavior is untouched.
        /// </summary>
        public bool TryGetWorkAnchor(string currentTask, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            if (string.IsNullOrEmpty(currentTask) || taskAnchors == null) return false;

            TaskAnchor best = null;
            for (int i = 0; i < taskAnchors.Length; i++)
            {
                var t = taskAnchors[i];
                if (t == null || string.IsNullOrEmpty(t.taskType)) continue;
                if (currentTask.StartsWith(t.taskType, System.StringComparison.Ordinal) &&
                    (best == null || t.taskType.Length > best.taskType.Length))
                {
                    best = t;
                }
            }
            if (best == null) return false;

            worldPos = transform.position + best.offset;
            return true;
        }
    }
}
