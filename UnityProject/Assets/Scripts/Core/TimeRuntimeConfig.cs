namespace Xianxia.Sect
{
    /// <summary>
    /// E1 — runtime config for the time/pause system. Plain-C# singleton, same
    /// convention as <see cref="Xianxia.Sect.Visual.VisualRuntimeConfig"/>
    /// (registered as an instance in the composition root installer).
    /// </summary>
    public sealed class TimeRuntimeConfig
    {
        public static readonly TimeRuntimeConfig Instance = new TimeRuntimeConfig();

        /// <summary>
        /// Default true: a decision-requiring world event adds the PendingDecision
        /// pause reason (so the player / AI GM can answer), cleared by
        /// DecisionExecutor / ResumeByPlayer. Set false to let the game keep running
        /// through decision events.
        /// </summary>
        public bool AutoPauseOnDecisionEvent { get; set; } = true;
    }
}
