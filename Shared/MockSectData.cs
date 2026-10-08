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

            // --- Placed buildings (start state for the task building-requirement
            // gate — gathering_herb must be available without extra setup) ---
            // b001 herb_plot at top-left (-11,-8): verified against
            // PlaceableLandMask — all 4 cells (-11,-8),(-10,-8),(-11,-7),(-10,-7)
            // are land on the production grid (50x50, origin -25,-25), and the
            // block does not overlap the survey's 3x3 candidate (-10,-2).
            state.PlacedBuildings.Add(new PlacedBuildingState
            {
                InstanceId = "b001",
                DefId = "herb_plot",
                GridX = -11,
                GridZ = -8,
                Rotation = 0
            });

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
                )
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
                CurrentTask = "gathering_herb",
                PersonalInventory = new List<InventoryItem>(),
                Avatar = AvatarAppearance.FromSlots(
                    new SlotPart(AvatarSlots.Body, "body_robe_grey"),
                    new SlotPart(AvatarSlots.Head, "head_male_01"),
                    new SlotPart(AvatarSlots.Hair, "hair_short")
                )
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
                )
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
                )
            });

            return state;
        }
    }
}