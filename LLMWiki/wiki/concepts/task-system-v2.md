# Task System — Design Doc (v2, revised)

> Revision of `task-system.md` (the stronger of the two AI-drafted docs).
> Changes in this pass: fixed a real regression bug in the `TickCrafting`
> refactor, added the disciple-ownership/permission model needed for
> viewer-controlled tasks, and sketched how Twitch chat commands plug into
> the existing MessagePipe.Interprocess transport without a new pipeline.
>
> `TaskSystem_GDD.md` (the other draft) is **not** the base for this — it
> cited a wrong `[Key(8)]` for `CurrentTask` (actual is `[Key(5)]`) and
> proposed things the codebase already rejected (ScriptableObject-backed
> data, lab 12 decision). Keep it only as background reading, not a source
> of truth.

---

## 0. Design goal (confirmed)

- **Solo / no viewers**: player (Sect Master) can reassign *any* disciple's
  task freely — Rimworld/ACS style, no restriction.
- **Viewer joined as a disciple**: that viewer can reassign *only their own*
  disciple's task (via Twitch chat command or extension UI). The player can
  still override anyone, including viewer-owned disciples.
- Permission is checked **once, server-side, in Unity** — not trusted to
  whichever client (AI GM / Twitch bot / future UI) sent the request. Same
  principle as `TryPurchaseItem`'s atomic check-and-deduct: one authority,
  no client gets to assume it's correct.

---

## 1. Data model changes

### 1.1 `DiscipleState` — add ownership fields

```csharp
public enum DiscipleOwnerType
{
    Npc,      // AI/no owner - Sect Master controls by default
    Player,   // explicitly the Sect Master's own alt/avatar, if that's ever modeled separately
    Viewer,   // controlled by a specific Twitch viewer
}
```

```csharp
// ⚠️ Verify the next free [Key(N)] against your CURRENT SectEconomyState.cs
// before writing this - this doc's author does not have your live file
// (workspace was reset mid-session). Last confirmed state had Avatar at
// Key(6); if nothing else was added after that, these would be Key(7)/(8),
// but confirm before typing the numbers into code.
[Key(N)]   public DiscipleOwnerType OwnerType { get; set; } = DiscipleOwnerType.Npc;
[Key(N+1)] public string OwnerId { get; set; } = string.Empty; // Twitch user id when OwnerType=Viewer, "" otherwise
```

`OwnerId` is a plain string, same convention as `ItemDefId`/`PartId` elsewhere
in the codebase — resolved/validated by whoever's calling in (Twitch bot,
AI GM), not by Unity. Unity only cares "does this OwnerId match the
requester."

### 1.2 `MockSectData.cs`

