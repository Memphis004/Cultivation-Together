using System;
using System.Collections.Generic;
using MessagePack;
using MessagePipe;
using NUnit.Framework;
using Xianxia.Sect;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// P5A — data + persistence contracts (data-only phase: ยังไม่มี join/expulsion/
    /// change-permission behavior ตามสเปก §7):
    ///   - ViewerRecord self-consistency (active ⇔ bound; non-active ⇒ unbound)
    ///   - SectViewerRegistry internal consistency (unique ViewerId, single binding,
    ///     pending ห้ามซ้ำกับ records)
    ///   - Cross-check invariants #1/#2 กับ DiscipleState roster (agree ทั้งสองทาง)
    ///   - MessagePack roundtrip + legacy deserialization (state เก่าไม่มี [Key(3)])
    ///   - IClock abstraction (UTC เท่านั้น — ไม่ persist stopwatch)
    /// MockSectData ไม่ถูกแตะ — start state ยังไม่มีสมาชิกผู้ชม
    /// </summary>
    public class ViewerMembershipTests
    {
        private static DiscipleState MakeDisciple(string id, DiscipleOwnerType ownerType, string ownerId)
        {
            return new DiscipleState
            {
                DiscipleId = id,
                DisplayName = id,
                OwnerType = ownerType,
                OwnerId = ownerId,
            };
        }

        private static ViewerRecord ActiveRecord(string viewerId, string discipleId)
        {
            return new ViewerRecord
            {
                ViewerId = viewerId,
                DisplayName = viewerId,
                BoundDiscipleId = discipleId,
                Status = ViewerMembershipStatus.Active,
                LastActiveAtUtc = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc),
            };
        }

        // ---- ViewerRecord self-consistency (invariants #1/#3 ฝั่งเดียว) ----

        [Test]
        public void ViewerRecord_ActiveRequiresBoundDisciple_NonActiveRequiresEmpty()
        {
            var active = ActiveRecord("viewer_test_01", "d001");
            Assert.IsTrue(active.IsSelfConsistent());

            var left = ActiveRecord("viewer_test_01", "d001");
            left.Status = ViewerMembershipStatus.Left;
            Assert.IsFalse(left.IsSelfConsistent(),
                "Left record must not keep a bound disciple (invariant #3)");

            var unboundActive = new ViewerRecord { ViewerId = "viewer_x", Status = ViewerMembershipStatus.Active };
            Assert.IsFalse(unboundActive.IsSelfConsistent(),
                "Active record without a bound disciple breaks invariant #1");
        }

        [Test]
        public void ViewerRecord_IdentityConvention_MatchesRepositoryRule()
        {
            Assert.IsTrue(new ViewerRecord { ViewerId = "viewer_test_01" }.HasValidIdentityConvention());
            Assert.IsTrue(new ViewerRecord { ViewerId = "player_main" }.HasValidIdentityConvention());
            Assert.IsFalse(new ViewerRecord { ViewerId = "" }.HasValidIdentityConvention());
            Assert.IsFalse(new ViewerRecord { ViewerId = "1bad" }.HasValidIdentityConvention());
            Assert.IsFalse(new ViewerRecord { ViewerId = "has space" }.HasValidIdentityConvention());
        }

        // ---- SectViewerRegistry internal consistency ----

        [Test]
        public void Registry_DuplicateViewerId_Rejected()
        {
            var registry = new SectViewerRegistry
            {
                Records = { ActiveRecord("viewer_test_01", "d001") },
            };
            Assert.IsTrue(registry.IsInternallyConsistent());

            registry.Records.Add(new ViewerRecord { ViewerId = "viewer_test_01", Status = ViewerMembershipStatus.Left });
            Assert.IsFalse(registry.IsInternallyConsistent(), "ViewerId must be unique across records");
        }

        [Test]
        public void Registry_TwoActiveRecordsOnOneDisciple_Rejected()
        {
            var registry = new SectViewerRegistry
            {
                Records =
                {
                    ActiveRecord("viewer_test_01", "d001"),
                    ActiveRecord("viewer_test_02", "d001"),
                },
            };
            Assert.IsFalse(registry.IsInternallyConsistent(),
                "one disciple can be bound by at most one active record");
        }

        [Test]
        public void Registry_PendingViewerAlsoInRecords_Rejected()
        {
            var registry = new SectViewerRegistry
            {
                Records = { ActiveRecord("viewer_test_01", "d001") },
                PendingApplications = { new PendingViewerApplication { ViewerId = "viewer_test_01" } },
            };
            Assert.IsFalse(registry.IsInternallyConsistent(),
                "a pending application must never duplicate an existing record (unapproved ≠ member)");
        }

        [Test]
        public void Registry_PendingListIsSeparateFromActiveMembership()
        {
            var registry = new SectViewerRegistry
            {
                Records = { ActiveRecord("viewer_test_01", "d001") },
                PendingApplications = { new PendingViewerApplication { ViewerId = "viewer_waiting" } },
            };
            Assert.IsTrue(registry.IsInternallyConsistent());

            // the applicant exists ONLY in the pending list — no record, not a member
            Assert.IsNull(registry.Find("viewer_waiting"), "unapproved applicant must not appear as a member record");
            Assert.AreEqual(ViewerMembershipStatus.Active, registry.Find("viewer_test_01").Status);
            Assert.AreEqual(1, registry.PendingApplications.Count);
        }

        // ---- cross-check with the real disciple roster (invariants #1/#2) ----

        [Test]
        public void Registry_AgreeingRoster_Passes()
        {
            var disciples = new List<DiscipleState>
            {
                MakeDisciple("d000", DiscipleOwnerType.Npc, ""),
                MakeDisciple("d001", DiscipleOwnerType.Viewer, "viewer_test_01"),
            };
            var registry = new SectViewerRegistry { Records = { ActiveRecord("viewer_test_01", "d001") } };

            Assert.IsTrue(registry.VerifyAgainst(disciples),
                "active record ⇔ Viewer-owned disciple with matching OwnerId");
        }

        [Test]
        public void Registry_RecordPointsToNpcDisciple_Fails()
        {
            var disciples = new List<DiscipleState> { MakeDisciple("d001", DiscipleOwnerType.Npc, "") };
            var registry = new SectViewerRegistry { Records = { ActiveRecord("viewer_test_01", "d001") } };

            Assert.IsFalse(registry.VerifyAgainst(disciples),
                "active record must agree with the disciple's OwnerType/OwnerId (invariant #1)");
        }

        [Test]
        public void Registry_ViewerOwnedDiscipleWithoutRecord_Fails()
        {
            var disciples = new List<DiscipleState> { MakeDisciple("d001", DiscipleOwnerType.Viewer, "viewer_test_01") };
            var registry = new SectViewerRegistry();

            Assert.IsFalse(registry.VerifyAgainst(disciples),
                "every Viewer-owned disciple needs a matching active record (invariant #2)");
        }

        [Test]
        public void Registry_OwnerIdMismatch_Fails()
        {
            var disciples = new List<DiscipleState>
            {
                MakeDisciple("d001", DiscipleOwnerType.Viewer, "viewer_other"),
            };
            var registry = new SectViewerRegistry { Records = { ActiveRecord("viewer_test_01", "d001") } };

            Assert.IsFalse(registry.VerifyAgainst(disciples),
                "record.ViewerId must equal disciple.OwnerId (invariants #1/#2 agree)");
        }

        [Test]
        public void Registry_ActiveRecordPointsToUnknownDisciple_Fails()
        {
            var disciples = new List<DiscipleState> { MakeDisciple("d000", DiscipleOwnerType.Npc, "") };
            var registry = new SectViewerRegistry { Records = { ActiveRecord("viewer_test_01", "d999") } };

            Assert.IsFalse(registry.VerifyAgainst(disciples),
                "active record must point at an existing disciple");
        }

        // ---- serialization (data contracts only — no save/load mechanism exists) ----

        [Test]
        public void SectEconomyState_Roundtrip_PreservesViewerRegistry()
        {
            var state = new SectEconomyState();
            state.Disciples.Add(MakeDisciple("d001", DiscipleOwnerType.Viewer, "viewer_test_01"));
            state.ViewerRegistry.Records.Add(ActiveRecord("viewer_test_01", "d001"));
            state.ViewerRegistry.PendingApplications.Add(new PendingViewerApplication
            {
                ViewerId = "viewer_waiting",
                DisplayName = "Waiting Viewer",
                AppliedAtUtc = new DateTime(2026, 10, 8, 10, 30, 0, DateTimeKind.Utc),
            });

            var restored = SectEconomyState.FromByteArray(state.ToByteArray());

            Assert.AreEqual(1, restored.ViewerRegistry.Records.Count);
            var r = restored.ViewerRegistry.Records[0];
            Assert.AreEqual("viewer_test_01", r.ViewerId);
            Assert.AreEqual("d001", r.BoundDiscipleId);
            Assert.AreEqual(ViewerMembershipStatus.Active, r.Status);
            Assert.AreEqual(new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc), r.LastActiveAtUtc,
                "UTC timestamp must survive round-trip exactly (no process-local values)");

            Assert.AreEqual(1, restored.ViewerRegistry.PendingApplications.Count);
            Assert.AreEqual("viewer_waiting", restored.ViewerRegistry.PendingApplications[0].ViewerId);
            Assert.AreEqual(new DateTime(2026, 10, 8, 10, 30, 0, DateTimeKind.Utc),
                restored.ViewerRegistry.PendingApplications[0].AppliedAtUtc);

            Assert.IsTrue(restored.ViewerRegistry.VerifyAgainst(restored.Disciples),
                "restored state must still satisfy the cross-check invariants");
        }

        [Test]
        public void SectEconomyState_LegacyWithoutKey3_DeserializesWithEmptyRegistry()
        {
            // state เก่า: serialize ผ่าน class ที่มีแค่ Key(0..2) → array 3 ช่อง = ไฟล์เก่าพอดี
            var legacyShape = new LegacyRootState
            {
                Disciples = new List<DiscipleState>(),
                Stockpile = new SectStockpile(),
                PlacedBuildings = new List<PlacedBuildingState>(),
            };

            var bytes = MessagePackSerializer.Serialize(legacyShape);
            var restored = SectEconomyState.FromByteArray(bytes);

            Assert.IsNotNull(restored.ViewerRegistry, "missing [Key(3)] must not null/throw");
            Assert.AreEqual(0, restored.ViewerRegistry.Records.Count, "old save = no viewer members (fail-closed)");
            Assert.AreEqual(0, restored.ViewerRegistry.PendingApplications.Count);
            Assert.IsTrue(restored.ViewerRegistry.IsInternallyConsistent());
        }

        [MessagePackObject]
        internal sealed class LegacyRootState
        {
            [Key(0)] public List<DiscipleState> Disciples { get; set; }
            [Key(1)] public SectStockpile Stockpile { get; set; }
            [Key(2)] public List<PlacedBuildingState> PlacedBuildings { get; set; }
        }

        // ---- IClock abstraction (P5A §6) ----

        [Test]
        public void UtcClock_ReturnsUtcNow()
        {
            IClock clock = new UtcClock();
            var before = DateTime.UtcNow.AddSeconds(-1);
            var now = clock.UtcNow;
            var after = DateTime.UtcNow.AddSeconds(1);

            Assert.GreaterOrEqual(now, before);
            Assert.LessOrEqual(now, after);
            Assert.AreEqual(DateTimeKind.Utc, now.Kind, "clock must report UTC (persist UTC only)");
        }

        [Test]
        public void ClockIsInjectable_FutureInactivityCheckShape()
        {
            //ตัวอย่างรูปร่างการใช้: inactivity check อนาคต inject clock แล้วเทียบกับ LastActiveAtUtc (UTC)
            var fixedClock = new FixedClock(new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc));
            var record = ActiveRecord("viewer_test_01", "d001");
            record.LastActiveAtUtc = fixedClock.UtcNow.AddDays(-14);

            var inactiveDays = (fixedClock.UtcNow - record.LastActiveAtUtc).TotalDays;
            Assert.AreEqual(14, inactiveDays, "inactivity math stays in UTC on injected clock");
        }

        private sealed class FixedClock : IClock
        {
            private readonly DateTime _now;
            public FixedClock(DateTime now) { _now = now; }
            public DateTime UtcNow => _now;
        }

        // ---- mock start state stays untouched ----

        [Test]
        public void MockStartState_ViewerRegistryIsEmpty()
        {
            var provider = new SectStateProvider(
                new BufferPublisher<DiscipleRecruitedMessage>(),
                new BufferPublisher<SectResourceChangedMessage>(),
                new BufferPublisher<AvatarEquipmentChangedMessage>(),
                new BufferPublisher<DiscipleChibiBackendChangedMessage>(),
                new AvatarPartPool(),
                VisualRuntimeConfig.Instance,
                new DefaultEntitlementProvider(),
                new BuildingDefPool(),
                new BufferPublisher<BuildingPlacedMessage>(),
                new BufferPublisher<DiscipleTaskChangedMessage>(),
                new BufferPublisher<DiscipleOwnerChangedMessage>());

            var state = provider.BuildSectEconomyState();
            Assert.AreEqual(0, state.ViewerRegistry.Records.Count, "MockSectData stays unmodified — no members");
            Assert.AreEqual(0, state.ViewerRegistry.PendingApplications.Count, "no pending applications");
            Assert.IsTrue(state.ViewerRegistry.IsInternallyConsistent());
            Assert.IsTrue(state.ViewerRegistry.VerifyAgainst(state.Disciples),
                "empty registry + all-Npc roster must satisfy both invariants trivially");
        }

        /// <summary>Minimal IPublisher over a list — same pattern as OwnershipHarnessTests.</summary>
        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public readonly List<T> Messages = new List<T>();
            public void Publish(T message) => Messages.Add(message);
        }
    }
}
