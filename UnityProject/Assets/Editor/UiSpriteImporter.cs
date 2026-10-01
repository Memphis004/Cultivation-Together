#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Xianxia.Sect.EditorTools
{
    /// <summary>
    /// Applies the correct TextureImporter settings to the DiscipleList window art
    /// in <c>Assets/Resources/ui/disciple_list</c>.
    ///
    /// Idempotent: run it as many times as you like, it always converges on the same
    /// settings and never creates or deletes assets. It only touches files named in
    /// the table below, so nothing else under Assets/Resources/ui is affected.
    ///
    /// The table mirrors <c>art/disciple_list/manifest.json</c>, which is the source
    /// of truth for target sizes and 9-slice borders. The manifest is JSON and Unity
    /// has no general JSON reader here (JsonUtility cannot express that document), so
    /// the values are baked in rather than parsed at edit time. To catch drift, every
    /// import checks the texture's ACTUAL pixel size against the expected size and
    /// logs an error if they disagree. If you regenerate the art, re-run
    /// `python tools/art/finalize.py` and update this table.
    ///
    /// Borders are recorded as (left, bottom, right, top), matching Vector4 order in
    /// Unity's TextureImporter.spriteBorder.
    /// </summary>
    public static class UiSpriteImporter
    {
        private const string Folder = "Assets/Resources/ui/disciple_list";
        private const int MaxTextureSize = 2048;

        private sealed class SpriteSpec
        {
            public readonly string FileName;
            public readonly int Width;
            public readonly int Height;
            public readonly int BorderLeft;
            public readonly int BorderBottom;
            public readonly int BorderRight;
            public readonly int BorderTop;
            public readonly int Seed;

            public SpriteSpec(string fileName, int width, int height, int seed,
                              int left, int bottom, int right, int top)
            {
                FileName = fileName;
                Width = width;
                Height = height;
                Seed = seed;
                BorderLeft = left;
                BorderBottom = bottom;
                BorderRight = right;
                BorderTop = top;
            }

            public bool Sliced => BorderLeft + BorderBottom + BorderRight + BorderTop > 0;
        }

        // width/height come from art/disciple_list/manifest.json (seed is the
        // checkpoint-B pick that produced the shipped file). Borders were
        // RE-MEASURED from the shipped PNG pixels (layout pass, see
        // Tools/art/measure_disciple_list_sprites.py) — order (l, b, r, t):
        //
        // scroll_paper (460x558): bright usable paper starts at L=45 T=20 R=42
        //   B=22 (luma-based inset; the old manifest border 38/0/36/0 left the
        //   top ink line + bottom roll edge inside the stretchable middle, so a
        //   stretched window smeared them). (45, 22, 42, 20).
        // scroll_rod (546x40): ~12px end fittings; 20px border keeps both caps
        //   intact so the rod can be Sliced to any length (vertical posts).
        // title_plate (320x54): end caps L=25 R=23 → 26/24 keeps them whole.
        // close_medallion / cloud_corner stay unsliced (irregular organic art).
        private static readonly SpriteSpec[] Specs =
        {
            //                file                     w    h  seed  l   b  r  t
            new SpriteSpec("scroll_paper.png",     460, 558, 101, 45, 22, 42, 20),
            new SpriteSpec("scroll_rod.png",       546,  40, 222, 20,  0, 20,  0),
            new SpriteSpec("close_medallion.png",  120, 239, 454,  0,  0,  0,  0),
            new SpriteSpec("cloud_corner.png",     180, 270, 353,  0,  0,  0,  0),
            new SpriteSpec("title_plate.png",      320,  54, 252, 26,  0, 24,  0),
        };

        [MenuItem("Xianxia/Import DiscipleList Art")]
        public static void Import()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                Debug.LogError("[UiSpriteImporter] folder not found: " + Folder);
                return;
            }

            var imported = new List<string>();
            var problems = new List<string>();

            foreach (var spec in Specs)
            {
                string path = Folder + "/" + spec.FileName;
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null)
                {
                    problems.Add(spec.FileName + ": not found at " + path);
                    continue;
                }

                // Drift guard: the shipped PNG must be the size the manifest claims.
                if (texture.width != spec.Width || texture.height != spec.Height)
                {
                    problems.Add(
                        spec.FileName + ": is " + texture.width + "x" + texture.height +
                        " but the manifest expects " + spec.Width + "x" + spec.Height);
                }

                ApplySettings(path, spec);

                var readBack = Verify(path, spec);
                if (readBack != null) problems.Add(readBack);
                else imported.Add(spec.FileName + " (seed " + spec.Seed + ")");
            }

            AssetDatabase.SaveAssets();

            if (problems.Count > 0)
            {
                foreach (var p in problems) Debug.LogError("[UiSpriteImporter] " + p);
                Debug.LogError("[UiSpriteImporter] finished with " + problems.Count + " problem(s)");
                return;
            }

            Debug.Log("[UiSpriteImporter] applied sprite settings to " + imported.Count +
                      " file(s) in " + Folder + ": " + string.Join(", ", imported));
        }

        private static void ApplySettings(string path, SpriteSpec spec)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;   // UI: never sample across the edge
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = MaxTextureSize;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.spriteBorder = new Vector4(
                spec.BorderLeft, spec.BorderBottom, spec.BorderRight, spec.BorderTop);

            importer.SaveAndReimport();

            // ForceUpdate as well: SaveAndReimport is normally enough, but this makes
            // a re-run on an unchanged file still resolve to a settled texture.
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        /// <summary>Read the settings back off disk; returns an error string, or null when correct.</summary>
        private static string Verify(string path, SpriteSpec spec)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                return spec.FileName + ": importer missing after import";
            }

            var errs = new List<string>();
            if (importer.textureType != TextureImporterType.Sprite) errs.Add("textureType");
            if (importer.spriteImportMode != SpriteImportMode.Single) errs.Add("spriteImportMode");
            if (!importer.alphaIsTransparency) errs.Add("alphaIsTransparency");
            if (importer.mipmapEnabled) errs.Add("mipmapEnabled");
            if (importer.filterMode != FilterMode.Bilinear) errs.Add("filterMode");
            if (importer.wrapMode != TextureWrapMode.Clamp) errs.Add("wrapMode");
            if (importer.maxTextureSize != MaxTextureSize) errs.Add("maxTextureSize");
            if (importer.crunchedCompression) errs.Add("crunchedCompression");
            if (importer.textureCompression == TextureImporterCompression.CompressedHQ)
                errs.Add("textureCompression-HQ");
            if (Mathf.Abs(importer.spriteBorder.x - spec.BorderLeft) > 0.5f) errs.Add("spriteBorder.left");
            if (Mathf.Abs(importer.spriteBorder.y - spec.BorderBottom) > 0.5f) errs.Add("spriteBorder.bottom");
            if (Mathf.Abs(importer.spriteBorder.z - spec.BorderRight) > 0.5f) errs.Add("spriteBorder.right");
            if (Mathf.Abs(importer.spriteBorder.w - spec.BorderTop) > 0.5f) errs.Add("spriteBorder.top");

            // A sliced sprite needs its Sprite (not just the Texture2D) to come back.
            if (spec.Sliced)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null) errs.Add("no Sprite sub-asset");
            }

            if (errs.Count == 0) return null;
            return spec.FileName + ": settings did not stick -> " + string.Join(", ", errs);
        }
    }
}
#endif