All 4 starting disciples default to `OwnerType = Npc` (unset, since that's
the enum's default) — nobody starts viewer-owned. No change needed beyond
what's already there, just confirming the default is correct without
touching every constructor.

---

## 2. `SectStateProvider.TryAssignTask` — with permission check

```csharp
// requesterId conventions:
//   "SECT_MASTER"  → player or the AI GM acting as Sect Master; bypasses
//                    ownership check entirely, can reassign anyone
//   any other string → must match disciple.OwnerId exactly, or rejected
public bool TryAssignTask(string requesterId, string discipleId, string taskId, out string failReason)
{
    failReason = string.Empty;

    var disciple = _state.Disciples.FirstOrDefault(d => d.DiscipleId == discipleId);
    if (disciple == null)
    {
        failReason = $"No disciple with id '{discipleId}'.";
        return false;
    }

    if (requesterId != "SECT_MASTER" && disciple.OwnerId != requesterId)
    {
        failReason = "You don't have permission to reassign this disciple.";
        return false;
    }

    var taskDef = _taskPool.GetTask(taskId);
    if (taskDef == null)
    {
        failReason = $"Unknown task id '{taskId}'.";
        return false;
    }

    // ... existing requirement checks from task-system.md (rank, etc.) ...

    disciple.CurrentTask = taskId;

    _discipleTaskChangedPublisher.Publish(new DiscipleTaskChangedMessage
    {
        DiscipleId = discipleId,
        TaskId = taskId,
    });

    return true;
}
```

**What changed from the original draft**: added `requesterId` as the first
parameter and the ownership check block. Every call site (MCP tool, Twitch
bot, any future in-game UI) must now supply who's asking - there's no
"trusted" caller.

---

## 3. Bug fix — `TickCrafting` must stay on `AdjustAndNotify`

The original draft's refactored `TickCrafting` mutated the stockpile
dictionary directly:

```csharp
// ❌ Don't do this - silently breaks the HUD
_state.Stockpile.RawResources[kvp.Key] -= (int)kvp.Value;
```

This bypasses `SectResourceChangedMessage`, which `ResourceHudPresenter`
depends on for its live delta display (lab 13). The numbers would still be
correct internally, but the HUD would stop updating for crafting-consumed
resources specifically — a quiet regression, not a crash, so it's easy to
ship without noticing.

**Fix**: keep routing every stockpile mutation through the existing
`AdjustAndNotify(resources, key, delta)` private helper, same as
`TickGathering` and `ApplyDecisionConsequence` already do:

```csharp
// ✅ Correct
foreach (var kvp in taskDef.ResourceCost)
{
    AdjustAndNotify(_state.Stockpile.RawResources, kvp.Key, -(int)kvp.Value);
}
```

---

## 4. Threading — apply the `DecisionExecutor` lesson here too

`AssignTaskRequest` will be answered by a new `AssignTaskHandler`
(`IAsyncRequestHandler<AssignTaskRequest, AssignTaskResponse>`), reached
over `MessagePipe.Interprocess`. Confirmed earlier in this project:
handlers invoked this way run on the **TCP receive thread, not Unity's
main thread**.

`TryAssignTask` itself only touches plain C# state (safe off-thread). The
risk is downstream: if any UI (e.g. a future `TaskAssignmentPresenter`)
subscribes to `DiscipleTaskChangedMessage` and does anything that touches
a Unity API - `Instantiate`, animating a progress bar via a MonoBehaviour
method, etc. - it will throw the same
`Internal_CloneSingleWithParent can only be called from the main thread`
exception hit with `DecisionExecutor` earlier.

**Rule going forward**: any request handler reachable over
`MessagePipe.Interprocess` that leads to UI-touching code needs to hop to
the main thread first (`await UniTask.SwitchToMainThread();`) before that
UI code runs - same pattern already in `DecisionExecutor.ExecuteAsync`.
Don't wait to discover this the same way twice.

---

## 5. Twitch chat commands — no new transport needed

Unity already hosts the `MessagePipe.Interprocess` TCP endpoint as a
server (`HostAsServer = true`). A TCP server accepts multiple clients by
design — `McpBridge` doesn't have to be the only one connected.

```
Twitch chat: "!cultivate"
        │
        ▼
Twitch integration service (separate process - Node.js bot or a small
C# service using MessagePipe.Interprocess directly, same library McpBridge
already uses)
        │  looks up: which DiscipleId does this Twitch user own?
        ▼
AssignTaskRequest { RequesterId = twitchUserId, DiscipleId = ..., TaskId = "cultivation" }
        │  sent over the SAME TCP port (127.0.0.1:3215) McpBridge already uses
        ▼
Unity: AssignTaskHandler → SectStateProvider.TryAssignTask(requesterId, ...)
        │  permission check happens here, once, regardless of caller
        ▼
Disciple's task actually changes (or a rejection reason comes back)
```

The AI GM (via the MCP `assign_task` tool) and the Twitch bot are just two
different clients calling the same request type with different
`RequesterId` values. No parallel pipeline, no new port, no new message
type beyond what Section 2 already needs.

**Open question, not blocking now**: how does the Twitch integration
service learn "this Twitch user owns this DiscipleId"? That's the
recruitment/ownership-assignment flow (a viewer joining → becoming a
disciple → getting an `OwnerId` written onto a `DiscipleState`) - out of
scope for this doc, comes later with the actual Twitch integration work.
For now, `OwnerId` can be set manually via `MockSectData.cs` or a debug
tool for testing the permission logic in isolation.

---

## 6. Message additions

```csharp
[MessagePackObject]
public class AssignTaskRequest
{
    [Key(0)] public string RequesterId { get; set; } = string.Empty; // "SECT_MASTER" or a viewer's id
    [Key(1)] public string DiscipleId { get; set; } = string.Empty;
    [Key(2)] public string TaskId { get; set; } = string.Empty;
}

[MessagePackObject]
public class AssignTaskResponse
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string FailReason { get; set; } = string.Empty;
}

// In-memory only, same reasoning as DecisionExecutedMessage - no bridge
// write tool subscribes to this directly, it's for local UI reacting to
// "a task just changed" (progress bar refresh, disciple list update).
[MessagePackObject]
public class DiscipleTaskChangedMessage
{
    [Key(0)] public string DiscipleId { get; set; } = string.Empty;
    [Key(1)] public string TaskId { get; set; } = string.Empty;
}
```

Request/response, not pub/sub — same reasoning as `PurchaseItemRequest`
and `ExecuteDecisionMessage`: the caller (AI GM or Twitch bot) needs to
know immediately whether the reassignment actually succeeded, not just
fire-and-hope.

---

## 7. Everything else unchanged from `task-system.md`

Keep as originally drafted (these parts held up fine on review):

- `TaskDef` structure (id, displayName, type, resourceCost, duration,
  resourceGain/craftResult)
- `TaskPool` loader shape (id → def lookup) — **note**: currently drafted
  as plain `JsonUtility` + `Resources.Load`, not through the Luban
  pipeline `LubanEventPool`/`AvatarPartPool` use. That's an inconsistency
  worth a second look later (task defs are the same kind of tabular
  content events/avatar-parts already went through Luban for), but not
  something this pass needs to force - flagging it, not blocking on it.
- UI Draft/Diff-Commit pattern for the task reassignment panel
- "What NOT to Touch" scoping section
- `TickGathering`/other unrelated `SectStateProvider` methods

---

## 8. Updated implementation checklist

- [ ] Confirm the actual next-free `[Key(N)]` on `DiscipleState` against
      the live `SectEconomyState.cs` (not this doc's placeholder)
- [ ] Add `DiscipleOwnerType` enum + `OwnerType`/`OwnerId` fields
- [ ] `TaskDef`/`TaskPool` (plain C# loader, per original draft)
- [ ] `SectStateProvider.TryAssignTask(requesterId, ...)` with ownership
      check — **use `AdjustAndNotify` for every resource mutation**, not
      direct dictionary writes
- [ ] `AssignTaskRequest`/`Response`/`DiscipleTaskChangedMessage` in
      `GameMessages.cs`
- [ ] `AssignTaskHandler` (`IAsyncRequestHandler`) + register in
      `GameLifetimeScope` (RPC pattern, same as `PurchaseItemHandler`)
- [ ] `assign_task` MCP tool in `McpBridge/Program.cs`
      (`SectActionTools`, requesterId always `"SECT_MASTER"` from this path
      for now)
- [ ] Apply `UniTask.SwitchToMainThread()` in any UI code that reacts to
      `DiscipleTaskChangedMessage` if it touches Unity APIs
- [ ] Test: reassign via MCP tool as `SECT_MASTER` → should always succeed
- [ ] Test: manually set a disciple's `OwnerId` in `MockSectData.cs`, then
      call `TryAssignTask` with a non-matching `requesterId` → should be
      rejected with the permission fail reason
- [ ] (Later, separate piece of work) Twitch integration service + the
      actual viewer→disciple ownership assignment flow
