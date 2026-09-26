#if UNITY_EDITOR
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Xianxia.Sect.EditorTools
{
    /// <summary>
    /// แก้ warning "character ... not found in THSarabunPSK SDF" ที่ต้นเพื่อ:
    /// atlas เป็น Dynamic อยู่แล้ว แต่ m_IsMultiAtlasTexturesEnabled = 0 และ
    /// atlas แผ่นเดียว (1024×1024 @ 227pt) เต็มหลัง bake ชุดเดิม (~73 ตัว) →
    /// TMP เติมอักขระใหม่ (ช U+0E0A, ใ U+0E43, ...) ไม่ได้เลย
    ///
    /// ทางแก้ (ฟอนต์เดิม — ห้ามเปลี่ยนฟอนต์):
    /// 1) re-import asset จากดิสก์ก่อน (กัน instance ในหน่วยความจำเก่า)
    /// 2) เปิด multi-atlas textures ให้ dynamic population ขยายไปแผ่นใหม่ได้
    /// 3) TryAddCharacters เป็นชุดเล็ก (out missingCharacters รายชุด) — ชุดแรก
    ///    จะ trigger SetupNewAtlasTexture สร้างแผ่นที่ 2 เอง
    /// 4) persist ทุกครั้งที่ตารางโต (แม้บางชุด fail) — กันข้อมูลหายตอน reload
    ///
    /// Menu: Xianxia/Bake Thai Font Atlas (THSarabunPSK) — รันซ้ำได้ (idempotent)
    /// </summary>
    public static class ThaiFontAtlasBaker
    {
        private const string FontAssetPath = "Assets/Resources/Fonts/THSarabunPSK SDF.asset";
        private const int ChunkSize = 16;

        [MenuItem("Xianxia/Bake Thai Font Atlas (THSarabunPSK)")]
        public static void Bake()
        {
            // 0) บังคับ re-import จากดิสก์ — asset ที่โหลดค้างไว้ในหน่วยความจำ
            //    อาจเก่ากว่าไฟล์ (สถานะ multi-atlas ที่แก้บนดิสก์ต้องเข้า instance จริง)
            AssetDatabase.ImportAsset(FontAssetPath, ImportAssetOptions.ForceUpdate);

            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (fontAsset == null)
            {
                Debug.LogError("[ThaiFontAtlasBaker] font asset not found: " + FontAssetPath);
                return;
            }

            int before = fontAsset.characterTable.Count;
            Debug.Log("[ThaiFontAtlasBaker] BEFORE chars=" + before +
                      " mode=" + fontAsset.atlasPopulationMode +
                      " multiAtlas=" + fontAsset.isMultiAtlasTexturesEnabled +
                      " atlasTexCount=" + fontAsset.atlasTextures.Length);

            // 1) dynamic population ต้องขยายหลายแผ่นได้ ไม่งั้น atlas แรกที่เต็ม
            //    จะกลืนอักขระใหม่ทิ้งเงียบ ๆ (สาเหตุที่เดิมเติมช/ใ ไม่เคยสำเร็จ)
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            fontAsset.isMultiAtlasTexturesEnabled = true;

            // 2) ชุดอักขระเป้าหมาย: ASCII พิมพ์ได้ (UI ใช้ "Rank: ", "Spirit Stones: ")
            //    + Thai block U+0E01–U+0E5B
            var sb = new StringBuilder(200);
            for (int c = 32; c <= 126; c++) sb.Append((char)c);
            for (int c = 0x0E01; c <= 0x0E5B; c++) sb.Append((char)c);
            string all = sb.ToString();

            // 3) ถ้าตารางครบเป้าหมายแล้ว ข้าม bake เลย — TryAddCharacters เมื่อ
            //    m_GlyphsToAdd ว่าง จะคืน false พร้อม missing=input ทั้งก้อน
            //    (ลายเซ็น “ไม่มีอะไรจะเพิ่ม” ไม่ใช่ความล้มเหลว)
            if (fontAsset.HasCharacters(all))
            {
                Debug.Log("[ThaiFontAtlasBaker] charset already complete — skip bake (chars=" + before + ")");
            }
            else
            {
                // bake เป็นชุดเล็ก — เห็นชัดว่าชุดไหนพัง และชุดแรกจะเป็นตัวจุด
                // SetupNewAtlasTexture (แผ่นใหม่) ของ multi-atlas
                for (int offset = 0; offset < all.Length; offset += ChunkSize)
                {
                    int len = Mathf.Min(ChunkSize, all.Length - offset);
                    string chunk = all.Substring(offset, len);
                    int chunkBefore = fontAsset.characterTable.Count;

                    string missing;
                    bool ok = fontAsset.TryAddCharacters(chunk, out missing);
                    if (!ok)
                    {
                        bool grew = fontAsset.characterTable.Count > chunkBefore;
                        if (!grew && missing == chunk)
                            continue; // ทุกตัวใน chunk มีอยู่แล้ว — no-op ไม่ใช่ failure

                        Debug.LogWarning("[ThaiFontAtlasBaker] chunk partially failed: \"" + chunk + "\" missing=[" + (missing ?? "∅") + "]");
                    }
                }
            }

            int after = fontAsset.characterTable.Count;
            bool complete = fontAsset.HasCharacters(all);
            Debug.Log("[ThaiFontAtlasBaker] RESULT chars=" + before + "->" + after +
                      " complete=" + complete +
                      " multiAtlas=" + fontAsset.isMultiAtlasTexturesEnabled +
                      " atlasTexCount=" + fontAsset.atlasTextures.Length);

            // 4) persist เสมอเมื่อตารางโตหรือ flag เปลี่ยน — ไม่งั้น dynamic edits
            //    หายตอน domain reload
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[ThaiFontAtlasBaker] persisted to " + FontAssetPath +
                      " (chars=" + fontAsset.characterTable.Count + ")");
        }
    }
}
#endif
