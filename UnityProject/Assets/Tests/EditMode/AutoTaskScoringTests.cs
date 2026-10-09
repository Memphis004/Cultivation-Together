using System.Collections.Generic;
using NUnit.Framework;
using Xianxia.Sect;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// P9B — the PURE utility scorer (no Unity, no state provider, no time). Covers the
    /// decision half of the requirement-8 list: unavailable tasks are never selectable,
    /// shortage drives preference, ties are deterministic, and hysteresis (margin +
    /// dwell) stops oscillation — while an unavailable current task re-evaluates free
    /// of hysteresis.
    /// </summary>
    public class AutoTaskScoringTests
    {
        // ---- builders ----

        private static AutoTaskFacts Gathering(string taskId, string resource, float rate, int stock,
                                               int otherWorkers = 0, bool available = true,
                                               string unavailableReason = "")
            => new AutoTaskFacts
            {
                Info = new SectTaskInfo
                {
                    TaskId = taskId,
                    Kind = SectTaskKind.Gathering,
                    ProducesResource = resource,
                    UnitsPerSecond = rate,
                },
                IsAvailable = available,
                UnavailableReason = unavailableReason,
                OtherWorkersOnTask = otherWorkers,
                ProducedResourceStock = stock,
            };

        private static AutoTaskFacts Crafting(string taskId, string item, int itemStock,
                                              Dictionary<string, int> costs, Dictionary<string, int> onHand,
                                              int otherWorkers = 0, bool available = true)
            => new AutoTaskFacts
            {
                Info = new SectTaskInfo
                {
                    TaskId = taskId,
                    Kind = SectTaskKind.Crafting,
                    ProducesItem = item,
                    InputCosts = costs,
                },
                IsAvailable = available,
                OtherWorkersOnTask = otherWorkers,
                ProducedItemStock = itemStock,
                InputsOnHand = onHand,
            };

        private static AutoTaskFacts Meditation(bool available = true)
            => new AutoTaskFacts
            {
                Info = new SectTaskInfo { TaskId = "meditation", Kind = SectTaskKind.Meditation },
                IsAvailable = available,
            };

        private static AutoTaskScore CandidateFor(AutoTaskDecision decision, string taskId)
        {
            foreach (var c in decision.Candidates)
                if (c.TaskId == taskId) return c;
            return null;
        }

        private static AutoTaskDecision Decide(string current, params AutoTaskFacts[] facts)
            => AutoTaskScoring.Decide(current, facts, float.PositiveInfinity, 0f, 0f);

        // ---- availability gate ----

        [Test]
        public void UnavailableCandidate_IsNeverSelected_EvenIfItWouldScoreHighest()
        {
            // ore is the shortest resource, but it is unavailable (no forge/plot gate)
            var decision = Decide(null,
                Gathering("gathering_ore", "ore", 0.15f, 0, available: false, unavailableReason: "needs a building"),
                Gathering("gathering_wood", "wood", 0.2f, 200));

            Assert.AreEqual("gathering_wood", decision.SelectedTask);
            Assert.IsTrue(decision.ShouldChange);

            var ore = CandidateFor(decision, "gathering_ore");
            Assert.IsFalse(ore.IsAvailable);
            Assert.AreEqual(0f, ore.Total, "unavailable candidates must not carry a selectable score");
        }

        [Test]
        public void NoAvailableCandidate_ChangesNothing()
        {
            var decision = Decide("meditation", Meditation(available: false));

            Assert.IsFalse(decision.ShouldChange);
            Assert.AreEqual("meditation", decision.SelectedTask);
            StringAssert.Contains("no candidate task is available", decision.Reason);
        }

        // ---- resource shortage drives preference ----

        [Test]
        public void ResourceShortage_ChangesPreference()
        {
            // herb and wood have the SAME rate, so stock is the only differentiator
            var herbShort = Decide(null,
                Gathering("gathering_herb", "herb", 0.2f, 0),
                Gathering("gathering_wood", "wood", 0.2f, 200));
            Assert.AreEqual("gathering_herb", herbShort.SelectedTask, "the short resource wins");

            var woodShort = Decide(null,
                Gathering("gathering_herb", "herb", 0.2f, 200),
                Gathering("gathering_wood", "wood", 0.2f, 0));
            Assert.AreEqual("gathering_wood", woodShort.SelectedTask, "preference follows the shortage");
        }

        [Test]
        public void Crowding_ReducesScore()
        {
            float alone = AutoTaskScoring.Score(Gathering("gathering_wood", "wood", 0.2f, 0)).Total;
            float crowded = AutoTaskScoring.Score(Gathering("gathering_wood", "wood", 0.2f, 0, otherWorkers: 2)).Total;

            Assert.Less(crowded, alone, "other workers already on a task reduce its value");
            Assert.AreEqual(alone - 0.7f, crowded, 0.001f);
        }

        // ---- determinism ----

        [Test]
        public void EqualScores_ResolveToEarlierCandidate_Deterministically()
        {
            var a = Gathering("gathering_herb", "herb", 0.2f, 200);
            var b = Gathering("gathering_wood", "wood", 0.2f, 200);

            var forward = AutoTaskScoring.Decide(null, new[] { a, b }, float.PositiveInfinity, 0f, 0f);
            var forwardAgain = AutoTaskScoring.Decide(null, new[] { a, b }, float.PositiveInfinity, 0f, 0f);
            var reversed = AutoTaskScoring.Decide(null, new[] { b, a }, float.PositiveInfinity, 0f, 0f);

            Assert.AreEqual(forward.SelectedTask, forwardAgain.SelectedTask, "same input → same answer");
            Assert.AreEqual("gathering_herb", forward.SelectedTask, "tie → earlier candidate");
            Assert.AreEqual("gathering_wood", reversed.SelectedTask, "tie → earlier candidate");
        }

        [Test]
        public void EqualScores_PreferCurrentTask_OnTie()
        {
            var a = Gathering("gathering_herb", "herb", 0.2f, 200);
            var b = Gathering("gathering_wood", "wood", 0.2f, 200);

            var decision = AutoTaskScoring.Decide("gathering_wood", new[] { a, b }, float.PositiveInfinity, 0f, 0f);

            Assert.IsFalse(decision.ShouldChange, "a tie must not move the disciple");
            Assert.AreEqual("gathering_wood", decision.SelectedTask);
        }

        // ---- hysteresis: improvement margin ----

        [Test]
        public void SmallImprovement_BelowMargin_DoesNotSwitch_ButSwitchesAtOrAboveIt()
        {
            // current herb (full) = 0.40; wood at 130/200 shortage = 0.35 → wood = 0.75
            var current = Gathering("gathering_herb", "herb", 0.2f, 200);
            var alternative = Gathering("gathering_wood", "wood", 0.2f, 130);
            var facts = new[] { current, alternative };

            var blocked = AutoTaskScoring.Decide("gathering_herb", facts, float.PositiveInfinity, 0f, 0.50f);
            Assert.IsFalse(blocked.ShouldChange, "improvement below the margin must not cause a switch");
            Assert.AreEqual("gathering_herb", blocked.SelectedTask);
            StringAssert.Contains("below margin", blocked.Reason);

            var allowed = AutoTaskScoring.Decide("gathering_herb", facts, float.PositiveInfinity, 0f, 0.20f);
            Assert.IsTrue(allowed.ShouldChange, "improvement above the margin switches");
            Assert.AreEqual("gathering_wood", allowed.SelectedTask);
        }

        // ---- hysteresis: minimum dwell ----

        [Test]
        public void MinimumDwell_BlocksSwitch_EvenWhenMuchBetter()
        {
            var decision = AutoTaskScoring.Decide("gathering_wood",
                new[] { Gathering("gathering_wood", "wood", 0.2f, 200), Gathering("gathering_herb", "herb", 0.2f, 0) },
                dwellSeconds: 1f, minimumDwellSeconds: 10f, improvementMargin: 0f);

            Assert.IsFalse(decision.ShouldChange);
            Assert.AreEqual("gathering_wood", decision.SelectedTask);
            StringAssert.Contains("minimum dwell", decision.Reason);
        }

        [Test]
        public void UnavailableCurrentTask_ReevaluatesWithoutHysteresis()
        {
            // huge dwell gate + huge margin would block any normal switch — but the
            // current task is unavailable, so both are skipped.
            var decision = AutoTaskScoring.Decide("refining_elixir",
                new[]
                {
                    Crafting("refining_elixir", "elixir_qi_gathering", 0,
                        new Dictionary<string, int> { ["herb"] = 10 }, new Dictionary<string, int>(),
                        available: false),
                    Gathering("gathering_herb", "herb", 0.2f, 200),
                },
                dwellSeconds: 0f, minimumDwellSeconds: 1000f, improvementMargin: 100f);

            Assert.IsTrue(decision.ShouldChange);
            Assert.AreEqual("gathering_herb", decision.SelectedTask);
            StringAssert.Contains("unavailable", decision.Reason);
        }

        // ---- meditation is a fallback ----

        [Test]
        public void Meditation_IsFallback_OnlyWhenNothingElseScoresAboveIt()
        {
            // a full-stock task crowded past its production value scores negative →
            // meditation (0) wins: 0.5*0.8 - 0.35*3 = -0.65
            var fallback = Decide(null,
                Gathering("gathering_wood", "wood", 0.2f, 200, otherWorkers: 3),
                Meditation());
            Assert.AreEqual("meditation", fallback.SelectedTask);
            Assert.IsTrue(CandidateFor(fallback, "meditation").IsFallback);

            // an uncrowded shortage beats meditation
            var productive = Decide(null,
                Gathering("gathering_wood", "wood", 0.2f, 0),
                Meditation());
            Assert.AreEqual("gathering_wood", productive.SelectedTask);
        }

        // ---- crafting uses item shortage + input readiness ----

        [Test]
        public void Crafting_ScoresByItemShortageAndInputReadiness()
        {
            var costs = new Dictionary<string, int> { ["herb"] = 10 };
            var blocked = Crafting("refining_elixir", "elixir_qi_gathering", 0, costs, new Dictionary<string, int>());
            var ready = Crafting("refining_elixir", "elixir_qi_gathering", 0, costs,
                new Dictionary<string, int> { ["herb"] = 10 });

            float blockedScore = AutoTaskScoring.Score(blocked).Total;
            float readyScore = AutoTaskScoring.Score(ready).Total;

            Assert.Less(blockedScore, readyScore, "missing inputs must lower the score");
            Assert.AreEqual(readyScore - AutoTaskWeights.InputReadinessWeight, blockedScore, 0.001f);
        }

        // ---- the weights are reported, not hidden ----

        [Test]
        public void PrototypeWeights_AreReported()
        {
            var described = AutoTaskWeights.Describe();

            StringAssert.Contains("shortage", described);
            StringAssert.Contains("production", described);
            StringAssert.Contains("crowding", described);
            StringAssert.Contains("meditation", described);
        }
    }
}
