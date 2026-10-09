using VContainer.Unity;

namespace Xianxia.Sect
{
    // Disciple crafting loop. Whoever's CurrentTask matches a known recipe
    // (see SectStateProvider.CraftingRecipes) accumulates progress each
    // tick and produces an item on completion, consuming raw resources.
    public class ResourceCraftingSystem : ITickable
    {
        private readonly ISectStateProvider _stateProvider;
        private readonly TimeSystem _timeSystem;

        public ResourceCraftingSystem(ISectStateProvider stateProvider, TimeSystem timeSystem)
        {
            _stateProvider = stateProvider;
            _timeSystem = timeSystem;
        }

        public void Tick()
        {
            // P10 — same shared simulation delta as every other gameplay tick: pause
            // freezes crafting and game speed is applied exactly once, inside
            // TimeSystem (never here).
            _stateProvider.TickCrafting(_timeSystem.SimulationDelta);
        }
    }
}
