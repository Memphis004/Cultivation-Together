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
        public string poseId;      // pose template ที่ part วาดสำหรับ; "" = universal
        public string sexTag;      // "male" / "female" / "" = any
        // AvatarRenderer.Rebuild(): เก็บเป็นคู่ (order, path) ก่อน sort
        // hair_back → (drawOrderBack=10), hair_front → (drawOrder=40)
        // แล้วใน JSON: hair ทุกทรง drawOrder: 40, drawOrderBack: 10 → stack จะตรงสเปก ACPart4 เป๊ะ: 
        // base 0 → hair_back 10 → body 20 → head 30 → face_marking 34 → hair_front 40 → accessory 50
    }

    [System.Serializable]
    public class OutfitDef
    {
        public string id;
        public string displayName;
        public string poseId;
        public string sexTag;      // "male" / "female" / "" = any
        public string thumbPath;
        public Dictionary<string, string> parts; // slot → partId
    }

    [System.Serializable]
    public class AvatarPartTable
    {
        public List<AvatarPartDef> parts = new List<AvatarPartDef>();
        public List<OutfitDef> outfits = new List<OutfitDef>();
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
        private readonly Dictionary<string, OutfitDef> _outfitsById =
            new Dictionary<string, OutfitDef>();

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

            // Index outfits (JsonUtility ignores if JSON has no "outfits" key — old files safe)
            if (table.outfits != null)
            {
                for (int i = 0; i < table.outfits.Count; i++)
                {
                    var o = table.outfits[i];
                    if (string.IsNullOrEmpty(o.id)) continue;
                    if (_outfitsById.ContainsKey(o.id))
                    {
                        Debug.LogWarning($"[AvatarPartPool] Duplicate outfit id: {o.id} - skipping");
                        continue;
                    }
                    _outfitsById[o.id] = o;
                }
            }

            IsLoaded = true;
            Debug.Log($"[AvatarPartPool] Loaded {_byId.Count} parts / {_bySlot.Count} slots / {_outfitsById.Count} outfits");
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

        // ── Outfit methods (v3) ──────────────────────────────────

        public OutfitDef GetOutfit(string outfitId)
        {
            if (string.IsNullOrEmpty(outfitId)) return null;
            OutfitDef def;
            return _outfitsById.TryGetValue(outfitId, out def) ? def : null;
        }

        public IReadOnlyList<OutfitDef> GetOutfitsFor(string sex)
        {
            var result = new List<OutfitDef>();
            foreach (var kvp in _outfitsById)
            {
                var o = kvp.Value;
                if (string.IsNullOrEmpty(o.sexTag) || o.sexTag == sex)
                    result.Add(o);
            }
            return result;
        }

        public IReadOnlyList<AvatarPartDef> GetPartsForSlot(string slot, string poseId, string sex)
        {
            var result = new List<AvatarPartDef>();
            List<AvatarPartDef> list;
            if (!_bySlot.TryGetValue(slot, out list)) return result;

            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                // pose filter: empty poseId = universal, otherwise must match
                if (!string.IsNullOrEmpty(p.poseId) && p.poseId != poseId) continue;
                // sex filter: empty sexTag = universal, otherwise must match
                if (!string.IsNullOrEmpty(p.sexTag) && p.sexTag != sex) continue;
                result.Add(p);
            }
            return result;
        }
    }
}
