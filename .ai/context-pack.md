# Cultivation-Together Context Pack

- Repository: Memphis004/Cultivation-Together
- Commit: c4e19fa
- Branch: main
- Generated: 2026-10-08

## Ground Rules (สำคัญมาก — บังคับ AI)

1. ใช้เฉพาะข้อมูลจากเอกสารนี้เท่านั้น ห้ามเดา
2. ห้ามสร้างชื่อไฟล์, class, method, API, หรือ behavior ที่ไม่มีใน context
3. เวลาอธิบายโค้ด ให้อ้างอิง path และ line range (เช่น `Shared/GameMessages.cs:45-60`)
4. ถ้าข้อมูลไม่พอ ให้ตอบเป็น JSON block แบบนี้ แล้วหยุด:

```need_files
{
  "paths": ["path/to/file.cs"],
  "reason": "เหตุผลที่ต้องอ่านไฟล์นี้"
}
```

5. โครงสร้าง repo:
   - `Shared/` = canonical source (แก้ตรงนี้เท่านั้น อย่าแก้ใน UnityProject/Shared/ หรือ McpBridge/Shared/)
   - `UnityProject/` = Unity game
   - `McpBridge/` = .NET AI GM bridge (TCP 127.0.0.1:3215)
   - `LLMWiki/` = wiki สำหรับ LLM
   - `DataTables/` = luban pipeline xlsx → csv

## Known Issues (จาก README)

- `SectStateQueryHandler.InvokeAsync` เรียก `state.ToByteArray()` ซึ่งยังไม่มีใน `SectEconomyState` (ต้อง run protoc หรือเขียน stub เอง)
- `SectActionTools.ExecuteDecision` ยังเป็น `NotImplementedException` (read path only)

## Key Symbols (C#)

### Shared/GameMessages.cs
```csharp
    public class DiscipleRecruitedMessage
    public class DiscipleRankChangedMessage
    public class SectResourceChangedMessage
    public class ContributionEarnedMessage
    public class SceneLoadedMessage
    public class WorldEventTriggeredMessage
    public class TimeSpeedChangedMessage
    public class SectStateQuery
    public class SectStateSnapshot
    public class AwaitWorldEventRequest
    public class AwaitWorldEventResponse
    public class EventChoiceInfo
    public class ExecuteDecisionMessage
    public class DecisionExecutedMessage
    public class BuildModeStartedMessage
    public class BuildModeEndedMessage
    public class PurchaseItemRequest
    public class PurchaseItemResponse
    public class BuildingPlacedMessage
    public class DiscipleChibiBackendChangedMessage
```

### Shared/MessageTypes.cs
```csharp
```

### Shared/MockSectData.cs
```csharp
```

### Shared/SectEconomyState.cs
```csharp
    public enum OwnerScope { Unspecified, Personal, SectStockpile }
    public enum DiscipleRank { Unspecified, OuterDisciple, InnerDisciple, Elder, SectMaster }
    public enum DiscipleSex { Unspecified, Male, Female }
    public enum DiscipleOwnerType { Npc, Player, Viewer }
    public enum ChibiBackend
    public class CurrencyWallet
    public class InventoryItem
    public class DiscipleState
    public class SectStockpile
    public class PlacedBuildingState
    public class SectEconomyState
    public struct SlotPart
          public SlotPart(string slot, string partId)
```

## Selected Source Files

### Shared/GameMessages.cs
```
using System.Collections.Generic;
using MessagePack;
using Xianxia.Sect;

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

    // Scene lifecycle message (in-memory only — additive scenes don't cross TCP)
    [MessagePackObject]
    public class SceneLoadedMessage
    {
        [Key(0)] public string SceneName { get; set; }
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
        [Key(2)] public string Description { get; set; }
        [Key(3)] public List<EventChoiceInfo> Choices { get; set; } = new List<EventChoiceInfo>();
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

    // Published in-memory by DecisionExecutor (not sent over interprocess -
    // this is the "it already happened" notification for local listeners
    // like the in-game log window, distinct from ExecuteDecisionMessage
    // which is the incoming command from the bridge). Whether the decision
    // originated from the bridge or from clicking a choice in the UI,
    // DecisionExecutor is the single place both paths funnel through, so
    // this fires exactly once per decision regardless of source.
    [MessagePackObject]
    public class DecisionExecutedMessage
    {
        [Key(0)] public string EventId { get; set; }
        [Key(1)] public string ChoiceId { get; set; }
    }

    // Internal-only: entering build (placement) mode. CameraRigController
    // zooms to the Placement preset and GridOverlayRenderer shows the grid;
    // Payload carries the ghost transform when one exists (may be null).
    [MessagePackObject]
    public class BuildModeStartedMessage
    {
        [Key(0)] public string SourceId { get; set; }
        [Key(1)] public string GhostId { get; set; }
    }

    // Internal-only: leaving build mode (confirm, cancel, or system stop).
    [MessagePackObject]
    public class BuildModeEndedMessage
    {
        [Key(0)] public string SourceId { get; set; }
        [Key(1)] public bool Confirmed { get; set; }
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

    // ---------- Building placed (Phase 1 — grid placement engine) ----------
    // Published in-memory by SectStateProvider.TryPlaceBuilding after a
    // successful commit. BuildingSystem listens and rebuilds grid occupancy +
    // visuals. ⚠️ Deliberately NOT added to InterprocessTopics — building
    // placement is player-only in Phase 1 (building-system.md §8 Q3 default),
    // so the MCP bridge never needs it on the wire.
    [MessagePackObject]
    public class BuildingPlacedMessage
    {
        [Key(0)] public string InstanceId { get; set; } = string.Empty;
        [Key(1)] public string DefId { get; set; } = string.Empty;
        [Key(2)] public int GridX { get; set; }
        [Key(3)] public int GridZ { get; set; }
        [Key(4)] public int Rotation { get; set; }
    }

    // ---------- Chibi backend entitlement change (Phase 3) ----------
    // Published in-memory by SectStateProvider.TrySetChibiBackend after mutating
    // DiscipleState.ChibiBackend. DiscipleVisualSystem respawns the visual IN PLACE
    // (same position/activity/facing) on this message.
    // ⚠️ Deliberately NOT added to InterprocessTopics — visual-tier churn is
    // irrelevant to the MCP bridge; keep it off the TCP broker (same rule as
    // every DiscipleVisualSystem message).
    [MessagePackObject]
    public class DiscipleChibiBackendChangedMessage
    {
        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
        [Key(1)] public ChibiBackend Old { get; set; }
        [Key(2)] public ChibiBackend New { get; set; }
    }

    // ---------- Disciple selected (Phase 4 — chibi click) ----------
    // Published in-memory by ChibiClickTarget when the player clicks a chibi
    // in the gameplay scene. DiscipleDetailUISystem opens the DiscipleDetail
    // panel on this message. ⚠️ Deliberately NOT added to InterprocessTopics —
    // same in-memory-only rule as every DiscipleVisualSystem message.
    [MessagePackObject]
    public class DiscipleSelectedMessage
    {
        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
    }

    // ---------- Avatar equipment change ----------
    // Broadcast after a successful avatar part change (pub/sub, goes to UI + MCP client)
    [MessagePackObject]
    public class AvatarEquipmentChangedMessage
    {
        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
        [Key(1)] public string Slot { get; set; } = string.Empty;   // AvatarSlots.*
        [Key(2)] public string OldPartId { get; set; } = string.Empty;
        [Key(3)] public string NewPartId { get; set; } = string.Empty;
    }

    // Request/response pair for changing an avatar part.
    // Request-response (not fire-and-forget) because the bridge needs
    // to know immediately whether the PartId is valid or the disciple
    // id is wrong - same reasoning as PurchaseItemRequest.
    [MessagePackObject]
    public class ChangeAvatarPartRequest
    {
        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
        [Key(1)] public string Slot { get; set; } = string.Empty;
        [Key(2)] public string PartId { get; set; } = string.Empty;   // "" = back to default
    }

    [MessagePackObject]
    public class ChangeAvatarPartResponse
    {
        [Key(0)] public bool Success { get; set; }
        [Key(1)] public string FailReason { get; set; } = string.Empty;
        [Key(2)] public AvatarAppearance ResultAvatar { get; set; }
    }

    // ---------- Task assignment (task-system-v2.md §6) ----------
    // Request/response pair: the MCP bridge (or AI GM) asks Unity to assign a
    // task to a disciple. Request-response because the caller needs the
    // permission/validity verdict immediately - same reasoning as
    // PurchaseItemRequest. RequesterId drives the ownership check
    // ("SECT_MASTER" may assign anyone; others only their own disciples).
    [MessagePackObject]
    public class AssignTaskRequest
    {
        [Key(0)] public string RequesterId { get; set; } = string.Empty;
        [Key(1)] public string DiscipleId { get; set; } = string.Empty;
        [Key(2)] public string TaskId { get; set; } = string.Empty;
    }

    [MessagePackObject]
    public class AssignTaskResponse
    {
        [Key(0)] public bool Success { get; set; }
        [Key(1)] public string FailReason { get; set; } = string.Empty;
    }

    // Published in-memory by SectStateProvider.TryAssignTask after a successful
    // commit. TaskActivityMapper / visual systems react to the new task.
    // ⚠️ Deliberately NOT added to InterprocessTopics — same in-memory-only
    // rule as DecisionExecutedMessage / BuildingPlacedMessage.
    [MessagePackObject]
    public class DiscipleTaskChangedMessage
    {
        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
        [Key(1)] public string TaskId { get; set; } = string.Empty;
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
        public const string AvatarEquipmentChanged = "sect.avatar_equipment_changed";
    }
}
```

