using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xianxia.Sect.Building
{
    /// <summary>
    /// Logical occupancy grid ของพื้นที่สำนัก (building-system.md §3.1) —
    /// plain C# ล้วน ไม่มี MonoBehaviour/Unity API เพื่อให้ EditMode test
    /// ครอบได้เต็ม ๆ และ logic เดียวกันใช้ได้ทั้งฝั่ง validate และ rebuild.
    /// cell ว่าง = null, ไม่ว่าง = instanceId ของ PlacedBuildingState ที่ครองอยู่.
    /// ขนาดตายตัวตอน Phase 1 (Q5 default: 40×40 cell, cell = 1 world unit) —
    /// ขยายพื้นที่สำนักเป็นเรื่อง Phase หลัง
    /// </summary>
    public class BuildingGrid
    {
        public const int DefaultWidth = 40;
        public const int DefaultHeight = 40;

        /// <summary>Rotation ที่ยอมรับ (0/90/180/270 — ค่าอื่น fail-closed)</summary>
        public static bool IsValidRotation(int rotation)
        {
            return rotation == 0 || rotation == 90 || rotation == 180 || rotation == 270;
        }

        /// <summary>
        /// footprint จริงหลังหมุน: 90/270 สลับกว้าง×สูง (ghost วางตามมุมบนซ้ายเสมอ)
        /// </summary>
        public static void ResolveFootprint(int w, int h, int rotation, out int outW, out int outH)
        {
            if (!IsValidRotation(rotation))
                throw new ArgumentOutOfRangeException(nameof(rotation),
                    $"Rotation must be 0/90/180/270, got {rotation}");
            if (w <= 0 || h <= 0)
                throw new ArgumentOutOfRangeException(nameof(w), $"Footprint must be positive, got {w}x{h}");

            if (rotation == 90 || rotation == 270)
            {
                outW = h;
                outH = w;
            }
            else
            {
                outW = w;
                outH = h;
            }
        }

        private readonly string[,] _occupancy;

        public int Width { get; }
        public int Height { get; }

        /// <summary>
        /// ตำแหน่ง logical cell มุมซ้ายล่างของ grid — ทุก API (CanPlace/Occupy/Release/
        /// GetOccupant/โซน) ใช้ logical cell ที่อาจติดลบได้ ตรงกับ overlay ที่วาด
        /// cell รอบ origin (−Extent/2 .. +Extent/2−1); index ภายใน = logical − origin
        /// </summary>
        public int OriginX { get; }
        public int OriginY { get; }

        // --- โซนวางได้ (placeable zone) ---
        // ค่าเริ่มต้น (ไม่ตั้ง zone) = ทั้ง grid วางได้; ตั้งแล้ว = เฉพาะพื้นที่ใน zone เท่านั้น
        // (จากภาพวาด: 4 โซนบนพื้นทราย ไม่รวมภูเขา/น้ำตก) — world→cell แปลงโดย BuildingSystem
        private readonly List<RectInt> _placeableZones = new List<RectInt>();

        /// <summary>
        /// ctor ไร้พารามิเตอร์ = ขนาด Q5 default (40×40).
        /// ⚠️ DI ห้ามพึ่ง ctor selection ตรง ๆ: VContainer TypeAnalyzer เลือก ctor
        /// ที่มี parameters มากที่สุดเสมอ และ ReflectionInjector ไม่ใช้ค่า default ของ
        /// optional parameter (resolve System.Int32 แล้ว throw) — GameLifetimeScope
        /// จึง register ด้วย factory `resolver => new BuildingGrid()` เพื่อ pin ขนาด
        /// </summary>
        public BuildingGrid() : this(DefaultWidth, DefaultHeight) { }

        /// <summary>ctor แบบระบุขนาด (origin = 0,0) — สำหรับ test (grid เล็ก 10×10)</summary>
        public BuildingGrid(int width, int height)
            : this(width, height, 0, 0) { }

        /// <summary>ctor เต็ม — origin ติดลบได้ (production ใช้ centered ตรงกับ overlay)</summary>
        public BuildingGrid(int width, int height, int originX, int originY)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Grid size must be positive");
            Width = width;
            Height = height;
            OriginX = originX;
            OriginY = originY;
            _occupancy = new string[width, height];
        }

        /// <summary>กำหนดโซนวางได้ (cell พิกัด grid) — เรียกซ้ำได้ แทนที่ชุดเดิมทั้งหมด.
        /// พื้นที่นอกโซน = วางไม่ได้ (CanPlace fail) และถูก mark ว่า "นอกโซน" สำหรับ overlay</summary>
        public void SetPlaceableZones(IEnumerable<RectInt> zones)
        {
            _placeableZones.Clear();
            if (zones == null) return;
            foreach (var zone in zones)
            {
                if (zone.width <= 0 || zone.height <= 0) continue;
                _placeableZones.Add(zone);
            }
        }

        public IReadOnlyList<RectInt> PlaceableZones => _placeableZones;

        /// <summary>cell นี้อยู่ในโซนวางได้ไหม (ไม่มี zone = ทั้ง grid วางได้)
        /// ⚠️ RectInt เป็น 2D — cell-z map เข้า .y (yMin/yMax) ของ RectInt</summary>
        public bool IsInPlaceableZone(int x, int z)
        {
            if (_placeableZones.Count == 0) return true;
            for (int i = 0; i < _placeableZones.Count; i++)
            {
                var zone = _placeableZones[i];
                if (x >= zone.xMin && x < zone.xMax && z >= zone.yMin && z < zone.yMax)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// วางได้ไหม: ทั้ง footprint ต้องอยู่ในขอบเขต grid + ในโซนวางได้ และทุก cell ว่าง.
        /// จุดประสงค์สำหรับ failReason (เหตุผลที่เป็นไปได้: ออกนอก grid/โซน /
        /// ซ้อนทับ — Phase 1 ใช้สีเดียวรวมทุกกรณีตามเกม ref จึงไม่แยก enum)
        /// </summary>
        public bool CanPlace(int x, int z, int w, int h, int rotation = 0)
        {
            int fw, fh;
            try
            {
                ResolveFootprint(w, h, rotation, out fw, out fh);
            }
            catch (ArgumentOutOfRangeException)
            {
                return false; // rotation/footprint มั่ว = วางไม่ได้เสมอ (fail-closed)
            }

            if (x < OriginX || z < OriginY || x + fw > OriginX + Width || z + fh > OriginY + Height)
                return false;

            for (int cy = z; cy < z + fh; cy++)
            {
                for (int cx = x; cx < x + fw; cx++)
                {
                    if (_occupancy[cx - OriginX, cy - OriginY] != null) return false;
                    if (!IsInPlaceableZone(cx, cy)) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// mark cells ให้ instanceId — caller ต้องเรียก CanPlace ก่อนเสมอ
        /// (Occupy ไม่ re-validate: double-occupy = programming error, throw ชัด)
        /// </summary>
        public void Occupy(string instanceId, int x, int z, int w, int h, int rotation = 0)
        {
            if (string.IsNullOrEmpty(instanceId))
                throw new ArgumentException("instanceId is required", nameof(instanceId));

            int fw, fh;
            ResolveFootprint(w, h, rotation, out fw, out fh);

            if (x < OriginX || z < OriginY || x + fw > OriginX + Width || z + fh > OriginY + Height)
                throw new ArgumentOutOfRangeException(nameof(x),
                    $"Footprint {fw}x{fh} at ({x},{z}) is outside the {Width}x{Height} grid " +
                    $"(origin {OriginX},{OriginY})");
            for (int cy = z; cy < z + fh; cy++)
            {
                for (int cx = x; cx < x + fw; cx++)
                {
                    if (_occupancy[cx - OriginX, cy - OriginY] != null)
                        throw new InvalidOperationException(
                            $"Cell ({cx},{cy}) already occupied by '{_occupancy[cx - OriginX, cy - OriginY]}' - call CanPlace first");
                }
            }

            for (int cy = z; cy < z + fh; cy++)
            {
                for (int cx = x; cx < x + fw; cx++)
                {
                    _occupancy[cx - OriginX, cy - OriginY] = instanceId;
                }
            }
        }

        /// <summary>
        /// คืน cell ทั้งหมดของ instanceId (สำหรับทุบอาคาร — Phase 1 ไม่มี UI ทุบ
        /// แต่ hook ต้องใช้งานได้จริงตาม building-system.md §9)
        /// </summary>
        public bool Release(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return false;

            bool found = false;
            for (int cy = 0; cy < Height; cy++)
            {
                for (int cx = 0; cx < Width; cx++)
                {
                    if (_occupancy[cx, cy] == instanceId)
                    {
                        _occupancy[cx, cy] = null;
                        found = true;
                    }
                }
            }
            return found;
        }

        /// <summary>Debug/diagnostic — ใครครอง cell นี้ (null = ว่าง) — logical coords</summary>
        public string GetOccupant(int x, int z)
        {
            int ix = x - OriginX;
            int iy = z - OriginY;
            if (ix < 0 || iy < 0 || ix >= Width || iy >= Height) return null;
            return _occupancy[ix, iy];
        }
    }
}
