#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.EditorTools
{
    /// <summary>
    /// AvatarIconBaker — อัด portrait ของศิษย์แต่ละคนเป็น PNG icon สี่เหลี่ยมจัตุรัส
    /// สำหรับการ์ดใน DiscipleList (แทนการให้ AvatarRenderer render สด 20-30 ใบใน scroll)
    ///
    /// ขั้นตอน: composite layer placeholder ทั้งหมดของ <see cref="AvatarAppearance"/>
    /// ลง RenderTexture ตามลำดับ <c>ResolvedLayer.Order</c> จาก <see cref="AppearanceResolver"/>
    /// (ห้าม hardcode ลำดับ layer — resolver เรียงแล้ว) แล้ว crop เป็นสี่เหลี่ยมจัตุรัส
    /// ด้วย <see cref="AvatarIconCrop"/> (normalized canvas coords — ไม่พอร์ตเลข
    /// pivotOffset/scale ของ AvatarFraming.Bust ที่เป็นหน่วย local ของ layerRoot)
    ///
    /// C5/C2 discipline (เดียวกับ PortraitPlaceholderBaker): เป็น Editor tool ที่เขียน
    /// real .png ลง Assets/ — <b>ห้าม bake ตอน runtime</b>
    ///
    /// INVARIANT ที่ยืนยันหลัง bake ทุกครั้ง: head-center (128, 262 y-up บน canvas
    /// 256×384) ต้องยังอยู่ใน crop — ถ้าไม่อยู่แปลว่า crop เพี้ยน ให้ยุติการ bake
    /// </summary>
    public static class AvatarIconBaker
    {
        private const string Menu = "Xianxia/Generate Disciple List Icons";
        private const string OutputDir = "Assets/Resources/Avatar/Icons";
        private const int IconSize = 256;   // RT สี่เหลี่ยมจัตุรัส (~ขนาด canvas ของ source)
        private const string CommandName = "bake_disciple_icons";

        /// <summary>ชื่อไฟล์ icon ของศิษย์ 1 คน (icon อื่นเรียกใช้เพื่อคง convention เดียว)</summary>
        public static string IconPath(string discipleId) => OutputDir + "/icon_" + discipleId + ".png";

        [MenuItem(Menu)]
        public static void GenerateAll()
        {
            var state = MockSectData.Create();
            var pool = new AvatarPartPool();
            var resolver = new AppearanceResolver(pool);

            Directory.CreateDirectory(OutputDir);
            var baked = new List<string>();

            foreach (var disciple in state.Disciples)
            {
                if (disciple?.Avatar == null) continue;
                var png = BakeIcon(disciple.Avatar, resolver);
                if (png == null) continue;

                var path = IconPath(disciple.DiscipleId);
                File.WriteAllBytes(path, png);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                ConfigureImport(path);
                baked.Add(disciple.DiscipleId);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[AvatarIconBaker] baked " + baked.Count + " icons into " + OutputDir +
                      " (" + IconSize + "x" + IconSize + ", square crop) — " + string.Join(", ", baked));
        }

        /// <summary>
        /// Bake 1 appearance → PNG bytes (public เพื่อทดสอบ/ใช้ซ้ำ) คืน null ถ้า fail
        /// </summary>
        public static byte[] BakeIcon(AvatarAppearance appearance, AppearanceResolver resolver)
        {
            if (appearance == null || resolver == null) return null;
            if (!AvatarIconCrop.ContainsHeadCenter())
            {
                Debug.LogError("[AvatarIconBaker] crop does not contain head-center (128,262) — fix AvatarIconCrop consts before baking");
                return null;
            }

            var layers = resolver.Resolve(appearance, VisualBackend.Portrait); // เรียงตาม Order แล้ว
            if (layers.Count == 0) return null;

            var rt = RenderTexture.GetTemporary(IconSize, IconSize, 0, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.clear);

            Texture2D readback = null;
            try
            {
                // crop เป็นจัตุรัสในหน่วย pixel ของ source canvas (ดู AvatarIconCrop)
                // → คัดลอกตรงไปยังปลายทางจัตุรัส ไม่ยืดสัดส่วน
                var square = AvatarIconCrop.NormalizedRect();

                foreach (var layer in layers)
                {
                    var sprite = LoadSprite(layer.Asset);
                    if (sprite == null) continue; // empty-layer part — resolver ข้ามให้อยู่แล้ว

                    var tex = sprite.texture;
                    // UV ของ sprite บน texture (placeholder = full texture, y-down)
                    var uMin = sprite.textureRect.xMin / tex.width;
                    var uMax = sprite.textureRect.xMax / tex.width;
                    var vMin = sprite.textureRect.yMin / tex.height;
                    var vMax = sprite.textureRect.yMax / tex.height;

                    // crop เป็น normalized y-up → UV y ต้องกลับด้าน
                    var src = new Rect(
                        Mathf.Lerp(uMin, uMax, square.xMin),
                        Mathf.Lerp(vMin, vMax, 1f - square.yMax),
                        square.width * (uMax - uMin),
                        square.height * (vMax - vMin));

                    // วาดลง RT ตรง ๆ — ไม่ต้องมี camera/scene object
                    Graphics.DrawTexture(new Rect(0, 0, IconSize, IconSize), tex, src,
                        0, 0, 0, 0, Color.white, null);
                }

                readback = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
                readback.ReadPixels(new Rect(0, 0, IconSize, IconSize), 0, 0);
                readback.Apply();
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }

            if (readback == null) return null;
            var png = readback.EncodeToPNG();
            Object.DestroyImmediate(readback);
            return png;
        }

        private static Sprite LoadSprite(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return Resources.Load<Sprite>(path);
        }

        /// <summary>Single sprite, point filtering, no mipmaps — เหมือน PortraitPlaceholderBaker</summary>
        private static void ConfigureImport(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }
}
#endif