### Shared/SectEconomyState.cs
```
// Plain C# mirror of economy.proto.
// Use this to wire up UI and gameplay logic right away.
using System;
using System.Collections.Generic;
using MessagePack;

namespace Xianxia.Sect
{
    public enum OwnerScope { Unspecified, Personal, SectStockpile }
    public enum DiscipleRank { Unspecified, OuterDisciple, InnerDisciple, Elder, SectMaster }
    public enum DiscipleSex { Unspecified, Male, Female }

    /// <summary>
    /// Who a disciple belongs to (task-system-v2.md §1.1). Default Npc so old
    /// saves/rosters without [Key(9)] deserialize as non-player-owned (L5).
    /// </summary>
    public enum DiscipleOwnerType { Npc, Player, Viewer }

    /// <summary>
    /// Entitlement ของศิษย์: "ได้สิทธิ์" แสดง chibi ในฉากด้วย backend ไหน — ไม่ใช่สิ่งที่ render จริงเสมอไป
    /// (runtime อาจ degrade Spine → SpriteSheet เมื่อเกิน SpineBudget โดยไม่แก้ค่านี้ — ดู §7 ของแผน)
    /// 0 = default → save/roster เก่าที่ไม่มี [Key(8)] deserialize ได้ SpriteSheet (L5)
    /// </summary>
    public enum ChibiBackend
    {
        SpriteSheet = 0,
        Spine = 1
    }

    [MessagePackObject]
    public class CurrencyWallet
    {
        [Key(0)] public long SpiritStones { get; set; }
        [Key(1)] public long Contribution { get; set; }
    }

    [MessagePackObject]
    public class InventoryItem
    {
        [Key(0)] public string ItemDefId { get; set; }
        [Key(1)] public int Quantity { get; set; }
        [Key(2)] public int Grade { get; set; }          // 1-5
        [Key(3)] public OwnerScope OwnerScope { get; set; }
    }

    [MessagePackObject]
    public class DiscipleState
    {
        [Key(0)] public string DiscipleId { get; set; }
        [Key(1)] public string DisplayName { get; set; }
        [Key(2)] public DiscipleRank Rank { get; set; }
        [Key(3)] public CurrencyWallet Wallet { get; set; } = new CurrencyWallet();
        [Key(4)] public List<InventoryItem> PersonalInventory { get; set; } = new List<InventoryItem>();
        [Key(5)] public string CurrentTask { get; set; }
        [Key(6)] public AvatarAppearance Avatar { get; set; } = new AvatarAppearance();
        [Key(7)] public DiscipleSex Sex { get; set; } = DiscipleSex.Unspecified;
        /// <summary>Entitlement (แกนแยกจาก AvatarAppearance) — default SpriteSheet เมื่อ deserialize state เก่า (L1/L5)</summary>
        [Key(8)] public ChibiBackend ChibiBackend { get; set; } = ChibiBackend.SpriteSheet;

        /// <summary>Ownership kind (task-system-v2.md §1.1) — append-only; old state default Npc</summary>
        [Key(9)] public DiscipleOwnerType OwnerType { get; set; } = DiscipleOwnerType.Npc;
        /// <summary>Owner identity (player/viewer id); empty for Npc-owned</summary>
        [Key(10)] public string OwnerId { get; set; } = string.Empty;
    }

    [MessagePackObject]
    public class SectStockpile
    {
        [Key(0)] public Dictionary<string, int> RawResources { get; set; } = new Dictionary<string, int>();
        [Key(1)] public List<InventoryItem> CraftedGoods { get; set; } = new List<InventoryItem>();
    }

    /// <summary>
    /// อาคาร 1 หลังที่วางบน grid สำนักแล้ว (building-system.md §3.3)
    /// GridX/GridZ = มุมบนซ้าย (cell แรก) ของ footprint; Rotation = 0/90/180/270
    /// (90/270 ทำให้ footprint กว้าง×สูงสลับกัน — ดู BuildingGrid.ResolveFootprint)
    /// </summary>
    [MessagePackObject]
    public class PlacedBuildingState
    {
        [Key(0)] public string InstanceId { get; set; }
        [Key(1)] public string DefId { get; set; }
        [Key(2)] public int GridX { get; set; }
        [Key(3)] public int GridZ { get; set; }
        [Key(4)] public int Rotation { get; set; }
    }

    [MessagePackObject]
    public class SectEconomyState
    {
        [Key(0)] public List<DiscipleState> Disciples { get; set; } = new List<DiscipleState>();
        [Key(1)] public SectStockpile Stockpile { get; set; } = new SectStockpile();
        /// <summary>Append-only key (building-system.md §3.3) — ห้ามแก้ [Key(0)]/[Key(1)] เดิม</summary>
        [Key(2)] public List<PlacedBuildingState> PlacedBuildings { get; set; } = new List<PlacedBuildingState>();

        public byte[] ToByteArray() => MessagePackSerializer.Serialize(this);
        public static SectEconomyState FromByteArray(byte[] bytes) =>
            MessagePackSerializer.Deserialize<SectEconomyState>(bytes);
    }

    // --- Avatar System (New Dictionary-Based Schema) ---

    /// <summary>
    /// Struct สำหรับ Factory Method FromSlots (เลี่ยง Value Tuple เพื่อความเข้ากันได้กับ Unity ทุกเวอร์ชัน)
    /// </summary>
    public struct SlotPart
    {
        public string Slot;
        public string PartId;
        public SlotPart(string slot, string partId)
        {
            Slot = slot;
            PartId = partId;
        }
    }

    /// <summary>
    /// หน้าตาของตัวละคร — dictionary-based รองรับ slot จำนวน任意
    /// Parts: slot → partId (ว่าง = ใช้ default ของ slot นั้น)
    /// Colors: slot → colorId (มีเฉพาะ slot ที่ tintable)
    /// </summary>
    [MessagePackObject]
    public sealed class AvatarAppearance
    {
        [Key(0)] public Dictionary<string, string> Parts { get; set; } = new Dictionary<string, string>();
        [Key(1)] public Dictionary<string, string> Colors { get; set; } = new Dictionary<string, string>();
        [Key(2)] public string PoseId { get; set; } = string.Empty;

        public string GetSlot(string slot)
        {
            string v;
            return Parts.TryGetValue(slot, out v) ? v : string.Empty;
        }

        public bool SetSlot(string slot, string partId)
        {
            if (string.IsNullOrEmpty(partId)) { Parts.Remove(slot); return true; }
            Parts[slot] = partId;
            return true;
        }

        public string GetColor(string slot)
        {
            string v;
            return Colors.TryGetValue(slot, out v) ? v : string.Empty;
        }

        public void SetColor(string slot, string colorId)
        {
            if (string.IsNullOrEmpty(colorId)) { Colors.Remove(slot); return; }
            Colors[slot] = colorId;
        }

        public AvatarAppearance Clone()
        {
            var c = new AvatarAppearance();
            c.Parts = new Dictionary<string, string>(Parts);
            c.Colors = new Dictionary<string, string>(Colors);
            c.PoseId = PoseId;
            return c;
        }

        [IgnoreMember]
        /// <summary>Effective pose: PoseId if set, else fallback to "pose_idle_01".</summary>
        public string EffectivePose
        {
            get { return string.IsNullOrEmpty(PoseId) ? "pose_idle_01" : PoseId; }
        }

        /// <summary>Factory สำหรับสร้าง AvatarAppearance จาก list — ใช้ใน MockData</summary>
        public static AvatarAppearance FromSlots(params SlotPart[] slots)
        {
            var a = new AvatarAppearance();
            for (int i = 0; i < slots.Length; i++)
            {
                if (!string.IsNullOrEmpty(slots[i].PartId))
                    a.Parts[slots[i].Slot] = slots[i].PartId;
            }
            return a;
        }
    }

    /// <summary>ชื่อ slot แบบ const string + Categories สำหรับ UI</summary>
    public static class AvatarSlots
    {
        public const string Base = "base";
        public const string Body = "body";
        public const string Head = "head";
        public const string Eyes = "eyes";
        public const string Brows = "brows";
        public const string Mouth = "mouth";
        public const string Nose = "nose";
        public const string Hair = "hair";
        public const string FaceMarking = "face_marking";
        public const string Eyeshadow = "eyeshadow";
        public const string Accessory = "accessory";

        public static readonly string[] Equippable =
        {
            Body, Head, Eyes, Brows, Mouth, Nose,
            Hair, FaceMarking, Eyeshadow, Accessory
        };

        public static readonly IReadOnlyDictionary<string, string[]> Categories =
            new Dictionary<string, string[]>
            {
                { "ใบหน้า", new[] { Head, Eyes, Brows, Mouth, Nose } }, // Head รวมอยู่ในใบหน้าด้วย
                { "ลักษณะ", new[] { Hair, FaceMarking, Eyeshadow } },
                { "ร่างกาย", new[] { Body, Accessory } },
            };

        public static readonly IReadOnlyDictionary<string, string> Labels =
            new Dictionary<string, string>
            {
                { Body,       "เสื้อผ้า" },
                { Head,       "ใบหน้า" },
                { Eyes,       "ตา" },
                { Brows,      "คิ้ว" },
                { Mouth,      "ปาก" },
                { Nose,       "จมูก" },
                { Hair,       "ทรงผม" },
                { FaceMarking,"ลายหน้า" },
                { Eyeshadow,  "อายแชโดว์" },
                { Accessory,  "เครื่องประดับ" },
            };
    }
}```

### Shared/MessageTypes.cs
```
// SceneLoadedMessage is defined in GameMessages.cs under Xianxia.Sect.Messages.
// This file is kept for backward compatibility but the type is now canonical
// in GameMessages.cs to avoid duplicate type conflicts across namespaces.
//
// If you need SceneLoadedMessage, use:
//   using Xianxia.Sect.Messages;
//   var msg = new SceneLoadedMessage { SceneName = "..." };

```

### Shared/MockSectData.cs
```
// Sample SectEconomyState for wiring up UI before real gameplay
// systems (resource gathering, crafting, quests) are implemented.
using System.Collections.Generic;
namespace Xianxia.Sect
{
    public static class MockSectData
    {
        public static SectEconomyState Create()
        {
            var state = new SectEconomyState();
            
            // --- Stockpile ---
            state.Stockpile.RawResources["herb"] = 120;
            state.Stockpile.RawResources["wood"] = 340;
            state.Stockpile.RawResources["ore"] = 88;
            state.Stockpile.RawResources["provisions"] = 260;
            
            state.Stockpile.CraftedGoods.Add(new InventoryItem
            {
                ItemDefId = "elixir_qi_gathering",
                Quantity = 4,
                Grade = 3,
                OwnerScope = OwnerScope.SectStockpile
            });

            // --- Disciples (Updated for New Avatar System with SlotPart struct) ---
            
            // d000: Sect Master
            state.Disciples.Add(new DiscipleState
            {
                DiscipleId = "d000",
                DisplayName = "Liu YiFeng",
                Sex = DiscipleSex.Male,
                ChibiBackend = ChibiBackend.Spine, // SectMaster = Spine tier (แผน §4.1)
                Rank = DiscipleRank.SectMaster,
                Wallet = new CurrencyWallet { SpiritStones = 1200, Contribution = 3400 },
                CurrentTask = "meditation",
                PersonalInventory = new List<InventoryItem>(),
                Avatar = AvatarAppearance.FromSlots(
                    new SlotPart(AvatarSlots.Body, "body_robe_azure"),
                    new SlotPart(AvatarSlots.Head, "head_male_01"),
                    new SlotPart(AvatarSlots.Hair, "hair_topknot_long"),
                    new SlotPart(AvatarSlots.Accessory, "acc_jade_crown")
                )
            });

            // d001: Lin Feng (Outer Disciple)
            state.Disciples.Add(new DiscipleState
            {
                DiscipleId = "d001",
                DisplayName = "Lin Feng",
                Sex = DiscipleSex.Female,
                ChibiBackend = ChibiBackend.SpriteSheet, // Outer = SpriteSheet (แผน §4.1)
                Rank = DiscipleRank.OuterDisciple,
                Wallet = new CurrencyWallet { SpiritStones = 12, Contribution = 340 },
                CurrentTask = "gathering_herb",
                PersonalInventory = new List<InventoryItem>(),
                Avatar = AvatarAppearance.FromSlots(
                    new SlotPart(AvatarSlots.Body, "body_robe_grey"),
                    new SlotPart(AvatarSlots.Head, "head_male_01"),
                    new SlotPart(AvatarSlots.Hair, "hair_short")
                )
            });

            // d002: Su Yan (Inner Disciple - With face marking + twin tail)
            state.Disciples.Add(new DiscipleState
            {
                DiscipleId = "d002",
                DisplayName = "Su Yan",
                Sex = DiscipleSex.Female,
                ChibiBackend = ChibiBackend.SpriteSheet, // Inner = SpriteSheet (แผน §4.1)
                Rank = DiscipleRank.InnerDisciple,
                Wallet = new CurrencyWallet { SpiritStones = 45, Contribution = 1120 },
                CurrentTask = "refining_elixir",
                PersonalInventory = new List<InventoryItem>(),
                Avatar = AvatarAppearance.FromSlots(
                    new SlotPart(AvatarSlots.Body, "body_robe_white"),
                    new SlotPart(AvatarSlots.Head, "head_female_01"),
                    new SlotPart(AvatarSlots.Hair, "hair_twin_tail"),
                    new SlotPart(AvatarSlots.FaceMarking, "face_marking_red_dot"),
                    new SlotPart(AvatarSlots.Accessory, "acc_hairpin_silver")
                )
            });

            // d003: Elder Zhao (Elder - Bald + Beard)
            state.Disciples.Add(new DiscipleState
            {
                DiscipleId = "d003",
                DisplayName = "Elder Zhao",
                Sex = DiscipleSex.Male,
                ChibiBackend = ChibiBackend.Spine, // Elder = Spine tier (แผน §4.1)
                Rank = DiscipleRank.Elder,
                Wallet = new CurrencyWallet { SpiritStones = 210, Contribution = 4300 },
                CurrentTask = "forging_artifact",
                PersonalInventory = new List<InventoryItem>
                {
                    new InventoryItem
                    {
                        ItemDefId = "sword_azure_flame",
                        Quantity = 1,
                        Grade = 5,
                        OwnerScope = OwnerScope.Personal
                    }
                },
                Avatar = AvatarAppearance.FromSlots(
                    new SlotPart(AvatarSlots.Body, "body_robe_black"),
                    new SlotPart(AvatarSlots.Head, "head_male_elder"),
                    new SlotPart(AvatarSlots.Hair, "hair_bald_beard"),
                    new SlotPart(AvatarSlots.Accessory, "acc_gourd")
                )
            });

            return state;
        }
    }
}```

