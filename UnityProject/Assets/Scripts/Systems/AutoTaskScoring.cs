using System;
using System.Collections.Generic;

namespace Xianxia.Sect
{
    // =========================================================================
    // P9B — Utility AI MVP (pure scoring, no UnityEngine).
    //
    // This file holds the DECISION half of the Auto-NPC brain: plain data in,
    // a scored decision out. It never touches Unity, MessagePipe, time or the
    // state provider, so it is directly unit-testable and cannot accidentally
    // become a second authority — the Unity scheduler (AutoTaskScheduler) is
    // the only thing that gathers facts and applies the answer through the
    // P9A entry point (ISectStateProvider.TryAutoAssignTask).
    //
    // P10C scope (deliberate): Stamina and skills INPUT here and nowhere else.
    //   - Stamina drives ONE recovery rule with hysteresis (low 25 → choose
    //     meditation, stay until high 80) using the P10A tuning constants.
    //   - A skill matching the candidate's category adds a small bounded bonus.
    // Still NO Mood / Traits, no hunger/health, no productivity effect: attributes
    // never change yield, craft speed or the assignment gate, and the manual path
    // is untouched. The scheduler (AutoTaskScheduler) is the only thing that
    // gathers facts and applies the answer.
    // =========================================================================

    /// <summary>What a task actually does, read from the SAME tables the assignment
    /// gate validates against (no second task/pipeline definition).</summary>
    public enum SectTaskKind
    {
        Gathering = 0,
        Crafting = 1,
        Meditation = 2,
    }

    /// <summary>Plain description of one known task. Built by the provider from its
    /// existing gathering/crafting maps; carries nothing that is not already there.</summary>
    public sealed class SectTaskInfo
    {
        public string TaskId { get; set; } = string.Empty;
        public SectTaskKind Kind { get; set; }

        /// <summary>Gathering only — resource id this task yields.</summary>
        public string ProducesResource { get; set; } = string.Empty;
        /// <summary>Gathering only — units per second one worker yields.</summary>
        public float UnitsPerSecond { get; set; }

        /// <summary>Crafting only — item def id produced.</summary>
        public string ProducesItem { get; set; } = string.Empty;
        /// <summary>Crafting only — produced item grade.</summary>
        public int ProducesItemGrade { get; set; }
        /// <summary>Crafting only — raw-resource inputs consumed per craft.</summary>
        public IReadOnlyDictionary<string, int> InputCosts { get; set; }

        /// <summary>
        /// P10C — the skill category this task exercises, taken from the SAME table the
        /// work-attribute tick awards XP from (gathering, or the recipe's own category;
        /// null/empty for meditation, which exercises no skill). No second mapping.
        /// </summary>
        public string SkillCategory { get; set; } = string.Empty;
    }

    /// <summary>
    /// Everything the pure scorer needs about ONE candidate task, gathered by the
    /// Unity scheduler from live state at evaluation time: availability (the same
    /// gate manual assignment uses), current stock, and how many OTHER disciples
    /// already work the task. No Unity types.
    ///
    /// Rank: the assignment path has NO rank restriction today (only ownership, the
    /// known-task set, the building gate and the cooldown), so the scorer imposes none
    /// — it must not invent a rule the manual path does not enforce.
    /// </summary>
    public sealed class AutoTaskFacts
    {
        public SectTaskInfo Info { get; set; }
        public bool IsAvailable { get; set; }
        public string UnavailableReason { get; set; } = string.Empty;

        /// <summary>Disciples currently on this task, EXCLUDING the one being evaluated.</summary>
        public int OtherWorkersOnTask { get; set; }

        /// <summary>Stock of <see cref="SectTaskInfo.ProducesResource"/> (gathering).</summary>
        public int ProducedResourceStock { get; set; }
        /// <summary>Sect-stockpile stock of <see cref="SectTaskInfo.ProducesItem"/> (crafting).</summary>
        public int ProducedItemStock { get; set; }
        /// <summary>Raw resources on hand, used for crafting input readiness. May be null.</summary>
        public IReadOnlyDictionary<string, int> InputsOnHand { get; set; }

        /// <summary>
        /// P10C — the evaluated disciple's derived level in <see cref="SectTaskInfo.SkillCategory"/>
        /// (0 when the category is unknown or the disciple has no XP). Adds the bounded
        /// skill bonus; the economic shortage stays the dominant term.
        /// </summary>
        public int SkillLevel { get; set; }
    }

