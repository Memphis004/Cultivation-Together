using System.Collections.Generic;
using UnityEngine;

namespace Xianxia.Sect.Building
{
    [System.Serializable]
    public class BuildingDefTable
    {
        public List<BuildingDef> buildings = new List<BuildingDef>();
    }

    /// <summary>
    /// Def-table สำหรับอาคาร โหลดจาก Resources/Data/building_defs.json
    /// Plain C# class (register เป็น Singleton ใน GameLifetimeScope) ไม่ใช่
    /// ScriptableObject — แพทเทิร์นเดียวกับ AvatarPartPool (Q1 default: JSON
    /// เขียนมือ ย้ายเข้า Luban ทีหลังได้โดยไม่พัง call sites).
    /// ไม่ throw — ไฟล์หาย/JSON พัง = log ชัดและปล่อยตารางว่าง (IsLoaded=false)
    /// </summary>
    public class BuildingDefPool
    {
        private const string ResourcePath = "Data/building_defs";

        private readonly Dictionary<string, BuildingDef> _byId =
            new Dictionary<string, BuildingDef>();
        private readonly List<BuildingDef> _all = new List<BuildingDef>();

        public bool IsLoaded { get; private set; }
        public int Count => _all.Count;

        public BuildingDefPool()
        {
            Load();
        }

        private void Load()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogError($"[BuildingDefPool] Could not find Resources/{ResourcePath}.json");
                return;
            }

            BuildingDefTable table;
            try
            {
                table = JsonUtility.FromJson<BuildingDefTable>(asset.text);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[BuildingDefPool] JSON parse failed: {ex.Message}");
                return;
            }

            if (table == null || table.buildings == null)
            {
                Debug.LogError("[BuildingDefPool] building_defs.json has no 'buildings' array");
                return;
            }

            foreach (var def in table.buildings)
            {
                if (def == null || string.IsNullOrEmpty(def.Id)) continue;

                if (_byId.ContainsKey(def.Id))
                {
                    Debug.LogWarning($"[BuildingDefPool] Duplicate building id: {def.Id} - skipping latter");
                    continue;
                }
                if (def.GridWidth <= 0 || def.GridHeight <= 0)
                {
                    Debug.LogWarning($"[BuildingDefPool] '{def.Id}' has non-positive footprint " +
                                     $"{def.GridWidth}x{def.GridHeight} - skipping");
                    continue;
                }
                _byId[def.Id] = def;
                _all.Add(def);
            }

            IsLoaded = true;
            Debug.Log($"[BuildingDefPool] Loaded {_all.Count} building defs " +
                      $"(Production={CountCategory(BuildingCategory.Production)}, " +
                      $"Convenience={CountCategory(BuildingCategory.Convenience)}, " +
                      $"Shop={CountCategory(BuildingCategory.Shop)}, " +
                      $"Landscape={CountCategory(BuildingCategory.Landscape)})");
        }

        public BuildingDef GetById(string defId)
        {
            if (string.IsNullOrEmpty(defId)) return null;
            BuildingDef def;
            return _byId.TryGetValue(defId, out def) ? def : null;
        }

        public IReadOnlyList<BuildingDef> GetAll() => _all;

        public IReadOnlyList<BuildingDef> GetByCategory(BuildingCategory category)
        {
            var result = new List<BuildingDef>();
            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i].CategoryValue == category) result.Add(_all[i]);
            }
            return result;
        }

        private int CountCategory(BuildingCategory category)
        {
            int n = 0;
            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i].CategoryValue == category) n++;
            }
            return n;
        }
    }
}
