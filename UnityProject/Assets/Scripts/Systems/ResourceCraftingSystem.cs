using MessagePipe;
using VContainer.Unity;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    // Stub - not implemented yet. Gather/craft-by-grade logic goes here.
    public class ResourceCraftingSystem : IStartable, ITickable
    {
        private readonly IDistributedPublisher<string, SectResourceChangedMessage> _resourcePublisher;
        private readonly IDistributedPublisher<string, ContributionEarnedMessage> _contributionPublisher;

        public ResourceCraftingSystem(
            IDistributedPublisher<string, SectResourceChangedMessage> resourcePublisher,
            IDistributedPublisher<string, ContributionEarnedMessage> contributionPublisher)
        {
            _resourcePublisher = resourcePublisher;
            _contributionPublisher = contributionPublisher;
        }

        public void Start() { }
        public void Tick() { }
    }
}
