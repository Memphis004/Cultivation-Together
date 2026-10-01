using UnityEngine;
using UnityEngine.UI;

namespace Xianxia.Sect.UI
{
    /// <summary>
    /// GridLayoutGroup that sizes its cells so exactly <see cref="Columns"/>
    /// columns fill the available width (viewport), preserving the card aspect
    /// for the height. Recomputes on EVERY canvas layout pass, so it
    /// self-corrects after window/resolution changes with no manual refresh
    /// timing (a plain GridLayoutGroup + one-shot size math goes stale when the
    /// rect is laid out after the first Awake).
    ///
    /// OWN FILE on purpose (moved out of DiscipleListView.cs — missing-script
    /// fix, Task B): Unity only creates a MonoScript sub-asset for a
    /// MonoBehaviour whose class name matches the file name. A secondary class
    /// inside another .cs serializes into the prefab as m_Script: {fileID: 0},
    /// i.e. a MISSING SCRIPT on the Content object (found by
    /// DiscipleListMissingScriptScan: Content: missing=1).
    /// </summary>
    public sealed class FitWidthGridLayoutGroup : GridLayoutGroup
    {
        public int Columns = DiscipleListView.GridColumns;
        public float SpacingValue = DiscipleListView.GridSpacing;
        public float CellAspect = DiscipleListView.CardAspect;
        public float MinCellWidth = 60f;
        public float MaxCellWidth = 320f;

        /// <summary>
        /// Task D: when the viewport is tall enough, stretch the cell height so
        /// exactly <see cref="RowsInView"/> rows fill it (reference art: a 6x2
        /// grid filling the window — viewport 624 → 304-tall rows instead of
        /// 253, so no dead strip under row 2 and no half-cut row 3). Cells never
        /// shrink below the card-aspect height, and rows beyond the second just
        /// scroll (ScrollRect.vertical is on).
        /// </summary>
        public int RowsInView = DiscipleListView.GridRowsInView;

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();

            spacing = new Vector2(SpacingValue, SpacingValue);

            float width = rectTransform.rect.width; // grid is stretch-anchored inside the viewport
            if (width <= 1f || Columns <= 0) return; // not laid out yet; next pass fixes it

            float pad = padding.left + padding.right;
            float cellW = Mathf.Floor(
                (width - pad - SpacingValue * (Columns - 1)) / Columns);
            cellW = Mathf.Clamp(cellW, MinCellWidth, MaxCellWidth);

            float cellH = Mathf.Floor(cellW * CellAspect);

            // Task D: fill the viewport height with exactly RowsInView rows.
            // The card's portrait row is flexible-height, so the extra height
            // lands on the icon (never squashes the name/stat text).
            if (RowsInView >= 2 && transform.parent is RectTransform viewport &&
                viewport.rect.height > 1f)
            {
                float vPad = padding.top + padding.bottom;
                float fitH = Mathf.Floor(
                    (viewport.rect.height - vPad - SpacingValue * (RowsInView - 1)) / RowsInView);
                if (fitH > cellH) cellH = fitH;
            }

            cellSize = new Vector2(cellW, cellH);
        }
    }
}
