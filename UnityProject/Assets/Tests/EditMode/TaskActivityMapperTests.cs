using NUnit.Framework;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// Phase 4 — TaskActivityMapper unit tests (EditMode, loads the REAL
    /// chibi_activity_task_map.json via Resources, mirroring how
    /// ChibiFrameBankTests exercises its table).
    /// </summary>
    public class TaskActivityMapperTests
    {
        private TaskActivityMapper _mapper;

        [SetUp]
        public void SetUp()
        {
            _mapper = new TaskActivityMapper();
        }

        [Test]
        public void Resolve_GatheringPrefix_MapsToWalk()
        {
            Assert.AreEqual("Walk", _mapper.ResolveActivity("gathering_herb"));
            Assert.AreEqual("Walk", _mapper.ResolveActivity("gathering_wood"));
        }

        [Test]
        public void Resolve_RefiningPrefix_MapsToWorking()
        {
            Assert.AreEqual("Working", _mapper.ResolveActivity("refining_elixir"));
        }

        [Test]
        public void Resolve_ForgingPrefix_MapsToWorking()
        {
            Assert.AreEqual("Working", _mapper.ResolveActivity("forging_artifact"));
        }

        [Test]
        public void Resolve_Meditation_MapsToResting()
        {
            Assert.AreEqual("Resting", _mapper.ResolveActivity("meditation"));
        }

        [Test]
        public void Resolve_UnknownTask_FallsBackToDefaultActivity()
        {
            Assert.AreEqual("Idle", _mapper.ResolveActivity("totally_unknown_task"));
        }

        [Test]
        public void Resolve_NullOrEmpty_FallsBackToDefaultActivity()
        {
            Assert.AreEqual("Idle", _mapper.ResolveActivity(null));
            Assert.AreEqual("Idle", _mapper.ResolveActivity(""));
        }
    }
}