    /// <summary>Scored candidate plus a human-readable breakdown for the debug log.</summary>
    public sealed class AutoTaskScore
    {
        public string TaskId { get; set; } = string.Empty;
        /// <summary>What the candidate task does (meditation is found by this, not by a name).</summary>
        public SectTaskKind Kind { get; set; }
        public bool IsAvailable { get; set; }
        /// <summary>True for meditation — a task that produces nothing and only wins
        /// when nothing else is worth doing.</summary>
        public bool IsFallback { get; set; }
        public float Total { get; set; }
        /// <summary>P10C — the bounded skill bonus included in <see cref="Total"/> (0 for
        /// meditation, an unknown category, or no XP). Logged so it is never hidden.</summary>
        public float SkillBonus { get; set; }
        public string Explanation { get; set; } = string.Empty;
    }

    /// <summary>
    /// P10C — the recovery decision of one evaluation, so the log (and tests) can see
    /// WHY a disciple rested or worked. <see cref="Normal"/> means recovery did not
    /// apply and the ordinary scoring decided.
    /// </summary>
    public enum AutoRecoveryState
    {
        Normal = 0,
        /// <summary>Stamina at/below the LOW threshold — meditation chosen.</summary>
        EnteringMeditation = 1,
        /// <summary>Already meditating below the HIGH threshold — staying (no flapping).</summary>
        StayingToRecover = 2,
        /// <summary>Recovery was needed but no meditation candidate is available.</summary>
        MeditationUnavailable = 3,
    }

    /// <summary>Outcome of one decision pass: what to do, why, and every candidate's score.</summary>
    public sealed class AutoTaskDecision
    {
        public bool ShouldChange { get; set; }
        public string CurrentTask { get; set; } = string.Empty;
        /// <summary>The task to switch to, or the (kept) current task when ShouldChange is false.</summary>
        public string SelectedTask { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        /// <summary>P10C — stamina this evaluation was made with (echoed for the log).</summary>
        public float Stamina { get; set; }
        /// <summary>P10C — what the recovery rule decided (see <see cref="AutoRecoveryState"/>).</summary>
        public AutoRecoveryState Recovery { get; set; }
        public IReadOnlyList<AutoTaskScore> Candidates { get; set; } = Array.Empty<AutoTaskScore>();
    }

    /// <summary>
    /// P9B — PROTOTYPE balance values, all in this ONE place (nothing hidden in the
    /// scheduler or the provider). They are explicit placeholders, not tuned balance:
    ///   score = shortageWeight × shortage(0..1)
    ///         + productionWeight × production(0..1)      [gathering]
    ///         + inputReadinessWeight × readiness(0..1)   [crafting]
    ///         − crowdingPenaltyPerWorker × otherWorkers
    ///   meditation scores <see cref="MeditationScore"/> (0) and acts as the fallback.
    /// Shortage is measured against <see cref="AutoTaskTargets"/>.
    /// </summary>
    public static class AutoTaskWeights
    {
        /// <summary>Weight on "how short the sect is of what this task yields".</summary>
        public const float ShortageWeight = 1.00f;
        /// <summary>Weight on raw throughput (gathering).</summary>
        public const float ProductionWeight = 0.50f;
        /// <summary>Weight on "can this craft actually start" (crafting).</summary>
        public const float InputReadinessWeight = 0.50f;
        /// <summary>Diminishing returns: penalty per other disciple already on the task.</summary>
        public const float CrowdingPenaltyPerWorker = 0.35f;
        /// <summary>Normalizer for throughput (highest gathering rate in the current tables, 0.25/s).</summary>
        public const float EffectiveRateForNormalization = 0.25f;
        /// <summary>Meditation baseline — only beats tasks that score below zero.</summary>
        public const float MeditationScore = 0f;

        /// <summary>
        /// P10C — prototype skill bonus: +this per derived level on the candidate whose
        /// category matches the disciple's skill. Bounded by <see cref="SkillBonusMax"/>,
        /// which is well below <see cref="ShortageWeight"/> so an economic shortage can
        /// never be outvoted by skill (no productivity effect either — score only).
        /// </summary>
        public const float SkillBonusPerLevel = 0.02f;
        /// <summary>P10C — ceiling on the skill bonus (10 levels' worth).</summary>
        public const float SkillBonusMax = 0.20f;

