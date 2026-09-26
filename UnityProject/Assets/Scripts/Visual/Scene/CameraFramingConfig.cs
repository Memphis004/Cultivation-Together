using UnityEngine;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// Plain-C# camera framing config (task forbids ScriptableObject for this).
    /// orthoSize is never stored here: it is always computed at runtime from
    /// tileWidthWorld and the live camera.aspect so ultrawide/16:10 behave.
    /// </summary>
    public sealed class CameraFramingConfig
    {
        // --- Presets (tiles visible horizontally, per the reference art) ---
        // Overview was 25 (zoomed out); reskin task 2 brought it down to 12 so the
        // idle camera matches the build-mode zoom of reference image #3.
        public const int OverviewVisibleTiles = 12;
        public const int PlacementVisibleTiles = 10;

        // --- Source-of-truth asset paths (Resources) ---
        // Actual terrain file on disk: mountain1.png (4096x3328 @ PPU 100).
        public const string MapBackdropPath = "tilemap/map/mountain1_wx/mountain1";
        public const string GridSpritePath = "tilemap/tileassets/data/gridblock";

        // --- Backdrop bounds (กัน pan หลุดกรอบภูเขา) — ตั้งโดย TerrainBackdropRenderer
        // หลังโหลด mountain1.png (4096x3328 @ PPU100 = 40.96x33.28 world units, กึ่งกลาง origin)
        public float BackdropWidthWorld { get; private set; }
        public float BackdropHeightWorld { get; private set; }
        public bool HasBackdropBounds { get; private set; }

        public void SetBackdropBounds(float widthWorld, float heightWorld)
        {
            BackdropWidthWorld = widthWorld;
            BackdropHeightWorld = heightWorld;
            HasBackdropBounds = widthWorld > 0f && heightWorld > 0f;
        }

        // --- Runtime-read values, populated by CameraRigController ---
        public float TileWidthWorld { get; private set; }
        public float TileHeightWorld { get; private set; }
        public int GridSpriteWidthPx { get; private set; }
        public int GridSpriteHeightPx { get; private set; }
        public int GridSpritePpu { get; private set; }
        public Vector3 GridCellSize { get; private set; }
        public bool GridIsIsometric { get; private set; }

        public bool IsMeasured { get; private set; }

        public void SetMeasured(
            float tileWidthWorld, float tileHeightWorld,
            int spriteWidthPx, int spriteHeightPx, int ppu,
            Vector3 cellSize, bool isIsometric)
        {
            TileWidthWorld = tileWidthWorld;
            TileHeightWorld = tileHeightWorld;
            GridSpriteWidthPx = spriteWidthPx;
            GridSpriteHeightPx = spriteHeightPx;
            GridSpritePpu = ppu;
            GridCellSize = cellSize;
            GridIsIsometric = isIsometric;
            IsMeasured = true;
        }

        /// <summary>
        /// orthoSize = (tilesVisibleHorizontally * tileWidthWorld) / (2 * aspect).
        /// aspect is read from the live camera so any window shape works.
        /// </summary>
        public float ComputeOrthoSize(int tilesVisibleHorizontally, float cameraAspect)
        {
            float aspect = cameraAspect > 0.01f ? cameraAspect : (16f / 9f);
            float tiles = Mathf.Max(1f, tilesVisibleHorizontally);
            return tiles * TileWidthWorld / (2f * aspect);
        }
    }
}