### McpBridge/Program.cs
```
// MCP bridge process (external, matches the "MCP bridge process" box in the
// architecture diagram). Runs as its own .NET console app, separate from
// Unity. Written in C# specifically so it can use MessagePipe.Interprocess
// directly instead of re-implementing MessagePipe's wire protocol in
// Node/Python - the tradeoff flagged earlier is resolved by keeping both
// sides on .NET, same as Wanxiang.Prelude's Frontend/Backend split.
//
// Talks to Unity over MessagePipe.Interprocess (TCP, localhost).
// Talks to the AI GM client (e.g. Open-LLM-VTuber) over MCP via stdio,
// using the official ModelContextProtocol C# SDK.
//
// NuGet packages needed: MessagePipe, MessagePipe.Interprocess,
// ModelContextProtocol, Microsoft.Extensions.Hosting

using System.ComponentModel;
using System.Text.Json;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Xianxia.Sect;
using Xianxia.Sect.Messages;

var builder = Host.CreateApplicationBuilder(args);

// MCP over stdio uses stdout for protocol messages - logs must go to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

Console.Error.WriteLine("[mcp-bridge] starting - if you're running this " +
    "directly with `dotnet run`, this is expected to look idle after this " +
    "line: stdio transport just waits for an MCP client (e.g. MCP " +
    "Inspector) to talk to it over stdin, it won't print anything else on " +
    "its own until a client connects and calls a tool.");

// --- connect into Unity's MessagePipe bus as a TCP client ---
// AddMessagePipe() on IServiceCollection returns IMessagePipeBuilder
// directly (unlike VContainer's IContainerBuilder, which needs an extra
// ToMessagePipeBuilder() conversion step) - AddTcpInterprocess() hangs off
// that builder, not off IServiceCollection itself.
var messagePipeBuilder = builder.Services.AddMessagePipe();
messagePipeBuilder.AddTcpInterprocess("127.0.0.1", 3215, options =>
{
    options.HostAsServer = false; // Unity hosts the endpoint; we connect as a client
});

// --- expose MCP tools to the AI GM client ---
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
await app.RunAsync();

// Read (query) tools - map 1:1 to what was sketched in the architecture
// discussion (get_sect_state, get_current_event, ...).
[McpServerToolType]
public static class SectQueryTools
{
    [McpServerTool, Description("Get the current sect state: disciples, wallets, stockpile.")]
    public static async Task<string> GetSectState(
        IRemoteRequestHandler<SectStateQuery, SectStateSnapshot> requestHandler)
    {
        var snapshot = await requestHandler.InvokeAsync(
            new SectStateQuery { RequestId = Guid.NewGuid().ToString() });

        // MessagePack stub decode - swap for the generated protobuf parser
        // once economy.proto is compiled for real.
        var state = SectEconomyState.FromByteArray(snapshot.EconomyStateBytes);
        return JsonSerializer.Serialize(state);
    }

    [McpServerTool, Description("Block until the next world event that requires a GM decision.")]
    public static async Task<string> AwaitNextWorldEvent(
        IRemoteRequestHandler<AwaitWorldEventRequest, AwaitWorldEventResponse> requestHandler)
    {
        // Request-response, not subscribe - IDistributedSubscriber over this
        // TCP transport always opens its own listen socket regardless of
        // HostAsServer, which collides with Unity's already-bound port when
        // called from a non-hub process like this bridge. Request-response
        // only ever connects out (no listen), same mechanism get_sect_state
        // already uses successfully.
        var response = await requestHandler.InvokeAsync(
            new AwaitWorldEventRequest { RequestId = Guid.NewGuid().ToString() });

        // Full response now (description + choices), not just the bare
        // event id - EventData ScriptableObjects actually author this
        // content now instead of it being hardcoded/absent.
        return JsonSerializer.Serialize(response);
    }
}

// Write (execute) tools go in a separate type on purpose - keeps the
// read/write split visible at a glance in the tool list, matching the
// separation decided earlier (query tools vs execute tools).
[McpServerToolType]
public static class SectActionTools
{
    [McpServerTool, Description("Execute a decision for the current world event.")]
    public static async Task<string> ExecuteDecision(
        IDistributedPublisher<string, ExecuteDecisionMessage> publisher,
        [Description("Event id from await_next_world_event")] string eventId,
        [Description("Chosen option id")] string choiceId)
    {
        await publisher.PublishAsync(
            InterprocessTopics.ExecuteDecision,
            new ExecuteDecisionMessage { EventId = eventId, ChoiceId = choiceId });

        return $"Decision sent to Unity: event={eventId} choice={choiceId}";
    }

    [McpServerTool, Description("Have a disciple buy an item from the sect stockpile using their contribution.")]
    public static async Task<string> PurchaseItem(
        IRemoteRequestHandler<PurchaseItemRequest, PurchaseItemResponse> requestHandler,
        [Description("Disciple id, e.g. from get_sect_state")] string discipleId,
        [Description("Item id, e.g. elixir_qi_gathering")] string itemDefId,
        [Description("Item grade, from the stockpile entry in get_sect_state")] int grade,
        [Description("How many to buy")] int quantity)
    {
        var response = await requestHandler.InvokeAsync(new PurchaseItemRequest
        {
            DiscipleId = discipleId,
            ItemDefId = itemDefId,
            Grade = grade,
            Quantity = quantity,
        });

        return response.Message;
    }

    [McpServerTool, Description("Assign a task to a disciple. Always sent as SECT_MASTER (the AI GM acts as the sect master, so it may assign any disciple).")]
    public static async Task<string> AssignTask(
        IRemoteRequestHandler<AssignTaskRequest, AssignTaskResponse> requestHandler,
        [Description("Disciple id, e.g. from get_sect_state")] string discipleId,
        [Description("Task id: a gathering or crafting task id, or \"meditation\"")] string taskId)
    {
        var response = await requestHandler.InvokeAsync(new AssignTaskRequest
        {
            RequesterId = "SECT_MASTER",
            DiscipleId = discipleId,
            TaskId = taskId,
        });

        return response.Success
            ? $"Task '{taskId}' assigned to {discipleId}."
            : response.FailReason;
    }
}
```

### sync-shared.sh
```
#!/usr/bin/env bash
# Copies the canonical source in Shared/ into both consumers.
# Run this after editing anything in Shared/.
#
# Not a real shared package yet (no project reference / plugin DLL) - that's
# a deliberate deferral while the schema is still moving fast. Revisit once
# GameMessages.cs / SectEconomyState.cs stop changing every session.

set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

UNITY_DEST="$ROOT/UnityProject/Assets/Scripts/Shared"
BRIDGE_DEST="$ROOT/McpBridge/Shared"

mkdir -p "$UNITY_DEST" "$BRIDGE_DEST"

cp "$ROOT"/Shared/*.cs "$UNITY_DEST"/
cp "$ROOT"/Shared/*.cs "$BRIDGE_DEST"/

echo "Synced $(ls "$ROOT"/Shared/*.cs | wc -l | tr -d ' ') file(s) into:"
echo "  $UNITY_DEST"
echo "  $BRIDGE_DEST"
```

### LLMWiki/README.md
```
# Cultivation Together — LLM Wiki

> Second brain for the game. AI maintains the wiki; you curate the sources.

Based on Andrej Karpathy's [LLM Wiki pattern](https://gist.github.com/karpathy/442a6bf555914893e9891c11519de94f).

## What's in here

```
LLMWiki/
├── raw/                          ← You write (immutable). LLM reads.
├── wiki/                         ← LLM writes. You read.
│   ├── index.md                  ← Master catalog (read first)
│   ├── log.md                    ← Chronological operation log
│   ├── overview.md               ← High-level project summary
│   ├── conventions.md            ← Your workflow rules
│   ├── sources/                  ← You write the design-doc seeds
│   │   ├── game-design-doc.md
│   │   ├── architecture.md
│   │   ├── mechanics.md
│   │   ├── devlog-history.md
│   │   ├── bug-log.md
│   │   ├── open-questions.md
│   │   └── code-snippets/        ← Important scripts as markdown
│   ├── concepts/                 ← LLM maintains: per system/concept
│   │   ├── vcontainer-composition.md
│   │   ├── message-pipe-bus.md
│   │   ├── mcp-bridge.md
│   │   ├── decision-pipeline.md
│   │   ├── state-management.md
│   │   ├── time-system.md
│   │   ├── world-events.md
│   │   ├── gathering-system.md
│   │   ├── crafting-system.md
│   │   ├── purchase-store.md
│   │   ├── mvp-ui.md
│   │   └── data-pipeline.md
│   └── entities/                 ← LLM maintains: per entity type
│       ├── disciples.md
│       ├── world-events.md
│       ├── events.md
│       ├── resources.md
│       ├── items.md
│       └── sects.md
├── AGENTS.md                     ← Schema — tells the LLM how to behave
└── README.md                     ← This file
```

## How to Use

### 1. Browse

Open `LLMWiki/` as an **Obsidian vault** to get:
- Graph view (see knowledge structure)
- Backlinks (auto-tracked)
- Dataview queries
- Search across all pages

### 2. Query

Ask the LLM agent (Claude Code, Codex, OpenCode) questions like:
- "How does the decision pipeline work?"
- "What bugs were hit during lab 6?"
- "How do I add a new world event?"
- "Why does `await_next_world_event` timeout after a decision event?"

The agent reads `wiki/index.md` first, finds relevant pages, answers with
`[[wikilinks]]` citations.

### 3. Add a Source

- **Design notes**: edit files in `wiki/sources/`
- **External articles**: drop into `raw/articles/`
- **Code**: paste important scripts into `wiki/sources/code-snippets/<Name>.cs.md`

Then tell the agent: "ingest wiki/sources/mechanics.md" or "I added a new
ScriptableObject for buildings, please document it"

### 4. Lint (Periodic Health Check)

```
> lint the wiki
```

Saves a report to `wiki/outputs/lint-YYYY-MM-DD.md` covering:
- Contradictions between pages
- Orphan pages (no incoming links)
- Stale claims
- Missing concepts

## Files You Write vs Files the LLM Writes

| Layer | Who writes | Files |
|---|---|---|
| `raw/` | You | All files (immutable) |
| `wiki/sources/` | You | Design-doc seeds + code snippets |
| `wiki/concepts/` | LLM | Concept pages per system |
| `wiki/entities/` | LLM | Entity pages per type |
| `wiki/index.md` | LLM | Master catalog |
| `wiki/log.md` | LLM | Operation log |
| `AGENTS.md` | You + LLM | Schema (co-evolves) |
| `wiki/conventions.md` | You | Your preferences |

## Next Steps

1. ✅ Wiki is bootstrapped with full project understanding
2. **Fill in `[[sources/open-questions|Open Questions]]` answers** as you decide
3. **Start a daily devlog** (`devlog-YYYY-MM-DD.md`) for each work session
4. **Add code snippets** for any script you want the LLM to know about
5. **Periodically ask the LLM to lint the wiki** for health

## Tips

- **Commit `LLMWiki/` to git** — every change is a trackable diff
- **Start with real project content** — the wiki already has 31 pages grounded
  in your actual code; add more as you write more
- **Reference the actual file paths** in backticks when explaining systems,
  e.g. `Assets/Scripts/Systems/SectStateProvider.cs:177`
- **Talk to the LLM** — discuss takeaways before mass updates; don't let it
  run unsupervised on the whole wiki

## Tech Stack Reminder

| | |
|---|---|
| Engine | Unity 6.3 (C# 8.0) |
| DI | VContainer |
| Internal bus | MessagePipe |
| IPC to MCP | MessagePipe.Interprocess (TCP) |
| MCP bridge | .NET 8 console app |
| State serialization | MessagePack |
| Data pipeline | Luban (Excel → JSON → C#) |
| UI | UGUI + TextMeshPro |

## Related Project Docs (Outside the Wiki)

- `../project_summary.md` — **THE design log** (Thai, 546 lines, 13 labs)
- `../README.md` — workspace overview
- `../XIANXIA_UI_MVP_LITE_SPEC.md` — UI framework spec
```

