namespace Xianxia.Sect
{
    // Implements the seam TimeSystem.cs defines (ISectStateProvider).
    // Returns mock data for now so the MCP bridge's get_sect_state tool has
    // something to return end-to-end before DiscipleSystem/ResourceCraftingSystem
    // actually track live state. Swap the body for real aggregation once
    // those subsystems are implemented - the interface doesn't need to change.
    public class SectStateProvider : ISectStateProvider
    {
        public SectEconomyState BuildSectEconomyState()
        {
            return MockSectData.Create();
        }
    }
}
