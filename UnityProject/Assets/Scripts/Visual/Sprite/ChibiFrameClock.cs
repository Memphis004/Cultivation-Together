using System.Collections.Generic;
using UnityEngine;
using VContainer.Unity;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// ONE central frame clock for every chibi in the scene (D5/L3, C10):
    /// no Animator per instance, no per-instance texture bake. Each registered
    /// visual gets its frame index advanced here; the visual applies the sprite
    /// swap ONLY when the index actually changes (no per-frame setter calls).
    /// Start-up phase is randomized per instance so a crowd never animates
    /// in lockstep. Registered as a VContainer entry point (single tick).
    /// </summary>
    public sealed class ChibiFrameClock : ITickable
    {
        private readonly List<IChibiAnimated> _animated = new List<IChibiAnimated>(256);
        private readonly List<int> _removeScratch = new List<int>(32);

        // One shared accumulator — per-instance phase offsets spread the crowd.
        private float _clock;
        private int _lastFrame = -1;

        /// <summary>Subscribe a visual; returns its randomized phase offset (0..1).</summary>
        public float Register(IChibiAnimated visual)
        {
            float phase = UnityEngine.Random.value;
            _animated.Add(visual);
            visual.ApplyFrameIndex(0); // start on frame 0 of the current state
            return phase;
        }
        public void Unregister(IChibiAnimated visual)
        {
            _animated.Remove(visual);
        }

        public void Tick()
        {
            // Global speed: states may have different fps — advance by the max fps
            // and let each visual mod down by its own state's frame rate.
            _clock += Time.deltaTime;
            int frame = (int)_clock;

            if (frame == _lastFrame) return; // nothing changed this tick — no work at all (C10)
            _lastFrame = frame;

            for (int i = 0; i < _animated.Count; i++)
            {
                var v = _animated[i];
                if (v == null) { _removeScratch.Add(i); continue; }
                v.ClockAdvance(Time.deltaTime); // visual accumulates against its own fps
            }

            // Compaction (no allocation, order-preserving enough for our use)
            if (_removeScratch.Count > 0)
            {
                for (int i = _removeScratch.Count - 1; i >= 0; i--) _animated.RemoveAt(_removeScratch[i]);
                _removeScratch.Clear();
            }
        }
    }

    /// <summary>
    /// The clock's contract with a visual. Implementations keep their own
    /// accumulator (seeded with a random phase) and translate elapsed time
    /// into a frame index for whatever state they are playing.
    /// </summary>
    public interface IChibiAnimated
    {
        /// <summary>Called by the clock each tick with delta time — visual applies frame only on index change.</summary>
        void ClockAdvance(float deltaTime);
        /// <summary>Initial application when registering with the clock.</summary>
        void ApplyFrameIndex(int frameIndex);
    }
}
