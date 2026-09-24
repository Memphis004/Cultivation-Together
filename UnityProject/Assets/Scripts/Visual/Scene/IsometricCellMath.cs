using UnityEngine;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// Pure isometric cell math shared by GridOverlayRenderer and EditMode
    /// tests. 2:1 diamond mapping:
    ///   center(cell) = origin + ((x - y) * w/2, (x + y) * h/2)
    /// and picking uses the standard inverse floor division:
    ///   x = floor(dx/w + dy/h), y = floor(dy/h - dx/w).
    /// Kept static/stateless because the project does not use
    /// InternalsVisibleTo, so tests cannot reach the renderer's privates
    /// directly - the renderer delegates to these exact methods instead.
    /// </summary>
    public static class IsometricCellMath
    {
        /// <summary>World position of the center of an isometric cell.</summary>
        public static Vector3 CellCenterToWorld(Vector2Int cell, Vector2 origin, float tileWidthWorld, float tileHeightWorld)
        {
            return new Vector3(
                origin.x + (cell.x - cell.y) * (tileWidthWorld * 0.5f),
                origin.y + (cell.x + cell.y) * (tileHeightWorld * 0.5f),
                0f);
        }

        /// <summary>Inverse pick: which isometric cell contains a world point.</summary>
        public static Vector2Int WorldToCell(Vector3 world, Vector2 origin, float tileWidthWorld, float tileHeightWorld)
        {
            float dx = world.x - origin.x;
            float dy = world.y - origin.y;
            int x = Mathf.FloorToInt(dx / tileWidthWorld + dy / tileHeightWorld);
            int y = Mathf.FloorToInt(dy / tileHeightWorld - dx / tileWidthWorld);
            return new Vector2Int(x, y);
        }
    }
}
