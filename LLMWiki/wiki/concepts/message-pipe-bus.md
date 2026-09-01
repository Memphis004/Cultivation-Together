---
title: MessagePipe Bus
type: concept
sources:
  - UnityProject/Assets/Scripts/Core/GameLifetimeScope.cs
  - UnityProject/Assets/Scripts/Shared/GameMessages.cs
related:
  - "[[concepts/vcontainer-composition]]"
  - "[[concepts/mcp-bridge]]"
  - "[[concepts/state-management]]"
created: 2026-08-31
updated: 2026-09-02
confidence: high
tags: [messagepipe, pubsub, request-response, ipc, avatar]
---

# MessagePipe Bus

> The internal pub/sub + request-response backbone. Used for in-Unity
> subsystem communication AND for cross-process Unity ↔ McpBridge IPC.

## Two Layers

### 1. In-Memory Bus (Unity side only)

For events that only matter inside Unity (e.g. `TimeSpeedChangedMessage`,
`WorldEventTriggeredMessage`).

Setup:
```csharp
var options = builder.RegisterMessagePipe(pipeOptions =>
{
    pipeOptions.InstanceLifetime = InstanceLifetime.Singleton;
});
// then inject IPublisher<T> / ISubscriber<T>
```

### 2. Interprocess Bus (Unity ↔ McpBridge)

For events/messages that must cross the TCP boundary.

Setup:
```csharp
var interprocess = messagePipeBuilder.AddTcpInterprocess(
    "127.0.0.1", 3215,
    tcp => { tcp.HostAsServer = true; });

// Pub/Sub (keyed for multiple topics)
messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, DiscipleRecruitedMessage>(interprocess);

// Request/Response
messagePipeBuilder.RegisterTcpRemoteRequestHandler<SectStateQuery, SectStateSnapshot>(interprocess);
builder.RegisterAsyncRequestHandler<SectStateQuery, SectStateSnapshot, SectStateQueryHandler>(options);
```

## Critical: Register Request Handlers on BOTH Sides

From `GameLifetimeScope.cs:78-82` (comment):

> Correction from an earlier pass: `RegisterTcpRemoteRequestHandler` is
> needed here too, even though Unity is `HostAsServer=true` and holds the
> real handler. It's what wires the TCP worker to the registered
> `IAsyncRequestHandler`, not just a caller-side proxy — confirmed against
> `Wanxiang.Guanxiangtai`'s `FrontendIpcServer.cs`, which registers both
> on its (`HostAsServer=true`) side.

**Same applies to `RegisterTcpInterprocessMessageBroker` on the bridge side.**

## Topic Keys for Keyed Pub/Sub

From `GameMessages.cs`:

```csharp
public static class InterprocessTopics
{
    public const string DiscipleRecruited = "sect.disciple_recruited";
    public const string DiscipleRankChanged = "sect.disciple_rank_changed";
    public const string ResourceChanged = "sect.resource_changed";
    public const string ContributionEarned = "sect.contribution_earned";
    public const string WorldEvent = "sect.world_event";
    public const string ExecuteDecision = "sect.execute_decision";
    public const string AvatarEquipmentChanged = "sect.avatar_equipment_changed";
}
```
> 📎 Source: Assets/Scripts/Shared/GameMessages.cs

The McpBridge side uses these strings to subscribe/publish on the right channel.
`AvatarEquipmentChanged` is broadcast by `SectStateProvider.TryChangeAvatarPart()` (Unity → bridge/UI); see [[entities/avatar-appearance]] for the avatar request/response pair `ChangeAvatarPartRequest/Response`.

## What's NOT on the Interprocess Bus

From `GameMessages.cs:88-102`:

- `WorldEventTriggeredMessage` — in-memory only. Bridge uses
  `AwaitWorldEventRequest` (request-response) instead
- `TimeSpeedChangedMessage` — in-memory only. UI internal state

**Why**: `MessagePipe.Interprocess`'s `TcpDistributedSubscriber` binds its
own listening socket regardless of `HostAsServer`. Bridge side would
collide with Unity's port. Request-response uses only the client connection
(`Connect`, not `Listen`), so no collision.

## When to Use Pub/Sub vs Direct Call vs Request-Response

| Pattern | Use when |
|---|---|
| **Direct method call** | In-Unity, both ends exist, both are in the same process |
| **In-memory pub/sub** | In-Unity, multiple possible subscribers, fire-and-forget |
| **Interprocess pub/sub** | One or both ends may be in different process, multiple subscribers, fire-and-forget |
| **Interprocess request-response** | Bridge must know outcome (e.g. state query, atomic purchase) |

**Critical anti-pattern** (lab 13 bug): don't use in-memory pub/sub for an
event that has an in-Unity subscriber on a DIFFERENT channel (e.g. the
subscriber is on the interprocess broker). They'll never see each other. Use
direct call.

## Serialization

All DTOs use `[MessagePackObject]`/`[Key]` attributes. MessagePack
serializes for both in-memory (technically not needed) and interprocess
(mandatory). Cost is near zero; benefit is "promote to interprocess anytime".

## Related Pages

- [[concepts/vcontainer-composition|VContainer Composition Root]]
- [[concepts/mcp-bridge|MCP Bridge]]
- [[sources/architecture|Architecture]]
- [[sources/bug-log|Bug Log]] (BUG-L3-03, BUG-L6-01, BUG-L13-01)
