#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Xianxia.Sect.Building;

namespace Xianxia.Sect.EditorTools
{
    /// <summary>
    /// Paint "where may a building go" directly on top of the terrain art, then bake it
    /// into Assets/Scripts/Building/PlaceableLandMask.cs.
    ///
    /// Workflow when the backdrop art changes (new mountain, different floating islands):
    ///   1. drop the new image at Resources/{CameraFramingConfig.MapBackdropPath}.png
    ///   2. Xianxia > Land Mask Painter > Reload art
    ///   3. Auto from art seeds the mask from the painted beige, Prune drops regions that
    ///      cannot host the largest building, then fix the rest by hand
    ///   4. Save mask -> PlaceableLandMask.cs is rewritten and Unity recompiles, so both
    ///      the build overlay and BuildingGrid.CanPlace pick up the new ground
    ///
    /// Painting: left = land, right (or shift+left) = not land. Drag to keep painting,
    /// wheel = zoom, middle-drag (or alt+left) = pan, Ctrl+Z = undo.
    /// </summary>
    public sealed class LandMaskPainterWindow : EditorWindow
    {
        private const int OverlayScale = 4;          // overlay texture = art / 4
        private const int MaxUndo = 24;

        private LandMaskBuilder.Art _art;
        private Vector2 _cellSize = new Vector2(1.28f, 0.66f);
        private bool[,] _mask;                        // [ix, iz], logical cell = FullOrigin + index
        private string _loadError;

        private int _brush = 1;
        private bool _erase;
        private bool _dirty;

        private float _zoom = 10f;
        private Vector2 _center;                       // world point at the canvas centre
        private Rect _canvas;
        private bool _fitPending = true;

        private Texture2D _overlay;
        private Color32[] _overlayPixels;
        private int _overlayW, _overlayH;
        private bool _overlayDirty = true;
        private bool _showLattice = true;
        private Vector2Int _hoverCell;
        private bool _hasHover;

        private readonly List<bool[,]> _undo = new List<bool[,]>();

        [MenuItem("Xianxia/Land Mask Painter")]
        public static void Open()
        {
            var window = GetWindow<LandMaskPainterWindow>("Land Mask");
            window.minSize = new Vector2(640f, 420f);
            window.ReloadAll();
        }

        private void OnDisable()
        {
            ReleaseArt();
            if (_overlay != null) DestroyImmediate(_overlay);
            _overlay = null;
            _overlayPixels = null;
        }

        private void ReleaseArt()
        {
            if (_art != null && _art.Texture != null) DestroyImmediate(_art.Texture);
            _art = null;
        }

        // =====================================================================
        //  load
        // =====================================================================

        private void ReloadAll()
        {
            ReloadArt();
            _mask = LandMaskBuilder.FromBaked();
            _dirty = false;
            _undo.Clear();
            _fitPending = true;
            _overlayDirty = true;
            Repaint();
        }

