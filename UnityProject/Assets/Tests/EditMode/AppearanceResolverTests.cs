using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// Phase 1 T3 — AppearanceResolver EditMode tests (4 เคสตามสเปค)
    /// ใช้ AvatarPartPool จริงจาก Resources/Data/avatar_parts.json เพื่อ verify
    /// ว่า JSON เก่าโหลดผ่านด้วย schema ใหม่ (C5)
    /// </summary>
    public class AppearanceResolverTests
    {
        private AvatarPartPool _pool;
        private AppearanceResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            _pool = new AvatarPartPool();
            _resolver = new AppearanceResolver(_pool);
        }

        /// <summary>เคส 1 — fallback: partId ไม่มีจริง → default ของ slot</summary>
        [Test]
        public void Resolve_UnknownPartId_FallsBackToSlotDefault()
        {
            var a = new AvatarAppearance();
            a.SetSlot(AvatarSlots.Hair, "hair_does_not_exist");

            var layers = _resolver.Resolve(a, VisualBackend.Portrait);

            var hair = layers.Find(l => l.Slot == AvatarSlots.Hair && !l.IsBack);
            Assert.IsNotNull(hair, "hair slot must resolve to something");
            Assert.AreEqual("hair_short", hair.PartId, "fallback must land on slot default");
            Assert.AreEqual("Avatar/hair_short", hair.Asset);
        }

        /// <summary>เคส 2 — back layer ไม่หาย: hair_topknot_long → 2 layers (back 10, front 40)</summary>
        [Test]
        public void Resolve_BackLayerPart_KeepsBothLayers()
        {
            var a = new AvatarAppearance();
            a.SetSlot(AvatarSlots.Hair, "hair_topknot_long");

            var layers = _resolver.Resolve(a, VisualBackend.Portrait);

            var backs = layers.FindAll(l => l.Slot == AvatarSlots.Hair && l.IsBack);
            var fronts = layers.FindAll(l => l.Slot == AvatarSlots.Hair && !l.IsBack);
            Assert.AreEqual(1, backs.Count, "hair back layer must exist");
            Assert.AreEqual(1, fronts.Count, "hair front layer must exist");
            Assert.AreEqual(10, backs[0].Order);
            Assert.AreEqual(40, fronts[0].Order);
            Assert.AreEqual("Avatar/hair_topknot_long_back", backs[0].Asset);
        }

        /// <summary>เคส 3 — tint ไม่ apply กับ tintable=false: Tint ต้องเป็น white เสมอใน Phase 1</summary>
        [Test]
        public void Resolve_NonTintablePart_TintStaysWhite()
        {
            var a = new AvatarAppearance();
            a.SetSlot(AvatarSlots.Head, "head_male_01"); // tintable=false
            a.SetColor(AvatarSlots.Head, "some_color");  // มีสีใน state แต่ part ไม่ tintable

            var layers = _resolver.Resolve(a, VisualBackend.Portrait);

            var head = layers.Find(l => l.Slot == AvatarSlots.Head && !l.IsBack);
            Assert.IsNotNull(head);
            Assert.AreEqual(Color.white, head.Tint, "non-tintable part must get white tint (no color applied)");
        }

        /// <summary>
        /// เคส 4 — backend ไม่รองรับ slot → slot นั้นไม่อยู่ในผลลัพธ์เลย
        /// ใช้ Spine backend กับ JSON เดิม (ไม่มี spineSkin ทุก part) → ต้องไม่มี slot ไหนผ่าน
        /// (ยกเว้น base ซึ่ง resolver ใส่เฉพาะ Portrait)
        /// </summary>
        [Test]
        public void Resolve_UnsupportedBackend_DropsSlotEntirely()
        {
            var a = new AvatarAppearance();
            a.SetSlot(AvatarSlots.Body, "body_robe_grey");
            a.SetSlot(AvatarSlots.Head, "head_male_01");
            a.SetSlot(AvatarSlots.Hair, "hair_short");

            var layers = _resolver.Resolve(a, VisualBackend.Spine);

            Assert.AreEqual(0, layers.Count, "no part in the old JSON supports Spine — all slots must be dropped");
        }

        /// <summary>
        /// Pixel-parity (C2): ผลของ resolver ต้องเหมือนอัลกอริทึมเดิมของ AvatarRenderer.Rebuild()
        /// เป๊ะทุก layer (order + path) สำหรับ 4 founders — rendering code ไม่ถูกแตะ
        /// จึงได้ภาพเดิมทุกพิกเซลเมื่อ layer list ตรงกัน
        /// </summary>
        [Test]
        public void Resolve_Portrait_LayerListMatchesLegacyAlgorithm_ForFourFounders()
        {
            var founders = new[]
            {
                MockSectData.Create().Disciples[0].Avatar,
                MockSectData.Create().Disciples[1].Avatar,
                MockSectData.Create().Disciples[2].Avatar,
                MockSectData.Create().Disciples[3].Avatar,
            };

            foreach (var appearance in founders)
            {
                var resolved = _resolver.Resolve(appearance, VisualBackend.Portrait);

                // ---- อัลกอริทึมเดิม (copy จาก AvatarRenderer.Rebuild ก่อน refactor) ----
                var legacy = new List<Vector2Int>(); // (order, pathHash) ไม่ได้ — เก็บเป็นคู่ order+path
                var legacyPairs = new List<LegacyPair>();
                var baseDef = _pool.Resolve(AvatarSlots.Base, string.Empty);
                if (baseDef != null) legacyPairs.Add(new LegacyPair(baseDef.drawOrder, baseDef.spritePath));

                foreach (var kvp in appearance.Parts)
                {
                    var def = _pool.Resolve(kvp.Key, kvp.Value);
                    if (def == null) continue;
                    if (!string.IsNullOrEmpty(def.spritePathBack))
                    {
                        int backOrder = def.drawOrderBack > 0 ? def.drawOrderBack : (def.drawOrder - 30);
                        legacyPairs.Add(new LegacyPair(backOrder, def.spritePathBack));
                    }
                    if (!string.IsNullOrEmpty(def.spritePath))
                        legacyPairs.Add(new LegacyPair(def.drawOrder, def.spritePath));
                }
                legacyPairs.Sort((x, y) => x.Order.CompareTo(y.Order));
                // ----------------------------------------------------------------------

                Assert.AreEqual(legacyPairs.Count, resolved.Count,
                    "layer count must match legacy for every founder");

                for (int i = 0; i < legacyPairs.Count; i++)
                {
                    Assert.AreEqual(legacyPairs[i].Order, resolved[i].Order,
                        "layer order mismatch at index " + i);
                    Assert.AreEqual(legacyPairs[i].Path, resolved[i].Asset,
                        "layer path mismatch at index " + i);
                }
            }
        }

        private struct LegacyPair
        {
            public int Order;
            public string Path;
            public LegacyPair(int order, string path) { Order = order; Path = path; }
        }
    }

    /// <summary>
    /// L5 — state เก่า (serialize ก่อนมี [Key(8)]) ต้อง deserialize ได้
    /// ChibiBackend = SpriteSheet (index 0) โดยไม่ null/throw
    /// จำลองด้วย class ที่มีแค่ Key(0–7) (MessagePack-C# serialize เป็น array
    /// ตาม index → array 8 ช่อง = รูปแบบไฟล์ save เก่าพอดี)
    /// </summary>
    public class ChibiBackendDeserializationTests
    {
        [MessagePack.MessagePackObject]
        internal sealed class LegacyDiscipleState
        {
            [MessagePack.Key(0)] public string DiscipleId { get; set; }
            [MessagePack.Key(1)] public string DisplayName { get; set; }
            [MessagePack.Key(2)] public DiscipleRank Rank { get; set; }
            [MessagePack.Key(3)] public CurrencyWallet Wallet { get; set; } = new CurrencyWallet();
            [MessagePack.Key(4)] public List<InventoryItem> PersonalInventory { get; set; } = new List<InventoryItem>();
            [MessagePack.Key(5)] public string CurrentTask { get; set; }
            [MessagePack.Key(6)] public AvatarAppearance Avatar { get; set; } = new AvatarAppearance();
            [MessagePack.Key(7)] public DiscipleSex Sex { get; set; } = DiscipleSex.Unspecified;
        }

        [Test]
        public void Deserialize_LegacyStateWithoutKey8_ChibiBackendDefaultsToSpriteSheet()
        {
            var legacy = new LegacyDiscipleState
            {
                DiscipleId = "d_old",
                DisplayName = "Old Save",
                Sex = DiscipleSex.Male,
            };
            byte[] bytes = MessagePack.MessagePackSerializer.Serialize(legacy);

            var restored = MessagePack.MessagePackSerializer.Deserialize<DiscipleState>(bytes);

            Assert.AreEqual("d_old", restored.DiscipleId, "old fields must survive round-trip");
            Assert.AreEqual(ChibiBackend.SpriteSheet, restored.ChibiBackend,
                "missing [Key(8)] must default to SpriteSheet (L5), never null/throw");
        }
    }
}
