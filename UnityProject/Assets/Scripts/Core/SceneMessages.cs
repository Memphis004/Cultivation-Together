using MessagePack;

namespace Xianxia.Sect.Messages
{
    // Unity-only scene-lifecycle messages — deliberately NOT in Shared/GameMessages.cs
    // (Phase 2 C2/C4 conflict resolution):
    //
    //  - C4 asked for SceneUnloadedMessage to be published by SceneLoader and told us
    //    to CHECK whether sync-shared.sh covers GameMessages.cs before editing it.
    //  - The check is done: sync-shared.sh copies Shared/*.cs wholesale, so ANY edit
    //    to GameMessages.cs would dirty all three Shared locations (root / Unity /
    //    McpBridge) — directly violating C2 ("git diff on Shared must be empty").
    //
    //  Resolution: keep the wire-shape identical to SceneLoadedMessage (same
    //  MessagePack field layout) but declare it in a Unity-only file. The bridge
    //  never sees these messages anyway — scenes are additive in-process, nothing
    //  here is registered on the interprocess broker (C11).

    /// <summary>
    /// Published by SceneLoader.UnloadCurrentGameplayScene() after a successful unload.
    /// Subscribers (DiscipleVisualSystem) drop references to scene objects being destroyed.
    /// </summary>
    [MessagePackObject]
    public class SceneUnloadedMessage
    {
        [Key(0)] public string SceneName { get; set; }
    }
}
