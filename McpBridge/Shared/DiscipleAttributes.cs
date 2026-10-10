// P10A — disciple attribute DATA only (stamina + skill XP).
// P10B — adds the work rates (drain/regen/XP) to the tuning class below; the DATA
// and its derived helpers are unchanged.
//
// Scope (deliberate): DATA + derived helpers + tuning constants ONLY. No AI
// scoring, no productivity effects, no traits, no Mood, no UI. Attributes never
// feed back into resource yield, craft speed, task assignment or the Auto
// scheduler — P10B only writes them from work outcomes.
//
// Shared/ is the authoritative copy. Edit here, then run ./sync-shared.sh.
// Plain C# — no UnityEngine API (Shared is also consumed by the McpBridge
// console app, which has no engine).
using System;
using System.Collections.Generic;
using MessagePack;

namespace Xianxia.Sect
{
    /// <summary>
    /// Centralized tuning for <see cref="DiscipleAttributes"/> (P10A).
    ///
    /// ⚠️ EVERY VALUE BELOW IS A PROTOTYPE PROPOSAL — not balance data. Revisit
    /// once P10B (drain/regen) and real gathering accrual exist; the numbers are
    /// only here so the rules and tests share one source of truth.
    /// </summary>
    public static class DiscipleAttributesConfig
    {
        // --- skill category ids (exactly these three) ---

        /// <summary>Prototype proposal — gathering skill category id.</summary>
        public const string CategoryGathering = "gathering";
        /// <summary>Prototype proposal — alchemy skill category id.</summary>
        public const string CategoryAlchemy = "alchemy";
        /// <summary>Prototype proposal — forging skill category id.</summary>
        public const string CategoryForging = "forging";

        /// <summary>
        /// Prototype proposal — every known category id. Stable order (used by
        /// tests/UI); normalization ensures all of these keys exist.
        /// </summary>
        public static readonly string[] Categories =
        {
            CategoryGathering,
            CategoryAlchemy,
            CategoryForging,
        };

        // --- stamina (prototype proposal) ---

        /// <summary>Prototype proposal — stamina floor.</summary>
        public const float StaminaMin = 0f;
        /// <summary>Prototype proposal — stamina ceiling.</summary>
        public const float StaminaMax = 100f;
        /// <summary>Prototype proposal — new-game stamina.</summary>
        public const float StaminaDefault = 100f;

        // --- skill XP / derived level (prototype proposal) ---

        /// <summary>Prototype proposal — XP floor per category.</summary>
        public const float SkillXpMin = 0f;
        /// <summary>Prototype proposal — XP cap per category.</summary>
        public const float SkillXpMax = 1000f;
        /// <summary>Prototype proposal — default XP for a missing category.</summary>
        public const float SkillXpDefault = 0f;
        /// <summary>Prototype proposal — XP needed per derived level.</summary>
        public const float SkillXpPerLevel = 100f;
        /// <summary>Prototype proposal — derived level cap.</summary>
        public const int SkillLevelMax = 10;

        // --- recovery thresholds (prototype proposal) ---
        // Named now so P10B can reference them. Still UNUSED after P10B: the
        // roadmap's low/high hysteresis (stop working below low, only resume above
        // high) is NOT part of P10B — nothing reads these yet.

        /// <summary>Prototype proposal — low recovery threshold (unused until a later phase).</summary>
        public const float RecoveryThresholdLow = 25f;
        /// <summary>Prototype proposal — high recovery threshold (unused until a later phase).</summary>
        public const float RecoveryThresholdHigh = 80f;

        // --- P10B work rates (prototype proposal) ---
        // Linear per-second rates against the tick's simulation delta (which is
        // TimeSystem.SimulationDelta: 0 while paused, already multiplied by 1x/2x/3x).
        // Applied per disciple per tick and clamped to the stamina / XP bounds above.
        // These change DISCIPLE ATTRIBUTES ONLY — no effect on resource yield, craft
        // speed, task assignment or the Auto scheduler.

        /// <summary>Prototype proposal — stamina drain per second while gathering productively.</summary>
        public const float WorkStaminaDrainPerSecondGathering = 0.12f;
        /// <summary>Prototype proposal — stamina drain per second while crafting productively.</summary>
        public const float WorkStaminaDrainPerSecondCrafting = 0.18f;
        /// <summary>Prototype proposal — stamina recovery per second while resting (meditation).</summary>
        public const float WorkStaminaRegenPerSecondResting = 0.40f;
        /// <summary>Prototype proposal — stamina recovery per second while blocked (gate failed / held for materials).</summary>
        public const float WorkStaminaRegenPerSecondBlocked = 0.05f;
        /// <summary>Prototype proposal — gathering skill XP per second while gathering productively.</summary>
        public const float WorkGatheringXpPerSecond = 0.05f;
        /// <summary>Prototype proposal — skill XP for the matching category on each COMPLETED craft.</summary>
        public const float WorkCraftXpPerCompletion = 1f;
    }

