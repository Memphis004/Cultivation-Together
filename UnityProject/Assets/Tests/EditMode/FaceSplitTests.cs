using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Xianxia.Sect;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// Face split (Avatar Roadmap #1) — EditMode tests for the new portrait face
    /// sub-layer slots (eyes/brows/nose/mouth/eyeshadow):
    /// 1. JSON data: every face slot has parts AND an isDefault part (so empty state
    ///    falls back to something renderable — the "slot ว่าง fallback ไป default
    ///    ของ slot" contract used by starters).
    /// 2. Draw-order window: face sub-layers must paint between head (30) and hair
    ///    front (40) — the invariant that makes swaps never overlap hair.
    /// 3. Part→asset mapping: every portrait spritePath must point into Avatar/ and
    ///    the file must exist on disk (the old JSON pointed at a folder that never
    ///    existed — this test pins that bug shut).
    /// 4. Resolver: equipped face parts appear in the portrait layer list in the
    ///    head→face→hair order.
    /// </summary>
    public class FaceSplitTests
    {
        private AvatarPartPool _pool;
        private AppearanceResolver _resolver;

        private static readonly string[] FaceSlots =
        {
            AvatarSlots.Eyes, AvatarSlots.Brows, AvatarSlots.Mouth,
            AvatarSlots.Nose, AvatarSlots.Eyeshadow
        };

        private static readonly string[] AllSlots =
        {
            AvatarSlots.Base, AvatarSlots.Body, AvatarSlots.Head,
            AvatarSlots.Eyes, AvatarSlots.Brows, AvatarSlots.Mouth,
            AvatarSlots.Nose, AvatarSlots.Hair, AvatarSlots.FaceMarking,
            AvatarSlots.Eyeshadow, AvatarSlots.Accessory
        };

        [SetUp]
        public void SetUp()
        {
            _pool = new AvatarPartPool();
            _resolver = new AppearanceResolver(_pool);
        }

        /// <summary>Every face slot has defs AND a default; defaults are empty layers.</summary>
        [Test]
        public void FaceSlots_HavePartsAndDefaults()
        {
            foreach (var slot in FaceSlots)
            {
                var parts = _pool.GetPartsForSlot(slot);
                Assert.Greater(parts.Count, 0, "slot '" + slot + "' must have at least one part");

                var def = _pool.GetDefaultForSlot(slot);
                Assert.IsNotNull(def, "slot '" + slot + "' must have an isDefault part");
                Assert.IsEmpty(def.spritePath,
                    "default of '" + slot + "' must be an empty layer (isDefault=true)");
            }
        }

        /// <summary>
        /// Draw-order window invariant: face core slots (brows/eyes/nose/mouth) paint
        /// 31..35; eyeshadow paints 36..39 (over the brows, under hair 40) —
        /// swapping any face part never crosses the head(30)/hair(40) boundaries.
        /// </summary>
        [Test]
        public void FaceParts_DrawOrderWithinWindow31To35()
        {
            foreach (var slot in FaceSlots)
            {
                foreach (var part in _pool.GetPartsForSlot(slot))
                {
                    if (string.IsNullOrEmpty(part.spritePath)) continue; // empty layer: no window
                    if (slot == AvatarSlots.Eyeshadow)
                    {
                        Assert.GreaterOrEqual(part.drawOrder, 36,
                            slot + "/" + part.id + " must paint above the core face window (35)");
                        Assert.LessOrEqual(part.drawOrder, 39,
                            slot + "/" + part.id + " must paint before hair front (40)");
                    }
                    else
                    {
                        Assert.GreaterOrEqual(part.drawOrder, 31,
                            slot + "/" + part.id + " must paint after head (30)");
                        Assert.LessOrEqual(part.drawOrder, 35,
                            slot + "/" + part.id + " must paint before eyeshadow band (36+)");
                    }
                }
            }
        }

        /// <summary>
        /// Face parts are portrait-only by design (R4): chibiSheetPath must stay empty
        /// and no face part may carry a Spine skin (the chibi keeps baked-in features).
        /// </summary>
        [Test]
        public void FaceParts_ArePortraitOnly_NoChibiNoSpine()
        {
            foreach (var slot in FaceSlots)
            {
                foreach (var part in _pool.GetPartsForSlot(slot))
                {
                    Assert.IsTrue(string.IsNullOrEmpty(part.chibiSheetPath),
                        slot + "/" + part.id + " must not have chibi art (portrait-only by design)");
                    Assert.IsTrue(string.IsNullOrEmpty(part.spineSkin),
                        slot + "/" + part.id + " must not have a Spine skin (portrait-only by design)");
                }
            }
        }

        /// <summary>
        /// Every non-empty portrait path resolves to an existing PNG under
        /// Resources/Avatar/ — pins the "JSON pointed at a folder that never existed"
        /// bug shut. Baked by PortraitPlaceholderBaker (same discipline as ChibiSheetBaker).
        /// </summary>
        [Test]
        public void PortraitPaths_PointToExistingFilesOnDisk()
        {
            int checkedCount = 0;
            foreach (var slot in AllSlots)
            {
                foreach (var part in _pool.GetPartsForSlot(slot))
                {
                    var path = part.spritePath;
                    if (string.IsNullOrEmpty(path)) continue;
                    checkedCount++;

                    Assert.IsTrue(path.StartsWith("Avatar/"),
                        part.id + " portrait path must live under Avatar/ (got '" + path + "')");

                    var full = System.IO.Path.Combine(
                        System.IO.Path.Combine(System.IO.Path.GetFullPath(Application.dataPath), "Resources"),
                        path + ".png");
                    Assert.IsTrue(System.IO.File.Exists(full),
                        part.id + " portrait art missing on disk: " + full);
                }
            }
            Assert.GreaterOrEqual(checkedCount, 30, "expected the full portrait part set");
        }

        /// <summary>
        /// Equipped face parts appear in the portrait layer list, sorted strictly
        /// inside the head(30)..hair(40) window: brows 31 → eyes 32 → nose 33 → mouth 34.
        /// </summary>
        [Test]
        public void Resolve_EquippedFaceParts_OrderBetweenHeadAndHair()
        {
            var a = new AvatarAppearance();
            a.SetSlot(AvatarSlots.Body, "body_robe_azure");
            a.SetSlot(AvatarSlots.Head, "head_male_01");
            a.SetSlot(AvatarSlots.Eyes, "eyes_round");   // 32
            a.SetSlot(AvatarSlots.Brows, "brows_thick"); // 31
            a.SetSlot(AvatarSlots.Nose, "nose_tall");    // 33
            a.SetSlot(AvatarSlots.Mouth, "mouth_smile"); // 34
            a.SetSlot(AvatarSlots.Hair, "hair_topknot"); // 40

            var layers = _resolver.Resolve(a, VisualBackend.Portrait);

            int IndexOf(string slot) => layers.FindIndex(l => l.Slot == slot && !l.IsBack);
            int head = IndexOf(AvatarSlots.Head);
            int brows = IndexOf(AvatarSlots.Brows);
            int eyes = IndexOf(AvatarSlots.Eyes);
            int nose = IndexOf(AvatarSlots.Nose);
            int mouth = IndexOf(AvatarSlots.Mouth);
            int hair = IndexOf(AvatarSlots.Hair);

            Assert.GreaterOrEqual(head, 0, "head layer must resolve");
            Assert.Greater(brows, head, "brows must paint after head");
            Assert.Greater(eyes, brows, "eyes must paint after brows");
            Assert.Greater(nose, eyes, "nose must paint after eyes");
            Assert.Greater(mouth, nose, "mouth must paint after nose");
            Assert.Greater(hair, mouth, "hair front must paint after mouth");
        }

        /// <summary>Resolver drops the face slot when its part cannot resolve.</summary>
        [Test]
        public void Resolve_FaceParts_AppearInLayerList()
        {
            var a = new AvatarAppearance();
            a.SetSlot(AvatarSlots.Head, "head_male_01");
            a.SetSlot(AvatarSlots.Eyes, "eyes_round");

            var layers = _resolver.Resolve(a, VisualBackend.Portrait);

            var eyes = layers.Find(l => l.Slot == AvatarSlots.Eyes && !l.IsBack);
            Assert.IsNotNull(eyes, "eyes layer must resolve when equipped");
            Assert.AreEqual("Avatar/eyes_round", eyes.Asset);
        }
    }
}
