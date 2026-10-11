namespace Xianxia.Sect
{
    /// <summary>
    /// P12B — runtime config for how the app boots. Plain-C# singleton, same convention as
    /// <see cref="TimeRuntimeConfig"/> / <see cref="WorldEventRuntimeConfig"/> /
    /// <see cref="Xianxia.Sect.Visual.VisualRuntimeConfig"/> (registered as an instance in the
    /// composition root installer, so it is data the coordinator reads — not a hard-coded
    /// constant and not a branch inside the coordinator).
    /// </summary>
    public sealed class SessionRuntimeConfig
    {
        /// <summary>
        /// P12B §6 — the environment variable that overrides <see cref="StartAtTitle"/> without a
        /// recompile. Set it to <c>0</c> / <c>false</c> (or unset) to keep the pre-P12B
        /// quick-start behaviour for dev iteration.
        /// </summary>
        public const string StartAtTitleEnvVar = "XIANXIA_START_AT_TITLE";

        public static readonly SessionRuntimeConfig Instance = new SessionRuntimeConfig();

        /// <summary>
        /// Public (like the other runtime configs) so composition/tests can construct one with a
        /// chosen value instead of mutating the shared instance.
        /// </summary>
        public SessionRuntimeConfig()
        {
            // Read the override ONCE, here, so the value is the same for the whole process
            // (the composition root takes this instance and hands it to the coordinator).
            string overrideValue = System.Environment.GetEnvironmentVariable(StartAtTitleEnvVar);
            if (!string.IsNullOrEmpty(overrideValue)
                && (overrideValue.Trim() == "0"
                    || string.Equals(overrideValue.Trim(), "false", System.StringComparison.OrdinalIgnoreCase)))
            {
                StartAtTitle = false;
            }
        }

        /// <summary>
        /// Default true: boot lands on the Title screen and waits for the player to pick
        /// New Game / Continue. No session exists until then, so gameplay ticking is off and the
        /// session HUD stays hidden (<see cref="GameSessionPhase.Title"/>).
        ///
        /// False keeps today's quick-start behaviour: <c>GameSessionCoordinator.Start()</c>
        /// immediately requests a New Game, which is what lets dev/verify Play-mode runs skip
        /// the menu. Set <c>XIANXIA_START_AT_TITLE=0</c> in the environment for that.
        /// </summary>
        public bool StartAtTitle { get; set; } = true;
    }
}
