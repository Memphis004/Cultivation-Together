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
    /// P9A — explicit Manual/Auto control (ownership ≠ autonomy). Driven by a fake
    /// clock so viewer protection windows never require real waiting.
    ///   - every disciple defaults to Manual (backward-compatible), including recruits
    ///   - only an Npc-owned disciple may opt into Auto; Player/Viewer owners and an
    ///     inactive viewer whose protection window has lapsed are all rejected
    ///   - the Auto toggle is idempotent and publishes only on a real change
    ///   - a successful manual assignment disables Auto; a failed one changes neither
    ///     the task nor the mode
    ///   - the dedicated auto entry point rechecks Npc ownership + Auto immediately
    ///     before commit and shares the manual path's validation (no SECT_MASTER
    ///     impersonation, no permission bypass)
    ///   - switching an Auto disciple to another owner disables Auto
    /// No Twitch connection anywhere here.
    /// </summary>
    public class AutoControlTests
    {
        private SectStateProvider _provider;
        private MutableClock _clock;
        private BufferPublisher<DiscipleTaskChangedMessage> _taskChanged;
        private BufferPublisher<DiscipleOwnerChangedMessage> _ownerChanged;
        private BufferPublisher<DiscipleControlModeChangedMessage> _controlModeChanged;

        private static readonly DateTime T0 = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

        [SetUp]
        public void SetUp()
        {
            _clock = new MutableClock(T0);
            _taskChanged = new BufferPublisher<DiscipleTaskChangedMessage>();
            _ownerChanged = new BufferPublisher<DiscipleOwnerChangedMessage>();
            _controlModeChanged = new BufferPublisher<DiscipleControlModeChangedMessage>();

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
                _clock,
                _controlModeChanged);

            // Cooldown is exercised in dedicated tests; disabling it here keeps the
            // transition tests focused (they still go through the shared core).
            _provider.TaskChangeCooldownSeconds = 0f;
        }

        private SectEconomyState State => _provider.BuildSectEconomyState();

        private DiscipleState Find(string id)
        {
            foreach (var d in State.Disciples)
                if (d.DiscipleId == id) return d;
            return null;
        }

        private void PlaceHerbPlot()
        {
            string reason;
            PlacedBuildingState placed;
            Assert.IsTrue(_provider.TryPlaceBuilding("herb_plot", 0, 0, 0, new BuildingGrid(10, 10),
                                                     out reason, out placed), reason);
        }

        private bool InAuto(string id) => Find(id).ControlMode == DiscipleControlMode.Auto;

        // ---- defaults (backward compatibility) ----

        [Test]
        public void ExistingDisciples_DefaultToManual()
        {
            foreach (var d in State.Disciples)
                Assert.AreEqual(DiscipleControlMode.Manual, d.ControlMode,
                    "old rosters/saves without the mode must stay Manual: " + d.DiscipleId);
        }

        [Test]
        public void RecruitedDisciple_DefaultsToManual()
        {
            _provider.RecruitOuterDisciple();
            var recruit = State.Disciples[State.Disciples.Count - 1];
            Assert.AreEqual(DiscipleControlMode.Manual, recruit.ControlMode);
        }

        // ---- TrySetDiscipleControlMode ----

        [Test]
        public void NpcDisciple_CanOptIntoAuto_AndBack()
        {
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleControlMode("d000", DiscipleControlMode.Auto, out reason), reason);
            Assert.IsTrue(InAuto("d000"));

            Assert.AreEqual(1, _controlModeChanged.Messages.Count);
            Assert.AreEqual(DiscipleControlMode.Manual, _controlModeChanged.Messages[0].OldMode);
            Assert.AreEqual(DiscipleControlMode.Auto, _controlModeChanged.Messages[0].NewMode);
            Assert.AreEqual("d000", _controlModeChanged.Messages[0].DiscipleId);

            Assert.IsTrue(_provider.TrySetDiscipleControlMode("d000", DiscipleControlMode.Manual, out reason), reason);
            Assert.IsFalse(InAuto("d000"));
            Assert.AreEqual(2, _controlModeChanged.Messages.Count);
        }

        [Test]
        public void IdempotentSet_PublishesNothing()
        {
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleControlMode("d000", DiscipleControlMode.Manual, out reason), reason);
            Assert.AreEqual(0, _controlModeChanged.Messages.Count, "no real change → no message");
        }

        [Test]
        public void UndefinedMode_Rejected_StateUnchanged()
        {
            string reason;
            Assert.IsFalse(_provider.TrySetDiscipleControlMode("d000", (DiscipleControlMode)999, out reason));
            StringAssert.Contains("Undefined", reason);
            Assert.IsFalse(InAuto("d000"));
            Assert.AreEqual(0, _controlModeChanged.Messages.Count);
        }

        [Test]
        public void UnknownDisciple_Rejected()
        {
            string reason;
            Assert.IsFalse(_provider.TrySetDiscipleControlMode("d999", DiscipleControlMode.Auto, out reason));
            StringAssert.Contains("No disciple", reason);
            Assert.AreEqual(0, _controlModeChanged.Messages.Count);
        }

        [Test]
        public void ViewerOwnedDisciple_AutoRejected()
        {
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer, "viewer_test_01", out reason), reason);

            Assert.IsFalse(_provider.TrySetDiscipleControlMode("d001", DiscipleControlMode.Auto, out reason));
            StringAssert.Contains("NPC-owned", reason);
            Assert.IsFalse(InAuto("d001"), "state unchanged on rejection");
            Assert.AreEqual(0, _controlModeChanged.Messages.Count);
        }

        [Test]
        public void PlayerOwnedDisciple_AutoRejected()
        {
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Player, "player_main", out reason), reason);

            Assert.IsFalse(_provider.TrySetDiscipleControlMode("d001", DiscipleControlMode.Auto, out reason));
            Assert.IsFalse(InAuto("d001"));
        }

        [Test]
        public void InactiveViewerPastWindow_StillCannotOptIntoAuto()
        {
            // The hybrid inactivity rule lets the SectMaster OVERRIDE a task; it must
            // never authorize a brain. Past the protection window the master is allowed…
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer, "viewer_test_01", out reason), reason);
            _clock.UtcNow = T0.AddMinutes(11);
            Assert.IsTrue(_provider.CheckTaskPermission(SectStateProvider.SectMasterRequesterId, "d001", "gathering_wood").Allowed,
                "precondition: inactivity past the window allows a SectMaster override");

            // …but Auto stays forbidden for a Viewer-owned disciple.
            Assert.IsFalse(_provider.TrySetDiscipleControlMode("d001", DiscipleControlMode.Auto, out reason));
            Assert.IsFalse(InAuto("d001"));
        }

        // ---- manual assignment disables Auto (rule 5) ----

        [Test]
        public void ManualAssign_Success_DisablesAuto()
        {
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleControlMode("d000", DiscipleControlMode.Auto, out reason), reason);

            Assert.IsTrue(_provider.TryAssignTask(SectStateProvider.SectMasterRequesterId, "d000", "gathering_wood", out reason), reason);
            Assert.AreEqual("gathering_wood", Find("d000").CurrentTask);
            Assert.IsFalse(InAuto("d000"), "a successful manual assignment takes the disciple out of Auto");
        }

        [Test]
        public void ManualAssign_NoOpSuccess_DisablesAuto()
        {
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleControlMode("d000", DiscipleControlMode.Auto, out reason), reason);

            // d000 starts on meditation — asking for the same task is a valid no-op and
            // still counts as the human asserting control.
            Assert.IsTrue(_provider.TryAssignTask(SectStateProvider.SectMasterRequesterId, "d000", "meditation", out reason), reason);
            Assert.IsFalse(InAuto("d000"));
            Assert.AreEqual(0, _taskChanged.Messages.Count, "no-op publishes no task change");
        }

        [Test]
        public void ManualAssign_Failure_LeavesTaskAndMode()
        {
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleControlMode("d000", DiscipleControlMode.Auto, out reason), reason);
            var taskBefore = Find("d000").CurrentTask;

            Assert.IsFalse(_provider.TryAssignTask(SectStateProvider.SectMasterRequesterId, "d000", "not_a_task", out reason));
            Assert.AreEqual(taskBefore, Find("d000").CurrentTask, "failed assignment changes no task");
            Assert.IsTrue(InAuto("d000"), "failed assignment changes no mode");
        }

        [Test]
        public void ManualAssign_BuildingGateFailure_LeavesAuto()
        {
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleControlMode("d000", DiscipleControlMode.Auto, out reason), reason);
            var taskBefore = Find("d000").CurrentTask;

            Assert.IsFalse(_provider.TryAssignTask(SectStateProvider.SectMasterRequesterId, "d000", "refining_elixir", out reason));
            StringAssert.Contains("pill_hall", reason);
            Assert.AreEqual(taskBefore, Find("d000").CurrentTask);
            Assert.IsTrue(InAuto("d000"));
        }

        [Test]
        public void ManualAssign_CooldownFailure_LeavesAuto()
        {
            _provider.TaskChangeCooldownSeconds = 12f;
            string reason;

            // one real manual change (disables Auto and starts the cooldown)…
            Assert.IsTrue(_provider.TrySetDiscipleControlMode("d000", DiscipleControlMode.Auto, out reason), reason);
            Assert.IsTrue(_provider.TryAssignTask(SectStateProvider.SectMasterRequesterId, "d000", "gathering_wood", out reason), reason);
            Assert.IsFalse(InAuto("d000"));

            // …then opt back into Auto and immediately try another manual change: the
            // cooldown rejects it, and a failed assignment must not flip the mode.
            Assert.IsTrue(_provider.TrySetDiscipleControlMode("d000", DiscipleControlMode.Auto, out reason), reason);
            Assert.IsFalse(_provider.TryAssignTask(SectStateProvider.SectMasterRequesterId, "d000", "gathering_ore", out reason));
            StringAssert.Contains("cooldown", reason);
            Assert.IsTrue(InAuto("d000"));
        }

        // ---- dedicated auto entry point (rule 6) ----

        [Test]
        public void AutoAssign_NpcInAuto_ChangesTask_AndStaysAuto()
        {
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleControlMode("d000", DiscipleControlMode.Auto, out reason), reason);

            Assert.IsTrue(_provider.TryAutoAssignTask("d000", "gathering_wood", out reason), reason);
            Assert.AreEqual("gathering_wood", Find("d000").CurrentTask);
            Assert.IsTrue(InAuto("d000"), "auto assignment does not take the disciple out of Auto");
            Assert.AreEqual(1, _taskChanged.Messages.Count);
            Assert.AreEqual("d000", _taskChanged.Messages[0].DiscipleId);
        }

        [Test]
        public void AutoAssign_ManualDisciple_Rejected()
        {
            var taskBefore = Find("d000").CurrentTask;
            string reason;

            Assert.IsFalse(_provider.TryAutoAssignTask("d000", "gathering_wood", out reason));
            StringAssert.Contains("Auto mode", reason);
            Assert.AreEqual(taskBefore, Find("d000").CurrentTask);
            Assert.AreEqual(0, _taskChanged.Messages.Count);
        }

        [Test]
        public void AutoAssign_NonNpcOwner_Rejected()
        {
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer, "viewer_test_01", out reason), reason);
            var taskBefore = Find("d001").CurrentTask;

            Assert.IsFalse(_provider.TryAutoAssignTask("d001", "gathering_wood", out reason));
            StringAssert.Contains("not NPC-owned", reason);
            Assert.AreEqual(taskBefore, Find("d001").CurrentTask);
        }

        [Test]
        public void AutoAssign_DoesNotInheritSectMasterOverride()
        {
            // Viewer-owned + inactive past the window: the SectMaster may override…
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer, "viewer_test_01", out reason), reason);
            _clock.UtcNow = T0.AddMinutes(11);
            Assert.IsTrue(_provider.TryAssignTask(SectStateProvider.SectMasterRequesterId, "d001", "gathering_wood", out reason), reason);

            // …but the brain has no requester identity and never gets that authority.
            Assert.IsFalse(_provider.TryAutoAssignTask("d001", "gathering_herb", out reason));
            StringAssert.Contains("not NPC-owned", reason);
            Assert.IsFalse(_provider.TryAutoAssignTask("d001", "gathering_herb", out _),
                "auto path must stay rejected regardless of owner inactivity");
        }

        [Test]
        public void AutoAssign_SharesKnownTaskAndBuildingValidation()
        {
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleControlMode("d000", DiscipleControlMode.Auto, out reason), reason);
            var taskBefore = Find("d000").CurrentTask;

            Assert.IsFalse(_provider.TryAutoAssignTask("d000", "bogus_task", out reason));
            StringAssert.Contains("Unknown task", reason);

            Assert.IsFalse(_provider.TryAutoAssignTask("d000", "refining_elixir", out reason));
            StringAssert.Contains("pill_hall", reason);

            Assert.AreEqual(taskBefore, Find("d000").CurrentTask);
            Assert.IsTrue(InAuto("d000"));
        }

        [Test]
        public void AutoAssign_NoOp_PublishesNothing_StaysAuto()
        {
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleControlMode("d000", DiscipleControlMode.Auto, out reason), reason);

            Assert.IsTrue(_provider.TryAutoAssignTask("d000", "meditation", out reason), reason);
            Assert.AreEqual(0, _taskChanged.Messages.Count, "no-op auto assignment publishes nothing");
            Assert.IsTrue(InAuto("d000"));
        }

        [Test]
        public void AutoAssign_Cooldown_Applies()
        {
            _provider.TaskChangeCooldownSeconds = 12f;
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleControlMode("d000", DiscipleControlMode.Auto, out reason), reason);

            Assert.IsTrue(_provider.TryAutoAssignTask("d000", "gathering_wood", out reason), reason);
            Assert.IsFalse(_provider.TryAutoAssignTask("d000", "gathering_ore", out reason));
            StringAssert.Contains("cooldown", reason);
            Assert.AreEqual("gathering_wood", Find("d000").CurrentTask);
            Assert.IsTrue(InAuto("d000"));
        }

        // ---- ownership switch disables Auto (rule 7) ----

        [Test]
        public void SwitchAutoDiscipleToViewer_DisablesAuto()
        {
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleControlMode("d001", DiscipleControlMode.Auto, out reason), reason);
            _controlModeChanged.Messages.Clear();

            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer, "viewer_test_01", out reason), reason);
            Assert.IsFalse(InAuto("d001"), "switching an Auto NPC to another owner disables Auto");
            Assert.AreEqual(1, _controlModeChanged.Messages.Count);
            Assert.AreEqual(DiscipleControlMode.Auto, _controlModeChanged.Messages[0].OldMode);
            Assert.AreEqual(DiscipleControlMode.Manual, _controlModeChanged.Messages[0].NewMode);
        }

        [Test]
        public void SwitchAutoDiscipleToPlayer_DisablesAuto()
        {
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleControlMode("d001", DiscipleControlMode.Auto, out reason), reason);

            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Player, "player_main", out reason), reason);
            Assert.IsFalse(InAuto("d001"));
        }

        [Test]
        public void SameOwnerReBind_KeepsAuto()
        {
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleControlMode("d001", DiscipleControlMode.Auto, out reason), reason);

            // no-op ownership call (same Npc owner) is not a switch — Auto survives
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Npc, "", out reason), reason);
            Assert.IsTrue(InAuto("d001"));
        }

        [Test]
        public void ManualDisciple_OwnershipSwitch_PublishesNoModeMessage()
        {
            string reason;
            _controlModeChanged.Messages.Clear();

            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer, "viewer_test_01", out reason), reason);
            Assert.AreEqual(0, _controlModeChanged.Messages.Count,
                "an already-Manual disciple produces no mode change on ownership switch");
        }

        [Test]
        public void AutoAssign_AfterOwnerSwitch_Rejected()
        {
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleControlMode("d001", DiscipleControlMode.Auto, out reason), reason);

            // owner changes while the brain might be about to commit
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer, "viewer_test_01", out reason), reason);

            Assert.IsFalse(_provider.TryAutoAssignTask("d001", "gathering_wood", out reason));
            Assert.AreEqual("meditation", Find("d001").CurrentTask, "re-eligibility check blocks the stale auto intent");
        }

        // ---- persistence import safety ----

        [Test]
        public void ImportMembership_NonNpcOwner_ClearsAuto()
        {
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleControlMode("d001", DiscipleControlMode.Auto, out reason), reason);

            var save = new SectViewerMembershipSave
            {
                Version = SectViewerMembershipSave.CurrentVersion,
                SavedAtUtc = T0,
                Records = new List<ViewerRecord>
                {
                    new ViewerRecord
                    {
                        ViewerId = "viewer_test_01",
                        DisplayName = "viewer_test_01",
                        BoundDiscipleId = "d001",
                        Status = ViewerMembershipStatus.Active,
                        LastActiveAtUtc = T0,
                    },
                },
                OwnerByDisciple = new List<SectSavedOwnership>
                {
                    new SectSavedOwnership
                    {
                        DiscipleId = "d001",
                        OwnerType = DiscipleOwnerType.Viewer,
                        OwnerId = "viewer_test_01",
                    },
                },
            };

            Assert.IsTrue(_provider.TryImportViewerMembership(save, out reason), reason);
            Assert.AreEqual(DiscipleOwnerType.Viewer, Find("d001").OwnerType);
            Assert.IsFalse(InAuto("d001"), "a restored non-Npc owner must never be left in Auto");
        }

        // ---- helpers ----

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
