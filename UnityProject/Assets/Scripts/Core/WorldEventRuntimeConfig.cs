namespace Xianxia.Sect
{
    /// <summary>
    /// E2-lite — runtime config for the world-event spawner. Plain-C# singleton,
    /// same convention as <see cref="TimeRuntimeConfig"/> /
    /// <see cref="Xianxia.Sect.Visual.VisualRuntimeConfig"/> (registered as an
    /// instance in the composition root installer).
    /// </summary>
    public sealed class WorldEventRuntimeConfig
    {
        public static readonly WorldEventRuntimeConfig Instance = new WorldEventRuntimeConfig();

        /// <summary>
        /// Simulation-time delay before the FIRST world event. No event is raised
        /// from a constructor any more; this grace replaces the old "fire
        /// immediately on startup" so the UI exists first (VContainer entry-point
        /// Start order is NOT guaranteed). Measured in simulation time, so it does
        /// not advance while the game is paused.
        /// </summary>
        public float StartGraceSeconds { get; set; } = 10f;
    }
}