### project_summary.md
```
# Xianxia Sect Simulator — MCP-Enabled Game Framework
สรุปการออกแบบ + บันทึกความคืบหน้า (อัปเดตล่าสุด: Additive Scene Architecture implement เสร็จแล้ว (lab 18) + เพิ่ม Task System v2 — Viewer-Controlled Tasks design phase)

## แนวคิดโปรเจกต์
เกม simulator บริหารสำนักเซียน (xianxia cultivation sect) บน Unity ที่ออกแบบให้ AI Agent
(เช่น Open-LLM-VTuber) เข้ามาเล่นเป็น Game Master / เจ้าสำนัก แทนคนได้ผ่าน MCP
โดยมีเป้าหมายรองรับสตรีมแบบโหวตร่วมกับผู้ชม (คล้าย King of the Castle บน Twitch)
ให้ผู้ชมร่วมเป็น "ศิษย์สำนัก" โหวตตัดสินใจเหตุการณ์ต่างๆ ผ่าน Twitch extension
แรงจูงใจหลัก: ลดความเหนื่อยของสตรีมเมอร์ที่ต้องอ่านออกเสียง/คุมกลไกเกมเองตลอด
โดยให้ AI VTuber รับบทนี้แทน

## Tech stack ที่ตกลงไว้
| ส่วน | เทคโนโลยี | หมายเหตุ |
| --- | --- | --- |
| Game engine | Unity (C#) | ใช้ C# ทั้งสองฝั่ง (Unity + Bridge) |
| DI / Composition Root | **VContainer** | เบากว่า Zenject, register ทุก subsystem |
| Internal messaging | **MessagePipe** | pub/sub + request-response, zero-alloc |
| IPC (Unity ↔ Bridge) | **MessagePipe.Interprocess (TCP)** | Unity เป็น host, Bridge เป็น client — **รองรับหลาย client พร้อมกัน** (McpBridge + ในอนาคต Twitch bot service) |
| MCP Bridge | **.NET 8 Console App** | ใช้ `ModelContextProtocol` C# SDK (stdio) |
| Serialization | **MessagePack** | เลิกใช้ protobuf แล้วถาวร (lab รอบ 7) |
| DataTable | **Luban** | Excel → JSON → C# Plain Class (ไม่ใช้ ScriptableObject เพื่อเลี่ยงการคลิกสร้าง Asset) |
| UI Framework | **Xianxia.UI.MVP Lite** | Custom UGUI + TMP (View=MonoBehaviour, Presenter=Plain C#) ทำงานร่วมกับ VContainer/MessagePipe โดยตรง ไม่พึ่ง Reflection |
| Scene Architecture | **Additive Scene (CoreScene + GameplayScene)** | ✅ implement แล้ว (lab 18) |

## หลักคิดกลางที่ใช้คุมทุกระบบ
> ทุกกลไกต้องเป็นสิ่งที่ AI ตัดสินใจได้จากข้อมูลที่ query ผ่าน MCP ได้จริง
> ไม่พึ่ง "สัญชาตญาณมนุษย์" ที่ AI เข้าไม่ถึง

> **หลักคิดใหม่ (จาก Task System v2):** เมื่อมีมากกว่าหนึ่ง "ผู้สั่งการ" ที่เป็นไปได้ (AI GM /
> ผู้เล่น / ผู้ชม Twitch) การตรวจสิทธิ์ต้องเกิด **ครั้งเดียว ฝั่งเดียว ที่ Unity** เสมอ — ไม่เชื่อ
> client ฝั่งไหนว่า "ส่งมาถูกต้องแล้ว" หลักเดียวกับที่ `TryPurchaseItem` ใช้ (atomic check-and-deduct
> ที่ single source of truth) ตอนนี้ถูกยกระดับเป็นหลักการทั่วไปสำหรับทุกฟีเจอร์ที่ viewer ควบคุมได้

---

## 🆕 ความคืบหน้าล่าสุด (รอบนี้)

### 1. Avatar System — Parts-only MVP (Dictionary Schema)

**⚠️ Rollback (2026-09-03):** ระบบ **Outfit Packages (v3) ถูกลบออกแล้ว** — โค้ด/JSON ปัจจุบันเป็น parts-only:
ผู้เล่นเลือก part ทีละ slot (`body`/`head`/`hair`/`accessory`/`face_marking`) อิสระกัน, ไม่มี outfit preset,
ไม่มี pose-switching, ไม่มี UI tab "ชุดแต่งกาย" เหตุผลเต็มอยู่ที่ LLMWiki `wiki/sources/avatar-appearance.md` §1
(สรุป: outfit derive จาก `parts[]` ได้ 100% และตอนนี้มี pose เดียวจึงไม่มีอะไรให้สลับ)

**ปัญหาเดิม:** Schema แบบ fixed property (`Body/Head/Hair/Accessory`) ขยาย slot ไม่ได้โดยไม่ break save file + ผมยาวซ้อน layer ผิด (วิกมุดหัว)

**สิ่งที่แก้:**
-   **`AvatarAppearance` เปลี่ยนเป็น Dictionary-based:**
    ```csharp
    [Key(0)] Dictionary<string, string> Parts;  // slot → partId
    [Key(1)] Dictionary<string, string> Colors; // slot → colorId (เตรียมไว้สำหรับ tint)
    [Key(2)] string PoseId;                     // reserved — pose คงที่ pose_idle_01 (ยังไม่มี UI เปลี่ยน)
    ```
    -   เพิ่ม slot ใหม่ = แก้แค่ JSON ไม่ต้องแก้ schema
    -   AI GM ค้นพบ slot ที่มีอยู่ได้เองจาก `get_sect_state`
    -   `GetSlot/SetSlot/Clone` API เหมือนเดิม → Presenter ไม่ต้องแก้
-   **ขยาย Slot เป็น 10 ช่อง + Category:**
    -   Slots: `body, head, eyes, brows, mouth, nose, hair, face_marking, eyeshadow, accessory`
    -   Categories (UI Tabs): `ใบหน้า` (Head/Eyes/Brows/Mouth/Nose), `ลักษณะ` (Hair/FaceMarking/Eyeshadow), `ร่างกาย` (Body/Accessory)
-   **Hair 2-Layer (ผมหน้า/ผมหลัง):**
    -   `AvatarPartDef` เพิ่ม `spritePathBack` + `drawOrderBack`
    -   `AvatarRenderer` แยก layer: ผมหลัง (order 10, ใต้ตัว) + ผมหน้า (order 40, บนหน้า)
    -   Stack ที่ถูกต้อง: `base(0) → hair_back(10) → body(20) → head(30) → face_marking(34) → hair_front(40) → accessory(50)`
-   **Part metadata (pose/sex):** ทุก `AvatarPartDef` มี `poseId` (`""` = universal, ปัจจุบันมีแค่ `pose_idle_01`)
    และ `sexTag` (`"male"`/`"female"`/`""`) — `Randomize` กรองตาม pose/sex ของศิษย์ (`DiscipleState.Sex`),
    `TryChangeAvatarPart()` มี pose validation (pose เดียวตอนนี้จึงไม่เคย fail); grid ยังไม่กรอง sex
-   **AvatarFraming Presets:**
    -   Enum `FullBody / Bust / HeadIcon` ควบคุม scale + offset ของ `layerRoot`
    -   Art ชุดเดียว (canvas 1024×1536) ใช้ได้ทั้ง Character Creation, Dialogue Portrait, HUD Icon

**สถานะ:** ✅ v2.5 parts-only MVP — dictionary schema + hair 2-layer renderer + framing presets + per-slot draft/commit UI
ทำงานจริง (Randomize/validation ใช้ pose+sex แล้ว); ❌ Outfit Packages ถูกลบ (rollback 2026-09-03) — รอ Art ส่ง Sprite จริง

### 2. Sex / Gender System (Explicit State)
**ปัญหาเดิม:** เพศศิษย์ถูก imply จาก `rosterIndex % 2` ใน `CreateStarterAvatar` → Query ไม่ได้, บังคับเพศตอนรับสมัครไม่ได้

**สิ่งที่แก้:**
-   **เพิ่ม `DiscipleSex` Enum + Field:**
    ```csharp
    public enum DiscipleSex { Unspecified, Male, Female }
    [Key(7)] public DiscipleSex Sex { get; set; } = DiscipleSex.Unspecified;
    ```
    -   Append `[Key(7)]` หลัง `Avatar` → Backward compatible กับ save เก่า
-   **Recruitment Logic:**
    -   `RecruitOuterDisciple(DiscipleSex sex = Unspecified)` รับ optional param
    -   `CreateStarterAvatar(index, sex)` เลือก Head ตาม Sex จริง ไม่ใช่ index
-   **Mock Data:** ระบุ Sex ชัดเจน (d000=Male, d001=Female, d002=Female, d003=Male)
-   **MCP:** `get_sect_state` คืนค่า `Sex` เป็น integer (0/1/2) อัตโนมัติ

**สถานะ:** ✅ Data Model + Recruitment Logic เสร็จแล้ว (รอ UI Selector ใน Phase ถัดไป)

### 3. Xianxia.UI.MVP Lite (EventPopup + ResourceHud + LogWindow + AvatarCustomization)
**สิ่งที่ทำ:**
-   **Pattern:** View (MonoBehaviour) / Presenter (Plain C#, Transient) / Service (Singleton)
-   **Panel Resolution:** Explicit `UIPanelType` enum → Type mapping (ไม่ scan assembly)
-   **Bug Fix สำคัญ (Lab 13):**
    -   `EventPopupPresenter` เคย publish ผ่าน in-memory `IPublisher` แต่ `DecisionLogger` ฟังผ่าน interprocess `IDistributedSubscriber` → คนละ graph กัน กดปุ่มแล้วเงียบ
    -   **แก้:** ดึง logic ออกมาเป็น `DecisionExecutor.cs` ให้ทั้ง UI และ Bridge เรียกตรงๆ (Direct Method Call) แทน pub/sub สำหรับ in-process logic
-   **ResourceHud:** แทนที่ `SectHudView` เดิม, subscribe `SectResourceChangedMessage` (มี delta มาให้ในตัว) แทน polling
-   **LogWindow (lab 14):** scrolling narrative log — subscribe เฉพาะ `DiscipleRecruitedMessage`/`WorldEventTriggeredMessage`/`DecisionExecutedMessage` (ไม่รวม resource ticks กันน้ำท่วมจอ)
-   **AvatarCustomization:**
    -   Draft Pattern (Clone → Edit → Confirm/Rollback) → ไม่ยิง message ข้าม TCP ทุกคลิก
    -   Category Tabs (ใบหน้า/ลักษณะ/ร่างกาย) → slot tabs → part grid (ไม่มี Outfit Tab — rollback แล้ว)
    -   Object Pooling สำหรับปุ่มในกริด (กัน GC กระตุกตอนสลับแท็บ)
-   **UIRoot Layout Fix:**
    -   Root cause เดิม: `ContentSizeFitter=PreferredSize` ทับ anchor stretch → UI กองกลางจอ
    -   แก้: `UIRoot.Awake()` บังคับ stretch เต็ม Canvas + `ApplyLayout()` ตั้ง `Unconstrained` ที่ root panel

**สถานะ:** ✅ EventPopup, ResourceHud, LogWindow, AvatarCustomization ทำงานจริง (ยืนยันแล้ว 30 ส.ค. – 1 ก.ย. 2026)

### 4. Additive Scene Architecture — ✅ Implement เสร็จแล้ว (lab 18, v0.12)
เดิมอยู่ในสถานะ Draft (ดูหัวข้อ "Lab 16 (Draft)" เวอร์ชันก่อนหน้าของเอกสารนี้) — ตอนนี้ implement จริงแล้ว:

-   **CoreScene** (Single, never unload): `GameLifetimeScope`, `TimeSystem`, `SectStateProvider`, MessagePipe bus,
    `SceneLoader` + persistent UI (Canvas, `UIRoot`, `ResourceHud`, `LogWindow`, `AvatarCustomization`) + `EventSystem`/`AudioListener` ตัวเดียว
-   **GameplayScene(s)** (Additive, unload/reload ได้): environment/terrain, NPC, buildings, scene-specific UI (`EventPopup`)
-   `SceneLoader` singleton — `LoadGameplayScene(name)` unload scene เดิมก่อนโหลดใหม่ (A→B swap), publish `SceneLoadedMessage`
-   `AdditiveSceneTest` — runtime GUI ทดสอบ Load/Unload/Swap
-   **VContainer strategy ที่เลือกใช้ตอนนี้**: Option A (single root scope ใน CoreScene) — parent-child scope (Option B) เลื่อนไปทำเมื่อมี scene-specific service จริง

**Gotcha ที่เจอ**: ทั้งสอง scene ต้องอยู่ใน Build Settings ก่อนถึงจะ async load ได้; GameplayScene ห้ามมี `EventSystem`/`AudioListener` ซ้ำ

**สถานะ:** ✅ implemented (v0.12) — ดูรายละเอียดที่ LLMWiki `concepts/additive-scene-architecture.md`

### 5. 🆕 Task System v2 — Viewer-Controlled Tasks + Ownership Model (Design Phase, ยังไม่ implement)

> เอกสารต้นฉบับ: `task-system-v2.md` — เป็นการ revise `task-system.md` (ฉบับที่แข็งแรงกว่าในสอง draft
> ที่ AI ช่วยร่างไว้) **ไม่ใช้ `TaskSystem_GDD.md` เป็นฐาน** เพราะฉบับนั้นอ้าง `[Key(8)]` สำหรับ
> `CurrentTask` ผิด (ของจริงคือ `[Key(5)]`) และเสนอแนวทางที่โปรเจกต์ปฏิเสธไปแล้ว (ScriptableObject-backed
> data — ขัดกับ decision ของ lab 12) เก็บไว้อ่านเป็น background เท่านั้น ไม่ใช่ source of truth

#### 5.1 เป้าหมายการออกแบบ (confirmed)

-   **โซโล่ / ไม่มีผู้ชม**: ผู้เล่น (Sect Master) สั่งเปลี่ยน task ของศิษย์ **คนไหนก็ได้** อิสระแบบ
    Rimworld/ACS — ไม่มีข้อจำกัด
-   **มีผู้ชมเข้าร่วมเป็นศิษย์**: ผู้ชมคนนั้นสั่งเปลี่ยน task ได้ **เฉพาะศิษย์ของตัวเอง** (ผ่าน Twitch
    chat command หรือ extension UI ในอนาคต) — ผู้เล่นยัง override ใครก็ได้เหมือนเดิม รวมถึงศิษย์ที่
    ผู้ชมเป็นเจ้าของ
-   **ตรวจสิทธิ์ครั้งเดียว ฝั่ง Unity เท่านั้น** — ไม่เชื่อ client ฝั่งไหน (AI GM / Twitch bot / UI ใน
    อนาคต) ว่าส่งคำขอมาถูกต้อง หลักการเดียวกับ atomic check-and-deduct ของ `TryPurchaseItem`: มี
    authority เดียว ไม่มี client ไหน "สันนิษฐานว่าตัวเองถูกต้อง"

#### 5.2 การเปลี่ยน Data Model

**`DiscipleState` — เพิ่ม field ความเป็นเจ้าของ:**

```csharp
public enum DiscipleOwnerType
{
    Npc,      // AI/ไม่มีเจ้าของ — Sect Master คุมโดย default
    Player,   // เจ้าของคือ Sect Master เอง (เผื่อโมเดล alt/avatar แยกในอนาคต)
    Viewer,   // ควบคุมโดยผู้ชม Twitch คนใดคนหนึ่ง
}
```

```csharp
// ⚠️ ต้องยืนยัน [Key(N)] ที่ว่างจริงกับ SectEconomyState.cs ปัจจุบันก่อนเขียนโค้ด —
// ล่าสุดที่ยืนยันแล้ว: Avatar = Key(6), Sex = Key(7) → ถ้าไม่มีอะไรถูกเพิ่มหลังจากนั้น
// ตัวถัดไปควรเป็น Key(8)/(9) แต่ต้องเช็คไฟล์จริงก่อนพิมพ์เลขลงโค้ด อย่าเดา
[Key(N)]   public DiscipleOwnerType OwnerType { get; set; } = DiscipleOwnerType.Npc;
[Key(N+1)] public string OwnerId { get; set; } = string.Empty; // Twitch user id เมื่อ OwnerType=Viewer, ไม่งั้น ""
```

`OwnerId` เป็น plain string ตาม convention เดียวกับ `ItemDefId`/`PartId` ที่อื่นในโค้ด — resolve/validate
โดยฝั่งที่เรียกเข้ามา (Twitch bot, AI GM) ไม่ใช่ Unity เอง Unity สนใจแค่ "OwnerId นี้ตรงกับผู้ขอไหม"

**`MockSectData.cs`**: ศิษย์เริ่มต้นทั้ง 4 คน default เป็น `OwnerType = Npc` (ค่า default ของ enum
อยู่แล้ว) — ไม่มีใคร viewer-owned ตั้งแต่ต้น ไม่ต้องแก้ constructor เพิ่ม

#### 5.3 `SectStateProvider.TryAssignTask` — พร้อม permission check

```csharp
// requesterId convention:
//   "SECT_MASTER"    → ผู้เล่น หรือ AI GM ที่ทำหน้าที่ Sect Master; ข้ามการเช็คความเป็นเจ้าของ
//                       ทั้งหมด สั่งใครก็ได้
//   string อื่นๆ      → ต้องตรงกับ disciple.OwnerId เป๊ะ ไม่งั้น reject
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

