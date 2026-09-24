using System;
using System.Collections.Generic;
using MessagePipe;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;
using Xianxia.Sect.Messages;
using Xianxia.Sect; // AvatarSlots

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// Scene-side owner of all chibi visuals (plan §5). Subscribes (in-memory
    /// MessagePipe only — C11) to:
    ///   - DiscipleRecruitedMessage                → spawn incremental
    ///   - AvatarEquipmentChangedMessage           → ApplySlot incremental (no respawn)
    ///   - DiscipleChibiBackendChangedMessage      → respawn IN PLACE (pos/activity/facing kept)
    ///   - SceneLoadedMessage                      → bind ChibiSceneRoot + Reconcile
    ///   - SceneUnloadedMessage                    → despawn everything
    ///
    /// Phase 3: backend choice goes through VisualTierPolicy.EffectiveBackend —
    /// entitled Spine AND under SpineBudget → SpineChibiVisual, else SpriteSheet
    /// (budget degrade never mutates the entitlement in state, §7). Spawn order is
    /// rank-desc then DiscipleId (VisualTierPolicy.CompareSpawnPriority) so priority
    /// disciples win the limited Spine slots.
    ///
    /// Q5 story-character overrides (visual_overrides.json): a disciple whose id has
    /// an override renders from its OWN per-character rig through
    /// <see cref="SpineOverrideProbe"/> + <see cref="SpineOverrideVisualFactory"/>
    /// (assigned by VisualSpineBootstrap INSIDE the license-gated block) — the branch
    /// runs BEFORE the §7 allocation/budget logic and does NOT count against
    /// SpineBudget. That is a deliberate decision (per-character rigs are not part of
    /// the shared-rig pool the budget sizes; change it only with the team's sign-off).
    /// Everything else takes the §7 path unchanged.
    ///
    /// No FindObjectOfType / reflection (C12): backend choice is a plain
    /// enum→factory map, scene root is found by scanning loaded scene roots (C3).
    /// </summary>
    public sealed class DiscipleVisualSystem : IStartable, IDisposable
    {
        /// <summary>
        /// asmdef boundary (C12 inversion): the Visual.Spine assembly (which DOES
        /// reference spine-unity) assigns this hook at bootstrap — Core/Runtime never
        /// reference Spine types, so Core/Sprite compile with zero Spine dependency.
        /// Null = Spine path unavailable → every Spine entitlement degrades to
        /// SpriteSheet (state untouched). Assigned with a null-checked skeleton asset
        /// and ONLY after the S4 license gate is confirmed — never before.
        /// </summary>
        public static Func<DiscipleState, Transform, IChibiVisual> SpineVisualFactory { get; set; }

        /// <summary>
        /// Q5 story-character override probe — discipleId → SkeletonDataAsset Resources
        /// path, or "" when the disciple has no override. Assigned ONLY by
        /// VisualSpineBootstrap inside the S4 license-gated block (null otherwise) —
        /// never a bypass of the gate. Core never touches Spine types: the probe
        /// returns a path string and the actual asset load stays in Visual.Spine.
        /// </summary>
        public static Func<string, string> SpineOverrideProbe { get; set; }

        /// <summary>
        /// Q5 override factory — same contract as <see cref="SpineVisualFactory"/> but
        /// building the visual from the disciple's OWN rig (no shared chibi_base, no
        /// part mixing). Returning null = caller degrades to SpriteSheet.
        /// Assigned alongside the probe by VisualSpineBootstrap after the gate.
        /// </summary>
        public static Func<DiscipleState, Transform, IChibiVisual> SpineOverrideVisualFactory { get; set; }

        private readonly ISectStateProvider _stateProvider;
        private readonly AppearanceResolver _resolver;
        private readonly VisualRuntimeConfig _config;
        private readonly AvatarPartPool _pool;
        private readonly ChibiFrameBank _bank;
        private readonly ChibiFrameClock _clock;
        private readonly TaskActivityMapper _taskActivityMapper; // Phase 4: CurrentTask → ChibiActivity (data-driven)
        private readonly IPublisher<DiscipleSelectedMessage> _selectedPublisher; // Phase 4: click-to-select (in-memory only)

        private readonly ISubscriber<DiscipleRecruitedMessage> _recruitedSub;
        private readonly ISubscriber<AvatarEquipmentChangedMessage> _equipmentSub;
        private readonly ISubscriber<DiscipleChibiBackendChangedMessage> _backendSub;
        private readonly ISubscriber<SceneLoadedMessage> _sceneLoadedSub;
        private readonly ISubscriber<SceneUnloadedMessage> _sceneUnloadedSub;

        private readonly List<IDisposable> _subscriptions = new List<IDisposable>(5);
        private readonly Dictionary<string, IChibiVisual> _active =
            new Dictionary<string, IChibiVisual>(StringComparer.Ordinal);

        private ChibiSceneRoot _root;
        private bool _disposed;

        public DiscipleVisualSystem(
            ISectStateProvider stateProvider,
            AppearanceResolver resolver,
            VisualRuntimeConfig config,
            AvatarPartPool pool,
            ChibiFrameBank bank,
            ChibiFrameClock clock,
            TaskActivityMapper taskActivityMapper,
            IPublisher<DiscipleSelectedMessage> selectedPublisher,
            ISubscriber<DiscipleRecruitedMessage> recruitedSub,
            ISubscriber<AvatarEquipmentChangedMessage> equipmentSub,
            ISubscriber<DiscipleChibiBackendChangedMessage> backendSub,
            ISubscriber<SceneLoadedMessage> sceneLoadedSub,
            ISubscriber<SceneUnloadedMessage> sceneUnloadedSub)
        {
            _stateProvider = stateProvider;
            _resolver = resolver;
            _config = config;
            _pool = pool;
            _bank = bank;
            _clock = clock;
            _taskActivityMapper = taskActivityMapper;
            _selectedPublisher = selectedPublisher;
            _recruitedSub = recruitedSub;
            _equipmentSub = equipmentSub;
            _backendSub = backendSub;
            _sceneLoadedSub = sceneLoadedSub;
            _sceneUnloadedSub = sceneUnloadedSub;
        }

        public void Start()
        {
            _subscriptions.Add(_recruitedSub.Subscribe(OnDiscipleRecruited));
            _subscriptions.Add(_equipmentSub.Subscribe(OnAvatarEquipmentChanged));
            _subscriptions.Add(_backendSub.Subscribe(OnChibiBackendChanged));
            _subscriptions.Add(_sceneLoadedSub.Subscribe(OnSceneLoaded));
            _subscriptions.Add(_sceneUnloadedSub.Subscribe(OnSceneUnloaded));

            // CoreScene already loaded when we start: pick up its ChibiSceneRoot (if any)
            // and spawn the current roster — handles the "game boots straight into
            // SampleScene without a gameplay scene load" path.
            if (_root == null) TryBindRootFromActiveScenes();
            if (_root != null) Reconcile();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            for (int i = 0; i < _subscriptions.Count; i++) _subscriptions[i].Dispose();
            _subscriptions.Clear();

            DespawnAll();
        }

        // ---- message handlers (event-driven — no polling, D9) ----

        private void OnDiscipleRecruited(DiscipleRecruitedMessage msg)
        {
            if (_root == null || _config == null || !_config.SpriteSheetEnabled) return;
            if (_active.ContainsKey(msg.DiscipleId)) return;

            var state = FindDisciple(msg.DiscipleId);
            if (state == null) return;

            // Re-allocate Spine slots BEFORE deciding the new disciple's backend so a
            // fresh recruit can win a slot (or be correctly degraded by priority).
            RefreshSpineAllocation();

            SpawnVisual(state, _active.Count);
        }

        private void OnAvatarEquipmentChanged(AvatarEquipmentChangedMessage msg)
        {
            IChibiVisual visual;
            if (!_active.TryGetValue(msg.DiscipleId, out visual)) return;
            visual.ApplySlot(msg.Slot, msg.NewPartId); // incremental — same visual instance (acceptance criterion)
        }

        private void OnSceneLoaded(SceneLoadedMessage msg)
        {
            TryBindRootFromActiveScenes();
            if (_root != null) Reconcile();
        }

        /// <summary>
        /// §7 promotion/demotion — respawn the visual IN PLACE: same world position,
        /// same current activity, same facing (acceptance criterion). Entitlement was
        /// already mutated by SectStateProvider.TrySetChibiBackend; whether the new
        /// visual is actually Spine is decided by VisualTierPolicy (budget applies).
        /// </summary>
        private void OnChibiBackendChanged(DiscipleChibiBackendChangedMessage msg)
        {
            if (_root == null || _config == null || !_config.SpriteSheetEnabled) return;

            // Entitlement changed → re-run §7 allocation FIRST, then respawn in place
            // (allocation may keep the visual on Sprite: budget full or Spine off —
            // state is the entitlement, effective backend is the render decision §7).
            RefreshSpineAllocation();

            IChibiVisual oldVisual;
            if (!_active.TryGetValue(msg.DiscipleId, out oldVisual)) return; // not on screen — Reconcile will handle

            var d = FindDisciple(msg.DiscipleId);
            if (d == null) return;

            // Skip the respawn when the EFFECTIVE backend is unchanged (e.g. Spine off
            // or budget full → allocation keeps the visual on Sprite) — never respawn
            // on an incremental change that renders identically. Q5-aware: overridden
            // disciples keep "wanting" Spine regardless of the budget.
            var wantBackend = WantsSpineRender(msg.DiscipleId)
                ? VisualBackend.Spine
                : VisualBackend.SpriteSheet;
            if (oldVisual.Backend == wantBackend)
            {
                Debug.Log("[DiscipleVisualSystem] " + msg.DiscipleId + " entitlement " + msg.Old + " → " + msg.New +
                          " but effective backend stays " + wantBackend + " — no respawn");
                return;
            }

            // Preserve in-place state across the swap
            Vector3 pos = oldVisual.Transform.position;
            int sortingBase = oldVisual.SortingBase;
            bool faceRight = oldVisual.FacingRight;
            string activity = oldVisual.CurrentActivity;

            oldVisual.Dispose();
            _active.Remove(msg.DiscipleId);

            IChibiVisual newVisual = CreateVisual(d);
            newVisual.Transform.position = pos;
            newVisual.Bind(d.DiscipleId, d.Avatar != null ? d.Avatar.Clone() : new AvatarAppearance(), d.Sex);
            newVisual.SetFacing(faceRight);
            newVisual.SetActivity(activity);
            newVisual.SetSortingBase(sortingBase);

            AttachClickTarget(newVisual, d);
            _active[msg.DiscipleId] = newVisual;
            Debug.Log("[DiscipleVisualSystem] " + msg.DiscipleId + " backend " + msg.Old + " → " + msg.New +
                      " (effective " + newVisual.Backend + ") — respawned in place");
        }

        /// <summary>
        /// Re-run the §7 allocation so _spineAllocated reflects the CURRENT roster +
        /// budget. Must run before any CreateVisual decision made outside Reconcile
        /// (recruit, backend-change) — the set is only authoritative right after a
        /// (re)allocation pass.
        /// </summary>
        private void RefreshSpineAllocation()
        {
            var state = _stateProvider.BuildSectEconomyState();
            if (state == null || state.Disciples == null)
            {
                _spineAllocated.Clear();
                return;
            }
            VisualTierPolicy.Instance.AllocateSpineSlots(state.Disciples, IdOf, _spineAllocated);
        }

        private void OnSceneUnloaded(SceneUnloadedMessage msg)
        {
            // Chibis were parented under the unloaded scene's root — those objects are
            // already being destroyed; just drop references so nothing dangles.
            DespawnAll(releaseOnly: true);
        }

        // ---- reconcile / spawn / despawn ----

        /// <summary>
        /// Diff current state roster vs _active — despawn gone, respawn visual whose
        /// EFFECTIVE backend changed (budget moves), spawn missing in priority order
        /// (rank desc, then DiscipleId — §7) so priority disciples win Spine slots.
        /// </summary>
        public void Reconcile()
        {
            if (_root == null || _config == null || !_config.SpriteSheetEnabled) return;
            var state = _stateProvider.BuildSectEconomyState();
            if (state == null || state.Disciples == null) return;

            // §7 allocation FIRST — VisualTierPolicy owns priority ordering + budget
            // and returns exactly who should render Spine this reconcile.
            VisualTierPolicy.Instance.AllocateSpineSlots(state.Disciples, IdOf, _spineAllocated);

            // Despawn visuals whose disciple no longer exists
            _toRemove.Clear();
            foreach (var kvp in _active)
            {
                bool found = false;
                var disciples = state.Disciples;
                for (int i = 0; i < disciples.Count; i++)
                {
                    if (disciples[i].DiscipleId == kvp.Key) { found = true; break; }
                }
                if (!found) _toRemove.Add(kvp.Key);
            }
            for (int i = 0; i < _toRemove.Count; i++)
            {
                IChibiVisual v;
                if (_active.TryGetValue(_toRemove[i], out v)) v.Dispose();
                _active.Remove(_toRemove[i]);
            }

            // Spawn missing in priority order (§7) — spine count grows as we go
            _pending.Clear();
            var existing = state.Disciples;
            for (int i = 0; i < existing.Count; i++)
            {
                var d = existing[i];
                if (d != null && !_active.ContainsKey(d.DiscipleId)) _pending.Add(d);
            }
            VisualTierPolicy.Instance.SortBySpawnPriority(_pending);
            for (int i = 0; i < _pending.Count; i++)
            {
                SpawnVisual(_pending[i], _active.Count);
            }

            // Degrade/promote already-active visuals whose ALLOCATED backend differs
            // — in place, never touching state. Allocation-based, NOT count-based:
            // per-item count checks cascade-degrade everyone when the budget drops.
            _effectiveChanged.Clear();
            foreach (var kvp in _active)
            {
                var wantBackend = WantsSpineRender(kvp.Key)
                    ? VisualBackend.Spine
                    : VisualBackend.SpriteSheet;
                if (kvp.Value.Backend != wantBackend)
                {
                    var d = FindDisciple(kvp.Key);
                    if (d != null) _effectiveChanged.Add(d);
                }
            }
            for (int i = 0; i < _effectiveChanged.Count; i++)
            {
                RespawnInPlace(_effectiveChanged[i]);
            }

            // Phase 4 — derive-on-reconcile: re-resolve activity + work-anchor
            // placement from the disciple's CURRENT CurrentTask. SetActivity and the
            // position write are no-ops when nothing changed, and Reconcile only runs
            // on events (scene load / recruit / backend change) — no polling (D9).
            foreach (var kvp in _active)
            {
                var d = FindDisciple(kvp.Key);
                if (d == null) continue;
                kvp.Value.SetActivity(_taskActivityMapper.ResolveActivity(d.CurrentTask));
                if (_root.TryGetWorkAnchor(d.CurrentTask, out Vector3 anchorPos) &&
                    (kvp.Value.Transform.position - anchorPos).sqrMagnitude > 0.0001f)
                {
                    kvp.Value.Transform.position = anchorPos;
                }
            }
        }

        private void SpawnVisual(DiscipleState d, int gridIndex)
        {
            var visual = CreateVisual(d);
            // Phase 4: work anchor when the scene defines one for this task family;
            // plain grid slot otherwise (anchors are art/level-design data — never
            // procedural, and the shipped scenes keep the array empty = grid).
            if (!_root.TryGetWorkAnchor(d.CurrentTask, out Vector3 anchorPos))
                anchorPos = _root.PositionFor(gridIndex);
            visual.Transform.SetPositionAndRotation(anchorPos, Quaternion.identity);
            visual.Bind(d.DiscipleId, d.Avatar != null ? d.Avatar.Clone() : new AvatarAppearance(), d.Sex);
            visual.SetFacing(d.Sex == DiscipleSex.Male);
            // Phase 4: CurrentTask → ChibiActivity, data-driven (TaskActivityMapper).
            // SetActivity is idempotent per visual — no extra dedupe layer here.
            visual.SetActivity(_taskActivityMapper.ResolveActivity(d.CurrentTask));
            visual.SetSortingBase(_root.SortingBase + gridIndex); // base from grid slot (y-sorting is Phase 4)

            AttachClickTarget(visual, d);

            _active[d.DiscipleId] = visual;
        }

        /// <summary>
        /// Phase 4 — attach a click-to-select hit target to the visual's root
        /// GameObject (both backends). Publisher is injected here at creation time;
        /// the component itself never resolves anything globally (C12). Collision
        /// layers/clickable area live on the visual's own GameObject.
        /// </summary>
        private void AttachClickTarget(IChibiVisual visual, DiscipleState d)
        {
            var go = visual.Transform.gameObject;
            if (go.GetComponent<ChibiClickTarget>() != null) return; // respawn-in-place reuses the same GO

            var collider = go.GetComponent<Collider2D>();
            if (collider == null)
            {
                var box = go.AddComponent<BoxCollider2D>();
                // 96×96 cell (Q3/S3) — full-cell hit area, centered a bit above the
                // feet pivot so the visible body is covered.
                box.offset = new Vector2(0f, 0.45f);
                box.size = new Vector2(0.9f, 0.9f);
            }

            var target = go.AddComponent<ChibiClickTarget>();
            target.Bind(d.DiscipleId, _selectedPublisher);
        }

        /// <summary>
        /// Q5-aware effective-backend predicate for the in-place respawn decision:
        /// overridden disciples always "want" Spine while the override hooks are live
        /// (they render their OWN rig — the §7 budget never applies, see CreateVisual),
        /// everyone else wants Spine only when THIS reconcile's allocation set contains
        /// them. Keeps Reconcile / OnChibiBackendChanged from churning an override
        /// visual's backend every time SpineBudget moves.
        /// </summary>
        private bool WantsSpineRender(string discipleId)
        {
            var probe = SpineOverrideProbe;
            if (probe != null && SpineOverrideVisualFactory != null &&
                !string.IsNullOrEmpty(probe(discipleId))) return true;
            return _spineAllocated.Contains(discipleId);
        }

        /// <summary>
        /// CreateVisual — backend decision ladder (in order):
        ///   1. Q5 story-character override (visual_overrides.json): render the
        ///      disciple's OWN per-character rig. Runs BEFORE the §7 allocation/budget
        ///      logic and does NOT count against SpineBudget — DELIBERATE DECISION:
        ///      the budget sizes the shared-rig pool (§7), while these rigs are unique
        ///      per-character assets with no part mixing (Q5 path), so counting them
        ///      would silently degrade story characters for no measured reason. Revisit
        ///      only with the team's sign-off (comment kept for that review).
        ///   2. §7 allocation path — Spine-allocated AND a shared-rig factory exists →
        ///      shared chibi_base rig; budget-full/unavailable → warn-once + SpriteSheet.
        ///   3. Default — SpriteSheet.
        /// Every failure path degrades to SpriteSheet — never a hard crash.
        /// </summary>
        private IChibiVisual CreateVisual(DiscipleState d)
        {
            // 1) Q5 story-character override — own rig, outside the §7 budget.
            var probe = SpineOverrideProbe;
            var overrideFactory = SpineOverrideVisualFactory;
            if (probe != null && overrideFactory != null &&
                !string.IsNullOrEmpty(probe(d.DiscipleId)))
            {
                var overridden = overrideFactory(d, _root.transform);
                if (overridden != null) return overridden;

                if (_overrideDegradeWarned.Add(d.DiscipleId))
                {
                    Debug.LogWarning("[DiscipleVisualSystem] " + d.DiscipleId +
                                     " has a story-character skeleton override but its rig failed to load — " +
                                     "rendering as SpriteSheet (scene keeps running).");
                }
                return SpriteChibiVisual.Create(_root.transform, _resolver, _bank, _clock, _pool);
            }

            // 2) §7 allocation path (unchanged) — shared-rig Spine with budget degrade.
            if (_spineAllocated.Contains(d.DiscipleId))
            {
                var factory = SpineVisualFactory;
                if (factory != null)
                {
                    var spine = factory(d, _root.transform);
                    if (spine != null) return spine;
                }
                if (_spineDegradeWarned.Add(d.DiscipleId))
                {
                    Debug.LogWarning("[DiscipleVisualSystem] " + d.DiscipleId + " is Spine-allocated but the " +
                                     "Spine backend is unavailable (" + (factory == null ? "no factory — S4 license gate not confirmed / skeleton missing" : "factory returned null") +
                                     ") — rendering as SpriteSheet. Entitlement stays " + d.ChibiBackend + " in state.");
                }
            }
            return SpriteChibiVisual.Create(_root.transform, _resolver, _bank, _clock, _pool);
        }

        /// <summary>Dispose + recreate one disciple's visual, preserving position/activity/facing/sorting (§7).</summary>
        private void RespawnInPlace(DiscipleState d)
        {
            IChibiVisual oldVisual;
            if (!_active.TryGetValue(d.DiscipleId, out oldVisual)) return;

            Vector3 pos = oldVisual.Transform.position;
            int sortingBase = oldVisual.SortingBase;
            bool faceRight = oldVisual.FacingRight;
            string activity = oldVisual.CurrentActivity;

            oldVisual.Dispose();
            _active.Remove(d.DiscipleId);

            var newVisual = CreateVisual(d);
            newVisual.Transform.position = pos;
            newVisual.Bind(d.DiscipleId, d.Avatar != null ? d.Avatar.Clone() : new AvatarAppearance(), d.Sex);
            newVisual.SetFacing(faceRight);
            if (!string.IsNullOrEmpty(activity)) newVisual.SetActivity(activity);
            newVisual.SetSortingBase(sortingBase);
            AttachClickTarget(newVisual, d);
            _active[d.DiscipleId] = newVisual;
        }

        private DiscipleState FindDisciple(string discipleId)
        {
            var state = _stateProvider.BuildSectEconomyState();
            if (state == null || state.Disciples == null) return null;
            var disciples = state.Disciples;
            for (int i = 0; i < disciples.Count; i++)
            {
                var d = disciples[i];
                if (d != null && d.DiscipleId == discipleId) return d;
            }
            return null;
        }

        /// <summary>
        /// Despawn everything. releaseOnly: after a scene unload the chibi GameObjects
        /// are already being destroyed with the scene — Dispose() still unregisters
        /// from the clock and clears layers; Object.Destroy on an already-destroyed
        /// object is a no-op safe-guarded inside SpriteChibiVisual.
        /// </summary>
        private void DespawnAll(bool releaseOnly = false)
        {
            foreach (var kvp in _active)
                kvp.Value.Dispose();
            _active.Clear();
            _spineAllocated.Clear();
        }

        /// <summary>
        /// Find the ChibiSceneRoot by scanning root objects of ALL loaded scenes
        /// (C3 — same pattern as SceneLoader.RemoveDuplicateSingletons; the last
        /// loaded gameplay scene wins because it was added most recently).
        /// </summary>
        private void TryBindRootFromActiveScenes()
        {
            ChibiSceneRoot found = null;
            int sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                var roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    var root = roots[r].GetComponent<ChibiSceneRoot>();
                    if (root != null) found = root; // keep last (most recently loaded scene)
                }
            }

            if (found != null && found != _root)
            {
                _root = found;
                Debug.Log("[DiscipleVisualSystem] bound ChibiSceneRoot in scene '" +
                          found.gameObject.scene.name + "'");
            }
        }

        // scratch — reused, never allocated per call (steady-state GC = 0)
        private static readonly Func<DiscipleState, string> IdOf = d => d.DiscipleId; // non-capturing — cached by compiler
        private readonly List<string> _toRemove = new List<string>(8);
        private readonly List<DiscipleState> _pending = new List<DiscipleState>(16);
        private readonly List<DiscipleState> _effectiveChanged = new List<DiscipleState>(8);
        private readonly HashSet<string> _spineAllocated = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _spineDegradeWarned = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _overrideDegradeWarned = new HashSet<string>(StringComparer.Ordinal); // Q5 warn-once
    }
}
