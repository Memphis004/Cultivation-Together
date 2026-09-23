using System;
using UnityEngine;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// Phase 4 (§11) — data-driven `DiscipleState.CurrentTask → ChibiActivity`
    /// resolver, loaded from Resources/Data/chibi_activity_task_map.json
    /// (same JsonUtility + Resources.Load convention as ChibiFrameBank's
    /// chibi_anim.json / ChibiActivityMap's chibi_activity_map.json — L4:
    /// the mapping lives in DATA, adding a task prefix never touches C#).
    ///
    /// Longest-prefix match wins (so a future "gathering_herb_special" can map
    /// differently from "gathering_"); unmatched task → defaultActivity with a
    /// warn once per distinct task id (same L9 warn-once pattern as
    /// ChibiActivityMap / SpriteChibiVisual).
    ///
    /// ⚠️ NOTE: spec-mandated TODO — if task-system-v2 lands with
    /// DiscipleTaskChangedMessage, activity derivation should SUBSCRIBE to that
    /// message instead of re-resolving on Reconcile() (poll-on-reconcile is the
    /// current contract because task assignment does not exist yet; see
    /// LLMWiki/wiki/sources/open-questions.md #11). Do not build that plumbing here.
    /// </summary>
    public sealed class TaskActivityMapper
    {
        private const string ResourcePath = "Data/chibi_activity_task_map";

        [Serializable]
        private class MappingEntry
        {
            public string taskPrefix = string.Empty;
            public string activity = string.Empty;
        }

        [Serializable]
        private class Table
        {
            public MappingEntry[] mappings = new MappingEntry[0];
            public string defaultActivity = "Idle";
        }

        private readonly MappingEntry[] _mappings;
        private readonly string _defaultActivity;
        private readonly System.Collections.Generic.HashSet<string> _warnedTasks =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        public TaskActivityMapper()
        {
            var table = new Table();
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset != null)
            {
                try
                {
                    table = JsonUtility.FromJson<Table>(asset.text);
                    if (table == null) table = new Table();
                }
                catch (Exception ex)
                {
                    Debug.LogError("[TaskActivityMapper] chibi_activity_task_map.json parse failed: " + ex.Message);
                    table = new Table();
                }
            }
            else
            {
                // Table file missing entirely → warn once; everything falls to Idle
                // (same graceful behavior as an empty mappings array).
                Debug.LogWarning("[TaskActivityMapper] chibi_activity_task_map.json not found at Resources/" +
                                 ResourcePath + " — every CurrentTask resolves to 'Idle'");
            }

            _mappings = table.mappings ?? new MappingEntry[0];
            _defaultActivity = string.IsNullOrEmpty(table.defaultActivity) ? "Idle" : table.defaultActivity;

            Debug.Log("[TaskActivityMapper] loaded " + _mappings.Length +
                      " task-prefix mappings (default: " + _defaultActivity + ")");
        }

        /// <summary>
        /// Resolve a ChibiActivity name for the given CurrentTask string.
        /// Longest-prefix match wins; unmatched → default (warn once per distinct task).
        /// Pure function of the loaded table — no Unity API beyond the ctor load.
        /// </summary>
        public string ResolveActivity(string currentTask)
        {
            if (string.IsNullOrEmpty(currentTask)) return _defaultActivity;

            MappingEntry best = null;
            for (int i = 0; i < _mappings.Length; i++)
            {
                var prefix = _mappings[i].taskPrefix;
                if (string.IsNullOrEmpty(prefix)) continue;
                if (currentTask.StartsWith(prefix, StringComparison.Ordinal) &&
                    (best == null || prefix.Length > best.taskPrefix.Length))
                {
                    best = _mappings[i];
                }
            }

            if (best != null) return best.activity;

            if (_warnedTasks.Add(currentTask))
            {
                Debug.LogWarning("[TaskActivityMapper] no mapping for CurrentTask '" + currentTask +
                                 "' — using defaultActivity '" + _defaultActivity + "'");
            }
            return _defaultActivity;
        }
    }
}
