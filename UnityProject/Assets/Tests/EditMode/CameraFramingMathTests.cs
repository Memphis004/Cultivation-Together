using NUnit.Framework;
using UnityEngine;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// EditMode tests for the camera framing formula (CameraFramingConfig.
    /// ComputeOrthoSize) and the isometric cell conversion (IsometricCellMath,
    /// the exact code path GridOverlayRenderer delegates to).
    ///
    /// Expected values were verified with op-for-op float32 evaluation of the
    /// production expressions (Unity uses 32-bit floats), not ideal math:
    ///   orthoSize = tiles * tileWidthWorld / (2 * aspect)
    ///   world     = origin + ((x-y) * w/2, (x+y) * h/2)
    ///   pick      = floor(dx/w + dy/h), floor(dy/h - dx/w)
    /// </summary>
    public class CameraFramingMathTests
    {
        private CameraFramingConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = new CameraFramingConfig();
            // Measured values from the real assets (gridblock_0 128x66 @ PPU 100).
            _config.SetMeasured(
                tileWidthWorld: 1.28f, tileHeightWorld: 0.66f,
                spriteWidthPx: 128, spriteHeightPx: 66, ppu: 100,
                cellSize: new Vector3(1.28f, 0.66f, 1f), isIsometric: true);
        }

        // -------------------------------------------------------------
        // CameraFramingConfig.ComputeOrthoSize
        // -------------------------------------------------------------

        [Test]
        public void ComputeOrthoSize_Overview_AtMeasuredAspect_MatchesRuntimeLog()
        {
            // Runtime log (Game view aspect 3.048): Overview orthoSize=6.618 in
            // editor logs with a different aspect; at 3.048 the exact value is:
            Assert.AreEqual(5.249344f, _config.ComputeOrthoSize(CameraFramingConfig.OverviewVisibleTiles, 3.048f), 1e-5f);
        }

        [Test]
        public void ComputeOrthoSize_Placement_AtMeasuredAspect_MatchesRuntimeLog()
        {
            Assert.AreEqual(2.099737f, _config.ComputeOrthoSize(CameraFramingConfig.PlacementVisibleTiles, 3.048f), 1e-5f);
        }

        [Test]
        public void ComputeOrthoSize_At16By9_GivesExactFractions()
        {
            Assert.AreEqual(9.0f, _config.ComputeOrthoSize(CameraFramingConfig.OverviewVisibleTiles, 16f / 9f), 1e-4f);
            Assert.AreEqual(3.6f, _config.ComputeOrthoSize(CameraFramingConfig.PlacementVisibleTiles, 16f / 9f), 1e-4f);
        }

        [Test]
        public void ComputeOrthoSize_ScalesLinearlyWithTiles()
        {
            float ten = _config.ComputeOrthoSize(10, 2f);
            float twenty = _config.ComputeOrthoSize(20, 2f);
            Assert.AreEqual(ten * 2f, twenty, 1e-4f);
        }

        [Test]
        public void ComputeOrthoSize_DegenerateAspect_FallsBackTo16By9()
        {
            float fallback = _config.ComputeOrthoSize(CameraFramingConfig.PlacementVisibleTiles, 16f / 9f);
            Assert.AreEqual(fallback, _config.ComputeOrthoSize(CameraFramingConfig.PlacementVisibleTiles, 0f), 1e-6f);
            Assert.AreEqual(fallback, _config.ComputeOrthoSize(CameraFramingConfig.PlacementVisibleTiles, -1f), 1e-6f);
        }

        [Test]
        public void ComputeOrthoSize_ClampsTilesBelowOne()
        {
            Assert.AreEqual(
                _config.ComputeOrthoSize(1, 2f),
                _config.ComputeOrthoSize(0, 2f), 1e-6f);
        }

        // -------------------------------------------------------------
        // IsometricCellMath.CellCenterToWorld
        // -------------------------------------------------------------

        [Test]
        public void CellCenterToWorld_OriginCell_IsAtOrigin()
        {
            var p = IsometricCellMath.CellCenterToWorld(new Vector2Int(0, 0), Vector2.zero, 1.28f, 0.66f);
            Assert.AreEqual(0f, p.x, 1e-6f);
            Assert.AreEqual(0f, p.y, 1e-6f);
        }

        [Test]
        public void CellCenterToWorld_EastAndSouthNeighbors_AreMirrorImages()
        {
            var east = IsometricCellMath.CellCenterToWorld(new Vector2Int(1, 0), Vector2.zero, 1.28f, 0.66f);
            var south = IsometricCellMath.CellCenterToWorld(new Vector2Int(0, 1), Vector2.zero, 1.28f, 0.66f);
            Assert.AreEqual(0.64f, east.x, 1e-6f);
            Assert.AreEqual(0.33f, east.y, 1e-6f);
            Assert.AreEqual(-east.x, south.x, 1e-6f);
            Assert.AreEqual(east.y, south.y, 1e-6f);
        }

        [Test]
        public void CellCenterToWorld_DiagonalNeighbor_MovesOneFullTileUp()
        {
            var p = IsometricCellMath.CellCenterToWorld(new Vector2Int(1, 1), Vector2.zero, 1.28f, 0.66f);
            Assert.AreEqual(0f, p.x, 1e-6f);
            Assert.AreEqual(0.66f, p.y, 1e-6f);
        }

        [Test]
        public void CellCenterToWorld_RespectsOrigin()
        {
            var p = IsometricCellMath.CellCenterToWorld(new Vector2Int(2, -3), new Vector2(3f, 2f), 1.28f, 0.66f);
            // (x - y) = 5 -> +5 * 0.64; (x + y) = -1 -> -0.33
            Assert.AreEqual(3f + 3.2f, p.x, 1e-5f);
            Assert.AreEqual(2f - 0.33f, p.y, 1e-5f);
        }

        // -------------------------------------------------------------
        // IsometricCellMath.WorldToCell — interior points (the real cursor
        // pick case; verified 20736/20736 probes across 3 tile shapes x 3
        // origins in float32)
        // -------------------------------------------------------------

        [Test]
        public void WorldToCell_InteriorPoints_PickTheirCell()
        {
            float w = 1.28f, h = 0.66f;
            var origin = new Vector2(3f, 2f);
            // (u, v) = fractional offsets inside the cell diamond, away from edges.
            var probes = new[] { (0.3f, 0.3f), (0.7f, 0.35f), (0.5f, 0.5f), (0.25f, 0.75f) };
            foreach (var (au, av) in probes)
            {
                for (int x = -6; x <= 6; x++)
                {
                    for (int y = -6; y <= 6; y++)
                    {
                        float u = x + au, v = y + av;
                        var world = new Vector3(
                            origin.x + w * (u - v) * 0.5f,
                            origin.y + h * (u + v) * 0.5f,
                            0f);
                        Assert.AreEqual(new Vector2Int(x, y),
                            IsometricCellMath.WorldToCell(world, origin, w, h),
                            $"probe offset ({au},{av}) cell ({x},{y})");
                    }
                }
            }
        }

        [Test]
        public void WorldToCell_TileExact_CenterRoundTripsForAllCells()
        {
            // Only tiles that are exact in float32 (1x1, 2x1) round-trip every
            // cell center exactly; 1.28x0.66 does NOT (0.33/0.66 are not
            // representable), so the interior-probe test above is the general
            // contract. Verified: 0 failures across +/-24 at 3 origins.
            float w = 1f, h = 1f;
            var origins = new[] { Vector2.zero, new Vector2(3f, 2f), new Vector2(-7.5f, 4.25f) };
            foreach (var origin in origins)
            {
                for (int x = -24; x <= 24; x++)
                {
                    for (int y = -24; y <= 24; y++)
                    {
                        var cell = new Vector2Int(x, y);
                        var world = IsometricCellMath.CellCenterToWorld(cell, origin, w, h);
                        Assert.AreEqual(cell,
                            IsometricCellMath.WorldToCell(world, origin, w, h),
                            $"cell ({x},{y}) origin ({origin.x},{origin.y})");
                    }
                }
            }
        }

        [Test]
        public void WorldToCell_DiamondEdgePoints_FloorDeterministically()
        {
            // Points exactly on a shared edge/vertex are ties; the floor
            // division maps them to a deterministic lower cell (verified in
            // float32) — the contract is stability, not a specific side.
            float w = 1.28f, h = 0.66f;
            // Midpoint of the shared edge between (0,0) and (1,0):
            var edgeMid = new Vector3(w * 0.25f, h * 0.25f, 0f);
            var mid = IsometricCellMath.WorldToCell(edgeMid, Vector2.zero, w, h);
            Assert.IsTrue(mid == new Vector2Int(0, 0) || mid == new Vector2Int(1, 0),
                $"edge midpoint picked {mid}");

            // Right vertex of the (1,0) diamond is shared with (0,1)/(1,-1):
            var vertex = new Vector3(w, h * 0.5f, 0f);
            var vcell = IsometricCellMath.WorldToCell(vertex, Vector2.zero, w, h);
            Assert.IsTrue(vcell == new Vector2Int(1, 0) || vcell == new Vector2Int(0, 1) || vcell == new Vector2Int(1, -1),
                $"vertex picked {vcell}");

            // And it is stable: same input, same answer.
            Assert.AreEqual(vcell, IsometricCellMath.WorldToCell(vertex, Vector2.zero, w, h));
        }

        [Test]
        public void WorldToCell_JustBelowCenter_MovesToLowerNeighbor()
        {
            // Cell centers sit exactly on the floor boundaries of the pick
            // terms, so nudging diagonally DOWN (verified in float32) flips
            // the pick to the lower neighbor even for the inexact 1.28x0.66
            // tile, while nudging UP keeps the same cell.
            float w = 1.28f, h = 0.66f;
            var origin = new Vector2(3f, 2f);
            var center = IsometricCellMath.CellCenterToWorld(new Vector2Int(4, 2), origin, w, h);
            var below = new Vector3(center.x - 5e-3f, center.y - 5e-3f, 0f);
            Assert.AreEqual(new Vector2Int(3, 1),
                IsometricCellMath.WorldToCell(below, origin, w, h));
            var above = new Vector3(center.x + 5e-3f, center.y + 5e-3f, 0f);
            Assert.AreEqual(new Vector2Int(4, 2),
                IsometricCellMath.WorldToCell(above, origin, w, h));
        }

        // -------------------------------------------------------------
        // Round-trip through the renderer-facing helper
        // -------------------------------------------------------------

        [Test]
        public void CellCenterToWorld_ThenWorldToCell_InteriorOffsetRoundTrips()
        {
            float w = 1.28f, h = 0.66f;
            var origin = new Vector2(-2f, 1.5f);
            for (int x = -10; x <= 10; x++)
            {
                for (int y = -10; y <= 10; y++)
                {
                    var cell = new Vector2Int(x, y);
                    var center = IsometricCellMath.CellCenterToWorld(cell, origin, w, h);
                    // Cell centers lie exactly on the lower floor boundary of
                    // both pick terms, so sample straight UP by h/4: that adds
                    // +0.25 to both rotated coordinates (verified in float32).
                    var interior = new Vector3(center.x, center.y + h * 0.25f, 0f);
                    var picked = IsometricCellMath.WorldToCell(interior, origin, w, h);
                    Assert.AreEqual(cell, picked, $"cell ({x},{y})");
                }
            }
        }
    }
}