        private void ReloadArt()
        {
            ReleaseArt();
            _loadError = null;
            if (!LandMaskBuilder.TryLoadArt(out _art, out _loadError))
            {
                Debug.LogWarning("[LandMaskPainter] " + _loadError);
                return;
            }
            _cellSize = LandMaskBuilder.MeasureCellSize();
            // lattice follows the art, so a reworked (bigger/smaller) backdrop still fits
            LandMaskBuilder.ConfigureLattice(_cellSize, _art.WorldWidth, _art.WorldHeight);
            _zoom = Mathf.Min(Screen.width, Screen.height) / 4f;
            if (_overlay != null) DestroyImmediate(_overlay);
            _overlayW = Mathf.Max(64, _art.Width / OverlayScale);
            _overlayH = Mathf.Max(64, _art.Height / OverlayScale);
            _overlay = new Texture2D(_overlayW, _overlayH, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _overlayPixels = new Color32[_overlayW * _overlayH];
            _overlayDirty = true;
        }

        // =====================================================================
        //  gui
        // =====================================================================

        private void OnGUI()
        {
            if (_loadError != null)
            {
                EditorGUILayout.HelpBox(_loadError, MessageType.Error);
                if (GUILayout.Button("Retry", GUILayout.Width(120f))) ReloadAll();
                return;
            }
            if (_art == null) { ReloadAll(); return; }
            if (_mask == null) _mask = LandMaskBuilder.EmptyMask();

            DrawToolbar();
            DrawStatus();

            var rect = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            _canvas = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
            EditorGUI.DrawRect(_canvas, new Color(0.12f, 0.12f, 0.13f, 1f));

            if (Event.current.type == EventType.Layout) return;
            if (_canvas.width < 8f || _canvas.height < 8f) return;

            if (_fitPending)
            {
                FitToArt();
                _fitPending = false;
            }

            HandleInput();
            if (_overlayDirty) RebuildOverlay();

            DrawCanvas();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button("Auto from art", EditorStyles.toolbarButton, GUILayout.Width(96f)))
                        AutoFromArt();
                    if (GUILayout.Button("Prune < 3x3", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                        PruneNow();
                    if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(56f)))
                    {
                        PushUndo();
                        _mask = LandMaskBuilder.EmptyMask();
                        MaskChanged();
                    }
                    if (GUILayout.Button("Invert", EditorStyles.toolbarButton, GUILayout.Width(56f)))
                    {
                        PushUndo();
                        for (int ix = 0; ix < LandMaskBuilder.FullSize; ix++)
                            for (int iz = 0; iz < LandMaskBuilder.FullSize; iz++)
                                _mask[ix, iz] = !_mask[ix, iz];
                        MaskChanged();
                    }
                    GUILayout.Space(8f);
                    if (GUILayout.Button("Undo", EditorStyles.toolbarButton, GUILayout.Width(52f))) Undo();
                    if (GUILayout.Button("Revert", EditorStyles.toolbarButton, GUILayout.Width(58f))) ReloadAll();
                    if (GUILayout.Button("Reload art", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                    {
                        ReloadArt();
                        _fitPending = true;
                    }
                    if (GUILayout.Button("Fit", EditorStyles.toolbarButton, GUILayout.Width(40f))) _fitPending = true;
                }

                GUILayout.FlexibleSpace();
                _showLattice = GUILayout.Toggle(_showLattice, "lattice", EditorStyles.toolbarButton, GUILayout.Width(60f));
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    var label = _dirty ? "Save mask *" : "Save mask";
                    if (GUILayout.Button(label, EditorStyles.toolbarButton, GUILayout.Width(96f))) SaveMask();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _brush = EditorGUILayout.IntSlider(new GUIContent("Brush", "radius in cells"), _brush, 0, 5);
                _erase = EditorGUILayout.ToggleLeft("Erase (right / shift+left also erases)", _erase, GUILayout.Width(280f));
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox(
                    "Exit play mode to paint: saving rewrites PlaceableLandMask.cs and recompiles.",
                    MessageType.Info);
            }
        }

        private void DrawStatus()
        {
            if (_mask == null) return;
            int cells = LandMaskBuilder.CountCells(_mask);
            var regions = LandMaskBuilder.Regions(_mask);
            int fit = 0;
            foreach (var region in regions)
                if (LandMaskBuilder.FitsFootprint(region, _mask, PlaceableLandMask.MinFootprintWidth,
                                                  PlaceableLandMask.MinFootprintHeight)) fit++;

            int extent = LandMaskBuilder.TrimExtent(_mask, out int minX, out int maxX, out int minZ, out int maxZ);
            var text = regions.Count == 0
                ? "empty mask - nothing to place"
                : cells + " land cells · " + regions.Count + " region(s) · " + fit + " can host " +
                  PlaceableLandMask.MinFootprintWidth + "x" + PlaceableLandMask.MinFootprintHeight +
                  " · cell x " + minX + ".." + maxX + " z " + minZ + ".." + maxZ +
                  (extent > 0 ? " -> saves as " + extent + "x" + extent + " (origin " + (-extent / 2) + ")" : "");

            EditorGUILayout.HelpBox(text, regions.Count > 0 && fit < regions.Count ? MessageType.Warning : MessageType.None);
            if (regions.Count > 0 && fit < regions.Count)
            {
                EditorGUILayout.HelpBox(
                    "Some regions are too small to fit the largest building (" +
                    PlaceableLandMask.MinFootprintWidth + "x" + PlaceableLandMask.MinFootprintHeight +
                    ") - press \"Prune < 3x3\" to drop them, or paint them bigger.",
                    MessageType.Warning);
            }
        }

        // =====================================================================
        //  view
        // =====================================================================

        private void FitToArt()
        {
            float sx = _canvas.width / _art.WorldWidth;
            float sy = _canvas.height / _art.WorldHeight;
            _zoom = Mathf.Max(0.5f, Mathf.Min(sx, sy) * 0.97f);
            _center = Vector2.zero;
        }

