using MessagePack;

namespace Xianxia.Sect.Messages
{
    // P14B — Unity-only in-memory notification that a disciple's cultivation stage
    // actually changed (one validated breakthrough). Deliberately NOT in
    // Shared/GameMessages.cs, for the same reason as SceneMessages.cs /
    // SessionMessages.cs: sync-shared.sh copies Shared/*.cs wholesale, so ANY edit
    // there dirties all three Shared locations (and the bridge has no use for it).
    //
    // In-memory only: a breakthrough is a local gameplay/UI concern, so this is NOT
    // registered on the interprocess broker — there is no RPC and no MCP tool for it,
    // and the bridge reads the resulting stage through get_sect_state like any other
    // state field. UI derives its state from the authoritative provider on bind as
    // well as from this message, so subscription/start order cannot lose progression.
    [MessagePackObject]
    public class DiscipleCultivationChangedMessage
    {
        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
        [Key(1)] public string OldStageId { get; set; } = string.Empty;
        [Key(2)] public string NewStageId { get; set; } = string.Empty;
    }
}
