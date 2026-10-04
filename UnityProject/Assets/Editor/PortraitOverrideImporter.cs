#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Xianxia.Sect.EditorTools
{
    /// <summary>
    /// Portrait v2 — ตั้ง TextureImporter ของ portrait override PNG
    /// (Assets/Resources/Avatar/Portraits/) แบบ idempotent: เช็คก่อนทุก field
    /// เขียนเฉพาะที่ต่าง → reimport เฉพับที่เปลี่ยน รันซ้ำได้ไม่เปลี่ยนอะไรเพิ่ม
    ///
    /// Import contract ของ portrait sprite (ตั้งค่าเหมือนกันทุกไฟล์):
    ///   Sprite / Single, alphaIsTransparency, mipmap off, Bilinear,
    ///   ไม่บีบอัด (no crunch), maxSize 2048
    /// (icon ใน Avatar/Icons ใช้ Point filter ตาม AvatarIconBaker.ConfigureImport —
    ///  คนละ contract กับ portrait ห้ามสลับกัน)
    ///
    /// ทริกเกอร์ผ่าน GeneratorRemoteControl command "import_portrait_overrides"
    /// </summary>
    public static class PortraitOverrideImporter
    {
        private const string Dir = "Assets/Resources/Avatar/Portraits";
        private const int MaxSize = 2048;

        public static void Import()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
            {
                Debug.LogWarning("[PortraitOverrideImporter] ไม่มีโฟลเดอร์ " + Dir + " — ไม่มีอะไรให้ import");
                return;
            }

            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { Dir });
            Array.Sort(guids, StringComparer.Ordinal);

            var changedCount = 0;
            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                var changed = false;

                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    changed = true;
                }
                if (importer.spriteImportMode != SpriteImportMode.Single)
                {
                    importer.spriteImportMode = SpriteImportMode.Single;
                    changed = true;
                }
                if (!importer.alphaIsTransparency)
                {
                    importer.alphaIsTransparency = true;
                    changed = true;
                }
                if (importer.mipmapEnabled)
                {
                    importer.mipmapEnabled = false;
                    changed = true;
                }
                if (importer.filterMode != FilterMode.Bilinear)
                {
                    importer.filterMode = FilterMode.Bilinear;
                    changed = true;
                }
                if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                {
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    changed = true;
                }
                if (importer.crunchedCompression)
                {
                    importer.crunchedCompression = false;
                    changed = true;
                }
                if (importer.maxTextureSize != MaxSize)
                {
                    importer.maxTextureSize = MaxSize;
                    changed = true;
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                    changedCount++;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[PortraitOverrideImporter] checked " + guids.Length +
                      " texture(s) in " + Dir + ", reimported " + changedCount);
        }
    }
}
#endif
