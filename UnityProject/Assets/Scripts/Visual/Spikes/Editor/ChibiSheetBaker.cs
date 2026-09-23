#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.EditorTools
{
    /// <summary>
    /// Phase 2 T1 — PRODUCTION placeholder chibi art generator. Deliberately a
    /// separate tool from the Phase-0 spike's PlaceholderSpriteBaker (C5: spike
    /// code is throwaway) — this one writes real .png assets into Resources,
    /// sets sprite import settings (96×96 grid, feet-center pivot) and packs a
    /// real Unity Sprite Atlas asset (C7 — no runtime-baked texture).
    ///
    /// Sheet layout (identical for every part — the batch invariant, L7):
    ///   - one row per anim state, row order = chibi_anim.json order (Idle, Walk)
    ///   - 6 cells per row, 96×96 per cell (L2 — flagged: pending human sign-off)
    ///   - pivot at feet-center (0.5, 0)
    ///   - sheets authored FACING RIGHT (left = flip at runtime, C8)
    /// Generated art is simple color-coded placeholders — Portrait PNGs are NOT
    /// touched (acceptance: Portrait stays byte-identical).
    /// </summary>
    public static class ChibiSheetBaker
    {
        private const string Menu = "Xianxia/Generate Chibi Placeholder Sheets";
        private const string OutputDir = "Assets/Resources/Data/Arts/Avatar/Chibi";
        private const string AtlasPath = "Assets/Resources/Data/Arts/Avatar/ChibiAtlas.spriteatlas";
        public const int Cell = 96;
        public const int FramesPerRow = 6;
        private const int AtlasPadding = 2;

        // body palette: grey/azure/white/black robes
        private static readonly Color[] RobeColors =
        {
            new Color(0.55f, 0.55f, 0.58f), new Color(0.20f, 0.35f, 0.65f),
            new Color(0.90f, 0.90f, 0.88f), new Color(0.15f, 0.15f, 0.17f),
        };

        [MenuItem(Menu)]
        public static void GenerateAll()
        {
            var states = ReadStateNames();
            int rows = states.Length;

            Directory.CreateDirectory(OutputDir);

            // Mirror of the generation plan encoded in avatar_parts.json (C6):
            // every chibi-visible part used by MockSectData founders + recruit starters.
            int generated = 0;
            generated += BakeBody("body_body_robe_grey", RobeColors[0], rows);
            generated += BakeBody("body_body_robe_azure", RobeColors[1], rows);
            generated += BakeBody("body_body_robe_white", RobeColors[2], rows);
            generated += BakeBody("body_body_robe_black", RobeColors[3], rows);

            generated += BakeHead("head_head_male_01", new Color(0.96f, 0.82f, 0.70f), rows);
            generated += BakeHead("head_head_male_elder", new Color(0.90f, 0.76f, 0.64f), rows);
            generated += BakeHead("head_head_female_01", new Color(0.98f, 0.85f, 0.74f), rows);

            generated += BakeHair("hair_hair_short", new Color(0.20f, 0.14f, 0.10f), rows, false);
            generated += BakeHair("hair_hair_topknot", new Color(0.25f, 0.17f, 0.10f), rows, false);
            generated += BakeHair("hair_hair_topknot_long", new Color(0.30f, 0.20f, 0.12f), rows, true);
            generated += BakeHair("hair_hair_twin_tail", new Color(0.45f, 0.28f, 0.16f), rows, true);
            generated += BakeHair("hair_hair_bald_beard", new Color(0.75f, 0.75f, 0.75f), rows, false);

            generated += BakeAccessory("accessory_acc_jade_crown", new Color(0.30f, 0.80f, 0.45f), rows);
            generated += BakeAccessory("accessory_acc_hairpin_silver", new Color(0.85f, 0.87f, 0.90f), rows);
            generated += BakeAccessory("accessory_acc_gourd", new Color(0.75f, 0.50f, 0.25f), rows);
            generated += BakeAccessory("accessory_acc_none", Color.clear, rows);

            generated += BakeFaceMarking("face_marking_face_marking_red_dot", new Color(0.85f, 0.15f, 0.15f), rows);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            CreateOrPackAtlas();

            Debug.Log("[ChibiSheetBaker] generated " + generated + " sheets into " + OutputDir +
                      " (" + rows + " state rows × " + FramesPerRow + " frames × " + Cell + "px, feet-center pivot)");
        }

        /// <summary>Row order must match ChibiFrameBank's state order (chibi_anim.json).</summary>
        private static string[] ReadStateNames()
        {
            var names = new List<string>();
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/Data/chibi_anim.json");
            if (asset != null)
            {
                var table = JsonUtility.FromJson<ChibiAnimTable>(asset.text);
                if (table != null && table.states != null)
                {
                    for (int i = 0; i < table.states.Count; i++)
                        if (!string.IsNullOrEmpty(table.states[i].state)) names.Add(table.states[i].state);
                }
            }
            if (names.Count == 0) names.Add("Idle"); // safe fallback
            return names.ToArray();
        }

        // ---- per-slot bakers (placeholder geometry, right-facing) ----

        private static int BakeBody(string name, Color robe, int rows)
        {
            return Bake(name, rows, (g, w, h, row, frame) =>
            {
                float bob = Mathf.Sin((frame / (float)FramesPerRow) * Mathf.PI * 2f) * 1.5f;
                float legSpread = (row == 1) ? 2.5f : 1.0f; // Walk rows swing wider
                // robe: trapezoid body
                RectBody(g, w, h, robe, bob, legSpread);
                // belt
                Fill(g, w, 30, 40 + (int)bob, 36, 6, robe * 0.6f);
            });
        }

        private static int BakeHead(string name, Color skin, int rows)
        {
            return Bake(name, rows, (g, w, h, row, frame) =>
            {
                float bob = Mathf.Sin((frame / (float)FramesPerRow) * Mathf.PI * 2f) * 1.5f;
                Fill(g, w, 26, 66 + (int)bob, 44, 28, skin); // face block
                // eyes (right-facing: offset right)
                Fill(g, w, 34, 78 + (int)bob, 5, 5, new Color(0.1f, 0.1f, 0.12f));
                Fill(g, w, 48, 78 + (int)bob, 5, 5, new Color(0.1f, 0.1f, 0.12f));
            });
        }

        private static int BakeHair(string name, Color hair, int rows, bool withBack)
        {
            int n = Bake(name, rows, (g, w, h, row, frame) =>
            {
                float bob = Mathf.Sin((frame / (float)FramesPerRow) * Mathf.PI * 2f) * 1.5f;
                Fill(g, w, 24, 86 + (int)bob, 48, 12, hair); // crown
                Fill(g, w, 22, 74 + (int)bob, 6, 16, hair);  // fringe (right side)
            });
            if (withBack)
            {
                n += Bake(name + "_back", rows, (g, w, h, row, frame) =>
                {
                    float sway = Mathf.Sin((frame / (float)FramesPerRow) * Mathf.PI * 2f) * 2f;
                    Fill(g, w, 34 - (int)sway, 46, 28, 42, hair); // long back hair mass
                });
            }
            return n;
        }

        private static int BakeAccessory(string name, Color accent, int rows)
        {
            return Bake(name, rows, (g, w, h, row, frame) =>
            {
                float bob = Mathf.Sin((frame / (float)FramesPerRow) * Mathf.PI * 2f) * 1.5f;
                if (name.Contains("crown"))
                    Fill(g, w, 30, 94 + (int)bob, 36, 7, accent);
                else if (name.Contains("hairpin"))
                    Fill(g, w, 60, 90 + (int)bob, 14, 4, accent);
                else if (name.Contains("gourd"))
                    Fill(g, w, 68, 52 + (int)bob, 12, 16, accent);
                // "none" stays fully transparent (empty layer by design)
            });
        }

        private static int BakeFaceMarking(string name, Color accent, int rows)
        {
            return Bake(name, rows, (g, w, h, row, frame) =>
            {
                float bob = Mathf.Sin((frame / (float)FramesPerRow) * Mathf.PI * 2f) * 1.5f;
                Fill(g, w, 40, 88 + (int)bob, 8, 5, accent); // red dot on forehead
            });
        }

        // ---- shared baking core ----

        private static int Bake(string name, int rows, System.Action<Color[], int, int, int, int> paint)
        {
            int w = FramesPerRow * Cell;
            int h = rows * Cell;
            var pixels = new Color[w * h];
            var clear = new Color(0f, 0f, 0f, 0f);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

            for (int row = 0; row < rows; row++)
                for (int f = 0; f < FramesPerRow; f++)
                    paint(pixels, w, h, row, f);

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels(pixels);
            tex.Apply();

            var png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            string path = OutputDir + "/" + name + ".png";
            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            ConfigureSheetImport(path, rows);
            return 1;
        }

        /// <summary>ISpriteEditorDataProvider — 96×96 grid, feet-center pivot (0.5, 0), one row per state.</summary>
        private static void ConfigureSheetImport(string path, int rows)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var data = factory.GetSpriteEditorDataProviderFromObject(importer);
            data.InitSpriteEditorDataProvider();

            var names = ReadStateNames();
            var rects = new List<SpriteRect>();
            int index = 0;
            for (int row = 0; row < rows && row < names.Length; row++)
            {
                for (int f = 0; f < FramesPerRow; f++)
                {
                    rects.Add(new SpriteRect
                    {
                        name = names[row] + "_" + f,
                        spriteID = new GUID(System.Guid.NewGuid().ToString("N")),
                        rect = new Rect(f * Cell, (rows - 1 - row) * Cell, Cell, Cell), // top row = state 0
                        pivot = new Vector2(0.5f, 0f),                                  // feet-center (C7)
                        alignment = SpriteAlignment.Custom,
                    });
                    index++;
                }
            }
            data.SetSpriteRects(rects.ToArray());
            data.Apply();
        }

        /// <summary>Pack every chibi sheet into ONE Sprite Atlas asset (C7 — real asset, not runtime bake).</summary>
        private static void CreateOrPackAtlas()
        {
            // SpriteAtlas settings/variant APIs live in UnityEditor.U2D.Sprites
            // (SpriteAtlasExtensions) — imported above.
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
            if (atlas == null)
            {
                atlas = new SpriteAtlas();
                atlas.SetIncludeInBuild(true);
                var settings = atlas.GetPackingSettings();
                settings.padding = AtlasPadding;
                settings.enableRotation = false;
                atlas.SetPackingSettings(settings);

                var texSettings = atlas.GetTextureSettings();
                texSettings.filterMode = FilterMode.Point;
                atlas.SetTextureSettings(texSettings);

                AssetDatabase.CreateAsset(atlas, AtlasPath);
            }

            // Replace the packable list with the sheet folder (UnityEditor.U2D extensions)
            var folder = AssetDatabase.LoadAssetAtPath<Object>(OutputDir);
            atlas.Remove(atlas.GetPackables());
            atlas.Add(new[] { folder });
            AssetDatabase.SaveAssets();
            Debug.Log("[ChibiSheetBaker] atlas packed: " + AtlasPath);
        }

        // ---- pixel helpers ----

        private static void RectBody(Color[] g, int w, int h, Color robe, float bob, float legSpread)
        {
            int top = 42 + (int)bob;
            Fill(g, w, 24, top, 48, 26, robe);
            // legs
            Fill(g, w, 30, 16, 8, 18, robe * 0.8f);
            Fill(g, w, 56 + (int)legSpread, 16, 8, 18, robe * 0.8f);
        }

        private static void Fill(Color[] g, int w, int x, int y, int rw, int rh, Color c)
        {
            for (int py = y; py < y + rh; py++)
            {
                if (py < 0 || py >= 96 * 8) break; // hard safety (sheets are at most 8 rows tall)
                for (int px = x; px < x + rw; px++)
                {
                    if (px < 0 || px >= w) continue;
                    g[py * w + px] = c;
                }
            }
        }
    }
}
#endif
