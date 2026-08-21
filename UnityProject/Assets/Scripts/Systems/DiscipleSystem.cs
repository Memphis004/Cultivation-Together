using MessagePipe;
using VContainer.Unity;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    // Stub - not implemented yet. Registered so GameLifetimeScope compiles
    // and the DI graph resolves end to end. Fill in gather/assign logic here.
    public class DiscipleSystem : IStartable, ITickable
    {
        private readonly IPublisher<DiscipleRecruitedMessage> _recruitedPublisher;
        private readonly IDistributedPublisher<string, DiscipleRankChangedMessage> _rankChangedPublisher;

        public DiscipleSystem(
            IPublisher<DiscipleRecruitedMessage> recruitedPublisher,
            IDistributedPublisher<string, DiscipleRankChangedMessage> rankChangedPublisher)
        {
            _recruitedPublisher = recruitedPublisher;
            _rankChangedPublisher = rankChangedPublisher;
        }

        public void Start() { }
        public void Tick() { }
    }
}
