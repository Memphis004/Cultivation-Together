using System;
using System.Collections.Generic;
using MessagePipe;
using NUnit.Framework;
using UnityEngine;
using Xianxia.Sect.Messages;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// Phase 5 — entitlement acceptance (§8):
    ///   1. DefaultEntitlementProvider rules ("" free, owner rank proxy, dlc deny,
    ///      unknown fail-closed)
    ///   2. Validation ชั้น 6 in TryChangeAvatarPart: OuterDisciple rejected with a
    ///      clear failReason; SectMaster/Elder allowed; free parts unaffected
    ///   3. EntitlementRandom never picks a locked part (repeated rolls)
    /// </summary>
    public class EntitlementTests
    {
        private DefaultEntitlementProvider _provider;

        [SetUp]
        public void SetUp()
        {
            _provider = new DefaultEntitlementProvider();
        }

        // ---- provider rules ----

        [Test]
        public void CanUse_EmptyEntitlement_AlwaysTrue_EvenUnknownDisciple()
        {
            Assert.IsTrue(_provider.CanUse("d999", ""));
            Assert.IsTrue(_provider.CanUse(null, null));
        }

        [Test]
        public void CanUse_Owner_ElderAndAbove_Pass()
        {
            _provider.BindRankLookup(id => id == "d000" ? DiscipleRank.SectMaster
                                   : id == "d003" ? DiscipleRank.Elder
                                   : DiscipleRank.OuterDisciple);
            Assert.IsTrue(_provider.CanUse("d000", "owner"));
            Assert.IsTrue(_provider.CanUse("d003", "owner"));
        }

        [Test]
        public void CanUse_OuterDisciple_WithOwner_IsRejected_FailClosed()
        {
            // ก่อน bind (Unspecified) → ปฏิเสธ (fail-closed ตามสเปก)
            Assert.IsFalse(_provider.CanUse("d001", "owner"));

            _provider.BindRankLookup(id => id == "d001" ? DiscipleRank.OuterDisciple
                                   : DiscipleRank.Unspecified);
            Assert.IsFalse(_provider.CanUse("d001", "owner"));
            Assert.IsFalse(_provider.CanUse("d002", "owner")); // InnerDisciple &lt; Elder
        }

        [Test]
        public void CanUse_DlcPack_NotOwned_IsRejected()
        {
            // placeholder owned-DLC list ว่าง → ปฏิเสธเสมอ (ห้าม hardcode true)
            Assert.IsFalse(_provider.CanUse("d000", "dlc:winter_pack_01"));
        }

        [Test]
        public void CanUse_UnknownToken_IsDenied()
        {
            Assert.IsFalse(_provider.CanUse("d000", "mystery_token"));
        }

        [Test]
        public void GetPriority_RankMapped_Descending()
        {
            _provider.BindRankLookup(id => id == "d000" ? DiscipleRank.SectMaster
                                   : id == "d003" ? DiscipleRank.Elder
                                   : id == "d002" ? DiscipleRank.InnerDisciple
                                   : DiscipleRank.OuterDisciple);
            Assert.Greater(_provider.GetPriority("d000"), _provider.GetPriority("d003"));
            Assert.Greater(_provider.GetPriority("d003"), _provider.GetPriority("d002"));
            Assert.Greater(_provider.GetPriority("d002"), _provider.GetPriority("d001"));
        }

        // ---- validation ชั้น 6 (ผ่าน DI เต็มสาย — เหมือนที่ production ใช้) ----

        [Test]
        public void TryChangeAvatarPart_OuterDisciple_OwnerPart_IsRejected_WithClearReason()
        {
            var (provider, stateProvider) = BuildWiredProvider();

            AvatarAppearance result;
            string reason;
            bool ok = stateProvider.TryChangeAvatarPart("d001", AvatarSlots.Accessory,
                                                        "acc_jade_crown", out reason, out result);

            Assert.IsFalse(ok, "OuterDisciple ต้องถูก reject ที่ part entitlement=owner");
            Assert.IsTrue(reason.Contains("acc_jade_crown") && reason.Contains("owner"),
                          "failReason ต้องบอก part + entitlement ชัดเจน (AI/UI อ่านเข้าใจ) — got: " + reason);
        }

        [Test]
        public void TryChangeAvatarPart_SectMasterAndElder_OwnerPart_IsAllowed()
        {
            var (provider, stateProvider) = BuildWiredProvider();

            AvatarAppearance result;
            string reason;
            Assert.IsTrue(stateProvider.TryChangeAvatarPart("d000", AvatarSlots.Accessory,
                                                            "acc_jade_crown", out reason, out result),
                          "SectMaster ใส่ acc_jade_crown ได้ — reason: " + reason);
            Assert.IsTrue(stateProvider.TryChangeAvatarPart("d003", AvatarSlots.Accessory,
                                                            "acc_jade_crown", out reason, out result),
                          "Elder ใส่ acc_jade_crown ได้ — reason: " + reason);
        }

        [Test]
        public void TryChangeAvatarPart_FreeParts_Unaffected_Regression()
        {
            var (provider, stateProvider) = BuildWiredProvider();

            AvatarAppearance result;
            string reason;
            // part ฟรีทุกชนิดยังผ่านปกติ (entitlement="" หรือไม่มี field ใน JSON)
            Assert.IsTrue(stateProvider.TryChangeAvatarPart("d001", AvatarSlots.Accessory,
                                                            "acc_gourd", out reason, out result),
                          "free part ต้องไม่โดนกระทบ — reason: " + reason);
            Assert.IsTrue(stateProvider.TryChangeAvatarPart("d001", AvatarSlots.Hair,
                                                            "hair_topknot", out reason, out result),
                          "free part (ไม่มี field) ต้องไม่โดนกระทบ — reason: " + reason);
        }

        [Test]
        public void TryChangeAvatarPart_UnknownDisciple_Fails_BeforeEntitlement()
        {
            var (provider, stateProvider) = BuildWiredProvider();

            AvatarAppearance result;
            string reason;
            bool ok = stateProvider.TryChangeAvatarPart("d999", AvatarSlots.Accessory,
                                                        "acc_jade_crown", out reason, out result);
            Assert.IsFalse(ok);
            Assert.IsTrue(reason.Contains("No disciple"), "got: " + reason);
        }

        // ---- randomize filter ----

        [Test]
        public void EntitlementRandom_NeverPicksLockedPart_OverManyRolls()
        {
            var (provider, _) = BuildWiredProvider();
            var pool = new AvatarPartPool();
            var rng = new System.Random(12345); // fixed seed — deterministic run

            var defs = pool.GetPartsForSlot(AvatarSlots.Accessory);
            Assert.Greater(defs.Count, 1, "accessory slot ต้องมีทั้งฟรีและล็อกเพื่อให้ test มีความหมาย");

            int freeCount = 0, lockedCount = 0;
            for (int i = 0; i < defs.Count; i++)
            {
                if (string.IsNullOrEmpty(defs[i].entitlement)) freeCount++;
                else lockedCount++;
            }
            Assert.Greater(freeCount, 0);
            Assert.Greater(lockedCount, 0, "acc_jade_crown (owner) ต้องอยู่ในลิสต์ด้วย");

            // OuterDisciple: สุ่ม 500 รอบ — ห้ามเจอ part ล็อกแม้แต่ครั้งเดียว
            for (int roll = 0; roll < 500; roll++)
            {
                var pick = EntitlementRandom.Pick(defs, "d001", provider, rng);
                Assert.IsNotNull(pick, "มี part ฟรีอยู่ — ต้อง pick ได้เสมอ");
                Assert.IsTrue(string.IsNullOrEmpty(pick.entitlement),
                              "สุ่มต้องไม่เจอ part ล็อก — got: " + pick.id);
            }

            // SectMaster: ทุก part (รวมล็อก) เป็นตัวเลือก — pick ได้ทั้งคู่
            bool sawLocked = false;
            for (int roll = 0; roll < 500 && !sawLocked; roll++)
            {
                var pick = EntitlementRandom.Pick(defs, "d000", provider, rng);
                if (pick != null && !string.IsNullOrEmpty(pick.entitlement)) sawLocked = true;
            }
            Assert.IsTrue(sawLocked, "SectMaster ควรสุ่มเจอ part ล็อกได้ (มันผ่าน CanUse)");
        }

        [Test]
        public void EntitlementRandom_AllLocked_ReturnsNull_SkipSlot()
        {
            _provider.BindRankLookup(_ => DiscipleRank.Unspecified);
            var candidates = new List<AvatarPartDef>
            {
                new AvatarPartDef { id = "locked_a", entitlement = "owner" },
                new AvatarPartDef { id = "locked_b", entitlement = "dlc:x" },
            };
            var rng = new System.Random(7);
            Assert.IsNull(EntitlementRandom.Pick(candidates, "d001", _provider, rng),
                          "ทั้ง slot ล็อกหมด → null → caller ข้าม slot เงียบ ๆ");
        }

        // ---- wiring helper: ผ่าน VContainer เหมือน production (EditMode ทำได้) ----

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
                new AvatarPartPool(), VisualRuntimeConfig.Instance, provider);

            return (provider, stateProvider);
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
