#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Xianxia.Sect.EditorTools
{
    /// <summary>
    /// Face split (Avatar Roadmap #1) — PRODUCTION placeholder portrait art generator,
    /// sibling of ChibiSheetBaker (same C5/C2 discipline: real .png assets into Resources,
    /// never runtime-baked).
    ///
    /// WHY: Assets/Resources/Avatar/ never existed on disk — every portrait spritePath in
    /// avatar_parts.json failed to load, so the AvatarCustomization panel never drew
    /// anything. This baker gives EVERY portrait part (old + the new face slots) a
    /// placeholder PNG so the draw-stack is verifiable end to end. Real art replaces
    /// these later (Roadmap #4) at the spec canvas 1024×1536 — placeholders use the same
    /// 2:3 ratio at 256×384 and the same invariant:
    ///
    ///   INVARIANT: every part is painted on the SAME canvas with the SAME head-center
    ///   (x=128, y=262, y-up) — swapping eyes/nose/mouth must never shift anything
    ///   (avatar-appearance §3.3).
    ///
    /// Face sub-layers are deliberately PORTRAIT-ONLY (chibiSheetPath stays "" — chibi
    /// face detail is cut by design, R4); the chibi keeps its baked-in features.
    /// </summary>
    public static class PortraitPlaceholderBaker
    {
        private const string Menu = "Xianxia/Generate Portrait Placeholder Sheets";
        private const string OutputDir = "Assets/Resources/Avatar";
        private const int W = 256;
        private const int H = 384;

        private static readonly Color RobeGrey  = new Color(0.55f, 0.55f, 0.58f);
        private static readonly Color RobeAzure = new Color(0.20f, 0.35f, 0.65f);
        private static readonly Color RobeWhite = new Color(0.90f, 0.90f, 0.88f);
        private static readonly Color RobeBlack = new Color(0.15f, 0.15f, 0.17f);

        private static readonly Color SkinMale   = new Color(0.96f, 0.82f, 0.70f);
        private static readonly Color SkinElder  = new Color(0.90f, 0.76f, 0.64f);
        private static readonly Color SkinFemale = new Color(0.98f, 0.85f, 0.74f);

        private static readonly Color HairShort  = new Color(0.20f, 0.14f, 0.10f);
        private static readonly Color HairTopknot = new Color(0.25f, 0.17f, 0.10f);
        private static readonly Color HairLong   = new Color(0.30f, 0.20f, 0.12f);
        private static readonly Color HairTwin   = new Color(0.45f, 0.28f, 0.16f);
        private static readonly Color Beard      = new Color(0.75f, 0.75f, 0.75f);

        private static readonly Color BrowColor = new Color(0.24f, 0.17f, 0.12f);
        private static readonly Color Sclera    = new Color(0.96f, 0.96f, 0.94f);
        private static readonly Color Pupil     = new Color(0.13f, 0.12f, 0.15f);
        private static readonly Color NoseColor = new Color(0.80f, 0.66f, 0.56f);
        private static readonly Color LipColor  = new Color(0.58f, 0.32f, 0.30f);

        [MenuItem(Menu)]
        public static void GenerateAll()
        {
            Directory.CreateDirectory(OutputDir);
            int n = 0;

            n += Bake("base_silhouette", g =>
            {
                Fill(g, 90, 210, 76, 104, new Color(0.30f, 0.30f, 0.34f)); // head block
                Fill(g, 80, 60, 96, 152, new Color(0.30f, 0.30f, 0.34f));  // torso
            });

            n += Bake("body_robe_grey",  g => PaintBody(g, RobeGrey));
            n += Bake("body_robe_azure", g => PaintBody(g, RobeAzure));
            n += Bake("body_robe_white", g => PaintBody(g, RobeWhite));
            n += Bake("body_robe_black", g => PaintBody(g, RobeBlack));

            n += Bake("head_male_01",   g => PaintHead(g, SkinMale));
            n += Bake("head_male_elder", g => PaintHead(g, SkinElder));
            n += Bake("head_female_01", g => PaintHead(g, SkinFemale));

            n += Bake("brows_straight", g =>
            {
                PaintBrowBars(g, 288, 291, BrowColor);
            });
            n += Bake("brows_thick", g => PaintBrowBars(g, 286, 292, BrowColor));
            n += Bake("brows_raised", g =>
            {
                PaintBrowBars(g, 288, 291, BrowColor);
                Fill(g, 116, 291, 6, 2, BrowColor); // inner lift
                Fill(g, 134, 291, 6, 2, BrowColor);
            });
            n += Bake("brows_thin", g => PaintBrowBars(g, 289, 290, BrowColor));

            n += Bake("eyes_round", g =>
            {
                PaintEyeSclera(g, 100, 118, 266, 280);
                PaintEyeSclera(g, 138, 156, 266, 280);
                Fill(g, 106, 268, 8, 10, Pupil);
                Fill(g, 144, 268, 8, 10, Pupil);
            });
            n += Bake("eyes_sharp", g =>
            {
                PaintEyeSclera(g, 100, 118, 270, 278);
                PaintEyeSclera(g, 138, 156, 270, 278);
                Fill(g, 105, 271, 9, 6, Pupil);
                Fill(g, 143, 271, 9, 6, Pupil);
            });
            n += Bake("eyes_kind", g =>
            {
                PaintEyeSclera(g, 101, 117, 268, 278);
                PaintEyeSclera(g, 139, 155, 268, 278);
                Fill(g, 105, 269, 8, 8, Pupil);
                Fill(g, 143, 269, 8, 8, Pupil);
            });
            n += Bake("eyes_narrow", g =>
            {
                PaintEyeSclera(g, 100, 118, 272, 277);
                PaintEyeSclera(g, 138, 156, 272, 277);
                Fill(g, 106, 273, 8, 3, Pupil);
                Fill(g, 144, 273, 8, 3, Pupil);
            });

            n += Bake("nose_small", g => Fill(g, 126, 250, 3, 6, NoseColor));
            n += Bake("nose_tall",  g => Fill(g, 126, 244, 3, 12, NoseColor));
            n += Bake("nose_wide", g =>
            {
                Fill(g, 122, 249, 12, 4, NoseColor);
                Fill(g, 120, 248, 2, 2, NoseColor * 0.85f);
                Fill(g, 134, 248, 2, 2, NoseColor * 0.85f);
            });

            n += Bake("mouth_neutral", g => Fill(g, 119, 236, 18, 2, LipColor));
            n += Bake("mouth_smile", g =>
            {
                Fill(g, 117, 239, 6, 2, LipColor);  // upturned ends
                Fill(g, 133, 239, 6, 2, LipColor);
                Fill(g, 123, 236, 10, 3, LipColor); // center
            });
            n += Bake("mouth_serious", g => Fill(g, 119, 235, 18, 3, new Color(0.45f, 0.24f, 0.24f)));
            n += Bake("mouth_open", g =>
            {
                Fill(g, 121, 232, 14, 8, new Color(0.42f, 0.20f, 0.22f));
                Fill(g, 121, 238, 14, 2, new Color(0.95f, 0.93f, 0.90f)); // teeth
            });

            n += Bake("eyeshadow_subtle", g => PaintEyeshadow(g, new Color(1.00f, 0.72f, 0.76f)));
            n += Bake("eyeshadow_red",    g => PaintEyeshadow(g, new Color(0.88f, 0.28f, 0.34f)));

            n += Bake("face_marking_red_dot", g => Fill(g, 124, 294, 8, 8, new Color(0.85f, 0.15f, 0.15f)));

            n += Bake("hair_short", g =>
            {
                Fill(g, 88, 302, 80, 20, HairShort);   // crown
                Fill(g, 148, 288, 18, 14, HairShort);  // fringe (right-facing)
            });
            n += Bake("hair_topknot", g =>
            {
                Fill(g, 88, 302, 80, 20, HairTopknot);
                Fill(g, 114, 322, 28, 24, HairTopknot); // bun
            });
            n += Bake("hair_topknot_long", g =>
            {
                Fill(g, 88, 302, 80, 20, HairLong);
                Fill(g, 114, 322, 28, 24, HairLong);
                Fill(g, 86, 240, 10, 68, HairLong);    // side locks
                Fill(g, 160, 240, 10, 68, HairLong);
            });
            n += Bake("hair_twin_tail", g =>
            {
                Fill(g, 88, 302, 80, 20, HairTwin);
                Fill(g, 62, 214, 22, 102, HairTwin);   // tails
                Fill(g, 172, 214, 22, 102, HairTwin);
            });
            n += Bake("hair_bald_beard", g => Fill(g, 98, 206, 60, 20, Beard)); // beard only

            n += Bake("hair_topknot_long_back", g => Fill(g, 82, 206, 92, 90, HairLong));
            n += Bake("hair_twin_tail_back", g =>
            {
                Fill(g, 68, 206, 22, 100, HairTwin);
                Fill(g, 166, 206, 22, 100, HairTwin);
            });

            n += Bake("acc_jade_crown", g =>
            {
                Fill(g, 102, 320, 52, 10, new Color(0.30f, 0.80f, 0.45f));
                Fill(g, 106, 330, 8, 10, new Color(0.30f, 0.80f, 0.45f));
                Fill(g, 122, 330, 8, 10, new Color(0.30f, 0.80f, 0.45f));
                Fill(g, 138, 330, 8, 10, new Color(0.30f, 0.80f, 0.45f));
            });
            n += Bake("acc_hairpin_silver", g =>
            {
                Fill(g, 152, 328, 32, 3, new Color(0.85f, 0.87f, 0.90f));
                Fill(g, 182, 326, 4, 7, new Color(0.85f, 0.87f, 0.90f));
            });
            n += Bake("acc_gourd", g =>
            {
                Fill(g, 176, 118, 18, 28, new Color(0.75f, 0.50f, 0.25f));
                Fill(g, 182, 146, 6, 6, new Color(0.55f, 0.36f, 0.18f));
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[PortraitPlaceholderBaker] generated " + n + " portrait sheets into " + OutputDir +
                      " (" + W + "x" + H + ", shared head-center 128,262)");
        }

        // ---- per-slot painters ----

        private static void PaintBody(Color[] g, Color robe)
        {
            Fill(g, 78, 66, 100, 148, robe);          // torso
            Fill(g, 58, 148, 34, 60, robe * 0.92f);   // left sleeve
            Fill(g, 164, 148, 34, 60, robe * 0.92f);  // right sleeve
            Fill(g, 120, 196, 16, 18, robe * 0.75f);  // collar notch
            Fill(g, 78, 118, 100, 6, robe * 0.55f);   // belt
        }

        private static void PaintHead(Color[] g, Color skin)
        {
            Fill(g, 94, 214, 68, 96, skin);           // face block
            Fill(g, 102, 206, 52, 8, skin * 0.94f);   // chin taper
            Fill(g, 88, 250, 6, 24, skin * 0.92f);    // ears
            Fill(g, 162, 250, 6, 24, skin * 0.92f);
        }

        private static void PaintBrowBars(Color[] g, int y0, int y1, Color c)
        {
            Fill(g, 102, y0, 20, y1 - y0, c);
            Fill(g, 134, y0, 20, y1 - y0, c);
        }

        private static void PaintEyeSclera(Color[] g, int x0, int x1, int y0, int y1)
        {
            Fill(g, x0, y0, x1 - x0, y1 - y0, Sclera);
        }

        private static void PaintEyeshadow(Color[] g, Color c)
        {
            Fill(g, 98, 282, 22, 4, c);
            Fill(g, 136, 282, 22, 4, c);
        }

        // ---- baking core (same discipline as ChibiSheetBaker) ----

        private static int Bake(string name, System.Action<Color[]> paint)
        {
            var pixels = new Color[W * H];
            var clear = new Color(0f, 0f, 0f, 0f);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

            paint(pixels);

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels(pixels);
            tex.Apply();

            var png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            string path = OutputDir + "/" + name + ".png";
            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            ConfigureImport(path);
            return 1;
        }

        /// <summary>Single sprite, point filtering, no mipmaps, uncompressed — crisp placeholder.</summary>
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

        /// <summary>Rect fill on the shared canvas, y-up (feet of the coordinate space at y=0).</summary>
        private static void Fill(Color[] g, int x, int y, int rw, int rh, Color c)
        {
            for (int py = y; py < y + rh; py++)
            {
                if (py < 0 || py >= H) continue;
                for (int px = x; px < x + rw; px++)
                {
                    if (px < 0 || px >= W) continue;
                    g[py * W + px] = c;
                }
            }
        }
    }
}
#endif
