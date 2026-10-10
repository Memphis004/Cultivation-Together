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
    /// P9B — the Unity scheduler half, wired to a real SectStateProvider (the P9A
    /// authority) and a real TimeSystem so pause behaviour is exercised for real.
    /// Covers the integration half of the requirement-8 list: manual NPCs and
    /// player/viewer disciples are untouched, assignment goes through the auto entry
    /// point, unavailable work is reevaluated, an unchanged choice emits nothing, and
    /// a cooldown rejection is not retried until the next evaluation.
    /// </summary>
    public class AutoTaskSchedulerTests
    {
        private SectStateProvider _provider;
        private TimeSystem _timeSystem;
        private AutoTaskScheduler _scheduler;
        private BufferPublisher<DiscipleTaskChangedMessage> _taskChanged;
        private MutableClock _clock;

        private static readonly DateTime T0 = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

        [SetUp]
        public void SetUp()
        {
            _clock = new MutableClock(T0);
            _taskChanged = new BufferPublisher<DiscipleTaskChangedMessage>();

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
                new BufferPublisher<DiscipleOwnerChangedMessage>(),
                _clock,
                new BufferPublisher<DiscipleControlModeChangedMessage>());

            // Cooldown is exercised in its own test; disable it so the selection tests
            // are not throttled. Dwell/margin are disabled so the CHOICE is what is
            // under test (hysteresis itself is covered by AutoTaskScoringTests).
            _provider.TaskChangeCooldownSeconds = 0f;

            _timeSystem = new TimeSystem(
                new BufferPublisher<TimeSpeedChangedMessage>(),
                new BufferPublisher<WorldEventTriggeredMessage>());

            _scheduler = new AutoTaskScheduler(_provider, _timeSystem)
            {
                EvaluationIntervalSeconds = 10f,
                MinimumDwellSeconds = 0f,
                ImprovementMargin = 0f,
            };
        }

        private SectEconomyState State => _provider.BuildSectEconomyState();

        private DiscipleState Find(string id)
        {
            foreach (var d in State.Disciples)
                if (d.DiscipleId == id) return d;
            return null;
        }

        private string TaskOf(string id) => Find(id).CurrentTask;

        private void SetStock(string resource, int amount) => State.Stockpile.RawResources[resource] = amount;

        private void SetAuto(string id)
        {
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleControlMode(id, DiscipleControlMode.Auto, out reason), reason);
        }

        private static AutoTaskScore CandidateFor(AutoTaskDecision decision, string taskId)
        {
            foreach (var c in decision.Candidates)
                if (c.TaskId == taskId) return c;
            return null;
        }

        // ---- manual NPCs are untouched ----

        [Test]
        public void ManualNpc_IsUntouched()
        {
            Assert.AreEqual(DiscipleControlMode.Manual, Find("d000").ControlMode, "precondition");

            _scheduler.Advance(1000f);

            Assert.AreEqual("meditation", TaskOf("d000"));
            Assert.AreEqual(0, _taskChanged.Messages.Count);
        }

        [Test]
        public void ManualDisciple_IsSkipped()
        {
            var outcome = _scheduler.EvaluateDisciple("d002");

            Assert.IsTrue(outcome.Skipped);
            StringAssert.Contains("Auto", outcome.SkipReason);
            Assert.AreEqual(0, _taskChanged.Messages.Count);
            Assert.AreEqual("meditation", TaskOf("d002"));
        }

        // ---- player/viewer disciples are untouched ----

        [Test]
        public void ViewerOwnedDisciple_IsUntouched()
        {
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer, "viewer_test_01", out reason), reason);

            var outcome = _scheduler.EvaluateDisciple("d001");

            Assert.IsTrue(outcome.Skipped);
            StringAssert.Contains("not NPC-owned", outcome.SkipReason);
            Assert.AreEqual(0, _taskChanged.Messages.Count);
            Assert.AreEqual("meditation", TaskOf("d001"));
        }

        [Test]
        public void OwnershipChangeBeforeCommit_PreventsAutoAssignment()
        {
            SetAuto("d000");

            // ownership changes AFTER the disciple was opted into Auto, BEFORE the brain commits
            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d000", DiscipleOwnerType.Viewer, "viewer_test_09", out reason), reason);

            var outcome = _scheduler.EvaluateDisciple("d000");

            Assert.IsTrue(outcome.Skipped);
            Assert.AreEqual(0, _taskChanged.Messages.Count);
            Assert.AreEqual("meditation", TaskOf("d000"));
        }

        // ---- assignment goes through the P9A entry point ----

        [Test]
        public void AutoNpc_IsAssigned_AndStaysAuto()
        {
            MakeOreScarce();
            SetAuto("d000");

            _scheduler.Advance(0f);

            Assert.AreEqual("gathering_ore", TaskOf("d000"));
            Assert.AreEqual(1, _taskChanged.Messages.Count);
            Assert.AreEqual("d000", _taskChanged.Messages[0].DiscipleId);
            Assert.AreEqual(DiscipleControlMode.Auto, Find("d000").ControlMode, "applying a task must not drop Auto");
        }

        [Test]
        public void ResourceShortage_ChangesSelection()
        {
            SetStock("wood", 0);
            SetStock("ore", 200);
            SetStock("provisions", 200);
            SetStock("herb", 200);
            SetAuto("d000");

            _scheduler.EvaluateDisciple("d000");

            Assert.AreEqual("gathering_wood", TaskOf("d000"), "preference follows the shortage");
        }

        // ---- unavailable work ----

        [Test]
        public void UnavailableTask_IsNeverSelected()
        {
            SetAuto("d000");

            var outcome = _scheduler.EvaluateDisciple("d000");

            var refining = CandidateFor(outcome.Decision, "refining_elixir");
            Assert.IsNotNull(refining, "the candidate is still scored, just not selectable");
            Assert.IsFalse(refining.IsAvailable);
            StringAssert.Contains("pill_hall", refining.Explanation);

            Assert.AreNotEqual("refining_elixir", outcome.Decision.SelectedTask);
            Assert.AreNotEqual("forging_artifact", outcome.Decision.SelectedTask);
        }

        [Test]
        public void UnavailableCurrentWork_IsReevaluated()
        {
            MakeOreScarce();
            SetAuto("d000");
            Find("d000").CurrentTask = "refining_elixir"; // gate-blocked, no pill_hall

            var outcome = _scheduler.EvaluateDisciple("d000");

            Assert.IsTrue(outcome.Applied);
            Assert.AreEqual("gathering_ore", TaskOf("d000"));
            Assert.AreEqual(1, _taskChanged.Messages.Count);
        }

        // ---- an unchanged choice emits nothing ----

        [Test]
        public void UnchangedChoice_EmitsNoTaskChangeEvent()
        {
            MakeOreScarce();
            SetAuto("d000");
            Find("d000").CurrentTask = "gathering_ore"; // already the best available task

            var outcome = _scheduler.EvaluateDisciple("d000");

            Assert.IsFalse(outcome.Decision.ShouldChange);
            Assert.IsTrue(outcome.Applied);
            Assert.AreEqual("gathering_ore", TaskOf("d000"));
            Assert.AreEqual(0, _taskChanged.Messages.Count, "a no-op must publish nothing");
        }

        // ---- cooldown rejection is not retried until the next evaluation ----

        [Test]
        public void CooldownRejection_IsNotRetriedInALoop()
        {
            _provider.TaskChangeCooldownSeconds = 12f;
            MakeOreScarce();
            SetAuto("d000");

            Assert.IsTrue(_scheduler.EvaluateDisciple("d000").Applied);
            Assert.AreEqual("gathering_ore", TaskOf("d000"));

            // An external change the brain did not make: the brain now wants to move back,
            // but the cooldown (re-checked by the provider) rejects it.
            Find("d000").CurrentTask = "meditation";
            _taskChanged.Messages.Clear();

            var blocked = _scheduler.EvaluateDisciple("d000");
            Assert.IsFalse(blocked.Applied);
            StringAssert.Contains("cooldown", blocked.ApplyFailReason);
            Assert.AreEqual("meditation", TaskOf("d000"));
            Assert.AreEqual(0, _taskChanged.Messages.Count);

            // and it must not keep retrying on its own
            Assert.IsFalse(_scheduler.EvaluateDisciple("d000").Applied);
            Assert.AreEqual(0, _taskChanged.Messages.Count);
        }

        // ---- pause ----

        [Test]
        public void Pause_StopsEvaluation_AndDoesNotConsumeTheInterval()
        {
            MakeOreScarce();
            SetAuto("d000");

            _timeSystem.SetPaused(true);
            _scheduler.Advance(100f);

            Assert.AreEqual(0, _taskChanged.Messages.Count, "paused gameplay must not evaluate");
            Assert.AreEqual("meditation", TaskOf("d000"));

            _timeSystem.SetPaused(false);
            _scheduler.Advance(0f);

            Assert.AreEqual("gathering_ore", TaskOf("d000"), "the interval was not consumed while paused");
            Assert.AreEqual(1, _taskChanged.Messages.Count);
        }

        // ---- cadence / staggering ----

        [Test]
        public void EvaluationHappensOnTheInterval_NotEveryFrame()
        {
            MakeOreScarce();
            SetAuto("d000");
            _scheduler.Advance(0f); // slot 0 → due immediately
            Assert.AreEqual(1, _taskChanged.Messages.Count);

            // an external change the brain has not yet observed...
            Find("d000").CurrentTask = "meditation";
            _taskChanged.Messages.Clear();

            // ...is NOT corrected before the interval elapses (no per-frame evaluation)
            _scheduler.Advance(1f);
            Assert.AreEqual("meditation", TaskOf("d000"));
            Assert.AreEqual(0, _taskChanged.Messages.Count);

            _scheduler.Advance(20f);
            Assert.AreEqual("gathering_ore", TaskOf("d000"), "due again after the interval");
            Assert.AreEqual(1, _taskChanged.Messages.Count);
        }

        [Test]
        public void Stagger_SpreadsFirstEvaluations()
        {
            MakeOreScarce();
            SetAuto("d000");
            SetAuto("d001");

            _scheduler.Advance(0f);
            Assert.AreEqual(1, _taskChanged.Messages.Count, "only the first staggered slot is due");
            Assert.AreEqual("gathering_ore", TaskOf("d000"));
            Assert.AreEqual("meditation", TaskOf("d001"), "the second slot is not due yet");

            _scheduler.Advance(_scheduler.EvaluationIntervalSeconds / AutoTaskScheduler.StaggerSlots);
            Assert.AreEqual(2, _taskChanged.Messages.Count);
            Assert.AreEqual("gathering_ore", TaskOf("d001"));
        }

        [Test]
        public void UnknownDisciple_IsSkipped()
        {
            var outcome = _scheduler.EvaluateDisciple("does_not_exist");

            Assert.IsTrue(outcome.Skipped);
            StringAssert.Contains("unknown disciple", outcome.SkipReason);
        }

        // ---- P10C: stamina recovery + skills (Auto NPCs only) ----

        private void SetStamina(string id, float stamina) => Find(id).Attributes.Stamina = stamina;

        [Test]
        public void ExhaustedAutoNpc_RecoversByMeditating_InsteadOfWorking()
        {
            MakeOreScarce();
            SetAuto("d000");
            Find("d000").CurrentTask = "gathering_wood";
            SetStamina("d000", DiscipleAttributesConfig.RecoveryThresholdLow);

            var outcome = _scheduler.EvaluateDisciple("d000");

            Assert.AreEqual("meditation", TaskOf("d000"), "an exhausted Auto NPC rests instead of working");
            Assert.IsTrue(outcome.Applied);
            Assert.AreEqual(AutoRecoveryState.EnteringMeditation, outcome.Decision.Recovery);
            Assert.AreEqual(1, _taskChanged.Messages.Count);
            Assert.AreEqual(DiscipleControlMode.Auto, Find("d000").ControlMode, "recovery must not drop Auto");
        }

        [Test]
        public void RecoveringAutoNpc_StaysOnMeditationUntilRecovered()
        {
            MakeOreScarce();
            SetAuto("d000");
            Find("d000").CurrentTask = "meditation";
            SetStamina("d000", 40f);

            var staying = _scheduler.EvaluateDisciple("d000");
            Assert.IsFalse(staying.Decision.ShouldChange, "between low and high the disciple keeps resting");
            Assert.AreEqual(AutoRecoveryState.StayingToRecover, staying.Decision.Recovery);
            Assert.AreEqual("meditation", TaskOf("d000"));
            Assert.AreEqual(0, _taskChanged.Messages.Count, "a no-op decision publishes nothing");

            SetStamina("d000", DiscipleAttributesConfig.RecoveryThresholdHigh);
            Assert.IsTrue(_scheduler.EvaluateDisciple("d000").Decision.ShouldChange,
                          "at the high threshold the disciple goes back to work");
            Assert.AreEqual("gathering_ore", TaskOf("d000"));
        }

        [Test]
        public void LowStaminaManualAndViewerDisciples_AreNeverReassigned()
        {
            MakeOreScarce();
            Find("d001").CurrentTask = "gathering_wood";
            SetStamina("d001", 0f); // Manual (default) and completely exhausted

            _scheduler.Advance(1000f); // several evaluations' worth of time, but it is not eligible
            Assert.AreEqual("gathering_wood", TaskOf("d001"),
                            "a Manual disciple is never reassigned, however tired");

            string reason;
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d001", DiscipleOwnerType.Viewer, "viewer_test_01", out reason), reason);
            var skipped = _scheduler.EvaluateDisciple("d001");

            Assert.IsTrue(skipped.Skipped);
            Assert.AreEqual("gathering_wood", TaskOf("d001"),
                            "a Viewer disciple is never reassigned, however tired");
            Assert.AreEqual(0, _taskChanged.Messages.Count);
        }

        [Test]
        public void RecoveryCooldownRejection_LeavesTheTaskAndDoesNotLoop()
        {
            _provider.TaskChangeCooldownSeconds = 12f;
            MakeOreScarce();
            SetAuto("d000");

            Assert.IsTrue(_scheduler.EvaluateDisciple("d000").Applied);
            Assert.AreEqual("gathering_ore", TaskOf("d000"));

            // The disciple is exhausted: the brain wants meditation, the 12s cooldown refuses.
            SetStamina("d000", 5f);
            _taskChanged.Messages.Clear();

            var blocked = _scheduler.EvaluateDisciple("d000");
            Assert.IsFalse(blocked.Applied);
            StringAssert.Contains("cooldown", blocked.ApplyFailReason);
            Assert.AreEqual(AutoRecoveryState.EnteringMeditation, blocked.Decision.Recovery);
            Assert.AreEqual("gathering_ore", TaskOf("d000"), "a rejected recovery switch leaves the task alone");
            Assert.AreEqual(0, _taskChanged.Messages.Count);

            // ...and it waits for the next scheduled evaluation rather than retrying in a loop.
            Assert.IsFalse(_scheduler.EvaluateDisciple("d000").Applied);
            Assert.AreEqual("gathering_ore", TaskOf("d000"));
            Assert.AreEqual(0, _taskChanged.Messages.Count);
        }

        [Test]
        public void EvaluationLogLine_CarriesStaminaRecoveryAndPerCandidateSkillBonus()
        {
            MakeOreScarce();

            // d000 (no skill XP): exhausted → the line shows stamina + the recovery decision.
            SetAuto("d000");
            Find("d000").CurrentTask = "gathering_wood";
            SetStamina("d000", 12f);
            var recovering = AutoTaskScheduler.FormatEvaluation("d000", _scheduler.EvaluateDisciple("d000"));
            StringAssert.Contains("stamina 12.0", recovering);
            StringAssert.Contains("recovery: enter meditation", recovering);
            StringAssert.Contains("skill+0.00", recovering);

            // d001 (200 gathering XP → level 2): its gathering candidates carry +0.04.
            SetAuto("d001");
            SetStamina("d001", 100f);
            var skilled = AutoTaskScheduler.FormatEvaluation("d001", _scheduler.EvaluateDisciple("d001"));
            StringAssert.Contains("recovery: normal", skilled);
            StringAssert.Contains("skill+0.04", skilled, "the skill bonus is visible per candidate");
        }

        private void MakeOreScarce()
        {
            SetStock("herb", 200);
            SetStock("wood", 200);
            SetStock("ore", 0);
            SetStock("provisions", 200);
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
