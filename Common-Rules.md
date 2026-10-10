Common rules for P10 onward:

1. Inspect current repository code before editing.
   Prior chat examples and this roadmap are design intent, not proof of
   existing implementation. Reuse existing systems; do not create duplicates.

2. Apply one phase only. Report unexpected dependencies before expanding scope.
   Do not implement later phases automatically.

3. Unity/shared code must remain C# 8 compatible.
   Preserve existing MessagePack keys and enum values.
   Determine new keys from current source.
   Follow the repository's authoritative Shared location and sync mechanism.

4. Unity owns gameplay state.
   Capture/restore/mutate live gameplay state on the Unity main thread.
   Background file I/O may operate only on detached data.
   Never enumerate live mutable state from a worker thread.

5. Reuse actual VContainer, MessagePipe and UniTask patterns in this repo.
   Do not guess API signatures.
   State changes go through authoritative services.
   Resource mutations must use AdjustAndNotify.

6. Keep time domains explicit:
   simulation time for gameplay progression;
   real UTC time for viewer inactivity and existing task cooldowns.
   Never multiply game speed twice.

7. Preserve Hybrid permissions.
   AI GM acting as SectMaster has no unconditional viewer override.
   DiscipleBrain controls only eligible NPCs explicitly in Auto mode.

8. Reuse existing observability, UI lifecycle, SceneLoader and persistence
   infrastructure where appropriate.
   Dispose subscriptions and cancel old-session async work correctly.

9. Run available relevant builds/tests and report actual commands/results.
   Do not claim Unity compilation or tests passed unless actually run.

10. Finish with changed files, behavior changes, tests run,
    manual verification steps and unresolved issues. Then STOP.

11. Time: gameplay progression uses TimeSystem.SimulationDelta only.
    Never read Time.deltaTime in gameplay code. Never set Time.timeScale.
    Pause has explicit reasons (User, PendingDecision): use Pause/Resume
    (reason) and ResumeByPlayer. Do not call the legacy SetPaused for new
    code. Real-time clocks (UTC cooldown, viewer inactivity) are separate.

12. Threading: any callback arriving from TCP/MessagePipe.Interprocess
    must hop to the Unity main thread before it mutates gameplay state,
    publishes in-memory messages, or touches UI. Follow the E1-pre
    pattern. New handlers must do the same.

13. UI that depends on game state derives it from the authoritative
    state (TimeSystem, SectStateProvider), not only from messages, so
    subscription/start order cannot lose it.

14. New panels follow the generator pattern (Editor generator, prefab
    regenerated, UIPresenterKind appended at the END, catalog entry,
    Thai TMP font, shared palette). Never hand-edit prefab YAML.

15. Do not edit Shared/ unless the phase explicitly lists it. Do not
    commit McpBridge/bin or obj.