using MessagePack;

namespace Xianxia.Sect.Messages
{
    // P12A — Unity-only session-lifecycle notification. Deliberately NOT in
    // Shared/GameMessages.cs (same reason as SceneMessages.cs: sync-shared.sh copies
    // Shared/*.cs wholesale, so any edit there dirties all three Shared locations).
    //
    // In-memory only: session lifecycle is a local concern and is never registered on
    // the interprocess broker. UI derives its state from the authoritative coordinator
    // on bind as well as from this message, so subscription/start order cannot lose it.
    [MessagePackObject]
    public class SessionPhaseChangedMessage
    {
        [Key(0)] public GameSessionPhase Previous { get; set; }
        [Key(1)] public GameSessionPhase Current { get; set; }
    }
}