        /// <summary>Human-readable summary, logged once at startup so the prototype
        /// weights are visible rather than hidden.</summary>
        public static string Describe()
        {
            return "shortage×" + ShortageWeight.ToString("0.00")
                 + ", production×" + ProductionWeight.ToString("0.00")
                 + ", inputReadiness×" + InputReadinessWeight.ToString("0.00")
                 + ", crowding−" + CrowdingPenaltyPerWorker.ToString("0.00") + "/worker"
                 + ", rateNormalizer=" + EffectiveRateForNormalization.ToString("0.00")
                 + ", meditation=" + MeditationScore.ToString("0.00") + " (fallback)"
                 + ", skillBonus=" + SkillBonusPerLevel.ToString("0.00") + "/level (max "
                 + SkillBonusMax.ToString("0.00") + ")";
        }
    }

    /// <summary>
    /// P9B — PROTOTYPE stock targets (same one-place rule as the weights). Shortage is
    /// max(0, target − have) / target, so a task stops being preferred once the sect
    /// is at or above its target. Uniform for now; the per-resource/per-item entry
    /// points exist so tuning later stays in this file.
    /// </summary>
    public static class AutoTaskTargets
    {
        public const int DefaultRawResourceTarget = 200;
        public const int DefaultCraftedItemTarget = 3;

        public static int ForRawResource(string resource) => DefaultRawResourceTarget;
        public static int ForCraftedItem(string itemDefId) => DefaultCraftedItemTarget;
    }

    /// <summary>
    /// The pure scoring + hysteresis function. Stateless and deterministic: given the
    /// same facts, order, dwell and margin it always returns the same decision.
    /// </summary>
    public static class AutoTaskScoring
    {
        /// <summary>Scores closer than this count as equal (float noise, and the
        /// "equal scores resolve deterministically" rule).</summary>
        public const float ScoreEpsilon = 0.0001f;

        /// <summary>Score one candidate. Unavailable candidates score 0 and are never
        /// selectable — <see cref="AutoTaskScore.IsAvailable"/> is the gate.</summary>
        public static AutoTaskScore Score(AutoTaskFacts facts)
        {
            var score = new AutoTaskScore();
            if (facts == null || facts.Info == null)
            {
                score.IsAvailable = false;
                score.Explanation = "no task info";
                return score;
            }

            score.TaskId = facts.Info.TaskId;
            score.Kind = facts.Info.Kind;
            score.IsAvailable = facts.IsAvailable;
            if (!facts.IsAvailable)
            {
                score.Explanation = "unavailable"
                    + (string.IsNullOrEmpty(facts.UnavailableReason) ? "" : ": " + facts.UnavailableReason);
                return score;
            }

            int otherWorkers = facts.OtherWorkersOnTask > 0 ? facts.OtherWorkersOnTask : 0;

            switch (facts.Info.Kind)
            {
                case SectTaskKind.Gathering:
                {
                    int target = AutoTaskTargets.ForRawResource(facts.Info.ProducesResource);
                    float shortage = Shortage(facts.ProducedResourceStock, target);
                    float production = Clamp01(facts.Info.UnitsPerSecond / AutoTaskWeights.EffectiveRateForNormalization);
                    score.SkillBonus = SkillBonus(facts.SkillLevel);
                    score.Total = AutoTaskWeights.ShortageWeight * shortage
                                + AutoTaskWeights.ProductionWeight * production
                                + score.SkillBonus
                                - AutoTaskWeights.CrowdingPenaltyPerWorker * otherWorkers;
                    score.Explanation = "shortage " + shortage.ToString("0.00")
                        + " + production " + production.ToString("0.00")
                        + " + skill " + score.SkillBonus.ToString("0.00")
                        + " - crowd " + otherWorkers;
                    break;
                }

                case SectTaskKind.Crafting:
                {
                    int target = AutoTaskTargets.ForCraftedItem(facts.Info.ProducesItem);
                    float shortage = Shortage(facts.ProducedItemStock, target);
                    float readiness = InputReadiness(facts);
                    score.SkillBonus = SkillBonus(facts.SkillLevel);
                    score.Total = AutoTaskWeights.ShortageWeight * shortage
                                + AutoTaskWeights.InputReadinessWeight * readiness
                                + score.SkillBonus
                                - AutoTaskWeights.CrowdingPenaltyPerWorker * otherWorkers;
                    score.Explanation = "itemShortage " + shortage.ToString("0.00")
                        + " + inputReadiness " + readiness.ToString("0.00")
                        + " + skill " + score.SkillBonus.ToString("0.00")
                        + " - crowd " + otherWorkers;
                    break;
                }

                default: // Meditation — produces nothing; a safe fallback, never crowded and
                     // never skill-bonused (it exercises no category).
                    score.IsFallback = true;
                    score.SkillBonus = 0f;
                    score.Total = AutoTaskWeights.MeditationScore;
                    score.Explanation = "fallback (produces nothing)";
                    break;
            }

            return score;
        }

