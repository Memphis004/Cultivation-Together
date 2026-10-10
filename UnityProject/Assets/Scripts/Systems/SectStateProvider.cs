using System;
using System.Collections.Generic;
using System.Linq;
using MessagePipe;
using UnityEngine;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect
{
    /// <summary>
    /// P10B — what one work tick did for one disciple. Exactly ONE of these is
    /// recorded per disciple per tick; <see cref="None"/> means the tick changed no
    /// attribute (unknown / other task).
    /// </summary>
    public enum DiscipleWorkOutcome
    {
        /// <summary>Any other task — no attribute change.</summary>
        None = 0,
        /// <summary>Gathering passed its building gate and produced its rate this tick.</summary>
        ProductiveGathering = 1,
        /// <summary>Crafting passed its gate and advanced (a completion tick counts as productive).</summary>
        ProductiveCrafting = 2,
        /// <summary>Gate failed (building missing) or the craft is held waiting for materials.</summary>
        Blocked = 3,
        /// <summary>Meditation.</summary>
        Resting = 4,
    }

    // Implements the seam TimeSystem.cs defines (ISectStateProvider).
    //
    // IMPORTANT: this holds ONE live state instance for the process
    // lifetime, created once from mock data. Earlier versions called
    // MockSectData.Create() fresh on every query, which meant any mutation
    // was invisible on the next get_sect_state call - it was building a
    // brand new object every time, not reading back the one that got
    // mutated. Swap _state's origin for real multi-subsystem aggregation
    // later if this ends up needing more than gathering + recruiting - the
    // interface doesn't need to change.
    public class SectStateProvider : ISectStateProvider
    {
        // task id -> (resource id, units produced per second while assigned)
        private static readonly Dictionary<string, (string Resource, float PerSecond)> GatheringRates = new()
        {
            ["gathering_herb"] = ("herb", 0.2f),
            ["gathering_wood"] = ("wood", 0.2f),
            ["gathering_ore"] = ("ore", 0.15f),
            ["gathering_provisions"] = ("provisions", 0.25f),
        };

        private static readonly string[] GatheringTasks = GatheringRates.Keys.ToArray();

        // task id -> recipe. CraftSeconds is how long one disciple assigned
        // to that task takes to finish one item, once ingredients are
        // available - if the stockpile runs short, progress holds at 100%
        // and waits rather than losing accumulated time.
        //
        // P10B: this dictionary is the ONE definition of the task -> skill-category
        // mapping — a completed craft awards XP to exactly the category of the recipe
        // it finished (refining_elixir -> alchemy, forging_artifact -> forging).
        // Gathering needs no table: every gathering task maps to the single
        // CategoryGathering constant. Meditation grants no skill XP.
        private static readonly Dictionary<string, CraftingRecipe> CraftingRecipes = new()
        {
            ["refining_elixir"] = new CraftingRecipe(
                "elixir_qi_gathering", 3, 20f,
                new Dictionary<string, int> { ["herb"] = 10 },
                DiscipleAttributesConfig.CategoryAlchemy),

            ["forging_artifact"] = new CraftingRecipe(
                "sword_azure_flame", 5, 30f,
                new Dictionary<string, int> { ["ore"] = 15, ["wood"] = 10 },
                DiscipleAttributesConfig.CategoryForging),
        };        // Task System v2 (§6) — the known task set is the keys of the existing
        // gathering + crafting dictionaries, plus "meditation". No new data
        // pipeline: the dictionaries ARE the source of truth for what a disciple
        // can be assigned. See open-questions.md §15 for the cultivation/meditation id question.
        private static readonly HashSet<string> KnownTasks = BuildKnownTasks();

        // P3 (Task Assignment UI) — stable ordered list of the same known tasks
        // (gathering, crafting, meditation). Kept beside the HashSet so the UI
        // query and the assignment gate can never drift apart; both are built
        // from the same dictionaries.
        private static readonly string[] KnownTaskOrder =
            GatheringRates.Keys.Concat(CraftingRecipes.Keys).Concat(new[] { "meditation" }).ToArray();

        private static HashSet<string> BuildKnownTasks()
        {
            var set = new HashSet<string>();
            foreach (var task in GatheringRates.Keys) set.Add(task);
            foreach (var task in CraftingRecipes.Keys) set.Add(task);
            set.Add("meditation");
            return set;
 }

        // Task building-requirements — static design data, not runtime state:
        // task id -> building def id that must be in SectEconomyState.PlacedBuildings
        // before that task can be assigned. Tasks absent from this dictionary
        // (gathering_wood/ore/provisions, meditation) have no requirement.
        // Placeholder lookup: list-scan of PlacedBuildings is fast enough while
        // the roster is small; swap for an index later if it grows (same shape
        // as NextBuildingInstanceId's scan).
        private static readonly Dictionary<string, string> TaskRequiredBuilding = new Dictionary<string, string>
        {
            ["gathering_herb"] = "herb_plot",
            ["refining_elixir"] = "pill_hall",
            ["forging_artifact"] = "forge",
        };

        // Tick-side variant of IsTaskAvailable — same rules, but the caller
        // supplies a placed-DefId set built ONCE per tick (never scan
        // PlacedBuildings per disciple). Same failReason shape so log/UI text
        // stays consistent with the gate on TryAssignTask.
        private static bool IsTaskAvailableWithBuildings(string taskId, HashSet<string> placedDefIds, out string failReason)
        {
            failReason = string.Empty;

            if (string.IsNullOrEmpty(taskId) || !KnownTasks.Contains(taskId))
            {
                failReason = $"Unknown task: '{taskId}'.";
                return false;
            }

            string requiredBuilding;
            if (!TaskRequiredBuilding.TryGetValue(taskId, out requiredBuilding))
                return true; // no requirement — always available once known

            if (placedDefIds != null && placedDefIds.Contains(requiredBuilding))
                return true;

            failReason = $"Task '{taskId}' requires an existing '{requiredBuilding}' building.";
            return false;
        }

        /// <summary>Placed DefId set for one tick — built once, shared by both ticks.</summary>
        private HashSet<string> CollectPlacedDefIds()
        {
            var set = new HashSet<string>();
            for (int i = 0; i < _state.PlacedBuildings.Count; i++)
            {
                var pb = _state.PlacedBuildings[i];
                if (pb != null && !string.IsNullOrEmpty(pb.DefId)) set.Add(pb.DefId);
            }
            return set;
        }

        // Placeholder name pool - swap for a real generator once there's a
        // reason to (naming conventions, avoiding repeats at scale, etc.).
        private static readonly string[] RecruitNamePool =
        {
            "Chen Wei", "Bai Ling", "Zhou Tao", "Xiao Mei", "Jiang Yu", "Wen Hao",
        };

        private static readonly string[] StarterHair = { "hair_short", "hair_topknot", "hair_twin_tail" };

        private readonly SectEconomyState _state = MockSectData.Create();
        private readonly IPublisher<DiscipleRecruitedMessage> _discipleRecruitedPublisher;
        private readonly IPublisher<SectResourceChangedMessage> _resourceChangedPublisher;
        private readonly IPublisher<AvatarEquipmentChangedMessage> _avatarChangedPublisher;
        private readonly IPublisher<DiscipleChibiBackendChangedMessage> _chibiBackendPublisher;
        private readonly IPublisher<BuildingPlacedMessage> _buildingPlacedPublisher;
        private readonly IPublisher<DiscipleTaskChangedMessage> _discipleTaskChangedPublisher;
        private readonly IPublisher<DiscipleOwnerChangedMessage> _ownershipChangedPublisher;
        // P9A — in-memory only, same rule as the ownership publisher (autonomy is a
        // local player/UI concern; it never crosses the TCP wire).
        private readonly IPublisher<DiscipleControlModeChangedMessage> _controlModeChangedPublisher;
        private readonly BuildingDefPool _buildingDefPool;
        private readonly AvatarPartPool _avatarPartPool;
        private readonly VisualRuntimeConfig _visualConfig;
        private readonly IVisualEntitlementProvider _entitlementProvider;
        /// <summary>Concrete ref to the injected provider (null when a test/substitute implements the interface directly) — used only for bind-late wiring, not for resolution.</summary>
        private readonly DefaultEntitlementProvider _defaultEntitlementProvider;

        // P5B — real-time clock for the viewer inactivity/activity rule. Injected so
        // tests can drive it deterministically; NEVER scaled game time (game time can
        // be paused/speed-changed, which would silently extend or shrink protection).
        private readonly IClock _clock;

        // P5B — real-time cooldown on ACTUAL task changes, keyed per disciple.
        // Prototype balance value (see DefaultTaskChangeCooldownSeconds). A no-op
        // request never consumes or checks it.
        private readonly Dictionary<string, DateTime> _taskChangeLastAtUtc = new();

        // Fractional resource accumulated per task since the last whole
        // unit was added to the stockpile - avoids losing sub-1 production
        // between ticks.
        private readonly Dictionary<string, float> _gatherAccumulators = new();

        // Seconds accumulated toward the current craft, keyed per disciple
        // (not per task like gathering) - crafting has a resource cost, so
        // two disciples on the same task must progress independently, not
        // share one pooled timer.
        private readonly Dictionary<string, float> _craftProgress = new();

        // P10B — per-tick scratch: disciple id -> that tick's single work record.
        // Cleared at the start of a tick and consumed by ApplyWorkAttributes, so an
        // outcome can never be written twice or applied twice. One small map, one
        // applier — no second clock, no fixed-step loop, no per-frame allocation.
        private struct WorkTickRecord
        {
            public DiscipleWorkOutcome Outcome;
            /// <summary>Non-null only on a craft-completion tick (P10B §4).</summary>
            public string CompletedCraftCategory;
        }

        private readonly Dictionary<string, WorkTickRecord> _workTick = new();
        private int _workTickRecordCount;

        public SectStateProvider(
            IPublisher<DiscipleRecruitedMessage> discipleRecruitedPublisher,
            IPublisher<SectResourceChangedMessage> resourceChangedPublisher,
            IPublisher<AvatarEquipmentChangedMessage> avatarChangedPublisher,
            IPublisher<DiscipleChibiBackendChangedMessage> chibiBackendPublisher,
            AvatarPartPool avatarPartPool,
            VisualRuntimeConfig visualConfig,
            IVisualEntitlementProvider entitlementProvider,
            BuildingDefPool buildingDefPool,
            IPublisher<BuildingPlacedMessage> buildingPlacedPublisher,
            IPublisher<DiscipleTaskChangedMessage> discipleTaskChangedPublisher,
            IPublisher<DiscipleOwnerChangedMessage> ownershipChangedPublisher = null,
            IClock clock = null,
            IPublisher<DiscipleControlModeChangedMessage> controlModeChangedPublisher = null)
        {
            _discipleRecruitedPublisher = discipleRecruitedPublisher;
            _resourceChangedPublisher = resourceChangedPublisher;
            _avatarChangedPublisher = avatarChangedPublisher;
            _chibiBackendPublisher = chibiBackendPublisher;
            _buildingDefPool = buildingDefPool;
            _buildingPlacedPublisher = buildingPlacedPublisher;
            _discipleTaskChangedPublisher = discipleTaskChangedPublisher;
            // P4: optional (default null) so every existing test construction site
            // stays valid; production wires it via VContainer in UIInstaller/GameLifetimeScope.
            _ownershipChangedPublisher = ownershipChangedPublisher;
            // P9A: optional (default null) for the same reason — appended LAST so the
            // positional test construction sites (ownerChanged, clock) are unchanged.
            _controlModeChangedPublisher = controlModeChangedPublisher;
            _avatarPartPool = avatarPartPool;
            _visualConfig = visualConfig;
            _entitlementProvider = entitlementProvider;
            _defaultEntitlementProvider = entitlementProvider as DefaultEntitlementProvider;
            // P5B: production registers UtcClock via DI; every existing test construction
            // site omits it and keeps working (real UTC clock, which those tests never
            // depend on because they never cross the 10-minute protection window).
            _clock = clock ?? new UtcClock();

            // Phase 5 — bind the provider's rank source HERE instead of injecting
            // ISectStateProvider into the provider itself: that direction would be a
            // DI cycle (SectStateProvider → provider → SectStateProvider). Bind-late
            // keeps the provider ignorant of the state module; before this line runs,
            // CanUse(discipleId, "owner") fails closed (Unspecified = deny).
            //
            // Note: inject the INTERFACE, not the concrete type. In this VContainer
            // version Register<I, Impl> registers only the interface (concrete Resolve
            // is not available), so consumers must resolve IVisualEntitlementProvider
            // and reach the concrete for bind-late via a type test.
            _defaultEntitlementProvider?.BindRankLookup(id =>
            {
                var d = FindDisciple(id);
                return d != null ? d.Rank : DiscipleRank.Unspecified;
            });
        }

        /// <summary>Single lookup helper — also used by the entitlement rank binding.</summary>
        private DiscipleState FindDisciple(string discipleId)
        {
            if (string.IsNullOrEmpty(discipleId)) return null;
            for (int i = 0; i < _state.Disciples.Count; i++)
            {
                var d = _state.Disciples[i];
                if (d != null && d.DiscipleId == discipleId) return d;
            }
            return null;
        }

        public SectEconomyState BuildSectEconomyState()
        {
            return _state;
        }

        // Passive resource gathering - every disciple whose CurrentTask is
        // a known gathering task contributes toward that resource. Called
        // from DiscipleSystem.Tick(). Disciples whose task fails the building
        // requirement are SKIPPED (CurrentTask is never rewritten here — the
        // assignment gate is the only place that validates on assignment).
        //
        // P10B: production is unchanged; this tick additionally records ONE work
        // outcome per disciple and then applies the attribute change once.
        public void TickGathering(float deltaTimeSeconds)
        {
            var placedDefIds = CollectPlacedDefIds(); // once per tick, not per disciple
            BeginWorkTick(); // P10B
            foreach (var disciple in _state.Disciples)
            {
                // P10B — EVERY disciple gets exactly one outcome in this tick. Meditation
                // rests; anything that is not a gathering task (including a crafting task,
                // whose tick owns its change) is None = no attribute change here, so the
                // outcome cannot be applied twice across the two ticks of a frame. The
                // empty-task guard must come first so no dictionary is probed with a null key.
                if (string.IsNullOrEmpty(disciple.CurrentTask))
                {
                    RecordWorkOutcome(disciple, DiscipleWorkOutcome.None);
                    continue;
                }

                if (!GatheringRates.TryGetValue(disciple.CurrentTask, out var rate))
                {
                    RecordWorkOutcome(disciple, IsMeditationTask(disciple.CurrentTask)
                        ? DiscipleWorkOutcome.Resting
                        : DiscipleWorkOutcome.None);
                    continue;
                }

                if (!IsTaskAvailableWithBuildings(disciple.CurrentTask, placedDefIds, out _))
                {
                    // P10B: the gate failed (building missing) → Blocked (small regen).
                    RecordWorkOutcome(disciple, DiscipleWorkOutcome.Blocked);
                    continue;
                }

                var accKey = disciple.CurrentTask;
                var acc = _gatherAccumulators.TryGetValue(accKey, out var existing) ? existing : 0f;
                acc += rate.PerSecond * deltaTimeSeconds;

                var wholeUnits = Mathf.FloorToInt(acc);
                if (wholeUnits > 0)
                {
                    AdjustAndNotify(_state.Stockpile.RawResources, rate.Resource, wholeUnits);
                    acc -= wholeUnits;
                    Debug.Log($"[SectStateProvider] Gathered +{wholeUnits} {rate.Resource} (task={accKey})");
                }

                _gatherAccumulators[accKey] = acc;

                // P10B: reaching here means the gate passed → Productive.
                RecordWorkOutcome(disciple, DiscipleWorkOutcome.ProductiveGathering);
            }

            ApplyWorkAttributes(deltaTimeSeconds); // P10B — the single apply step
        }

        // Disciple crafting: whoever's CurrentTask matches a known recipe
        // accumulates progress; once a craft completes, consumes the raw
        // resource cost and produces the item - into the sect stockpile for
        // ordinary disciples, or straight into personal inventory for
        // Elder+ (matches the ownership rule from the economy design:
        // outer/inner disciples craft for the sect, elders keep their own).
        // Called from ResourceCraftingSystem.Tick(). Disciples whose task
        // fails the building requirement are SKIPPED — progress is HELD at
        // its current value exactly like the out-of-materials path (never
        // reset), and CurrentTask is never rewritten here.
        public void TickCrafting(float deltaTimeSeconds)
        {
            var placedDefIds = CollectPlacedDefIds(); // once per tick, not per disciple
            BeginWorkTick(); // P10B
            foreach (var disciple in _state.Disciples)
            {
                // P10B — EVERY disciple gets exactly one outcome in this tick too. Only a
                // crafting task changes an attribute here; everything else (gathering task,
                // meditation, unknown/empty) is None = handled by TickGathering, so no
                // stamina change is applied twice across the two ticks of a frame.
                if (string.IsNullOrEmpty(disciple.CurrentTask))
                {
                    RecordWorkOutcome(disciple, DiscipleWorkOutcome.None);
                    continue;
                }

                if (!CraftingRecipes.TryGetValue(disciple.CurrentTask, out var recipe))
                {
                    RecordWorkOutcome(disciple, DiscipleWorkOutcome.None);
                    continue;
                }

                if (!IsTaskAvailableWithBuildings(disciple.CurrentTask, placedDefIds, out _))
                {
                    // P10B: the gate failed (building missing) → Blocked (small regen).
                    RecordWorkOutcome(disciple, DiscipleWorkOutcome.Blocked);
                    continue;
                }

                var progress = _craftProgress.TryGetValue(disciple.DiscipleId, out var existing) ? existing : 0f;
                progress += deltaTimeSeconds;

                if (progress < recipe.CraftSeconds) 
                {
                    _craftProgress[disciple.DiscipleId] = progress;
                    // P10B: progress advanced → Productive (no completion yet).
                    RecordWorkOutcome(disciple, DiscipleWorkOutcome.ProductiveCrafting);
                    continue;
                }

                if (!TryConsume(_state.Stockpile.RawResources, recipe.Costs))
                {
                    // Ready to complete but not enough raw resources - hold
                    // at the completion threshold and wait rather than
                    // losing the accumulated progress or overshooting.
                    _craftProgress[disciple.DiscipleId] = recipe.CraftSeconds;
                    // P10B: held waiting for materials → Blocked; no XP is awarded.
                    RecordWorkOutcome(disciple, DiscipleWorkOutcome.Blocked);
                    continue;
                }

                foreach (var (resource, amount) in recipe.Costs)
                {
                    var newTotal = _state.Stockpile.RawResources.TryGetValue(resource, out var v) ? v : 0;
                    _resourceChangedPublisher.Publish(new SectResourceChangedMessage
                    {
                        ResourceId = resource,
                        Delta = -amount,
                        NewTotal = newTotal,
                    });
                }

                var item = new InventoryItem
                {
                    ItemDefId = recipe.ItemDefId,
                    Quantity = 1,
                    Grade = recipe.Grade,
                    OwnerScope = disciple.Rank >= DiscipleRank.Elder ? OwnerScope.Personal : OwnerScope.SectStockpile,
                };

                if (item.OwnerScope == OwnerScope.Personal)
                {
                    disciple.PersonalInventory.Add(item);
                }
                else
                {
                    AddToStockpileGoods(item);
                }

                Debug.Log($"[SectStateProvider] {disciple.DisplayName} crafted {item.ItemDefId} " +
                          $"(grade {item.Grade}, {item.OwnerScope})");

                // Carry over any overshoot instead of resetting to exactly 0.
                _craftProgress[disciple.DiscipleId] = progress - recipe.CraftSeconds;

                // P10B: this is the authoritative completion point. The completion tick
                // counts as Productive (§4 simplification) and carries the recipe's
                // skill category so the XP is awarded ONCE, in ApplyWorkAttributes.
                RecordWorkOutcome(disciple, DiscipleWorkOutcome.ProductiveCrafting, recipe.SkillCategory);
            }

            ApplyWorkAttributes(deltaTimeSeconds); // P10B — the single apply step
        }

        // ---------- P10B: work outcome → attribute change ----------

        /// <summary>Starts one tick's outcome scratch (P10B).</summary>
        private void BeginWorkTick()
        {
            _workTick.Clear();
            _workTickRecordCount = 0;
        }

        /// <summary>
        /// Records this tick's outcome for one disciple. Each tick writes exactly ONE
        /// record per disciple (the two ticks of a frame cover different task domains and
        /// the other side records None), which is what keeps the apply step from
        /// double-counting a change.
        /// </summary>
        private void RecordWorkOutcome(DiscipleState disciple, DiscipleWorkOutcome outcome,
                                       string completedCraftCategory = null)
        {
            if (disciple == null || string.IsNullOrEmpty(disciple.DiscipleId)) return;

            _workTick[disciple.DiscipleId] = new WorkTickRecord
            {
                Outcome = outcome,
                CompletedCraftCategory = completedCraftCategory,
            };
            _workTickRecordCount++;
        }

        /// <summary>
        /// The ONLY place P10B mutates attributes (one function, called once per tick,
        /// after that tick's outcomes are recorded — so nothing can be applied twice).
        /// Rates are linear in the tick's simulation delta (rate * delta, then clamped);
        /// a zero or non-finite delta (pause) changes no rate-driven value. A completed
        /// craft is an event, not a rate, so its XP is not scaled by the delta.
        /// </summary>
        private void ApplyWorkAttributes(float deltaTimeSeconds)
        {
            if (_workTick.Count == 0) return;

            bool ratesApply = deltaTimeSeconds > 0f; // false for 0 (paused) and for NaN

            foreach (var disciple in _state.Disciples)
            {
                if (disciple == null || string.IsNullOrEmpty(disciple.DiscipleId)) continue;

                WorkTickRecord record;
                if (!_workTick.TryGetValue(disciple.DiscipleId, out record)) continue;

                // Attributes are created + normalized by P10A; never invented or repaired here.
                var attributes = disciple.Attributes;
                if (attributes == null) continue;

                float staminaPerSecond;
                string xpCategory = null;
                float xpPerSecond = 0f;

                switch (record.Outcome)
                {
                    case DiscipleWorkOutcome.ProductiveGathering:
                        staminaPerSecond = -DiscipleAttributesConfig.WorkStaminaDrainPerSecondGathering;
                        xpCategory = DiscipleAttributesConfig.CategoryGathering;
                        xpPerSecond = DiscipleAttributesConfig.WorkGatheringXpPerSecond;
                        break;
                    case DiscipleWorkOutcome.ProductiveCrafting:
                        staminaPerSecond = -DiscipleAttributesConfig.WorkStaminaDrainPerSecondCrafting;
                        break;
                    case DiscipleWorkOutcome.Resting:
                        staminaPerSecond = DiscipleAttributesConfig.WorkStaminaRegenPerSecondResting;
                        break;
                    case DiscipleWorkOutcome.Blocked:
                        staminaPerSecond = DiscipleAttributesConfig.WorkStaminaRegenPerSecondBlocked;
                        break;
                    default:
                        continue; // None — no attribute change at all
                }

                if (ratesApply)
                {
                    attributes.Stamina = ClampStamina(attributes.Stamina + staminaPerSecond * deltaTimeSeconds);
                    if (xpCategory != null)
                        AddSkillXp(attributes, xpCategory, xpPerSecond * deltaTimeSeconds);
                }

                // Craft completion XP: awarded where the item was actually produced. A
                // craft already held at CraftSeconds can complete on a zero-delta frame
                // (the item is still made), so the award follows the completion, not the delta.
                if (!string.IsNullOrEmpty(record.CompletedCraftCategory))
                    AddSkillXp(attributes, record.CompletedCraftCategory,
                               DiscipleAttributesConfig.WorkCraftXpPerCompletion);
            }
        }

        /// <summary>Adds XP to one category, reading through the shared safe accessor and clamping to the cap.</summary>
        private static void AddSkillXp(DiscipleAttributes attributes, string category, float amount)
        {
            if (string.IsNullOrEmpty(category) || amount == 0f) return;
            if (attributes.SkillXp == null) attributes.SkillXp = new Dictionary<string, float>();

            float current = DiscipleAttributes.GetSkillXp(attributes, category);
            attributes.SkillXp[category] = ClampSkillXp(current + amount);
        }

        /// <summary>Clamps to the configured stamina bounds (a non-finite value is left for Normalize to repair).</summary>
        private static float ClampStamina(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return value;
            if (value < DiscipleAttributesConfig.StaminaMin) return DiscipleAttributesConfig.StaminaMin;
            if (value > DiscipleAttributesConfig.StaminaMax) return DiscipleAttributesConfig.StaminaMax;
            return value;
        }

        /// <summary>Clamps to the configured XP bounds (0..cap).</summary>
        private static float ClampSkillXp(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return DiscipleAttributesConfig.SkillXpDefault;
            if (value < DiscipleAttributesConfig.SkillXpMin) return DiscipleAttributesConfig.SkillXpMin;
            if (value > DiscipleAttributesConfig.SkillXpMax) return DiscipleAttributesConfig.SkillXpMax;
            return value;
        }

        /// <summary>P10B — the resting task id (same literal KnownTasks is built with).</summary>
        private static bool IsMeditationTask(string taskId)
        {
            return taskId == "meditation";
        }

        // ---- P10B test seams (EditMode tests, no reflection) ----

        /// <summary>The outcome recorded for one disciple in the tick that just ran.</summary>
        public bool TryGetLastWorkOutcome(string discipleId, out DiscipleWorkOutcome outcome)
        {
            WorkTickRecord record;
            if (!string.IsNullOrEmpty(discipleId) && _workTick.TryGetValue(discipleId, out record))
            {
                outcome = record.Outcome;
                return true;
            }

            outcome = DiscipleWorkOutcome.None;
            return false;
        }

        /// <summary>
        /// How many outcome records the last tick wrote. Equal to the number of
        /// disciples means exactly one record each — a second record for the same
        /// disciple would be one more than the roster size.
        /// </summary>
        public int LastTickOutcomeRecordCountForTest => _workTickRecordCount;

        // Adds a new Outer Disciple assigned to a gathering task, round-robin
        // across GatheringTasks so recruits don't all pile onto one resource.
        public void RecruitOuterDisciple(DiscipleSex sex = DiscipleSex.Unspecified)
        {
            var index = _state.Disciples.Count;
            var task = GatheringTasks[index % GatheringTasks.Length];
            var name = RecruitNamePool[index % RecruitNamePool.Length];

            // Default parity: even index → Male, odd → Female (preserves existing mixed-roster look)
            var resolvedSex = (sex != DiscipleSex.Unspecified) ? sex
                : (index % 2 == 0) ? DiscipleSex.Male : DiscipleSex.Female;

            var disciple = new DiscipleState
            {
                DiscipleId = $"d{index + 1:000}",
                DisplayName = name,
                Rank = DiscipleRank.OuterDisciple,
                Wallet = new CurrencyWallet(),
                PersonalInventory = new List<InventoryItem>(),
                CurrentTask = task,
                Sex = resolvedSex,
                Avatar = CreateStarterAvatar(index, resolvedSex),
                // P9A — new disciples start Manual (explicit, matching the field default).
                ControlMode = DiscipleControlMode.Manual,
                // P10A — fresh, normalized attribute block (stamina 100, all 3 skill
                // categories present at 0 XP). Data only; nothing consumes it yet.
                Attributes = DiscipleAttributes.Normalize(new DiscipleAttributes()),
            };

            _state.Disciples.Add(disciple);
            _discipleRecruitedPublisher.Publish(new DiscipleRecruitedMessage
            {
                DiscipleId = disciple.DiscipleId,
                DisplayName = disciple.DisplayName,
            });

            Debug.Log($"[SectStateProvider] Recruited outer disciple: {disciple.DisplayName} ({disciple.DiscipleId}), sex={resolvedSex}, assigned to {task}");
        }

        private AvatarAppearance CreateStarterAvatar(int rosterIndex, DiscipleSex sex)
        {
            var a = new AvatarAppearance();
            a.SetSlot(AvatarSlots.Body, "body_robe_grey");
            a.SetSlot(AvatarSlots.Head, (sex == DiscipleSex.Female) ? "head_female_01" : "head_male_01");
            a.SetSlot(AvatarSlots.Hair, StarterHair[rosterIndex % StarterHair.Length]);
            return a;
        }

        public bool TryChangeAvatarPart(string discipleId, string slot, string partId,
                                        out string failReason, out AvatarAppearance result)
        {
            failReason = string.Empty;
            result = null;

            var disciple = _state.Disciples.FirstOrDefault(d => d.DiscipleId == discipleId);
            if (disciple == null) { failReason = $"No disciple with id: {discipleId}"; return false; }

            if (System.Array.IndexOf(AvatarSlots.Equippable, slot) < 0)
            { failReason = $"Invalid slot: {slot}"; return false; }

            if (!_avatarPartPool.IsValidForSlot(slot, partId))
            { failReason = $"PartId '{partId}' is not valid for slot '{slot}'"; return false; }

            // Pose validation: reject a part whose poseId is non-empty and
            // differs from the disciple's effective pose.
            if (!string.IsNullOrEmpty(partId))
            {
                var partDef = _avatarPartPool.GetById(partId);
                if (partDef != null && !string.IsNullOrEmpty(partDef.poseId))
                {
                    string effectivePose = (disciple.Avatar != null && !string.IsNullOrEmpty(disciple.Avatar.PoseId))
                        ? disciple.Avatar.PoseId
                        : "pose_idle_01";
                    if (partDef.poseId != effectivePose)
                    {
                        failReason = $"Part '{partId}' requires pose '{partDef.poseId}' but disciple is in pose '{effectivePose}'.";
                        return false;
                    }
                }
            }

            // Validation ชั้น 5 — coverage: part ต้องมี art อย่างน้อย 1 backend ที่เปิดใช้
            // (empty layer = เจตนา "ถอดออก" ผ่านเสมอ — *_none defaults)
            // Face split (Roadmap #1): face sub-layers เป็น portrait-only ตามดีไซน์ (R4 —
            // chibi เก็บ feature baked-in) จึงผ่านด้วย Portrait เดี่ยว โดยไม่ต้องมี chibi/spine art
            // (แก้ latent bug: acc_none — ชิ้น "ถอดเครื่องประดับ" ที่มีแต่ chibi art — เคยโดน reject)
            if (!string.IsNullOrEmpty(partId))
            {
                var partDef = _avatarPartPool.GetById(partId);
                if (partDef != null && !partDef.IsEmptyLayer)
                {
                    bool anyCovered =
                        partDef.Supports(VisualBackend.Portrait) ||
                        (_visualConfig != null && _visualConfig.SpriteSheetEnabled &&
                         partDef.Supports(VisualBackend.SpriteSheet)) ||
                        (_visualConfig != null && _visualConfig.SpineEnabled &&
                         partDef.Supports(VisualBackend.Spine));
                    if (!anyCovered)
                    {
                        failReason = $"Part '{partId}' has no art on any enabled backend (coverage check).";
                        return false;
                    }
                }
            }

            // Validation ชั้น 6 — entitlement (Phase 5, §8): part ที่ติด entitlement
            // ต้องผ่าน IVisualEntitlementProvider เท่านั้น — ต่างจากชั้น 5 ที่ป้องกัน
            // "render ไม่ได้" ชั้นนี้ป้องกัน "ไม่มีสิทธิ์ใช้" — failReason เขียนให้
            // AI/UI อ่านแล้วเข้าใจเหตุผล (ตามสเปก Phase 5)
            if (!string.IsNullOrEmpty(partId) && _entitlementProvider != null)
            {
                var entitlementDef = _avatarPartPool.GetById(partId);
                if (entitlementDef != null && !string.IsNullOrEmpty(entitlementDef.entitlement) &&
                    !_entitlementProvider.CanUse(discipleId, entitlementDef.entitlement))
                {
                    failReason = $"Part '{partId}' requires entitlement '{entitlementDef.entitlement}'.";
                    return false;
                }
            }

            if (disciple.Avatar == null) disciple.Avatar = new AvatarAppearance();

            var oldPart = disciple.Avatar.GetSlot(slot);
            disciple.Avatar.SetSlot(slot, partId);

            _avatarChangedPublisher.Publish(new AvatarEquipmentChangedMessage
            {
                DiscipleId = discipleId,
                Slot       = slot,
                OldPartId  = oldPart,
                NewPartId  = partId
            });

            result = disciple.Avatar.Clone();   // return copy, not reference to live state
            return true;
        }

        // ---------- P4 (local ownership test harness) — validated ownership assignment ----------
        // Non-Npc OwnerId convention: a real Twitch user id is numeric, so the
        // synthetic local-identity convention is "viewer_*" / "player_*" (alpha
        // prefix + underscore, no whitespace) — clearly fake, never shaped like a
        // real Twitch id. Real authentication (P5B+) replaces this entirely.
        private const string OwnerIdPattern = "^[A-Za-z][A-Za-z0-9]*(_[A-Za-z0-9]+)*$";

        public bool TrySetDiscipleOwner(string discipleId, DiscipleOwnerType ownerType,
                                        string ownerId, out string failReason)
        {
            failReason = string.Empty;

            // 1. disciple must exist
            var disciple = FindDisciple(discipleId);
            if (disciple == null)
            {
                failReason = $"No disciple with id: {discipleId}";
                return false;
            }

            // 2. enum value must be defined (fail-closed on out-of-range casts)
            if (!Enum.IsDefined(typeof(DiscipleOwnerType), ownerType))
            {
                failReason = $"Undefined DiscipleOwnerType value: {ownerType}";
                return false;
            }

            var trimmedOwnerId = (ownerId ?? string.Empty).Trim();

            // 3. Npc normalizes OwnerId to empty (no orphaned ids on Npc rows)
            if (ownerType == DiscipleOwnerType.Npc)
            {
                if (!string.IsNullOrEmpty(trimmedOwnerId))
                {
                    failReason = "Npc ownership must carry an empty OwnerId.";
                    return false;
                }
                trimmedOwnerId = string.Empty;
            }
            else
            {
                // 4. non-Npc identities satisfy the identity convention
                if (string.IsNullOrEmpty(trimmedOwnerId) ||
                    !System.Text.RegularExpressions.Regex.IsMatch(trimmedOwnerId, OwnerIdPattern))
                {
                    failReason = $"OwnerId '{trimmedOwnerId}' does not satisfy the identity convention " +
                                 "(alpha prefix, [A-Za-z0-9_], no whitespace).";
                    return false;
                }
            }

            // 5. a Viewer identity must not control a different active disciple
            if (ownerType == DiscipleOwnerType.Viewer && !string.IsNullOrEmpty(trimmedOwnerId))
            {
                for (int i = 0; i < _state.Disciples.Count; i++)
                {
                    var other = _state.Disciples[i];
                    if (other == null || other.DiscipleId == discipleId) continue;
                    if (other.OwnerType == DiscipleOwnerType.Viewer && other.OwnerId == trimmedOwnerId)
                    {
                        failReason = $"Viewer '{trimmedOwnerId}' already controls '{other.DiscipleId}'.";
                        return false;
                    }
                }
            }

            // 6. no-op → no mutation, no message. A re-bind/reclaim of the SAME
            // owner (P5B) still refreshes that owner's activity — it is a valid
            // owner command, and it keeps an active player outside the override window.
            if (disciple.OwnerType == ownerType && disciple.OwnerId == trimmedOwnerId)
            {
                if (ownerType == DiscipleOwnerType.Viewer)
                    SyncViewerRegistryForBind(disciple, trimmedOwnerId);
                return true;
            }

            // --- validation complete: single mutation block (no partial writes) ---
            var oldType = disciple.OwnerType;
            var oldOwnerId = disciple.OwnerId;
            disciple.OwnerType = ownerType;
            disciple.OwnerId = trimmedOwnerId;

            // P9A — autonomy travels with Npc ownership: any real ownership switch
            // takes the disciple out of Auto (a Player/Viewer owner is never
            // brain-controlled, and the switch itself must disable Auto — rule 7).
            DisableAutoIfEngaged(disciple);

            // P5B: registry travels with the disciple row — release the previous
            // viewer record (if any), then activate/bind the new one, so invariant
            // #1/#2/#3 (active ⇔ bound; non-active ⇒ unbound) always holds.
            if (oldType == DiscipleOwnerType.Viewer && !string.IsNullOrEmpty(oldOwnerId))
                SyncViewerRegistryForRelease(oldOwnerId);
            if (ownerType == DiscipleOwnerType.Viewer)
                SyncViewerRegistryForBind(disciple, trimmedOwnerId);

            _ownershipChangedPublisher?.Publish(new DiscipleOwnerChangedMessage
            {
                DiscipleId = discipleId,
                OldType = oldType,
                OldOwnerId = oldOwnerId,
                NewType = ownerType,
                NewOwnerId = trimmedOwnerId,
            });
            return true;
        }

        // ---------- P5B — SectViewerRegistry ⇄ disciple ownership sync ----------
        // These are the ONLY writers of membership records, and they run inside the
        // same mutation block as the disciple row, so the two can never disagree.

        /// <summary>Activate (or create) the active record binding <paramref name="viewerId"/> to this disciple and stamp activity.</summary>
        private void SyncViewerRegistryForBind(DiscipleState disciple, string viewerId)
        {
            var record = _state.ViewerRegistry.Find(viewerId);
            if (record == null)
            {
                record = new ViewerRecord { ViewerId = viewerId, DisplayName = viewerId };
                _state.ViewerRegistry.Records.Add(record);
            }
            record.Status = ViewerMembershipStatus.Active;
            record.BoundDiscipleId = disciple.DiscipleId;
            record.LastActiveAtUtc = _clock.UtcNow;
        }

        /// <summary>Release a record that no longer owns a disciple (kept as Left, never left bound — invariant #3).</summary>
        private void SyncViewerRegistryForRelease(string viewerId)
        {
            var record = _state.ViewerRegistry.Find(viewerId);
            if (record == null) return;
            record.Status = ViewerMembershipStatus.Left;
            record.BoundDiscipleId = string.Empty;
        }

        // ---------- P3 (Task Assignment UI) — read-only known-task query ----------
        // Same source of truth as TryAssignTask's KnownTasks set — no second list.
        public System.Collections.Generic.IReadOnlyList<string> GetKnownTaskIds()
        {
            return KnownTaskOrder;
        }

        // ---------- P9B (utility AI) — read-only task metadata ----------
        // Derived from the SAME GatheringRates/CraftingRecipes/KnownTasks tables the
        // assignment gate validates against, so the scorer can never describe a task
        // differently from what assignment accepts. Read-only, no mutation, and no
        // separate task-definition pipeline: the dictionaries above ARE the source.
        public bool TryGetTaskInfo(string taskId, out SectTaskInfo info)
        {
            info = null;
            if (string.IsNullOrEmpty(taskId) || !KnownTasks.Contains(taskId)) return false;

            if (GatheringRates.TryGetValue(taskId, out var rate))
            {
                info = new SectTaskInfo
                {
                    TaskId = taskId,
                    Kind = SectTaskKind.Gathering,
                    ProducesResource = rate.Resource,
                    UnitsPerSecond = rate.PerSecond,
                    // P10C: same category the work tick awards XP to (P10B) — no second mapping.
                    SkillCategory = DiscipleAttributesConfig.CategoryGathering,
                };
                return true;
            }

            if (CraftingRecipes.TryGetValue(taskId, out var recipe))
            {
                info = new SectTaskInfo
                {
                    TaskId = taskId,
                    Kind = SectTaskKind.Crafting,
                    ProducesItem = recipe.ItemDefId,
                    ProducesItemGrade = recipe.Grade,
                    InputCosts = recipe.Costs,
                    // P10C: the recipe's own category (refining_elixir → alchemy,
                    // forging_artifact → forging) — the ONE task→category definition.
                    SkillCategory = recipe.SkillCategory,
                };
                return true;
            }

            // Anything else in KnownTasks produces nothing (meditation) — the safe fallback
            // (and it exercises no skill category, so it never earns a skill bonus).
            info = new SectTaskInfo { TaskId = taskId, Kind = SectTaskKind.Meditation };
            return true;
        }

        // ---------- Task System v2 (§6) + P5B (Hybrid Permissions) ----------
        // Validation order: disciple lookup → permission (read-only evaluation,
        // revalidated here) → known task → building gate → no-op → cooldown → commit.
        // Nothing mutates until every check passes (no partial mutation).
        // This method never touches Stockpile.RawResources or any progress store.
        // Public so read-only observers (bridge protection query, UI) use the SAME
        // requester id the authority checks — no second spelling can drift.
        public const string SectMasterRequesterId = "SECT_MASTER";

        /// <summary>
        /// P5B — how long a viewer owner is protected from a SectMaster override,
        /// measured in REAL time on the injected clock. 10 minutes per the agreed
        /// policy; the override requires inactivity STRICTLY greater than this
        /// (exactly 10 minutes still counts as active → protected).
        /// </summary>
        private const double ViewerProtectionWindowSeconds = 600d;

        /// <summary>
        /// P5B — prototype balance value: minimum real-time gap between two ACTUAL
        /// task changes on the same disciple. Configurable via
        /// <see cref="TaskChangeCooldownSeconds"/> (0 disables it). A no-op request
        /// (task already current) never checks or consumes it. This throttles task
        /// thrash only — it does NOT guarantee crafting completion.
        /// </summary>
        public const float DefaultTaskChangeCooldownSeconds = 12f;

        /// <summary>P5B — configurable real-time cooldown for actual task changes (see the const above). 0 disables.</summary>
        public float TaskChangeCooldownSeconds { get; set; } = DefaultTaskChangeCooldownSeconds;

        public bool TryAssignTask(string requesterId, string discipleId, string taskId, out string failReason)
        {
            failReason = string.Empty;

            var disciple = FindDisciple(discipleId);
            if (disciple == null)
            {
                failReason = $"No disciple with id: {discipleId}";
                return false;
            }

            // 1. Permission — same evaluation the read-only UI query exposes
            // (single authority, so display and commit can never disagree).
            var permission = EvaluateTaskPermission(requesterId, disciple);
            if (!permission.Allowed)
            {
                failReason = permission.Reason;
                return false;
            }

            var trimmedRequester = (requesterId ?? string.Empty).Trim();

            // 2. Shared validation/mutation path (known task → building gate →
            // no-op → cooldown → commit). Identical to the auto-assignment path.
            if (!TryAssignTaskCore(disciple, taskId, trimmedRequester, out failReason))
                return false;

            // P9A — an explicit manual assignment is a human asserting control: it
            // takes the disciple out of Auto. Runs only after the shared core committed,
            // so a failed assignment changes NEITHER the task NOR the mode (rule 5).
            DisableAutoIfEngaged(disciple);
            return true;
        }

        // ---------- P9A — explicit Manual/Auto control (ownership ≠ autonomy) ----------

        /// <summary>
        /// Explicitly set a disciple's Manual/Auto mode. Ownership and autonomy are
        /// different: this never changes ownership, and only an Npc-owned disciple may
        /// opt into Auto in this MVP (Player/Viewer owners are rejected, and viewer
        /// inactivity never grants Auto — hybrid inactivity only lets the SectMaster
        /// override a task). Eligibility is rechecked HERE, the authoritative mutation.
        /// Idempotent; publishes only on a real change.
        /// </summary>
        public bool TrySetDiscipleControlMode(string discipleId, DiscipleControlMode mode, out string failReason)
        {
            failReason = string.Empty;

            var disciple = FindDisciple(discipleId);
            if (disciple == null)
            {
                failReason = $"No disciple with id: {discipleId}";
                return false;
            }

            if (!Enum.IsDefined(typeof(DiscipleControlMode), mode))
            {
                failReason = $"Undefined DiscipleControlMode value: {mode}";
                return false;
            }

            if (mode == DiscipleControlMode.Auto && disciple.OwnerType != DiscipleOwnerType.Npc)
            {
                failReason = $"Only NPC-owned disciples may opt into Auto (owner is '{disciple.OwnerType}').";
                return false;
            }

            SetControlModeInternal(disciple, mode);
            return true;
        }

        /// <summary>
        /// P9A — trusted internal caller context for the future DiscipleBrain. NOT
        /// TryAssignTask(SectMasterRequesterId, ...): the auto-assigner has no requester
        /// identity, never impersonates the player, and never inherits the SectMaster
        /// override. Rechecks Npc ownership AND Auto mode immediately before commit,
        /// then shares TryAssignTaskCore (the exact validation/cooldown/mutation path),
        /// so the brain can never bypass the known-task/building/cooldown gates.
        /// </summary>
        public bool TryAutoAssignTask(string discipleId, string taskId, out string failReason)
        {
            failReason = string.Empty;

            var disciple = FindDisciple(discipleId);
            if (disciple == null)
            {
                failReason = $"No disciple with id: {discipleId}";
                return false;
            }

            // Recheck immediately before commit — ownership/mode may have changed since
            // the caller decided to auto-assign.
            if (disciple.OwnerType != DiscipleOwnerType.Npc)
            {
                failReason = $"Disciple '{discipleId}' is not NPC-owned (owner is '{disciple.OwnerType}') " +
                             "— not eligible for auto-assignment.";
                return false;
            }

            if (disciple.ControlMode != DiscipleControlMode.Auto)
            {
                failReason = $"Disciple '{discipleId}' is not in Auto mode (mode is '{disciple.ControlMode}').";
                return false;
            }

            return TryAssignTaskCore(disciple, taskId, string.Empty, out failReason);
        }

        /// <summary>
        /// Shared task-change implementation for both the manual (TryAssignTask) and
        /// auto (TryAutoAssignTask) paths. <paramref name="trimmedRequester"/> drives only
        /// the per-viewer activity refresh ("SECT_MASTER" and the empty auto source are
        /// no-ops there); permission is never evaluated here — callers gate before entry.
        /// </summary>
        private bool TryAssignTaskCore(DiscipleState disciple, string taskId, string trimmedRequester, out string failReason)
        {
            failReason = string.Empty;

            // Known task + building requirement (no mutation on failure).
            if (string.IsNullOrEmpty(taskId) || !KnownTasks.Contains(taskId))
            {
                failReason = $"Unknown task: '{taskId}'.";
                return false;
            }

            if (!IsTaskAvailable(taskId, out failReason))
                return false;

            // No-op BEFORE the cooldown: asking for the task already assigned is a
            // valid no-op — no progress reset, no DiscipleTaskChangedMessage, and it
            // must not consume or be blocked by the task-change cooldown. It may
            // refresh the requester's OWN activity (never another viewer's).
            if (disciple.CurrentTask == taskId)
            {
                RefreshRequesterActivity(trimmedRequester);
                return true;
            }

            // Cooldown on actual changes (per disciple, real time).
            if (TaskChangeCooldownSeconds > 0f &&
                _taskChangeLastAtUtc.TryGetValue(disciple.DiscipleId, out var lastChangeUtc))
            {
                var elapsed = (_clock.UtcNow - lastChangeUtc).TotalSeconds;
                if (elapsed < TaskChangeCooldownSeconds)
                {
                    var remaining = TaskChangeCooldownSeconds - elapsed;
                    failReason = $"Task change for '{disciple.DiscipleId}' is on cooldown " +
                                 $"({TaskChangeCooldownSeconds:0.#}s) — {remaining:0.0}s remaining.";
                    return false;
                }
            }

            // --- validation complete: single mutation block (no partial writes) ---
            disciple.CurrentTask = taskId;
            _taskChangeLastAtUtc[disciple.DiscipleId] = _clock.UtcNow;
            RefreshRequesterActivity(trimmedRequester);

            _discipleTaskChangedPublisher.Publish(new DiscipleTaskChangedMessage
            {
                DiscipleId = disciple.DiscipleId,
                TaskId = taskId,
            });

            var who = string.IsNullOrEmpty(trimmedRequester) ? "AUTO" : trimmedRequester;
            Debug.Log($"[SectStateProvider] {who} assigned task '{taskId}' to {disciple.DiscipleId}.");
            return true;
        }

        /// <summary>Sets the mode and publishes the in-memory change message, but only on a real change.</summary>
        private void SetControlModeInternal(DiscipleState disciple, DiscipleControlMode mode)
        {
            var old = disciple.ControlMode;
            if (old == mode) return;

            disciple.ControlMode = mode;
            _controlModeChangedPublisher?.Publish(new DiscipleControlModeChangedMessage
            {
                DiscipleId = disciple.DiscipleId,
                OldMode = old,
                NewMode = mode,
            });
        }

        /// <summary>P9A — a successful manual assignment takes the disciple out of Auto (human asserted control).</summary>
        private void DisableAutoIfEngaged(DiscipleState disciple)
        {
            if (disciple.ControlMode == DiscipleControlMode.Auto)
                SetControlModeInternal(disciple, DiscipleControlMode.Manual);
        }

        // ---------- P5B — permission authority (read-only; never mutates) ----------
        // Rules, in precedence order:
        //   0. empty/invalid requester → Denied (fail closed).
        //   1. missing/contradictory membership data for a Viewer-owned disciple →
        //      ConsistencyError (NEVER automatic permission), regardless of requester.
        //   2. "SECT_MASTER": Npc (unowned) / Player (single-player identity
        //      convention) → Allowed. Viewer-owned → Allowed only when the owner has
        //      been inactive for strictly more than the protection window.
        //   3. any other requester → only its own valid Active membership.
        // Invalid/future owner timestamps are handled conservatively: the owner is
        // treated as recently active, so protection holds (fail closed).
        // There is deliberately NO AI-GM bypass: the trusted GM sends the same
        // SectMasterRequesterId and follows the identical rule.
        private TaskPermissionResult EvaluateTaskPermission(string requesterId, DiscipleState disciple)
        {
            var trimmedRequester = (requesterId ?? string.Empty).Trim();
            if (trimmedRequester.Length == 0)
                return TaskPermissionResult.Denied("Requester id is empty.");

            bool isMaster = trimmedRequester == SectMasterRequesterId;
            if (!isMaster && !IsValidIdentity(trimmedRequester))
            {
                return TaskPermissionResult.Denied(
                    $"Requester id '{trimmedRequester}' does not satisfy the identity convention " +
                    "(alpha prefix, [A-Za-z0-9_], no whitespace).");
            }

            // Membership data must be complete and agree before ANY decision about a
            // Viewer-owned disciple — a gap is a consistency error, never a grant.
            string consistencyReason = null;
            ViewerRecord ownerRecord = null;

            if (disciple.OwnerType == DiscipleOwnerType.Viewer)
            {
                if (!_state.ViewerRegistry.IsInternallyConsistent())
                {
                    consistencyReason = $"the viewer registry is internally inconsistent (disciple '{disciple.DiscipleId}').";
                }
                else
                {
                    ownerRecord = _state.ViewerRegistry.FindByBoundDisciple(disciple.DiscipleId);
                    if (ownerRecord == null)
                    {
                        consistencyReason = $"disciple '{disciple.DiscipleId}' is viewer-owned but has no active membership record.";
                    }
                    else if (ownerRecord.Status != ViewerMembershipStatus.Active)
                    {
                        consistencyReason = $"membership record for '{ownerRecord.ViewerId}' is not active.";
                    }
                    else if (ownerRecord.ViewerId != disciple.OwnerId)
                    {
                        consistencyReason = $"disciple '{disciple.DiscipleId}' owner id disagrees with its membership record.";
                    }
                    else if (!ownerRecord.IsSelfConsistent() || !ownerRecord.HasValidIdentityConvention())
                    {
                        consistencyReason = $"membership record for '{ownerRecord.ViewerId}' is not self-consistent.";
                    }
                }
            }

            if (isMaster)
            {
                if (disciple.OwnerType == DiscipleOwnerType.Viewer)
                {
                    if (consistencyReason != null)
                        return TaskPermissionResult.ConsistencyError(consistencyReason);

                    return EvaluateMasterOverride(ownerRecord, disciple);
                }

                // Npc = unowned (SectMaster may control) and Player = the existing
                // single-player identity convention (SectMaster may control).
                return TaskPermissionResult.Allow();
            }

            // Non-master: only the disciple's own valid Active membership.
            if (disciple.OwnerType != DiscipleOwnerType.Viewer)
            {
                return TaskPermissionResult.Denied(
                    $"'{trimmedRequester}' is not allowed to assign tasks to '{disciple.DiscipleId}'.");
            }

            if (consistencyReason != null)
                return TaskPermissionResult.ConsistencyError(consistencyReason);

            if (ownerRecord.ViewerId != trimmedRequester)
            {
                return TaskPermissionResult.Denied(
                    $"'{trimmedRequester}' is not allowed to assign tasks to '{disciple.DiscipleId}' " +
                    $"(owned by '{disciple.OwnerId}').");
            }

            var requesterRecord = _state.ViewerRegistry.Find(trimmedRequester);
            if (requesterRecord == null)
            {
                return TaskPermissionResult.Denied(
                    $"'{trimmedRequester}' is not allowed to assign tasks to '{disciple.DiscipleId}' " +
                    "(no active membership).");
            }
            if (!requesterRecord.IsSelfConsistent() || !requesterRecord.HasValidIdentityConvention())
            {
                return TaskPermissionResult.ConsistencyError(
                    $"membership record for '{trimmedRequester}' is not self-consistent.");
            }
            if (requesterRecord.Status != ViewerMembershipStatus.Active)
            {
                return TaskPermissionResult.Denied(
                    $"'{trimmedRequester}' is not allowed to assign tasks to '{disciple.DiscipleId}' " +
                    $"(membership is {requesterRecord.Status}).");
            }

            return TaskPermissionResult.Allow();
        }

        /// <summary>
        /// SectMaster override against a Viewer-owned disciple. Strictly-greater-than
        /// window on real time; an invalid (MinValue/non-UTC) or future timestamp is
        /// treated as "recently active" so protection holds rather than silently lapsing.
        /// </summary>
        private TaskPermissionResult EvaluateMasterOverride(ViewerRecord ownerRecord, DiscipleState disciple)
        {
            var now = _clock.UtcNow;
            var last = ownerRecord.LastActiveAtUtc;

            if (last == DateTime.MinValue || last.Kind != DateTimeKind.Utc || last > now)
            {
                return TaskPermissionResult.Denied(
                        $"Viewer '{ownerRecord.ViewerId}' has an invalid or future activity timestamp — " +
                        "treated as recently active, so the owner remains protected.")
                    .WithProtection((float)ViewerProtectionWindowSeconds);
            }

            var elapsed = (now - last).TotalSeconds;
            if (elapsed > ViewerProtectionWindowSeconds)
                return TaskPermissionResult.Allow();

            return TaskPermissionResult.Denied(
                    $"Viewer '{ownerRecord.ViewerId}' is still protected " +
                    $"({elapsed:0}s of {ViewerProtectionWindowSeconds:0}s inactivity; override needs strictly more).")
                .WithProtection((float)(ViewerProtectionWindowSeconds - elapsed));
        }

        /// <summary>Identity convention shared with TrySetDiscipleOwner (synthetic local ids, never real Twitch ids).</summary>
        private static bool IsValidIdentity(string id)
        {
            return !string.IsNullOrEmpty(id) &&
                   System.Text.RegularExpressions.Regex.IsMatch(id, OwnerIdPattern);
        }

        // ---------- P5B — read-only permission query ----------
        public TaskPermissionResult CheckTaskPermission(string requesterId, string discipleId, string taskId)
        {
            var disciple = FindDisciple(discipleId);
            if (disciple == null)
                return TaskPermissionResult.Denied($"No disciple with id: {discipleId}");

            var result = EvaluateTaskPermission(requesterId, disciple);
            return result.WithNoOp(disciple.CurrentTask == taskId);
        }

        /// <summary>
        /// P5B — refresh ONLY the requester's own active membership activity. Never
        /// creates a record and never touches another viewer's record: an invalid or
        /// unauthorized command must not extend someone else's protection window.
        /// SectMaster has no membership record, so this is a no-op for the master.
        /// </summary>
        private void RefreshRequesterActivity(string trimmedRequesterId)
        {
            if (string.IsNullOrEmpty(trimmedRequesterId) || trimmedRequesterId == SectMasterRequesterId) return;

            var record = _state.ViewerRegistry.Find(trimmedRequesterId);
            if (record == null || record.Status != ViewerMembershipStatus.Active) return;
            record.LastActiveAtUtc = _clock.UtcNow;
        }

        // ---------- P5B — viewer membership persistence (slice export/import) ----------
        // The registry and the disciples' ownership are exported/imported TOGETHER:
        // restoring only one half would break invariants #1/#2/#3 (active record ⇔
        // matching Viewer-owned disciple). Copies are made on export so a caller
        // cannot mutate live state by holding the returned object.

        public SectViewerMembershipSave ExportViewerMembership()
        {
            var save = new SectViewerMembershipSave
            {
                Version = SectViewerMembershipSave.CurrentVersion,
                SavedAtUtc = _clock.UtcNow,
            };

            for (int i = 0; i < _state.ViewerRegistry.Records.Count; i++)
            {
                var r = _state.ViewerRegistry.Records[i];
                if (r == null) continue;
                save.Records.Add(new ViewerRecord
                {
                    ViewerId = r.ViewerId,
                    DisplayName = r.DisplayName,
                    BoundDiscipleId = r.BoundDiscipleId,
                    Status = r.Status,
                    LastActiveAtUtc = r.LastActiveAtUtc,
                });
            }

            for (int i = 0; i < _state.ViewerRegistry.PendingApplications.Count; i++)
            {
                var p = _state.ViewerRegistry.PendingApplications[i];
                if (p == null) continue;
                save.PendingApplications.Add(new PendingViewerApplication
                {
                    ViewerId = p.ViewerId,
                    DisplayName = p.DisplayName,
                    AppliedAtUtc = p.AppliedAtUtc,
                });
            }

            for (int i = 0; i < _state.Disciples.Count; i++)
            {
                var d = _state.Disciples[i];
                if (d == null) continue;
                save.OwnerByDisciple.Add(new SectSavedOwnership
                {
                    DiscipleId = d.DiscipleId,
                    OwnerType = d.OwnerType,
                    OwnerId = d.OwnerId ?? string.Empty,
                });
            }

            return save;
        }

        public bool TryImportViewerMembership(SectViewerMembershipSave save, out string failReason)
        {
            failReason = string.Empty;

            // 1. shape/version — an unknown shape is ignored, never guessed at
            if (save == null)
            {
                failReason = "Save data is null.";
                return false;
            }
            if (save.Version != SectViewerMembershipSave.CurrentVersion)
            {
                failReason = $"Unsupported membership save version: {save.Version} (expected {SectViewerMembershipSave.CurrentVersion}).";
                return false;
            }

            // 2. every saved ownership row must reference a disciple we actually have,
            //    and its type must be a defined enum (fail-closed on corrupt data)
            var ownershipById = new Dictionary<string, SectSavedOwnership>();
            var saved = save.OwnerByDisciple ?? new List<SectSavedOwnership>();
            for (int i = 0; i < saved.Count; i++)
            {
                var s = saved[i];
                if (s == null || string.IsNullOrEmpty(s.DiscipleId))
                {
                    failReason = "Save data contains an ownership row without a disciple id.";
                    return false;
                }
                if (!Enum.IsDefined(typeof(DiscipleOwnerType), s.OwnerType))
                {
                    failReason = $"Save data contains an undefined DiscipleOwnerType for '{s.DiscipleId}'.";
                    return false;
                }
                if (FindDisciple(s.DiscipleId) == null)
                {
                    failReason = $"Save data references an unknown disciple: '{s.DiscipleId}'.";
                    return false;
                }
                ownershipById[s.DiscipleId] = s;
            }

            // 3. the registry itself must be internally consistent
            var candidate = new SectViewerRegistry
            {
                Records = save.Records != null ? new List<ViewerRecord>(save.Records) : new List<ViewerRecord>(),
                PendingApplications = save.PendingApplications != null
                    ? new List<PendingViewerApplication>(save.PendingApplications)
                    : new List<PendingViewerApplication>(),
            };
            if (!candidate.IsInternallyConsistent())
            {
                failReason = "Save data has an internally inconsistent viewer registry.";
                return false;
            }

            // 4. registry ⇄ ownership must agree with the SAVED ownership, not the
            //    current one — otherwise ownership would be restored from a different
            //    moment than the registry and the pair could silently disagree.
            var projected = new List<DiscipleState>(_state.Disciples.Count);
            for (int i = 0; i < _state.Disciples.Count; i++)
            {
                var d = _state.Disciples[i];
                if (d == null) continue;

                var ownerType = d.OwnerType;
                var ownerId = d.OwnerId ?? string.Empty;
                if (ownershipById.TryGetValue(d.DiscipleId, out var row))
                {
                    ownerType = row.OwnerType;
                    ownerId = row.OwnerType == DiscipleOwnerType.Npc ? string.Empty : (row.OwnerId ?? string.Empty);
                }

                projected.Add(new DiscipleState
                {
                    DiscipleId = d.DiscipleId,
                    OwnerType = ownerType,
                    OwnerId = ownerId,
                });
            }

            if (!candidate.VerifyAgainst(projected))
            {
                failReason = "Save data's viewer registry does not agree with its saved ownership.";
                return false;
            }

            // --- validation complete: single mutation block (no partial restore) ---
            _state.ViewerRegistry.Records.Clear();
            _state.ViewerRegistry.Records.AddRange(candidate.Records);
            _state.ViewerRegistry.PendingApplications.Clear();
            _state.ViewerRegistry.PendingApplications.AddRange(candidate.PendingApplications);

            for (int i = 0; i < _state.Disciples.Count; i++)
            {
                var d = _state.Disciples[i];
                if (d == null || !ownershipById.TryGetValue(d.DiscipleId, out var row)) continue;
                d.OwnerType = row.OwnerType;
                d.OwnerId = row.OwnerType == DiscipleOwnerType.Npc ? string.Empty : (row.OwnerId ?? string.Empty);

                // P9A — a restored non-Npc owner is not eligible for Auto; drop the
                // mode rather than leave a Player/Viewer-owned disciple brain-controlled.
                if (d.OwnerType != DiscipleOwnerType.Npc)
                    d.ControlMode = DiscipleControlMode.Manual;
            }

            Debug.Log($"[SectStateProvider] Imported viewer membership save: " +
                      $"{_state.ViewerRegistry.Records.Count} record(s), {saved.Count} ownership row(s).");
            return true;
        }

        // ---------- P11A — full-session snapshot (capture / validate / apply) ----------
        // Unity owns gameplay state, so the authority over what counts as a valid
        // candidate and how live state is replaced stays HERE. SessionSnapshotService
        // orchestrates WHEN a restore happens; this class owns WHAT is valid and the
        // atomic swap. Everything below works on detached DTOs only.

        /// <summary>
        /// P11A — deep, detached copy of the authoritative economy + work
        /// accumulators. Simulation speed / pending decision live in TimeSystem and
        /// are merged by <c>SessionSnapshotService</c>. Read-only: live state is
        /// never modified, and mutating the returned snapshot cannot touch it.
        /// </summary>
        public SectSessionSnapshot CaptureSessionSnapshot()
        {
            var snapshot = new SectSessionSnapshot
            {
                Economy = CloneEconomy(_state),
            };

            foreach (var pair in _gatherAccumulators)
                snapshot.GatherAccumulators[pair.Key] = pair.Value;
            foreach (var pair in _craftProgress)
                snapshot.CraftProgress[pair.Key] = pair.Value;

            return snapshot;
        }

        /// <summary>
        /// P11A — validate a candidate snapshot BEFORE anything live is replaced. A
        /// failure leaves the running session untouched (fail closed). Checks:
        /// unique non-empty ids, finite numeric values, defined enums, ownership ⇄
        /// membership agreement, avatar presence, known task ids, known building
        /// defs, valid rotations, and in-grid non-overlapping placement. Never mutates
        /// the candidate or live state.
        /// </summary>
        public bool TryValidateSessionSnapshot(SectSessionSnapshot snapshot, out string failReason)
        {
            failReason = string.Empty;

            if (snapshot == null) { failReason = "Session snapshot is null."; return false; }

            var economy = snapshot.Economy;
            if (economy == null) { failReason = "Session snapshot has no economy state."; return false; }

            // 1. work accumulators — finite and non-negative (a negative float would
            //    be a corrupted carry-over, not a real sub-unit remainder).
            if (!ValidateAccumulators(snapshot.GatherAccumulators, "gather accumulator", out failReason)) return false;
            if (!ValidateAccumulators(snapshot.CraftProgress, "craft progress", out failReason)) return false;

            // 2. roster
            var disciples = economy.Disciples;
            if (disciples == null) { failReason = "Session snapshot has no disciple roster."; return false; }

            var discipleIds = new HashSet<string>();
            for (int i = 0; i < disciples.Count; i++)
            {
                var d = disciples[i];
                if (d == null) { failReason = $"Session snapshot has a null disciple at index {i}."; return false; }
                if (string.IsNullOrEmpty(d.DiscipleId))
                { failReason = $"Session snapshot has a disciple without an id at index {i}."; return false; }
                if (!discipleIds.Add(d.DiscipleId))
                { failReason = $"Session snapshot has a duplicate disciple id: '{d.DiscipleId}'."; return false; }

                if (!Enum.IsDefined(typeof(DiscipleRank), d.Rank))
                { failReason = $"Disciple '{d.DiscipleId}' has an undefined rank value."; return false; }
                if (!Enum.IsDefined(typeof(DiscipleSex), d.Sex))
                { failReason = $"Disciple '{d.DiscipleId}' has an undefined sex value."; return false; }
                if (!Enum.IsDefined(typeof(ChibiBackend), d.ChibiBackend))
                { failReason = $"Disciple '{d.DiscipleId}' has an undefined chibi backend value."; return false; }
                if (!Enum.IsDefined(typeof(DiscipleControlMode), d.ControlMode))
                { failReason = $"Disciple '{d.DiscipleId}' has an undefined control mode value."; return false; }

                // ownership
                if (!Enum.IsDefined(typeof(DiscipleOwnerType), d.OwnerType))
                { failReason = $"Disciple '{d.DiscipleId}' has an undefined owner type value."; return false; }
                if (d.OwnerType == DiscipleOwnerType.Npc)
                {
                    if (!string.IsNullOrEmpty(d.OwnerId))
                    { failReason = $"Npc-owned disciple '{d.DiscipleId}' must carry an empty OwnerId."; return false; }
                }
                else if (!IsValidIdentity(d.OwnerId))
                {
                    failReason = $"Disciple '{d.DiscipleId}' has an invalid OwnerId '{d.OwnerId}'.";
                    return false;
                }

                // autonomy only for Npc-owned disciples (same rule the mutation path enforces)
                if (d.ControlMode == DiscipleControlMode.Auto && d.OwnerType != DiscipleOwnerType.Npc)
                {
                    failReason = $"Disciple '{d.DiscipleId}' is {d.OwnerType}-owned but in Auto mode.";
                    return false;
                }

                // task — a live directive; an unknown id fails rather than silently
                // keeping an unexecutable task or substituting another one.
                if (!string.IsNullOrEmpty(d.CurrentTask) && !KnownTasks.Contains(d.CurrentTask))
                {
                    failReason = $"Disciple '{d.DiscipleId}' references an unknown task id '{d.CurrentTask}'.";
                    return false;
                }

                if (d.Wallet == null)
                { failReason = $"Disciple '{d.DiscipleId}' has no wallet."; return false; }
                if (d.Avatar == null)
                { failReason = $"Disciple '{d.DiscipleId}' has no avatar appearance."; return false; }

                if (!ValidateAttributeBlock(d, out failReason)) return false;

                if (d.PersonalInventory == null)
                { failReason = $"Disciple '{d.DiscipleId}' has a null personal inventory."; return false; }
                if (!ValidateItems(d.PersonalInventory, $"disciple '{d.DiscipleId}' inventory", out failReason)) return false;
            }

            // 3. stockpile
            if (economy.Stockpile == null) { failReason = "Session snapshot has no stockpile."; return false; }
            var raw = economy.Stockpile.RawResources;
            if (raw == null) { failReason = "Session snapshot has a null raw-resource dictionary."; return false; }
            foreach (var pair in raw)
            {
                if (string.IsNullOrEmpty(pair.Key))
                { failReason = "Session snapshot has a raw resource without an id."; return false; }
                if (pair.Value < 0)
                { failReason = $"Session snapshot has a negative '{pair.Key}' amount ({pair.Value})."; return false; }
            }
            if (!ValidateItems(economy.Stockpile.CraftedGoods, "sect stockpile goods", out failReason)) return false;

            // 4. buildings — unique ids, known defs, valid rotation, in-grid + non-overlapping.
            if (!ValidateBuildings(economy.PlacedBuildings, out failReason)) return false;

            // 5. membership ⇄ ownership must agree in the SAME snapshot.
            if (economy.ViewerRegistry == null)
            { failReason = "Session snapshot has no viewer registry."; return false; }
            if (!economy.ViewerRegistry.IsInternallyConsistent())
            { failReason = "Session snapshot has an internally inconsistent viewer registry."; return false; }
            if (!economy.ViewerRegistry.VerifyAgainst(economy.Disciples))
            { failReason = "Session snapshot's viewer registry does not agree with its disciple ownership."; return false; }

            return true;
        }

        /// <summary>
        /// P11A — replace live economy + work accumulators from a VALIDATED snapshot.
        /// The candidate is deep-cloned on the way in, so the caller's object never
        /// aliases live state and can be safely reused or mutated afterwards. Never
        /// fails after validation (the two steps are split for that reason): if this
        /// is called on an unvalidated snapshot it simply returns false and leaves
        /// live state untouched.
        /// </summary>
        public bool TryApplySessionSnapshot(SectSessionSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Economy == null) return false;

            var candidate = CloneEconomy(snapshot.Economy);
            SectEconomyState.NormalizeDisciples(candidate); // P10A repair on the detached copy only

            // --- commit: economy (single assignment per field; no partial state) ---
            _state.Disciples = candidate.Disciples;
            _state.Stockpile.RawResources = candidate.Stockpile.RawResources;
            _state.Stockpile.CraftedGoods = candidate.Stockpile.CraftedGoods;
            _state.PlacedBuildings = candidate.PlacedBuildings;
            _state.ViewerRegistry = candidate.ViewerRegistry;

            // --- commit: work accumulators ---
            _gatherAccumulators.Clear();
            if (snapshot.GatherAccumulators != null)
            {
                foreach (var pair in snapshot.GatherAccumulators)
                    if (!string.IsNullOrEmpty(pair.Key) && IsFinite(pair.Value) && pair.Value >= 0f)
                        _gatherAccumulators[pair.Key] = pair.Value;
            }

            _craftProgress.Clear();
            if (snapshot.CraftProgress != null)
            {
                foreach (var pair in snapshot.CraftProgress)
                    if (!string.IsNullOrEmpty(pair.Key) && IsFinite(pair.Value) && pair.Value >= 0f)
                        _craftProgress[pair.Key] = pair.Value;
            }

            // --- intentional resets (see SectSessionSnapshot.cs classification) ---
            _taskChangeLastAtUtc.Clear(); // real-UTC task-change cooldown is session-scoped
            _workTick.Clear();            // per-tick scratch — must never carry a stale outcome
            _workTickRecordCount = 0;

            return true;
        }

        // ---- P11A validation helpers ----

        private static bool ValidateAccumulators(Dictionary<string, float> map, string label, out string failReason)
        {
            failReason = string.Empty;
            if (map == null) return true; // missing map == empty

            foreach (var pair in map)
            {
                if (string.IsNullOrEmpty(pair.Key))
                { failReason = $"Session snapshot has a {label} with an empty key."; return false; }
                if (!IsFinite(pair.Value) || pair.Value < 0f)
                { failReason = $"Session snapshot {label} for '{pair.Key}' is not a finite non-negative value."; return false; }
            }
            return true;
        }

        private static bool ValidateAttributeBlock(DiscipleState disciple, out string failReason)
        {
            failReason = string.Empty;
            var attributes = disciple.Attributes;
            if (attributes == null)
            { failReason = $"Disciple '{disciple.DiscipleId}' has no attribute block."; return false; }
            if (!IsFinite(attributes.Stamina))
            { failReason = $"Disciple '{disciple.DiscipleId}' has a non-finite stamina value."; return false; }

            if (attributes.SkillXp != null)
            {
                foreach (var pair in attributes.SkillXp)
                {
                    if (string.IsNullOrEmpty(pair.Key))
                    { failReason = $"Disciple '{disciple.DiscipleId}' has a skill entry with an empty category."; return false; }
                    if (!IsFinite(pair.Value))
                    { failReason = $"Disciple '{disciple.DiscipleId}' has a non-finite skill XP value for '{pair.Key}'."; return false; }
                }
            }
            return true;
        }

        private static bool ValidateItems(List<InventoryItem> items, string context, out string failReason)
        {
            failReason = string.Empty;
            if (items == null) return true;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null) { failReason = $"Session snapshot has a null item in {context}."; return false; }
                if (string.IsNullOrEmpty(item.ItemDefId))
                { failReason = $"Session snapshot has an item without a def id in {context}."; return false; }
                if (item.Quantity <= 0)
                { failReason = $"Session snapshot item '{item.ItemDefId}' in {context} has a non-positive quantity."; return false; }
                if (item.Grade < 1 || item.Grade > 5)
                { failReason = $"Session snapshot item '{item.ItemDefId}' in {context} has an out-of-range grade {item.Grade}."; return false; }
                if (!Enum.IsDefined(typeof(OwnerScope), item.OwnerScope))
                { failReason = $"Session snapshot item '{item.ItemDefId}' in {context} has an undefined owner scope."; return false; }
            }
            return true;
        }

        private bool ValidateBuildings(List<PlacedBuildingState> buildings, out string failReason)
        {
            failReason = string.Empty;
            if (buildings == null) return true; // missing list == none placed

            // Same grid geometry production uses (BuildingInstaller factory); the
            // land mask is deliberately NOT applied here — mask is scene-derived, and
            // placement validity is bounds + overlap + known def + rotation.
            var grid = new BuildingGrid(
                GridOverlayRenderer.GridExtent, GridOverlayRenderer.GridExtent,
                -GridOverlayRenderer.GridExtent / 2, -GridOverlayRenderer.GridExtent / 2);

            var seen = new HashSet<string>();
            for (int i = 0; i < buildings.Count; i++)
            {
                var pb = buildings[i];
                if (pb == null) { failReason = $"Session snapshot has a null building at index {i}."; return false; }
                if (string.IsNullOrEmpty(pb.InstanceId))
                { failReason = $"Session snapshot has a building without an instance id at index {i}."; return false; }
                if (!seen.Add(pb.InstanceId))
                { failReason = $"Session snapshot has a duplicate building instance id: '{pb.InstanceId}'."; return false; }
                if (!BuildingGrid.IsValidRotation(pb.Rotation))
                { failReason = $"Building '{pb.InstanceId}' has an invalid rotation {pb.Rotation}."; return false; }

                var def = _buildingDefPool != null ? _buildingDefPool.GetById(pb.DefId) : null;
                if (def == null)
                { failReason = $"Building '{pb.InstanceId}' references an unknown def id '{pb.DefId}'."; return false; }

                if (!grid.CanPlace(pb.GridX, pb.GridZ, def.GridWidth, def.GridHeight, pb.Rotation))
                {
                    failReason = $"Building '{pb.InstanceId}' cannot be placed at ({pb.GridX},{pb.GridZ}) " +
                                 $"rot={pb.Rotation} - outside the sect grid or overlapping another building.";
                    return false;
                }
                grid.Occupy(pb.InstanceId, pb.GridX, pb.GridZ, def.GridWidth, def.GridHeight, pb.Rotation);
            }
            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        // ---- P11A detached deep-copy helpers ----

        private static SectEconomyState CloneEconomy(SectEconomyState source)
        {
            var copy = new SectEconomyState();
            if (source == null) return copy;

            if (source.Disciples != null)
            {
                for (int i = 0; i < source.Disciples.Count; i++)
                    copy.Disciples.Add(CloneDisciple(source.Disciples[i]));
            }

            if (source.Stockpile != null)
            {
                copy.Stockpile.RawResources = source.Stockpile.RawResources != null
                    ? new Dictionary<string, int>(source.Stockpile.RawResources)
                    : new Dictionary<string, int>();

                if (source.Stockpile.CraftedGoods != null)
                {
                    for (int i = 0; i < source.Stockpile.CraftedGoods.Count; i++)
                        copy.Stockpile.CraftedGoods.Add(CloneItem(source.Stockpile.CraftedGoods[i]));
                }
            }

            if (source.PlacedBuildings != null)
            {
                for (int i = 0; i < source.PlacedBuildings.Count; i++)
                {
                    var b = source.PlacedBuildings[i];
                    copy.PlacedBuildings.Add(b == null ? null : new PlacedBuildingState
                    {
                        InstanceId = b.InstanceId,
                        DefId = b.DefId,
                        GridX = b.GridX,
                        GridZ = b.GridZ,
                        Rotation = b.Rotation,
                    });
                }
            }

            if (source.ViewerRegistry != null)
            {
                if (source.ViewerRegistry.Records != null)
                {
                    for (int i = 0; i < source.ViewerRegistry.Records.Count; i++)
                    {
                        var r = source.ViewerRegistry.Records[i];
                        copy.ViewerRegistry.Records.Add(r == null ? null : new ViewerRecord
                        {
                            ViewerId = r.ViewerId,
                            DisplayName = r.DisplayName,
                            BoundDiscipleId = r.BoundDiscipleId,
                            Status = r.Status,
                            LastActiveAtUtc = r.LastActiveAtUtc,
                        });
                    }
                }
                if (source.ViewerRegistry.PendingApplications != null)
                {
                    for (int i = 0; i < source.ViewerRegistry.PendingApplications.Count; i++)
                    {
                        var p = source.ViewerRegistry.PendingApplications[i];
                        copy.ViewerRegistry.PendingApplications.Add(p == null ? null : new PendingViewerApplication
                        {
                            ViewerId = p.ViewerId,
                            DisplayName = p.DisplayName,
                            AppliedAtUtc = p.AppliedAtUtc,
                        });
                    }
                }
            }

            return copy;
        }

        private static DiscipleState CloneDisciple(DiscipleState source)
        {
            if (source == null) return null;

            var copy = new DiscipleState
            {
                DiscipleId = source.DiscipleId,
                DisplayName = source.DisplayName,
                Rank = source.Rank,
                Sex = source.Sex,
                ChibiBackend = source.ChibiBackend,
                OwnerType = source.OwnerType,
                OwnerId = source.OwnerId,
                ControlMode = source.ControlMode,
                CurrentTask = source.CurrentTask,
                Wallet = new CurrencyWallet
                {
                    SpiritStones = source.Wallet != null ? source.Wallet.SpiritStones : 0L,
                    Contribution = source.Wallet != null ? source.Wallet.Contribution : 0L,
                },
                Avatar = source.Avatar != null ? source.Avatar.Clone() : new AvatarAppearance(),
                Attributes = source.Attributes != null ? source.Attributes.Clone() : new DiscipleAttributes(),
            };

            if (source.PersonalInventory != null)
            {
                for (int i = 0; i < source.PersonalInventory.Count; i++)
                    copy.PersonalInventory.Add(CloneItem(source.PersonalInventory[i]));
            }

            return copy;
        }

        private static InventoryItem CloneItem(InventoryItem source)
        {
            if (source == null) return null;
            return new InventoryItem
            {
                ItemDefId = source.ItemDefId,
                Quantity = source.Quantity,
                Grade = source.Grade,
                OwnerScope = source.OwnerScope,
            };
        }

        // ---------- Task building-requirement gate (§6 addendum, Phase 2 static-data step) ----------
        // Public so UI / ghost-preview callers (and TryAssignTask) can check the
        // requirement without permission checks or mutation. Deliberately plain
        // C# — no UnityEngine API, no Stockpile touch, no TaskDef pipeline: the
        // dictionary above IS the source of truth, PlacedBuildings IS the state.
        public bool IsTaskAvailable(string taskId, out string failReason)
        {
            failReason = string.Empty;

            if (string.IsNullOrEmpty(taskId) || !KnownTasks.Contains(taskId))
            {
                failReason = $"Unknown task: '{taskId}'.";
                return false;
            }

            string requiredBuilding;
            if (!TaskRequiredBuilding.TryGetValue(taskId, out requiredBuilding))
                return true; // no requirement — always available once known

            for (int i = 0; i < _state.PlacedBuildings.Count; i++)
            {
                var placed = _state.PlacedBuildings[i];
                if (placed != null && placed.DefId == requiredBuilding)
                    return true;
            }

            failReason = $"Task '{taskId}' requires an existing '{requiredBuilding}' building.";
            return false;
        }

        /// <summary>
        /// §7 promotion/demotion path — mutate the ENTITLEMENT in state and publish
        /// (in-memory MessagePipe only, never interprocess). Whether the visual actually
        /// re-renders as Spine is decided later by VisualTierPolicy (entitlement ∩ budget),
        /// so a demote-to-Sprite command under a tight budget is a no-op on screen but
        /// still updates state.
        /// </summary>
        public bool TrySetChibiBackend(string discipleId, ChibiBackend backend, out string failReason)
        {
            failReason = string.Empty;

            var disciple = _state.Disciples.FirstOrDefault(d => d.DiscipleId == discipleId);
            if (disciple == null) { failReason = $"No disciple with id: {discipleId}"; return false; }

            ChibiBackend old = disciple.ChibiBackend;
            if (old == backend) return true; // idempotent — no message spam

            disciple.ChibiBackend = backend;
            _chibiBackendPublisher.Publish(new DiscipleChibiBackendChangedMessage
            {
                DiscipleId = discipleId,
                Old        = old,
                New        = backend
            });
            return true;
        }

        // ---------- Building Phase 1 (grid placement — building-system.md §3.2) ----------
        // Player-only in Phase 1 (Q3 default): no MCP tool exposes this — the call
        // path is PlacementController (UI) only. Validation order per §3.2:
        // def lookup → occupancy (ขอบเขต + ซ้อนทับ) → cost. After the last check
        // there is NO failure path — resource deduction, state append, occupancy
        // mark, and publish all happen together (no partial mutation).

        /// <summary>Ghost preview check (read-only) — occupancy อยู่ฝั่ง BuildingGrid.</summary>
        public bool CanAffordBuilding(BuildingDef def)
        {
            if (def == null) return false;

            var cost = def.GetCost();
            foreach (var (resource, amount) in cost)
            {
                // invalid cost entry = ฟรี (ไม่หัก) — def data ผิดไม่ควรทำให้วางไม่ได้
                if (string.IsNullOrEmpty(resource) || amount <= 0) continue;
                if (!_state.Stockpile.RawResources.TryGetValue(resource, out var have) || have < amount)
                    return false;
            }
            return true;
        }

        public bool TryPlaceBuilding(string defId, int gridX, int gridZ, int rotation,
                                     BuildingGrid grid, out string failReason,
                                     out PlacedBuildingState placed)
        {
            failReason = string.Empty;
            placed = null;

            // 1. def lookup
            if (string.IsNullOrEmpty(defId))
            {
                failReason = "Building def id is empty.";
                return false;
            }
            var def = _buildingDefPool != null ? _buildingDefPool.GetById(defId) : null;
            if (def == null)
            {
                failReason = $"Unknown building def: '{defId}'.";
                return false;
            }
            if (grid == null)
            {
                failReason = "BuildingGrid is not available.";
                return false;
            }

            // 2. occupancy (ขอบเขต + ซ้อนทับ) — re-validate ที่นี่เสมอ ไม่เชื่อ caller
            if (!grid.CanPlace(gridX, gridZ, def.GridWidth, def.GridHeight, rotation))
            {
                failReason = $"Cannot place '{defId}' at ({gridX},{gridZ}) rot={rotation} - " +
                             "outside the sect grid or overlapping an existing building.";
                return false;
            }

            // 3. cost — จ่ายได้ครบเท่านั้น (all-or-nothing, กฎเดียวกับ crafting)
            if (!CanAffordBuilding(def))
            {
                var cost = def.GetCost();
                var missing = new List<string>();
                foreach (var (resource, amount) in cost)
                {
                    if (string.IsNullOrEmpty(resource) || amount <= 0) continue;
                    _state.Stockpile.RawResources.TryGetValue(resource, out var have);
                    if (have < amount) missing.Add($"{resource} ({have}/{amount})");
                }
                failReason = $"Not enough resources to build '{def.Id}': {string.Join(", ", missing)}.";
                return false;
            }

            // --- หลังจุดนี้ไม่มี failure path: mutation เริ่ม (no partial mutation) ---
            var instanceId = NextBuildingInstanceId();

            grid.Occupy(instanceId, gridX, gridZ, def.GridWidth, def.GridHeight, rotation);

            foreach (var (resource, amount) in def.GetCost())
            {
                // เงื่อนไข skip เดียวกับ CanAffordBuilding — หักเท่าที่เพิ่งเช็ค
                if (string.IsNullOrEmpty(resource) || amount <= 0) continue;
                AdjustAndNotify(_state.Stockpile.RawResources, resource, -amount);
            }

            placed = new PlacedBuildingState
            {
                InstanceId = instanceId,
                DefId = def.Id,
                GridX = gridX,
                GridZ = gridZ,
                Rotation = rotation,
            };
            _state.PlacedBuildings.Add(placed);

            _buildingPlacedPublisher?.Publish(new BuildingPlacedMessage
            {
                InstanceId = placed.InstanceId,
                DefId = placed.DefId,
                GridX = gridX,
                GridZ = gridZ,
                Rotation = rotation,
            });

            Debug.Log($"[SectStateProvider] Placed building '{def.Id}' as {placed.InstanceId} " +
                      $"at ({gridX},{gridZ}) rot={rotation}.");
            return true;
        }

        /// <summary>
        /// instanceId = "b###" — max numeric suffix + 1 (ไม่ใช้ Count เพราะจะชน
        /// เมื่อ Phase ทุบอาคารมาถึง; scan เร็วพอที่ roster อาคารจะไม่ใหญ่).
        /// Caller ต้องเรียกครั้งเดียวต่อการวางแล้วใช้ค่าเดิมทั้ง occupancy และ state —
        /// occupancy id กับ state id ต้องตรงกัน ไม่งั้น rebuild ตอน Start หา cell ไม่เจอ.
        /// </summary>
        private string NextBuildingInstanceId()
        {
            int next = 1;
            for (int i = 0; i < _state.PlacedBuildings.Count; i++)
            {
                var id = _state.PlacedBuildings[i]?.InstanceId;
                if (!string.IsNullOrEmpty(id) && id.Length > 1 && id[0] == 'b' &&
                    int.TryParse(id.Substring(1), out var n) && n >= next)
                {
                    next = n + 1;
                }
            }
            return $"b{next:000}";
        }

        // Placeholder consequence rules keyed by event id - not real game
        // balance, just enough to prove ExecuteDecision actually mutates
        // state instead of only logging. Real weighted/authored
        // consequences belong with the EventData ScriptableObject work
        // (deferred - see project_summary.md).
        public void ApplyDecisionConsequence(string eventId, string choiceId)
        {
            var resources = _state.Stockpile.RawResources;

            switch (eventId)
            {
                case "bandit_raid_001":
                    AdjustAndNotify(resources, "provisions", -20);
                    break;

                case "herb_garden_bloom":
                    AdjustAndNotify(resources, "herb", 30);
                    break;

                case "wandering_merchant":
                    AdjustAndNotify(resources, "ore", -20);
                    AdjustAndNotify(resources, "provisions", 40);
                    break;

                case "new_disciple_applicant":
                    // E2-lite: explicit choice id, not a substring match - the
                    // choice is already validated against the pending choices.
                    if (choiceId == "accept")
                    {
                        RecruitOuterDisciple();
                    }
                    else
                    {
                        Debug.Log($"[SectStateProvider] Applicant rejected (choice={choiceId}) - no disciple added.");
                    }
                    return; // recruiting already logged its own outcome

                default:
                    Debug.LogWarning($"[SectStateProvider] No consequence rule for event '{eventId}' - state unchanged.");
                    return;
            }

            Debug.Log($"[SectStateProvider] Applied consequence for event={eventId} choice={choiceId}. " +
                      $"Stockpile now: {string.Join(", ", resources.Keys.Select(k => $"{k}={resources[k]}"))}");
        }

        private static void Adjust(Dictionary<string, int> resources, string key, int delta)
        {
            var current = resources.TryGetValue(key, out var value) ? value : 0;
            resources[key] = Math.Max(0, current + delta);
        }

        // Same as Adjust, but also publishes SectResourceChangedMessage so
        // UI (WalletHudPresenter) picks up the change - single choke
        // point instead of scattering publish calls at every mutation site.
        // Publishes the *actual* applied delta, not the requested one - the
        // two can differ when Adjust clamps at 0 (e.g. requesting -50 on a
        // stock of 20 only actually removes 20).
        private void AdjustAndNotify(Dictionary<string, int> resources, string key, int delta)
        {
            var before = resources.TryGetValue(key, out var b) ? b : 0;
            Adjust(resources, key, delta);
            var after = resources.TryGetValue(key, out var a) ? a : 0;

            _resourceChangedPublisher.Publish(new SectResourceChangedMessage
            {
                ResourceId = key,
                Delta = after - before,
                NewTotal = after,
            });
        }

        // Merges into an existing stack (same item + grade) in the sect
        // warehouse rather than always appending a new entry.
        private void AddToStockpileGoods(InventoryItem item)
        {
            var existing = _state.Stockpile.CraftedGoods
                .FirstOrDefault(g => g.ItemDefId == item.ItemDefId && g.Grade == item.Grade && g.OwnerScope == item.OwnerScope);

            if (existing != null)
            {
                existing.Quantity += item.Quantity;
            }
            else
            {
                _state.Stockpile.CraftedGoods.Add(item);
            }
        }

        // All-or-nothing: only subtracts if every cost can be fully paid,
        // so a craft never partially consumes ingredients it can't finish.
        private static bool TryConsume(Dictionary<string, int> resources, Dictionary<string, int> costs)
        {
            foreach (var (resource, amount) in costs)
            {
                if (!resources.TryGetValue(resource, out var have) || have < amount) return false;
            }

            foreach (var (resource, amount) in costs)
            {
                resources[resource] -= amount;
            }

            return true;
        }

        // Placeholder pricing - grade * flat rate per unit. Not balanced,
        // just enough to prove the store loop end to end. Revisit once
        // there's a real pricing model (rarity, sect reputation, etc.).
        private const int ContributionPricePerGrade = 50;

        // A disciple buying an item from the sect stockpile (CraftedGoods)
        // with their own contribution. Deducts contribution from the
        // disciple's wallet, moves the item out of the shared stockpile
        // into that disciple's personal inventory. Called from
        // PurchaseItemHandler (interprocess request-response).
        public PurchaseItemResponse TryPurchaseItem(string discipleId, string itemDefId, int grade, int quantity)
        {
            if (quantity <= 0)
            {
                return new PurchaseItemResponse { Success = false, Message = "Quantity must be positive." };
            }

            var disciple = _state.Disciples.FirstOrDefault(d => d.DiscipleId == discipleId);
            if (disciple == null)
            {
                return new PurchaseItemResponse { Success = false, Message = $"No disciple with id '{discipleId}'." };
            }

            var stockEntry = _state.Stockpile.CraftedGoods.FirstOrDefault(g =>
                g.ItemDefId == itemDefId && g.Grade == grade && g.OwnerScope == OwnerScope.SectStockpile);

            var haveQuantity = stockEntry?.Quantity ?? 0;
            if (stockEntry == null || haveQuantity < quantity)
            {
                return new PurchaseItemResponse
                {
                    Success = false,
                    Message = $"Not enough '{itemDefId}' (grade {grade}) in the sect stockpile - have {haveQuantity}, need {quantity}.",
                    RemainingContribution = disciple.Wallet.Contribution,
                };
            }

            var cost = (long)grade * ContributionPricePerGrade * quantity;
            if (disciple.Wallet.Contribution < cost)
            {
                return new PurchaseItemResponse
                {
                    Success = false,
                    Message = $"{disciple.DisplayName} needs {cost} contribution but only has {disciple.Wallet.Contribution}.",
                    RemainingContribution = disciple.Wallet.Contribution,
                };
            }

            disciple.Wallet.Contribution -= cost;
            stockEntry.Quantity -= quantity;
            if (stockEntry.Quantity <= 0)
            {
                _state.Stockpile.CraftedGoods.Remove(stockEntry);
            }

            disciple.PersonalInventory.Add(new InventoryItem
            {
                ItemDefId = itemDefId,
                Quantity = quantity,
                Grade = grade,
                OwnerScope = OwnerScope.Personal,
            });

            Debug.Log($"[SectStateProvider] {disciple.DisplayName} bought {quantity}x {itemDefId} (grade {grade}) " +
                      $"for {cost} contribution. Remaining: {disciple.Wallet.Contribution}");

            return new PurchaseItemResponse
            {
                Success = true,
                Message = $"Purchased {quantity}x {itemDefId} (grade {grade}) for {cost} contribution.",
                RemainingContribution = disciple.Wallet.Contribution,
            };
        }

        // Changed from record to class for Unity compatibility
        private class CraftingRecipe
        {
            public string ItemDefId { get; }
            public int Grade { get; }
            public float CraftSeconds { get; }
            public Dictionary<string, int> Costs { get; }
            /// <summary>P10B — skill category this task's COMPLETED crafts award XP to.</summary>
            public string SkillCategory { get; }

            public CraftingRecipe(string itemDefId, int grade, float craftSeconds,
                                  Dictionary<string, int> costs, string skillCategory)
            {
                ItemDefId = itemDefId;
                Grade = grade;
                CraftSeconds = craftSeconds;
                Costs = costs;
                SkillCategory = skillCategory;
            }
        }
    }
}
