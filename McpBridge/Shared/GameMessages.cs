using System.Collections.Generic;
using MessagePack;

namespace Xianxia.Sect.Messages
{
    // These are lightweight event/delta messages carried over MessagePipe.
    // Deliberately separate from SectEconomyState (economy.proto): the proto
    // state is a full snapshot used for MCP state queries, these are small
    // deltas broadcast on every change so subscribers don't need to diff
    // full state themselves.
    //
    // [MessagePackObject]/[Key] are required because MessagePipe.Interprocess
    // serializes with MessagePack when a message crosses the TCP boundary to
    // the MCP bridge process. In-memory-only messages don't strictly need
    // this, but keeping it consistent means any message can be promoted to
    // interprocess later without a rewrite.

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

    // Raised on random world events. RequiresDecision marks the ones that
    // should auto-pause the game and wait for the AI GM / vote window.
    // In-memory bus only now (not registered on the interprocess broker) -
    // the bridge learns about world events via AwaitWorldEventRequest/
    // Response (request-response) instead, since IDistributedSubscriber
    // can't be used from the bridge side (see the comment on
    // AwaitWorldEventRequest below). Still useful in-process for e.g. a
    // future Unity-side UI popup that wants to react to the same event.
    [MessagePackObject]
    public class WorldEventTriggeredMessage
    {
        [Key(0)] public string EventId { get; set; }
        [Key(1)] public bool RequiresDecision { get; set; }
    }

    // Internal-only (not registered on the interprocess bus) - the UI and
    // other in-Unity systems care about this, the MCP bridge doesn't.
    [MessagePackObject]
    public class TimeSpeedChangedMessage
    {
        [Key(0)] public int Speed { get; set; }
        [Key(1)] public bool Paused { get; set; }
    }

    // Request/response pair: the MCP bridge asks Unity for a full state
    // snapshot on demand (e.g. when the AI GM calls the get_sect_state tool).
    [MessagePackObject]
    public class SectStateQuery
    {
        [Key(0)] public string RequestId { get; set; }
    }

    [MessagePackObject]
    public class SectStateSnapshot
    {
        [Key(0)] public string RequestId { get; set; }
        [Key(1)] public byte[] EconomyStateBytes { get; set; } // SectEconomyState, MessagePack-serialized
    }

    // Request/response pair for await_next_world_event.
    //
    // This is deliberately request-response, not pub/sub, even though
    // conceptually it's "wait for the next event". Confirmed root cause:
    // MessagePipe.Interprocess's TcpDistributedSubscriber unconditionally
    // calls worker.StartReceiver() (binds its own listening socket)
    // regardless of HostAsServer - so a non-hub process (the bridge) can
    // never safely use IDistributedSubscriber over this TCP transport, it
    // collides with the hub's (Unity's) already-bound port. Request-response
    // only ever uses the client connection (Connect, not Listen), which is
    // exactly what get_sect_state already proved works fine. The handler on
    // the Unity side just holds the response open (via a UniTaskCompletionSource)
    // until an event actually fires - same observed behavior as a
    // subscription, without the broken transport.
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

    // One selectable option for a world event - authored per-event in the
    // EventData ScriptableObject, carried over the wire so the AI GM sees
    // real choices instead of just a bare event id.
    [MessagePackObject]
    public class EventChoiceInfo
    {
        [Key(0)] public string ChoiceId { get; set; }
        [Key(1)] public string Label { get; set; }
    }

    // Sent from the MCP bridge back into Unity when the AI GM (or a vote
    // result) picks an option for the current world event. Completes the
    // write path that SectActionTools.ExecuteDecision was a stub for.
    [MessagePackObject]
    public class ExecuteDecisionMessage
    {
        [Key(0)] public string EventId { get; set; }
        [Key(1)] public string ChoiceId { get; set; }
    }

    // Request/response pair: a disciple buying an item from the sect
    // stockpile (CraftedGoods) with their own contribution. Request-response
    // rather than a fire-and-forget message because the bridge needs to
    // know immediately whether the purchase actually succeeded (enough
    // contribution, enough stock) - same reasoning as SectStateQuery.
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

    // Topic keys for the keyed (IDistributedPublisher<TKey,TMessage>) channels.
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
