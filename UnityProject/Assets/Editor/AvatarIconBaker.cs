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
    /// ตามลำดับ <c>ResolvedLayer.Order</c> จาก <see cref="AppearanceResolver"/>
    /// (ห้าม hardcode ลำดับ layer — resolver เรียงแล้ว) แล้ว crop เป็นสี่เหลี่ยมจัตุรัส
    /// ด้วย <see cref="AvatarIconCrop"/> (normalized canvas coords — ไม่พอร์ตเลข
    /// pivotOffset/scale ของ AvatarFraming.Bust ที่เป็นหน่วย local ของ layerRoot)
    ///
    /// Portrait v2: ศิษย์ที่มี entry ใน <c>Resources/Data/portrait_overrides</c> อบจาก
    /// portrait เดี่ยว (crop เดียวกัน — canvas convention เดิม) ทุกคนอื่น = layered เดิม
    ///
    /// <b>ทำ composite บน CPU</b> (อ่าน PNG ต้นทาง → crop → bilinear → alpha over)
    /// ไม่ใช้ <c>Graphics.DrawTexture</c> + RenderTexture: วิธีนั้นผลิตแถบสีขาว
    /// ใน Editor session นี้ (icon ที่ commit ไว้ก่อนหน้านี้ก็เป็นแถบ) เพราะ
    /// immediate-mode draw ไม่ลง RenderTexture — CPU path ได้ผลเดียวกันทุกเครื่อง/
    /// ทุก color space และ deterministic (ไม่พึ่งสถานะ GPU)
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
        private const int IconSize = 256;   // ไฟล์ปลายทางสี่เหลี่ยมจัตุรัส
        private const string CommandName = "bake_disciple_icons";

        /// <summary>ชื่อไฟล์ icon ของศิษย์ 1 คน (icon อื่นเรียกใช้เพื่อคง convention เดียว)</summary>
        public static string IconPath(string discipleId) => OutputDir + "/icon_" + discipleId + ".png";

        [MenuItem(Menu)]
        public static void GenerateAll()
        {
            var state = MockSectData.Create();
            var pool = new AvatarPartPool();
            var resolver = new AppearanceResolver(pool);
            var portraitOverrides = new PortraitOverrideMap(); // Portrait v2

            Directory.CreateDirectory(OutputDir);
            var baked = new List<string>();

            foreach (var disciple in state.Disciples)
            {
                if (disciple?.Avatar == null) continue;

                // Portrait v2: ศิษย์ที่มี portrait override อบ icon จากภาพเดี่ยว
                // (crop เดียวกัน) ไม่มีในตาราง (รวม recruit ใหม่) = layered เดิม
                byte[] png;
                var overridePath = portraitOverrides.ResolveOverride(disciple.DiscipleId);
                if (!string.IsNullOrEmpty(overridePath))
                    png = BakeIconFromSprite(LoadSprite(overridePath));
                else
                    png = BakeIcon(disciple.Avatar, resolver);
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
            if (!CropGuard()) return null;

            var layers = resolver.Resolve(appearance, VisualBackend.Portrait); // เรียงตาม Order แล้ว
            if (layers.Count == 0) return null;

            var acc = new float[IconSize * IconSize * 4];
            for (int i = 0; i < layers.Count; i++)
            {
                var sprite = LoadSprite(layers[i].Asset);
                if (sprite == null) continue; // empty-layer part — resolver ข้ามให้อยู่แล้ว
                CompositeCropped(acc, sprite);
            }
            return Encode(acc);
        }

        /// <summary>
        /// Portrait v2: bake icon 1:1 จาก portrait override sprite — ใช้ crop เดียวกับ
        /// layered path (normalized canvas coords ของ AvatarIconCrop) คืน null ถ้า fail
        /// </summary>
        public static byte[] BakeIconFromSprite(Sprite sprite)
        {
            if (sprite == null) return null;
            if (!CropGuard()) return null;

            var acc = new float[IconSize * IconSize * 4];
            CompositeCropped(acc, sprite);
            return Encode(acc);
        }

        private static bool CropGuard()
        {
            if (AvatarIconCrop.ContainsHeadCenter()) return true;
            Debug.LogError("[AvatarIconBaker] crop does not contain head-center (128,262) — fix AvatarIconCrop consts before baking");
            return false;
        }

        /// <summary>
        /// อ่าน sprite ต้นทางจากไฟล์ (readable) แล้ว crop ตาม AvatarIconCrop + ย่อเป็น
        /// IconSize ด้วย bilinear จากนั้น alpha-over ทับ accumulator (ตรงกับ
        /// blend ปกติของ UI: src ทับ dst)
        /// </summary>
        private static void CompositeCropped(float[] acc, Sprite sprite)
        {
            int srcW, srcH;
            var srcPixels = LoadReadablePixels(sprite, out srcW, out srcH);
            if (srcPixels == null) return;

            var square = AvatarIconCrop.NormalizedRect(); // normalized, y-up
            float rx = square.xMin * srcW;
            float rw = square.width * srcW;
            // Texture2D.GetPixels32() นับแถวจากล่างขึ้น (y-up) ตรงกับ normalized
            // coords ของ AvatarIconCrop — ห้ามกลับด้านซ้ำ (เคยได้ช่วงกุ่งแทนช่วงหัว)
            float ry = square.yMin * srcH;
            float rh = square.height * srcH;

            for (int dy = 0; dy < IconSize; dy++)
            {
                float sy = ry + (dy + 0.5f) * rh / IconSize - 0.5f;
                for (int dx = 0; dx < IconSize; dx++)
                {
                    float sx = rx + (dx + 0.5f) * rw / IconSize - 0.5f;

                    float sr, sg, sb, sa;
                    SampleBilinear(srcPixels, srcW, srcH, sx, sy, out sr, out sg, out sb, out sa);
                    if (sa <= 0f) continue;

                    int di = (dy * IconSize + dx) * 4;
                    float da = acc[di + 3];
                    float outA = sa + da * (1f - sa);
                    if (outA <= 0f) { acc[di] = acc[di + 1] = acc[di + 2] = acc[di + 3] = 0f; continue; }
                    float inv = da * (1f - sa);
                    acc[di]     = (sr * sa + acc[di]     * inv) / outA;
                    acc[di + 1] = (sg * sa + acc[di + 1] * inv) / outA;
                    acc[di + 2] = (sb * sa + acc[di + 2] * inv) / outA;
                    acc[di + 3] = outA;
                }
            }
        }

        /// <summary>
        /// โหลดพิกเซลจากไฟล์ต้นทางโดยตรง (LoadImage = readable เสมอ ไม่ต้องพึ่ง
        /// isReadable ของ texture ในโปรเจกต์) — คืน null ถ้าไฟล์อ่านไม่ได้
        /// </summary>
        private static Color32[] LoadReadablePixels(Sprite sprite, out int width, out int height)
        {
            width = height = 0;
            var tex = sprite.texture;
            if (tex == null) return null;

            var assetPath = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(assetPath) ||
                !assetPath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)) return null;

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(assetPath);
            }
            catch (System.IO.IOException)
            {
                return null;
            }

            var temp = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!temp.LoadImage(bytes)) return null;
                width = temp.width;
                height = temp.height;
                return temp.GetPixels32();
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        /// <summary>bilinear + clamp ขอบ (ภาพ placeholder/portrait ต้องไม่มีขอบหยัก) — y เป็นแถวจากล่าง (Unity convention)</summary>
        private static void SampleBilinear(Color32[] px, int w, int h, float x, float y,
                                           out float r, out float g, out float b, out float a)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, w - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, h - 1);
            int x1 = Mathf.Clamp(x0 + 1, 0, w - 1);
            int y1 = Mathf.Clamp(y0 + 1, 0, h - 1);
            float fx = Mathf.Clamp01(x - x0);
            float fy = Mathf.Clamp01(y - y0);

            var p00 = px[y0 * w + x0]; var p10 = px[y0 * w + x1];
            var p01 = px[y1 * w + x0]; var p11 = px[y1 * w + x1];

            r = Bilinear(p00.r, p10.r, p01.r, p11.r, fx, fy) / 255f;
            g = Bilinear(p00.g, p10.g, p01.g, p11.g, fx, fy) / 255f;
            b = Bilinear(p00.b, p10.b, p01.b, p11.b, fx, fy) / 255f;
            a = Bilinear(p00.a, p10.a, p01.a, p11.a, fx, fy) / 255f;
        }

        private static float Bilinear(float v00, float v10, float v01, float v11, float fx, float fy)
        {
            float top = v00 + (v10 - v00) * fx;
            float bot = v01 + (v11 - v01) * fx;
            return top + (bot - top) * fy;
        }

        private static byte[] Encode(float[] acc)
        {
            var tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
            try
            {
                var px = new Color32[IconSize * IconSize];
                for (int i = 0; i < px.Length; i++)
                {
                    int o = i * 4;
                    px[i] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(acc[o] * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(acc[o + 1] * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(acc[o + 2] * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(acc[o + 3] * 255f), 0, 255));
                }
                tex.SetPixels32(px);
                tex.Apply();
                return tex.EncodeToPNG();
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
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