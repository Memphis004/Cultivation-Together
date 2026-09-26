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
                "elixir_qi_gathering", 3, 20f,
                new Dictionary<string, int> { ["herb"] = 10 }),

            ["forging_artifact"] = new CraftingRecipe(
                "sword_azure_flame", 5, 30f,
                new Dictionary<string, int> { ["ore"] = 15, ["wood"] = 10 }),
        };

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
        private readonly BuildingDefPool _buildingDefPool;
        private readonly AvatarPartPool _avatarPartPool;
        private readonly VisualRuntimeConfig _visualConfig;
        private readonly IVisualEntitlementProvider _entitlementProvider;
        /// <summary>Concrete ref to the injected provider (null when a test/substitute implements the interface directly) — used only for bind-late wiring, not for resolution.</summary>
        private readonly DefaultEntitlementProvider _defaultEntitlementProvider;

        // Fractional resource accumulated per task since the last whole
        // unit was added to the stockpile - avoids losing sub-1 production
        // between ticks.
        private readonly Dictionary<string, float> _gatherAccumulators = new();

        // Seconds accumulated toward the current craft, keyed per disciple
        // (not per task like gathering) - crafting has a resource cost, so
        // two disciples on the same task must progress independently, not
        // share one pooled timer.
        private readonly Dictionary<string, float> _craftProgress = new();

        public SectStateProvider(
            IPublisher<DiscipleRecruitedMessage> discipleRecruitedPublisher,
            IPublisher<SectResourceChangedMessage> resourceChangedPublisher,
            IPublisher<AvatarEquipmentChangedMessage> avatarChangedPublisher,
            IPublisher<DiscipleChibiBackendChangedMessage> chibiBackendPublisher,
            AvatarPartPool avatarPartPool,
            VisualRuntimeConfig visualConfig,
            IVisualEntitlementProvider entitlementProvider,
            BuildingDefPool buildingDefPool,
            IPublisher<BuildingPlacedMessage> buildingPlacedPublisher)
        {
            _discipleRecruitedPublisher = discipleRecruitedPublisher;
            _resourceChangedPublisher = resourceChangedPublisher;
            _avatarChangedPublisher = avatarChangedPublisher;
            _chibiBackendPublisher = chibiBackendPublisher;
            _buildingDefPool = buildingDefPool;
            _buildingPlacedPublisher = buildingPlacedPublisher;
            _avatarPartPool = avatarPartPool;
            _visualConfig = visualConfig;
            _entitlementProvider = entitlementProvider;
            _defaultEntitlementProvider = entitlementProvider as DefaultEntitlementProvider;

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
                    AdjustAndNotify(_state.Stockpile.RawResources, rate.Resource, wholeUnits);
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
            }
        }

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

            public CraftingRecipe(string itemDefId, int grade, float craftSeconds, Dictionary<string, int> costs)
            {
                ItemDefId = itemDefId;
                Grade = grade;
                CraftSeconds = craftSeconds;
                Costs = costs;
            }
        }
    }
}
