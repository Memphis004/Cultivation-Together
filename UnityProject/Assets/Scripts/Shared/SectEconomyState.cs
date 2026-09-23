// Plain C# mirror of economy.proto.
// Use this to wire up UI and gameplay logic right away.
using System;
using System.Collections.Generic;
using MessagePack;

namespace Xianxia.Sect
{
    public enum OwnerScope { Unspecified, Personal, SectStockpile }
    public enum DiscipleRank { Unspecified, OuterDisciple, InnerDisciple, Elder, SectMaster }
    public enum DiscipleSex { Unspecified, Male, Female }

    /// <summary>
    /// Entitlement ของศิษย์: "ได้สิทธิ์" แสดง chibi ในฉากด้วย backend ไหน — ไม่ใช่สิ่งที่ render จริงเสมอไป
    /// (runtime อาจ degrade Spine → SpriteSheet เมื่อเกิน SpineBudget โดยไม่แก้ค่านี้ — ดู §7 ของแผน)
    /// 0 = default → save/roster เก่าที่ไม่มี [Key(8)] deserialize ได้ SpriteSheet (L5)
    /// </summary>
    public enum ChibiBackend
    {
        SpriteSheet = 0,
        Spine = 1
    }

    [MessagePackObject]
    public class CurrencyWallet
    {
        [Key(0)] public long SpiritStones { get; set; }
        [Key(1)] public long Contribution { get; set; }
    }

    [MessagePackObject]
    public class InventoryItem
    {
        [Key(0)] public string ItemDefId { get; set; }
        [Key(1)] public int Quantity { get; set; }
        [Key(2)] public int Grade { get; set; }          // 1-5
        [Key(3)] public OwnerScope OwnerScope { get; set; }
    }

    [MessagePackObject]
    public class DiscipleState
    {
        [Key(0)] public string DiscipleId { get; set; }
        [Key(1)] public string DisplayName { get; set; }
        [Key(2)] public DiscipleRank Rank { get; set; }
        [Key(3)] public CurrencyWallet Wallet { get; set; } = new CurrencyWallet();
        [Key(4)] public List<InventoryItem> PersonalInventory { get; set; } = new List<InventoryItem>();
        [Key(5)] public string CurrentTask { get; set; }
        [Key(6)] public AvatarAppearance Avatar { get; set; } = new AvatarAppearance();
        [Key(7)] public DiscipleSex Sex { get; set; } = DiscipleSex.Unspecified;
        /// <summary>Entitlement (แกนแยกจาก AvatarAppearance) — default SpriteSheet เมื่อ deserialize state เก่า (L1/L5)</summary>
        [Key(8)] public ChibiBackend ChibiBackend { get; set; } = ChibiBackend.SpriteSheet;
    }

    [MessagePackObject]
    public class SectStockpile
    {
        [Key(0)] public Dictionary<string, int> RawResources { get; set; } = new Dictionary<string, int>();
        [Key(1)] public List<InventoryItem> CraftedGoods { get; set; } = new List<InventoryItem>();
    }

    [MessagePackObject]
    public class SectEconomyState
    {
        [Key(0)] public List<DiscipleState> Disciples { get; set; } = new List<DiscipleState>();
        [Key(1)] public SectStockpile Stockpile { get; set; } = new SectStockpile();

        public byte[] ToByteArray() => MessagePackSerializer.Serialize(this);
        public static SectEconomyState FromByteArray(byte[] bytes) =>
            MessagePackSerializer.Deserialize<SectEconomyState>(bytes);
    }

    // --- Avatar System (New Dictionary-Based Schema) ---

    /// <summary>
    /// Struct สำหรับ Factory Method FromSlots (เลี่ยง Value Tuple เพื่อความเข้ากันได้กับ Unity ทุกเวอร์ชัน)
    /// </summary>
    public struct SlotPart
    {
        public string Slot;
        public string PartId;
        public SlotPart(string slot, string partId)
        {
            Slot = slot;
            PartId = partId;
        }
    }