สิ่งที่เพิ่มจาก draft เดิม (`task-system.md`): พารามิเตอร์ `requesterId` เป็นตัวแรก + บล็อกตรวจ
ความเป็นเจ้าของ ทุก call site (MCP tool, Twitch bot, UI ในอนาคต) ต้องส่งมาว่า "ใครเป็นคนถาม" — ไม่มี
caller ไหน "ถูกไว้ก่อน" อีกต่อไป

#### 5.4 🐛 บั๊กที่แก้ก่อน implement — `TickCrafting` ต้องผ่าน `AdjustAndNotify` เสมอ

Draft เดิม (`task-system.md`) เวอร์ชัน refactor หัก resource ตรงๆ:

```csharp
// ❌ ห้ามทำแบบนี้ — ทำให้ HUD เงียบแบบไม่มี error
_state.Stockpile.RawResources[kvp.Key] -= (int)kvp.Value;
```

วิธีนี้ bypass `SectResourceChangedMessage` ที่ `ResourceHudPresenter` ต้องพึ่งสำหรับ live delta display
(ตั้งแต่ lab 13) ตัวเลขภายในยังถูกต้อง แต่ HUD จะไม่อัปเดตเฉพาะตอนที่ crafting กินทรัพยากร — เป็น
regression แบบเงียบ (ไม่ crash) ที่ ship ไปโดยไม่รู้ตัวได้ง่ายมาก

**วิธีแก้**: ให้การหัก stockpile ทุกจุดผ่าน helper `AdjustAndNotify(resources, key, delta)` ที่มีอยู่แล้ว
เหมือนที่ `TickGathering` และ `ApplyDecisionConsequence` ทำอยู่:

```csharp
// ✅ ถูกต้อง
foreach (var kvp in taskDef.ResourceCost)
{
    AdjustAndNotify(_state.Stockpile.RawResources, kvp.Key, -(int)kvp.Value);
}
```

#### 5.5 Threading — บทเรียนจาก `DecisionExecutor` ต้องใช้ซ้ำที่นี่

`AssignTaskRequest` จะถูกตอบโดย `AssignTaskHandler`
(`IAsyncRequestHandler<AssignTaskRequest, AssignTaskResponse>`) ที่มาผ่าน `MessagePipe.Interprocess` —
ยืนยันแล้วในโปรเจกต์นี้ว่า handler แบบนี้รันบน **TCP receive thread ไม่ใช่ main thread ของ Unity**

`TryAssignTask` เองแตะแค่ plain C# state (ปลอดภัยนอก main thread) ความเสี่ยงอยู่ที่ปลายทาง: ถ้า UI ไหน
(เช่น `TaskAssignmentPresenter` ในอนาคต) subscribe `DiscipleTaskChangedMessage` แล้วทำอะไรที่แตะ Unity
API — `Instantiate`, อนิเมท progress bar ผ่าน MonoBehaviour เมธอด ฯลฯ — จะพังด้วย exception เดียวกับ
`Internal_CloneSingleWithParent can only be called from the main thread` ที่เคยเจอกับ
`DecisionExecutor` มาแล้ว

**กฎต่อจากนี้**: request handler ใดๆ ที่มาจาก `MessagePipe.Interprocess` แล้วนำไปสู่โค้ดที่แตะ UI ต้อง
hop กลับ main thread ก่อน (`await UniTask.SwitchToMainThread();`) ก่อนรันโค้ด UI นั้น — pattern เดียวกับ
`DecisionExecutor.ExecuteAsync` ที่มีอยู่แล้ว ไม่ต้องรอเจอซ้ำอีกรอบ

#### 5.6 Twitch chat commands — ไม่ต้องสร้าง transport ใหม่

Unity host `MessagePipe.Interprocess` TCP endpoint เป็น server อยู่แล้ว (`HostAsServer = true`) และ TCP
server โดยธรรมชาติรับหลาย client พร้อมกันได้ — `McpBridge` ไม่จำเป็นต้องเป็น client ตัวเดียวที่ต่ออยู่

```
Twitch chat: "!cultivate"
        │
        ▼
Twitch integration service (แยก process — Node.js bot หรือ C# service เล็กๆ ที่ใช้
MessagePipe.Interprocess ไลบรารีเดียวกับที่ McpBridge ใช้อยู่แล้ว)
        │  ดูว่า Twitch user นี้เป็นเจ้าของ DiscipleId ตัวไหน
        ▼
AssignTaskRequest { RequesterId = twitchUserId, DiscipleId = ..., TaskId = "cultivation" }
        │  ส่งผ่าน TCP port เดียวกัน (127.0.0.1:3215) ที่ McpBridge ใช้อยู่
        ▼
Unity: AssignTaskHandler → SectStateProvider.TryAssignTask(requesterId, ...)
        │  ตรวจสิทธิ์ที่นี่ ครั้งเดียว ไม่ว่าจะมาจาก client ไหน
        ▼
Task ของศิษย์เปลี่ยนจริง (หรือได้เหตุผลที่ถูก reject กลับมา)
```

AI GM (ผ่าน MCP tool `assign_task`) กับ Twitch bot เป็นแค่ client คนละตัวที่เรียก request type เดียวกัน
ด้วยค่า `RequesterId` ต่างกัน — ไม่มี pipeline คู่ขนาน ไม่มี port ใหม่ ไม่มี message type ใหม่นอกจากที่
หัวข้อ 5.7 ต้องมี

**คำถามเปิด ไม่บล็อกตอนนี้**: Twitch integration service จะรู้ได้ยังไงว่า "Twitch user คนนี้เป็นเจ้าของ
DiscipleId ตัวไหน"? นั่นคือ flow การรับสมัคร/ผูกความเป็นเจ้าของ (ผู้ชมเข้าร่วม → กลายเป็นศิษย์ → ได้
`OwnerId` เขียนลง `DiscipleState`) — อยู่นอก scope เอกสารนี้ มาทีหลังพร้อมงาน Twitch integration จริง
ระหว่างนี้ตั้ง `OwnerId` มือได้ผ่าน `MockSectData.cs` หรือ debug tool เพื่อทดสอบ permission logic
แยกเดี่ยวไปก่อน