    /// <summary>
    /// P10A — per-disciple attribute data.
    ///
    /// Stamina: 0..100 (default 100 for a new game).
    /// SkillXp: category id → accumulated XP, 0..1000 (float because gathering
    /// XP accrues fractionally).
    ///
    /// Skill LEVEL is DERIVED (see <see cref="SkillLevel"/>) and never serialized.
    /// Attached to <see cref="DiscipleState"/> at append-only [Key(12)]; old state
    /// without that key deserializes to a class default and is repaired by
    /// <see cref="Normalize"/> on load/import.
    /// </summary>
    [MessagePackObject]
    public class DiscipleAttributes
    {
        [Key(0)] public float Stamina { get; set; } = DiscipleAttributesConfig.StaminaDefault;
        [Key(1)] public Dictionary<string, float> SkillXp { get; set; } = new Dictionary<string, float>();

        /// <summary>
        /// Derived level from accumulated XP: floor(xp / SkillXpPerLevel), clamped
        /// 0..SkillLevelMax. Pure — no serialized field. Non-finite input → 0.
        /// </summary>
        public static int SkillLevel(float xp)
        {
            if (float.IsNaN(xp) || float.IsInfinity(xp) || xp <= DiscipleAttributesConfig.SkillXpMin)
                return 0;

            int level = (int)Math.Floor(xp / DiscipleAttributesConfig.SkillXpPerLevel);
            if (level < 0) return 0;
            if (level > DiscipleAttributesConfig.SkillLevelMax) return DiscipleAttributesConfig.SkillLevelMax;
            return level;
        }

        /// <summary>
        /// Safe accessor: returns 0 XP when the attributes object, the SkillXp map,
        /// the category name, or the category entry itself is missing. NaN/Infinity
        /// stored in a map value also reads as 0.
        /// </summary>
        public static float GetSkillXp(DiscipleAttributes attributes, string category)
        {
            if (attributes == null || attributes.SkillXp == null || string.IsNullOrEmpty(category))
                return DiscipleAttributesConfig.SkillXpDefault;

            float xp;
            if (!attributes.SkillXp.TryGetValue(category, out xp))
                return DiscipleAttributesConfig.SkillXpDefault;

            if (float.IsNaN(xp) || float.IsInfinity(xp))
                return DiscipleAttributesConfig.SkillXpDefault;

            return xp;
        }

        /// <summary>Derived level for one category on this instance (0 when missing).</summary>
        public int SkillLevelOf(string category)
        {
            return SkillLevel(GetSkillXp(this, category));
        }

        /// <summary>
        /// Explicit normalization — call on load/import and when creating disciples.
        /// Repairs in place and returns the (possibly newly allocated) instance:
        ///   - null attributes → a fresh default instance
        ///   - null SkillXp map → a fresh map
        ///   - NaN/Infinity → the field's default
        ///   - ranges clamped (stamina 0..100, XP 0..1000)
        ///   - the three known category keys exist (missing → 0)
        /// A legitimately saved 0 is PRESERVED — zero is a real value, not "missing"
        /// (e.g. exhausted stamina 0 must not bounce back to the default 100).
        /// Unknown/extra category keys are kept but sanitized the same way.
        /// </summary>
        public static DiscipleAttributes Normalize(DiscipleAttributes attributes)
        {
            if (attributes == null) attributes = new DiscipleAttributes();

            // stamina: non-finite → default, then clamp
            if (float.IsNaN(attributes.Stamina) || float.IsInfinity(attributes.Stamina))
                attributes.Stamina = DiscipleAttributesConfig.StaminaDefault;
            if (attributes.Stamina < DiscipleAttributesConfig.StaminaMin)
                attributes.Stamina = DiscipleAttributesConfig.StaminaMin;
            if (attributes.Stamina > DiscipleAttributesConfig.StaminaMax)
                attributes.Stamina = DiscipleAttributesConfig.StaminaMax;

            // skill map: null → fresh
            if (attributes.SkillXp == null)
                attributes.SkillXp = new Dictionary<string, float>();

            // sanitize every existing value (including unknown categories)
            var keys = new List<string>(attributes.SkillXp.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                float v = attributes.SkillXp[keys[i]];
                if (float.IsNaN(v) || float.IsInfinity(v)) v = DiscipleAttributesConfig.SkillXpDefault;
                if (v < DiscipleAttributesConfig.SkillXpMin) v = DiscipleAttributesConfig.SkillXpMin;
                if (v > DiscipleAttributesConfig.SkillXpMax) v = DiscipleAttributesConfig.SkillXpMax;
                attributes.SkillXp[keys[i]] = v;
            }

            // ensure the three known categories exist; a saved 0 is left untouched
            for (int i = 0; i < DiscipleAttributesConfig.Categories.Length; i++)
            {
                var category = DiscipleAttributesConfig.Categories[i];
                if (!attributes.SkillXp.ContainsKey(category))
                    attributes.SkillXp[category] = DiscipleAttributesConfig.SkillXpDefault;
            }

            return attributes;
        }

        /// <summary>
        /// Deep copy — the SkillXp dictionary is copied, never shared with the source.
        /// </summary>
        public DiscipleAttributes Clone()
        {
            var copy = new DiscipleAttributes { Stamina = Stamina };
            copy.SkillXp = SkillXp != null
                ? new Dictionary<string, float>(SkillXp)
                : new Dictionary<string, float>();
            return copy;
        }
    }
}
