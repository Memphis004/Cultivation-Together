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
        [Key(1)] public byte[] EconomyStateProtobuf { get; set; } // SectEconomyState, serialized
    }

    // Topic keys for the keyed (IDistributedPublisher<TKey,TMessage>) channels.
    public static class InterprocessTopics
    {
        public const string DiscipleRecruited = "sect.disciple_recruited";
        public const string DiscipleRankChanged = "sect.disciple_rank_changed";
        public const string ResourceChanged = "sect.resource_changed";
        public const string ContributionEarned = "sect.contribution_earned";
        public const string WorldEvent = "sect.world_event";
    }
}
