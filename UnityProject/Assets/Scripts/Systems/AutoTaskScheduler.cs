using System.Collections.Generic;
using System.Text;
using MessagePipe;
using UnityEngine;
using VContainer.Unity;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    /// <summary>Result of one evaluation pass, kept for tests / debug tooling.</summary>
    public sealed class AutoEvaluationOutcome
    {
        /// <summary>Null when the disciple was skipped before scoring (ineligible).</summary>
        public AutoTaskDecision Decision { get; set; }
        /// <summary>True when the provider accepted the change (or no change was needed).</summary>
        public bool Applied { get; set; }
        /// <summary>Provider's reason when a change was requested but rejected.</summary>
        public string ApplyFailReason { get; set; } = string.Empty;
        /// <summary>Why the disciple was not eligible for evaluation at all.</summary>
        public string SkipReason { get; set; } = string.Empty;
        /// <summary>P10C — the disciple's stamina when this pass ran (echoed for the log/tests).</summary>
        public float Stamina { get; set; }

        public bool Skipped => Decision == null;
    }

    /// <summary>
    /// P9B — the Unity half of the Auto-NPC brain. Ticks on a configurable
    /// SIMULATION-time interval (never per frame), pauses with gameplay, and staggers
    /// evaluations so a burst of Auto disciples does not all decide from the same
    /// worker-distribution snapshot. All scoring lives in the pure
    /// <see cref="AutoTaskScoring"/>; this class only gathers facts from live state and
    /// applies the answer through the P9A entry point
    /// (<see cref="ISectStateProvider.TryAutoAssignTask"/>), which re-checks ownership,
    /// Auto mode, availability and cooldown at commit time.
    ///
    /// P10C scope: stamina and skills now INFLUENCE the choice of an Auto NPC (a
    /// recovery rule with hysteresis, plus a bounded per-category skill bonus) — both
    /// decided in the pure scorer above. Deliberately still absent: Mood, traits,
    /// hunger/health, GOAP/BehaviorTree/personas, and any productivity effect —
    /// attributes never change yield, craft speed or the assignment gate, and a
    /// Manual/Player/Viewer disciple is never evaluated here at all.
    /// </summary>
    public class AutoTaskScheduler : IStartable, ITickable
    {
        /// <summary>Number of phase buckets used to stagger first evaluations.</summary>
        public const int StaggerSlots = 8;

        private readonly ISectStateProvider _stateProvider;
        private readonly TimeSystem _timeSystem;
        private readonly ISubscriber<SessionRestoredMessage> _sessionRestoredSubscriber;

        /// <summary>Simulation seconds between a disciple's evaluations (not frames).</summary>
        public float EvaluationIntervalSeconds { get; set; } = 10f;
        /// <summary>Simulation seconds a task must be held before the brain may change it.</summary>
        public float MinimumDwellSeconds { get; set; } = 20f;
        /// <summary>Minimum score improvement required to switch away from a held task.</summary>
        public float ImprovementMargin { get; set; } = 0.10f;

        private float _simTime;
        private int _registeredCount;

        private readonly Dictionary<string, float> _nextEvalAt = new Dictionary<string, float>();
        private readonly Dictionary<string, string> _observedTask = new Dictionary<string, string>();
        private readonly Dictionary<string, float> _lastChangeAt = new Dictionary<string, float>();
        private readonly HashSet<string> _evaluatedOnce = new HashSet<string>();

        public AutoTaskScheduler(ISectStateProvider stateProvider, TimeSystem timeSystem,
                                 ISubscriber<SessionRestoredMessage> sessionRestoredSubscriber = null)
        {
            _stateProvider = stateProvider;
            _timeSystem = timeSystem;
            _sessionRestoredSubscriber = sessionRestoredSubscriber;
        }

        /// <summary>Log the prototype balance once, so the values are visible rather than hidden.</summary>
        public void Start()
        {
            // P11A — a restored session invalidates every derived scheduling reference
            // (old-session disciple ids, dwell clocks, staggered eval slots).
            _sessionRestoredSubscriber?.Subscribe(OnSessionRestored);

            Debug.Log("[AutoTaskScheduler] P9B/P10C utility AI weights: " + AutoTaskWeights.Describe()
                + " | recovery: stamina ≤ " + DiscipleAttributesConfig.RecoveryThresholdLow.ToString("0")
                + " → meditation, stay until " + DiscipleAttributesConfig.RecoveryThresholdHigh.ToString("0")
                + " | targets raw " + AutoTaskTargets.DefaultRawResourceTarget
                + "/crafted " + AutoTaskTargets.DefaultCraftedItemTarget
                + " | interval " + EvaluationIntervalSeconds.ToString("0.#") + "s"
                + ", minDwell " + MinimumDwellSeconds.ToString("0.#") + "s"
                + ", margin " + ImprovementMargin.ToString("0.00"));
        }

        /// <summary>
        /// P11A — the AI's per-disciple scheduling/dwell maps describe the PREVIOUS
        /// session; a restored roster may reuse ids with different tasks or drop them
        /// entirely. Clear them so the next pass re-registers from scratch and a stale
        /// dwell clock can never gate (or rush) a freshly restored disciple's choice.
        /// This is presentation cadence only — no assignment or reward lives here.
        /// </summary>
        public void ResetDerivedSchedule()
        {
            _simTime = 0f;
            _registeredCount = 0;
            _nextEvalAt.Clear();
            _observedTask.Clear();
            _lastChangeAt.Clear();
            _evaluatedOnce.Clear();
        }

        private void OnSessionRestored(SessionRestoredMessage message) => ResetDerivedSchedule();

        /// <summary>Frame tick — advances simulation time by the shared simulation delta
        /// (pause = 0, speed applied once in TimeSystem) and evaluates whatever is due.</summary>
        public void Tick()
        {
            if (_timeSystem == null) return; // no time source — nothing to advance
            Advance(_timeSystem.SimulationDelta);
        }

        /// <summary>
        /// Advance the scheduler by a simulation delta and evaluate every Auto NPC whose
        /// interval has elapsed. Split from <see cref="Tick"/> so tests (and embedding
        /// callers) drive it deterministically. While gameplay is paused nothing is
        /// evaluated AND simulation time does not advance, so the interval is not
        /// consumed by the pause.
        /// </summary>
        public void Advance(float deltaTimeSeconds)
        {
            if (_timeSystem != null && _timeSystem.IsPaused) return;
            if (deltaTimeSeconds > 0f) _simTime += deltaTimeSeconds;

            var state = _stateProvider.BuildSectEconomyState();
            var disciples = state != null ? state.Disciples : null;
            if (disciples == null) return;

            for (int i = 0; i < disciples.Count; i++)
            {
                var disciple = disciples[i];
                if (disciple == null) continue;
                if (disciple.OwnerType != DiscipleOwnerType.Npc) continue;
                if (disciple.ControlMode != DiscipleControlMode.Auto) continue;

                RegisterIfNeeded(disciple.DiscipleId);
                if (_simTime < _nextEvalAt[disciple.DiscipleId]) continue;

                EvaluateDisciple(disciple.DiscipleId);
            }
        }

        /// <summary>
        /// Evaluate ONE disciple immediately, bypassing the schedule (used by the due
        /// loop and by tests). Returns a no-op outcome with a SkipReason when the
        /// disciple is unknown, not NPC-owned, or not in Auto — the scheduler never
        /// touches a disciple the player still controls.
        /// </summary>
        public AutoEvaluationOutcome EvaluateDisciple(string discipleId)
        {
            var outcome = new AutoEvaluationOutcome();
            if (string.IsNullOrEmpty(discipleId))
            {
                outcome.SkipReason = "empty disciple id";
                return outcome;
            }

            var state = _stateProvider.BuildSectEconomyState();
            var disciple = FindDisciple(state, discipleId);
            if (disciple == null)
            {
                outcome.SkipReason = "unknown disciple";
                return outcome;
            }
            if (disciple.OwnerType != DiscipleOwnerType.Npc)
            {
                outcome.SkipReason = "not NPC-owned (player/viewer controlled)";
                return outcome;
            }
            if (disciple.ControlMode != DiscipleControlMode.Auto)
            {
                outcome.SkipReason = "not in Auto mode";
                return outcome;
            }

            string current = disciple.CurrentTask ?? string.Empty;
            float stamina = StaminaOf(disciple);
            outcome.Stamina = stamina;

            // Dwell bookkeeping. A change we did not make (e.g. a manual assignment)
            // restarts the dwell clock; the first evaluation is never dwell-gated so a
            // freshly Auto disciple acts immediately.
            bool evaluatedBefore = _evaluatedOnce.Contains(discipleId);
            if (_observedTask.TryGetValue(discipleId, out var observedTask) && observedTask != current)
            {
                _observedTask[discipleId] = current;
                _lastChangeAt[discipleId] = _simTime;
            }
            float dwell;
            if (!evaluatedBefore) dwell = float.PositiveInfinity;
            else dwell = _lastChangeAt.TryGetValue(discipleId, out var lastChange) ? _simTime - lastChange : float.PositiveInfinity;

            var workers = CountWorkers(state);
            var facts = BuildFacts(state, disciple, workers);
            var decision = AutoTaskScoring.Decide(current, facts, dwell, MinimumDwellSeconds,
                                                  ImprovementMargin, stamina);
            outcome.Decision = decision;

            if (decision.ShouldChange && !string.IsNullOrEmpty(decision.SelectedTask))
            {
                // The provider re-checks NPC ownership + Auto mode + availability +
                // cooldown at commit time. A rejection is NOT retried here — the next
                // scheduled evaluation is the only retry.
                bool ok = _stateProvider.TryAutoAssignTask(discipleId, decision.SelectedTask, out var failReason);
                outcome.Applied = ok;
                outcome.ApplyFailReason = failReason;
                if (ok)
                {
                    _observedTask[discipleId] = decision.SelectedTask;
                    _lastChangeAt[discipleId] = _simTime;
                }
            }
            else
            {
                outcome.Applied = true; // nothing to change = success (unchanged, no event)
            }

            if (!evaluatedBefore) _lastChangeAt[discipleId] = _simTime;
            if (!_observedTask.ContainsKey(discipleId)) _observedTask[discipleId] = current;
            _evaluatedOnce.Add(discipleId);

            _nextEvalAt[discipleId] = _simTime + EvaluationIntervalSeconds;
            LogEvaluation(discipleId, outcome);
            return outcome;
        }

        private void RegisterIfNeeded(string discipleId)
        {
            if (_nextEvalAt.ContainsKey(discipleId)) return;

            // Stagger: spread first evaluations across the interval so a burst of Auto
            // disciples does not all decide in the same frame from the same snapshot.
            int slot = StaggerSlots > 0 ? _registeredCount++ % StaggerSlots : 0;
            float phase = StaggerSlots > 0 ? EvaluationIntervalSeconds * (slot / (float)StaggerSlots) : 0f;
            _nextEvalAt[discipleId] = _simTime + phase;
        }

        private List<AutoTaskFacts> BuildFacts(SectEconomyState state, DiscipleState disciple, Dictionary<string, int> workers)
        {
            var known = _stateProvider.GetKnownTaskIds();
            var facts = new List<AutoTaskFacts>(known != null ? known.Count : 0);
            if (known == null) return facts;

            string currentTask = disciple.CurrentTask ?? string.Empty;

            for (int i = 0; i < known.Count; i++)
            {
                string taskId = known[i];
                bool available = _stateProvider.IsTaskAvailable(taskId, out var reason);

                if (!_stateProvider.TryGetTaskInfo(taskId, out var info) || info == null) continue;

                workers.TryGetValue(taskId, out var onTask);
                int others = onTask - (currentTask == taskId ? 1 : 0);
                if (others < 0) others = 0;

                var fact = new AutoTaskFacts
                {
                    Info = info,
                    IsAvailable = available,
                    UnavailableReason = reason,
                    OtherWorkersOnTask = others,
                };

                // P10C — the disciple's own level in THIS candidate's category (0 when the
                // category is unknown or there is no XP), so the skill bonus is per candidate.
                fact.SkillLevel = SkillLevelFor(disciple, info.SkillCategory);

                if (info.Kind == SectTaskKind.Gathering)
                {
                    fact.ProducedResourceStock = RawStock(state, info.ProducesResource);
                }
                else if (info.Kind == SectTaskKind.Crafting)
                {
                    fact.ProducedItemStock = CraftedStock(state, info.ProducesItem);
                    fact.InputsOnHand = state != null && state.Stockpile != null ? state.Stockpile.RawResources : null;
                }

                facts.Add(fact);
            }

            return facts;
        }

        private static DiscipleState FindDisciple(SectEconomyState state, string discipleId)
        {
            var disciples = state != null ? state.Disciples : null;
            if (disciples == null) return null;
            for (int i = 0; i < disciples.Count; i++)
            {
                if (disciples[i] != null && disciples[i].DiscipleId == discipleId) return disciples[i];
            }
            return null;
        }

        private static Dictionary<string, int> CountWorkers(SectEconomyState state)
        {
            var map = new Dictionary<string, int>();
            var disciples = state != null ? state.Disciples : null;
            if (disciples == null) return map;

            for (int i = 0; i < disciples.Count; i++)
            {
                var d = disciples[i];
                if (d == null || string.IsNullOrEmpty(d.CurrentTask)) continue;
                map.TryGetValue(d.CurrentTask, out var count);
                map[d.CurrentTask] = count + 1;
            }
            return map;
        }

        /// <summary>
        /// P10C — live stamina, or the healthy default when attributes are missing/non-finite.
        /// Read-only: the scheduler never writes attributes (the P10B work tick owns that).
        /// </summary>
        private static float StaminaOf(DiscipleState disciple)
        {
            var attributes = disciple != null ? disciple.Attributes : null;
            if (attributes == null) return DiscipleAttributesConfig.StaminaDefault;
            float stamina = attributes.Stamina;
            if (float.IsNaN(stamina) || float.IsInfinity(stamina)) return DiscipleAttributesConfig.StaminaDefault;
            return stamina;
        }

        /// <summary>P10C — derived level in one skill category (null-safe; unknown category → 0).</summary>
        private static int SkillLevelFor(DiscipleState disciple, string category)
        {
            if (disciple == null || string.IsNullOrEmpty(category)) return 0;
            return DiscipleAttributes.SkillLevel(
                DiscipleAttributes.GetSkillXp(disciple.Attributes, category));
        }

        private static int RawStock(SectEconomyState state, string resource)
        {
            if (state == null || state.Stockpile == null || state.Stockpile.RawResources == null) return 0;
            if (string.IsNullOrEmpty(resource)) return 0;
            return state.Stockpile.RawResources.TryGetValue(resource, out var value) ? value : 0;
        }

        private static int CraftedStock(SectEconomyState state, string itemDefId)
        {
            if (state == null || state.Stockpile == null || state.Stockpile.CraftedGoods == null) return 0;
            if (string.IsNullOrEmpty(itemDefId)) return 0;

            int total = 0;
            var goods = state.Stockpile.CraftedGoods;
            for (int i = 0; i < goods.Count; i++)
            {
                var g = goods[i];
                if (g != null && g.ItemDefId == itemDefId && g.OwnerScope == OwnerScope.SectStockpile)
                    total += g.Quantity;
            }
            return total;
        }

        /// <summary>
        /// P10C — one concise line per evaluation (never per frame, never a message): the
        /// choice, applied/rejected, the evaluated STAMINA, the recovery decision, the
        /// reason, and every candidate's score WITH its skill bonus. Pure string building
        /// so the format is testable without capturing Unity logs;
        /// <see cref="LogEvaluation"/> prints it on the EXISTING log channel (no second bus).
        /// </summary>
        public static string FormatEvaluation(string discipleId, AutoEvaluationOutcome outcome)
        {
            var decision = outcome != null ? outcome.Decision : null;
            if (decision == null) return string.Empty;

            var sb = new StringBuilder(200);
            sb.Append("[AutoTaskScheduler] ").Append(discipleId).Append(" → ")
              .Append(string.IsNullOrEmpty(decision.SelectedTask) ? "(none)" : decision.SelectedTask);

            if (outcome.Applied && decision.ShouldChange) sb.Append(" [applied]");
            else if (decision.ShouldChange) sb.Append(" [rejected: ").Append(outcome.ApplyFailReason).Append(']');
            else sb.Append(" [kept]");

            sb.Append(" | stamina ").Append(outcome.Stamina.ToString("0.0"))
              .Append('/').Append(DiscipleAttributesConfig.StaminaMax.ToString("0"))
              .Append(" | recovery: ").Append(RecoveryLabel(decision.Recovery));

            sb.Append(" | ").Append(decision.Reason);
            sb.Append(" | candidates: ");
            for (int i = 0; i < decision.Candidates.Count; i++)
            {
                var c = decision.Candidates[i];
                if (i > 0) sb.Append(", ");
                sb.Append(c.TaskId);
                if (!c.IsAvailable)
                {
                    sb.Append("(n/a)");
                    continue;
                }
                sb.Append(' ').Append(c.Total.ToString("0.00"));
                sb.Append("(skill+").Append(c.SkillBonus.ToString("0.00")).Append(')');
                if (c.IsFallback) sb.Append("(fallback)");
            }

            return sb.ToString();
        }

        /// <summary>P10C — the recovery decision in words (log/tests).</summary>
        public static string RecoveryLabel(AutoRecoveryState recovery)
        {
            switch (recovery)
            {
                case AutoRecoveryState.EnteringMeditation: return "enter meditation";
                case AutoRecoveryState.StayingToRecover: return "stay to recover";
                case AutoRecoveryState.MeditationUnavailable: return "meditation unavailable";
                default: return "normal";
            }
        }

        private static void LogEvaluation(string discipleId, AutoEvaluationOutcome outcome)
        {
            var line = FormatEvaluation(discipleId, outcome);
            if (line.Length == 0) return;
            Debug.Log(line);
        }
    }
}
