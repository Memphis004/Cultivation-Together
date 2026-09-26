using System.Collections.Generic;
using MessagePipe;
using UnityEngine;
using UnityEngine.EventSystems;
using VContainer.Unity;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect
{
    /// <summary>
    /// Building Phase 1 entry point (Q4 default: ใช้ stub เดิมเป็น entry point จริง).
    ///
    /// หน้าที่:
    ///  1. Start(): rebuild BuildingGrid จาก state.PlacedBuildings ที่มีอยู่ —
    ///     ไม่งั้น reload scene แล้ว occupancy หาย ทั้งที่ state ยังอยู่ (AC #6)
    ///  2. ระหว่าง placement mode: สร้าง/ขยับ/ระบายสี ghost ตาม PlacementController
    ///     (เขียว=วางได้, แดง=ซ้อนทับหรือทรัพยากรไม่พอ — สีเดียวรวมทุกกรณี §3.2)
    ///  3. วาด marker ของอาคารที่วางแล้ว (placeholder quad — ยังไม่มี art จริง)
    ///  4. Tick(): อ่านตำแหน่งเมาส์ → grid cell (แปลงผ่าน IsometricCellMath เดิม
    ///     เพื่อให้ตรงกับ GridOverlayRenderer) + snap ghost ตาม footprint
    ///
    /// Grid = logical 40×40 (Q5) แปลงเป็น world ด้วย tile size ที่กล้องวัดจริง
    /// (CameraFramingConfig) — ไม่ hardcode ขนาด art. Landscape กิน grid
    /// เหมือนกันหมด (Q6 default — ไม่มี object นอก grid).
    /// </summary>
    public class BuildingSystem : IStartable, ITickable
    {
        private const string GameplaySceneName = "TestGameplayScene";
        private const string GhostRootName = "BuildingGhostRoot";
        private const string PlacedRootName = "BuildingPlacedRoot";
        private const int PlaceholderPpu = 100;
        private static readonly Color GhostOkColor = new Color(0.35f, 1f, 0.35f, 0.7f);
        private static readonly Color GhostBadColor = new Color(1f, 0.3f, 0.25f, 0.7f);
        private static readonly Color PlacedColor = new Color(0.55f, 0.5f, 0.4f, 0.95f);

        private readonly BuildingGrid _grid;
        private readonly BuildingDefPool _defPool;
        private readonly PlacementController _placement;
        private readonly ISectStateProvider _stateProvider;
        private readonly ISubscriber<BuildingPlacedMessage> _placedSubscriber;
        private readonly ISubscriber<BuildModeStartedMessage> _buildStartedSubscriber;
        private readonly ISubscriber<BuildModeEndedMessage> _buildEndedSubscriber;
        private readonly CameraFramingConfig _framing;

        private Transform _ghostRoot;
        private readonly List<SpriteRenderer> _ghostCells = new List<SpriteRenderer>(16);
        private Transform _placedRoot;
        private readonly Dictionary<string, Transform> _placedMarkers = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();
        private Sprite _unitQuadSprite;

        private Camera _camera;
        private bool _placementVisualsActive;
        private bool _zonesAppliedWithBackdrop;

        public BuildingSystem(
            BuildingGrid grid,
            BuildingDefPool defPool,
            PlacementController placement,
            ISectStateProvider stateProvider,
            ISubscriber<BuildingPlacedMessage> placedSubscriber,
            ISubscriber<BuildModeStartedMessage> buildStartedSubscriber,
            ISubscriber<BuildModeEndedMessage> buildEndedSubscriber,
            CameraFramingConfig framing)
        {
            // fallback (grid ไม่ถูก inject — ทดสอบ/edge case): ขนาด+origin ตรง overlay
            _grid = grid ?? new BuildingGrid(
                GridOverlayRenderer.GridExtent, GridOverlayRenderer.GridExtent,
                -GridOverlayRenderer.GridExtent / 2, -GridOverlayRenderer.GridExtent / 2);
            _defPool = defPool;
            _placement = placement;
            _stateProvider = stateProvider;
            _placedSubscriber = placedSubscriber;
            _buildStartedSubscriber = buildStartedSubscriber;
            _buildEndedSubscriber = buildEndedSubscriber;
            _framing = framing;
        }

        // ── โซนวางได้ (จากภาพวาด: 4 โซนบนพื้นทราย ไม่รวมภูเขา/น้ำตก) ──
        // พิกัด "logical isometric cell" ที่ตรงกับ overlay (cell (0,0) = กึ่งกลางแบ็คกราว,
        // แกน x วิ่งลง-ขวา, แกน z วิ่งลง-ซ้าย ตาม IsometricCellMath) — ปรับเลขที่นี่จุดเดียว
        // เมื่อ art/ภาพวาดเปลี่ยน; แต่ละ RectInt: xMin/zMin..xMax/zMax (z → .y ของ RectInt)
        private static readonly RectInt[] PlaceableZoneRectsCells =
        {
            new RectInt(-6, -12, 8, 5),   // plateau บนซ้าย (เหนือเมือง, ซ้ายน้ำตก)
            new RectInt(-11, -6, 5, 3),   // เกาะเล็กกลางซ้าย (วงแหวนหญ้า)
            new RectInt(2, -6, 10, 8),    // โซนใหญ่ขวา (พื้นทรายกว้าง)
            new RectInt(-7, 2, 9, 5),     // ถนนล่างกลาง (แถบยาว)
        };

        public void Start()
        {
            RebuildGridFromState();
            ApplyPlaceableZones();

            // Process-lifetime singleton — subscriptions live forever, same as
            // WorldEventUISystem/TimeSystem patterns (no per-scene cleanup needed).
            _placedSubscriber.Subscribe(OnBuildingPlaced);
            _buildStartedSubscriber.Subscribe(OnBuildModeStarted);
            _buildEndedSubscriber.Subscribe(OnBuildModeEnded);
        }

        /// <summary>เข้า build mode → marker ของอาคารที่วางไว้ต้องโชว์ครบ
        /// (scene เปิดครั้งแรกหลังวาง / กลับเข้า build mode รอบถัดไป)</summary>
        public void RefreshMarkers()
        {
            RefreshPlacedMarkers();
        }

        // Per-frame logic มีเฉพาะตอน placement mode (ghost sync) — ปกติ no-op เหมือน stub เดิม
        public void Tick()
        {
            // โซนวางได้ควรอิงขนาดแบ็คกราวจริง — ถ้าตอน Start ยังไม่วัด (ลำดับ entry
            // point ไม่การันตี) ลองใหม่เมื่อวัดได้แล้ว (ครั้งเดียวพอ)
            if (!_zonesAppliedWithBackdrop && _framing.HasBackdropBounds)
            {
                ApplyPlaceableZones();
                _zonesAppliedWithBackdrop = true;
            }

            if (!_placement.IsActive)
            {
                if (_placementVisualsActive) HideGhostVisuals();
                return;
            }

            EnsureSceneRefs();
            if (_camera == null) return; // ยังไม่อยู่ใน gameplay scene — ghost รอจนกว่ากล้องพร้อม

            SyncGhostFromMouse();
            PaintGhost();
        }

        /// <summary>
        /// AC #6 — สร้าง occupancy ใหม่จาก state.PlacedBuildings ทุกครั้งที่ Start
        /// entry มั่ว (def หาย/ล้น grid/rotation ผิด) = log แล้วข้าม ไม่ throw ให้เกมพังทั้งฉาก
        /// </summary>
        public void RebuildGridFromState()
        {
            var state = _stateProvider.BuildSectEconomyState();
            var placed = state != null ? state.PlacedBuildings : null;
            if (placed == null) return;

            // สร้าง grid เปล่าใหม่เสมอ — กัน double-occupy ตอน domain reload / Start ซ้ำ
            // (origin/ขนาดต้องตรง grid หลัก — ไม่งั้น restore อาคารที่ cell ติดลบพัง)
            var fresh = new BuildingGrid(_grid.Width, _grid.Height, _grid.OriginX, _grid.OriginY);
            int restored = 0;

            foreach (var pb in placed)
            {
                if (pb == null || string.IsNullOrEmpty(pb.InstanceId) || string.IsNullOrEmpty(pb.DefId))
                {
                    Debug.LogWarning("[BuildingSystem] Skipping malformed PlacedBuildingState entry.");
                    continue;
                }

                var def = _defPool.GetById(pb.DefId);
                if (def == null)
                {
                    Debug.LogWarning($"[BuildingSystem] Placed building '{pb.InstanceId}' references " +
                                     $"unknown def '{pb.DefId}' - skipped (occupancy only).");
                    continue;
                }

                try
                {
                    fresh.Occupy(pb.InstanceId, pb.GridX, pb.GridZ,
                                 def.GridWidth, def.GridHeight, pb.Rotation);
                }
                catch (System.Exception ex)
                {
                    // ล้น grid/overlap/rotation มั่ว = state เสียหายจาก session ก่อน —
                    // เก็บ entry ไว้ใน state ต่อ แต่ไม่ให้ครอง cell
                    Debug.LogWarning($"[BuildingSystem] Could not restore '{pb.InstanceId}' at " +
                                     $"({pb.GridX},{pb.GridZ}): {ex.Message}");
                    continue;
                }

                restored++;
            }

            CopyOccupancy(fresh);
            Debug.Log($"[BuildingSystem] Grid rebuilt from state: {restored}/{placed.Count} buildings restored.");
        }

        /// <summary>
        /// ตั้งโซนวางได้จากภาพวาด — พื้นที่นอกโซน (ภูเขา/น้ำตก/ขอบแมพ) วางไม่ได้
        /// (ghost แดงเมื่ออยู่นอกโซน). โซนอยู่ใน logical cell coords ตรงกับ overlay;
        /// ตัด cell ที่หลุดขอบ grid ทิ้ง (grid = โดเมนเดียวกับ overlay 24×24 centered)
        /// </summary>
        private void ApplyPlaceableZones()
        {
            var zones = new List<RectInt>(PlaceableZoneRectsCells.Length);
            foreach (var zone in PlaceableZoneRectsCells)
            {
                int xMin = Mathf.Max(zone.xMin, _grid.OriginX);
                int zMin = Mathf.Max(zone.yMin, _grid.OriginY);
                int xMax = Mathf.Min(zone.xMax, _grid.OriginX + _grid.Width);
                int zMax = Mathf.Min(zone.yMax, _grid.OriginY + _grid.Height);

                if (xMax <= xMin || zMax <= zMin) continue;
                zones.Add(new RectInt(xMin, zMin, xMax - xMin, zMax - zMin));
            }

            _grid.SetPlaceableZones(zones);
            _zonesAppliedWithBackdrop = true; // โซนเป็น cell-space — ไม่ผูกกับการวัดแบ็คกราว
            Debug.Log($"[BuildingSystem] Placeable zones applied: {zones.Count} zones " +
                      $"(grid {_grid.Width}x{_grid.Height} at origin {_grid.OriginX},{_grid.OriginY})");
        }

        private void CopyOccupancy(BuildingGrid source)
        {
            for (int z = source.OriginY; z < source.OriginY + source.Height; z++)
            {
                for (int x = source.OriginX; x < source.OriginX + source.Width; x++)
                {
                    var occupant = source.GetOccupant(x, z);
                    if (occupant == null) continue;

                    _grid.Occupy(occupant, x, z, 1, 1); // copy cell-by-cell (logical coords, ทั้งคู่ origin/ขนาดเดียวกัน)
                }
            }
        }

        // ---- building placed: marker บนฉาก ----

        private void OnBuildingPlaced(BuildingPlacedMessage message)
        {
            RefreshPlacedMarkers();
        }

        private void RefreshPlacedMarkers()
        {
            var state = _stateProvider.BuildSectEconomyState();
            if (state == null) return;

            EnsurePlacedRoot();
            if (_placedRoot == null) return;

            // rebuild marker ทั้งชุด — จำนวนอาคารระดับ prototype น้อย ง่ายและถูกกว่า diff
            foreach (var pair in _placedMarkers)
            {
                if (pair.Value != null) Object.Destroy(pair.Value.gameObject);
            }
            _placedMarkers.Clear();

            var cellW = _framing.IsMeasured ? _framing.TileWidthWorld : 1f;
            var cellH = _framing.IsMeasured ? _framing.TileHeightWorld : 1f;

            foreach (var pb in state.PlacedBuildings)
            {
                if (pb == null || string.IsNullOrEmpty(pb.InstanceId)) continue;
                var def = _defPool.GetById(pb.DefId);
                if (def == null) continue;

                int fw, fh;
                BuildingGrid.ResolveFootprint(def.GridWidth, def.GridHeight, pb.Rotation, out fw, out fh);

                var go = new GameObject("Building_" + pb.InstanceId, typeof(SpriteRenderer));
                go.transform.SetParent(_placedRoot, false);

                var sr = go.GetComponent<SpriteRenderer>();
                sr.sprite = GetFootprintSprite(pb.DefId, fw, fh);
                sr.color = PlacedColor;
                sr.sortingOrder = 20; // เหนือ grid overlay, ใต้ ghost

                go.transform.position = FootprintCenterWorld(pb.GridX, pb.GridZ, fw, fh, cellW, cellH);
                _placedMarkers[pb.InstanceId] = go.transform;
            }
        }

        // ---- placement visuals ----

        private void OnBuildModeStarted(BuildModeStartedMessage message)
        {
            // เมนูเลือกอาคารจะสั่ง BeginPlacement เอง — event นี้เปิด overlay/กล้องแล้ว;
            // ghost จะถูกสร้างเมื่อ PlacementController.IsActive (ดู Tick)
            RefreshPlacedMarkers();
        }

        private void OnBuildModeEnded(BuildModeEndedMessage message)
        {
            // ปิด build mode (toggle ปุ่ม สร้าง / ยืนยัน / ยกเลิกจาก UI) → เก็บ ghost;
            // ถ้า placement ยัง active (สลับไปวางอีก def ใน build mode เดียวกัน) ปล่อยไว้
            if (!_placement.IsActive) HideGhostVisuals();
        }

        private void EnsureSceneRefs()
        {
            if (_camera != null) return;

            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || scene.name != GameplaySceneName) continue;

                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length && _camera == null; r++)
                {
                    var cam = roots[r].GetComponentInChildren<Camera>(true);
                    if (cam != null && cam.enabled) _camera = cam;
                }
            }
        }

        private void EnsureGhostRoot()
        {
            if (_ghostRoot != null) return;

            var go = new GameObject(GhostRootName);
            go.SetActive(false);
            _ghostRoot = go.transform;

            for (int i = 0; i < _ghostCells.Capacity; i++)
            {
                var cellGo = new GameObject("GhostCell", typeof(SpriteRenderer));
                cellGo.transform.SetParent(_ghostRoot, false);
                _ghostCells.Add(cellGo.GetComponent<SpriteRenderer>());
            }
        }

        private void HideGhostVisuals()
        {
            _placementVisualsActive = false;
            _hasGhostScreenPos = false;
            if (_ghostRoot != null) _ghostRoot.gameObject.SetActive(false);
        }

        // โมเดลอินพุตตามเกม ref: ghost ไม่ตามเมาส์ตลอด — เลื่อนเฉพาะตอน "คลิกค้างแล้วลาก"
        // บนฉาก (กดบน HUD ไม่ขยับ); ปล่อยแล้ว ghost ค้าง cell เดิม เมาส์ว่างไปกดปุ่มลอยได้
        private void SyncGhostFromMouse()
        {
            if (_camera == null) return;
            if (!Input.GetMouseButton(0)) return; // ลากเท่านั้น — ไม่ follow cursor อิสระ
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return; // pointer อยู่บน HUD (ปุ่มลอย/เมนู) — ไม่ดึง ghost มาที่ปุ่ม

            var world = _camera.ScreenToWorldPoint(Input.mousePosition);
            var cell = IsometricCellMath.WorldToCell(world, OverlayOrigin(),
                _framing.IsMeasured ? _framing.TileWidthWorld : 1f,
                _framing.IsMeasured ? _framing.TileHeightWorld : 1f);

            _placement.UpdatePosition(cell.x, cell.y);
        }

        private void PaintGhost()
        {
            var def = _placement.CurrentDef;
            if (def == null)
            {
                HideGhostVisuals();
                return;
            }

            EnsureGhostRoot();
            EnsurePlacedRoot();
            _ghostRoot.gameObject.SetActive(true);
            _placementVisualsActive = true;

            int fw, fh;
            BuildingGrid.ResolveFootprint(def.GridWidth, def.GridHeight, _placement.Rotation, out fw, out fh);

            bool canCommit = _placement.CanCommit(_grid);
            var color = canCommit ? GhostOkColor : GhostBadColor;

            var cellW = _framing.IsMeasured ? _framing.TileWidthWorld : 1f;
            var cellH = _framing.IsMeasured ? _framing.TileHeightWorld : 1f;
            var origin = OverlayOrigin();

            for (int i = 0; i < _ghostCells.Count; i++)
            {
                var sr = _ghostCells[i];
                if (sr == null) continue;

                if (i >= fw * fh)
                {
                    sr.enabled = false;
                    continue;
                }

                int cx = i % fw;
                int cy = i / fw;
                sr.enabled = true;
                sr.sprite = UnitQuad();
                sr.color = color;
                sr.sortingOrder = 30; // เหนือ marker ที่วางแล้ว

                sr.transform.position = IsometricCellMath.CellCenterToWorld(
                    new Vector2Int(_placement.GridX + cx, _placement.GridZ + cy),
                    origin, cellW, cellH);
            }

            // จุดยึดปุ่มลอย: กึ่งกลาง footprint แปลงเป็น screen pos —
            // BuildingPlacementUISystem ดึงผ่าน TryGetGhostScreenPosition เพื่อให้
            // ปุ่ม ยกเลิก/หมุน/วาง ลอยเหนือ ghost ตามเกม ref (screenshot 3–4)
            if (_camera != null)
            {
                var center = FootprintCenterWorld(_placement.GridX, _placement.GridZ, fw, fh, cellW, cellH);
                var sp = _camera.WorldToScreenPoint(center);
                _hasGhostScreenPos = sp.z > 0f;
                _ghostScreenPos = new Vector2(sp.x, sp.y);
            }
        }

        private bool _hasGhostScreenPos;
        private Vector2 _ghostScreenPos;

        /// <summary>จุดกึ่งกลาง ghost บนหน้าจอ — ปุ่มลอยเกาะจุดนี้ (คืน false ถ้ายังไม่มี ghost)</summary>
        public bool TryGetGhostScreenPosition(out Vector2 screenPos)
        {
            screenPos = _ghostScreenPos;
            return _hasGhostScreenPos;
        }

        /// <summary>
        /// จุดอ้างอิง world ของ cell (0,0) — world-anchored ที่กึ่งกลางแบ็คกราว
        /// (เหมือน GridOverlayRenderer หลังแก้) กล้อง pan ยังไง ghost/marker/โซน
        /// ก็อยู่กับที่บนพื้นโลก
        /// </summary>
        private static readonly Vector2 OverlayOriginValue = Vector2.zero;

        private static Vector2 OverlayOrigin() => OverlayOriginValue;

        private static Vector3 FootprintCenterWorld(int gridX, int gridZ, int fw, int fh,
                                                    float cellW, float cellH)
        {
            // กึ่งกลาง footprint = ค่าเฉลี่ย center ของสี่มุม (isometric mapping)
            var c00 = IsometricCellMath.CellCenterToWorld(new Vector2Int(gridX, gridZ), Vector2.zero, cellW, cellH);
            var c10 = IsometricCellMath.CellCenterToWorld(new Vector2Int(gridX + fw - 1, gridZ), Vector2.zero, cellW, cellH);
            var c01 = IsometricCellMath.CellCenterToWorld(new Vector2Int(gridX, gridZ + fh - 1), Vector2.zero, cellW, cellH);
            var c11 = IsometricCellMath.CellCenterToWorld(new Vector2Int(gridX + fw - 1, gridZ + fh - 1), Vector2.zero, cellW, cellH);
            return (c00 + c10 + c01 + c11) / 4f;
        }

        /// <summary>Quad ขาว 1 cell (cached) — ใช้ระบายสี ghost ทีละ cell</summary>
        private Sprite UnitQuad()
        {
            if (_unitQuadSprite == null)
                _unitQuadSprite = CreateQuadSprite(1, 1);
            return _unitQuadSprite;
        }

        /// <summary>
        /// Placeholder quad ขนาด footprint (cached ต่อ def+ขนาด) — โปรเจกต์ยังไม่มี
        /// art อาคาร (SpritePath ว่างหมดใน building_defs.json); ภายหลังถ้ามี sprite
        /// จริง ให้ Resources.Load&lt;Sprite&gt;(def.SpritePath) แทนที่ได้เลย
        /// </summary>
        private Sprite GetFootprintSprite(string defId, int fw, int fh)
        {
            var key = defId + "_" + fw + "x" + fh;
            Sprite sprite;
            if (!_spriteCache.TryGetValue(key, out sprite) || sprite == null)
            {
                sprite = CreateQuadSprite(fw, fh);
                _spriteCache[key] = sprite;
            }
            return sprite;
        }

        private static Sprite CreateQuadSprite(int cellsX, int cellsY)
        {
            var tex = new Texture2D(cellsX * PlaceholderPpu, cellsY * PlaceholderPpu,
                                    TextureFormat.RGBA32, false);
            var pixels = new Color32[tex.width * tex.height];
            var fill = new Color32(255, 255, 255, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = fill;
            tex.SetPixels32(pixels);
            tex.Apply();

            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                 new Vector2(0.5f, 0.5f), PlaceholderPpu);
        }

        private void EnsurePlacedRoot()
        {
            if (_placedRoot != null) return;

            var go = new GameObject(PlacedRootName);
            _placedRoot = go.transform;
        }
    }
}
