using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Xianxia.Sect.EditorTools
{
    /// <summary>
    /// Phase 1 T6 — Editor coverage validator (edit-time เท่านั้น — C8)
    /// อยู่ใน Visual.Spikes.Editor asmdef พร้อม remote control เพื่อให้ asmdef
    /// อ้างถึงตัวมันเองได้ (agent เรียกผ่าน marker-file command "validate")
    ///
    /// ตรวจ:
    /// 1. AvatarPartDef coverage ตาม backend ที่ VisualRuntimeConfig เปิด (L4):
    ///    - Portrait: path ต้องมีไฟล์จริง (Resources.Load&lt;Sprite&gt; ได้)
    ///    - SpriteSheet: chibiSheetPath ต้องไม่ว่างสำหรับ part ที่ roster ใช้จริง (Phase 2 C6)
    ///      — ตรวจเฉพาะเมื่อ VisualRuntimeConfig.SpriteSheetEnabled เปิดอยู่
    ///    - Spine: field ว่าง = ไม่มี layer (OK — asset มา Phase 3)
    ///    - spineSkin ชื่อซ้ำ
    /// 2. MockSectData ทุก DiscipleState: head/hair ตรง Sex (ผ่าน sexTag ที่มีอยู่แล้ว)
    ///    - ต้องรายงาน d001 "Lin Feng" (Female) ใช้ head_male_01 — บั๊กที่มีอยู่แล้ว
    ///      (ห้ามแก้ MockSectData เอง — แค่รายงาน, L7 Resemblance rule)
    /// </summary>
    public static class VisualCoverageValidator
    {
        private const string MenuRoot = "Xianxia/Validate Visual Coverage";

        [MenuItem(MenuRoot)]
        public static void Validate()
        {
            int errors = 0, warnings = 0;
            var pool = new AvatarPartPool();

            Debug.Log("=== [VisualCoverageValidator] START ===");

            var allParts = new List<AvatarPartDef>();
            foreach (var slot in AvatarSlots.Equippable)
            {
                var list = pool.GetPartsForSlot(slot);
                for (int i = 0; i < list.Count; i++) allParts.Add(list[i]);
            }

            // ---- 1) Portrait path ต้องมีไฟล์จริง ----
            foreach (var p in allParts)
            {
                if (string.IsNullOrEmpty(p.spritePath)) continue;
                if (AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/" + p.spritePath + ".png") == null)
                {
                    Debug.LogWarning("[Validator] Portrait sprite missing on disk: '" + p.id +
                                     "' -> Resources/" + p.spritePath);
                    warnings++;
                }
            }

            // ---- 2) spineSkin ซ้ำ ----
            var spineOwners = new Dictionary<string, List<string>>();
            foreach (var p in allParts)
            {
                if (string.IsNullOrEmpty(p.spineSkin)) continue;
                List<string> owners;
                if (!spineOwners.TryGetValue(p.spineSkin, out owners))
                {
                    owners = new List<string>();
                    spineOwners[p.spineSkin] = owners;
                }
                owners.Add(p.id);
            }
            foreach (var kvp in spineOwners)
            {
                if (kvp.Value.Count > 1)
                {
                    Debug.LogError("[Validator] duplicate spineSkin '" + kvp.Key + "' on parts: " +
                                   string.Join(", ", kvp.Value.ToArray()));
                    errors++;
                }
            }

            // ---- 3) MockSectData sex/appearance mismatch (L7: รายงาน ไม่แก้) ----
            // + Phase 2 C6: ทุก part ที่ roster ใช้จริงต้องมี chibiSheetPath ก่อนเปิด SpriteSheetEnabled
            var state = MockSectData.Create();
            var usedPartIds = new HashSet<string>();
            foreach (var d in state.Disciples)
            {
                if (d.Avatar == null) continue;

                foreach (var slot in AvatarSlots.Equippable)
                {
                    string pid = d.Avatar.GetSlot(slot);
                    if (!string.IsNullOrEmpty(pid)) usedPartIds.Add(pid);
                }

                string headPartId = d.Avatar.GetSlot(AvatarSlots.Head);
                if (string.IsNullOrEmpty(headPartId)) continue;

                var headDef = pool.GetById(headPartId);
                if (headDef == null)
                {
                    Debug.LogError("[Validator] " + d.DiscipleId + " '" + d.DisplayName +
                                   "': head part '" + headPartId + "' not found in avatar_parts.json");
                    errors++;
                    continue;
                }

                string expectedTag = SexTag(d.Sex);
                bool mismatch = !string.IsNullOrEmpty(headDef.sexTag) && headDef.sexTag != expectedTag;
                if (mismatch)
                {
                    Debug.LogWarning("[Validator] SEX MISMATCH (known bug, reported not fixed): " +
                        d.DiscipleId + " '" + d.DisplayName + "' Sex=" + d.Sex +
                        " but head part '" + headPartId + "' sexTag='" + headDef.sexTag + "'");
                    warnings++;
                }
                else
                {
                    Debug.Log("[Validator] " + d.DiscipleId + " '" + d.DisplayName +
                              "' head '" + headPartId + "' OK (sex=" + d.Sex + ")");
                }
            }

            // Recruit starter parts (SectStateProvider.CreateStarterAvatar) เป็นชุดที่ศิษย์ใหม่ใช้จริง (C6)
            usedPartIds.Add("body_robe_grey");
            usedPartIds.Add("head_male_01");
            usedPartIds.Add("head_female_01");
            usedPartIds.Add("hair_short");
            usedPartIds.Add("hair_topknot");
            usedPartIds.Add("hair_twin_tail");

            // ---- 4) Chibi coverage ของ part ที่ roster ใช้จริง (Phase 2 C6) ----
            foreach (var pid in usedPartIds)
            {
                var def = pool.GetById(pid);
                if (def == null) continue; // unknown id — ถูกจับที่ section 3 อยู่แล้ว

                if (string.IsNullOrEmpty(def.chibiSheetPath))
                {
                    Debug.LogWarning("[Validator] part '" + pid + "' (slot '" + def.slot +
                                     "') is used by the live roster but has NO chibiSheetPath — " +
                                     "chibi will drop this slot");
                    warnings++;
                }
                else if (!System.IO.File.Exists("Assets/Resources/Data/Arts/Avatar/" + def.chibiSheetPath + ".png"))
                {
                    Debug.LogWarning("[Validator] chibi sheet missing on disk: '" + pid +
                                     "' -> Resources/Data/Arts/Avatar/" + def.chibiSheetPath + ".png " +
                                     "(run Xianxia/Generate Chibi Placeholder Sheets)");
                    warnings++;
                }
            }

            // ---- Summary ----
            Debug.Log("=== [VisualCoverageValidator] DONE: " + errors + " error(s), " + warnings + " warning(s) ===");
            if (errors > 0) EditorUtility.DisplayDialog("Visual Coverage Validator",
                errors + " error(s), " + warnings + " warning(s) - see Console.", "OK");
        }

        private static string SexTag(DiscipleSex sex)
        {
            switch (sex)
            {
                case DiscipleSex.Male:   return "male";
                case DiscipleSex.Female: return "female";
                default:                 return "";
            }
        }
    }
}
