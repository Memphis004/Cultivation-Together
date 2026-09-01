using System.Collections.Generic;
using UnityEngine;

namespace Xianxia.Sect
{
    [System.Serializable]
    public class AvatarPartDef
    {
        public string id;
        public string slot;
        public string category;       // "ใบหน้า" / "ลักษณะ" / "ร่างกาย" — เตรียม data ไว้ก่อน UI 2-layer
        public string displayName;
        public string spritePath;     // path under Resources/ ; "" = empty layer
        public string spritePathBack; // ผมหลัง — ว่าง = ชิ้นนี้ไม่มี back layer (เช่นผมสั้น)
        public string thumbPath;      // thumbnail สำหรับกริด — ว่าง = ใช้ spritePath หลัก
        public int    drawOrder;
        public bool   isDefault;
        public bool   tintable;       // true = slot นี้รองรับ color selection
        public int drawOrderBack;  // 0 = ใช้ drawOrder (ผมหลังใช้ 10)
        // AvatarRenderer.Rebuild(): เก็บเป็นคู่ (order, path) ก่อน sort
        // hair_back → (drawOrderBack=10), hair_front → (drawOrder=40)
        // แล้วใน JSON: hair ทุกทรง drawOrder: 40, drawOrderBack: 10 → stack จะตรงสเปก ACPart4 เป๊ะ: 
        // base 0 → hair_back 10 → body 20 → head 30 → face_marking 34 → hair_front 40 → accessory 50
    }

    [System.Serializable]
    public class AvatarPartTable
    {
        public List<AvatarPartDef> parts = new List<AvatarPartDef>();
    }

    /// <summary>
    /// Def-table for avatar parts, loaded from Resources/Data/avatar_parts.json.
    /// Plain C# class (registered in GameLifetimeScope), not a ScriptableObject.
    /// Same pattern as LubanEventPool.
    /// </summary>
    public class AvatarPartPool
    {
        private const string ResourcePath = "Data/avatar_parts";

        private readonly Dictionary<string, AvatarPartDef> _byId =
            new Dictionary<string, AvatarPartDef>();
        private readonly Dictionary<string, List<AvatarPartDef>> _bySlot =
            new Dictionary<string, List<AvatarPartDef>>();
        private readonly Dictionary<string, AvatarPartDef> _defaultBySlot =
            new Dictionary<string, AvatarPartDef>();

        public bool IsLoaded { get; private set; }

        public AvatarPartPool()
        {
            Load();
        }

        private void Load()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogError($"[AvatarPartPool] Could not find Resources/{ResourcePath}.json");
                return;
            }

            AvatarPartTable table;
            try
            {
                table = JsonUtility.FromJson<AvatarPartTable>(asset.text);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AvatarPartPool] JSON parse failed: {ex.Message}");
                return;
            }

            if (table == null || table.parts == null) return;

            foreach (var p in table.parts)
            {
                if (string.IsNullOrEmpty(p.id) || string.IsNullOrEmpty(p.slot)) continue;

                if (_byId.ContainsKey(p.id))
                {
                    Debug.LogWarning($"[AvatarPartPool] Duplicate partId: {p.id} - skipping latter");
                    continue;
                }
                _byId[p.id] = p;

                List<AvatarPartDef> list;
                if (!_bySlot.TryGetValue(p.slot, out list))
                {
                    list = new List<AvatarPartDef>();
                    _bySlot[p.slot] = list;
                }
                list.Add(p);

                if (p.isDefault && !_defaultBySlot.ContainsKey(p.slot))
                    _defaultBySlot[p.slot] = p;
            }

            IsLoaded = true;
            Debug.Log($"[AvatarPartPool] Loaded {_byId.Count} parts / {_bySlot.Count} slots");
        }

        public AvatarPartDef GetById(string partId)
        {
            if (string.IsNullOrEmpty(partId)) return null;
            AvatarPartDef def;
            return _byId.TryGetValue(partId, out def) ? def : null;
        }

        public AvatarPartDef GetDefaultForSlot(string slot)
        {
            AvatarPartDef def;
            return _defaultBySlot.TryGetValue(slot, out def) ? def : null;
        }

        /// <summary>Real resolve: partId empty or not found -> fallback to slot default</summary>
        public AvatarPartDef Resolve(string slot, string partId)
        {
            var def = GetById(partId);
            if (def != null && def.slot == slot) return def;
            return GetDefaultForSlot(slot);
        }

        public IReadOnlyList<AvatarPartDef> GetPartsForSlot(string slot)
        {
            List<AvatarPartDef> list;
            return _bySlot.TryGetValue(slot, out list)
                ? (IReadOnlyList<AvatarPartDef>)list
                : System.Array.Empty<AvatarPartDef>();
        }

        /// <summary>Used when validating ChangeAvatarPartRequest</summary>
        public bool IsValidForSlot(string slot, string partId)
        {
            if (string.IsNullOrEmpty(partId)) return true;   // "" = default, always valid
            var def = GetById(partId);
            return def != null && def.slot == slot;
        }
    }
}
