#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.EditorTools
{
    /// <summary>
    /// Logic behind the land-mask painter (see <see cref="LandMaskPainterWindow"/>).
    ///
    /// It mirrors Tools/art/bake_land_mask.py 1:1 so the window and the CLI baker
    /// produce interchangeable output, and it is the only code path that writes
    /// Assets/Scripts/Building/PlaceableLandMask.cs when the art is re-masked.
    ///
    /// Why an editor tool at all: the runtime cannot read the backdrop (imported with
    /// isReadable:0), so the per-cell "where can a building go" answer has to be baked
    /// data. This builder turns the painted art into that data:
    ///
    ///   1. ground = the flat warm beige fill of the plateau tops. The beige also floods
    ///      the plane *outside* the islands, so the beige is labelled with 8-connectivity
    ///      and only components that do NOT touch the image border are kept.
    ///   2. a cell is land when at least LAND_FRACTION of a 13-point lattice inside its
    ///      diamond samples land (mirrors the bake script).
    ///   3. a land region only counts if it can host the game's largest building
    ///      footprint (3x3 `pill_hall` in building_defs.json) - slivers over a canyon and
    ///      single cells floating in the mist drop out with it.
    ///
    /// Cell/index conventions (must stay in sync with the runtime):
    ///   world centre of cell (x,z) = ((x - z) * cellW / 2, (x + z) * cellH / 2)
    ///   the backdrop image is centred on the world origin
    ///   mask columns run x = OriginX .. OriginX+Width-1, rows run z likewise
    /// </summary>
    public static class LandMaskBuilder
    {
        /// <summary>Mask rect on disk, relative to the Assets folder.</summary>
        public const string MaskAssetPath = "Assets/Scripts/Building/PlaceableLandMask.cs";

        // --- lattice that can cover the whole backdrop ---
        // The image is an axis-aligned rectangle in world space, but a cell range is a
        // square in *cell* space - rotated 45 degrees against it. The image corners
        // therefore reach out to |x|,|z| ~ 41 for the current 40.96x33.28 world art, not
        // ~20. Call ConfigureLattice(art) after loading a backdrop so a different image
        // size (a reworked mountain) still gets a lattice that covers all of it; the
        // defaults below are what the 4096x3328 art needs.
        private static int _fullOrigin = -42;
        private static int _fullSize = 84;

        public static int FullOrigin => _fullOrigin;
        public static int FullSize => _fullSize;

        /// <summary>
        /// Sizes the paint lattice so every cell whose diamond can touch the image is
        /// reachable: the far corner of the rectangle is at world
        /// (±W/2, ±H/2), which is cell (W/2/cellW + H/2/cellH) in x.
        /// </summary>
        public static void ConfigureLattice(Vector2 cellSize, float worldWidth, float worldHeight)
        {
            float maxCell = worldWidth / (2f * cellSize.x) + worldHeight / (2f * cellSize.y);
            int half = Mathf.Max(1, Mathf.CeilToInt(maxCell));
            _fullOrigin = -half;
            _fullSize = half * 2;
        }

        // --- classification thresholds (keep in sync with bake_land_mask.py) ---
        public const int BeigeR = 222, BeigeG = 214, BeigeB = 200;
        public const int BeigeToleranceR = 6, BeigeToleranceG = 6, BeigeToleranceB = 8;
        public const int MinComponentPixels = 2000;

        // --- cell sampling (mirrors the bake script) ---
        public const int SampleSteps = 5;
        public const float SampleInset = 0.9f;
        public const float LandFraction = 0.85f;

        /// <summary>Cells of margin kept around the land bounding box when trimming.</summary>
        public const int TrimMargin = 1;

        // =====================================================================
        //  art
        // =====================================================================

        public sealed class Art
        {
            public Texture2D Texture;
            public Color32[] Pixels;   // row 0 = bottom of the image, columns left to right
            public int Width;
            public int Height;
            public float Ppu;
            public float WorldWidth;
            public float WorldHeight;
            public string AssetPath;
        }

        /// <summary>
        /// Reads the backdrop PNG as a CPU-readable texture. The runtime import has
        /// isReadable:0, so the bytes are decoded here instead of reading the imported
        /// Texture2D (which would throw).
        /// </summary>
        public static bool TryLoadArt(out Art art, out string error)
        {
            art = null;
            error = null;

            string assetPath = "Assets/Resources/" + CameraFramingConfig.MapBackdropPath + ".png";
            if (!File.Exists(assetPath))
            {
                error = "backdrop not found: " + assetPath;
                return false;
            }

            byte[] bytes = File.ReadAllBytes(assetPath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                error = "could not decode " + assetPath + " as an image";
                Object.DestroyImmediate(tex);
                return false;
            }

            float ppu = 100f;
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null && importer.spritePixelsPerUnit > 0f)
                ppu = importer.spritePixelsPerUnit;

            art = new Art
            {
                Texture = tex,
                Pixels = tex.GetPixels32(),
                Width = tex.width,
                Height = tex.height,
                Ppu = ppu,
                WorldWidth = tex.width / ppu,
                WorldHeight = tex.height / ppu,
                AssetPath = assetPath,
            };
            return true;
        }

        /// <summary>Cell size in world units, measured off the gridblock sprite (same
        /// source CameraRigController measures at runtime).</summary>
        public static Vector2 MeasureCellSize()
        {
            var sprite = Resources.Load<Sprite>(CameraFramingConfig.GridSpritePath);
            if (sprite != null && sprite.pixelsPerUnit > 0f
                && sprite.rect.width > 0f && sprite.rect.height > 0f)
            {
                return new Vector2(sprite.rect.width / sprite.pixelsPerUnit,
                                   sprite.rect.height / sprite.pixelsPerUnit);
            }

            Debug.LogWarning("[LandMaskBuilder] gridblock sprite not found at Resources/" +
                             CameraFramingConfig.GridSpritePath +
                             " - falling back to 1.28x0.66 (128x66 px @ PPU 100)");
            return new Vector2(1.28f, 0.66f);
        }

        // =====================================================================
        //  mask maths
        // =====================================================================

        public static Vector2 CellCenter(int x, int z, Vector2 cellSize)
        {
            return new Vector2((x - z) * cellSize.x * 0.5f, (x + z) * cellSize.y * 0.5f);
        }

        public static Vector2Int WorldToCell(Vector2 world, Vector2 cellSize)
        {
            int x = Mathf.FloorToInt(world.x / cellSize.x + world.y / cellSize.y);
            int z = Mathf.FloorToInt(world.y / cellSize.y - world.x / cellSize.x);
            return new Vector2Int(x, z);
        }

        public static bool[,] EmptyMask()
        {
            return new bool[FullSize, FullSize];
        }

        public static int CountCells(bool[,] mask)
        {
            int n = 0;
            for (int ix = 0; ix < mask.GetLength(0); ix++)
                for (int iz = 0; iz < mask.GetLength(1); iz++)
                    if (mask[ix, iz]) n++;
            return n;
        }

        /// <summary>Logical cell x of column ix.</summary>
        public static int CellX(int ix) => FullOrigin + ix;
        public static int CellZ(int iz) => FullOrigin + iz;
        public static int IndexX(int x) => x - FullOrigin;
        public static int IndexZ(int z) => z - FullOrigin;

        /// <summary>8-connected regions of a mask (used by both the prune rule and the stats).</summary>
        public static List<List<Vector2Int>> Regions(bool[,] mask)
        {
            int size = mask.GetLength(0);
            var regions = new List<List<Vector2Int>>();
            var seen = new bool[size, size];
            var stack = new Stack<Vector2Int>();
            for (int ix = 0; ix < size; ix++)
            {
                for (int iz = 0; iz < size; iz++)
                {
                    if (seen[ix, iz] || !mask[ix, iz]) continue;
                    var region = new List<Vector2Int>();
                    stack.Push(new Vector2Int(ix, iz));
                    seen[ix, iz] = true;
                    while (stack.Count > 0)
                    {
                        var c = stack.Pop();
                        region.Add(c);
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            for (int dz = -1; dz <= 1; dz++)
                            {
                                if (dx == 0 && dz == 0) continue;
                                int nx = c.x + dx, nz = c.y + dz;
                                if (nx < 0 || nz < 0 || nx >= size || nz >= size) continue;
                                if (seen[nx, nz] || !mask[nx, nz]) continue;
                                seen[nx, nz] = true;
                                stack.Push(new Vector2Int(nx, nz));
                            }
                        }
                    }
                    regions.Add(region);
                }
            }
            return regions;
        }

        /// <summary>Can this region hold an fw x fh all-land block anywhere?</summary>
        public static bool FitsFootprint(List<Vector2Int> region, bool[,] mask, int fw, int fh)
        {
            int size = mask.GetLength(0);
            foreach (var c in region)
            {
                bool block = true;
                for (int dz = 0; dz < fh && block; dz++)
                    for (int dx = 0; dx < fw && block; dx++)
                    {
                        int nx = c.x + dx, nz = c.y + dz;
                        if (nx >= size || nz >= size || !mask[nx, nz]) block = false;
                    }
                if (block) return true;
            }
            return false;
        }

        /// <summary>Drops every region that cannot host the largest footprint. Returns a new mask.</summary>
        public static bool[,] PruneToFootprint(bool[,] mask, int fw, int fh, out int dropped, out int droppedRegions)
        {
            var keep = new bool[mask.GetLength(0), mask.GetLength(1)];
            dropped = 0;
            droppedRegions = 0;
            foreach (var region in Regions(mask))
            {
                if (FitsFootprint(region, mask, fw, fh))
                {
                    foreach (var c in region) keep[c.x, c.y] = true;
                }
                else
                {
                    dropped += region.Count;
                    droppedRegions++;
                }
            }
            return keep;
        }

        /// <summary>
        /// The square grid the mask is emitted on: land bounding box + margin, rounded
        /// out to a centred even square (origin -extent/2). Mirrors the bake script's
        /// trim() - GridOverlayRenderer.GridExtent and the production BuildingGrid both
        /// follow PlaceableLandMask.Width, so this is what sizes the whole build grid.
        /// </summary>
        public static int TrimExtent(bool[,] mask, out int minX, out int maxX, out int minZ, out int maxZ)
        {
            minX = int.MaxValue; maxX = int.MinValue;
            minZ = int.MaxValue; maxZ = int.MinValue;
            for (int ix = 0; ix < mask.GetLength(0); ix++)
            {
                for (int iz = 0; iz < mask.GetLength(1); iz++)
                {
                    if (!mask[ix, iz]) continue;
                    int x = CellX(ix), z = CellZ(iz);
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (z < minZ) minZ = z;
                    if (z > maxZ) maxZ = z;
                }
            }
            if (minX == int.MaxValue) return 0; // nothing painted

            minX -= TrimMargin; maxX += TrimMargin;
            minZ -= TrimMargin; maxZ += TrimMargin;
            return 2 * Mathf.Max(Mathf.Max(Mathf.Abs(minX), Mathf.Abs(maxX)),
                                 Mathf.Max(Mathf.Abs(minZ), Mathf.Abs(maxZ)));
        }

        // =====================================================================
        //  classification (mirrors Tools/art/bake_land_mask.py)
        // =====================================================================

        public static bool[,] ClassifyFromArt(Art art, Vector2 cellSize, out int rawCells)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            bool[] beige = BeigePixels(art);
            bool[] enclosed = EnclosedComponents(beige, art.Width, art.Height);

            var mask = new bool[FullSize, FullSize];
            var offsets = SampleOffsets(cellSize);
            for (int ix = 0; ix < FullSize; ix++)
            {
                for (int iz = 0; iz < FullSize; iz++)
                {
                    Vector2 c = CellCenter(CellX(ix), CellZ(iz), cellSize);
                    int hits = 0;
                    for (int i = 0; i < offsets.Length; i++)
                    {
                        if (SampleLand(art, enclosed, c.x + offsets[i].x, c.y + offsets[i].y)) hits++;
                    }
                    mask[ix, iz] = hits >= LandFraction * offsets.Length - 1e-6f;
                }
            }

            rawCells = CountCells(mask);
            Debug.Log("[LandMaskBuilder] classified " + art.AssetPath + " (" + art.Width + "x" + art.Height +
                      " px @ " + art.Ppu + " PPU) -> " + rawCells + " raw land cells in " +
                      stopwatch.ElapsedMilliseconds + " ms");
            return mask;
        }

        private static bool[] BeigePixels(Art art)
        {
            var beige = new bool[art.Pixels.Length];
            for (int i = 0; i < art.Pixels.Length; i++)
            {
                Color32 p = art.Pixels[i];
                beige[i] = Mathf.Abs(p.r - BeigeR) <= BeigeToleranceR
                           && Mathf.Abs(p.g - BeigeG) <= BeigeToleranceG
                           && Mathf.Abs(p.b - BeigeB) <= BeigeToleranceB;
            }
            return beige;
        }

        /// <summary>
        /// The beige of the plateau tops: every component that is at least
        /// <see cref="MinComponentPixels"/> big and does NOT touch the image border. The
        /// huge border-touching component is the plane outside the islands.
        /// </summary>
        private static bool[] EnclosedComponents(bool[] beige, int width, int height)
        {
            var labels = new int[beige.Length];
            for (int i = 0; i < labels.Length; i++) labels[i] = -1;

            var sizes = new List<int>();
            var touchesBorder = new List<bool>();
            var queue = new Queue<int>();
            for (int seed = 0; seed < beige.Length; seed++)
            {
                if (!beige[seed] || labels[seed] >= 0) continue;
                int label = sizes.Count;
                int size = 0;
                bool border = false;
                queue.Enqueue(seed);
                labels[seed] = label;
                while (queue.Count > 0)
                {
                    int i = queue.Dequeue();
                    size++;
                    int x = i % width;
                    int y = i / width;
                    if (x == 0 || y == 0 || x == width - 1 || y == height - 1) border = true;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int ny = y + dy;
                        if (ny < 0 || ny >= height) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx;
                            if (nx < 0 || nx >= width) continue;
                            if (dx == 0 && dy == 0) continue;
                            int ni = ny * width + nx;
                            if (!beige[ni] || labels[ni] >= 0) continue;
                            labels[ni] = label;
                            queue.Enqueue(ni);
                        }
                    }
                }
                sizes.Add(size);
                touchesBorder.Add(border);
            }

            var enclosed = new bool[beige.Length];
            for (int i = 0; i < beige.Length; i++)
            {
                int l = labels[i];
                if (l < 0) continue;
                enclosed[i] = sizes[l] >= MinComponentPixels && !touchesBorder[l];
            }
            return enclosed;
        }

        private static Vector2[] SampleOffsets(Vector2 cellSize)
        {
            var offsets = new List<Vector2>();
            for (int i = 0; i < SampleSteps; i++)
            {
                float dx = 2f * i / (SampleSteps - 1) - 1f;
                for (int j = 0; j < SampleSteps; j++)
                {
                    float dy = 2f * j / (SampleSteps - 1) - 1f;
                    if (Mathf.Abs(dx) + Mathf.Abs(dy) > 1f) continue;
                    offsets.Add(new Vector2(dx * cellSize.x * 0.5f * SampleInset,
                                            dy * cellSize.y * 0.5f * SampleInset));
                }
            }
            return offsets.ToArray();
        }

        /// <summary>World point -> backdrop pixel (row 0 = bottom) -> is it enclosed land?</summary>
        private static bool SampleLand(Art art, bool[] enclosed, float wx, float wy)
        {
            int col = Mathf.RoundToInt((wx + art.WorldWidth * 0.5f) * art.Ppu);
            int row = Mathf.RoundToInt((wy + art.WorldHeight * 0.5f) * art.Ppu);
            if (col < 0 || row < 0 || col >= art.Width || row >= art.Height) return false;
            return enclosed[row * art.Width + col];
        }

        // =====================================================================
        //  emit
        // =====================================================================

        /// <summary>
        /// The generated C# file. Layout is byte-compatible with the bake script so a
        /// hand-painted mask and a CLI bake can be swapped without the runtime noticing
        /// (GridOverlayRenderer/BuildingSystem only consume the members emitted here).
        /// </summary>
        public static string EmitSource(bool[,] mask, int extent, string producer,
                                        Vector2 cellSize, int footprintW, int footprintH,
                                        int artWidthPx, int artHeightPx, float artPpu)
        {
            int half = extent / 2;
            int landCount = 0;
            var rows = new string[extent];
            int maskSize = mask.GetLength(0);
            for (int iz = 0; iz < extent; iz++)
            {
                var sb = new StringBuilder(extent);
                for (int ix = 0; ix < extent; ix++)
                {
                    int x = -half + ix, z = -half + iz;
                    bool land = false;
                    int fx = IndexX(x), fz = IndexZ(z);
                    if (fx >= 0 && fz >= 0 && fx < maskSize && fz < maskSize) land = mask[fx, fz];
                    sb.Append(land ? '#' : '.');
                    if (land) landCount++;
                }
                rows[iz] = sb.ToString();
            }

            var body = new StringBuilder();
            for (int iz = 0; iz < extent; iz++)
                body.Append("            \"").Append(rows[iz]).Append("\",\n");

            string source = "Assets/Resources/" + CameraFramingConfig.MapBackdropPath + ".png";
            return
                "// <auto-generated>\n" +
                "//   " + producer + "\n" +
                "//   Regenerate after the terrain art or the painted mask changes:\n" +
                "//     Unity:  Xianxia > Land Mask Painter   (paint + Save)\n" +
                "//     CLI:    python Tools/art/bake_land_mask.py\n" +
                "//\n" +
                "//   Per-cell \"land\" mask of the painted backdrop\n" +
                "//   " + source + " (" + artWidthPx + "x" + artHeightPx + " px @ PPU " +
                artPpu.ToString("0.##") + " = " + (artWidthPx / artPpu).ToString("0.00") + "x" +
                (artHeightPx / artPpu).ToString("0.00") + " world units, centred on the origin).\n" +
                "//   Land = the flat warm beige ground (rgb 222,214,200) of the plateau tops.\n" +
                "//   Fog banks, rock, trees, the beige plane *outside* the islands and everything\n" +
                "//   off-image are NOT land, so both the build-mode overlay and BuildingGrid.CanPlace\n" +
                "//   stay on the islands instead of spilling over the fog.\n" +
                "//\n" +
                "//   Two filters:\n" +
                "//     * \"on an island\" - the beige is labelled with 8-connectivity and only the\n" +
                "//       components that do NOT touch the image border are kept (the outer plane is\n" +
                "//       one huge border-touching component).\n" +
                "//     * \"worth building on\" - the cell mask is labelled again and a region is kept\n" +
                "//       only if it can host the largest footprint in the game, " + footprintW + "x" + footprintH +
                " cells\n" +
                "//       (pill_hall in building_defs.json). Slivers hanging over a canyon and single\n" +
                "//       cells floating in the mist are dropped with it.\n" +
                "//\n" +
                "//   Cell size / mapping mirror CameraFramingConfig + IsometricCellMath:\n" +
                "//     centre(x,z) = ((x - z) * " + cellSize.x.ToString("0.00") + "/2, (x + z) * " +
                cellSize.y.ToString("0.00") + "/2)\n" +
                "// </auto-generated>\n" +
                "namespace Xianxia.Sect.Building\n" +
                "{\n" +
                "    /// <summary>\n" +
                "    /// Baked placeable-land mask - " + landCount + " of " + (extent * extent) + " cells are land.\n" +
                "    /// Row index = z - <see cref=\"OriginY\"/>, column index = x - <see cref=\"OriginX\"/>.\n" +
                "    /// The rect matches GridOverlayRenderer.GridExtent and the production BuildingGrid.\n" +
                "    /// </summary>\n" +
                "    public static class PlaceableLandMask\n" +
                "    {\n" +
                "        public const string SourceImage = \"" + source + "\";\n" +
                "\n" +
                "        /// <summary>Logical cell of Rows[0][0] (matches the isometric cell grid).</summary>\n" +
                "        public const int OriginX = " + (-half) + ";\n" +
                "        public const int OriginY = " + (-half) + ";\n" +
                "\n" +
                "        public const int Width = " + extent + ";\n" +
                "        public const int Height = " + extent + ";\n" +
                "\n" +
                "        public const char LandChar = '#';\n" +
                "\n" +
                "        /// <summary>Number of land cells in the whole mask.</summary>\n" +
                "        public const int LandCellCount = " + landCount + ";\n" +
                "\n" +
                "        /// <summary>\n" +
                "        /// Largest building footprint the bake required a land region to fit\n" +
                "        /// (pill_hall in building_defs.json). Kept in sync by the generator.\n" +
                "        /// </summary>\n" +
                "        public const int MinFootprintWidth = " + footprintW + ";\n" +
                "        public const int MinFootprintHeight = " + footprintH + ";\n" +
                "\n" +
                "        private static readonly string[] Rows =\n" +
                "        {\n" +
                body +
                "        };\n" +
                "\n" +
                "        /// <summary>Cell is plateau top surface (mist / rock / tree / off-image = false).</summary>\n" +
                "        public static bool IsLand(int x, int z)\n" +
                "        {\n" +
                "            int ix = x - OriginX;\n" +
                "            int iz = z - OriginY;\n" +
                "            if (ix < 0 || iz < 0 || ix >= Width || iz >= Height) return false;\n" +
                "            return Rows[iz][ix] == LandChar;\n" +
                "        }\n" +
                "\n" +
                "        /// <summary>\n" +
                "        /// Row-major (index = (z - originZ) * width + (x - originX)) bool block for\n" +
                "        /// an arbitrary rect of logical cells - the shape BuildingGrid.SetPlaceableMask\n" +
                "        /// takes. Cells outside the mask bounds are false (fail-closed).\n" +
                "        /// </summary>\n" +
                "        public static bool[] BuildCells(int originX, int originZ, int width, int height)\n" +
                "        {\n" +
                "            var cells = new bool[width * height];\n" +
                "            for (int iz = 0; iz < height; iz++)\n" +
                "            {\n" +
                "                for (int ix = 0; ix < width; ix++)\n" +
                "                {\n" +
                "                    cells[iz * width + ix] = IsLand(originX + ix, originZ + iz);\n" +
                "                }\n" +
                "            }\n" +
                "            return cells;\n" +
                "        }\n" +
                "    }\n" +
                "}\n";
        }

        /// <summary>Writes the generated file and lets Unity import + recompile it.</summary>
        public static bool Save(string source, out string message)
        {
            message = null;
            try
            {
                File.WriteAllText(MaskAssetPath, source, new UTF8Encoding(false));
            }
            catch (IOException e)
            {
                message = "could not write " + MaskAssetPath + ": " + e.Message;
                return false;
            }

            AssetDatabase.ImportAsset(MaskAssetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
            message = "wrote " + MaskAssetPath;
            return true;
        }

        /// <summary>Cells comparable to a mask file, so a painter result can be diffed against the bake.</summary>
        public static bool[,] FromBaked()
        {
            var mask = EmptyMask();
            for (int ix = 0; ix < FullSize; ix++)
                for (int iz = 0; iz < FullSize; iz++)
                    mask[ix, iz] = Xianxia.Sect.Building.PlaceableLandMask.IsLand(CellX(ix), CellZ(iz));
            return mask;
        }

        /// <summary>Counts cells that differ between two masks (sanity check helper).</summary>
        public static int Diff(bool[,] a, bool[,] b)
        {
            int n = 0;
            for (int ix = 0; ix < a.GetLength(0); ix++)
                for (int iz = 0; iz < a.GetLength(1); iz++)
                    if (a[ix, iz] != b[ix, iz]) n++;
            return n;
        }
    }
}
#endif
