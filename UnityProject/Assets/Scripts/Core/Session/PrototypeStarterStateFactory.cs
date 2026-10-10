namespace Xianxia.Sect
{
    /// <summary>
    /// P12A — the prototype starter-state factory. It is an ADAPTER over the existing
    /// <see cref="MockSectData"/> so New Game cannot drift from the prototype start state:
    /// the mock data stays the single source of the authored start, and this class only
    /// wraps it in a detached, validated session snapshot.
    ///
    /// A brand-new snapshot carries: the prototype roster (with attributes), the start
    /// stockpile, NO placed buildings, an empty viewer registry (no ownership), speed 1x
    /// and NO pending decision. Everything else that must be reset for a fresh session —
    /// gather/craft accumulators, the real-UTC task-change cooldown, per-tick scratch,
    /// AI scheduling/dwell maps, session-scoped observability — is reset by the commit
    /// path (<c>SessionSnapshotService.TryApplyNewSession</c> /
    /// <c>SectStateProvider.TryApplySessionSnapshot</c>) and the session boundary.
    /// </summary>
    public sealed class PrototypeStarterStateFactory : IStarterStateFactory
    {
        public SectSessionSnapshot CreateStarterSnapshot()
        {
            var economy = MockSectData.Create();
            // P10A repair on the detached copy only (same normalization every load/import gets).
            SectEconomyState.NormalizeDisciples(economy);

            return new SectSessionSnapshot
            {
                Economy = economy,
                SimulationSpeed = 1,
            };
        }
    }
}