        /// <summary>
        /// Choose a task. <paramref name="facts"/> order is the stable candidate order
        /// (known-task order) and the deterministic tie-breaker.
        ///
        /// Hysteresis: a switch must beat the current task by at least
        /// <paramref name="improvementMargin"/> AND the current task must have been held
        /// for at least <paramref name="minimumDwellSeconds"/>. When the current task is
        /// unavailable, both are skipped (re-evaluate immediately) — the caller still
        /// re-validates ownership/mode/cooldown before committing.
        ///
        /// P10C: <paramref name="stamina"/> drives the recovery rule first (see
        /// <see cref="AutoRecoveryState"/>). It is one plain number — this stays a pure
        /// function with no Unity, no time and no state access, so it is unit-testable,
        /// and it never mutates anything (the scheduler applies the answer).
        /// </summary>
        public static AutoTaskDecision Decide(
            string currentTask,
            IReadOnlyList<AutoTaskFacts> facts,
            float dwellSeconds,
            float minimumDwellSeconds,
            float improvementMargin,
            float stamina = DiscipleAttributesConfig.StaminaMax)
        {
            var decision = new AutoTaskDecision
            {
                CurrentTask = currentTask ?? string.Empty,
                SelectedTask = currentTask ?? string.Empty,
                Stamina = stamina,
            };

            var scores = new List<AutoTaskScore>(facts != null ? facts.Count : 0);
            if (facts != null)
            {
                for (int i = 0; i < facts.Count; i++) scores.Add(Score(facts[i]));
            }
            decision.Candidates = scores;

            AutoTaskScore current = null;
            var available = new List<AutoTaskScore>(scores.Count);
            for (int i = 0; i < scores.Count; i++)
            {
                if (scores[i].TaskId == decision.CurrentTask) current = scores[i];
                if (scores[i].IsAvailable) available.Add(scores[i]);
            }

            if (available.Count == 0)
            {
                // No meditation candidate exists/available either, so recovery cannot run.
                decision.Reason = "no candidate task is available";
                return decision;
            }

            // ---- P10C: recovery (stamina) with hysteresis, BEFORE dwell/margin ----
            // The disciple's own CurrentTask IS the memory — no "recovering" flag is stored.
            //   stamina ≤ LOW (25)      → choose meditation, if available
            //   on meditation < HIGH    → stay (this is what stops 25↔80 flapping)
            // Recovery overrides the dwell/improvement-margin guards on purpose: an
            // exhausted disciple must be allowed to rest even inside the dwell window.
            // The ownership/Auto gate, building gate and the provider's 12s cooldown
            // remain the only entry guards — a cooldown rejection waits for the next
            // scheduled evaluation (no retry loop here).
            AutoTaskScore meditation = null;
            for (int i = 0; i < scores.Count; i++)
            {
                if (scores[i].Kind == SectTaskKind.Meditation)
                {
                    meditation = scores[i];
                    break;
                }
            }

            bool meditationAvailable = meditation != null && meditation.IsAvailable;
            bool currentIsMeditation = meditation != null && !string.IsNullOrEmpty(meditation.TaskId)
                                       && decision.CurrentTask == meditation.TaskId;
            bool staminaAtOrBelowLow = stamina <= DiscipleAttributesConfig.RecoveryThresholdLow;

            if (currentIsMeditation && meditationAvailable
                && stamina < DiscipleAttributesConfig.RecoveryThresholdHigh)
            {
                decision.Recovery = AutoRecoveryState.StayingToRecover;
                decision.Reason = "recovering: stamina " + stamina.ToString("0.0")
                    + " below high " + DiscipleAttributesConfig.RecoveryThresholdHigh.ToString("0") 
                    + " — staying on '" + decision.CurrentTask + "'";
                return decision;
            }

            if (staminaAtOrBelowLow && meditationAvailable)
            {
                decision.Recovery = AutoRecoveryState.EnteringMeditation;
                decision.SelectedTask = meditation.TaskId;
                decision.ShouldChange = meditation.TaskId != decision.CurrentTask;
                decision.Reason = "recovering: stamina " + stamina.ToString("0.0")
                    + " at/below low " + DiscipleAttributesConfig.RecoveryThresholdLow.ToString("0")
                    + " — choosing '" + meditation.TaskId + "'";
                return decision;
            }

            if (staminaAtOrBelowLow) decision.Recovery = AutoRecoveryState.MeditationUnavailable;

            bool currentAvailable = current != null && current.IsAvailable;

            if (!currentAvailable)
            {
                var bestUnavailable = PickBest(available, null);
                if (bestUnavailable == null)
                {
                    decision.Reason = "no candidate task is available";
                    return decision;
                }

                decision.SelectedTask = bestUnavailable.TaskId;
                decision.ShouldChange = bestUnavailable.TaskId != decision.CurrentTask;
                decision.Reason = string.IsNullOrEmpty(decision.CurrentTask)
                    ? "no current task — taking best available '" + bestUnavailable.TaskId + "'"
                    : "current task '" + decision.CurrentTask + "' is unavailable — re-evaluating without hysteresis";
                return decision;
            }

            if (dwellSeconds < minimumDwellSeconds)
            {
                decision.Reason = "within minimum dwell (" + dwellSeconds.ToString("0.0")
                    + "s < " + minimumDwellSeconds.ToString("0.0") + "s) — staying";
                return decision;
            }

            var best = PickBest(available, decision.CurrentTask);
            if (best == null || best.TaskId == decision.CurrentTask)
            {
                decision.Reason = "current task '" + decision.CurrentTask + "' (" + current.Total.ToString("0.00")
                    + ") remains the best available choice";
                return decision;
            }

            if (best.Total >= current.Total + improvementMargin)
            {
                decision.ShouldChange = true;
                decision.SelectedTask = best.TaskId;
                decision.Reason = "'" + best.TaskId + "' (" + best.Total.ToString("0.00")
                    + ") beats current '" + decision.CurrentTask + "' (" + current.Total.ToString("0.00")
                    + ") by ≥ margin " + improvementMargin.ToString("0.00");
            }
            else
            {
                decision.Reason = "improvement below margin ('" + best.TaskId + "' " + best.Total.ToString("0.00")
                    + " vs current " + current.Total.ToString("0.00") + " + " + improvementMargin.ToString("0.00") + ")";
            }

            return decision;
        }