    /// <summary>
    /// หน้าตาของตัวละคร — dictionary-based รองรับ slot จำนวน任意
    /// Parts: slot → partId (ว่าง = ใช้ default ของ slot นั้น)
    /// Colors: slot → colorId (มีเฉพาะ slot ที่ tintable)
    /// </summary>
    [MessagePackObject]
    public sealed class AvatarAppearance
    {
        [Key(0)] public Dictionary<string, string> Parts { get; set; } = new Dictionary<string, string>();
        [Key(1)] public Dictionary<string, string> Colors { get; set; } = new Dictionary<string, string>();
        [Key(2)] public string PoseId { get; set; } = string.Empty;

        public string GetSlot(string slot)
        {
            string v;
            return Parts.TryGetValue(slot, out v) ? v : string.Empty;
        }

        public bool SetSlot(string slot, string partId)
        {
            if (string.IsNullOrEmpty(partId)) { Parts.Remove(slot); return true; }
            Parts[slot] = partId;
            return true;
        }

        public string GetColor(string slot)
        {
            string v;
            return Colors.TryGetValue(slot, out v) ? v : string.Empty;
        }

        public void SetColor(string slot, string colorId)
        {
            if (string.IsNullOrEmpty(colorId)) { Colors.Remove(slot); return; }
            Colors[slot] = colorId;
        }

        public AvatarAppearance Clone()
        {
            var c = new AvatarAppearance();
            c.Parts = new Dictionary<string, string>(Parts);
            c.Colors = new Dictionary<string, string>(Colors);
            c.PoseId = PoseId;
            return c;
        }

        [IgnoreMember]
        /// <summary>Effective pose: PoseId if set, else fallback to "pose_idle_01".</summary>
        public string EffectivePose
        {
            get { return string.IsNullOrEmpty(PoseId) ? "pose_idle_01" : PoseId; }
        }

        /// <summary>Factory สำหรับสร้าง AvatarAppearance จาก list — ใช้ใน MockData</summary>
        public static AvatarAppearance FromSlots(params SlotPart[] slots)
        {
            var a = new AvatarAppearance();
            for (int i = 0; i < slots.Length; i++)
            {
                if (!string.IsNullOrEmpty(slots[i].PartId))
                    a.Parts[slots[i].Slot] = slots[i].PartId;
            }
            return a;
        }
    }

    /// <summary>ชื่อ slot แบบ const string + Categories สำหรับ UI</summary>
    public static class AvatarSlots
    {
        public const string Base = "base";
        public const string Body = "body";
        public const string Head = "head";
        public const string Eyes = "eyes";
        public const string Brows = "brows";
        public const string Mouth = "mouth";
        public const string Nose = "nose";
        public const string Hair = "hair";
        public const string FaceMarking = "face_marking";
        public const string Eyeshadow = "eyeshadow";
        public const string Accessory = "accessory";

        public static readonly string[] Equippable =
        {
            Body, Head, Eyes, Brows, Mouth, Nose,
            Hair, FaceMarking, Eyeshadow, Accessory
        };

        public static readonly IReadOnlyDictionary<string, string[]> Categories =
            new Dictionary<string, string[]>
            {
                { "ใบหน้า", new[] { Head, Eyes, Brows, Mouth, Nose } }, // Head รวมอยู่ในใบหน้าด้วย
                { "ลักษณะ", new[] { Hair, FaceMarking, Eyeshadow } },
                { "ร่างกาย", new[] { Body, Accessory } },
            };

        public static readonly IReadOnlyDictionary<string, string> Labels =
            new Dictionary<string, string>
            {
                { Body,       "เสื้อผ้า" },
                { Head,       "ใบหน้า" },
                { Eyes,       "ตา" },
                { Brows,      "คิ้ว" },
                { Mouth,      "ปาก" },
                { Nose,       "จมูก" },
                { Hair,       "ทรงผม" },
                { FaceMarking,"ลายหน้า" },
                { Eyeshadow,  "อายแชโดว์" },
                { Accessory,  "เครื่องประดับ" },
            };
    }
}