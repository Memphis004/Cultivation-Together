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

            state.Disciples.Add(new DiscipleState
            {
                DiscipleId = "d001",
                DisplayName = "Lin Feng",
                Rank = DiscipleRank.OuterDisciple,
                Wallet = new CurrencyWallet { SpiritStones = 12, Contribution = 340 },
                CurrentTask = "gathering_herb",
                PersonalInventory = new List<InventoryItem>()
            });

            state.Disciples.Add(new DiscipleState
            {
                DiscipleId = "d002",
                DisplayName = "Su Yan",
                Rank = DiscipleRank.InnerDisciple,
                Wallet = new CurrencyWallet { SpiritStones = 45, Contribution = 1120 },
                CurrentTask = "refining_elixir",
                PersonalInventory = new List<InventoryItem>()
            });

            state.Disciples.Add(new DiscipleState
            {
                DiscipleId = "d003",
                DisplayName = "Elder Zhao",
                Rank = DiscipleRank.Elder,
                Wallet = new CurrencyWallet { SpiritStones = 210, Contribution = 4300 },
                CurrentTask = "forging_artifact",
                PersonalInventory = new List<InventoryItem>
                {
                    new InventoryItem
                    {
                        ItemDefId = "sword_azure_flame",
                        Quantity = 1,
                        Grade = 5,
                        OwnerScope = OwnerScope.Personal
                    }
                }
            });

            return state;
        }
    }
}
