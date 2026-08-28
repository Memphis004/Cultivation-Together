using UnityEngine;
using VContainer.Unity;

namespace Xianxia.Sect
{
    // Disciple crafting loop. Whoever's CurrentTask matches a known recipe
    // (see SectStateProvider.CraftingRecipes) accumulates progress each
    // tick and produces an item on completion, consuming raw resources.
    public class ResourceCraftingSystem : ITickable
    {
        private readonly ISectStateProvider _stateProvider;

        public ResourceCraftingSystem(ISectStateProvider stateProvider)
        {
            _stateProvider = stateProvider;
        }

        public void Tick()
        {
            _stateProvider.TickCrafting(Time.deltaTime);
        }
    }
}
