using System;
using System.Collections.Generic;
using System.Linq;
using MessagePipe;
using UnityEngine;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
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
        private static readonly Dictionary<string, CraftingRecipe> CraftingRecipes = new()
        {
            ["refining_elixir"] = new CraftingRecipe(
                ItemDefId: "elixir_qi_gathering", Grade: 3, CraftSeconds: 20f,
                Costs: new Dictionary<string, int> { ["herb"] = 10 }),

            ["forging_artifact"] = new CraftingRecipe(
                ItemDefId: "sword_azure_flame", Grade: 5, CraftSeconds: 30f,
                Costs: new Dictionary<string, int> { ["ore"] = 15, ["wood"] = 10 }),
        };

        // Placeholder name pool - swap for a real generator once there's a
        // reason to (naming conventions, avoiding repeats at scale, etc.).
        private static readonly string[] RecruitNamePool =
        {
            "Chen Wei", "Bai Ling", "Zhou Tao", "Xiao Mei", "Jiang Yu", "Wen Hao",
        };

        private readonly SectEconomyState _state = MockSectData.Create();
        private readonly IPublisher<DiscipleRecruitedMessage> _discipleRecruitedPublisher;

        // Fractional resource accumulated per task since the last whole
        // unit was added to the stockpile - avoids losing sub-1 production
        // between ticks.
        private readonly Dictionary<string, float> _gatherAccumulators = new();

        // Seconds accumulated toward the current craft, keyed per disciple
        // (not per task like gathering) - crafting has a resource cost, so
        // two disciples on the same task must progress independently, not
        // share one pooled timer.
        private readonly Dictionary<string, float> _craftProgress = new();

        public SectStateProvider(IPublisher<DiscipleRecruitedMessage> discipleRecruitedPublisher)
        {
            _discipleRecruitedPublisher = discipleRecruitedPublisher;
        }

        public SectEconomyState BuildSectEconomyState()
        {
            return _state;
        }

        // Passive resource gathering - every disciple whose CurrentTask is
        // a known gathering task contributes toward that resource. Called
        // from DiscipleSystem.Tick().
        public void TickGathering(float deltaTimeSeconds)
        {
            foreach (var disciple in _state.Disciples)
            {
                if (!GatheringRates.TryGetValue(disciple.CurrentTask, out var rate)) continue;

                var accKey = disciple.CurrentTask;
                var acc = _gatherAccumulators.TryGetValue(accKey, out var existing) ? existing : 0f;
                acc += rate.PerSecond * deltaTimeSeconds;

                var wholeUnits = Mathf.FloorToInt(acc);
                if (wholeUnits > 0)
                {
                    Adjust(_state.Stockpile.RawResources, rate.Resource, wholeUnits);
                    acc -= wholeUnits;
                    Debug.Log($"[SectStateProvider] Gathered +{wholeUnits} {rate.Resource} (task={accKey})");
                }

                _gatherAccumulators[accKey] = acc;
            }
        }

        // Disciple crafting: whoever's CurrentTask matches a known recipe
        // accumulates progress; once a craft completes, consumes the raw
        // resource cost and produces the item - into the sect stockpile for
        // ordinary disciples, or straight into personal inventory for
        // Elder+ (matches the ownership rule from the economy design:
        // outer/inner disciples craft for the sect, elders keep their own).
        // Called from ResourceCraftingSystem.Tick().
        public void TickCrafting(float deltaTimeSeconds)
        {
            foreach (var disciple in _state.Disciples)
            {
                if (!CraftingRecipes.TryGetValue(disciple.CurrentTask, out var recipe)) continue;

                var progress = _craftProgress.TryGetValue(disciple.DiscipleId, out var existing) ? existing : 0f;
                progress += deltaTimeSeconds;

                if (progress < recipe.CraftSeconds) 
                {
                    _craftProgress[disciple.DiscipleId] = progress;
                    continue;
                }

                if (!TryConsume(_state.Stockpile.RawResources, recipe.Costs))
                {
                    // Ready to complete but not enough raw resources - hold
                    // at the completion threshold and wait rather than
                    // losing the accumulated progress or overshooting.
                    _craftProgress[disciple.DiscipleId] = recipe.CraftSeconds;
                    continue;
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
            }
        }

        // Adds a new Outer Disciple assigned to a gathering task, round-robin
        // across GatheringTasks so recruits don't all pile onto one resource.
        public void RecruitOuterDisciple()
        {
            var index = _state.Disciples.Count;
            var task = GatheringTasks[index % GatheringTasks.Length];
            var name = RecruitNamePool[index % RecruitNamePool.Length];

            var disciple = new DiscipleState
            {
                DiscipleId = $"d{index + 1:000}",
                DisplayName = name,
                Rank = DiscipleRank.OuterDisciple,
                Wallet = new CurrencyWallet(),
                PersonalInventory = new List<InventoryItem>(),
                CurrentTask = task,
            };

            _state.Disciples.Add(disciple);
            _discipleRecruitedPublisher.Publish(new DiscipleRecruitedMessage
            {
                DiscipleId = disciple.DiscipleId,
                DisplayName = disciple.DisplayName,
            });

            Debug.Log($"[SectStateProvider] Recruited outer disciple: {disciple.DisplayName} ({disciple.DiscipleId}), assigned to {task}");
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
                    Adjust(resources, "provisions", -20);
                    break;

                case "herb_garden_bloom":
                    Adjust(resources, "herb", 30);
                    break;

                case "wandering_merchant":
                    Adjust(resources, "ore", -20);
                    Adjust(resources, "provisions", 40);
                    break;

                case "new_disciple_applicant":
                    if (choiceId != null && choiceId.ToLowerInvariant().Contains("accept"))
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

        private record CraftingRecipe(string ItemDefId, int Grade, float CraftSeconds, Dictionary<string, int> Costs);
    }
}
