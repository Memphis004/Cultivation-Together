using NUnit.Framework;
using Xianxia.Sect.Building;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// PlaceableLandMask คือข้อมูลที่ bake จากภาพ mountain1.png (Tools/art/bake_land_mask.py)
    /// แล้วใช้ร่วมกันโดย GridOverlayRenderer (วาด cell ไหน) กับ BuildingSystem
    /// (BuildingGrid.SetPlaceableMask → CanPlace). เทสต์ชุดนี้กันไม่ให้สามที่นี้หลุดกัน
    /// และกันไม่ให้ mask กลายเป็น "ทั้ง grid วางได้" โดยไม่ตั้งใจ
    /// </summary>
    public class PlaceableLandMaskTests
    {
        [Test]
        public void MaskRect_MatchesTheOverlayGridRect()
        {
            Assert.AreEqual(GridOverlayRenderer.GridExtent, PlaceableLandMask.Width,
                "mask ต้องกว้างเท่า grid overlay");
            Assert.AreEqual(GridOverlayRenderer.GridExtent, PlaceableLandMask.Height,
                "mask ต้องสูงเท่า grid overlay");
            Assert.AreEqual(-GridOverlayRenderer.GridExtent / 2, PlaceableLandMask.OriginX);
            Assert.AreEqual(-GridOverlayRenderer.GridExtent / 2, PlaceableLandMask.OriginY);
        }

        [Test]
        public void MaskRect_MatchesTheProductionBuildingGridRect()
        {
            // เหมือนที่ BuildingInstaller register ไว้เป๊ะ ๆ
            var grid = new BuildingGrid(
                GridOverlayRenderer.GridExtent, GridOverlayRenderer.GridExtent,
                -GridOverlayRenderer.GridExtent / 2, -GridOverlayRenderer.GridExtent / 2);

            Assert.AreEqual(grid.OriginX, PlaceableLandMask.OriginX, "origin x ต้องตรงกับ grid");
            Assert.AreEqual(grid.OriginY, PlaceableLandMask.OriginY, "origin y ต้องตรงกับ grid");
            Assert.AreEqual(grid.Width, PlaceableLandMask.Width);
            Assert.AreEqual(grid.Height, PlaceableLandMask.Height);
        }

        [Test]
        public void LandCellCount_MatchesTheMaskRows()
        {
            int count = 0;
            for (int z = PlaceableLandMask.OriginY; z < PlaceableLandMask.OriginY + PlaceableLandMask.Height; z++)
            {
                for (int x = PlaceableLandMask.OriginX; x < PlaceableLandMask.OriginX + PlaceableLandMask.Width; x++)
                {
                    if (PlaceableLandMask.IsLand(x, z)) count++;
                }
            }

            Assert.AreEqual(PlaceableLandMask.LandCellCount, count,
                "LandCellCount ต้องตรงกับ Rows จริง (ถ้าไม่ตรง = ไฟล์ generated เสีย)");
        }

        [Test]
        public void IsLand_OutsideTheMask_IsFalse_FailClosed()
        {
            Assert.IsFalse(PlaceableLandMask.IsLand(PlaceableLandMask.OriginX - 1, 0));
            Assert.IsFalse(PlaceableLandMask.IsLand(0, PlaceableLandMask.OriginY - 1));
            Assert.IsFalse(PlaceableLandMask.IsLand(PlaceableLandMask.OriginX + PlaceableLandMask.Width, 0));
            Assert.IsFalse(PlaceableLandMask.IsLand(0, PlaceableLandMask.OriginY + PlaceableLandMask.Height));
            Assert.IsFalse(PlaceableLandMask.IsLand(9999, -9999));
        }

        [Test]
        public void LandCellCount_LeavesRoomForMistAndRock()
        {
            int total = PlaceableLandMask.Width * PlaceableLandMask.Height;

            Assert.Greater(PlaceableLandMask.LandCellCount, 0, "ต้องมี land อย่างน้อยหนึ่ง cell");
            Assert.Less(PlaceableLandMask.LandCellCount, total / 4,
                "หลังเกาะต้องเป็นส่วนน้อยของกรอบ grid — ถ้าเกิน 25% แปลว่า mask " +
                "ไปนับหมอกเป็น land (ตรวจ threshold ใน Tools/art/bake_land_mask.py)");
        }

        [Test]
        public void BuildCells_IsRowMajor_AndMatchesIsLand()
        {
            var cells = PlaceableLandMask.BuildCells(
                PlaceableLandMask.OriginX, PlaceableLandMask.OriginY,
                PlaceableLandMask.Width, PlaceableLandMask.Height);

            Assert.AreEqual(PlaceableLandMask.Width * PlaceableLandMask.Height, cells.Length);

            for (int iz = 0; iz < PlaceableLandMask.Height; iz++)
            {
                for (int ix = 0; ix < PlaceableLandMask.Width; ix++)
                {
                    Assert.AreEqual(
                        PlaceableLandMask.IsLand(PlaceableLandMask.OriginX + ix, PlaceableLandMask.OriginY + iz),
                        cells[iz * PlaceableLandMask.Width + ix],
                        $"index {(iz * PlaceableLandMask.Width + ix)} (x={PlaceableLandMask.OriginX + ix}, z={PlaceableLandMask.OriginY + iz})");
                }
            }
        }

        [Test]
        public void EveryLandRegion_FitsTheLargestBuildingFootprint()
        {
            // กติกาที่ bake ใช้ตัดพื้นที่ทิ้ง: ผืนที่วางได้ต้องรับ footprint ใหญ่สุดของเกมได้
            // (pill_hall 3x3 — ดู Tools/art/bake_land_mask.py) ถ้ามีชิ้นเล็ก ๆ หลุดเข้ามา
            // (เช่น cell เดียวกลางหมอก หรือเศษขอบเกาะ) เทสต์นี้จะฟ้อง
            int w = PlaceableLandMask.MinFootprintWidth;
            int h = PlaceableLandMask.MinFootprintHeight;
            var seen = new bool[PlaceableLandMask.Width, PlaceableLandMask.Height];

            for (int z0 = PlaceableLandMask.OriginY; z0 < PlaceableLandMask.OriginY + PlaceableLandMask.Height; z0++)
            {
                for (int x0 = PlaceableLandMask.OriginX; x0 < PlaceableLandMask.OriginX + PlaceableLandMask.Width; x0++)
                {
                    int ix0 = x0 - PlaceableLandMask.OriginX;
                    int iz0 = z0 - PlaceableLandMask.OriginY;
                    if (seen[ix0, iz0] || !PlaceableLandMask.IsLand(x0, z0)) continue;

                    // flood fill ผืนนี้ (8-connectivity เหมือนตอน bake) แล้วหาบล็อก w x h
                    var region = new System.Collections.Generic.List<(int X, int Z)>();
                    var stack = new System.Collections.Generic.Stack<(int X, int Z)>();
                    stack.Push((x0, z0));
                    seen[ix0, iz0] = true;
                    bool fits = false;
                    while (stack.Count > 0)
                    {
                        var (x, z) = stack.Pop();
                        region.Add((x, z));
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            for (int dz = -1; dz <= 1; dz++)
                            {
                                if (dx == 0 && dz == 0) continue;
                                int nx = x + dx;
                                int nz = z + dz;
                                int ix = nx - PlaceableLandMask.OriginX;
                                int iz = nz - PlaceableLandMask.OriginY;
                                if (ix < 0 || iz < 0 || ix >= PlaceableLandMask.Width || iz >= PlaceableLandMask.Height) continue;
                                if (seen[ix, iz] || !PlaceableLandMask.IsLand(nx, nz)) continue;
                                seen[ix, iz] = true;
                                stack.Push((nx, nz));
                            }
                        }
                    }

                    foreach (var (x, z) in region)
                    {
                        bool block = true;
                        for (int dz = 0; dz < h && block; dz++)
                            for (int dx = 0; dx < w && block; dx++)
                                if (!PlaceableLandMask.IsLand(x + dx, z + dz)) block = false;
                        if (block) fits = true;
                    }

                    Assert.IsTrue(fits,
                        $"ผืน land ที่ ({x0},{z0}) มี {region.Count} cell แต่รับ footprint {w}x{h} ไม่ได้ " +
                        "— ควรถูกตัดออกตอน bake (ดู keep_buildable ใน Tools/art/bake_land_mask.py)");
                }
            }
        }

        [Test]
        public void BuildCells_OutsideTheMaskRect_IsAllFalse()
        {
            var cells = PlaceableLandMask.BuildCells(1000, 1000, 3, 2);

            Assert.AreEqual(6, cells.Length);
            foreach (var cell in cells)
                Assert.IsFalse(cell, "rect นอก mask = วางไม่ได้ทั้งหมด (fail-closed)");
        }

        [Test]
        public void Mask_FeedsTheGrid_SoTheGridSeesTheSameLandCells()
        {
            var grid = new BuildingGrid(
                PlaceableLandMask.Width, PlaceableLandMask.Height,
                PlaceableLandMask.OriginX, PlaceableLandMask.OriginY);
            grid.SetPlaceableMask(
                PlaceableLandMask.OriginX, PlaceableLandMask.OriginY,
                PlaceableLandMask.Width, PlaceableLandMask.Height,
                PlaceableLandMask.BuildCells(PlaceableLandMask.OriginX, PlaceableLandMask.OriginY,
                                             PlaceableLandMask.Width, PlaceableLandMask.Height));

            int buildable = 0;
            for (int z = grid.OriginY; z < grid.OriginY + grid.Height; z++)
            {
                for (int x = grid.OriginX; x < grid.OriginX + grid.Width; x++)
                {
                    if (grid.IsInPlaceableZone(x, z)) buildable++;
                }
            }

            Assert.AreEqual(PlaceableLandMask.LandCellCount, buildable,
                "grid ต้องเห็น land เท่ากับ mask เป๊ะ (overlay/grid ใช้ mask ชุดเดียวกัน)");
        }
    }
}
