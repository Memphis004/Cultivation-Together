---
title: GameMessages.cs
type: snippet
sources:
  - UnityProject/Assets/Scripts/Shared/GameMessages.cs
related:
  - "[[concepts/message-pipe-bus]]"
  - "[[concepts/state-management]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [messages, dto, messagepack, pubsub, request-response]
---

# GameMessages.cs

> All pub/sub DTOs and RPC request/response pairs carried by MessagePipe.
> **File path**: `UnityProject/Assets/Scripts/Shared/GameMessages.cs` (170 lines)
> **Synced from**: `Shared/GameMessages.cs` (workspace root)

## Purpose

Single source of truth for all message types that flow over the bus. Three
categories:

1. **Pub/Sub events** (fire-and-forget, both directions):
   - `DiscipleRecruitedMessage`
   - `DiscipleRankChangedMessage`
   - `SectResourceChangedMessage`
   - `ContributionEarnedMessage`
   - `WorldEventTriggeredMessage` (in-memory only)
   - `TimeSpeedChangedMessage` (in-memory only)
   - `ExecuteDecisionMessage` (bridge → Unity only)

2. **Request/Response pairs** (atomic):
   - `SectStateQuery` → `SectStateSnapshot`
   - `AwaitWorldEventRequest` → `AwaitWorldEventResponse`
   - `PurchaseItemRequest` → `PurchaseItemResponse`

3. **Topic keys** (for keyed interprocess channels):
   - `InterprocessTopics` static class

## Why All DTOs Have `[MessagePackObject]`/`[Key]`

From `GameMessages.cs:11-16`:

> MessagePipe.Interprocess serializes with MessagePack when a message
> crosses the TCP boundary to the MCP bridge process. In-memory-only
> messages don't strictly need this, but keeping it consistent means any
> message can be promoted to interprocess later without a rewrite.

## Why `WorldEventTriggeredMessage` is NOT on the Interprocess Bus

From `GameMessages.cs:88-102` (comment block):

> This is deliberately request-response, not pub/sub... Confirmed root cause:
> MessagePipe.Interprocess's TcpDistributedSubscriber unconditionally calls
> worker.StartReceiver() (binds its own listening socket) regardless of
> HostAsServer — so a non-hub process (the bridge) can never safely use
> IDistributedSubscriber over this TCP transport, it collides with the hub's
> already-bound port. Request-response only ever uses the client connection
> (Connect, not Listen), which is exactly what get_sect_state already proved
> works fine.

## Code (full file)

```csharp
using System.Collections.Generic;
using MessagePack;

namespace Xianxia.Sect.Messages
{
    [MessagePackObject]
    public class DiscipleRecruitedMessage
    {
        [Key(0)] public string DiscipleId { get; set; }
        [Key(1)] public string DisplayName { get; set; }
    }

    [MessagePackObject]
    public class DiscipleRankChangedMessage
    {
        [Key(0)] public string DiscipleId { get; set; }
        [Key(1)] public DiscipleRank NewRank { get; set; }
    }

    [MessagePackObject]
    public class SectResourceChangedMessage
    {
        [Key(0)] public string ResourceId { get; set; }
        [Key(1)] public int Delta { get; set; }
        [Key(2)] public int NewTotal { get; set; }
    }

    [MessagePackObject]
    public class ContributionEarnedMessage
    {
        [Key(0)] public string DiscipleId { get; set; }
        [Key(1)] public long Amount { get; set; }
        [Key(2)] public string Reason { get; set; }
    }

    [MessagePackObject]
    public class WorldEventTriggeredMessage
    {
        [Key(0)] public string EventId { get; set; }
        [Key(1)] public bool RequiresDecision { get; set; }
        [Key(2)] public string Description { get; set; }
        [Key(3)] public List<EventChoiceInfo> Choices { get; set; } = new List<EventChoiceInfo>();
    }

    [MessagePackObject]
    public class TimeSpeedChangedMessage
    {
        [Key(0)] public int Speed { get; set; }
        [Key(1)] public bool Paused { get; set; }
    }

    [MessagePackObject]
    public class SectStateQuery
    {
        [Key(0)] public string RequestId { get; set; }
    }

    [MessagePackObject]
    public class SectStateSnapshot
    {
        [Key(0)] public string RequestId { get; set; }
        [Key(1)] public byte[] EconomyStateBytes { get; set; }
    }

    [MessagePackObject]
    public class AwaitWorldEventRequest
    {
        [Key(0)] public string RequestId { get; set; }
    }

    [MessagePackObject]
    public class AwaitWorldEventResponse
    {
        [Key(0)] public string EventId { get; set; }
        [Key(1)] public bool RequiresDecision { get; set; }
        [Key(2)] public string Description { get; set; }
        [Key(3)] public List<EventChoiceInfo> Choices { get; set; } = new List<EventChoiceInfo>();
    }

    [MessagePackObject]
    public class EventChoiceInfo
    {
        [Key(0)] public string ChoiceId { get; set; }
        [Key(1)] public string Label { get; set; }
    }

    [MessagePackObject]
    public class ExecuteDecisionMessage
    {
        [Key(0)] public string EventId { get; set; }
        [Key(1)] public string ChoiceId { get; set; }
    }

    [MessagePackObject]
    public class PurchaseItemRequest
    {
        [Key(0)] public string DiscipleId { get; set; }
        [Key(1)] public string ItemDefId { get; set; }
        [Key(2)] public int Grade { get; set; }
        [Key(3)] public int Quantity { get; set; }
    }

    [MessagePackObject]
    public class PurchaseItemResponse
    {
        [Key(0)] public bool Success { get; set; }
        [Key(1)] public string Message { get; set; }
        [Key(2)] public long RemainingContribution { get; set; }
    }

    public static class InterprocessTopics
    {
        public const string DiscipleRecruited = "sect.disciple_recruited";
        public const string DiscipleRankChanged = "sect.disciple_rank_changed";
        public const string ResourceChanged = "sect.resource_changed";
        public const string ContributionEarned = "sect.contribution_earned";
        public const string WorldEvent = "sect.world_event";
        public const string ExecuteDecision = "sect.execute_decision";
    }
}
```

## Key Patterns

1. **Separation of message and state**: `DiscipleRecruitedMessage` is a
   delta event; `SectStateSnapshot.EconomyStateBytes` is a full state snapshot.
   They're never the same shape.
2. **All messages have MessagePack attributes** — even in-memory ones. Cost is
   near zero, benefit is "can promote to interprocess anytime".
3. **RequestId in request/response** — useful for logging and async tracking
4. **Choices as `List<EventChoiceInfo>`** — passed through the bus so AI GM
   sees real labels, not bare IDs

## Related Pages

- [[concepts/message-pipe-bus|MessagePipe Bus]]
- [[concepts/state-management|State Management]]
- [[concepts/mcp-bridge|MCP Bridge]]
- [[sources/architecture|Architecture]]
