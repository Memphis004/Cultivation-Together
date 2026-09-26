using System;
using System.Collections.Generic;

namespace Xianxia.Sect.Building
{
    /// <summary>หมวดหมู่อาคาร — ตาม building-system.md §2 (4 หมวดจากเกม ref)</summary>
    public enum BuildingCategory
    {
        Production,   // ผลิต — เช่น แปลงสมุนไพร, หอปรุงยา
        Convenience,  // สิ่งอำนวยความสะดวก
        Shop,         // ร้านค้า
        Landscape,    // ภูมิทัศน์ (ของตกแต่ง, ไม่ผูก task)
    }

    /// <summary>
    /// Def อาคาร 1 ชนิด (design-time data). Phase 1 โหลดจาก
    /// Resources/Data/building_defs.json ผ่าน BuildingDefPool (Q1 default:
    /// JSON เขียนมือแบบ avatar_parts.json — ย้ายเข้า Luban ทีหลังได้).
    /// Cost = resourceId → amount จาก Stockpile.RawResources (Q2 default:
    /// herb/wood/ore/provisions — ไม่สร้าง currency ใหม่, YAGNI).
    /// Phase 2 จะเพิ่ม WorkAnchorOffsets/MaxWorkers ฯลฯ — ยังไม่ใส่ (YAGNI).
    /// </summary>
    [Serializable]
    public class BuildingDef
    {
        public string Id;                 // "herb_plot", "pill_hall"
        public string DisplayName;        // ชื่อไทยแสดงบนเมนู
        public string Category;           // JSON เก็บเป็น string — parse ผ่าน CategoryValue (JsonUtility ไม่แปลง enum จาก string)
        public int GridWidth;             // ขนาดบน grid หน่วยเป็น cell (เช่น 2x2)
        public int GridHeight;
        public List<BuildingCostEntry> Cost = new List<BuildingCostEntry>(); // JSON: รายการ, runtime: lookup ผ่าน GetCost
        public bool Rotatable;            // บางอาคาร (กำแพง/ถนน) หมุนได้, ของตกแต่งบางชิ้นไม่ให้หมุน
        public string SpritePath;         // Resources path สำหรับ placed instance + thumbnail ("ว่าง" = placeholder สี่เหลี่ยม)
        public string ThumbnailPath;      // thumbnail สำหรับกริดในเมนู ("" = ใช้ SpritePath)

        /// <summary>Cost lookup — list เป็น JSON shape, dict เป็น runtime shape (เลี่ยง Dictionary บน JsonUtility)</summary>
        public IReadOnlyDictionary<string, int> GetCost()
        {
            if (_costLookup == null)
            {
                var dict = new Dictionary<string, int>(StringComparer.Ordinal);
                if (Cost != null)
                {
                    for (int i = 0; i < Cost.Count; i++)
                    {
                        var entry = Cost[i];
                        if (entry == null || string.IsNullOrEmpty(entry.ResourceId)) continue;
                        // duplicate resource key = รวมจำนวน (กัน JSON เผลอใส่ซ้ำ)
                        if (dict.ContainsKey(entry.ResourceId))
                        {
                            dict[entry.ResourceId] += entry.Amount;
                        }
                        else
                        {
                            dict[entry.ResourceId] = entry.Amount;
                        }
                    }
                }
                _costLookup = dict;
            }
            return _costLookup;
        }

        [NonSerialized] private Dictionary<string, int> _costLookup;

        [NonSerialized] private BuildingCategory _categoryValue;
        [NonSerialized] private bool _categoryParsed;

        /// <summary>
        /// หมวดหมู่ที่ parse แล้ว — JsonUtility ไม่แปลง string เป็น enum
        /// (bug ที่เคยเกิด: ทุก def ได้ค่า default Production ทำให้แท็บผลิตโชว์การ์ดครบทุกหมวด)
        /// ชื่อไม่รู้จัก = Production (fail-safe ระดับ prototype — ไม่ throw ตอน load)
        /// </summary>
        public BuildingCategory CategoryValue
        {
            get
            {
                if (!_categoryParsed)
                {
                    _categoryParsed = true;
                    if (!Enum.TryParse(Category, true, out _categoryValue))
                        _categoryValue = BuildingCategory.Production;
                }
                return _categoryValue;
            }
        }
    }

    /// <summary>รายการเดียวใน Cost — JSON ไม่สะดวกกับ Dictionary ตรง ๆ จึงใช้ list ของคู่</summary>
    [Serializable]
    public class BuildingCostEntry
    {
        public string ResourceId; // "herb" / "wood" / "ore" / "provisions"
        public int Amount;
    }
}
