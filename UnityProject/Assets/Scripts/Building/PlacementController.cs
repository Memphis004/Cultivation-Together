namespace Xianxia.Sect.Building
{
    /// <summary>
    /// Ghost state + commit orchestration (building-system.md §3.2/§5, Step 4).
    /// Plain C# singleton — BuildingSystem ฉาย ghost ตาม state นี้ (Q5: isometric
    /// world mapping), PlacementController เป็นเจ้าของ "กำลังวางอะไร ตรงไหน
    /// หมุนกี่องศา และ commit ได้ไหม".
    ///
    /// ลำดับ validate ตอน commit (§3.2): ขอบเขต → ซ้อนทับ (CanPlace) →
    /// ทรัพยากรพอไหม (CanAffordBuilding) — cost check ผ่าน ISectStateProvider
    /// เท่านั้น (ห้าม peek state ตรง ๆ) แล้วสั่ง mutation ผ่าน
    /// ISectStateProvider.TryPlaceBuilding (AdjustAndNotify choke point + state
    /// append + BuildingPlacedMessage publish อยู่ข้างในนั้น).
    ///
    /// Cancel() ทิ้ง ghost เฉย ๆ — ไม่มีอะไรแตะ state เลย (AC: กด ✕ แล้ว state
    /// ต้องเท่าเดิม) เพราะทุก mutation เกิดเฉพาะจังหวะ commit สำเร็จเท่านั้น.
    /// </summary>
    public class PlacementController
    {
        private readonly BuildingDefPool _defPool;
        private readonly Xianxia.Sect.ISectStateProvider _stateProvider;

        public PlacementController(BuildingDefPool defPool, Xianxia.Sect.ISectStateProvider stateProvider)
        {
            _defPool = defPool;
            _stateProvider = stateProvider;
        }

        public bool IsActive { get; private set; }
        public string DefId { get; private set; }
        public int GridX { get; private set; }
        public int GridZ { get; private set; }
        public int Rotation { get; private set; }

        /// <summary>Def ของ ghost ปัจจุบัน (null ถ้าไม่ได้อยู่ placement mode)</summary>
        public BuildingDef CurrentDef
        {
            get { return IsActive ? _defPool.GetById(DefId) : null; }
        }

        /// <summary>
        /// เข้าสู่ placement mode ด้วย def ที่เลือกจากเมนู — ghost เริ่มที่ (0,0)
        /// หมุน 0°. defId ไม่รู้จัก = ไม่เข้าโหมด (คืน null ให้ caller จัดการ)
        /// </summary>
        public BuildingDef BeginPlacement(string defId)
        {
            var def = _defPool != null ? _defPool.GetById(defId) : null;
            if (def == null) return null;

            IsActive = true;
            DefId = def.Id;
            GridX = 0;
            GridZ = 0;
            Rotation = 0;
            return def;
        }

        /// <summary>เลื่อน ghost (snap cell แล้วจาก caller — BuildingSystem แปลง mouse → cell)</summary>
        public void UpdatePosition(int gridX, int gridZ)
        {
            GridX = gridX;
            GridZ = gridZ;
        }

        /// <summary>
        /// หมุน 90° ตามเข็ม — อาคารที่ Rotatable=false ปฏิเสธเงียบ ๆ (คืน false,
        /// ปุ่ม ⟲ บน UI จะกดแล้วไม่เกิดอะไร ตามพฤติกรรมเกม ref)
        /// </summary>
        public bool Rotate()
        {
            var def = CurrentDef;
            if (def == null || !def.Rotatable) return false;

            Rotation = (Rotation + 90) % 360;
            return true;
        }

        /// <summary>ยกเลิก ghost — ไม่มีอะไรเปลี่ยนใน state เลย</summary>
        public void Cancel()
        {
            ClearGhost();
        }

        /// <summary>
        /// Ghost validity สำหรับสีเขียว/แดง (§3.2) — read-only ทั้งหมด:
        /// ขอบเขต+ซ้อนทับ (BuildingGrid.CanPlace) และ ทรัพยากรพอ (CanAffordBuilding).
        /// Phase 1 ใช้สีเดียวรวมทุกกรณี ไม่แยกเหตุผล (failReason ไว้ให้ commit path)
        /// </summary>
        public bool CanCommit(BuildingGrid grid)
        {
            if (!IsActive || grid == null) return false;

            var def = CurrentDef;
            if (def == null) return false;

            if (!grid.CanPlace(GridX, GridZ, def.GridWidth, def.GridHeight, Rotation)) return false;
            if (_stateProvider == null || !_stateProvider.CanAffordBuilding(def)) return false;
            return true;
        }

        /// <summary>
        /// Commit การวาง: validate ตาม §3.2 แล้วสั่ง place ผ่าน state provider
        /// (ที่เดียวที่หักทรัพยากร/แตะ state). สำเร็จ = ออกจาก placement mode
        /// ทันที (วางใหม่ทีละชิ้น auto-commit ตาม §5 — ไม่มี draft ผังรวม).
        /// </summary>
        public bool TryCommit(BuildingGrid grid, out string failReason,
                              out Xianxia.Sect.PlacedBuildingState placed)
        {
            placed = null;
            failReason = string.Empty;

            if (!IsActive)
            {
                failReason = "Placement mode is not active.";
                return false;
            }

            var def = CurrentDef;
            if (def == null)
            {
                failReason = "Placement ghost has no valid def.";
                Cancel();
                return false;
            }

            // 1+2. ขอบเขต + ซ้อนทับ / 3. ทรัพยากร — รายงานเหตุผลก่อนสั่ง mutation
            if (!grid.CanPlace(GridX, GridZ, def.GridWidth, def.GridHeight, Rotation))
            {
                failReason = "วางไม่ได้: ออกนอกเขตสำนัก หรือซ้อนทับกับสิ่งก่อสร้างเดิม";
                return false;
            }
            if (_stateProvider == null || !_stateProvider.CanAffordBuilding(def))
            {
                failReason = "ทรัพยากรไม่พอสำหรับสร้างอาคารนี้";
                return false;
            }

            // authoritative mutation — re-validate ข้างในและหักทรัพยากรที่นั่น
            if (!_stateProvider.TryPlaceBuilding(DefId, GridX, GridZ, Rotation, grid,
                                                 out failReason, out placed))
            {
                return false;
            }

            ClearGhost(); // auto-commit ทีละชิ้น — ออกจาก placement mode
            return true;
        }

        private void ClearGhost()
        {
            IsActive = false;
            DefId = null;
            GridX = 0;
            GridZ = 0;
            Rotation = 0;
        }
    }
}