        /// <summary>Highest total wins; ties prefer the current task (stability), then the
        /// earlier candidate in the stable known-task order (determinism).</summary>
        private static AutoTaskScore PickBest(List<AutoTaskScore> available, string preferTaskId)
        {
            AutoTaskScore best = null;
            bool preferSet = !string.IsNullOrEmpty(preferTaskId);

            for (int i = 0; i < available.Count; i++)
            {
                var candidate = available[i];
                if (best == null)
                {
                    best = candidate;
                    continue;
                }

                if (candidate.Total > best.Total + ScoreEpsilon)
                {
                    best = candidate;
                    continue;
                }

                if (Math.Abs(candidate.Total - best.Total) <= ScoreEpsilon && preferSet)
                {
                    bool candidatePreferred = candidate.TaskId == preferTaskId;
                    bool bestPreferred = best.TaskId == preferTaskId;
                    if (candidatePreferred && !bestPreferred) best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// P10C — bounded skill bonus: +SkillBonusPerLevel per derived level, capped at
        /// SkillBonusMax. Deliberately smaller than a one-point swing in shortage, so a
        /// skill can nudge a tie or a near-tie and never overrule the economy.
        /// </summary>
        private static float SkillBonus(int skillLevel)
        {
            if (skillLevel <= 0) return 0f;
            float bonus = skillLevel * AutoTaskWeights.SkillBonusPerLevel;
            return bonus > AutoTaskWeights.SkillBonusMax ? AutoTaskWeights.SkillBonusMax : bonus;
        }

        /// <summary>0..1 — how far below target the stock is.</summary>
        private static float Shortage(int have, int target)
        {
            if (target <= 0) return 0f;
            if (have >= target) return 0f;
            return (target - have) / (float)target;
        }

        /// <summary>0..1 — worst input coverage; 1 when a craft needs nothing.</summary>
        private static float InputReadiness(AutoTaskFacts facts)
        {
            var costs = facts.Info != null ? facts.Info.InputCosts : null;
            if (costs == null || costs.Count == 0) return 1f;

            float worst = 1f;
            foreach (var cost in costs)
            {
                if (cost.Value <= 0) continue;
                int have = 0;
                if (facts.InputsOnHand != null) facts.InputsOnHand.TryGetValue(cost.Key, out have);
                float ratio = Clamp01(have / (float)cost.Value);
                if (ratio < worst) worst = ratio;
            }
            return worst;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
