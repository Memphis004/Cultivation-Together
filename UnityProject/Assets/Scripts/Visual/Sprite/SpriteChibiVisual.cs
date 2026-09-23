using System.Collections.Generic;
using UnityEngine;
using Xianxia.Sect; // AvatarPartDef / AvatarPartPool / AvatarSlots

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// SpriteSheet chibi backend (plan §6.2, Phase 2). Layered SpriteRenderers
    /// driven by the central ChibiFrameClock (C10 — no Animator per instance,
    /// no per-instance bake). Production code — deliberately does NOT reference
    /// anything under Visual/Spikes/ (C5); same proven technique, written fresh.
    ///
    /// Perf invariants (proven in Phase 0 S2):
    ///  - all sheets share one grid + feet-center pivot → one Sprite Atlas → batches stay flat
    ///  - sprites swap only on frame-index change
    ///  - zero steady-state GC: pooling for layers, no per-frame allocation
    ///  - facing = parent localScale.x flip only (C8/L8)
    /// </summary>
    public sealed class SpriteChibiVisual : MonoBehaviour, IChibiVisual, IChibiAnimated
    {
        private const string DefaultActivity = "Idle"; // L9 fallback (single warn per visual)

        // --- injected via Initialize (VContainer-constructed owner passes deps) ---
        private AppearanceResolver _resolver;
        private ChibiFrameBank _bank;
        private ChibiFrameClock _clock;
        private AvatarPartPool _pool;

        // --- runtime ---
        private readonly List<LayerView> _layers = new List<LayerView>(8);   // sorted by Order
        private readonly Stack<SpriteRenderer> _rendererPool = new Stack<SpriteRenderer>(8);
        private readonly Dictionary<string, int> _slotToLayer = new Dictionary<string, int>(8);

        private AvatarAppearance _appearance;
        private string _discipleId;
        private Transform _flipPivot;      // localScale.x flip lives HERE (C8)
        private string _activity = DefaultActivity;
        private string _requestedActivity = DefaultActivity; // semantic activity — reported via CurrentActivity (in-place swap parity with Spine)
        private string _warnedState = string.Empty;
        private bool _facingRight = true;
        private int _sortingBase;

        private float _phase;              // random start offset (anti-lockstep crowd)
        private float _accumulator;
        private int _frameIndex = -1;
        private bool _registered;

        private sealed class LayerView
        {
            public string Slot;
            public string PartId;
            public int Order;
            public bool IsBack;
            public SpriteRenderer Renderer;
        }

        /// <summary>Factory — called by DiscipleVisualSystem (enum→factory map, C12: no reflection).</summary>
        public static SpriteChibiVisual Create(Transform parent,
                                               AppearanceResolver resolver,
                                               ChibiFrameBank bank,
                                               ChibiFrameClock clock,
                                               AvatarPartPool pool)
        {
            var pivot = new GameObject("chibi_flipPivot");
            pivot.transform.SetParent(parent, false);

            var visual = pivot.AddComponent<SpriteChibiVisual>();
            visual._resolver = resolver;
            visual._bank = bank;
            visual._clock = clock;
            visual._pool = pool;
            visual._flipPivot = pivot.transform;
            return visual;
        }

        public VisualBackend Backend { get { return VisualBackend.SpriteSheet; } }
        public Transform Transform { get { return _flipPivot; } }

        /// <summary>Disciple this visual renders — diagnostics/in-place-swap state reads.</summary>
        public string DiscipleId { get { return _discipleId; } }

        /// <summary>Sorting base given via SetSortingBase — preserved across backend swaps (§7).</summary>
        public int SortingBase { get { return _sortingBase; } }

        /// <summary>true = facing +X (flip lives at _flipPivot.localScale.x — L8).</summary>
        public bool FacingRight { get { return _facingRight; } }

        /// <summary>Current activity name (semantic, as requested — never PoseId, L6).
        /// Reports the REQUESTED activity so in-place backend swaps preserve it even
        /// when this backend resolves it to a different animation state.</summary>
        public string CurrentActivity { get { return _requestedActivity; } }

        public void Bind(string discipleId, AvatarAppearance appearance, DiscipleSex sex)
        {
            _discipleId = discipleId;
            _appearance = appearance;
            RebuildAllLayers();
            RegisterWithClock();
        }

        /// <summary>Incremental slot update — swaps only the affected layer(s) (acceptance: no full respawn).</summary>
        public void ApplySlot(string slot, string partId)
        {
            if (_appearance == null) return;

            _appearance.SetSlot(slot, partId); // visual keeps its own copy of the appearance

            // Resolve just this slot against the pool (L2 — no duplicated fallback logic;
            // the resolver remains the single decision point for full rebuilds).
            var def = _pool.Resolve(slot, partId);
            if (def == null || !def.Supports(VisualBackend.SpriteSheet))
            {
                Debug.LogWarning("[SpriteChibiVisual] " + _discipleId + ": part '" + partId +
                                 "' has no chibi sheet — slot '" + slot + "' will drop (resolver fallback skipped for incremental path)");
                DropSlot(slot);
                return;
            }

            // Remove existing layers of this slot then re-add at correct order (no respawn of others)
            DropSlot(slot);
            AddSlotLayers(def);
            _layers.Sort((a, b) => a.Order.CompareTo(b.Order));
            ReindexSlots();
            RefreshAllSprites();
        }

        public void SetActivity(string activityState)
        {
            if (string.IsNullOrEmpty(activityState)) return;
            if (activityState == _requestedActivity) return; // idempotent at the semantic level (contract: no dedupe ABOVE this)

            // L9 spirit: unknown state → resolve to the bank's fallback (Idle) +
            // warn ONCE per (visual, unknown state). GetState resolves internally,
            // so "unknown" means the returned def is NOT the requested state.
            var def = _bank.GetState(activityState);
            var known = def != null && def.state == activityState;
            var resolved = known ? activityState : (def != null ? def.state : ChibiFrameBank.FallbackState);
            if (!known && _warnedState != activityState)
            {
                Debug.LogWarning("[SpriteChibiVisual] " + _discipleId + ": activity '" + activityState +
                                 "' not in chibi_anim.json — playing '" + resolved + "'");
                _warnedState = activityState;
            }

            // Store the REQUESTED activity (not the resolved animation state) — the
            // spec's "activity preserved" invariant spans backend swaps, while the
            // resolved animation may legitimately differ per backend (e.g. "Working"
            // has no sprite strip yet and plays Idle).
            _requestedActivity = activityState;
            if (resolved == _activity) return; // already playing the resolved state — no churn
            _activity = resolved;
            _accumulator = 0f;
            RefreshAllSprites(); // new state = different strip → apply immediately
        }

        public void SetFacing(bool faceRight)
        {
            _facingRight = faceRight;
            var s = _flipPivot.localScale;
            float targetX = faceRight ? 1f : -1f;
            if (!Mathf.Approximately(s.x, targetX))
                _flipPivot.localScale = new Vector3(targetX, s.y, s.z); // flip at pivot ONLY (L8)
        }

        public void SetSortingBase(int order)
        {
            _sortingBase = order; // mirrored for IChibiVisual.SortingBase (in-place backend swaps)
            for (int i = 0; i < _layers.Count; i++)
            {
                if (_layers[i].Renderer != null)
                    _layers[i].Renderer.sortingOrder = order + _layers[i].Order;
            }
        }

        // ---- IChibiAnimated (clock contract) ----

        public void ApplyFrameIndex(int frameIndex)
        {
            if (_frameIndex == frameIndex) return;
            _frameIndex = frameIndex;
            RefreshAllSprites();
        }

        public void ClockAdvance(float deltaTime)
        {
            var def = _bank.GetState(_activity);
            if (def == null) return;

            _accumulator += deltaTime;
            float frameDuration = 1f / Mathf.Max(1, def.fps);
            int newFrame = (int)(_accumulator / frameDuration);
            if (!def.loop) newFrame = Mathf.Min(newFrame, def.frames - 1);
            else newFrame %= def.frames;

            if (newFrame != _frameIndex)
            {
                _frameIndex = newFrame;
                RefreshAllSprites(); // sprite swap ONLY on index change (C10)
            }
        }

        // ---- internals ----

        private void RegisterWithClock()
        {
            if (_registered || _clock == null) return;
            _phase = _clock.Register(this);
            _accumulator = _phase * (1f / Mathf.Max(1, _bank.GetState(_activity) != null ? _bank.GetState(_activity).fps : 6));
            _registered = true;
        }

        private void RebuildAllLayers()
        {
            ReleaseAllLayers();
            if (_appearance == null) return;

            var resolved = _resolver.Resolve(_appearance, VisualBackend.SpriteSheet);
            for (int i = 0; i < resolved.Count; i++)
            {
                var layer = resolved[i];
                var def = _pool.GetById(layer.PartId);
                if (def == null) continue;

                AddLayer(layer.Slot, layer.PartId, layer.Order, layer.IsBack, layer.Tint);
            }
            _layers.Sort((a, b) => a.Order.CompareTo(b.Order));
            ReindexSlots();
            _frameIndex = -1;      // force a sprite apply on next tick/apply
            RefreshAllSprites();
        }

        private void AddSlotLayers(AvatarPartDef def)
        {
            // back layer first (lower order) — mirrors resolver semantics without re-resolving everything
            string backPath = def.BackPathFor(VisualBackend.SpriteSheet);
            if (!string.IsNullOrEmpty(backPath))
            {
                int backOrder = def.BackOrderFor(VisualBackend.SpriteSheet) > 0
                    ? def.BackOrderFor(VisualBackend.SpriteSheet)
                    : def.OrderFor(VisualBackend.SpriteSheet) - 30;
                AddLayer(def.slot, def.id, backOrder, true, Color.white);
            }

            int mainOrder = def.OrderFor(VisualBackend.SpriteSheet);
            AddLayer(def.slot, def.id, mainOrder, false, Color.white);
        }

        private void AddLayer(string slot, string partId, int order, bool isBack, Color tint)
        {
            var sr = RentRenderer();
            sr.sortingOrder = order; // absolute order within the same baseFromY bucket
            _layers.Add(new LayerView { Slot = slot, PartId = partId, Order = order, IsBack = isBack, Renderer = sr });
        }

        private void DropSlot(string slot)
        {
            for (int i = _layers.Count - 1; i >= 0; i--)
            {
                if (_layers[i].Slot != slot) continue;
                ReturnRenderer(_layers[i].Renderer);
                _layers.RemoveAt(i);
            }
        }

        private void ReindexSlots()
        {
            _slotToLayer.Clear();
            for (int i = 0; i < _layers.Count; i++)
                _slotToLayer[_layers[i].Slot + (_layers[i].IsBack ? ":back" : "")] = i;
        }

        private void RefreshAllSprites()
        {
            if (_bank == null) return;

            for (int i = 0; i < _layers.Count; i++)
            {
                var layer = _layers[i];
                if (layer.Renderer == null) continue;

                var frames = _bank.GetFrames(layer.PartId, _activity);
                if (frames == null || frames.Length == 0)
                {
                    layer.Renderer.sprite = null; // part has no sheet for this state — hide layer
                    continue;
                }

                int idx = Mathf.Clamp(_frameIndex < 0 ? 0 : _frameIndex, 0, frames.Length - 1);
                layer.Renderer.sprite = frames[idx];
            }
        }

        private SpriteRenderer RentRenderer()
        {
            SpriteRenderer sr = _rendererPool.Count > 0 ? _rendererPool.Pop() : null;
            if (sr == null)
            {
                var go = new GameObject("chibi_layer");
                go.transform.SetParent(_flipPivot, false);
                sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = null;
            }
            sr.gameObject.SetActive(true);
            return sr;
        }

        private void ReturnRenderer(SpriteRenderer sr)
        {
            if (sr == null) return;
            sr.sprite = null;
            sr.gameObject.SetActive(false);
            _rendererPool.Push(sr);
        }

        private void ReleaseAllLayers()
        {
            for (int i = 0; i < _layers.Count; i++)
                ReturnRenderer(_layers[i].Renderer);
            _layers.Clear();
            _slotToLayer.Clear();
        }

        public void Dispose()
        {
            if (_registered && _clock != null)
            {
                _clock.Unregister(this);
                _registered = false;
            }
            ReleaseAllLayers();
            // Destroy the GameObject — Destroy(transform) is rejected by Unity
            // ("Can't destroy Transform component") and leaves the chibi alive.
            if (_flipPivot != null) Object.Destroy(_flipPivot.gameObject);
        }
    }
}
