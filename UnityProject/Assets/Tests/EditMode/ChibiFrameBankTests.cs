using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// Phase 2 T2 — ChibiFrameBank EditMode tests. Verifies the sheet contract:
    /// one row per anim state (chibi_anim.json order), 6 frames per row,
    /// 96×96 cells, feet-center pivot — sliced from a synthetic texture so the
    /// test does not depend on generated art assets.
    /// </summary>
    public class ChibiFrameBankTests
    {
        private Texture2D _tex;

        [TearDown]
        public void TearDown()
        {
            if (_tex != null) Object.DestroyImmediate(_tex);
            _tex = null;
        }

        /// <summary>เคส 1 — sheet slicing: 2 states × 6 frames, feet-center pivot, correct strip order.</summary>
        [Test]
        public void LoadSheet_SlicesStateRowsAndFeetPivot()
        {
            var bank = new ChibiFrameBank(); // reads real chibi_anim.json (Idle, Walk)
            Assert.GreaterOrEqual(bank.StateOrder.Count, 1, "chibi_anim.json must define at least Idle");

            int rows = bank.StateOrder.Count;
            _tex = new Texture2D(6 * ChibiFrameBank.CellSize, rows * ChibiFrameBank.CellSize,
                                 TextureFormat.RGBA32, false);
            // paint each row a distinct color so we can verify strip identity
            for (int row = 0; row < rows; row++)
            {
                var c = row == 0 ? Color.red : Color.green;
                var pixels = new Color[ChibiFrameBank.CellSize * ChibiFrameBank.CellSize];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
                int yBottom = (rows - 1 - row) * ChibiFrameBank.CellSize; // top row = state 0
                _tex.SetPixels(0, yBottom, ChibiFrameBank.CellSize, ChibiFrameBank.CellSize, pixels);
            }
            _tex.Apply();

            bank.LoadSheetForTest(_tex, "test_part");

            var idle = bank.GetFrames("test_part", "Idle");
            Assert.IsNotNull(idle, "Idle strip must slice");
            Assert.AreEqual(6, idle.Length, "6 frames per state row");

            // feet-center pivot on every frame (Sprite.pivot is in PIXELS:
            // center-x = 96/2 = 48, feet-y = 0)
            for (int f = 0; f < idle.Length; f++)
            {
                Assert.AreEqual(48f, idle[f].pivot.x, 0.001f, "pivot.x must be cell center (48px)");
                Assert.AreEqual(0f, idle[f].pivot.y, 0.001f, "pivot.y must be feet (bottom edge)");
                Assert.AreEqual(96f, idle[f].rect.width, "cell width must be 96px");
                Assert.AreEqual(96f, idle[f].rect.height, "cell height must be 96px");
            }

            if (rows > 1)
            {
                var walk = bank.GetFrames("test_part", "Walk");
                Assert.IsNotNull(walk, "Walk strip must slice");
                // state 0 (Idle) is authored at the TOP of the sheet → higher texture Y;
                // Walk (row 1) sits one cell lower → strictly smaller rect.y
                Assert.Less(walk[0].textureRect.y, idle[0].textureRect.y,
                    "Walk row must be below the Idle row in the sheet");
            }
        }

        /// <summary>เคส 2 — unknown state falls back to Idle frames (L9 spirit, no throw).</summary>
        [Test]
        public void GetFrames_UnknownState_FallsBackToIdle()
        {
            var bank = new ChibiFrameBank();
            _tex = new Texture2D(6 * ChibiFrameBank.CellSize, ChibiFrameBank.CellSize, TextureFormat.RGBA32, false);
            bank.LoadSheetForTest(_tex, "test_part_fb");

            var frames = bank.GetFrames("test_part_fb", "TotallyUnknownState");
            Assert.IsNotNull(frames, "unknown state must fall back, not return null");
            Assert.AreEqual(bank.GetFrames("test_part_fb", "Idle"), frames, "fallback must return the Idle strip");
        }

        /// <summary>เคส 3 — part ที่ไม่เคย load → null (caller ตัดสินใจเอง: ซ่อน layer).</summary>
        [Test]
        public void GetFrames_NeverLoadedPart_ReturnsNull()
        {
            var bank = new ChibiFrameBank();
            Assert.IsNull(bank.GetFrames("ghost_part", "Idle"));
        }

        /// <summary>เคส 4 — SpriteSheet resolver order: hair back (10) &lt; body (20) &lt; head (30) &lt; hair front (40) &lt; accessory (50).</summary>
        [Test]
        public void Resolve_SpriteSheet_OrdersLayersLikePortrait()
        {
            var pool = new AvatarPartPool();
            var resolver = new AppearanceResolver(pool);

            var a = new AvatarAppearance();
            a.SetSlot(AvatarSlots.Body, "body_robe_grey");
            a.SetSlot(AvatarSlots.Head, "head_male_01");
            a.SetSlot(AvatarSlots.Hair, "hair_topknot_long");
            a.SetSlot(AvatarSlots.Accessory, "acc_jade_crown");

            var layers = resolver.Resolve(a, VisualBackend.SpriteSheet);

            Assert.GreaterOrEqual(layers.Count, 5, "body+head+hair back+hair front+accessory expected");
            for (int i = 1; i < layers.Count; i++)
                Assert.LessOrEqual(layers[i - 1].Order, layers[i].Order, "layers must be sorted by Order ascending");

            var hairBack = layers.Find(l => l.Slot == AvatarSlots.Hair && l.IsBack);
            var hairFront = layers.Find(l => l.Slot == AvatarSlots.Hair && !l.IsBack);
            Assert.IsNotNull(hairBack, "chibi long hair must have a back layer");
            Assert.AreEqual(10, hairBack.Order);
            Assert.AreEqual(40, hairFront.Order);
            Assert.AreEqual("Chibi/hair_hair_topknot_long_back", hairBack.Asset);
        }
    }
}
