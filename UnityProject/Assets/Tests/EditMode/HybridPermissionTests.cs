using System;
using System.Collections.Generic;
using MessagePipe;
using NUnit.Framework;
using Xianxia.Sect;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// P5B — Hybrid permissions + authoritative queries, driven by a fake clock
    /// (never wait 10 real minutes):
    ///   - NPC assignment (SECT_MASTER on an unowned disciple) still works
    ///   - a viewer controls only its own valid ACTIVE membership
    ///   - another viewer is rejected
    ///   - SECT_MASTER cannot override an ACTIVE viewer owner
    ///   - EXACTLY 10 minutes of inactivity is still protected; strictly more is not
    ///   - missing/conflicting membership data → consistency error, never permission
    ///   - invalid/future owner timestamps are handled conservatively (protected)
    ///   - the trusted AI GM uses the SAME SECT_MASTER rule — no bypass id
    ///   - activity refreshes on bind/reclaim and valid owner commands only;
    ///     invalid/unauthorized commands never refresh another viewer
    ///   - a no-op request publishes nothing, changes nothing, resets no progress
    ///     and does not consume the cooldown
    ///   - the 12s task-change cooldown gates actual changes (configurable)
    ///   - rejected commands leave task, progress and resources untouched
    /// No Twitch connection anywhere here.
    /// </summary>
    public class HybridPermissionTests
    {
        private SectStateProvider _provider;
        private MutableClock _clock;
        private BufferPublisher<DiscipleTaskChangedMessage> _taskChanged;
        private BufferPublisher<DiscipleOwnerChangedMessage> _ownerChanged;

        private static readonly DateTime T0 = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

        [SetUp]
        public void SetUp()
        {
            _clock = new MutableClock(T0);
            _taskChanged = new BufferPublisher<DiscipleTaskChangedMessage>();
            _ownerChanged = new BufferPublisher<DiscipleOwnerChangedMessage>();

            _provider = new SectStateProvider(
                new BufferPublisher<DiscipleRecruitedMessage>(),
                new BufferPublisher<SectResourceChangedMessage>(),
                new BufferPublisher<AvatarEquipmentChangedMessage>(),
                new BufferPublisher<DiscipleChibiBackendChangedMessage>(),
                new AvatarPartPool(),
                VisualRuntimeConfig.Instance,
                new DefaultEntitlementProvider(),
                new BuildingDefPool(),
                new BufferPublisher<BuildingPlacedMessage>(),
                _taskChanged,
                _ownerChanged,
                _clock);
        }

        private SectEconomyState State => _provider.BuildSectEconomyState();

        private DiscipleState Find(string id)
        {
            foreach (var d in State.Disciples)
                if (d.DiscipleId == id) return d;
            return null;
        }

        private ViewerRecord Record(string viewerId) => State.ViewerRegistry.Find(viewerId);

        private bool Bind(string discipleId, string viewerId)
            => _provider.TrySetDiscipleOwner(discipleId, DiscipleOwnerType.Viewer, viewerId, out var reason);

        // ---- NPC assignment (unchanged baseline) ----

        [Test]
        public void SectMaster_OnUnownedNpc_Allowed()
        {
            var permission = _provider.CheckTaskPermission("SECT_MASTER", "d000", "gathering_herb");
            Assert.IsTrue(permission.Allowed, permission.Reason);

            string reason;
            Assert.IsTrue(_provider.TryAssignTask("SECT_MASTER", "d000", "gathering_herb", out reason), reason);
            Assert.AreEqual("gathering_herb", Find("d000").CurrentTask);
        }

        [Test]
        public void NonMaster_OnUnownedNpc_Denied()
        {
            var permission = _provider.CheckTaskPermission("viewer_test_01", "d000", "gathering_herb");
            Assert.AreEqual(TaskPermissionOutcome.Denied, permission.Outcome);

            string reason;
            Assert.IsFalse(_provider.TryAssignTask("viewer_test_01", "d000", "gathering_herb", out reason));
            Assert.AreEqual("meditation", Find("d000").CurrentTask);
        }

        // ---- empty / invalid requester ----

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("1starts_with_digit")]
        [TestCase("has space")]
        public void InvalidRequester_Denied(string requesterId)
        {
            var permission = _provider.CheckTaskPermission(requesterId, "d000", "gathering_herb");
            Assert.AreEqual(TaskPermissionOutcome.Denied, permission.Outcome);

            string reason;
            Assert.IsFalse(_provider.TryAssignTask(requesterId, "d000", "gathering_herb", out reason));
            Assert.AreEqual("meditation", Find("d000").CurrentTask);
            Assert.AreEqual(0, _taskChanged.Messages.Count);
        }

        // ---- own viewer vs another viewer ----

        [Test]
        public void OwnViewer_ActiveMembership_Allowed()
        {
            Assert.IsTrue(Bind("d001", "viewer_test_01"));

            var permission = _provider.CheckTaskPermission("viewer_test_01", "d001", "gathering_wood");
            Assert.IsTrue(permission.Allowed, permission.Reason);

            string reason;
            Assert.IsTrue(_provider.TryAssignTask("viewer_test_01", "d001", "gathering_wood", out reason), reason);
            Assert.AreEqual("gathering_wood", Find("d001").CurrentTask);
        }

        [Test]
        public void OtherViewer_Denied_AndTaskUnchanged()
        {
            Assert.IsTrue(Bind("d001", "viewer_test_01"));

            var permission = _provider.CheckTaskPermission("viewer_test_02", "d001", "gathering_wood");
            Assert.AreEqual(TaskPermissionOutcome.Denied, permission.Outcome);

            string reason;
            Assert.IsFalse(_provider.TryAssignTask("viewer_test_02", "d001", "gathering_wood", out reason));
            Assert.AreEqual("gathering_herb", Find("d001").CurrentTask);
            Assert.AreEqual(0, _taskChanged.Messages.Count);
        }

        [Test]
        public void Viewer_OnAnothersDisciple_WithOwnMembership_Denied()
        {
            Assert.IsTrue(Bind("d001", "viewer_test_01"));
            Assert.IsTrue(Bind("d002", "viewer_test_02"));

            var permission = _provider.CheckTaskPermission("viewer_test_02", "d001", "gathering_wood");
            Assert.AreEqual(TaskPermissionOutcome.Denied, permission.Outcome);
        }

        // ---- missing / conflicting membership → consistency error, not permission ----

        [Test]
        public void MissingMembership_ConsistencyError_EvenForSectMaster()
        {
            // fixture writes a Viewer-owned row directly — no membership record exists
            var d001 = Find("d001");
            d001.OwnerType = DiscipleOwnerType.Viewer;
            d001.OwnerId = "viewer_ghost";

            var asOwner = _provider.CheckTaskPermission("viewer_ghost", "d001", "gathering_wood");
            Assert.AreEqual(TaskPermissionOutcome.ConsistencyError, asOwner.Outcome,
                "membership data missing must never grant permission");

            var asMaster = _provider.CheckTaskPermission("SECT_MASTER", "d001", "gathering_wood");
            Assert.AreEqual(TaskPermissionOutcome.ConsistencyError, asMaster.Outcome,
                "SectMaster must not bypass missing membership data");

            string reason;
            Assert.IsFalse(_provider.TryAssignTask("SECT_MASTER", "d001", "gathering_wood", out reason));
            Assert.AreEqual("gathering_herb", Find("d001").CurrentTask);
            Assert.AreEqual(0, _taskChanged.Messages.Count);
        }

        [Test]
        public void ConflictingMembership_RecordDisagreesWithDisciple_ConsistencyError()
        {
            Assert.IsTrue(Bind("d001", "viewer_test_01"));
            // corrupt the disciple side so it contradicts the registry
            Find("d001").OwnerId = "viewer_someone_else";

            var permission = _provider.CheckTaskPermission("viewer_test_01", "d001", "gathering_wood");
            Assert.AreEqual(TaskPermissionOutcome.ConsistencyError, permission.Outcome);
        }

        // ---- SectMaster override window ----

        [Test]
        public void ActiveViewer_OverrideRejected()
        {
            Assert.IsTrue(Bind("d001", "viewer_test_01")); // activity = T0

            _clock.UtcNow = T0.AddSeconds(5);
            var permission = _provider.CheckTaskPermission("SECT_MASTER", "d001", "gathering_wood");
            Assert.AreEqual(TaskPermissionOutcome.Denied, permission.Outcome);
            Assert.IsTrue(permission.OwnerProtected);
            Assert.Greater(permission.OwnerProtectionRemainingSeconds, 590f, "~595s remain");

            string reason;
            Assert.IsFalse(_provider.TryAssignTask("SECT_MASTER", "d001", "gathering_wood", out reason));
            Assert.AreEqual("gathering_herb", Find("d001").CurrentTask);
        }

        [Test]
        public void ExactlyTenMinutes_StillProtected()
        {
            Assert.IsTrue(Bind("d001", "viewer_test_01"));

            _clock.UtcNow = T0.AddSeconds(600); // exactly the window — NOT strictly greater
            var permission = _provider.CheckTaskPermission("SECT_MASTER", "d001", "gathering_wood");
            Assert.AreEqual(TaskPermissionOutcome.Denied, permission.Outcome,
                "override requires strictly more than 10 minutes");
            Assert.IsTrue(permission.OwnerProtected);

            string reason;
            Assert.IsFalse(_provider.TryAssignTask("SECT_MASTER", "d001", "gathering_wood", out reason));
            Assert.AreEqual("gathering_herb", Find("d001").CurrentTask);
        }

        [Test]
        public void JustOverTenMinutes_OverrideAllowed()
        {
            Assert.IsTrue(Bind("d001", "viewer_test_01"));

            _clock.UtcNow = T0.AddSeconds(600.5);
            var permission = _provider.CheckTaskPermission("SECT_MASTER", "d001", "gathering_wood");
            Assert.IsTrue(permission.Allowed, permission.Reason);

            string reason;
            Assert.IsTrue(_provider.TryAssignTask("SECT_MASTER", "d001", "gathering_wood", out reason), reason);
            Assert.AreEqual("gathering_wood", Find("d001").CurrentTask);
        }

        [Test]
        public void FutureActivityTimestamp_TreatedAsProtected()
        {
            Assert.IsTrue(Bind("d001", "viewer_test_01"));
            Record("viewer_test_01").LastActiveAtUtc = _clock.UtcNow.AddHours(1); // corrupt/future

            var permission = _provider.CheckTaskPermission("SECT_MASTER", "d001", "gathering_wood");
            Assert.AreEqual(TaskPermissionOutcome.Denied, permission.Outcome);
            Assert.IsTrue(permission.OwnerProtected, "conservative: future timestamp = recently active");
        }

        [Test]
        public void MinValueActivityTimestamp_TreatedAsProtected()
        {
            Assert.IsTrue(Bind("d001", "viewer_test_01"));
            Record("viewer_test_01").LastActiveAtUtc = DateTime.MinValue;

            var permission = _provider.CheckTaskPermission("SECT_MASTER", "d001", "gathering_wood");
            Assert.AreEqual(TaskPermissionOutcome.Denied, permission.Outcome);
            Assert.IsTrue(permission.OwnerProtected, "conservative: unset timestamp must not lapse the window");
        }

        [Test]
        public void MasterOverride_DoesNotRefreshTheInactiveOwnersActivity()
        {
            Assert.IsTrue(Bind("d001", "viewer_test_01")); // activity = T0

            _clock.UtcNow = T0.AddMinutes(11);
            string reason;
            Assert.IsTrue(_provider.TryAssignTask("SECT_MASTER", "d001", "gathering_wood", out reason), reason);

            Assert.AreEqual(T0, Record("viewer_test_01").LastActiveAtUtc,
                "an override is not the owner's own command — it must not extend their window");
        }

        // ---- AI GM uses the SAME rule (no bypass) ----

        [Test]
        public void AiGm_FollowsTheSameSectMasterRule_NoBypass()
        {
            Assert.IsTrue(Bind("d001", "viewer_test_01")); // active → protected

            // a separate "ai_gm" identity is NOT privileged: it is not SECT_MASTER
            // and has no membership, so it is denied outright.
            var aiGm = _provider.CheckTaskPermission("ai_gm", "d001", "gathering_wood");
            Assert.AreNotEqual(TaskPermissionOutcome.Allowed, aiGm.Outcome);

            string reason;
            Assert.IsFalse(_provider.TryAssignTask("ai_gm", "d001", "gathering_wood", out reason));

            // and SECT_MASTER (the id both the player UI and the trusted GM send)
            // is blocked by the same protection rule...
            Assert.IsFalse(_provider.TryAssignTask("SECT_MASTER", "d001", "gathering_wood", out reason));
            Assert.AreEqual("gathering_herb", Find("d001").CurrentTask);

            // ...then allowed once the window lapses — identical rule, no special case.
            _clock.UtcNow = T0.AddMinutes(11);
            Assert.IsTrue(_provider.TryAssignTask("SECT_MASTER", "d001", "gathering_wood", out reason), reason);
        }

        // ---- activity refresh rules ----

        [Test]
        public void BindAndValidOwnerCommand_RefreshOwnActivity()
        {
            Assert.IsTrue(Bind("d001", "viewer_test_01"));
            Assert.AreEqual(T0, Record("viewer_test_01").LastActiveAtUtc, "bind refreshes activity");

            _clock.UtcNow = T0.AddSeconds(60);
            string reason;
            Assert.IsTrue(_provider.TryAssignTask("viewer_test_01", "d001", "gathering_wood", out reason), reason);
            Assert.AreEqual(T0.AddSeconds(60), Record("viewer_test_01").LastActiveAtUtc,
                "a valid owner command refreshes its own activity");
        }

        [Test]
        public void ReclaimReBind_RefreshesActivity()
        {
            Assert.IsTrue(Bind("d001", "viewer_test_01"));

            _clock.UtcNow = T0.AddSeconds(120);
            Assert.IsTrue(Bind("d001", "viewer_test_01"), "re-binding the same owner is a valid reclaim");
            Assert.AreEqual(T0.AddSeconds(120), Record("viewer_test_01").LastActiveAtUtc);
        }

        [Test]
        public void RejectedCommand_DoesNotRefreshAnotherViewersActivity()
        {
            Assert.IsTrue(Bind("d001", "viewer_test_01"));
            Assert.IsTrue(Bind("d002", "viewer_test_02"));

            _clock.UtcNow = T0.AddSeconds(60);
            string reason;

            // viewer_test_02 is unauthorized on d001
            Assert.IsFalse(_provider.TryAssignTask("viewer_test_02", "d001", "gathering_wood", out reason));
            Assert.AreEqual(T0, Record("viewer_test_01").LastActiveAtUtc,
                "a rejected command must not refresh the owner's activity");

            // and an invalid task from the owner themselves is not a valid command either
            Assert.IsFalse(_provider.TryAssignTask("viewer_test_01", "d001", "not_a_task", out reason));
            Assert.AreEqual(T0, Record("viewer_test_01").LastActiveAtUtc);
        }

        // ---- no-op behaviour ----

        [Test]
        public void NoOp_PublishesNothing_KeepsTask_AndDoesNotConsumeCooldown()
        {
            string reason;
            Assert.AreEqual("meditation", Find("d000").CurrentTask);

            // valid request for the current task → no-op
            Assert.IsTrue(_provider.TryAssignTask("SECT_MASTER", "d000", "meditation", out reason), reason);
            Assert.AreEqual(0, _taskChanged.Messages.Count, "no task-change event on a no-op");
            Assert.AreEqual("meditation", Find("d000").CurrentTask);

            // cooldown was NOT consumed → an immediate real change still goes through
            Assert.IsTrue(_provider.TryAssignTask("SECT_MASTER", "d000", "gathering_wood", out reason), reason);
            Assert.AreEqual(1, _taskChanged.Messages.Count);
            Assert.AreEqual("gathering_wood", Find("d000").CurrentTask);
        }

        [Test]
        public void NoOp_DoesNotResetGatheringProgress()
        {
            // d001 gathers herb at 0.2/s; the fractional accumulator lives per task.
            var herbAtStart = State.Stockpile.RawResources["herb"];
            Tick(3.5f); // 0.7 accumulated → no whole unit yet
            Assert.AreEqual(herbAtStart, State.Stockpile.RawResources["herb"], "0.7 units produced nothing");

            var herbBefore = State.Stockpile.RawResources["herb"];
            string reason;
            Assert.IsTrue(_provider.TryAssignTask("SECT_MASTER", "d001", "gathering_herb", out reason), reason);
            Tick(2.0f); // 0.7 + 0.4 = 1.1 → exactly one unit IF the accumulator survived the no-op

            Assert.AreEqual(herbBefore + 1, State.Stockpile.RawResources["herb"],
                "the no-op must not reset accumulated gathering progress");
        }

        [Test]
        public void NoOp_DuringCooldown_IsStillAllowed()
        {
            string reason;
            Assert.IsTrue(_provider.TryAssignTask("SECT_MASTER", "d000", "gathering_wood", out reason), reason);

            // immediately after a real change the cooldown is running…
            Assert.IsFalse(_provider.TryAssignTask("SECT_MASTER", "d000", "gathering_herb", out reason));
            StringAssert.Contains("cooldown", reason);

            // …but asking for the task already held is a no-op and is never blocked
            Assert.IsTrue(_provider.TryAssignTask("SECT_MASTER", "d000", "gathering_wood", out reason), reason);
            Assert.AreEqual(1, _taskChanged.Messages.Count, "the no-op publishes nothing");
        }

        // ---- cooldown (configurable 12s prototype value) ----

        [Test]
        public void Cooldown_BlocksThrash_AndExpiresAfterTheWindow()
        {
            Assert.AreEqual(12f, SectStateProvider.DefaultTaskChangeCooldownSeconds);
            Assert.AreEqual(12f, _provider.TaskChangeCooldownSeconds);

            string reason;
            Assert.IsTrue(_provider.TryAssignTask("SECT_MASTER", "d000", "gathering_wood", out reason), reason);

            _clock.UtcNow = T0.AddSeconds(11.9);
            Assert.IsFalse(_provider.TryAssignTask("SECT_MASTER", "d000", "gathering_herb", out reason));
            StringAssert.Contains("cooldown", reason);
            Assert.AreEqual("gathering_wood", Find("d000").CurrentTask);

            _clock.UtcNow = T0.AddSeconds(12.1);
            Assert.IsTrue(_provider.TryAssignTask("SECT_MASTER", "d000", "gathering_herb", out reason), reason);
            Assert.AreEqual("gathering_herb", Find("d000").CurrentTask);
        }

        [Test]
        public void Cooldown_IsPerDisciple_AndCanBeDisabled()
        {
            string reason;
            Assert.IsTrue(_provider.TryAssignTask("SECT_MASTER", "d000", "gathering_wood", out reason), reason);
            // a different disciple is unaffected by d000's cooldown
            Assert.IsTrue(_provider.TryAssignTask("SECT_MASTER", "d002", "gathering_wood", out reason), reason);

            // configurable knob: 0 disables the throttle entirely
            _provider.TaskChangeCooldownSeconds = 0f;
            Assert.IsTrue(_provider.TryAssignTask("SECT_MASTER", "d000", "gathering_herb", out reason), reason);
        }

        // ---- rejected commands leave everything untouched ----

        [Test]
        public void RejectedCommand_LeavesTaskProgressAndResourcesUnchanged()
        {
            Assert.IsTrue(Bind("d001", "viewer_test_01"));
            Tick(3.5f); // partial gathering progress on d001

            var herbBefore = State.Stockpile.RawResources["herb"];
            var woodBefore = State.Stockpile.RawResources["wood"];

            string reason;
            Assert.IsFalse(_provider.TryAssignTask("viewer_test_02", "d001", "gathering_wood", out reason));

            Assert.AreEqual("gathering_herb", Find("d001").CurrentTask);
            Assert.AreEqual(herbBefore, State.Stockpile.RawResources["herb"]);
            Assert.AreEqual(woodBefore, State.Stockpile.RawResources["wood"]);
            Assert.AreEqual(0, _taskChanged.Messages.Count);

            // progress survived: 0.7 + 0.4 = one more unit on the next tick
            Tick(2.0f);
            Assert.AreEqual(herbBefore + 1, State.Stockpile.RawResources["herb"]);
        }

        // ---- read-only query never mutates ----

        [Test]
        public void CheckTaskPermission_IsReadOnly()
        {
            Assert.IsTrue(Bind("d001", "viewer_test_01"));

            var taskBefore = Find("d001").CurrentTask;
            var activeBefore = Record("viewer_test_01").LastActiveAtUtc;
            var herbBefore = State.Stockpile.RawResources["herb"];

            _provider.CheckTaskPermission("SECT_MASTER", "d001", "gathering_wood");
            _provider.CheckTaskPermission("viewer_test_01", "d001", "gathering_wood");
            _provider.CheckTaskPermission(null, "d001", "gathering_wood");

            Assert.AreEqual(taskBefore, Find("d001").CurrentTask);
            Assert.AreEqual(activeBefore, Record("viewer_test_01").LastActiveAtUtc,
                "a query must not refresh activity either");
            Assert.AreEqual(herbBefore, State.Stockpile.RawResources["herb"]);
        }

        [Test]
        public void CheckTaskPermission_ReportsNoOp()
        {
            var noOp = _provider.CheckTaskPermission("SECT_MASTER", "d000", "meditation");
            Assert.IsTrue(noOp.Allowed);
            Assert.IsTrue(noOp.IsNoOp);

            var change = _provider.CheckTaskPermission("SECT_MASTER", "d000", "gathering_wood");
            Assert.IsTrue(change.Allowed);
            Assert.IsFalse(change.IsNoOp);
        }

        // ---- registry stays in lock-step with ownership ----

        [Test]
        public void OwnerBindReleaseRebind_KeepsRegistryConsistent()
        {
            Assert.IsTrue(Bind("d001", "viewer_test_01"));
            Assert.AreEqual(ViewerMembershipStatus.Active, Record("viewer_test_01").Status);
            Assert.AreEqual("d001", Record("viewer_test_01").BoundDiscipleId);
            Assert.IsTrue(State.ViewerRegistry.VerifyAgainst(State.Disciples));

            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Npc, "", out reason), reason);
            Assert.AreEqual(ViewerMembershipStatus.Left, Record("viewer_test_01").Status);
            Assert.AreEqual(string.Empty, Record("viewer_test_01").BoundDiscipleId, "invariant #3: non-active ⇒ unbound");
            Assert.IsTrue(State.ViewerRegistry.VerifyAgainst(State.Disciples));

            _clock.UtcNow = T0.AddSeconds(30);
            Assert.IsTrue(Bind("d002", "viewer_test_01"));
            Assert.AreEqual(ViewerMembershipStatus.Active, Record("viewer_test_01").Status);
            Assert.AreEqual("d002", Record("viewer_test_01").BoundDiscipleId);
            Assert.AreEqual(T0.AddSeconds(30), Record("viewer_test_01").LastActiveAtUtc);
            Assert.IsTrue(State.ViewerRegistry.VerifyAgainst(State.Disciples));
        }

        // ---- helpers ----

        private void Tick(float seconds)
        {
            _provider.TickGathering(seconds);
        }

        private sealed class MutableClock : IClock
        {
            public MutableClock(DateTime now) { UtcNow = now; }
            public DateTime UtcNow { get; set; }
        }

        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public readonly List<T> Messages = new List<T>();
            public void Publish(T message) => Messages.Add(message);
        }
    }
}
