using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// One animation state of the chibi, loaded from Resources/Data/chibi_anim.json (L4 —
    /// state names/fps/frame-counts live in DATA, not code; adding a state never touches C#).
    /// </summary>
    [Serializable]
    public class ChibiAnimStateDef
    {
        public string state;   // e.g. "Idle", "Walk" — data-driven (L4/L6)
        public int fps;
        public int frames;
        public bool loop;
    }

    [Serializable]
    public class ChibiAnimTable
    {
        public List<ChibiAnimStateDef> states = new List<ChibiAnimStateDef>();
    }

    /// <summary>
    /// ChibiFrameBank (plan §6.2) — (partId, state, dir) → Sprite[].
    /// Sheets are loaded by the path convention authored in avatar_parts.json
    /// (chibiSheetPath / chibiSheetPathBack under Resources/) and sliced on the
    /// shared grid: one row per anim state (row order = chibi_anim.json order),
    /// 6 cells per row, 96×96 per cell, feet-center pivot (C7/L7).
    /// Direction: sheets are authored facing RIGHT; "left" is the same sprites
    /// flipped at the parent pivot (C8 — no separate left sheets), so the bank
    /// keys frames by (partId, state) only.
    /// Plain C# singleton — registered in GameLifetimeScope.
    /// </summary>
    public sealed class ChibiFrameBank
    {
        public const int CellSize = 96;          // S3 candidate cell (L2 — flagged: pending human sign-off)
        public const string FallbackState = "Idle"; // L9 spirit: unknown state → Idle

        private const string AnimResourcePath = "Data/chibi_anim";

        // partId -> (state -> frames); state strips sliced from the sheet rows
        private readonly Dictionary<string, Dictionary<string, Sprite[]>> _byPart =
            new Dictionary<string, Dictionary<string, Sprite[]>>(StringComparer.Ordinal);

        // resourcePath -> sheet texture (one PNG can back main + back sheets is not
        // possible, but the same path may be requested twice — main and via JSON)
        private readonly HashSet<string> _loadedPaths = new HashSet<string>(StringComparer.Ordinal);

        private Dictionary<string, ChibiAnimStateDef> _states;
        private List<string> _stateOrder;

        public ChibiFrameBank()
        {
            LoadAnimTable();
        }

        public ChibiAnimStateDef GetState(string stateName)
        {
            var s = stateName ?? string.Empty;
            if (_states.TryGetValue(s, out var def)) return def;
            if (_states.TryGetValue(FallbackState, out var fb)) return fb;
            return null;
        }

        public IReadOnlyList<string> StateOrder
        {
            get { return _stateOrder; }
        }

        /// <summary>Frame count usable for a part+state (0 = part has no sheet).</summary>
        public int FrameCount(string partId, string stateName)
        {
            var def = GetState(stateName);
            if (def == null) return 0;
            return def.frames;
        }

        /// <summary>
        /// All frames of a part for one state, or null if the part has no sheet
        /// (caller decides: skip layer / fall back to slot default — resolver's job).
        /// </summary>
        public Sprite[] GetFrames(string partId, string stateName)
        {
            Dictionary<string, Sprite[]> byState;
            if (!_byPart.TryGetValue(partId ?? string.Empty, out byState)) return null;

            Sprite[] frames;
            if (byState.TryGetValue(stateName ?? string.Empty, out frames)) return frames;
            return byState.TryGetValue(FallbackState, out frames) ? frames : null;
        }

        /// <summary>
        /// Load one sheet PNG (Resources path from avatar_parts.json) and slice it
        /// into per-state strips. Idempotent — repeat loads of the same path are no-ops.
        /// Safe to call for paths that don't exist (logs once, stores nothing).
        /// </summary>
        public void LoadSheet(string resourcePath, string partId)
        {
            if (string.IsNullOrEmpty(resourcePath) || string.IsNullOrEmpty(partId)) return;
            if (!_loadedPaths.Add(resourcePath)) return;

            var tex = Resources.Load<Texture2D>(resourcePath);
            if (tex == null)
            {
                Debug.LogWarning("[ChibiFrameBank] chibi sheet not found: Resources/" + resourcePath +
                                 " (part '" + partId + "') — slot will be skipped on chibi");
                return;
            }

            SliceSheet(tex, resourcePath, partId);
        }

#if UNITY_INCLUDE_TESTS
        /// <summary>Test-only hook — inject a synthetic texture instead of loading from Resources.
        /// public เพราะ test asmdef แยกจาก runtime assembly และโปรเจกต์ไม่ใช้ InternalsVisibleTo</summary>
        public void LoadSheetForTest(Texture2D tex, string partId)
        {
            SliceSheet(tex, "test://" + partId, partId);
        }
#endif

        private void SliceSheet(Texture2D tex, string resourcePath, string partId)
        {
            int rows = _stateOrder.Count;
            int cell = CellSize;

            if (tex.width < cell || tex.height < rows * cell)
            {
                Debug.LogWarning("[ChibiFrameBank] sheet '" + resourcePath + "' is " + tex.width + "x" +
                                 tex.height + " — expected >= " + cell + "x" + (rows * cell) +
                                 " (" + rows + " state rows x " + cell + "px). Frames will be mis-sliced.");
            }

            var byState = new Dictionary<string, Sprite[]>(StringComparer.Ordinal);
            for (int row = 0; row < rows && row * cell < tex.height; row++)
            {
                string stateName = _stateOrder[row];
                var def = _states[stateName];
                int frames = Mathf.Min(def.frames, FramesPerRow(tex.width, cell));

                var strip = new Sprite[frames];
                // Unity texture Y is bottom-up; row 0 (first state) is authored at the TOP.
                float yTop = tex.height - row * cell;
                for (int f = 0; f < frames; f++)
                {
                    var rect = new Rect(f * cell, yTop - cell, cell, cell);
                    var sprite = Sprite.Create(tex, rect, new Vector2(0.5f, 0f), // feet-center pivot (C7)
                        (float)cell, 0, SpriteMeshType.FullRect);
                    sprite.name = partId + "_" + stateName + "_" + f;
                    strip[f] = sprite;
                }
                byState[stateName] = strip;
            }

            _byPart[partId] = byState;
        }

        private static int FramesPerRow(int texWidth, int cell)
        {
            return Mathf.Max(1, texWidth / cell);
        }

        private void LoadAnimTable()
        {
            _states = new Dictionary<string, ChibiAnimStateDef>(StringComparer.Ordinal);
            _stateOrder = new List<string>();

            var asset = Resources.Load<TextAsset>(AnimResourcePath);
            if (asset == null)
            {
                Debug.LogError("[ChibiFrameBank] missing Resources/" + AnimResourcePath +
                               ".json — chibi anim states unavailable, no sheet will slice");
                return;
            }

            try
            {
                var table = JsonUtility.FromJson<ChibiAnimTable>(asset.text);
                if (table != null && table.states != null)
                {
                    for (int i = 0; i < table.states.Count; i++)
                    {
                        var s = table.states[i];
                        if (string.IsNullOrEmpty(s.state)) continue;
                        s.frames = Mathf.Max(1, s.frames);
                        s.fps = Mathf.Max(1, s.fps);
                        if (_states.ContainsKey(s.state))
                        {
                            Debug.LogWarning("[ChibiFrameBank] duplicate state '" + s.state + "' in chibi_anim.json — keeping first");
                            continue;
                        }
                        _states[s.state] = s;
                        _stateOrder.Add(s.state);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[ChibiFrameBank] chibi_anim.json parse failed: " + ex.Message);
            }

            Debug.Log("[ChibiFrameBank] loaded " + _states.Count + " chibi anim states: " +
                      string.Join(", ", _stateOrder.ToArray()));
        }
    }
}
