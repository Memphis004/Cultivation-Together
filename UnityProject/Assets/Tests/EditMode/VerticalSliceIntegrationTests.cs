using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using NUnit.Framework;
using UnityEngine;
using Xianxia.Sect;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;
using Xianxia.Sect.Visual;
using TestContext = NUnit.Framework.TestContext;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// P15 — the whole game as ONE path.
    ///
    /// Every test here drives the REAL services (SectStateProvider, TimeSystem,
    /// AutoTaskScheduler, DecisionExecutor, SessionSnapshotService, SaveSessionService,
    /// GameSessionCoordinator, AssignTaskHandler) through their existing seams — no
    /// reimplementation of the rules and no test-only policy. The only doubles are the
    /// ones EditMode forces on us (a deterministic scene loader, an inline save worker
    /// hop, a mutable clock, and buffering publishers).
    ///
    /// Covered end-to-end:
    ///   1. clean launch → Title → New Game → manual assignment → Auto → work/recovery
    ///      → event decision → Save → Return to Title → Continue → exact restored state;
    ///   2. 1x / 2x / 3x equivalence over equal SIMULATION time;
    ///   3. pause correctness on every axis (no loss, no jump, no catch-up);
    ///   4. Viewer Hybrid protection through local fixtures;
    ///   5. no duplicate task notifications or craft rewards (also across a save/load);
    ///   6. fractional gather/craft progress across saves;
    ///   7. building occupancy after load;
    ///   8. old-session RPC / event callbacks are rejected;
    ///   9. repeated Title/NewGame/Load cycles leak no listeners;
    ///  10. cold restart (load from disk with no live state) and the no-save refusal;
    ///  11. corrupt save fails closed + backup recovery + a failed write preserves the
    ///      previous save;
    ///  12. the debug metrics P15 asks for: task switches per disciple, productive vs
    ///      blocked vs recovery simulation seconds, and resource change over simulation
    ///      time — reported for TUNING, never used to rebalance the game.
    /// </summary>
    public class VerticalSliceIntegrationTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);

        private readonly List<string> _roots = new List<string>();

        [TearDown]
        public void TearDown()
        {
            MainThreadDispatch.ResetForTests();
            foreach (var root in _roots) DeleteDirectory(root);
            _roots.Clear();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  1. the full path
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void FullPath_Title_NewGame_ManualAuto_Decision_Save_Continue_RestoresExactState()
        {
            var h = NewHarness();

            // ---- clean launch: at Title, nothing loaded, no live session ----
            Assert.AreEqual(GameSessionPhase.Title, h.Coordinator.Phase);
            Assert.IsFalse(h.Time.IsSessionActive);
            Assert.IsFalse(h.Coordinator.IsPlaying);
            Assert.AreEqual(string.Empty, h.Loader.GetCurrentGameplayScene());
            Assert.AreEqual(0, h.Restored.Messages.Count);

            // ---- New Game ----
            var newGame = h.Coordinator.StartNewGameAsync().GetAwaiter().GetResult();
            Assert.IsTrue(newGame.Success, newGame.Error);
            Assert.AreEqual(GameSessionPhase.Playing, h.Coordinator.Phase);
            Assert.AreEqual(SceneNames.Sect, h.Loader.Current);
            Assert.IsTrue(h.Time.IsSessionActive);
            Assert.AreEqual(4, h.Roster(), "the starter roster is applied");

            // ---- manual task assignment (the AI GM path, as SECT_MASTER) ----
            h.Place("herb_plot", 0, 0);
            Assert.IsTrue(h.AssignAsSectMaster("d001", "gathering_herb"), "manual assignment must be accepted");
            Assert.AreEqual(1, h.TaskChanged.Messages.Count, "one real change = exactly one notification");
            Assert.AreEqual("gathering_herb", h.TaskOf("d001"));

            // ---- enable Auto on two NPCs, then work/recovery for 40 s of sim time ----
            Assert.IsTrue(h.SetAuto("d002"), "NPCs may opt into Auto");
            Assert.IsTrue(h.SetAuto("d003"));
            h.StepMany(realDeltaSeconds: 1f, steps: 40);

            var d001AfterWork = h.Find("d001");
            Assert.AreEqual("gathering_herb", d001AfterWork.CurrentTask,
                            "a manual (non-Auto) disciple is never reassigned by the scheduler");
            Assert.Greater(h.Raw("herb") + h.Accum("gathering_herb"), 120f,
                           "40 s of gathering must have produced something");
            Assert.LessOrEqual(d001AfterWork.Attributes.Stamina, DiscipleAttributesConfig.StaminaDefault);

            // ---- an event decision: paused, then resolved ----
            h.Time.RaiseWorldEvent("bandit_raid_001", "raiders", true,
                new List<EventChoiceInfo> { new EventChoiceInfo { ChoiceId = "fight", Label = "Fight" } });
            Assert.IsTrue(h.Time.HasPendingDecision);
            Assert.IsTrue(h.Time.IsPendingDecisionPaused, "a decision event pauses the session");

            float frozenCultivation = h.Find("d001").Attributes.CultivationProgress;
            float frozenStamina = h.Find("d001").Attributes.Stamina;
            h.StepMany(realDeltaSeconds: 1f, steps: 5);
            Assert.AreEqual(frozenCultivation, h.Find("d001").Attributes.CultivationProgress, 1e-6f,
                            "nothing advances while a decision is outstanding");
            Assert.AreEqual(frozenStamina, h.Find("d001").Attributes.Stamina, 1e-6f);

            string instanceId = h.Time.PendingInstanceId;
            var executor = new DecisionExecutor(h.Provider, h.Time, new BufferPublisher<DecisionExecutedMessage>());
            var decision = executor.Execute(instanceId, "bandit_raid_001", "fight");
            Assert.IsTrue(decision.Accepted, decision.Reason);
            Assert.IsFalse(h.Time.HasPendingDecision, "resolving consumes the occurrence");
            Assert.IsFalse(h.Time.IsPendingDecisionPaused, "and clears only the decision pause");

            // ---- work resumes after the decision ----
            float herbBeforeResume = h.Raw("herb") + h.Accum("gathering_herb");
            h.StepMany(realDeltaSeconds: 1f, steps: 10);
            Assert.Greater(h.Raw("herb") + h.Accum("gathering_herb"), herbBeforeResume,
                           "the session resumes rather than staying frozen");

            // ---- capture the exact state we expect to get back ----
            var expected = h.CaptureExpectations();
            Assert.IsTrue(h.Saves.SaveAsync().GetAwaiter().GetResult().Success);

            // ---- Return to Title ----
            var toTitle = h.Coordinator.ReturnToTitleAsync().GetAwaiter().GetResult();
            Assert.IsTrue(toTitle.Success, toTitle.Error);
            Assert.AreEqual(GameSessionPhase.Title, h.Coordinator.Phase);
            Assert.IsFalse(h.Time.IsSessionActive);

            // ---- Continue ----
            int restoredBeforeLoad = h.Restored.Messages.Count;
            var load = h.Coordinator.LoadGameAsync().GetAwaiter().GetResult();
            Assert.IsTrue(load.Success, load.Error);
            Assert.AreEqual(GameSessionPhase.Playing, h.Coordinator.Phase);
            Assert.AreEqual(SceneNames.Sect, h.Loader.Current);
            Assert.IsTrue(h.Time.IsSessionActive);
            Assert.AreEqual(restoredBeforeLoad + 1, h.Restored.Messages.Count,
                            "one committed load = exactly one restored notification");

            // ---- the restored state is the saved state, exactly ----
            h.AssertMatchesExpectations(expected);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  2. speed equivalence — equal simulation time, equal outcome
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void SpeedEquivalence_1x_2x_3x_OverEqualSimulationTime_ProducesEqualProducedMass()
        {
            const float simSeconds = 20f;

            var at1x = RunGatheringSlice(1, simSeconds);
            var at2x = RunGatheringSlice(2, simSeconds);
            var at3x = RunGatheringSlice(3, simSeconds);

            // Produced mass = whole units already banked + the held fraction. Comparing the
            // SUM is step-size independent (the floor boundary is exactly what differs).
            Assert.AreEqual(at1x.Mass, at2x.Mass, 1e-3f, "1x and 2x must agree over equal simulation time");
            Assert.AreEqual(at1x.Mass, at3x.Mass, 1e-3f, "1x and 3x must agree over equal simulation time");
            Assert.AreEqual(at1x.Rate * simSeconds, at1x.Mass, 1e-3f, "and it must equal rate x simulation time");

            Assert.AreEqual(at1x.Stamina, at2x.Stamina, 1e-3f, "stamina is a simulation-time function only");
            Assert.AreEqual(at1x.Stamina, at3x.Stamina, 1e-3f);
            Assert.AreEqual(at1x.Cultivation, at2x.Cultivation, 1e-3f, "cultivation accrual scales once, not twice");
            Assert.AreEqual(at1x.Cultivation, at3x.Cultivation, 1e-3f);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  3. pause correctness
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void Pause_FreezesEveryAxis_AndResumeHoldsProgressWithoutLossOrJump()
        {
            var h = NewHarness();
            Assert.IsTrue(h.Coordinator.StartNewGameAsync().GetAwaiter().GetResult().Success);
            h.Place("herb_plot", 0, 0);
            Assert.IsTrue(h.AssignAsSectMaster("d001", "gathering_herb"));
            Assert.IsTrue(h.SetAuto("d002"));
            h.SetSpeed(3);

            h.StepMany(1f, 5);
            float massBeforePause = h.Raw("herb") + h.Accum("gathering_herb");
            float staminaBefore = h.Find("d001").Attributes.Stamina;
            string autoTaskBefore = h.TaskOf("d002");
            int taskMessagesBefore = h.TaskChanged.Messages.Count;

            h.Time.Pause(TimePauseReason.User);
            Assert.IsTrue(h.Time.IsUserPaused);
            Assert.AreEqual(0f, h.SimDelta(1f), 1e-6f, "a paused session hands out a zero simulation delta");

            h.StepMany(1f, 30); // half a minute of real time while paused

            Assert.AreEqual(massBeforePause, h.Raw("herb") + h.Accum("gathering_herb"), 1e-4f,
                            "gathering must not advance while paused");
            Assert.AreEqual(staminaBefore, h.Find("d001").Attributes.Stamina, 1e-4f,
                            "stamina must not advance while paused");
            Assert.AreEqual(autoTaskBefore, h.TaskOf("d002"), "the AI must not evaluate while paused");
            Assert.AreEqual(taskMessagesBefore, h.TaskChanged.Messages.Count,
                            "a paused scheduler publishes nothing");

            // Resume: progress continues from exactly where it stopped — no loss, no catch-up.
            h.Time.ResumeByPlayer();
            Assert.IsFalse(h.Time.IsPaused);
            h.StepMany(1f, 5);

            float massAfterResume = h.Raw("herb") + h.Accum("gathering_herb");
            Assert.Greater(massAfterResume, massBeforePause, "resuming continues the held work");
            Assert.AreEqual(massBeforePause + 3f, massAfterResume, 0.05f,
                            "5 s at 3x = 15 s of simulation = 3.0 units, with no catch-up for the pause");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  4. Viewer Hybrid protection through local fixtures
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void ViewerHybridProtection_LocalFixture_BlocksMasterWhileOwnerActive_ThenAllows()
        {
            var h = NewHarness();
            Assert.IsTrue(h.Coordinator.StartNewGameAsync().GetAwaiter().GetResult().Success);
            h.Place("herb_plot", 0, 0);

            // Local fixture: a synthetic viewer id with a controllable real-time clock.
            Assert.IsTrue(h.TrySetOwner("d001", DiscipleOwnerType.Viewer, "viewer_test_01"));

            Assert.IsTrue(h.AssignViaHandler("viewer_test_01", "d001", "gathering_herb"),
                          "the viewer controls its own disciple");
            h.Clock.UtcNow = T0.AddSeconds(600);
            Assert.IsTrue(h.AssignViaHandler("viewer_test_01", "d001", "meditation"),
                          "the owner may still issue commands at the exact window edge");
            // NOTE: a successful owner command refreshes the owner's own activity, so the
            // protection window restarts from that instant (T0 + 600).

            // The master is blocked while the owner is inside the 10 real-time minutes…
            var permission = h.Provider.CheckTaskPermission(SectStateProvider.SectMasterRequesterId, "d001", "gathering_herb");
            Assert.AreEqual(TaskPermissionOutcome.Denied, permission.Outcome);
            Assert.IsTrue(permission.OwnerProtected);
            Assert.IsFalse(h.Provider.TryAssignTask(SectStateProvider.SectMasterRequesterId, "d001", "gathering_herb", out _));
            Assert.IsFalse(h.Provider.CanBreakthrough(SectStateProvider.SectMasterRequesterId, "d001", out _),
                           "the same authority gates breakthrough as gates task assignment");
            Assert.AreEqual("meditation", h.TaskOf("d001"), "a refused command changes nothing");

            // …and allowed once the window has passed (strictly greater than 600 s after the
            // owner's last command).
            h.Clock.UtcNow = T0.AddSeconds(1301);
            var afterWindow = h.Provider.CheckTaskPermission(SectStateProvider.SectMasterRequesterId, "d001", "gathering_herb");
            Assert.IsTrue(afterWindow.Allowed, afterWindow.Reason);
            Assert.IsTrue(h.AssignViaHandler(SectStateProvider.SectMasterRequesterId, "d001", "gathering_herb"));
            Assert.AreEqual("gathering_herb", h.TaskOf("d001"));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  5. no duplicate notifications / rewards
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void NoDuplicateTaskNotificationsOrCraftRewards_AcrossTicksAndAcrossASaveLoad()
        {
            var h = NewHarness();
            Assert.IsTrue(h.Coordinator.StartNewGameAsync().GetAwaiter().GetResult().Success);
            h.Place("herb_plot", 0, 0);
            h.Place("pill_hall", 5, 5);

            // one real change = exactly one notification; a no-op publishes nothing.
            Assert.IsTrue(h.AssignAsSectMaster("d001", "gathering_herb"));
            Assert.AreEqual(1, h.TaskChanged.Messages.Count);
            Assert.IsTrue(h.AssignAsSectMaster("d001", "gathering_herb"), "re-assigning the same task is a no-op");
            Assert.AreEqual(1, h.TaskChanged.Messages.Count, "a no-op must not notify anyone");

            // Park the gatherer so the craft's herb cost is the only thing moving herb.
            Assert.IsTrue(h.AssignAsSectMaster("d001", "meditation"));
            Assert.AreEqual(2, h.TaskChanged.Messages.Count);

            // A craft completes exactly once: one herb cost, one progress reset, XP once.
            Assert.IsTrue(h.AssignAsSectMaster("d002", "refining_elixir"));
            int herbAtStart = h.Raw("herb");
            h.StepMany(1f, 20); // refining_elixir needs 20 s of simulation

            const int cost = 10;
            Assert.AreEqual(herbAtStart - cost, h.Raw("herb"),
                            "the craft must consume its cost exactly once");
            Assert.AreEqual(0f, h.CraftProgress("d002"), 1e-3f, "a completed craft resets its progress");
            float xpAfterCraft = h.TotalSkillXp("d002");
            Assert.Greater(xpAfterCraft, 0f, "a completed craft awards skill XP");

            // Save in the middle of the next craft, then load: no re-award for what was already paid.
            h.StepMany(1f, 5);
            float xpBeforeLoad = h.TotalSkillXp("d002");
            Assert.IsTrue(h.Saves.SaveAsync().GetAwaiter().GetResult().Success);
            Assert.IsTrue(h.Coordinator.LoadGameAsync().GetAwaiter().GetResult().Success);

            int herbAfterLoad = h.Raw("herb");
            Assert.AreEqual(xpBeforeLoad, h.TotalSkillXp("d002"), 1e-4f, "XP survives the round trip");
            h.StepMany(1f, 1); // the first post-load tick must not re-award or re-consume
            Assert.AreEqual(herbAfterLoad, h.Raw("herb"), "the restored partial craft must not pay twice");
            Assert.AreEqual(xpBeforeLoad, h.TotalSkillXp("d002"), 1e-4f, "and must not re-award XP");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  6. fractional progress across saves
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void FractionalGatherAndCraftProgress_SurviveSaveAndLoad_WithoutDoubleProduction()
        {
            var h = NewHarness();
            Assert.IsTrue(h.Coordinator.StartNewGameAsync().GetAwaiter().GetResult().Success);
            h.Place("herb_plot", 0, 0);
            h.Place("pill_hall", 5, 5);
            Assert.IsTrue(h.AssignAsSectMaster("d001", "gathering_herb"));
            Assert.IsTrue(h.AssignAsSectMaster("d002", "refining_elixir"));

            h.StepMany(1f, 5); // 1.0 herb, craft progress 5 s
            float gatherRemainder = h.Accum("gathering_herb");
            float craftProgress = h.CraftProgress("d002");
            Assert.AreEqual(5f, craftProgress, 1e-3f, "five whole-second ticks leave exactly 5 s of craft progress");

            Assert.IsTrue(h.Saves.SaveAsync().GetAwaiter().GetResult().Success);
            Assert.IsTrue(h.Coordinator.LoadGameAsync().GetAwaiter().GetResult().Success);

            Assert.AreEqual(gatherRemainder, h.Accum("gathering_herb"), 1e-4f,
                            "the fractional gather remainder survives the round trip");
            Assert.AreEqual(craftProgress, h.CraftProgress("d002"), 1e-4f,
                            "the fractional craft progress survives the round trip");

            // Park the gatherer so the craft's cost is the only thing moving herb, then tick on
            // from the restored value: it must complete once — not twice.
            Assert.IsTrue(h.AssignAsSectMaster("d001", "meditation"));
            int herbBeforeCompletion = h.Raw("herb");
            h.StepMany(1f, 15); // 5 + 15 = 20 s → one completion
            Assert.AreEqual(herbBeforeCompletion - 10, h.Raw("herb"));
            Assert.AreEqual(0f, h.CraftProgress("d002"), 1e-3f, "one completion, then progress starts over");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  7. building occupancy after load
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void BuildingOccupancy_AfterLoad_MatchesPlacedBuildings_AndStillBlocksOverlap()
        {
            var h = NewHarness();
            Assert.IsTrue(h.Coordinator.StartNewGameAsync().GetAwaiter().GetResult().Success);

            var herb = h.Place("herb_plot", 0, 0);
            var pill = h.Place("pill_hall", 5, 5);
            Assert.IsTrue(h.Saves.SaveAsync().GetAwaiter().GetResult().Success);
            Assert.IsTrue(h.Coordinator.LoadGameAsync().GetAwaiter().GetResult().Success);

            // Rebuild occupancy from the RESTORED state exactly like BuildingSystem does.
            var grid = new BuildingGrid(50, 50);
            var placed = h.Provider.BuildSectEconomyState().PlacedBuildings;
            Assert.AreEqual(2, placed.Count, "both buildings survived the round trip");
            foreach (var building in placed)
            {
                var def = h.Defs.GetById(building.DefId);
                Assert.IsTrue(grid.CanPlace(building.GridX, building.GridZ, def.GridWidth, def.GridHeight, building.Rotation),
                              building.DefId + " must still fit where it was saved");
                grid.Occupy(building.InstanceId, building.GridX, building.GridZ, def.GridWidth, def.GridHeight, building.Rotation);
            }

            Assert.AreEqual(herb.InstanceId, grid.GetOccupant(0, 0));
            Assert.AreEqual(pill.InstanceId, grid.GetOccupant(5, 5));

            // …and the restored occupancy is authoritative for new placements.
            string reason;
            PlacedBuildingState rejected;
            Assert.IsFalse(h.Provider.TryPlaceBuilding("herb_plot", 0, 0, 0, grid, out reason, out rejected),
                           "an occupied cell must still be refused after a load");
            Assert.IsFalse(string.IsNullOrEmpty(reason));
            Assert.AreEqual(2, h.Provider.BuildSectEconomyState().PlacedBuildings.Count, "a refused placement changes nothing");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  8. old-session RPC / event callbacks
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void OldSessionRpcAndEventCallbacks_AreRejected_AfterReturnToTitle()
        {
            var h = NewHarness();
            Assert.IsTrue(h.Coordinator.StartNewGameAsync().GetAwaiter().GetResult().Success);
            h.Place("herb_plot", 0, 0);

            // A waiter parked while Playing…
            var waiter = h.Time.WaitForNextWorldEventAsync();
            Assert.IsFalse(waiter.Status.IsCompleted());

            Assert.IsTrue(h.Coordinator.ReturnToTitleAsync().GetAwaiter().GetResult().Success);

            // …is completed with the session-ended outcome, not a fabricated event.
            var closed = waiter.GetAwaiter().GetResult();
            Assert.AreEqual(string.Empty, closed.EventId);
            StringAssert.Contains("session ended", closed.Description);

            // A new awaiter outside a session is answered immediately.
            var immediate = h.Time.WaitForNextWorldEventAsync().GetAwaiter().GetResult();
            Assert.AreEqual(string.Empty, immediate.EventId);
            StringAssert.Contains("no active session", immediate.Description);

            // Every write path refuses a request from the dead session.
            WithImmediateMainThread(() =>
            {
                var assign = new AssignTaskHandler(h.Provider, h.Coordinator);
                var assigned = assign.InvokeAsync(new AssignTaskRequest
                {
                    RequesterId = SectStateProvider.SectMasterRequesterId,
                    DiscipleId = "d000",
                    TaskId = "gathering_herb",
                }).GetAwaiter().GetResult();
                Assert.IsFalse(assigned.Success);
                StringAssert.Contains("no active session", assigned.FailReason);

                var breakthrough = new AttemptBreakthroughHandler(h.Provider, h.Coordinator);
                var attempted = breakthrough.InvokeAsync(new AttemptBreakthroughRequest
                {
                    RequesterId = SectStateProvider.SectMasterRequesterId,
                    DiscipleId = "d000",
                }).GetAwaiter().GetResult();
                Assert.IsFalse(attempted.Success);
                StringAssert.Contains("no active session", attempted.FailReason);
            });

            h.Time.RaiseWorldEvent("bandit_raid_001", "raiders", true,
                new List<EventChoiceInfo> { new EventChoiceInfo { ChoiceId = "fight", Label = "Fight" } });
            var executor = new DecisionExecutor(h.Provider, h.Time, new BufferPublisher<DecisionExecutedMessage>());
            var rejected = executor.Execute("evt_000001", "bandit_raid_001", "fight");
            Assert.IsFalse(rejected.Accepted);
            StringAssert.Contains("no active session", rejected.Reason);
            Assert.IsTrue(h.Time.HasPendingDecision, "a refused decision does not consume the pending occurrence");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  9. repeated cycles leak nothing
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void RepeatedTitleNewGameLoadCycles_LeakNoListeners_AndNotifyExactlyOncePerEvent()
        {
            var h = NewHarness();

            int phaseDeltaPerNewGame = -1;
            int phaseDeltaPerReturn = -1;
            int restoredExpected = 0;
            int taskChangesExpected = 0;

            for (int cycle = 1; cycle <= 3; cycle++)
            {
                int phasesBeforeNewGame = h.Phases.Messages.Count;
                Assert.IsTrue(h.Coordinator.StartNewGameAsync().GetAwaiter().GetResult().Success);
                int newGamePhases = h.Phases.Messages.Count - phasesBeforeNewGame;
                Assert.Greater(newGamePhases, 0, "a New Game must announce its phases");

                if (phaseDeltaPerNewGame < 0) phaseDeltaPerNewGame = newGamePhases;
                else Assert.AreEqual(phaseDeltaPerNewGame, newGamePhases,
                                     "each New Game must publish the same number of phase notifications — "
                                     + "a leaked listener would add extra ones (cycle " + cycle + ")");

                Assert.AreEqual(4, h.Roster(), "the roster resets on every New Game");
                Assert.AreEqual(120, h.Raw("herb"), "the stockpile resets on every New Game");

                h.Place("herb_plot", 0, 0);
                Assert.IsTrue(h.AssignAsSectMaster("d001", "gathering_herb"));
                taskChangesExpected++;
                h.StepMany(1f, 3);

                Assert.IsTrue(h.Saves.SaveAsync().GetAwaiter().GetResult().Success);

                int phasesBeforeReturn = h.Phases.Messages.Count;
                Assert.IsTrue(h.Coordinator.ReturnToTitleAsync().GetAwaiter().GetResult().Success);
                int returnPhases = h.Phases.Messages.Count - phasesBeforeReturn;

                if (phaseDeltaPerReturn < 0) phaseDeltaPerReturn = returnPhases;
                else Assert.AreEqual(phaseDeltaPerReturn, returnPhases,
                                     "each Return to Title must publish the same number of phase notifications");

                int restoredBeforeLoad = h.Restored.Messages.Count;
                Assert.IsTrue(h.Coordinator.LoadGameAsync().GetAwaiter().GetResult().Success);
                restoredExpected++;
                Assert.AreEqual(restoredBeforeLoad + 1, h.Restored.Messages.Count,
                                "exactly one restored notification per committed load — cycle " + cycle);
                Assert.AreEqual(taskChangesExpected, h.TaskChanged.Messages.Count,
                                "replaying a cycle must not replay old notifications");
            }

            Assert.AreEqual(3, restoredExpected);
            Assert.AreEqual(3, h.TaskChanged.Messages.Count);
            Assert.AreEqual(GameSessionPhase.Playing, h.Coordinator.Phase);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  10. cold restart + no-save refusal
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void ColdRestart_LoadsTheSaveFromDiskWithNoLiveState_AndRefusesWhenNoSaveExists()
        {
            // Session A plays and saves.
            var a = NewHarness();
            Assert.IsTrue(a.Coordinator.StartNewGameAsync().GetAwaiter().GetResult().Success);
            a.Place("herb_plot", 0, 0);
            Assert.IsTrue(a.AssignAsSectMaster("d001", "gathering_herb"));
            a.StepMany(1f, 7);
            a.SetSpeed(2);
            var expected = a.CaptureExpectations();
            Assert.IsTrue(a.Saves.SaveAsync().GetAwaiter().GetResult().Success);

            // Cold restart: a brand-new process-like harness over the SAME directory, with no
            // live state carried over and a clock far in the future.
            var b = Harness.Create(a.Root, T0.AddDays(3));
            Assert.AreEqual(GameSessionPhase.Title, b.Coordinator.Phase);
            Assert.AreEqual(0, b.Restored.Messages.Count);

            var load = b.Coordinator.LoadGameAsync().GetAwaiter().GetResult();
            Assert.IsTrue(load.Success, load.Error);
            Assert.AreEqual(GameSessionPhase.Playing, b.Coordinator.Phase);
            b.AssertMatchesExpectations(expected);

            // A cold start with NO save refuses Continue and stays at Title.
            var empty = NewHarness();
            var noSave = empty.Coordinator.LoadGameAsync().GetAwaiter().GetResult();
            Assert.IsFalse(noSave.Success, "there is nothing to continue");
            Assert.IsFalse(string.IsNullOrEmpty(noSave.Error));
            Assert.AreEqual(GameSessionPhase.Title, empty.Coordinator.Phase);
            Assert.IsFalse(empty.Time.IsSessionActive);
            Assert.AreEqual(0, empty.Loader.LoadCount, "a refused Continue must not load a scene");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  11. corrupt save / failed save & load paths
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void CorruptSave_FailsClosed_AndRecoversFromTheBackup_AndAFailedWriteKeepsThePreviousSave()
        {
            var h = NewHarness();
            Assert.IsTrue(h.Coordinator.StartNewGameAsync().GetAwaiter().GetResult().Success);

            // First save: becomes the backup on the next successful write.
            h.Place("herb_plot", 0, 0);
            Assert.IsTrue(h.AssignAsSectMaster("d001", "gathering_herb"));
            h.StepMany(1f, 5);
            Assert.IsTrue(h.Saves.SaveAsync().GetAwaiter().GetResult().Success);

            // Second save (this rotates the first into the backup).
            h.StepMany(1f, 5);
            Assert.IsTrue(h.Saves.SaveAsync().GetAwaiter().GetResult().Success);
            Assert.IsTrue(File.Exists(h.Paths.BackupPath), "a second write keeps the previous save as the backup");
            byte[] committedBytes = File.ReadAllBytes(h.Paths.SlotPath);
            Assert.Greater(committedBytes.Length, 0, "a committed save has bytes on disk");

            // Corrupt the slot, then Continue: the backup carries the session.
            File.WriteAllBytes(h.Paths.SlotPath, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 });
            var recovered = h.Coordinator.LoadGameAsync().GetAwaiter().GetResult();
            Assert.IsTrue(recovered.Success, recovered.Error);
            Assert.AreEqual(GameSessionPhase.Playing, h.Coordinator.Phase);
            Assert.AreEqual("gathering_herb", h.TaskOf("d001"), "the recovered save is the previous committed one");

            // A failed WRITE must report itself, keep the committed save intact and leave live
            // state alone. The repository's size limit is the existing, deterministic failure
            // seam (a 1-byte limit can never fit a real envelope).
            byte[] beforeFailedWrite = File.ReadAllBytes(h.Paths.SlotPath);
            string taskAtCheck = h.TaskOf("d001");
            int herbAtCheck = h.Raw("herb");
            var limited = new SaveSessionService(h.Snapshots,
                new SaveSlotRepository(new SystemSaveFileSystem(), h.Paths, maxSaveFileBytes: 1),
                new InlineSaveWorkScheduler(), h.Transition, new SaveOperationGate());

            var failedWrite = limited.SaveAsync().GetAwaiter().GetResult();
            Assert.IsFalse(failedWrite.Success, "an over-limit write must report failure");
            Assert.IsFalse(string.IsNullOrEmpty(failedWrite.Error));
            Assert.AreEqual(taskAtCheck, h.TaskOf("d001"), "a failed save never mutates live state");
            Assert.AreEqual(herbAtCheck, h.Raw("herb"));
            CollectionAssert.AreEqual(beforeFailedWrite, File.ReadAllBytes(h.Paths.SlotPath),
                                      "a failed write must preserve the previous save byte for byte");

            // Corrupt BOTH generations: the load must fail closed and leave live state alone.
            File.WriteAllBytes(h.Paths.SlotPath, new byte[] { 0xFF, 0xEE });
            File.WriteAllBytes(h.Paths.BackupPath, new byte[] { 0xFF, 0xEE });
            string taskBefore = h.TaskOf("d001");
            int herbBefore = h.Raw("herb");
            int loadsBefore = h.Loader.LoadCount;

            var refused = h.Coordinator.LoadGameAsync().GetAwaiter().GetResult();
            Assert.IsFalse(refused.Success, "a corrupt save must never be restored");
            Assert.IsFalse(string.IsNullOrEmpty(refused.Error));
            Assert.AreEqual(GameSessionPhase.Playing, h.Coordinator.Phase, "the live session is untouched");
            Assert.AreEqual(taskBefore, h.TaskOf("d001"));
            Assert.AreEqual(herbBefore, h.Raw("herb"));
            Assert.AreEqual(loadsBefore, h.Loader.LoadCount, "a corrupt save never triggers destructive scene work");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  12. debug metrics (collected, reported, NOT auto-applied)
        // ══════════════════════════════════════════════════════════════════════

        [Test]
        public void DebugMetrics_TaskSwitches_WorkMix_AndResourceChangeOverSimulationTime()
        {
            const float stepSeconds = 1f;
            const int steps = 120;

            var h = NewHarness();
            Assert.IsTrue(h.Coordinator.StartNewGameAsync().GetAwaiter().GetResult().Success);
            h.Place("herb_plot", 0, 0);
            h.RecordSessionStart(); // after the building cost, so produced mass = what work added

            var roster = new List<string>();
            foreach (var d in h.Provider.BuildSectEconomyState().Disciples) roster.Add(d.DiscipleId);
            Assert.AreEqual(h.Roster(), roster.Count);

            var secondsByOutcome = new Dictionary<string, Dictionary<DiscipleWorkOutcome, float>>();
            var switches = new Dictionary<string, int>();
            var previousTask = new Dictionary<string, string>();
            foreach (var id in roster)
            {
                secondsByOutcome[id] = new Dictionary<DiscipleWorkOutcome, float>();
                switches[id] = 0;
                previousTask[id] = h.TaskOf(id); // the task the session started with
            }

            // The manual assignment and the Auto opt-in happen before the loop, so count them
            // with the same rule the loop uses — otherwise the observed switches would undercount
            // the notifications the session actually published.
            Assert.IsTrue(h.AssignAsSectMaster("d001", "gathering_herb"));
            Assert.IsTrue(h.SetAuto("d002"));
            Assert.IsTrue(h.SetAuto("d003"));
            foreach (var id in roster)
            {
                if (h.TaskOf(id) != previousTask[id])
                {
                    switches[id]++;
                    previousTask[id] = h.TaskOf(id);
                }
            }

            // Resource mass by producing task, so the metric can be checked against reality.
            var massByTask = new Dictionary<string, float>();
            var producedSeconds = new Dictionary<string, float>();
            foreach (var task in new[] { "gathering_herb", "gathering_wood" })
            {
                massByTask[task] = 0f;
                producedSeconds[task] = 0f;
            }

            for (int i = 0; i < steps; i++)
            {
                var taskThisTick = new Dictionary<string, string>();
                foreach (var id in roster) taskThisTick[id] = h.TaskOf(id);

                float delta = h.SimDelta(stepSeconds);
                h.Provider.TickGathering(delta);
                Assert.AreEqual(roster.Count, h.Provider.LastTickOutcomeRecordCountForTest,
                                "every disciple is accounted for in every tick");

                foreach (var id in roster)
                {
                    DiscipleWorkOutcome outcome;
                    Assert.IsTrue(h.Provider.TryGetLastWorkOutcome(id, out outcome), id + " has an outcome");
                    float prior;
                    secondsByOutcome[id].TryGetValue(outcome, out prior);
                    secondsByOutcome[id][outcome] = prior + delta;

                    if (outcome == DiscipleWorkOutcome.ProductiveGathering)
                    {
                        string task = taskThisTick[id];
                        if (producedSeconds.ContainsKey(task)) producedSeconds[task] += delta;
                    }
                }

                h.Provider.TickCrafting(delta);
                h.Scheduler.Advance(delta);

                foreach (var id in roster)
                {
                    string now = h.TaskOf(id);
                    if (now != previousTask[id])
                    {
                        switches[id]++;
                        previousTask[id] = now;
                    }
                }
            }

            foreach (var task in new[] { "gathering_herb", "gathering_wood" })
                massByTask[task] = h.ProducedMass(task, task == "gathering_herb" ? "herb" : "wood");

            // ---- accounting invariants (the metrics must be complete, not approximate) ----
            foreach (var id in roster)
            {
                float total = 0f;
                foreach (var pair in secondsByOutcome[id]) total += pair.Value;
                Assert.AreEqual(steps * stepSeconds, total, 1e-3f,
                                id + ": every simulated second must fall in exactly one category");
            }

            // Each resource's mass must equal rate x the seconds actually spent producing it.
            const float gatherRatePerSecond = 0.2f;
            Assert.AreEqual(gatherRatePerSecond * producedSeconds["gathering_herb"], massByTask["gathering_herb"], 1e-2f,
                            "herb mass must match the measured productive gathering time");
            Assert.AreEqual(gatherRatePerSecond * producedSeconds["gathering_wood"], massByTask["gathering_wood"], 1e-2f,
                            "wood mass must match the measured productive gathering time");

            // Task switches observed by polling must equal the notifications actually published.
            foreach (var id in roster)
            {
                int notified = 0;
                foreach (var message in h.TaskChanged.Messages)
                    if (message.DiscipleId == id) notified++;
                Assert.AreEqual(notified, switches[id], id + ": switch count must match the published changes");
            }

            // ---- the report (for a human to interpret; nothing is applied automatically) ----
            var report = new System.Text.StringBuilder();
            report.AppendLine("P15 debug metrics — " + steps + " steps x " + stepSeconds + "s sim time, speed "
                              + h.Speed + "x, roster " + roster.Count);
            foreach (var id in roster)
            {
                float productive = Seconds(secondsByOutcome[id], DiscipleWorkOutcome.ProductiveGathering)
                                 + Seconds(secondsByOutcome[id], DiscipleWorkOutcome.ProductiveCrafting);
                float blocked = Seconds(secondsByOutcome[id], DiscipleWorkOutcome.Blocked);
                float recovery = Seconds(secondsByOutcome[id], DiscipleWorkOutcome.Resting);
                float other = Seconds(secondsByOutcome[id], DiscipleWorkOutcome.None);
                report.AppendLine(string.Format(
                    "  {0}: task={1,-16} switches={2} productive={3:0.0}s blocked={4:0.0}s recovery={5:0.0}s idle={6:0.0}s stamina={7:0.0}",
                    id, h.TaskOf(id), switches[id], productive, blocked, recovery, other,
                    h.Find(id).Attributes.Stamina));
            }
            foreach (var task in new[] { "gathering_herb", "gathering_wood" })
            {
                string resource = task == "gathering_herb" ? "herb" : "wood";
                report.AppendLine(string.Format(
                    "  resource {0}: {1:0.0} units over {2:0.0}s sim ({3:0.00}/sim-min) from {4:0.0}s productive",
                    resource, massByTask[task], steps * stepSeconds,
                    massByTask[task] / (steps * stepSeconds / 60f), producedSeconds[task]));
            }
            TestContext.WriteLine(report.ToString());
        }

        // ══════════════════════════════════════════════════════════════════════
        //  harness
        // ══════════════════════════════════════════════════════════════════════

        private Harness NewHarness()
        {
            string root = Path.Combine(Path.GetTempPath(), "sect_vslice_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            _roots.Add(root);
            return Harness.Create(root, T0);
        }

        private static float Seconds(Dictionary<DiscipleWorkOutcome, float> map, DiscipleWorkOutcome key)
        {
            float value;
            return map.TryGetValue(key, out value) ? value : 0f;
        }

        /// <summary>The per-disciple / per-resource facts a save/load round trip must preserve.</summary>
        private sealed class Expectations
        {
            public readonly List<DiscipleFact> Disciples = new List<DiscipleFact>();
            public readonly Dictionary<string, int> RawResources = new Dictionary<string, int>();
            public readonly Dictionary<string, float> GatherAccumulators = new Dictionary<string, float>();
            public readonly Dictionary<string, float> CraftProgress = new Dictionary<string, float>();
            public int SimulationSpeed;
            public bool HasPlacedBuildings;
        }

        private sealed class DiscipleFact
        {
            public string DiscipleId;
            public string Task;
            public DiscipleControlMode ControlMode;
            public DiscipleOwnerType OwnerType;
            public string OwnerId;
            public float Stamina;
            public string CultivationStageId;
            public float CultivationProgress;
        }

        private sealed class Harness
        {
            public string Root;
            public SaveSlotPaths Paths;
            public SaveSlotRepository Repo;
            public MutableClock Clock;
            public SectStateProvider Provider;
            public TimeSystem Time;
            public BuildingDefPool Defs;
            public BufferPublisher<SectResourceChangedMessage> Resources;
            public BufferPublisher<DiscipleTaskChangedMessage> TaskChanged;
            public BufferPublisher<SessionRestoredMessage> Restored;
            public BufferPublisher<SessionPhaseChangedMessage> Phases;
            public SessionSnapshotService Snapshots;
            public SessionTransitionTracker Transition;
            public AutoTaskScheduler Scheduler;
            public SaveSessionService Saves;
            public FakeSceneLoader Loader;
            public CountingUiCloser Ui;
            public GameSessionCoordinator Coordinator;

            /// <summary>The speed this harness asked for — used to compute the exact delta a frame would hand out.</summary>
            public int Speed = 1;

            public static Harness Create(string root, DateTime nowUtc)
            {
                var h = new Harness();
                h.Root = root;
                h.Clock = new MutableClock(nowUtc);
                h.Paths = new SaveSlotPaths(root);
                h.Repo = new SaveSlotRepository(new SystemSaveFileSystem(), h.Paths);
                h.Resources = new BufferPublisher<SectResourceChangedMessage>();
                h.TaskChanged = new BufferPublisher<DiscipleTaskChangedMessage>();

                h.Provider = new SectStateProvider(
                    new BufferPublisher<DiscipleRecruitedMessage>(),
                    h.Resources,
                    new BufferPublisher<AvatarEquipmentChangedMessage>(),
                    new BufferPublisher<DiscipleChibiBackendChangedMessage>(),
                    new AvatarPartPool(),
                    VisualRuntimeConfig.Instance,
                    new DefaultEntitlementProvider(),
                    new BuildingDefPool(),
                    new BufferPublisher<BuildingPlacedMessage>(),
                    h.TaskChanged,
                    new BufferPublisher<DiscipleOwnerChangedMessage>(),
                    h.Clock,
                    new BufferPublisher<DiscipleControlModeChangedMessage>(),
                    new BufferPublisher<DiscipleCultivationChangedMessage>());
                h.Provider.TaskChangeCooldownSeconds = 0f; // cadence is not what this slice measures
                h.Defs = new BuildingDefPool();

                h.Time = new TimeSystem(
                    new BufferPublisher<TimeSpeedChangedMessage>(),
                    new BufferPublisher<WorldEventTriggeredMessage>());
                h.Restored = new BufferPublisher<SessionRestoredMessage>();
                h.Phases = new BufferPublisher<SessionPhaseChangedMessage>();
                h.Snapshots = new SessionSnapshotService(h.Provider, h.Time, h.Restored, h.Clock);
                h.Transition = new SessionTransitionTracker();
                h.Scheduler = new AutoTaskScheduler(h.Provider, h.Time)
                {
                    EvaluationIntervalSeconds = 10f,
                    MinimumDwellSeconds = 0f,
                    ImprovementMargin = 0f,
                };
                h.Saves = new SaveSessionService(h.Snapshots, h.Repo, new InlineSaveWorkScheduler(),
                                                 h.Transition, new SaveOperationGate());
                h.Loader = new FakeSceneLoader();
                h.Ui = new CountingUiCloser();
                h.Coordinator = new GameSessionCoordinator(
                    h.Loader, h.Snapshots, h.Time, new PrototypeStarterStateFactory(), h.Transition,
                    h.Saves, h.Scheduler, null, null, h.Ui, h.Phases);
                return h;
            }

            // ---- state access ----

            public int Roster() => Provider.BuildSectEconomyState().Disciples.Count;

            public DiscipleState Find(string id)
            {
                var disciples = Provider.BuildSectEconomyState().Disciples;
                for (int i = 0; i < disciples.Count; i++)
                    if (disciples[i].DiscipleId == id) return disciples[i];
                return null;
            }

            public string TaskOf(string id) => Find(id).CurrentTask;

            public int Raw(string resource)
            {
                int value;
                return Provider.BuildSectEconomyState().Stockpile.RawResources.TryGetValue(resource, out value) ? value : 0;
            }

            public float Accum(string taskId)
            {
                float value;
                return Snapshots.CaptureSnapshot().GatherAccumulators.TryGetValue(taskId, out value) ? value : 0f;
            }

            public float CraftProgress(string discipleId)
            {
                float value;
                return Snapshots.CaptureSnapshot().CraftProgress.TryGetValue(discipleId, out value) ? value : 0f;
            }

            /// <summary>All skill XP of one disciple — category-name agnostic on purpose.</summary>
            public float TotalSkillXp(string discipleId)
            {
                float total = 0f;
                var disciple = Find(discipleId);
                if (disciple == null || disciple.Attributes == null) return total;
                foreach (var pair in disciple.Attributes.SkillXp) total += pair.Value;
                return total;
            }

            // ---- driving ----

            public void SetSpeed(int speed)
            {
                Speed = speed;
                Time.SetSpeed(speed);
            }

            /// <summary>The delta TimeSystem hands a consumer for one real (un-scaled) frame delta.</summary>
            public float SimDelta(float realDeltaSeconds)
                => TimeSystem.ComputeSimulationDelta(Time.IsPaused || !Time.IsSessionActive, Speed, realDeltaSeconds);

            /// <summary>One frame's worth of work: both production ticks plus one scheduler pass.</summary>
            public void Step(float realDeltaSeconds)
            {
                float delta = SimDelta(realDeltaSeconds);
                Provider.TickGathering(delta);
                Provider.TickCrafting(delta);
                Scheduler.Advance(delta);
            }

            public void StepMany(float realDeltaSeconds, int steps)
            {
                for (int i = 0; i < steps; i++) Step(realDeltaSeconds);
            }

            /// <summary>Whole units banked plus the held fraction — mass, not "stockpile level".</summary>
            public float TotalMass(string taskId, string resource) => Raw(resource) + Accum(taskId);

            private readonly Dictionary<string, float> _sessionStartRaw = new Dictionary<string, float>();

            /// <summary>
            /// Record the session's starting stockpile so <see cref="ProducedMass"/> can report
            /// production rather than the level (the starter stockpile is not production).
            /// Call it right after a New Game.
            /// </summary>
            public void RecordSessionStart()
            {
                _sessionStartRaw.Clear();
                foreach (var pair in Provider.BuildSectEconomyState().Stockpile.RawResources)
                    _sessionStartRaw[pair.Key] = pair.Value;
            }

            /// <summary>Mass PRODUCED since the session started.</summary>
            public float ProducedMass(string taskId, string resource)
            {
                float start;
                float startRaw = _sessionStartRaw.TryGetValue(resource, out start) ? start : 0f;
                return TotalMass(taskId, resource) - startRaw;
            }

            // ---- entry points a player/bridge would use ----

            public PlacedBuildingState Place(string defId, int x, int z)
            {
                string reason;
                PlacedBuildingState placed;
                Assert.IsTrue(Provider.TryPlaceBuilding(defId, x, z, 0, new BuildingGrid(50, 50), out reason, out placed), reason);
                return placed;
            }

            public bool SetAuto(string discipleId)
            {
                string reason;
                return Provider.TrySetDiscipleControlMode(discipleId, DiscipleControlMode.Auto, out reason);
            }

            public bool TrySetOwner(string discipleId, DiscipleOwnerType type, string ownerId)
            {
                string reason;
                return Provider.TrySetDiscipleOwner(discipleId, type, ownerId, out reason);
            }

            /// <summary>The AI-GM / sect-master path (the requester the bridge always sends).</summary>
            public bool AssignAsSectMaster(string discipleId, string taskId)
                => AssignViaHandler(SectStateProvider.SectMasterRequesterId, discipleId, taskId);

            /// <summary>Manual assignment through the same handler the bridge calls.</summary>
            public bool AssignViaHandler(string requesterId, string discipleId, string taskId)
            {
                bool success = false;
                string failReason = null;
                WithImmediateMainThread(() =>
                {
                    var handler = new AssignTaskHandler(Provider, Coordinator);
                    var response = handler.InvokeAsync(new AssignTaskRequest
                    {
                        RequesterId = requesterId,
                        DiscipleId = discipleId,
                        TaskId = taskId,
                    }).GetAwaiter().GetResult();
                    success = response.Success;
                    failReason = response.FailReason;
                });
                if (!success) Debug.Log("[VerticalSlice] assignment refused: " + failReason);
                return success;
            }

            // ---- save/load expectations ----

            public Expectations CaptureExpectations()
            {
                var expected = new Expectations();
                var snapshot = Snapshots.CaptureSnapshot();
                foreach (var disciple in snapshot.Economy.Disciples)
                {
                    expected.Disciples.Add(new DiscipleFact
                    {
                        DiscipleId = disciple.DiscipleId,
                        Task = disciple.CurrentTask,
                        ControlMode = disciple.ControlMode,
                        OwnerType = disciple.OwnerType,
                        OwnerId = disciple.OwnerId,
                        Stamina = disciple.Attributes.Stamina,
                        CultivationStageId = disciple.Attributes.CultivationStageId,
                        CultivationProgress = disciple.Attributes.CultivationProgress,
                    });
                }
                foreach (var pair in snapshot.Economy.Stockpile.RawResources) expected.RawResources[pair.Key] = pair.Value;
                foreach (var pair in snapshot.GatherAccumulators) expected.GatherAccumulators[pair.Key] = pair.Value;
                foreach (var pair in snapshot.CraftProgress) expected.CraftProgress[pair.Key] = pair.Value;
                expected.SimulationSpeed = snapshot.SimulationSpeed;
                expected.HasPlacedBuildings = snapshot.Economy.PlacedBuildings.Count > 0;
                return expected;
            }

            public void AssertMatchesExpectations(Expectations expected)
            {
                var snapshot = Snapshots.CaptureSnapshot();
                Assert.AreEqual(expected.SimulationSpeed, snapshot.SimulationSpeed, "simulation speed");
                Assert.AreEqual(expected.Disciples.Count, snapshot.Economy.Disciples.Count, "roster size");

                foreach (var fact in expected.Disciples)
                {
                    var disciple = Find(fact.DiscipleId);
                    Assert.IsNotNull(disciple, fact.DiscipleId + " must be restored");
                    Assert.AreEqual(fact.Task, disciple.CurrentTask, fact.DiscipleId + " task");
                    Assert.AreEqual(fact.ControlMode, disciple.ControlMode, fact.DiscipleId + " control mode");
                    Assert.AreEqual(fact.OwnerType, disciple.OwnerType, fact.DiscipleId + " owner type");
                    Assert.AreEqual(fact.OwnerId, disciple.OwnerId, fact.DiscipleId + " owner id");
                    Assert.AreEqual(fact.Stamina, disciple.Attributes.Stamina, fact.DiscipleId + " stamina");
                    Assert.AreEqual(fact.CultivationStageId, disciple.Attributes.CultivationStageId, fact.DiscipleId + " stage");
                    Assert.AreEqual(fact.CultivationProgress, disciple.Attributes.CultivationProgress, fact.DiscipleId + " progress");
                }

                foreach (var pair in expected.RawResources)
                    Assert.AreEqual(pair.Value, Raw(pair.Key), "raw resource " + pair.Key);
                foreach (var pair in expected.GatherAccumulators)
                    Assert.AreEqual(pair.Value, Accum(pair.Key), 1e-5f, "gather accumulator " + pair.Key);
                foreach (var pair in expected.CraftProgress)
                    Assert.AreEqual(pair.Value, CraftProgress(pair.Key), 1e-5f, "craft progress " + pair.Key);
                Assert.AreEqual(expected.HasPlacedBuildings, snapshot.Economy.PlacedBuildings.Count > 0, "buildings");
            }
        }

        /// <summary>One 1x/2x/3x run of the same simulation-time slice.</summary>
        private sealed class SliceResult
        {
            /// <summary>Produced herb only (the starter stockpile is not production).</summary>
            public float Mass;
            public float Rate;
            public float Stamina;
            public float Cultivation;
        }

        private SliceResult RunGatheringSlice(int speed, float simSeconds)
        {
            var h = NewHarness();
            Assert.IsTrue(h.Coordinator.StartNewGameAsync().GetAwaiter().GetResult().Success);
            h.RecordSessionStart();
            h.Place("herb_plot", 0, 0);
            Assert.IsTrue(h.AssignAsSectMaster("d001", "gathering_herb"));
            h.SetSpeed(speed);

            // Same SIMULATION time at every speed: real frame delta = simSeconds / speed per step.
            const int steps = 20;
            float realPerStep = simSeconds / speed / steps;
            h.StepMany(realPerStep, steps);

            var result = new SliceResult();
            result.Rate = 0.2f; // gathering_herb rate (SectStateProvider.GatheringRates)
            result.Mass = h.ProducedMass("gathering_herb", "herb");
            result.Stamina = h.Find("d001").Attributes.Stamina;
            result.Cultivation = h.Find("d001").Attributes.CultivationProgress;
            return result;
        }

        private static void WithImmediateMainThread(Action body)
        {
            MainThreadDispatch.ResetForTests();
            MainThreadDispatch.OverrideSwitchForTests(() => UniTask.CompletedTask);
            try
            {
                body();
            }
            finally
            {
                MainThreadDispatch.ResetForTests();
            }
        }

        private static void DeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                else if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException) { }
        }

        // ---------- test doubles ----------

        private sealed class FakeSceneLoader : IGameplaySceneLoader
        {
            public string Current;
            public int LoadCount;
            public int UnloadCount;

            public UniTask LoadGameplayScene(string sceneName)
            {
                LoadCount++;
                Current = sceneName;
                return UniTask.CompletedTask;
            }

            public UniTask UnloadCurrentGameplayScene()
            {
                UnloadCount++;
                Current = null;
                return UniTask.CompletedTask;
            }

            public string GetCurrentGameplayScene() => Current ?? string.Empty;
        }

        private sealed class CountingUiCloser : ISessionUiCloser
        {
            public int CloseSessionUi() => 1;
            public int SetSessionUiVisible(bool visible) => 0;
        }

        private sealed class InlineSaveWorkScheduler : ISaveWorkScheduler
        {
            public UniTask<T> RunOnWorkerAsync<T>(Func<T> work, CancellationToken cancellationToken)
                => UniTask.FromResult(work());

            public UniTask SwitchToMainThreadAsync() => UniTask.CompletedTask;
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