#### 5.7 Message เพิ่มใหม่

```csharp
[MessagePackObject]
public class AssignTaskRequest
{
    [Key(0)] public string RequesterId { get; set; } = string.Empty; // "SECT_MASTER" หรือ id ของผู้ชม
    [Key(1)] public string DiscipleId { get; set; } = string.Empty;
    [Key(2)] public string TaskId { get; set; } = string.Empty;
}

[MessagePackObject]
public class AssignTaskResponse
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string FailReason { get; set; } = string.Empty;
}

// In-memory only เหตุผลเดียวกับ DecisionExecutedMessage — ไม่มี bridge write tool subscribe
// ตัวนี้โดยตรง มีไว้ให้ UI ในเครื่องตอบสนอง "task เพิ่งเปลี่ยน" (progress bar refresh, disciple list update)
[MessagePackObject]
public class DiscipleTaskChangedMessage
{
    [Key(0)] public string DiscipleId { get; set; } = string.Empty;
    [Key(1)] public string TaskId { get; set; } = string.Empty;
}
```

ใช้ request/response ไม่ใช่ pub/sub — เหตุผลเดียวกับ `PurchaseItemRequest` และ `ExecuteDecisionMessage`:
ผู้เรียก (AI GM หรือ Twitch bot) ต้องรู้ทันทีว่าการสั่งงานสำเร็จจริงไหม ไม่ใช่แค่ fire-and-hope

#### 5.8 สิ่งที่ยังคงเดิมจาก `task-system.md` (ผ่านการรีวิวแล้ว ไม่แก้)

-   โครงสร้าง `TaskDef` (id, displayName, type, resourceCost, duration, resourceGain/craftResult)
-   รูปแบบ loader ของ `TaskPool` (id → def lookup) — **หมายเหตุ**: ตอนนี้ร่างไว้เป็น plain
    `JsonUtility` + `Resources.Load` ไม่ได้ผ่าน Luban pipeline แบบที่ `LubanEventPool`/`AvatarPartPool`
    ใช้ — เป็นจุดที่ไม่ consistent (task def เป็นข้อมูล tabular แบบเดียวกับที่ event/avatar-part ผ่าน
    Luban ไปแล้ว) ที่ควรพิจารณาอีกทีในอนาคต แต่ยังไม่ต้อง force รอบนี้ — flag ไว้ ไม่ block
-   UI Draft/Diff-Commit pattern สำหรับ panel มอบหมาย task
-   ส่วน "What NOT to Touch" ที่ scope ไว้เดิม
-   `TickGathering`/เมธอดอื่นของ `SectStateProvider` ที่ไม่เกี่ยวข้อง

#### 5.9 Implementation Checklist (อัปเดตล่าสุด)

-   [ ] ยืนยัน `[Key(N)]` ที่ว่างจริงบน `DiscipleState` กับ `SectEconomyState.cs` ตัวจริง (ไม่ใช่เลข
        placeholder ในเอกสารนี้)