        private Vector2 WorldToCanvas(Vector2 world)
        {
            return new Vector2(_canvas.center.x + (world.x - _center.x) * _zoom,
                               _canvas.center.y - (world.y - _center.y) * _zoom);
        }

        private Vector2 CanvasToWorld(Vector2 canvas)
        {
            return new Vector2(_center.x + (canvas.x - _canvas.center.x) / _zoom,
                               _center.y - (canvas.y - _canvas.center.y) / _zoom);
        }

        private void DrawCanvas()
        {
            Rect artRect = new Rect(
                WorldToCanvas(new Vector2(-_art.WorldWidth * 0.5f, _art.WorldHeight * 0.5f)).x,
                WorldToCanvas(new Vector2(-_art.WorldWidth * 0.5f, _art.WorldHeight * 0.5f)).y,
                _art.WorldWidth * _zoom,
                _art.WorldHeight * _zoom);

            GUI.BeginClip(_canvas);
            var local = new Rect(artRect.x - _canvas.x, artRect.y - _canvas.y, artRect.width, artRect.height);
            GUI.DrawTexture(local, _art.Texture, ScaleMode.StretchToFill, false);
            if (_overlay != null) GUI.DrawTexture(local, _overlay, ScaleMode.StretchToFill, true);

            // what will actually be saved: the trimmed rect the grid will use
            int extent = LandMaskBuilder.TrimExtent(_mask, out int minX, out int maxX, out int minZ, out int maxZ);
            if (extent > 0)
            {
                int half = extent / 2;
                var a = WorldToCanvas(LandMaskBuilder.CellCenter(-half, -half, _cellSize));
                var b = WorldToCanvas(LandMaskBuilder.CellCenter(-half + extent - 1, -half + extent - 1, _cellSize));
                Handles.BeginGUI();
                Handles.color = new Color(1f, 0.85f, 0.2f, 0.9f);
                Handles.DrawAAPolyLine(2f,
                    new Vector3(a.x - _canvas.x, a.y - _canvas.y, 0f),
                    new Vector3(b.x - _canvas.x, b.y - _canvas.y, 0f));
                Handles.EndGUI();
            }
            GUI.EndClip();

            var hint = "cell " + _cellSize.x.ToString("0.00") + "x" + _cellSize.y.ToString("0.00") +
                       " world · " + _art.Width + "x" + _art.Height + " px @ " + _art.Ppu + " PPU";
            if (_hasHover)
                hint += "   |   cursor cell (" + _hoverCell.x + ", " + _hoverCell.y + ") = " +
                        (_mask[LandMaskBuilder.IndexX(_hoverCell.x), LandMaskBuilder.IndexZ(_hoverCell.y)] ? "land" : "not land");
            GUI.Label(new Rect(_canvas.x + 6f, _canvas.yMax - 18f, _canvas.width - 12f, 16f), hint);
        }

        // =====================================================================
        //  input
        // =====================================================================

