// Sample SectEconomyState for wiring up UI before real gameplay
// systems (resource gathering, crafting, quests) are implemented.
using System.Collections.Generic;
namespace Xianxia.Sect
{
    public static class MockSectData
    {
        public static SectEconomyState Create()
        {
            var state = new SectEconomyState();
            
            // --- Stockpile ---
            state.Stockpile.RawResources["herb"] = 120;
            state.Stockpile.RawResources["wood"] = 340;
            state.Stockpile.RawResources["ore"] = 88;
            state.Stockpile.RawResources["provisions"] = 260;
            
            state.Stockpile.CraftedGoods.Add(new InventoryItem
            {
                ItemDefId = "elixir_qi_gathering",
                Quantity = 4,
                Grade = 3,
                OwnerScope = OwnerScope.SectStockpile
            });

            // --- Placed buildings ---
            // None. The start state carries NO buildings: the player sees an empty
            // sect and must build everything themselves. A start-state herb_plot
            // used to be placed here for the task building-requirement gate, but it
            // was invisible on screen, so state disagreed with what the player saw.

            // --- Disciples (Updated for New Avatar System with SlotPart struct) ---
            
            // d000: Sect Master
            state.Disciples.Add(new DiscipleState
            {
                DiscipleId = "d000",
                DisplayName = "Liu YiFeng",
                Sex = DiscipleSex.Male,
                ChibiBackend = ChibiBackend.Spine, // SectMaster = Spine tier (แผน §4.1)
                Rank = DiscipleRank.SectMaster,
                Wallet = new CurrencyWallet { SpiritStones = 1200, Contribution = 3400 },
                CurrentTask = "meditation",
                PersonalInventory = new List<InventoryItem>(),
                Avatar = AvatarAppearance.FromSlots(
                    new SlotPart(AvatarSlots.Body, "body_robe_azure"),
                    new SlotPart(AvatarSlots.Head, "head_male_01"),
                    new SlotPart(AvatarSlots.Hair, "hair_topknot_long"),
                    new SlotPart(AvatarSlots.Accessory, "acc_jade_crown")
                ),
                // P10A TEST DATA (prototype): d000 = no skill XP.
                Attributes = TestAttributes(0f, 0f, 0f)
            });

            // d001: Lin Feng (Outer Disciple)
            state.Disciples.Add(new DiscipleState
            {
                DiscipleId = "d001",
                DisplayName = "Lin Feng",
                Sex = DiscipleSex.Female,
                ChibiBackend = ChibiBackend.SpriteSheet, // Outer = SpriteSheet (แผน §4.1)
                Rank = DiscipleRank.OuterDisciple,
                Wallet = new CurrencyWallet { SpiritStones = 12, Contribution = 340 },
                CurrentTask = "meditation", // was gathering_herb — herb_plot is no longer in start state, gate would block it anyway
                PersonalInventory = new List<InventoryItem>(),
                Avatar = AvatarAppearance.FromSlots(
                    new SlotPart(AvatarSlots.Body, "body_robe_grey"),
                    new SlotPart(AvatarSlots.Head, "head_male_01"),
                    new SlotPart(AvatarSlots.Hair, "hair_short")
                ),
                // P10A TEST DATA (prototype): gathering-leaning founder.
                Attributes = TestAttributes(200f, 0f, 0f)
            });

            // d002: Su Yan (Inner Disciple - With face marking + twin tail)
            state.Disciples.Add(new DiscipleState
            {
                DiscipleId = "d002",
                DisplayName = "Su Yan",
                Sex = DiscipleSex.Female,
                ChibiBackend = ChibiBackend.SpriteSheet, // Inner = SpriteSheet (แผน §4.1)
                Rank = DiscipleRank.InnerDisciple,
                Wallet = new CurrencyWallet { SpiritStones = 45, Contribution = 1120 },
                CurrentTask = "meditation", // was refining_elixir — pill_hall not in start state, gate would block it anyway
                PersonalInventory = new List<InventoryItem>(),
                Avatar = AvatarAppearance.FromSlots(
                    new SlotPart(AvatarSlots.Body, "body_robe_white"),
                    new SlotPart(AvatarSlots.Head, "head_female_01"),
                    new SlotPart(AvatarSlots.Hair, "hair_twin_tail"),
                    new SlotPart(AvatarSlots.FaceMarking, "face_marking_red_dot"),
                    new SlotPart(AvatarSlots.Accessory, "acc_hairpin_silver")
                ),
                // P10A TEST DATA (prototype): alchemy-leaning founder.
                Attributes = TestAttributes(0f, 200f, 0f)
            });

            // d003: Elder Zhao (Elder - Bald + Beard)
            state.Disciples.Add(new DiscipleState
            {
                DiscipleId = "d003",
                DisplayName = "Elder Zhao",
                Sex = DiscipleSex.Male,
                ChibiBackend = ChibiBackend.Spine, // Elder = Spine tier (แผน §4.1)
                Rank = DiscipleRank.Elder,
                Wallet = new CurrencyWallet { SpiritStones = 210, Contribution = 4300 },
                CurrentTask = "meditation", // was forging_artifact — forge not in start state, gate would block it anyway
                PersonalInventory = new List<InventoryItem>
                {
                    new InventoryItem
                    {
                        ItemDefId = "sword_azure_flame",
                        Quantity = 1,
                        Grade = 5,
                        OwnerScope = OwnerScope.Personal
                    }
                },
                Avatar = AvatarAppearance.FromSlots(
                    new SlotPart(AvatarSlots.Body, "body_robe_black"),
                    new SlotPart(AvatarSlots.Head, "head_male_elder"),
                    new SlotPart(AvatarSlots.Hair, "hair_bald_beard"),
                    new SlotPart(AvatarSlots.Accessory, "acc_gourd")
                ),
                // P10A TEST DATA (prototype): forging-leaning founder.
                Attributes = TestAttributes(0f, 0f, 400f)
            });

            return state;
        }

        /// <summary>
        /// P10A TEST DATA (prototype) — build a normalized attribute block for a
        /// founder. Stamina is 100 for everyone; the four founders differ only in
        /// which skill category they have XP in, so later phases can tell them apart.
        /// These are placeholders, NOT balance values.
        /// </summary>
        private static DiscipleAttributes TestAttributes(float gatheringXp, float alchemyXp, float forgingXp)
        {
            var attributes = new DiscipleAttributes { Stamina = DiscipleAttributesConfig.StaminaDefault };
            attributes.SkillXp[DiscipleAttributesConfig.CategoryGathering] = gatheringXp;
            attributes.SkillXp[DiscipleAttributesConfig.CategoryAlchemy] = alchemyXp;
            attributes.SkillXp[DiscipleAttributesConfig.CategoryForging] = forgingXp;
            return DiscipleAttributes.Normalize(attributes);
        }
    }
}