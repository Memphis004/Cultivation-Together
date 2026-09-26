using System.Collections.Generic;
using MessagePipe;
using NUnit.Framework;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// Coverage rule (Validation ชั้น 5) after the face split (Roadmap #1):
    /// a part passes if it has art on ANY enabled backend; an empty layer
    /// (removal intent — the *_none defaults) always passes.
    /// Pins two previously-broken paths:
    ///   - portrait-only face sub-layers (R4: chibi keeps baked-in features)
    ///   - chibi-only "take-off" parts (acc_none has no portrait art)
    /// </summary>
    public class AvatarPartCoverageTests
    {
        [Test]
        public void FacePart_PortraitOnly_PassesCoverage()
        {
            var (_, stateProvider) = BuildWiredProvider();

            string reason; AvatarAppearance result;
            bool ok = stateProvider.TryChangeAvatarPart("d001", AvatarSlots.Eyes,
                                                        "eyes_round", out reason, out result);

            Assert.IsTrue(ok, "portrait-only face part must pass coverage — reason: " + reason);
            Assert.AreEqual("eyes_round", result.GetSlot(AvatarSlots.Eyes));
        }

        [Test]
        public void EmptyLayerAndTakeOffParts_PassCoverage()
        {
            var (_, stateProvider) = BuildWiredProvider();

            string reason; AvatarAppearance result;
            Assert.IsTrue(stateProvider.TryChangeAvatarPart("d001", AvatarSlots.FaceMarking,
                            "face_marking_none", out reason, out result),
                "empty layer (removal intent) must pass — reason: " + reason);
            Assert.IsTrue(stateProvider.TryChangeAvatarPart("d001", AvatarSlots.Mouth,
                            "mouth_none", out reason, out result),
                "mouth_none (empty layer) must pass — reason: " + reason);
            Assert.IsTrue(stateProvider.TryChangeAvatarPart("d001", AvatarSlots.Accessory,
                            "acc_none", out reason, out result),
                "acc_none (chibi-only take-off part) must pass — reason: " + reason);
        }

        // ---- wiring helper: ผ่าน VContainer เหมือน production (copy จาก EntitlementTests) ----

        private (DefaultEntitlementProvider provider, ISectStateProvider stateProvider)
            BuildWiredProvider()
        {
            var provider = new DefaultEntitlementProvider();
            var recruited = new MessagePipeBuffer<DiscipleRecruitedMessage>();
            var resource = new MessagePipeBuffer<SectResourceChangedMessage>();
            var avatar = new MessagePipeBuffer<AvatarEquipmentChangedMessage>();
            var backend = new MessagePipeBuffer<DiscipleChibiBackendChangedMessage>();

            var stateProvider = new SectStateProvider(
                recruited.CreatePublisher(), resource.CreatePublisher(),
                avatar.CreatePublisher(), backend.CreatePublisher(),
                new AvatarPartPool(), VisualRuntimeConfig.Instance, provider,
                new BuildingDefPool(),
                new BuildingPlacedBuffer().CreatePublisher());

            return (provider, stateProvider);
        }

        /// <summary>Empty buffer for BuildingPlacedMessage (no assertion needed here).</summary>
        private sealed class BuildingPlacedBuffer
        {
            public IPublisher<BuildingPlacedMessage> CreatePublisher() => new NullPublisher();

            private sealed class NullPublisher : IPublisher<BuildingPlacedMessage>
            {
                public void Publish(BuildingPlacedMessage message) { }
            }
        }

        /// <summary>
        /// Minimal IPublisher&lt;T&gt; over a list — MessagePipe's real broker needs a
        /// full container build; tests only need "publish doesn't throw".
        /// </summary>
        private sealed class MessagePipeBuffer<T>
        {
            public readonly List<T> Messages = new List<T>();

            public IPublisher<T> CreatePublisher()
            {
                return new BufferPublisher(this);
            }

            private sealed class BufferPublisher : IPublisher<T>
            {
                private readonly MessagePipeBuffer<T> _buffer;
                public BufferPublisher(MessagePipeBuffer<T> buffer) { _buffer = buffer; }
                public void Publish(T message) { _buffer.Messages.Add(message); }
            }
        }
    }
}