        private void HandleInput()
        {
            Event e = Event.current;
            if (!_canvas.Contains(e.mousePosition) && e.type != EventType.MouseDrag) { _hasHover = false; }

            bool panning = e.button == 2 || (e.button == 0 && e.alt);
            switch (e.type)
            {
                case EventType.ScrollWheel:
                    if (_canvas.Contains(e.mousePosition))
                    {
                        Vector2 anchor = CanvasToWorld(e.mousePosition);
                        _zoom = Mathf.Clamp(_zoom * (e.delta.y > 0f ? 0.9f : 1.111f), 0.5f, 400f);
                        Vector2 after = CanvasToWorld(e.mousePosition);
                        _center += anchor - after;
                        e.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseDown:
                    if (_canvas.Contains(e.mousePosition))
                    {
                        if (panning) break;
                        PushUndo();
                        _erase = e.shift || e.button == 1;
                        PaintAt(e.mousePosition);
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (panning && e.button == 2)
                    {
                        _center += new Vector2(-e.delta.x / _zoom, e.delta.y / _zoom);
                        e.Use();
                        Repaint();
                    }
                    else if (e.button == 0 || e.button == 1)
                    {
                        PaintAt(e.mousePosition);
                        e.Use();
                    }
                    break;

                case EventType.KeyDown:
                    if (e.keyCode == KeyCode.Z && (e.control || e.command)) Undo();
                    if (e.keyCode == KeyCode.F) { _fitPending = true; Repaint(); }
                    break;

                case EventType.MouseMove:
                    UpdateHover(e.mousePosition);
                    break;
            }
        }

        private void UpdateHover(Vector2 mouse)
        {
            bool inside = _canvas.Contains(mouse);
            if (!inside)
            {
                if (_hasHover) { _hasHover = false; _overlayDirty = true; Repaint(); }
                return;
            }

            Vector2 world = CanvasToWorld(mouse);
            Vector2Int cell = LandMaskBuilder.WorldToCell(world, _cellSize);
            if (cell.x < LandMaskBuilder.FullOrigin || cell.y < LandMaskBuilder.FullOrigin
                || cell.x >= LandMaskBuilder.FullOrigin + LandMaskBuilder.FullSize
                || cell.y >= LandMaskBuilder.FullOrigin + LandMaskBuilder.FullSize)
            {
                if (_hasHover) { _hasHover = false; _overlayDirty = true; Repaint(); }
                return;
            }

            if (!_hasHover || cell != _hoverCell)
            {
                _hoverCell = cell;
                _hasHover = true;
                _overlayDirty = true;
                Repaint();
            }
        }

        private void PaintAt(Vector2 mouse)
        {
            Vector2 world = CanvasToWorld(mouse);
            Vector2Int cell = LandMaskBuilder.WorldToCell(world, _cellSize);
            bool changed = false;
            float r = _brush + 0.5f;
            for (int dx = -_brush; dx <= _brush; dx++)
            {
                for (int dz = -_brush; dz <= _brush; dz++)
                {
                    if (_brush > 0 && Mathf.Sqrt(dx * dx + dz * dz) > r) continue;
                    int x = cell.x + dx, z = cell.y + dz;
                    int ix = LandMaskBuilder.IndexX(x), iz = LandMaskBuilder.IndexZ(z);
                    if (ix < 0 || iz < 0 || ix >= LandMaskBuilder.FullSize || iz >= LandMaskBuilder.FullSize) continue;
                    bool value = !_erase;
                    if (_mask[ix, iz] == value) continue;
                    _mask[ix, iz] = value;
                    changed = true;
                }
            }

            if (changed)
            {
                MaskChanged();
                Repaint();
            }
        }

        private void MaskChanged()
        {
            _dirty = true;
            _overlayDirty = true;
        }

        private void PushUndo()
        {
            _undo.Add((bool[,])_mask.Clone());
            if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        }

        private void Undo()
        {
            if (_undo.Count == 0) return;
            _mask = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            MaskChanged();
            Repaint();
        }

        // =====================================================================
        //  actions
        // =====================================================================

        private void AutoFromArt()
        {
            PushUndo();
            _mask = LandMaskBuilder.ClassifyFromArt(_art, _cellSize, out int raw);
            int pruned = 0, regions = 0;
            if (raw > 0)
                _mask = LandMaskBuilder.PruneToFootprint(_mask, PlaceableLandMask.MinFootprintWidth,
                                                         PlaceableLandMask.MinFootprintHeight, out pruned, out regions);
            MaskChanged();
            Debug.Log("[LandMaskPainter] auto mask: " + raw + " raw cells, pruned " + pruned +
                      " cell(s) in " + regions + " region(s) too small for " +
                      PlaceableLandMask.MinFootprintWidth + "x" + PlaceableLandMask.MinFootprintHeight +
                      " -> " + LandMaskBuilder.CountCells(_mask) + " cells. Press Save mask to bake.");
            Repaint();
        }

        private void PruneNow()
        {
            PushUndo();
            _mask = LandMaskBuilder.PruneToFootprint(_mask, PlaceableLandMask.MinFootprintWidth,
                                                     PlaceableLandMask.MinFootprintHeight,
                                                     out int dropped, out int regions);
            MaskChanged();
            Debug.Log("[LandMaskPainter] pruned " + dropped + " cell(s) in " + regions +
                      " region(s) -> " + LandMaskBuilder.CountCells(_mask) + " cells left");
            Repaint();
        }

        private void SaveMask()
        {
            int extent = LandMaskBuilder.TrimExtent(_mask, out int minX, out int maxX, out int minZ, out int maxZ);
            if (extent <= 0)
            {
                EditorUtility.DisplayDialog("Land Mask Painter", "The mask is empty - nothing to save.", "OK");
                return;
            }

            string producer = "Painted in the Unity land-mask painter (Xianxia > Land Mask Painter, " +
                              System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")" +
                              " and baked by Assets/Editor/LandMaskBuilder.cs - do not edit by hand.";
            string source = LandMaskBuilder.EmitSource(_mask, extent, producer, _cellSize,
                                                      PlaceableLandMask.MinFootprintWidth,
                                                      PlaceableLandMask.MinFootprintHeight,
                                                      _art.Width, _art.Height, _art.Ppu);
            if (!LandMaskBuilder.Save(source, out string message))
            {
                Debug.LogError("[LandMaskPainter] " + message);
                EditorUtility.DisplayDialog("Land Mask Painter", message, "OK");
                return;
            }

            _dirty = false;
            Debug.Log("[LandMaskPainter] " + message + " - mask " + extent + "x" + extent + ", " +
                      LandMaskBuilder.CountCells(_mask) + " land cells (cells x " + minX + ".." + maxX +
                      ", z " + minZ + ".." + maxZ + ")");
            Repaint();
        }

        // =====================================================================
        //  overlay raster
        // =====================================================================

        private void RebuildOverlay()
        {
            if (_overlay == null || _overlayPixels == null) return;

            var clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < _overlayPixels.Length; i++) _overlayPixels[i] = clear;

            float pxPerUnit = _art.Ppu / OverlayScale;
            float halfW = _cellSize.x * 0.5f * pxPerUnit;
            float halfH = _cellSize.y * 0.5f * pxPerUnit;

            for (int ix = 0; ix < LandMaskBuilder.FullSize; ix++)
            {
                for (int iz = 0; iz < LandMaskBuilder.FullSize; iz++)
                {
                    if (!_mask[ix, iz]) continue;
                    var c = LandMaskBuilder.CellCenter(LandMaskBuilder.CellX(ix), LandMaskBuilder.CellZ(iz), _cellSize);
                    FillDiamond(c, halfW, halfH, new Color32(70, 235, 120, 110));
                }
            }

            if (_showLattice)
            {
                var line = new Color32(255, 255, 255, 55);
                int lo = LandMaskBuilder.FullOrigin;
                int hi = LandMaskBuilder.FullOrigin + LandMaskBuilder.FullSize - 1;
                for (int c = lo; c <= hi; c++)
                {
                    Line(LandMaskBuilder.CellCenter(c, lo, _cellSize), LandMaskBuilder.CellCenter(c, hi, _cellSize), line);
                    Line(LandMaskBuilder.CellCenter(lo, c, _cellSize), LandMaskBuilder.CellCenter(hi, c, _cellSize), line);
                }
            }

            if (_hasHover)
            {
                var c = LandMaskBuilder.CellCenter(_hoverCell.x, _hoverCell.y, _cellSize);
                var tint = _erase ? new Color32(255, 120, 120, 120) : new Color32(255, 255, 255, 120);
                FillDiamond(c, halfW, halfH, tint);
            }

            _overlay.SetPixels32(_overlayPixels);
            _overlay.Apply(false, false);
            _overlayDirty = false;
        }

        private Vector2 OverlayPixel(Vector2 world)
        {
            float pxPerUnit = _art.Ppu / OverlayScale;
            return new Vector2((world.x + _art.WorldWidth * 0.5f) * pxPerUnit,
                               (_art.WorldHeight * 0.5f - world.y) * pxPerUnit);
        }

        private void FillDiamond(Vector2 worldCenter, float halfW, float halfH, Color32 color)
        {
            Vector2 p = OverlayPixel(worldCenter);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(p.x - halfW));
            int x1 = Mathf.Min(_overlayW - 1, Mathf.CeilToInt(p.x + halfW));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(p.y - halfH));
            int y1 = Mathf.Min(_overlayH - 1, Mathf.CeilToInt(p.y + halfH));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float dx = Mathf.Abs((x + 0.5f - p.x) / halfW);
                    float dy = Mathf.Abs((y + 0.5f - p.y) / halfH);
                    if (dx + dy > 1f) continue;
                    _overlayPixels[y * _overlayW + x] = color;
                }
            }
        }

        private void Line(Vector2 worldA, Vector2 worldB, Color32 color)
        {
            Vector2 a = OverlayPixel(worldA);
            Vector2 b = OverlayPixel(worldB);
            int x0 = Mathf.RoundToInt(a.x), y0 = Mathf.RoundToInt(a.y);
            int x1 = Mathf.RoundToInt(b.x), y1 = Mathf.RoundToInt(b.y);
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            int guard = dx + Mathf.Abs(y1 - y0) + 4;
            while (guard-- > 0)
            {
                if (x0 >= 0 && y0 >= 0 && x0 < _overlayW && y0 < _overlayH)
                    _overlayPixels[y0 * _overlayW + x0] = color;
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }
    }
}
#endif
