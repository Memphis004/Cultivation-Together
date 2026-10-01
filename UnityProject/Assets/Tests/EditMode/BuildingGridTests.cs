using NUnit.Framework;
using Xianxia.Sect.Building;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// BuildingGrid acceptance (building-system.md §3.1 + task AC#2):
    /// out-of-bounds, overlap, ว่าง-ผ่าน, release-แล้ว-วางซ้ำได้.
    /// Plain C# — no Unity API, รันได้ใน EditMode ตรง ๆ
    /// </summary>
    public class BuildingGridTests
    {
        private BuildingGrid _grid;

        [SetUp]
        public void SetUp()
        {
            _grid = new BuildingGrid(10, 10);
        }

        // ---- ว่าง-ผ่าน ----

        [Test]
        public void CanPlace_EmptyArea_ReturnsTrue()
        {
            Assert.IsTrue(_grid.CanPlace(0, 0, 2, 2));
            Assert.IsTrue(_grid.CanPlace(8, 8, 2, 2), "flush with the far corner still fits");
        }

        [Test]
        public void Occupy_ThenGetOccupant_ReportsInstanceId()
        {
            _grid.Occupy("b001", 3, 4, 2, 2);

            Assert.AreEqual("b001", _grid.GetOccupant(3, 4));
            Assert.AreEqual("b001", _grid.GetOccupant(4, 5));
            Assert.IsNull(_grid.GetOccupant(5, 5), "cell outside footprint stays free");
        }

        // ---- out-of-bounds ----

        [Test]
        public void CanPlace_OffLeftEdge_ReturnsFalse()
        {
            Assert.IsFalse(_grid.CanPlace(-1, 0, 2, 2));
        }

        [Test]
        public void CanPlace_OffRightEdge_ReturnsFalse()
        {
            Assert.IsFalse(_grid.CanPlace(9, 0, 2, 2), "2-wide at x=9 overflows a 10-wide grid");
        }

        [Test]
        public void CanPlace_OffBottomAndTopEdges_ReturnsFalse()
        {
            Assert.IsFalse(_grid.CanPlace(0, -1, 1, 1));
            Assert.IsFalse(_grid.CanPlace(0, 10, 1, 1));
        }

        [Test]
        public void CanPlace_FootprintLargerThanGrid_ReturnsFalse()
        {
            Assert.IsFalse(_grid.CanPlace(0, 0, 11, 1));
            Assert.IsFalse(_grid.CanPlace(0, 0, 1, 11));
        }

        [Test]
        public void Occupy_OutOfBounds_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => _grid.Occupy("b001", 9, 0, 2, 2));
        }

        // ---- overlap ----

        [Test]
        public void CanPlace_OverlappingOccupiedCell_ReturnsFalse()
        {
            _grid.Occupy("b001", 2, 2, 2, 2);

            Assert.IsFalse(_grid.CanPlace(3, 3, 2, 2), "corner overlap");
            Assert.IsFalse(_grid.CanPlace(0, 0, 3, 3), "engulfing overlap");
            Assert.IsFalse(_grid.CanPlace(2, 2, 2, 2), "exact same spot");
        }

        [Test]
        public void CanPlace_AdjacentNotOverlapping_ReturnsTrue()
        {
            _grid.Occupy("b001", 2, 2, 2, 2);

            Assert.IsTrue(_grid.CanPlace(4, 2, 2, 2), "touching on the right edge is fine");
            Assert.IsTrue(_grid.CanPlace(0, 0, 2, 2), "touching on the top-left corner is fine");
        }

        [Test]
        public void Occupy_WithoutCanPlace_DoubleOccupancy_Throws()
        {
            _grid.Occupy("b001", 0, 0, 2, 2);
            Assert.Throws<System.InvalidOperationException>(
                () => _grid.Occupy("b002", 1, 1, 2, 2));
        }

        // ---- release-แล้ว-วางซ้ำได้ ----

        [Test]
        public void Release_FreesCells_AndPlaceSucceedsAgain()
        {
            _grid.Occupy("b001", 5, 5, 2, 2);
            Assert.IsFalse(_grid.CanPlace(5, 5, 2, 2));

            Assert.IsTrue(_grid.Release("b001"));
            Assert.IsNull(_grid.GetOccupant(5, 5));

            Assert.IsTrue(_grid.CanPlace(5, 5, 2, 2), "released cells must be placeable again");
            _grid.Occupy("b002", 5, 5, 2, 2);
            Assert.AreEqual("b002", _grid.GetOccupant(5, 5));
        }

        [Test]
        public void Release_UnknownInstanceId_ReturnsFalse_AndChangesNothing()
        {
            _grid.Occupy("b001", 1, 1, 1, 1);

            Assert.IsFalse(_grid.Release("nobody"));
            Assert.IsFalse(_grid.Release(null));
            Assert.IsFalse(_grid.Release(""));
            Assert.AreEqual("b001", _grid.GetOccupant(1, 1), "grid untouched");
        }

        // ---- rotation (footprint swap) ----

        [Test]
        public void CanPlace_Rotated90_SwapsFootprint()
        {
            // พิสูจน์การสลับ footprint ด้วยขอบกริด 10 ช่อง: 1x3 ไม่หมุน = กว้าง 1 → x=8 พอดี;
            // หมุน 90 สลับเป็น 3x1 → x=8..10 ล้นกริด (เดิม test คาดผิดว่าวางที่ x=8 ได้)
            Assert.IsTrue(_grid.CanPlace(8, 0, 1, 3), "1x3 unrotated fits at (8,0) — 1 wide");
            Assert.IsFalse(_grid.CanPlace(8, 0, 1, 3, 90), "rot90 swaps to 3x1 → x=8..10 overflows a 10-wide grid");
            Assert.IsTrue(_grid.CanPlace(7, 0, 1, 3, 90), "3x1 rot90 fits at (7,0) — x=7..9");
            Assert.IsFalse(_grid.CanPlace(9, 0, 1, 3, 90), "3-wide rot90 at x=9 overflows");
        }

        [Test]
        public void Occupy_WithRotation_OccupiesSwappedFootprint()
        {
            _grid.Occupy("b001", 0, 0, 1, 3, 90); // 3x1

            Assert.AreEqual("b001", _grid.GetOccupant(2, 0), "rotated footprint spans x=0..2");
            Assert.IsNull(_grid.GetOccupant(0, 2), "unrotated span (z=0..2) must stay free");
        }

        [Test]
        public void CanPlace_InvalidRotation_ReturnsFalse_FailClosed()
        {
            Assert.IsFalse(_grid.CanPlace(0, 0, 1, 1, 45));
            Assert.IsFalse(_grid.CanPlace(0, 0, 1, 1, -90));
        }

        [Test]
        public void ResolveFootprint_InvalidRotation_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => BuildingGrid.ResolveFootprint(1, 1, 123, out _, out _));
        }

        // ---- land mask ต่อ cell (ทางที่ production ใช้ — PlaceableLandMask) ----

        /// <summary>สร้าง block row-major จาก ASCII ('#' = land) — index = (z) * width + (x)</summary>
        private static bool[] MaskFrom(params string[] rows)
        {
            var cells = new bool[rows.Length * rows[0].Length];
            for (int iz = 0; iz < rows.Length; iz++)
            {
                for (int ix = 0; ix < rows[iz].Length; ix++)
                    cells[iz * rows[0].Length + ix] = rows[iz][ix] == '#';
            }
            return cells;
        }

        [Test]
        public void SetPlaceableMask_BlocksCellsThatAreNotLand()
        {
            var grid = new BuildingGrid(4, 4, 0, 0);
            // land = คอลัมน์ซ้ายสุด (z=0,1) เท่านั้น
            grid.SetPlaceableMask(0, 0, 4, 4, MaskFrom("#...", "#...", "....", "...."));

            Assert.IsTrue(grid.HasPlaceableMask);
            Assert.IsTrue(grid.CanPlace(0, 0, 1, 1), "(0,0) is land");
            Assert.IsTrue(grid.CanPlace(0, 1, 1, 1), "(0,1) is land");
            Assert.IsFalse(grid.CanPlace(1, 0, 1, 1), "(1,0) is mist/rock -> not placeable");
            Assert.IsFalse(grid.CanPlace(0, 2, 1, 1), "(0,2) is outside the land shape");
        }

        [Test]
        public void CanPlace_FootprintStraddlingLandAndNonLand_ReturnsFalse()
        {
            var grid = new BuildingGrid(4, 4, 0, 0);
            grid.SetPlaceableMask(0, 0, 4, 4, MaskFrom("#...", "#...", "....", "...."));

            Assert.IsFalse(grid.CanPlace(0, 0, 2, 1),
                "ทุก cell ของ footprint ต้องเป็น land — (1,0) ไม่ใช่");
        }

        [Test]
        public void SetPlaceableMask_CellsOutsideMaskRect_AreNotPlaceable()
        {
            var grid = new BuildingGrid(10, 10, 0, 0);
            grid.SetPlaceableMask(0, 0, 2, 2, MaskFrom("##", "##"));

            Assert.IsTrue(grid.CanPlace(0, 0, 2, 2), "ใน rect = land ทั้งหมด");
            Assert.IsFalse(grid.CanPlace(5, 5, 1, 1), "cell นอก mask rect = fail-closed");
            Assert.IsFalse(grid.IsInPlaceableZone(-1, 0), "ติดลบก็อยู่นอก rect");
        }

        [Test]
        public void SetPlaceableMask_OverridesZoneRects()
        {
            var grid = new BuildingGrid(4, 4, 0, 0);
            grid.SetPlaceableZones(new[] { new UnityEngine.RectInt(0, 0, 4, 4) }); // ทั้ง grid
            Assert.IsTrue(grid.CanPlace(3, 3, 1, 1), "zone บอกว่าวางได้");

            grid.SetPlaceableMask(0, 0, 4, 4, MaskFrom("#...", "....", "....", "...."));
            Assert.IsFalse(grid.CanPlace(3, 3, 1, 1), "mask เป็นผู้ตัดสินแทน zone rect");
            Assert.IsTrue(grid.CanPlace(0, 0, 1, 1));
        }

        [Test]
        public void ClearPlaceableMask_FallsBackToZoneRects()
        {
            var grid = new BuildingGrid(4, 4, 0, 0);
            grid.SetPlaceableMask(0, 0, 4, 4, MaskFrom("#...", "....", "....", "...."));
            Assert.IsFalse(grid.CanPlace(2, 2, 1, 1));

            grid.ClearPlaceableMask();

            Assert.IsFalse(grid.HasPlaceableMask);
            Assert.IsTrue(grid.CanPlace(2, 2, 1, 1), "ไม่มี mask และไม่มี zone = ทั้ง grid วางได้");
        }

        [Test]
        public void SetPlaceableMask_WrongBlockSize_Throws()
        {
            var grid = new BuildingGrid(4, 4, 0, 0);
            Assert.Throws<System.ArgumentException>(
                () => grid.SetPlaceableMask(0, 0, 2, 2, new[] { true, false }));
        }

        [Test]
        public void SetPlaceableMask_NullOrEmptySize_Throws()
        {
            var grid = new BuildingGrid(4, 4, 0, 0);
            Assert.Throws<System.ArgumentNullException>(() => grid.SetPlaceableMask(0, 0, 1, 1, null));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => grid.SetPlaceableMask(0, 0, 0, 1, new bool[0]));
        }

        [Test]
        public void SetPlaceableMask_IsCopied_CallerMutationDoesNotLeak()
        {
            var grid = new BuildingGrid(4, 4, 0, 0);
            var cells = new[] { true, false, false, false };
            grid.SetPlaceableMask(0, 0, 2, 2, cells);

            cells[0] = false; // caller แก้ block ของตัวเอง

            Assert.IsTrue(grid.CanPlace(0, 0, 1, 1), "grid ต้องถือสำเนาของตัวเอง");
        }

        // ---- default size (Q5) ----

        [Test]
        public void DefaultConstructor_Is40By40()
        {
            var grid = new BuildingGrid();
            Assert.AreEqual(BuildingGrid.DefaultWidth, grid.Width);
            Assert.AreEqual(BuildingGrid.DefaultHeight, grid.Height);
        }
    }
}
