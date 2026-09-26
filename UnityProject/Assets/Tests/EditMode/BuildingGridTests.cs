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
            // 2x3 at (8,0): unrotated overflows right (8+2=10 fits, but height 3 -> rows 0-2 fine) -
            // use a clearly asymmetric case: 1x3 rotated 90 becomes 3x1.
            Assert.IsTrue(_grid.CanPlace(8, 0, 1, 3, 90), "1x3 rot90 = 3x1, fits at (8,0)");
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
