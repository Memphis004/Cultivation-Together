using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Xianxia.Sect.Visual.Spikes
{
    /// <summary>
    /// Editor-only placeholder art for S2/S3. Bakes ONE atlas texture shared by
    /// every slot (L7: single atlas → batch ≈ 1, not per-instance), plus per-slot
    /// sheet sprites cut from that atlas on the SAME 12-frame grid with the SAME
    /// feet-center pivot. Generated textures are read-only data for spikes.
    /// </summary>
    public static class PlaceholderSpriteBaker
    {
        public const int CellSize = 96;      // S3 candidate cell
        public const int FramesPerSheet = 12;
        private const int CellsPerRow = 6;   // 6x2 grid per sheet inside the atlas

        public sealed class BakedResult
        {
            public Texture2D Atlas;
            public Sprite[] AtlasSprites;          // every frame of every sheet (atlas page)
            public Dictionary<string, Sprite[]> SheetsBySlot;
            public Sprite Body;
        }

        private static readonly string[] Slots =
        {
            "body", "hair_back", "head", "face_marking", "hair_front", "accessory"
        };

        private static BakedResult _cached;

#if UNITY_EDITOR
        public static BakedResult GetOrCreate()
        {
            if (_cached != null) return _cached;

            // One atlas: 6 sheets × 12 frames on a shared 96×96 grid.
            int framesWide = CellsPerRow * CellSize;                     // 576
            int framesHigh = (FramesPerSheet / CellsPerRow) * CellSize;  // 192
            int atlasW = framesWide;
            int atlasH = framesHigh * Slots.Length;                      // 6 sheets stacked

            var atlas = new Texture2D(atlasW, atlasH, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[atlasW * atlasH];
            FillAtlasPixels(pixels, atlasW, atlasH);

            atlas.SetPixels32(pixels);
            atlas.Apply(false, false);

            var sheetsBySlot = new Dictionary<string, Sprite[]>();
            var atlasSprites = new List<Sprite>(Slots.Length * FramesPerSheet);
            Sprite body = null;

            for (int s = 0; s < Slots.Length; s++)
            {
                string slot = Slots[s];
                int sheetTop = s * framesHigh;
                var frames = new Sprite[FramesPerSheet];
                for (int f = 0; f < FramesPerSheet; f++)
                {
                    int col = f % CellsPerRow;
                    int row = f / CellsPerRow;
                    var rect = new Rect(col * CellSize, atlasH - sheetTop - (row + 1) * CellSize, CellSize, CellSize);
                    var pivot = new Vector2(0.5f, 0f); // feet center — invariant across all slots (L7)
                    var sprite = Sprite.Create(atlas, rect, pivot, CellSize / 1.5f, 0, SpriteMeshType.FullRect);
                    sprite.name = slot + "_f" + f;
                    frames[f] = sprite;
                    atlasSprites.Add(sprite);
                    if (slot == "body" && f == 0) body = sprite;
                }
                sheetsBySlot[slot] = frames;
            }

            _cached = new BakedResult
            {
                Atlas = atlas,
                AtlasSprites = atlasSprites.ToArray(),
                SheetsBySlot = sheetsBySlot,
                Body = body
            };
            return _cached;
        }

        private static void FillAtlasPixels(Color32[] pixels, int atlasW, int atlasH)
        {
            var cBody = new Color32(96, 160, 220, 255);
            var cHairBack = new Color32(70, 50, 40, 255);
            var cHead = new Color32(250, 225, 190, 255);
            var cMarking = new Color32(200, 60, 60, 255);
            var cHairFront = new Color32(110, 80, 60, 255);
            var cAccessory = new Color32(240, 200, 60, 255);
            var transparent = new Color32(0, 0, 0, 0);

            // Slot-specific silhouette rects inside each 96×96 cell, feet at pivot y=0.
            for (int s = 0; s < Slots.Length; s++)
            {
                int sheetTop = s * (FramesPerSheet / CellsPerRow) * CellSize;
                for (int f = 0; f < FramesPerSheet; f++)
                {
                    int col = f % CellsPerRow;
                    int row = f / CellsPerRow;
                    int x0 = col * CellSize;
                    int yTop = atlasH - sheetTop - row * CellSize; // top edge of this cell
                    var color = SlotColor(Slots[s], cBody, cHairBack, cHead, cMarking, cHairFront, cAccessory);

                    // Body bob: frames 0..11 shift up/down 1px so the clock visibly animates.
                    int bob = f % 4 == 1 ? 1 : (f % 4 == 3 ? -1 : 0);

                    FillRect(pixels, atlasW, x0 + 30, yTop - 4 + bob, 36, 44, color);          // torso-ish
                    if (Slots[s] == "head" || Slots[s] == "hair_front" || Slots[s] == "hair_back")
                        FillRect(pixels, atlasW, x0 + 28, yTop - 44 + bob, 40, 16, color);      // head/hair band
                    if (Slots[s] == "face_marking" || Slots[s] == "accessory")
                        FillRect(pixels, atlasW, x0 + 40, yTop - 38 + bob, 16, 6, color);       // small detail
                    if (Slots[s] == "accessory")
                        FillRect(pixels, atlasW, x0 + 34, yTop - 50 + bob, 28, 8, color);       // crown band
                    _ = transparent; // (no per-pixel clear needed; atlas starts zeroed)
                }
            }
        }

        private static Color32 SlotColor(string slot, Color32 cBody, Color32 cHairBack, Color32 cHead,
            Color32 cMarking, Color32 cHairFront, Color32 cAccessory)
        {
            switch (slot)
            {
                case "body": return cBody;
                case "hair_back": return cHairBack;
                case "head": return cHead;
                case "face_marking": return cMarking;
                case "hair_front": return cHairFront;
                case "accessory": return cAccessory;
                default: return cBody;
            }
        }

        private static void FillRect(Color32[] pixels, int width, int x, int y, int w, int h, Color32 color)
        {
            for (int py = y; py < y + h; py++)
            {
                if (py < 0 || py >= pixels.Length / width) continue;
                for (int px = x; px < x + w; px++)
                {
                    if (px < 0 || px >= width) continue;
                    pixels[py * width + px] = color;
                }
            }
        }
#else
        public static BakedResult GetOrCreate()
        {
            // Player builds never bake placeholders — spikes are editor-only (C2).
            return null;
        }
#endif
    }
}
