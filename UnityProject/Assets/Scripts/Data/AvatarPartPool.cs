using System.Collections.Generic;
using UnityEngine;
using Xianxia.Sect.Visual;

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
        // --- SpriteSheet chibi (Phase 1 schema; asset มาใน Phase 2) ---
        public string chibiSheetPath;     // "" = part นี้ไม่มี layer บน chibi
        public string chibiSheetPathBack; // ผมหลังบน chibi — ว่าง = ไม่มี back layer
        public int    chibiOrder;         // 0 = ใช้ค่า default ของ slot
        public int    chibiOrderBack;     // 0 = ใช้ chibiOrder
        // --- Spine ---
        public string spineSkin;          // เช่น "hair/hair_topknot_long"; "" = ไม่มี
        // --- Access (Phase 5; ว่าง = free) ---
        public string entitlement;        // "" | "owner" | "dlc:<packId>"

        /// <summary>
        /// Empty-layer part: เจตนา "ไม่มี art ทุก backend" (ถอดชิ้นนี้ออก) —
        /// ต้องผ่าน coverage เสมอ เพราะคือวิธีที่ผู้เล่นถอด face_marking ฯลฯ
        /// (ยืนยันโดย *_none defaults ใน avatar_parts.json)
        /// </summary>
        public bool IsEmptyLayer
        {
            get
            {
                return string.IsNullOrEmpty(spritePath)
                    && string.IsNullOrEmpty(spritePathBack)
                    && string.IsNullOrEmpty(chibiSheetPath)
                    && string.IsNullOrEmpty(chibiSheetPathBack)
                    && string.IsNullOrEmpty(spineSkin);
            }
        }

        /// <summary>
        /// Coverage = คำนวณสดจาก path/skin ที่มี — ห้ามเก็บซ้ำเป็น field ใน JSON
        /// (C6 — กัน dual source of truth แบบที่เคยเกิดกับ outfit)
        /// </summary>
        public bool Supports(VisualBackend b)
        {
            switch (b)
            {
                case VisualBackend.Portrait:    return !string.IsNullOrEmpty(spritePath);
                case VisualBackend.SpriteSheet: return !string.IsNullOrEmpty(chibiSheetPath);
                case VisualBackend.Spine:       return !string.IsNullOrEmpty(spineSkin);
                default: return false;
            }
        }

        /// <summary>Back-layer path ของ backend นั้น — ว่างถ้าไม่มี back layer</summary>
        public string BackPathFor(VisualBackend b)
        {
            switch (b)
            {
                case VisualBackend.Portrait:    return spritePathBack;
                case VisualBackend.SpriteSheet: return chibiSheetPathBack;
                default: return string.Empty;   // Spine: back layer อยู่ใน rig แล้ว
            }
        }

        /// <summary>Draw order ของ layer หลักบน backend นั้น (0 = ใช้ค่า default ของ slot)</summary>
        public int OrderFor(VisualBackend b)
        {
            switch (b)
            {
                case VisualBackend.Portrait:    return drawOrder;
                case VisualBackend.SpriteSheet: return chibiOrder;
                default: return drawOrder;      // Spine ไม่ใช้ order จาก part
            }
        }

        /// <summary>Draw order ของ back layer (0 = ใช้ order ของ layer หลัก)</summary>
        public int BackOrderFor(VisualBackend b)
        {
            switch (b)
            {
                case VisualBackend.Portrait:    return drawOrderBack;
                case VisualBackend.SpriteSheet: return chibiOrderBack;
                default: return 0;
            }
        }

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

        /// <summary>
        /// Pose/sex-filtered listing: empty poseId = universal (fits any pose),
        /// otherwise must match; empty sexTag = any sex, otherwise must match.
        /// Used by UI randomize so a male disciple never rolls female-only parts
        /// (and vice versa). Pose คงที่ที่ pose_idle_01 ในตอนนี้.
        /// </summary>
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

        /// <summary>Used when validating ChangeAvatarPartRequest</summary>
        public bool IsValidForSlot(string slot, string partId)
        {
            if (string.IsNullOrEmpty(partId)) return true;   // "" = default, always valid
            var def = GetById(partId);
            return def != null && def.slot == slot;
        }

    }
}