-   [ ] เพิ่ม `DiscipleOwnerType` enum + field `OwnerType`/`OwnerId`
-   [ ] `TaskDef`/`TaskPool` (plain C# loader ตาม draft เดิม)
-   [ ] `SectStateProvider.TryAssignTask(requesterId, ...)` พร้อม ownership check —
        **ใช้ `AdjustAndNotify` กับทุกการหักทรัพยากร** ห้ามเขียน dictionary ตรงๆ
-   [ ] `AssignTaskRequest`/`Response`/`DiscipleTaskChangedMessage` ใน `GameMessages.cs`
-   [ ] `AssignTaskHandler` (`IAsyncRequestHandler`) + register ใน `GameLifetimeScope`
        (RPC pattern เดียวกับ `PurchaseItemHandler`)
-   [ ] MCP tool `assign_task` ใน `McpBridge/Program.cs`
        (`SectActionTools`, `requesterId` เป็น `"SECT_MASTER"` เสมอจาก path นี้ตอนนี้)
-   [ ] ใช้ `UniTask.SwitchToMainThread()` ในโค้ด UI ใดๆ ที่ตอบสนอง `DiscipleTaskChangedMessage`
        ถ้าแตะ Unity API
-   [ ] Test: สั่งงานผ่าน MCP tool ในนาม `SECT_MASTER` → ต้องสำเร็จเสมอ
-   [ ] Test: ตั้ง `OwnerId` ของศิษย์คนหนึ่งใน `MockSectData.cs` มือ แล้วเรียก `TryAssignTask` ด้วย
        `requesterId` ที่ไม่ตรง → ต้องถูก reject พร้อมเหตุผล
-   [ ] (ทีหลัง แยกงาน) Twitch integration service + flow ผูกความเป็นเจ้าของ viewer→disciple จริง

**สถานะ:** ⏳ Design phase — ยังไม่เริ่ม implement โค้ดจริง เอกสารผ่านการรีวิวและแก้บั๊ก 2 จุดจาก draft
แรกแล้ว (§5.4, §5.5) พร้อมเริ่มเขียนโค้ดตาม checklist ข้างบน

---

## 📋 งานที่ทำไปแล้ว (Lab Rounds Summary)

### Lab 1-3: Foundation + Round Trip
-   `economy.proto` → `SectEconomyState.cs` (MessagePack stub)
-   `GameLifetimeScope.cs` (VContainer + MessagePipe TCP)
-   `TimeSystem.cs`, `McpBridgeProgram.cs`
-   **Result:** `get_sect_state` round trip ผ่านจริง (21 ส.ค. 2026)

### Lab 4-6: Write Path + World Events + Transport Bug
-   `ExecuteDecision` (Bridge → Unity) ผ่าน `IDistributedPublisher`
-   `WorldEventSystem` (สุ่ม event ทุก 15 วิ, auto-pause)
-   **Bug:** `IDistributedSubscriber` over TCP เปิด listen socket เสมอ → Bridge ชน port Unity
-   **Fix:** เปลี่ยน `await_next_world_event` เป็น Request-Response (`AwaitWorldEventRequest/Response`)
-   **Result:** Loop เต็มรูปแบบทำงานจริง (22 ส.ค. 2026)

### Lab 7: ExecuteDecision มีผลจริง + เลิก Protobuf
-   `SectStateProvider` ถือ live state instance เดียว (ไม่สร้างใหม่ทุก query)
-   `ApplyDecisionConsequence` ปรับ Stockpile จริง
-   **Decision:** เลิก Protobuf ถาวร → ใช้ MessagePack เป็นตัวจริง
-   **Result:** เห็นตัวเลขเปลี่ยนจริงหลัง `execute_decision`

### Lab 8-9: DiscipleSystem + Crafting
-   `RecruitOuterDisciple` (ผูกกับ event `new_disciple_applicant`)
-   `TickGathering` (passive resource, fractional accumulator)
-   `TickCrafting` (consume resources, produce items, ownership rule: Elder=Personal, Others=Stockpile)
-   **Gotcha:** Unity Editor `Run In Background` ต้องเปิด ไม่งั้น Tick หยุดตอนสลับหน้าต่าง

### Lab 10: Purchase Store
-   `purchase_item` MCP tool (Request-Response)
-   `TryPurchaseItem` (เช็คของ/เงิน → หัก → ย้ายเข้า PersonalInventory)
-   **Result:** ทดสอบผ่าน

### Lab 11-12: UI พื้นฐาน + Luban Pipeline
-   `SectHudView` (top bar HUD) → ต่อมาถูกแทนด้วย `ResourceHud`
-   **Luban:** ย้าย EventData จาก ScriptableObject → Excel → JSON → C# Plain Class
    -   Bug: `<module name="event">` ชน C# keyword → แก้เป็น `worldevent`
    -   Toolchain: `Tools/Luban/`, `DataTables/`, `gen.bat`

### Lab 13: Xianxia.UI.MVP Lite
-   Implement ตาม spec (`XIANXIA_UI_MVP_LITE_SPEC.md`)
-   Bug Fix: `DecisionExecutor` (แก้ channel ผิด), `UIRoot.ApplyLayout` (แก้ UI กองกลางจอ)
-   **Result:** EventPopup + ResourceHud ทำงานจริง (30 ส.ค. 2026)

### Lab 14: LogWindow Event Log
-   `LogWindowView` (scrolling TMP log, FIFO eviction, auto-scroll) + `LogWindowPresenter`
    (subscribe เฉพาะ `DiscipleRecruitedMessage`/`WorldEventTriggeredMessage`/`DecisionExecutedMessage`
    — ตัด `SectResourceChangedMessage` ออกกันน้ำท่วมจอ)
-   `DecisionExecutedMessage` ถูก publish จาก `DecisionExecutor` (funnel เดียว ไม่ว่าจะมาจาก UI หรือ bridge)
-   `UIBootstrap` เปิด LogWindow ตอนเกมเริ่ม
-   **Result:** ใช้งานจริง (1 ก.ย. 2026)

### Lab 15: Avatar v2.5 Portrait Swap + Sex/Gender
-   Dictionary Schema, Hair 2-Layer, Framing Presets, Category Tabs
-   `DiscipleSex` enum + Recruitment Logic
-   **Result:** Data Model พร้อม, MCP คืนค่า Sex ถูกต้อง

### Lab 16: Avatar v3 Outfit Packages — implement แล้ว **rollback**
-   (ตอน v3): `OutfitDef` + `TryApplyOutfit()` + UI Tab "ชุดแต่งกาย" + filtered options by pose/sex + pose-conflict warning
-   **(rollback 2026-09-03):** ลบ `outfits[]`/`OutfitDef`/`TryApplyOutfit`/`GetOutfit(s)`/`AvatarOutfitChangedMessage`/tab "ชุดแต่งกาย"/pose-conflict warning ออกทั้งหมด
-   **คงไว้:** `PoseId`/`sexTag` บน part, `[Key(2)] PoseId` ใน state (fallback `pose_idle_01`), pose validation ใน `TryChangeAvatarPart`, `PoseId` ใน `BuildSignature`, Randomize กรอง pose/sex
-   **Result:** กลับเป็น parts-only MVP — เลือก part ทีละ slot อิสระ; เหตุผล: LLMWiki `wiki/sources/avatar-appearance.md` §1

### Lab 17: Sex/Gender + Unity-MCP setup
-   `MockSectData` founders ได้ sex ชัดเจน (d000 M, d001 F, d002 F, d003 M)
-   `AvatarPartPool.GetPartsForSlot(slot, poseId, sex)` — `OnRandomize` กรอง part ตาม sex + pose
-   Side work: Unity-MCP integration setup (`.zcode` skills), ร่าง additive scene architecture (→ lab 18)

### Lab 18: Additive Scene Architecture — ✅ Implemented (v0.12)
-   Single Scene → Additive Scene: **CoreScene** (persistent systems + UI, never unload) +
    **GameplayScene(s)** (additive, unload/reload ได้)
-   `SceneLoader` singleton — `LoadGameplayScene(name)` unload-then-load (A→B swap), publish
    `SceneLoadedMessage`; registered ใน `GameLifetimeScope`
-   `AdditiveSceneTest` — runtime GUI ทดสอบ Load/Unload/Swap
-   VContainer: เลือก single root scope ใน CoreScene ไปก่อน (parent-child Option B เลื่อนไปทำเมื่อมี
    scene-specific service จริง)
-   **Gotchas**: ทั้งสอง scene ต้องอยู่ใน Build Settings ก่อนถึงจะ async load ได้; GameplayScene ห้ามมี
    `EventSystem`/`AudioListener` ซ้ำ
-   **Result:** เสร็จสมบูรณ์ — เอกสารสถานะเดิมของหัวข้อนี้ (เคยเขียนว่า "Draft phase") **ล้าสมัยแล้ว**
    อัปเดตในฉบับนี้ให้ตรงกับโค้ดจริง

### Lab 19 (Design, ยังไม่เขียนโค้ด): Task System v2 — Viewer-Controlled Tasks
-   ดูรายละเอียดเต็มที่หัวข้อ "🆕 ความคืบหน้าล่าสุด (รอบนี้) § 5" ด้านบน
-   สรุปสั้น: เพิ่ม `DiscipleOwnerType`/`OwnerId` บน `DiscipleState`, `TryAssignTask(requesterId, ...)`
    ตรวจสิทธิ์ฝั่ง Unity ครั้งเดียว, ใช้ TCP interprocess เดิม (ไม่สร้าง transport ใหม่) ให้ Twitch bot
    service ต่อเข้ามาเป็น client เพิ่มอีกตัว, แก้บั๊ก `TickCrafting` ที่ bypass `AdjustAndNotify`, และ
    ย้ำกฎ threading (`UniTask.SwitchToMainThread()`) สำหรับ handler ที่มาจาก interprocess แล้วโยงไปแตะ UI
-   **Status:** เอกสารออกแบบเสร็จ ผ่านการรีวิวบั๊ก 2 จุดแล้ว — ยังไม่เริ่ม implementation checklist

---

## 🚧 สิ่งที่ยังค้าง / TODO

### High Priority (ต้องทำก่อน Art ส่งงาน)
1.  **Art Assets (Sprites):**
    -   สร้างไฟล์ sprite จริงตาม `avatar_parts.json` 19 ชิ้นใน `Resources/Avatar/` (base/body/head/hair/face_marking/accessory)
    -   สร้าง hair back layers (`hair_topknot_long_back.png`, `hair_twin_tail_back.png`)
2.  **ทดสอบ UI จริง:**
    -   กด Play → หน้าจอ Avatar Customization ควรโผล่
    -   ทดสอบเปลี่ยน part แต่ละ slot → Preview ควรอัปเดตทันที
    -   ทดสอบ Randomize: ศิษย์ male ต้องไม่สุ่มได้ `head_female_01` (กรองตาม `DiscipleState.Sex`)

### Medium Priority (Phase 2)
3.  **UI Sex Selector:**
    -   เพิ่ม Toggle ใน `AvatarCustomizationPanel` (เปลี่ยน Male↔Female)
    -   เพิ่ม Selector ใน `EventPopup` (ตอนรับสมัคร)
4.  **Tinting System (Color):**
    -   `AvatarAppearance.Colors` มีอยู่แล้ว แต่ยังไม่มี logic ใน Renderer
    -   เริ่มที่ `Image.color` multiply (Method A) ก่อน
5.  ~~**MCP Tool `apply_outfit`:**~~ — **ยกเลิกพร้อม outfit packages** (rollback 2026-09-03); ถ้ามี pose ที่ 2 เข้าโปรเจกต์ค่อยพิจารณาใหม่
6.  **MCP Tool `change_avatar_part`:**
    -   Wire เรียบร้อยฝั่ง Unity (`ChangeAvatarPartHandler`) แต่ Bridge ยังไม่มี tool expose
7.  **🆕 Task System v2 — เริ่ม implement ตาม checklist ในหัวข้อ § 5.9:**
    -   ยืนยัน `[Key(N)]` ว่างจริงก่อน แล้วเพิ่ม `DiscipleOwnerType`/`OwnerId`
    -   เขียน `TaskDef`/`TaskPool`, `TryAssignTask(requesterId, ...)` (**ใช้ `AdjustAndNotify` เสมอ**),
        `AssignTaskRequest/Response`, `AssignTaskHandler`, MCP tool `assign_task`
    -   ระวัง threading — ใส่ `UniTask.SwitchToMainThread()` ในทุก UI ที่ subscribe
        `DiscipleTaskChangedMessage`
    -   Twitch integration service + flow ผูก viewer→disciple ownership เป็นงานแยกทีหลัง (ไม่ block
        การ implement ส่วน permission/task core)

### Low Priority (Phase 3+)
8.  **AvatarIconBaker:**
    -   Bake RenderTexture cache สำหรับ HUD icon (กัน UGUI rebuild หนักเมื่อมีศิษย์เยอะ)
9.  **DialoguePanel:**
    -   Portrait (Bust framing) + Text box
    -   ใช้ `AvatarRenderer` ตัวเดิม
10. **BuildingSystem / Combat / Stats:**
    -   ยังเป็น stub / ยังไม่ตัดสินใจแนวทาง
11. **Twitch Extension เต็มรูปแบบ**:
    -   ต่อยอดจาก Task System v2 — UI ฝั่งผู้ชม, flow ผูกความเป็นเจ้าของศิษย์, Twitch bot service จริง

---

## 📂 Workspace Layout (Multi-project Monorepo)
```text
Cultivation Together/              ← workspace root
 ├── Shared/                        ← canonical source for shared types
 │   ├── GameMessages.cs            ← sync to Unity + Bridge (+ AssignTaskRequest/Response,
 │   │                                 DiscipleTaskChangedMessage เมื่อ Task System v2 เริ่ม implement)
 │   ├── SectEconomyState.cs        ← Dictionary AvatarAppearance + DiscipleSex + PoseId
 │   │                                 (+ DiscipleOwnerType/OwnerId เมื่อ Task System v2 เริ่ม implement)
 │   └── MockSectData.cs            ← FromSlots factory + explicit Sex
 ├── UnityProject/                  ← open in Unity Hub
 │   ├── Assets/Scripts/
 │   │   ├── Core/                  ← TimeSystem, DecisionExecutor, GameLifetimeScope, SceneLoader
 │   │   ├── Data/                  ← AvatarPartPool (parts-only def-table + poseId/sexTag), LubanEventPool,
 │   │   │                             (TaskPool — planned, Task System v2)
 │   │   ├── Systems/               ← SectStateProvider (TryChangeAvatarPart + pose validation,
 │   │   │                             TryAssignTask — planned), DiscipleSystem, etc.
 │   │   └── UI/                    ← Xianxia.UI.MVP Lite (Views/Presenters/Core)
 │   └── Resources/Data/            ← avatar_parts.json (parts only — outfits ถูกลบ), worldevent_*.json
 ├── McpBridge/                     ← .NET 8 console app
 │   └── Program.cs                 ← MCP server (SectQueryTools/SectActionTools, + assign_task — planned)
 ├── DataTables/                    ← Luban Excel sources
 ├── Tools/Luban/                   ← Luban binary
 ├── LLMWiki/                       ← this folder (architecture.md, disciples.md, avatar-appearance.md, etc.)
 └── sync-shared.sh                 ← copy Shared/ → Unity + Bridge
```

**Rule:** แก้ไฟล์ใน `Shared/` → รัน `./sync-shared.sh` → ห้ามแก้ copy ใน Unity/Bridge โดยตรง

---

## 🔑 Key Architectural Decisions (Updated)

### Why Dictionary AvatarAppearance (not Fixed Properties)
-   Slot โตจาก 4 → 10+ → Fixed `[Key]` จะ break schema ทุกครั้งที่เพิ่มหมวด
-   Dictionary = เพิ่ม slot แก้แค่ JSON, AI GM ค้นพบ slot ได้เองจาก `get_sect_state`
-   `GetSlot/SetSlot` API เหมือนเดิม → Presenter ไม่ต้องแก้

### Why NOT Outfit Packages (v3 — REJECTED / rolled back 2026-09-03)
-   ตอนนี้ทุก part เป็น `pose_idle_01` เดียว → outfit = แค่ "กด `SetSlot` หลายครั้งรวดเดียว" = derive จาก `parts[]` ได้ 100% ไม่มีข้อมูลใหม่
-   `outfits[]` สร้าง dual source of truth (ลบ part ใน `parts[]` แต่ลืมลบใน `outfits[]` → runtime พัง) — ไม่คุ้มกับประโยชน์
-   เงื่อนไขรื้อฟื้น = **เมื่อมี pose ≥ 2 เข้าโปรเจกต์จริง** (ตอนนั้น pose validation/outfit ถึงจะมีค่าจริง)
-   คงไว้ (ต้นทุน ~0): `poseId`/`sexTag` บน part, `[Key(2)] PoseId` ใน state (`PoseId == ""` → fallback `pose_idle_01`), pose validation ใน `TryChangeAvatarPart`, Randomize กรอง pose/sex

### Why Portrait Swap (not Paper Doll Tiles)
-   Target visual: VN-style dialogue portrait (ไม่ใช่ chibi tile เล็กๆ)
-   Art วาดบน canvas/pose เดียวกัน → layer ซ้อนสนิทโดยไม่ต้องแก้โค้ด render
-   Hair 2-Layer (back/front) จำเป็นสำหรับผมยาว (ไม่งั้นวิกมุดหัว)

### Why Explicit DiscipleSex (not Implicit from Slot)
-   State ต้อง hold ids/flags ชัดเจน ไม่ซ่อน derivation (cryptic bug-wait)
-   Query ได้ตรงๆ ("is disciple X male?") ไม่ต้อง parse ชื่อไฟล์รูป
-   Source of truth สำหรับ recruitment flavor / gender-gated content ในอนาคต

### Why Direct Method Call for In-Process Logic (not Pub/Sub)
-   **Lesson (Lab 13):** `EventPopupPresenter` (in-memory pub) vs `DecisionLogger` (interprocess sub) → คนละ graph → เงียบกริบ
-   **Rule:** In-process logic ที่ต้อง trigger ระบบอื่นใน Unity → เรียก Direct Method (`DecisionExecutor`)
-   Pub/Sub ใช้เฉพาะ: (1) Cross-process (Bridge ↔ Unity) หรือ (2) Broadcast ที่ไม่มี Unity-side listener นอกจาก publisher เอง

### Why MessagePack (not Protobuf)
-   Bridge เป็น C# เหมือนกัน → ไม่ข้าม language boundary
-   MessagePack เร็วกว่า, ไม่ต้อง codegen pipeline
-   ตัดสินใจถาวรตั้งแต่ Lab 7

### Why Luban (not ScriptableObject)
-   Events/AvatarParts เป็น tabular data → Excel เหมาะกว่า
-   ไม่ต้องคลิกสร้าง `.asset` → แก้ด้วย text editor / git diff ได้
-   Generate C# Plain Class + JSON loader (ไม่ใช่ SO) → สอดคล้องกับแนวทาง "Plain C# everywhere"

### Why Additive Scene Architecture (implemented lab 18)
-   UI persistent ข้าม scene (ไม่ต้อง recreate ทุกรอบ)
-   แยก concerns: core systems (TimeSystem, SectStateProvider, MessagePipe) vs scene content
    (environment/NPC/buildings)
-   รองรับ multiple biomes/locations ในอนาคตโดยไม่กระทบ persistent UI/state
-   VContainer: เลือก single root scope ก่อน (ง่ายที่สุด) แทน parent-child scope จนกว่าจะมี
    scene-specific service ที่จำเป็นจริง

### 🆕 Why Permission Check Happens Once, Server-Side, in Unity (Task System v2)
-   มีมากกว่าหนึ่ง client ที่อาจเรียกสั่งงานได้ (AI GM ผ่าน MCP, Twitch bot ในอนาคต, ผู้เล่นผ่าน UI) —
    ถ้าให้แต่ละ client เช็คสิทธิ์เอง จะมีจุดโกง/บั๊กได้หลายจุด
-   ใช้หลักการเดียวกับ `TryPurchaseItem`: validate ทั้งหมด → mutate ทั้งหมด → return, ไม่มี partial state
-   `requesterId` เป็นพารามิเตอร์บังคับของทุก mutation ที่ viewer อาจเรียกได้ ("SECT_MASTER" = ข้าม
    check, อย่างอื่น = ต้องตรง `OwnerId`) — ไม่มี "trusted caller" โดย default

### 🆕 Why Twitch Integration Reuses the Existing TCP Interprocess Server (not a New Pipeline)
-   Unity เป็น TCP server (`HostAsServer = true`) อยู่แล้ว และ TCP server รับหลาย client ได้โดย
    ธรรมชาติ — ไม่จำเป็นต้องผูกกับ `McpBridge` แค่ตัวเดียว
-   Twitch bot service เป็นแค่ client อีกตัวที่พูด `MessagePipe.Interprocess` โปรโตคอลเดียวกัน ส่ง
    request type เดียวกัน (`AssignTaskRequest`) ต่างแค่ค่า `RequesterId`
-   หลีกเลี่ยงการดูแล transport คู่ขนาน (port ใหม่, message type ใหม่, serialization แยก) ที่ไม่จำเป็น
```

## File Tree (filtered)

```
  .agents/mcp.json
  .agents/skills/animation-create/SKILL.md
  .agents/skills/animation-get-data/SKILL.md
  .agents/skills/animation-modify/SKILL.md
  .agents/skills/animator-create/SKILL.md
  .agents/skills/animator-get-data/SKILL.md
  .agents/skills/animator-modify/SKILL.md
  .agents/skills/assets-copy/SKILL.md
  .agents/skills/assets-create-folder/SKILL.md
  .agents/skills/assets-delete/SKILL.md
  .agents/skills/assets-find-built-in/SKILL.md
  .agents/skills/assets-find/SKILL.md
  .agents/skills/assets-get-data/SKILL.md
  .agents/skills/assets-material-create/SKILL.md
  .agents/skills/assets-modify/SKILL.md
  .agents/skills/assets-move/SKILL.md
  .agents/skills/assets-prefab-close/SKILL.md
  .agents/skills/assets-prefab-create/SKILL.md
  .agents/skills/assets-prefab-instantiate/SKILL.md
  .agents/skills/assets-prefab-open/SKILL.md
  .agents/skills/assets-prefab-save/SKILL.md
  .agents/skills/assets-refresh/SKILL.md
  .agents/skills/assets-shader-get-data/SKILL.md
  .agents/skills/assets-shader-list-all/SKILL.md
  .agents/skills/chroma-key-portrait-pipeline/SKILL.md
  .agents/skills/cinemachine-add-extension/SKILL.md
  .agents/skills/cinemachine-brain-ensure/SKILL.md
  .agents/skills/cinemachine-camera-create/SKILL.md
  .agents/skills/cinemachine-camera-get/SKILL.md
  .agents/skills/cinemachine-camera-list/SKILL.md
  .agents/skills/cinemachine-get/SKILL.md
  .agents/skills/cinemachine-modify/SKILL.md
  .agents/skills/cinemachine-set-aim/SKILL.md
  .agents/skills/cinemachine-set-body/SKILL.md
  .agents/skills/cinemachine-set-default-blend/SKILL.md
  .agents/skills/cinemachine-set-lens/SKILL.md
  .agents/skills/cinemachine-set-noise/SKILL.md
  .agents/skills/cinemachine-set-priority/SKILL.md
  .agents/skills/cinemachine-set-targets/SKILL.md
  .agents/skills/console-clear-logs/SKILL.md
  .agents/skills/console-get-logs/SKILL.md
  .agents/skills/editor-application-get-state/SKILL.md
  .agents/skills/editor-application-set-state/SKILL.md
  .agents/skills/editor-selection-get/SKILL.md
  .agents/skills/editor-selection-set/SKILL.md
  .agents/skills/gameobject-component-add/SKILL.md
  .agents/skills/gameobject-component-destroy/SKILL.md
  .agents/skills/gameobject-component-get/SKILL.md
  .agents/skills/gameobject-component-list-all/SKILL.md
  .agents/skills/gameobject-component-modify/SKILL.md
  .agents/skills/gameobject-create/SKILL.md
  .agents/skills/gameobject-destroy/SKILL.md
  .agents/skills/gameobject-duplicate/SKILL.md
  .agents/skills/gameobject-find/SKILL.md
  .agents/skills/gameobject-modify/SKILL.md
  .agents/skills/gameobject-set-parent/SKILL.md
  .agents/skills/inputsystem-action-add/SKILL.md
  .agents/skills/inputsystem-action-remove/SKILL.md
  .agents/skills/inputsystem-actionmap-add/SKILL.md
  .agents/skills/inputsystem-actionmap-remove/SKILL.md
  .agents/skills/inputsystem-asset-create/SKILL.md
  .agents/skills/inputsystem-binding-add/SKILL.md
  .agents/skills/inputsystem-binding-composite-add/SKILL.md
  .agents/skills/inputsystem-binding-remove/SKILL.md
  .agents/skills/inputsystem-binding-set/SKILL.md
  .agents/skills/inputsystem-controlscheme-add/SKILL.md
  .agents/skills/inputsystem-get/SKILL.md
  .agents/skills/inputsystem-modify/SKILL.md
  .agents/skills/inputsystem-save/SKILL.md
  .agents/skills/navigation-agent-add/SKILL.md
  .agents/skills/navigation-agent-set-destination/SKILL.md
  .agents/skills/navigation-get/SKILL.md
  .agents/skills/navigation-link-add/SKILL.md
  .agents/skills/navigation-list/SKILL.md
  .agents/skills/navigation-modifier-add/SKILL.md
  .agents/skills/navigation-modifier-volume-add/SKILL.md
  .agents/skills/navigation-modify/SKILL.md
  .agents/skills/navigation-set-bake-settings/SKILL.md
  .agents/skills/navigation-surface-add/SKILL.md
  .agents/skills/navigation-surface-bake/SKILL.md
  .agents/skills/object-get-data/SKILL.md
  .agents/skills/object-modify/SKILL.md
  .agents/skills/package-add/SKILL.md
  .agents/skills/package-list/SKILL.md
  .agents/skills/package-remove/SKILL.md
  .agents/skills/package-search/SKILL.md
  .agents/skills/ping/SKILL.md
  .agents/skills/portrait-edit-prompt/SKILL.md
  .agents/skills/portrait-edit-prompt/assets/presets.json
  .agents/skills/portrait-edit-prompt/assets/recipe.example.json
  .agents/skills/portrait-edit-prompt/scripts/compose_edit_prompt.py
  .agents/skills/profiler-capture-frame/SKILL.md
  .agents/skills/profiler-clear-data/SKILL.md
  .agents/skills/profiler-enable-module/SKILL.md
  .agents/skills/profiler-get-memory-stats/SKILL.md
  .agents/skills/profiler-get-rendering-stats/SKILL.md
  .agents/skills/profiler-get-script-stats/SKILL.md
  .agents/skills/profiler-get-status/SKILL.md
  .agents/skills/profiler-list-modules/SKILL.md
  .agents/skills/profiler-load-data/SKILL.md
  .agents/skills/profiler-save-data/SKILL.md
  .agents/skills/profiler-start/SKILL.md
  .agents/skills/profiler-stop/SKILL.md
  .agents/skills/reflection-method-call/SKILL.md
  .agents/skills/reflection-method-find/SKILL.md
  .agents/skills/scene-create/SKILL.md
  .agents/skills/scene-get-data/SKILL.md
  .agents/skills/scene-list-opened/SKILL.md
  .agents/skills/scene-open/SKILL.md
  .agents/skills/scene-save/SKILL.md
  .agents/skills/scene-set-active/SKILL.md
  .agents/skills/scene-unload/SKILL.md
  .agents/skills/screenshot-camera/SKILL.md
  .agents/skills/screenshot-game-view/SKILL.md
  .agents/skills/screenshot-isolated/SKILL.md
  .agents/skills/screenshot-scene-view/SKILL.md
  .agents/skills/script-delete/SKILL.md
  .agents/skills/script-execute/SKILL.md
  .agents/skills/script-read/SKILL.md
  .agents/skills/script-update-or-create/SKILL.md
  .agents/skills/tests-run/SKILL.md
  .agents/skills/tool-set-enabled-state/SKILL.md
  .agents/skills/type-get-json-schema/SKILL.md
  .agents/skills/typesafe-ai/LICENSE
  .agents/skills/typesafe-ai/SKILL.md
  .agents/skills/unity-initial-setup/SKILL.md
  .agents/skills/unity-skill-create/SKILL.md
  .agents/skills/unity-skill-generate/SKILL.md
  .agents/skills/unity-tool-list/SKILL.md
  .freebuff/project-id
  .gitattributes
  .gitignore
  .opencode/AGENT.md
  .vscode/settings.json
  .zcode/AGENT.md
  .zcode/config.json
  Artwork/Body_dizijm.png
  Artwork/qwen21_hanfu_atlas_edit.json
  DataTables/Data/EventChoiceDef.csv
  DataTables/Data/EventDef.csv
  DataTables/Defines/schema.xml
  DataTables/gen.bat
  DataTables/gen.sh
  DataTables/luban.conf
  LLMWiki/.obsidian/app.json
  LLMWiki/.obsidian/appearance.json
  LLMWiki/.obsidian/community-plugins.json
  LLMWiki/.obsidian/core-plugins.json
  LLMWiki/.obsidian/graph.json
  LLMWiki/.obsidian/plugins/karpathywiki/data.json
  LLMWiki/.obsidian/plugins/karpathywiki/main.js
  LLMWiki/.obsidian/plugins/karpathywiki/manifest.json
  LLMWiki/.obsidian/plugins/karpathywiki/styles.css
  LLMWiki/.obsidian/plugins/smart-connections/data.json
  LLMWiki/.obsidian/plugins/smart-connections/main.js
  LLMWiki/.obsidian/plugins/smart-connections/manifest.json
  LLMWiki/.obsidian/plugins/smart-connections/styles.css
  LLMWiki/.obsidian/workspace.json
  LLMWiki/.smart-env/embedding_models/embedding_models.ajson
  LLMWiki/.smart-env/event_logs/event_logs.ajson
  LLMWiki/.smart-env/smart_blocks/mf_ccarbz
  LLMWiki/.smart-env/smart_env.json
  LLMWiki/.smart-env/smart_sources/mf_ccarbz
  LLMWiki/.smart-env/smart_sources/smart_sources.ajson
  LLMWiki/AGENTS.md
  LLMWiki/README.md
  LLMWiki/wiki/concepts/additive-scene-architecture.md
  LLMWiki/wiki/concepts/crafting-system.md
  LLMWiki/wiki/concepts/data-pipeline.md
  LLMWiki/wiki/concepts/decision-pipeline.md
  LLMWiki/wiki/concepts/disciple-visual-system.md
  LLMWiki/wiki/concepts/gathering-system.md
  LLMWiki/wiki/concepts/log-window.md
  LLMWiki/wiki/concepts/mcp-bridge.md
  LLMWiki/wiki/concepts/message-pipe-bus.md
  LLMWiki/wiki/concepts/mvp-ui.md
  LLMWiki/wiki/concepts/purchase-store.md
  LLMWiki/wiki/concepts/state-management.md
  LLMWiki/wiki/concepts/task-system-v2.md
  LLMWiki/wiki/concepts/time-system.md
  LLMWiki/wiki/concepts/vcontainer-composition.md
  LLMWiki/wiki/concepts/world-events.md
  LLMWiki/wiki/conventions.md
  LLMWiki/wiki/decisions/visual-overrides-straight-alpha.md
  LLMWiki/wiki/entities/avatar-appearance.md
  LLMWiki/wiki/entities/disciples.md
  LLMWiki/wiki/entities/events.md
  LLMWiki/wiki/entities/items.md
  LLMWiki/wiki/entities/resources.md
  LLMWiki/wiki/entities/sects.md
  LLMWiki/wiki/entities/world-events.md
  LLMWiki/wiki/index.md
  LLMWiki/wiki/log.md
  LLMWiki/wiki/overview.md
  LLMWiki/wiki/schema/config.md
  LLMWiki/wiki/sources/architecture.md
  LLMWiki/wiki/sources/avatar-appearance.md
  LLMWiki/wiki/sources/bug-log.md
  LLMWiki/wiki/sources/building-system.md
  LLMWiki/wiki/sources/changelog-summary.md
```

