# Cultivation-Together Context Pack

- Repository: Memphis004/Cultivation-Together
- Commit: 8c866ca
- Branch: main
- Generated: 2026-10-09
- Uncommitted changes in working tree: 26 file(s) (ไฟล์ที่แนบมาอ่านจาก working tree ไม่ใช่จาก commit — ถ้า > 0 อาจต่างจาก commit)
- Files after .aiignore filter: 2030

## Ground Rules (สำคัญมาก — บังคับ AI)

1. ใช้เฉพาะข้อมูลจากเอกสารนี้เท่านั้น ห้ามเดา
2. ห้ามสร้างชื่อไฟล์, class, method, API, หรือ behavior ที่ไม่มีใน context
3. เวลาอธิบายโค้ด ให้อ้างอิง path และ line range (ไฟล์ที่แนบมีเลขบรรทัดนำหน้าแล้ว เช่น `Shared/GameMessages.cs:45-60`)
4. ถ้าข้อมูลไม่พอ ให้ตอบเป็น JSON block แบบนี้ แล้วหยุด:

```need_files
{
  "paths": ["path/to/file.cs"],
  "reason": "เหตุผลที่ต้องอ่านไฟล์นี้"
}
```

5. โครงสร้าง repo:
   - `Shared/` = canonical source ของ DTO/state (แก้ตรงนี้เท่านั้น แล้วรัน `./sync-shared.sh` — ห้ามแก้ `UnityProject/Assets/Scripts/Shared/` หรือ `McpBridge/Shared/` ตรง ๆ; มี 3 สำเนา ถ้าไม่ sync จะ drift)
   - `UnityProject/` = Unity 6 game (VContainer + MessagePipe + UGUI/TMP)
   - `McpBridge/` = .NET 8 console app, MCP server (stdio) ↔ Unity (TCP 127.0.0.1:3215)
   - `LLMWiki/` = wiki (อ่าน `wiki/index.md` ก่อน) — **code คือ source of truth** ถ้า wiki ขัดกับ code ให้ code ชนะและแจ้งว่า wiki ล้าสมัย
   - `DataTables/` = Luban: `Data/*.csv` + `Defines/schema.xml` → `gen.sh`/`gen.bat` → `UnityProject/Assets/Scripts/Data/Gen` (namespace `cfg.game`, ห้ามแก้ไฟล์ใน Gen/) + JSON ใน `Resources/DataTables`

6. กฎโค้ดที่ผิดบ่อย (ยืนยันแล้วจาก bug log):
   - C# 8 เท่านั้น: ห้าม `record`, `init`, `global using`, target-typed `new`
   - ทุกการแก้ `Stockpile.RawResources` ต้องผ่าน `AdjustAndNotify()` ใน `SectStateProvider` เท่านั้น (ไม่งั้น HUD ไม่อัปเดต)
   - handler ที่มาจาก MessagePipe.Interprocess (TCP) ทำงานบน **background thread** — แตะ Unity API ต้อง `await UniTask.SwitchToMainThread()` ก่อน
   - in-process pub/sub ห้ามใช้แทนการเรียกข้าม process — ถ้า UI ต้องทำสิ่งเดียวกับ bridge ให้เรียก shared entry point ตรง ๆ (ตัวอย่าง: `DecisionExecutor`)
   - ฝั่ง visual/building/task message เป็น in-memory เท่านั้น ห้าม register บน interprocess broker
   - request-response ต้อง register `RegisterTcpRemoteRequestHandler` + `RegisterAsyncRequestHandler` ทั้งคู่ (ดู `InterprocessInstaller.cs`)
   - MessagePack `[Key(N)]` append-only ห้ามแก้เลขเดิม
   - ห้ามฟื้น outfit / `TryApplyOutfit` (decision record ใน `LLMWiki/wiki/sources/avatar-appearance.md` §1)
   - ห้ามใช้ `FindObjectOfType`/reflection หา service — ใช้ DI (constructor injection)
   - registration ใหม่ต้องไปอยู่ installer ที่ถูกชั้น (`Core/Installers/*`) หรือ `SectSceneLifetimeScope` ถ้าผูกฉาก — ห้ามยัดกลับ `GameLifetimeScope.Configure()`

## Known Issues / Open Items

ดูรายการล่าสุดในไฟล์ `LLMWiki/wiki/sources/open-questions.md` (แนบด้านล่าง) และ "Recent Wiki Log" ท้ายเอกสาร — script นี้ไม่ hardcode รายการที่ล้าสมัยง่ายอีกแล้ว

## Key Symbols (C# types per file)

(ตัด `Gen/`, `Tests/`, `Editor/`, `Spikes/` ออก — ถ้าต้องการให้ขอผ่าน need_files)

- `McpBridge/Program.cs`: static class SectQueryTools, static class SectActionTools
- `McpBridge/Shared/GameMessages.cs`: class DiscipleRecruitedMessage, class DiscipleRankChangedMessage, class SectResourceChangedMessage, class ContributionEarnedMessage, class SceneLoadedMessage, class WorldEventTriggeredMessage, class TimeSpeedChangedMessage, class SectStateQuery, class SectStateSnapshot, class AwaitWorldEventRequest, class AwaitWorldEventResponse, class EventChoiceInfo, class ExecuteDecisionMessage, class DecisionExecutedMessage, class BuildModeStartedMessage, class BuildModeEndedMessage, class PurchaseItemRequest, class PurchaseItemResponse, class BuildingPlacedMessage, class DiscipleChibiBackendChangedMessage, class DiscipleSelectedMessage, class AvatarEquipmentChangedMessage, class ChangeAvatarPartRequest, class ChangeAvatarPartResponse, class AssignTaskRequest, class AssignTaskResponse, class DiscipleTaskChangedMessage, class DiscipleOwnerChangedMessage, class OwnershipObservabilityQuery, class OwnershipObservabilitySnapshot, class TaskChangeObservabilityQuery, class TaskChangeObservabilitySnapshot, class TaskProtectionQuery, class TaskProtectionEntry, class TaskProtectionSnapshot, class SectViewerMembershipSave, class SectSavedOwnership, static class InterprocessTopics
- `McpBridge/Shared/MockSectData.cs`: static class MockSectData
- `McpBridge/Shared/SectEconomyState.cs`: enum OwnerScope, enum DiscipleRank, enum DiscipleSex, enum DiscipleOwnerType, enum ChibiBackend, class CurrencyWallet, class InventoryItem, class DiscipleState, class SectStockpile, class PlacedBuildingState, class SectEconomyState, struct SlotPart, sealed class AvatarAppearance, static class AvatarSlots
- `McpBridge/Shared/ViewerMembership.cs`: enum ViewerMembershipStatus, class ViewerRecord, class PendingViewerApplication, interface IClock, sealed class UtcClock, class SectViewerRegistry
- `Shared/GameMessages.cs`: class DiscipleRecruitedMessage, class DiscipleRankChangedMessage, class SectResourceChangedMessage, class ContributionEarnedMessage, class SceneLoadedMessage, class WorldEventTriggeredMessage, class TimeSpeedChangedMessage, class SectStateQuery, class SectStateSnapshot, class AwaitWorldEventRequest, class AwaitWorldEventResponse, class EventChoiceInfo, class ExecuteDecisionMessage, class DecisionExecutedMessage, class BuildModeStartedMessage, class BuildModeEndedMessage, class PurchaseItemRequest, class PurchaseItemResponse, class BuildingPlacedMessage, class DiscipleChibiBackendChangedMessage, class DiscipleSelectedMessage, class AvatarEquipmentChangedMessage, class ChangeAvatarPartRequest, class ChangeAvatarPartResponse, class AssignTaskRequest, class AssignTaskResponse, class DiscipleTaskChangedMessage, class DiscipleOwnerChangedMessage, class OwnershipObservabilityQuery, class OwnershipObservabilitySnapshot, class TaskChangeObservabilityQuery, class TaskChangeObservabilitySnapshot, class TaskProtectionQuery, class TaskProtectionEntry, class TaskProtectionSnapshot, class SectViewerMembershipSave, class SectSavedOwnership, static class InterprocessTopics
- `Shared/MockSectData.cs`: static class MockSectData
- `Shared/SectEconomyState.cs`: enum OwnerScope, enum DiscipleRank, enum DiscipleSex, enum DiscipleOwnerType, enum ChibiBackend, class CurrencyWallet, class InventoryItem, class DiscipleState, class SectStockpile, class PlacedBuildingState, class SectEconomyState, struct SlotPart, sealed class AvatarAppearance, static class AvatarSlots
- `Shared/ViewerMembership.cs`: enum ViewerMembershipStatus, class ViewerRecord, class PendingViewerApplication, interface IClock, sealed class UtcClock, class SectViewerRegistry
- `UnityProject/Assets/Scripts/Building/BuildingDef.cs`: enum BuildingCategory, class BuildingDef, class BuildingCostEntry
- `UnityProject/Assets/Scripts/Building/BuildingDefPool.cs`: class BuildingDefTable, class BuildingDefPool
- `UnityProject/Assets/Scripts/Building/BuildingGrid.cs`: class BuildingGrid
- `UnityProject/Assets/Scripts/Building/PlaceableLandMask.cs`: static class PlaceableLandMask
- `UnityProject/Assets/Scripts/Building/PlacementController.cs`: class PlacementController
- `UnityProject/Assets/Scripts/Core/AssignTaskHandler.cs`: class AssignTaskHandler
- `UnityProject/Assets/Scripts/Core/ChangeAvatarPartHandler.cs`: class ChangeAvatarPartHandler
- `UnityProject/Assets/Scripts/Core/DecisionExecutor.cs`: class DecisionExecutor
- `UnityProject/Assets/Scripts/Core/DecisionLogger.cs`: class DecisionLogger
- `UnityProject/Assets/Scripts/Core/GameLifetimeScope.cs`: class GameLifetimeScope
- `UnityProject/Assets/Scripts/Core/Installers/BuildingInstaller.cs`: static class BuildingInstaller
- `UnityProject/Assets/Scripts/Core/Installers/GameplayInstaller.cs`: static class GameplayInstaller
- `UnityProject/Assets/Scripts/Core/Installers/InterprocessInstaller.cs`: static class InterprocessInstaller
- `UnityProject/Assets/Scripts/Core/Installers/UIInstaller.cs`: static class UIInstaller
- `UnityProject/Assets/Scripts/Core/Installers/VisualInstaller.cs`: static class VisualInstaller
- `UnityProject/Assets/Scripts/Core/OwnershipObservability.cs`: class OwnershipObservabilityBuffer, class OwnershipObservabilityHandler
- `UnityProject/Assets/Scripts/Core/PurchaseItemHandler.cs`: class PurchaseItemHandler
- `UnityProject/Assets/Scripts/Core/SceneLoader.cs`: class SceneLoader
- `UnityProject/Assets/Scripts/Core/SceneMessages.cs`: class SceneUnloadedMessage
- `UnityProject/Assets/Scripts/Core/SceneNames.cs`: static class SceneNames
- `UnityProject/Assets/Scripts/Core/TaskObservability.cs`: class TaskChangeObservabilityBuffer, class TaskChangeObservabilityHandler, class TaskProtectionHandler
- `UnityProject/Assets/Scripts/Core/TimeSystem.cs`: class TimeSystem, class AwaitWorldEventHandler, class SectStateQueryHandler, enum TaskPermissionOutcome, sealed class TaskPermissionResult, interface ISectStateProvider
- `UnityProject/Assets/Scripts/Core/ViewerMembershipPersistence.cs`: class ViewerMembershipPersistenceSystem
- `UnityProject/Assets/Scripts/Data/AvatarPartPool.cs`: class AvatarPartDef, class AvatarPartTable, class AvatarPartPool
- `UnityProject/Assets/Scripts/Data/LubanEventPool.cs`: class LubanEventPool
- `UnityProject/Assets/Scripts/Scenes/SectScene/SectSceneLifetimeScope.cs`: sealed class SectSceneLifetimeScope
- `UnityProject/Assets/Scripts/Shared/GameMessages.cs`: class DiscipleRecruitedMessage, class DiscipleRankChangedMessage, class SectResourceChangedMessage, class ContributionEarnedMessage, class SceneLoadedMessage, class WorldEventTriggeredMessage, class TimeSpeedChangedMessage, class SectStateQuery, class SectStateSnapshot, class AwaitWorldEventRequest, class AwaitWorldEventResponse, class EventChoiceInfo, class ExecuteDecisionMessage, class DecisionExecutedMessage, class BuildModeStartedMessage, class BuildModeEndedMessage, class PurchaseItemRequest, class PurchaseItemResponse, class BuildingPlacedMessage, class DiscipleChibiBackendChangedMessage, class DiscipleSelectedMessage, class AvatarEquipmentChangedMessage, class ChangeAvatarPartRequest, class ChangeAvatarPartResponse, class AssignTaskRequest, class AssignTaskResponse, class DiscipleTaskChangedMessage, class DiscipleOwnerChangedMessage, class OwnershipObservabilityQuery, class OwnershipObservabilitySnapshot, class TaskChangeObservabilityQuery, class TaskChangeObservabilitySnapshot, class TaskProtectionQuery, class TaskProtectionEntry, class TaskProtectionSnapshot, class SectViewerMembershipSave, class SectSavedOwnership, static class InterprocessTopics
- `UnityProject/Assets/Scripts/Shared/MockSectData.cs`: static class MockSectData
- `UnityProject/Assets/Scripts/Shared/SectEconomyState.cs`: enum OwnerScope, enum DiscipleRank, enum DiscipleSex, enum DiscipleOwnerType, enum ChibiBackend, class CurrencyWallet, class InventoryItem, class DiscipleState, class SectStockpile, class PlacedBuildingState, class SectEconomyState, struct SlotPart, sealed class AvatarAppearance, static class AvatarSlots
- `UnityProject/Assets/Scripts/Shared/ViewerMembership.cs`: enum ViewerMembershipStatus, class ViewerRecord, class PendingViewerApplication, interface IClock, sealed class UtcClock, class SectViewerRegistry
- `UnityProject/Assets/Scripts/Systems/BuildingSystem.cs`: class BuildingSystem
- `UnityProject/Assets/Scripts/Systems/DiscipleSystem.cs`: class DiscipleSystem
- `UnityProject/Assets/Scripts/Systems/ResourceCraftingSystem.cs`: class ResourceCraftingSystem
- `UnityProject/Assets/Scripts/Systems/SectStateProvider.cs`: class SectStateProvider
- `UnityProject/Assets/Scripts/Systems/WorldEventSystem.cs`: class WorldEventSystem
- `UnityProject/Assets/Scripts/UI/Core/IUIView.cs`: interface IUIView
- `UnityProject/Assets/Scripts/UI/Core/IUIViewPresenter.cs`: interface IUIViewPresenter
- `UnityProject/Assets/Scripts/UI/Core/UIPanelCatalog.cs`: class UIPanelCatalog
- `UnityProject/Assets/Scripts/UI/Core/UIPanelDefinition.cs`: class UIPanelDefinition
- `UnityProject/Assets/Scripts/UI/Core/UIPanelHandle.cs`: class UIPanelHandle
- `UnityProject/Assets/Scripts/UI/Core/UIPresenter.cs`: abstract class UIPresenter
- `UnityProject/Assets/Scripts/UI/Core/UIPresenterKind.cs`: enum UIPresenterKind
- `UnityProject/Assets/Scripts/UI/Core/UIRoot.cs`: class UIRoot
- `UnityProject/Assets/Scripts/UI/Core/UIService.cs`: class UIService
- `UnityProject/Assets/Scripts/UI/Presenters/AvatarCustomizationPresenter.cs`: class AvatarCustomizationPresenter
- `UnityProject/Assets/Scripts/UI/Presenters/BottomMenuPresenter.cs`: class BottomMenuPresenter
- `UnityProject/Assets/Scripts/UI/Presenters/BuildingMenuPresenter.cs`: class BuildingMenuPresenter
- `UnityProject/Assets/Scripts/UI/Presenters/DiscipleDetailPresenter.cs`: class DiscipleDetailPresenter
- `UnityProject/Assets/Scripts/UI/Presenters/DiscipleDetailTabPresenters.cs`: interface IDiscipleTabPresenter, class InfoTabPresenter, class StatusTabPresenter, class PlaceholderTabPresenter
- `UnityProject/Assets/Scripts/UI/Presenters/DiscipleListPresenter.cs`: sealed class DiscipleListArgs, class DiscipleListPresenter
- `UnityProject/Assets/Scripts/UI/Presenters/EventPopupPresenter.cs`: class EventPopupOpenArgs, class EventPopupPresenter
- `UnityProject/Assets/Scripts/UI/Presenters/LogWindowPresenter.cs`: class LogWindowPresenter
- `UnityProject/Assets/Scripts/UI/Presenters/ResourcePopupPresenter.cs`: sealed class ResourcePopupArgs, class ResourcePopupPresenter
- `UnityProject/Assets/Scripts/UI/Presenters/TaskAssignmentPresenter.cs`: class TaskAssignmentPresenter
- `UnityProject/Assets/Scripts/UI/Presenters/WalletHudPresenter.cs`: class WalletHudPresenter
- `UnityProject/Assets/Scripts/UI/Systems/BuildingPlacementUISystem.cs`: class BuildingPlacementUISystem
- `UnityProject/Assets/Scripts/UI/Systems/DiscipleDetailUISystem.cs`: class DiscipleDetailUISystem
- `UnityProject/Assets/Scripts/UI/Systems/UIBootstrap.cs`: class UIBootstrap
- `UnityProject/Assets/Scripts/UI/Systems/WorldEventUISystem.cs`: class WorldEventUISystem
- `UnityProject/Assets/Scripts/UI/Views/AvatarCustomizationView.cs`: class AvatarCustomizationView
- `UnityProject/Assets/Scripts/UI/Views/AvatarCustomizationViewData.cs`: sealed class AvatarCustomizationPayload, struct AvatarCategoryTabViewData, struct AvatarSlotTabViewData, struct AvatarPartOptionViewData
- `UnityProject/Assets/Scripts/UI/Views/AvatarOptionButton.cs`: class AvatarOptionButton
- `UnityProject/Assets/Scripts/UI/Views/AvatarRenderer.cs`: enum AvatarFraming, struct AvatarFramingPreset, class AvatarRenderer
- `UnityProject/Assets/Scripts/UI/Views/BottomMenuView.cs`: class BottomMenuView
- `UnityProject/Assets/Scripts/UI/Views/BuildingMenuView.cs`: sealed class BuildingMenuArgs, sealed class BuildingTabCell, sealed class BuildingItemCell, class BuildingMenuView
- `UnityProject/Assets/Scripts/UI/Views/BuildingPlacementView.cs`: sealed class BuildingPlacementArgs, class BuildingPlacementView
- `UnityProject/Assets/Scripts/UI/Views/DiscipleDetailView.cs`: enum DiscipleTab, class DiscipleDetailView, sealed class RailItem
- `UnityProject/Assets/Scripts/UI/Views/DiscipleListView.cs`: sealed class DiscipleListCard, class DiscipleListView
- `UnityProject/Assets/Scripts/UI/Views/EventPopupView.cs`: class EventChoiceViewData, class EventPopupView
- `UnityProject/Assets/Scripts/UI/Views/FitWidthGridLayoutGroup.cs`: sealed class FitWidthGridLayoutGroup
- `UnityProject/Assets/Scripts/UI/Views/LogWindowView.cs`: class LogWindowView
- `UnityProject/Assets/Scripts/UI/Views/PortraitOverrideBinding.cs`: sealed class PortraitOverrideBinding
- `UnityProject/Assets/Scripts/UI/Views/ResourcePopupView.cs`: sealed class ResourcePopupRow, class ResourcePopupView
- `UnityProject/Assets/Scripts/UI/Views/TaskAssignmentView.cs`: sealed class TaskAssignmentArgs, sealed class TaskAssignmentRowCell, class TaskAssignmentView
- `UnityProject/Assets/Scripts/UI/Views/UIViewBase.cs`: abstract class UIViewBase
- `UnityProject/Assets/Scripts/UI/Views/WalletHudView.cs`: class ResourceSlotBinding, class WalletHudView
- `UnityProject/Assets/Scripts/UI/Widgets/DiscipleModalScope.cs`: sealed class DiscipleModalScope
- `UnityProject/Assets/Scripts/UI/Widgets/InkTooltip.cs`: sealed class InkTooltip
- `UnityProject/Assets/Scripts/UI/Widgets/InkWidgets.cs`: static class InkWidgets
- `UnityProject/Assets/Scripts/UI/Widgets/RadarChartGraphic.cs`: class RadarChartGraphic
- `UnityProject/Assets/Scripts/UI/Widgets/UiPalette.cs`: static class UiPalette
- `UnityProject/Assets/Scripts/Visual/Core/AppearanceResolver.cs`: enum VisualBackend, struct ResolvedLayer, sealed class AppearanceResolver
- `UnityProject/Assets/Scripts/Visual/Core/AvatarIconCrop.cs`: static class AvatarIconCrop
- `UnityProject/Assets/Scripts/Visual/Core/DefaultEntitlementProvider.cs`: sealed class DefaultEntitlementProvider
- `UnityProject/Assets/Scripts/Visual/Core/DiscipleVisualSystem.cs`: sealed class DiscipleVisualSystem
- `UnityProject/Assets/Scripts/Visual/Core/EntitlementRandom.cs`: static class EntitlementRandom
- `UnityProject/Assets/Scripts/Visual/Core/IChibiVisual.cs`: interface IChibiVisual
- `UnityProject/Assets/Scripts/Visual/Core/IPortraitVisual.cs`: interface IPortraitVisual
- `UnityProject/Assets/Scripts/Visual/Core/IVisualEntitlementProvider.cs`: interface IVisualEntitlementProvider
- `UnityProject/Assets/Scripts/Visual/Core/PortraitOverrideMap.cs`: class PortraitOverrideMap, class Entry
- `UnityProject/Assets/Scripts/Visual/Core/TaskActivityMapper.cs`: sealed class TaskActivityMapper
- `UnityProject/Assets/Scripts/Visual/Core/VisualRuntimeConfig.cs`: sealed class VisualRuntimeConfig
- `UnityProject/Assets/Scripts/Visual/Core/VisualTierPolicy.cs`: sealed class VisualTierPolicy
- `UnityProject/Assets/Scripts/Visual/Scene/CameraFramingConfig.cs`: sealed class CameraFramingConfig
- `UnityProject/Assets/Scripts/Visual/Scene/CameraRigController.cs`: sealed class CameraRigController
- `UnityProject/Assets/Scripts/Visual/Scene/CameraRigPanBootstrap.cs`: static class CameraRigPanBootstrap
- `UnityProject/Assets/Scripts/Visual/Scene/CameraRigPorts.cs`: interface IRigMessageBus, interface IRigGhost, interface ICameraRigEnvironment, interface ICellSpriteMetrics, interface IMainThreadQueue, interface IRigClock, interface ICameraRigCameraView, static class CameraRigLogger, sealed class MessagePipeRigBus, sealed class SceneEnvironment, sealed class ResourcesCellSpriteMetrics, sealed class UniTaskMainThreadQueue, sealed class UnityRigClock, sealed class UnityCameraView, sealed class UnityGhostHandle
- `UnityProject/Assets/Scripts/Visual/Scene/ChibiClickTarget.cs`: sealed class ChibiClickTarget
- `UnityProject/Assets/Scripts/Visual/Scene/ChibiSceneRoot.cs`: class TaskAnchor, class ChibiSceneRoot
- `UnityProject/Assets/Scripts/Visual/Scene/GridOverlayRenderer.cs`: sealed class GridOverlayRenderer
- `UnityProject/Assets/Scripts/Visual/Scene/IsometricCellMath.cs`: static class IsometricCellMath
- `UnityProject/Assets/Scripts/Visual/Scene/RigPanInput.cs`: static class RigPanInput
- `UnityProject/Assets/Scripts/Visual/Scene/RigZoomInput.cs`: static class RigZoomInput
- `UnityProject/Assets/Scripts/Visual/Scene/TerrainBackdropRenderer.cs`: sealed class TerrainBackdropRenderer
- `UnityProject/Assets/Scripts/Visual/Spine/ChibiActivityMap.cs`: class ChibiActivityMap, class Entry
- `UnityProject/Assets/Scripts/Visual/Spine/DemoSpineRigMap.cs`: class DemoSpineRigMap, class SlotSkin, class PartSkin, class ActivityAnim
- `UnityProject/Assets/Scripts/Visual/Spine/SpineChibiVisual.cs`: sealed class SpineChibiVisual
- `UnityProject/Assets/Scripts/Visual/Spine/VisualOverrideMap.cs`: class VisualOverrideMap, class Entry, class ActivityAnim
- `UnityProject/Assets/Scripts/Visual/Spine/VisualSpineBootstrap.cs`: static class VisualSpineBootstrap
- `UnityProject/Assets/Scripts/Visual/Sprite/ChibiFrameBank.cs`: class ChibiAnimStateDef, class ChibiAnimTable, sealed class ChibiFrameBank
- `UnityProject/Assets/Scripts/Visual/Sprite/ChibiFrameClock.cs`: sealed class ChibiFrameClock, interface IChibiAnimated
- `UnityProject/Assets/Scripts/Visual/Sprite/SpriteChibiVisual.cs`: sealed class SpriteChibiVisual

## Selected Source Files

(แต่ละไฟล์จำกัด 40000 bytes; เลขนำหน้าคือเลขบรรทัดจริงของไฟล์)

### sync-shared.sh (751 bytes)
````
     1	#!/usr/bin/env bash
     2	# Copies the canonical source in Shared/ into both consumers.
     3	# Run this after editing anything in Shared/.
     4	#
     5	# Not a real shared package yet (no project reference / plugin DLL) - that's
     6	# a deliberate deferral while the schema is still moving fast. Revisit once
     7	# GameMessages.cs / SectEconomyState.cs stop changing every session.
     8	
     9	set -euo pipefail
    10	ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    11	
    12	UNITY_DEST="$ROOT/UnityProject/Assets/Scripts/Shared"
    13	BRIDGE_DEST="$ROOT/McpBridge/Shared"
    14	
    15	mkdir -p "$UNITY_DEST" "$BRIDGE_DEST"
    16	
    17	cp "$ROOT"/Shared/*.cs "$UNITY_DEST"/
    18	cp "$ROOT"/Shared/*.cs "$BRIDGE_DEST"/
    19	
    20	echo "Synced $(ls "$ROOT"/Shared/*.cs | wc -l | tr -d ' ') file(s) into:"
    21	echo "  $UNITY_DEST"
    22	echo "  $BRIDGE_DEST"
````

### Shared/GameMessages.cs (20183 bytes)
````
     1	using System;
     2	using System.Collections.Generic;
     3	using MessagePack;
     4	using Xianxia.Sect;
     5	
     6	namespace Xianxia.Sect.Messages
     7	{
     8	    // These are lightweight event/delta messages carried over MessagePipe.
     9	    // Deliberately separate from SectEconomyState (economy.proto): the proto
    10	    // state is a full snapshot used for MCP state queries, these are small
    11	    // deltas broadcast on every change so subscribers don't need to diff
    12	    // full state themselves.
    13	    //
    14	    // [MessagePackObject]/[Key] are required because MessagePipe.Interprocess
    15	    // serializes with MessagePack when a message crosses the TCP boundary to
    16	    // the MCP bridge process. In-memory-only messages don't strictly need
    17	    // this, but keeping it consistent means any message can be promoted to
    18	    // interprocess later without a rewrite.
    19	
    20	    [MessagePackObject]
    21	    public class DiscipleRecruitedMessage
    22	    {
    23	        [Key(0)] public string DiscipleId { get; set; }
    24	        [Key(1)] public string DisplayName { get; set; }
    25	    }
    26	
    27	    [MessagePackObject]
    28	    public class DiscipleRankChangedMessage
    29	    {
    30	        [Key(0)] public string DiscipleId { get; set; }
    31	        [Key(1)] public DiscipleRank NewRank { get; set; }
    32	    }
    33	
    34	    [MessagePackObject]
    35	    public class SectResourceChangedMessage
    36	    {
    37	        [Key(0)] public string ResourceId { get; set; }
    38	        [Key(1)] public int Delta { get; set; }
    39	        [Key(2)] public int NewTotal { get; set; }
    40	    }
    41	
    42	    [MessagePackObject]
    43	    public class ContributionEarnedMessage
    44	    {
    45	        [Key(0)] public string DiscipleId { get; set; }
    46	        [Key(1)] public long Amount { get; set; }
    47	        [Key(2)] public string Reason { get; set; }
    48	    }
    49	
    50	    // Scene lifecycle message (in-memory only — additive scenes don't cross TCP)
    51	    [MessagePackObject]
    52	    public class SceneLoadedMessage
    53	    {
    54	        [Key(0)] public string SceneName { get; set; }
    55	    }
    56	
    57	    // Raised on random world events. RequiresDecision marks the ones that
    58	    // should auto-pause the game and wait for the AI GM / vote window.
    59	    // In-memory bus only now (not registered on the interprocess broker) -
    60	    // the bridge learns about world events via AwaitWorldEventRequest/
    61	    // Response (request-response) instead, since IDistributedSubscriber
    62	    // can't be used from the bridge side (see the comment on
    63	    // AwaitWorldEventRequest below). Still useful in-process for e.g. a
    64	    // future Unity-side UI popup that wants to react to the same event.
    65	    [MessagePackObject]
    66	    public class WorldEventTriggeredMessage
    67	    {
    68	        [Key(0)] public string EventId { get; set; }
    69	        [Key(1)] public bool RequiresDecision { get; set; }
    70	        [Key(2)] public string Description { get; set; }
    71	        [Key(3)] public List<EventChoiceInfo> Choices { get; set; } = new List<EventChoiceInfo>();
    72	    }
    73	
    74	    // Internal-only (not registered on the interprocess bus) - the UI and
    75	    // other in-Unity systems care about this, the MCP bridge doesn't.
    76	    [MessagePackObject]
    77	    public class TimeSpeedChangedMessage
    78	    {
    79	        [Key(0)] public int Speed { get; set; }
    80	        [Key(1)] public bool Paused { get; set; }
    81	    }
    82	
    83	    // Request/response pair: the MCP bridge asks Unity for a full state
    84	    // snapshot on demand (e.g. when the AI GM calls the get_sect_state tool).
    85	    [MessagePackObject]
    86	    public class SectStateQuery
    87	    {
    88	        [Key(0)] public string RequestId { get; set; }
    89	    }
    90	
    91	    [MessagePackObject]
    92	    public class SectStateSnapshot
    93	    {
    94	        [Key(0)] public string RequestId { get; set; }
    95	        [Key(1)] public byte[] EconomyStateBytes { get; set; } // SectEconomyState, MessagePack-serialized
    96	    }
    97	
    98	    // Request/response pair for await_next_world_event.
    99	    //
   100	    // This is deliberately request-response, not pub/sub, even though
   101	    // conceptually it's "wait for the next event". Confirmed root cause:
   102	    // MessagePipe.Interprocess's TcpDistributedSubscriber unconditionally
   103	    // calls worker.StartReceiver() (binds its own listening socket)
   104	    // regardless of HostAsServer - so a non-hub process (the bridge) can
   105	    // never safely use IDistributedSubscriber over this TCP transport, it
   106	    // collides with the hub's (Unity's) already-bound port. Request-response
   107	    // only ever uses the client connection (Connect, not Listen), which is
   108	    // exactly what get_sect_state already proved works fine. The handler on
   109	    // the Unity side just holds the response open (via a UniTaskCompletionSource)
   110	    // until an event actually fires - same observed behavior as a
   111	    // subscription, without the broken transport.
   112	    [MessagePackObject]
   113	    public class AwaitWorldEventRequest
   114	    {
   115	        [Key(0)] public string RequestId { get; set; }
   116	    }
   117	
   118	    [MessagePackObject]
   119	    public class AwaitWorldEventResponse
   120	    {
   121	        [Key(0)] public string EventId { get; set; }
   122	        [Key(1)] public bool RequiresDecision { get; set; }
   123	        [Key(2)] public string Description { get; set; }
   124	        [Key(3)] public List<EventChoiceInfo> Choices { get; set; } = new List<EventChoiceInfo>();
   125	    }
   126	
   127	    // One selectable option for a world event - authored per-event in the
   128	    // EventData ScriptableObject, carried over the wire so the AI GM sees
   129	    // real choices instead of just a bare event id.
   130	    [MessagePackObject]
   131	    public class EventChoiceInfo
   132	    {
   133	        [Key(0)] public string ChoiceId { get; set; }
   134	        [Key(1)] public string Label { get; set; }
   135	    }
   136	
   137	    // Sent from the MCP bridge back into Unity when the AI GM (or a vote
   138	    // result) picks an option for the current world event. Completes the
   139	    // write path that SectActionTools.ExecuteDecision was a stub for.
   140	    [MessagePackObject]
   141	    public class ExecuteDecisionMessage
   142	    {
   143	        [Key(0)] public string EventId { get; set; }
   144	        [Key(1)] public string ChoiceId { get; set; }
   145	    }
   146	
   147	    // Published in-memory by DecisionExecutor (not sent over interprocess -
   148	    // this is the "it already happened" notification for local listeners
   149	    // like the in-game log window, distinct from ExecuteDecisionMessage
   150	    // which is the incoming command from the bridge). Whether the decision
   151	    // originated from the bridge or from clicking a choice in the UI,
   152	    // DecisionExecutor is the single place both paths funnel through, so
   153	    // this fires exactly once per decision regardless of source.
   154	    [MessagePackObject]
   155	    public class DecisionExecutedMessage
   156	    {
   157	        [Key(0)] public string EventId { get; set; }
   158	        [Key(1)] public string ChoiceId { get; set; }
   159	    }
   160	
   161	    // Internal-only: entering build (placement) mode. CameraRigController
   162	    // zooms to the Placement preset and GridOverlayRenderer shows the grid;
   163	    // Payload carries the ghost transform when one exists (may be null).
   164	    [MessagePackObject]
   165	    public class BuildModeStartedMessage
   166	    {
   167	        [Key(0)] public string SourceId { get; set; }
   168	        [Key(1)] public string GhostId { get; set; }
   169	    }
   170	
   171	    // Internal-only: leaving build mode (confirm, cancel, or system stop).
   172	    [MessagePackObject]
   173	    public class BuildModeEndedMessage
   174	    {
   175	        [Key(0)] public string SourceId { get; set; }
   176	        [Key(1)] public bool Confirmed { get; set; }
   177	    }
   178	
   179	    // Request/response pair: a disciple buying an item from the sect
   180	    // stockpile (CraftedGoods) with their own contribution. Request-response
   181	    // rather than a fire-and-forget message because the bridge needs to
   182	    // know immediately whether the purchase actually succeeded (enough
   183	    // contribution, enough stock) - same reasoning as SectStateQuery.
   184	    [MessagePackObject]
   185	    public class PurchaseItemRequest
   186	    {
   187	        [Key(0)] public string DiscipleId { get; set; }
   188	        [Key(1)] public string ItemDefId { get; set; }
   189	        [Key(2)] public int Grade { get; set; }
   190	        [Key(3)] public int Quantity { get; set; }
   191	    }
   192	
   193	    [MessagePackObject]
   194	    public class PurchaseItemResponse
   195	    {
   196	        [Key(0)] public bool Success { get; set; }
   197	        [Key(1)] public string Message { get; set; }
   198	        [Key(2)] public long RemainingContribution { get; set; }
   199	    }
   200	
   201	    // ---------- Building placed (Phase 1 — grid placement engine) ----------
   202	    // Published in-memory by SectStateProvider.TryPlaceBuilding after a
   203	    // successful commit. BuildingSystem listens and rebuilds grid occupancy +
   204	    // visuals. ⚠️ Deliberately NOT added to InterprocessTopics — building
   205	    // placement is player-only in Phase 1 (building-system.md §8 Q3 default),
   206	    // so the MCP bridge never needs it on the wire.
   207	    [MessagePackObject]
   208	    public class BuildingPlacedMessage
   209	    {
   210	        [Key(0)] public string InstanceId { get; set; } = string.Empty;
   211	        [Key(1)] public string DefId { get; set; } = string.Empty;
   212	        [Key(2)] public int GridX { get; set; }
   213	        [Key(3)] public int GridZ { get; set; }
   214	        [Key(4)] public int Rotation { get; set; }
   215	    }
   216	
   217	    // ---------- Chibi backend entitlement change (Phase 3) ----------
   218	    // Published in-memory by SectStateProvider.TrySetChibiBackend after mutating
   219	    // DiscipleState.ChibiBackend. DiscipleVisualSystem respawns the visual IN PLACE
   220	    // (same position/activity/facing) on this message.
   221	    // ⚠️ Deliberately NOT added to InterprocessTopics — visual-tier churn is
   222	    // irrelevant to the MCP bridge; keep it off the TCP broker (same rule as
   223	    // every DiscipleVisualSystem message).
   224	    [MessagePackObject]
   225	    public class DiscipleChibiBackendChangedMessage
   226	    {
   227	        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
   228	        [Key(1)] public ChibiBackend Old { get; set; }
   229	        [Key(2)] public ChibiBackend New { get; set; }
   230	    }
   231	
   232	    // ---------- Disciple selected (Phase 4 — chibi click) ----------
   233	    // Published in-memory by ChibiClickTarget when the player clicks a chibi
   234	    // in the gameplay scene. DiscipleDetailUISystem opens the DiscipleDetail
   235	    // panel on this message. ⚠️ Deliberately NOT added to InterprocessTopics —
   236	    // same in-memory-only rule as every DiscipleVisualSystem message.
   237	    [MessagePackObject]
   238	    public class DiscipleSelectedMessage
   239	    {
   240	        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
   241	    }
   242	
   243	    // ---------- Avatar equipment change ----------
   244	    // Broadcast after a successful avatar part change (pub/sub, goes to UI + MCP client)
   245	    [MessagePackObject]
   246	    public class AvatarEquipmentChangedMessage
   247	    {
   248	        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
   249	        [Key(1)] public string Slot { get; set; } = string.Empty;   // AvatarSlots.*
   250	        [Key(2)] public string OldPartId { get; set; } = string.Empty;
   251	        [Key(3)] public string NewPartId { get; set; } = string.Empty;
   252	    }
   253	
   254	    // Request/response pair for changing an avatar part.
   255	    // Request-response (not fire-and-forget) because the bridge needs
   256	    // to know immediately whether the PartId is valid or the disciple
   257	    // id is wrong - same reasoning as PurchaseItemRequest.
   258	    [MessagePackObject]
   259	    public class ChangeAvatarPartRequest
   260	    {
   261	        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
   262	        [Key(1)] public string Slot { get; set; } = string.Empty;
   263	        [Key(2)] public string PartId { get; set; } = string.Empty;   // "" = back to default
   264	    }
   265	
   266	    [MessagePackObject]
   267	    public class ChangeAvatarPartResponse
   268	    {
   269	        [Key(0)] public bool Success { get; set; }
   270	        [Key(1)] public string FailReason { get; set; } = string.Empty;
   271	        [Key(2)] public AvatarAppearance ResultAvatar { get; set; }
   272	    }
   273	
   274	    // ---------- Task assignment (task-system-v2.md §6) ----------
   275	    // Request/response pair: the MCP bridge (or AI GM) asks Unity to assign a
   276	    // task to a disciple. Request-response because the caller needs the
   277	    // permission/validity verdict immediately - same reasoning as
   278	    // PurchaseItemRequest. RequesterId drives the ownership check
   279	    // ("SECT_MASTER" may assign anyone; others only their own disciples).
   280	    [MessagePackObject]
   281	    public class AssignTaskRequest
   282	    {
   283	        [Key(0)] public string RequesterId { get; set; } = string.Empty;
   284	        [Key(1)] public string DiscipleId { get; set; } = string.Empty;
   285	        [Key(2)] public string TaskId { get; set; } = string.Empty;
   286	    }
   287	
   288	    [MessagePackObject]
   289	    public class AssignTaskResponse
   290	    {
   291	        [Key(0)] public bool Success { get; set; }
   292	        [Key(1)] public string FailReason { get; set; } = string.Empty;
   293	    }
   294	
   295	    // Published in-memory by SectStateProvider.TryAssignTask after a successful
   296	    // commit. TaskActivityMapper / visual systems react to the new task.
   297	    // ⚠️ Deliberately NOT added to InterprocessTopics — same in-memory-only
   298	    // rule as DecisionExecutedMessage / BuildingPlacedMessage.
   299	    [MessagePackObject]
   300	    public class DiscipleTaskChangedMessage
   301	    {
   302	        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
   303	        [Key(1)] public string TaskId { get; set; } = string.Empty;
   304	    }
   305	
   306	    // ---------- Disciple ownership change (P4 — local ownership test harness) ----------
   307	    // Published in-memory by SectStateProvider.TrySetDiscipleOwner after a real
   308	    // successful change (no-op when the values are already identical). The local
   309	    // dev harness is the only intended caller — there is no public viewer command.
   310	    // ⚠️ Deliberately NOT added to InterprocessTopics — ownership assignment is a
   311	    // development/debug concern until real account linking exists (P5B+).
   312	    // Observability: the MCP bridge can READ the change log via the
   313	    // OwnershipObservabilityQuery/Snapshot request-response pair below (same
   314	    // mechanism as SectStateQuery — IDistributedSubscriber is unusable from the
   315	    // bridge, see AwaitWorldEventRequest). The bridge can never WRITE ownership:
   316	    // no assignment request/response or tool exists on the wire.
   317	    [MessagePackObject]
   318	    public class DiscipleOwnerChangedMessage
   319	    {
   320	        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
   321	        [Key(1)] public DiscipleOwnerType OldType { get; set; }
   322	        [Key(2)] public string OldOwnerId { get; set; } = string.Empty;
   323	        [Key(3)] public DiscipleOwnerType NewType { get; set; }
   324	        [Key(4)] public string NewOwnerId { get; set; } = string.Empty;
   325	    }
   326	
   327	    // Request/response pair: the MCP bridge reads the disciples' ownership-change
   328	    // log (filled by OwnershipObservabilityBuffer, one entry per real change).
   329	    // Observability ONLY — deliberately no request/response pair or MCP tool that
   330	    // assigns ownership: the only writer stays the local dev harness / fixtures.
   331	    [MessagePackObject]
   332	    public class OwnershipObservabilityQuery
   333	    {
   334	        [Key(0)] public string RequestId { get; set; }
   335	    }
   336	
   337	    [MessagePackObject]
   338	    public class OwnershipObservabilitySnapshot
   339	    {
   340	        [Key(0)] public string RequestId { get; set; }
   341	        [Key(1)] public List<DiscipleOwnerChangedMessage> Events { get; set; } = new List<DiscipleOwnerChangedMessage>();
   342	    }
   343	
   344	    // ---------- P5B observability: task-change log + protection state (READ-ONLY) ----------
   345	    // Both pairs are pulled on demand by the MCP bridge (request-response, same
   346	    // mechanism as OwnershipObservabilityQuery — the bridge cannot
   347	    // IDistributedSubscriber over the TCP transport). Neither pair has a write
   348	    // path: task assignment keeps its own AssignTaskRequest/Response, and these
   349	    // are observation only. DiscipleTaskChangedMessage stays in-memory-only; the
   350	    // bridge pulls the buffered log instead.
   351	    [MessagePackObject]
   352	    public class TaskChangeObservabilityQuery
   353	    {
   354	        [Key(0)] public string RequestId { get; set; }
   355	    }
   356	
   357	    [MessagePackObject]
   358	    public class TaskChangeObservabilitySnapshot
   359	    {
   360	        [Key(0)] public string RequestId { get; set; }
   361	        [Key(1)] public List<DiscipleTaskChangedMessage> Events { get; set; } = new List<DiscipleTaskChangedMessage>();
   362	    }
   363	
   364	    [MessagePackObject]
   365	    public class TaskProtectionQuery
   366	    {
   367	        [Key(0)] public string RequestId { get; set; }
   368	    }
   369	
   370	    /// <summary>
   371	    /// One Viewer-owned disciple (the set the hybrid protection policy governs),
   372	    /// with the read-only answers the bridge needs: is the owner still protected,
   373	    /// how long is left, is the membership data consistent, and may the SectMaster
   374	    /// / the owner change its task right now (with the authority's reason).
   375	    /// Computed from ISectStateProvider.CheckTaskPermission — the same authority
   376	    /// TryAssignTask revalidates, so this view can never disagree with a commit.
   377	    /// </summary>
   378	    [MessagePackObject]
   379	    public class TaskProtectionEntry
   380	    {
   381	        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
   382	        [Key(1)] public string CurrentTask { get; set; } = string.Empty;
   383	        [Key(2)] public DiscipleOwnerType OwnerType { get; set; }
   384	        [Key(3)] public string OwnerId { get; set; } = string.Empty;
   385	        [Key(4)] public bool OwnerProtected { get; set; }
   386	        [Key(5)] public float OwnerProtectionRemainingSeconds { get; set; }
   387	        [Key(6)] public DateTime LastActiveAtUtc { get; set; }
   388	        [Key(7)] public bool MembershipConsistent { get; set; }
   389	        [Key(8)] public bool SectMasterMayChange { get; set; }
   390	        [Key(9)] public string SectMasterReason { get; set; } = string.Empty;
   391	        [Key(10)] public bool OwnerMayChange { get; set; }
   392	        [Key(11)] public string OwnerReason { get; set; } = string.Empty;
   393	    }
   394	
   395	    [MessagePackObject]
   396	    public class TaskProtectionSnapshot
   397	    {
   398	        [Key(0)] public string RequestId { get; set; }
   399	        [Key(1)] public List<TaskProtectionEntry> Entries { get; set; } = new List<TaskProtectionEntry>();
   400	    }
   401	
   402	    // ---------- P5B persistence: viewer membership slice (local disk, not on the wire) ----------
   403	    // Persisted together so the registry ⇄ disciple ownership pair can never be
   404	    // restored half-way (invariants #1/#2/#3 hold across sessions). Deliberately
   405	    // a SLICE, not the whole SectEconomyState: only membership + ownership +
   406	    // activity survive; economy/roster still come from MockSectData.
   407	    [MessagePackObject]
   408	    public class SectViewerMembershipSave
   409	    {
   410	        /// <summary>Only this shape is accepted; anything else is ignored on load (fail-closed to mock start).</summary>
   411	        public const int CurrentVersion = 1;
   412	
   413	        /// <summary>Bumped only if the shape changes incompatibly; a mismatch is ignored on load (fail-closed to mock start).</summary>
   414	        [Key(0)] public int Version { get; set; } = CurrentVersion;
   415	
   416	        /// <summary>When the file was written (UTC) — informational.</summary>
   417	        [Key(1)] public DateTime SavedAtUtc { get; set; }
   418	
   419	        [Key(2)] public List<ViewerRecord> Records { get; set; } = new List<ViewerRecord>();
   420	        [Key(3)] public List<PendingViewerApplication> PendingApplications { get; set; } = new List<PendingViewerApplication>();
   421	
   422	        /// <summary>discipleId → owner, captured in the SAME snapshot so ownership and registry agree on load.</summary>
   423	        [Key(4)] public List<SectSavedOwnership> OwnerByDisciple { get; set; } = new List<SectSavedOwnership>();
   424	    }
   425	
   426	    [MessagePackObject]
   427	    public class SectSavedOwnership
   428	    {
   429	        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
   430	        [Key(1)] public DiscipleOwnerType OwnerType { get; set; }
   431	        [Key(2)] public string OwnerId { get; set; } = string.Empty;
   432	    }
   433	
   434	    // Topic keys for the keyed (IDistributedPublisher<TKey,TMessage>) channels.
   435	    public static class InterprocessTopics
   436	    {
   437	        public const string DiscipleRecruited = "sect.disciple_recruited";
   438	        public const string DiscipleRankChanged = "sect.disciple_rank_changed";
   439	        public const string ResourceChanged = "sect.resource_changed";
   440	        public const string ContributionEarned = "sect.contribution_earned";
   441	        public const string WorldEvent = "sect.world_event";
   442	        public const string ExecuteDecision = "sect.execute_decision";
   443	        public const string AvatarEquipmentChanged = "sect.avatar_equipment_changed";
   444	    }
   445	}
````

### Shared/SectEconomyState.cs (10506 bytes)
````
     1	// Plain C# mirror of economy.proto.
     2	// Use this to wire up UI and gameplay logic right away.
     3	using System;
     4	using System.Collections.Generic;
     5	using MessagePack;
     6	
     7	namespace Xianxia.Sect
     8	{
     9	    public enum OwnerScope { Unspecified, Personal, SectStockpile }
    10	    public enum DiscipleRank { Unspecified, OuterDisciple, InnerDisciple, Elder, SectMaster }
    11	    public enum DiscipleSex { Unspecified, Male, Female }
    12	
    13	    /// <summary>
    14	    /// Who a disciple belongs to (task-system-v2.md §1.1). Default Npc so old
    15	    /// saves/rosters without [Key(9)] deserialize as non-player-owned (L5).
    16	    /// </summary>
    17	    public enum DiscipleOwnerType { Npc, Player, Viewer }
    18	
    19	    /// <summary>
    20	    /// Entitlement ของศิษย์: "ได้สิทธิ์" แสดง chibi ในฉากด้วย backend ไหน — ไม่ใช่สิ่งที่ render จริงเสมอไป
    21	    /// (runtime อาจ degrade Spine → SpriteSheet เมื่อเกิน SpineBudget โดยไม่แก้ค่านี้ — ดู §7 ของแผน)
    22	    /// 0 = default → save/roster เก่าที่ไม่มี [Key(8)] deserialize ได้ SpriteSheet (L5)
    23	    /// </summary>
    24	    public enum ChibiBackend
    25	    {
    26	        SpriteSheet = 0,
    27	        Spine = 1
    28	    }
    29	
    30	    [MessagePackObject]
    31	    public class CurrencyWallet
    32	    {
    33	        [Key(0)] public long SpiritStones { get; set; }
    34	        [Key(1)] public long Contribution { get; set; }
    35	    }
    36	
    37	    [MessagePackObject]
    38	    public class InventoryItem
    39	    {
    40	        [Key(0)] public string ItemDefId { get; set; }
    41	        [Key(1)] public int Quantity { get; set; }
    42	        [Key(2)] public int Grade { get; set; }          // 1-5
    43	        [Key(3)] public OwnerScope OwnerScope { get; set; }
    44	    }
    45	
    46	    [MessagePackObject]
    47	    public class DiscipleState
    48	    {
    49	        [Key(0)] public string DiscipleId { get; set; }
    50	        [Key(1)] public string DisplayName { get; set; }
    51	        [Key(2)] public DiscipleRank Rank { get; set; }
    52	        [Key(3)] public CurrencyWallet Wallet { get; set; } = new CurrencyWallet();
    53	        [Key(4)] public List<InventoryItem> PersonalInventory { get; set; } = new List<InventoryItem>();
    54	        [Key(5)] public string CurrentTask { get; set; }
    55	        [Key(6)] public AvatarAppearance Avatar { get; set; } = new AvatarAppearance();
    56	        [Key(7)] public DiscipleSex Sex { get; set; } = DiscipleSex.Unspecified;
    57	        /// <summary>Entitlement (แกนแยกจาก AvatarAppearance) — default SpriteSheet เมื่อ deserialize state เก่า (L1/L5)</summary>
    58	        [Key(8)] public ChibiBackend ChibiBackend { get; set; } = ChibiBackend.SpriteSheet;
    59	
    60	        /// <summary>Ownership kind (task-system-v2.md §1.1) — append-only; old state default Npc</summary>
    61	        [Key(9)] public DiscipleOwnerType OwnerType { get; set; } = DiscipleOwnerType.Npc;
    62	        /// <summary>Owner identity (player/viewer id); empty for Npc-owned</summary>
    63	        [Key(10)] public string OwnerId { get; set; } = string.Empty;
    64	    }
    65	
    66	    [MessagePackObject]
    67	    public class SectStockpile
    68	    {
    69	        [Key(0)] public Dictionary<string, int> RawResources { get; set; } = new Dictionary<string, int>();
    70	        [Key(1)] public List<InventoryItem> CraftedGoods { get; set; } = new List<InventoryItem>();
    71	    }
    72	
    73	    /// <summary>
    74	    /// อาคาร 1 หลังที่วางบน grid สำนักแล้ว (building-system.md §3.3)
    75	    /// GridX/GridZ = มุมบนซ้าย (cell แรก) ของ footprint; Rotation = 0/90/180/270
    76	    /// (90/270 ทำให้ footprint กว้าง×สูงสลับกัน — ดู BuildingGrid.ResolveFootprint)
    77	    /// </summary>
    78	    [MessagePackObject]
    79	    public class PlacedBuildingState
    80	    {
    81	        [Key(0)] public string InstanceId { get; set; }
    82	        [Key(1)] public string DefId { get; set; }
    83	        [Key(2)] public int GridX { get; set; }
    84	        [Key(3)] public int GridZ { get; set; }
    85	        [Key(4)] public int Rotation { get; set; }
    86	    }
    87	
    88	    [MessagePackObject]
    89	    public class SectEconomyState
    90	    {
    91	        [Key(0)] public List<DiscipleState> Disciples { get; set; } = new List<DiscipleState>();
    92	        [Key(1)] public SectStockpile Stockpile { get; set; } = new SectStockpile();
    93	        /// <summary>Append-only key (building-system.md §3.3) — ห้ามแก้ [Key(0)]/[Key(1)] เดิม</summary>
    94	        [Key(2)] public List<PlacedBuildingState> PlacedBuildings { get; set; } = new List<PlacedBuildingState>();
    95	        /// <summary>
    96	        /// P5A append-only key — สมุดทะเบียนสมาชิกผู้ชม + คำขอรออนุมัติ (แยก list ตาม P5A §4).
    97	        /// Old saves without [Key(3)] deserialize as empty registry (fail-closed: ไม่มีสมาชิกจนกว่าจะมี mutation).
    98	        /// ⚠️ ยังไม่มี save/load mechanism ใน repo (open-questions §14) — contract เท่านั้น,
    99	        /// cross-session reclaim ยังใช้ไม่ได้จนกว่าจะมี persistence จริง
   100	        /// </summary>
   101	        [Key(3)] public SectViewerRegistry ViewerRegistry { get; set; } = new SectViewerRegistry();
   102	
   103	        public byte[] ToByteArray() => MessagePackSerializer.Serialize(this);
   104	        public static SectEconomyState FromByteArray(byte[] bytes) =>
   105	            MessagePackSerializer.Deserialize<SectEconomyState>(bytes);
   106	    }
   107	
   108	    // --- Avatar System (New Dictionary-Based Schema) ---
   109	
   110	    /// <summary>
   111	    /// Struct สำหรับ Factory Method FromSlots (เลี่ยง Value Tuple เพื่อความเข้ากันได้กับ Unity ทุกเวอร์ชัน)
   112	    /// </summary>
   113	    public struct SlotPart
   114	    {
   115	        public string Slot;
   116	        public string PartId;
   117	        public SlotPart(string slot, string partId)
   118	        {
   119	            Slot = slot;
   120	            PartId = partId;
   121	        }
   122	    }
   123	
   124	    /// <summary>
   125	    /// หน้าตาของตัวละคร — dictionary-based รองรับ slot จำนวน任意
   126	    /// Parts: slot → partId (ว่าง = ใช้ default ของ slot นั้น)
   127	    /// Colors: slot → colorId (มีเฉพาะ slot ที่ tintable)
   128	    /// </summary>
   129	    [MessagePackObject]
   130	    public sealed class AvatarAppearance
   131	    {
   132	        [Key(0)] public Dictionary<string, string> Parts { get; set; } = new Dictionary<string, string>();
   133	        [Key(1)] public Dictionary<string, string> Colors { get; set; } = new Dictionary<string, string>();
   134	        [Key(2)] public string PoseId { get; set; } = string.Empty;
   135	
   136	        public string GetSlot(string slot)
   137	        {
   138	            string v;
   139	            return Parts.TryGetValue(slot, out v) ? v : string.Empty;
   140	        }
   141	
   142	        public bool SetSlot(string slot, string partId)
   143	        {
   144	            if (string.IsNullOrEmpty(partId)) { Parts.Remove(slot); return true; }
   145	            Parts[slot] = partId;
   146	            return true;
   147	        }
   148	
   149	        public string GetColor(string slot)
   150	        {
   151	            string v;
   152	            return Colors.TryGetValue(slot, out v) ? v : string.Empty;
   153	        }
   154	
   155	        public void SetColor(string slot, string colorId)
   156	        {
   157	            if (string.IsNullOrEmpty(colorId)) { Colors.Remove(slot); return; }
   158	            Colors[slot] = colorId;
   159	        }
   160	
   161	        public AvatarAppearance Clone()
   162	        {
   163	            var c = new AvatarAppearance();
   164	            c.Parts = new Dictionary<string, string>(Parts);
   165	            c.Colors = new Dictionary<string, string>(Colors);
   166	            c.PoseId = PoseId;
   167	            return c;
   168	        }
   169	
   170	        [IgnoreMember]
   171	        /// <summary>Effective pose: PoseId if set, else fallback to "pose_idle_01".</summary>
   172	        public string EffectivePose
   173	        {
   174	            get { return string.IsNullOrEmpty(PoseId) ? "pose_idle_01" : PoseId; }
   175	        }
   176	
   177	        /// <summary>Factory สำหรับสร้าง AvatarAppearance จาก list — ใช้ใน MockData</summary>
   178	        public static AvatarAppearance FromSlots(params SlotPart[] slots)
   179	        {
   180	            var a = new AvatarAppearance();
   181	            for (int i = 0; i < slots.Length; i++)
   182	            {
   183	                if (!string.IsNullOrEmpty(slots[i].PartId))
   184	                    a.Parts[slots[i].Slot] = slots[i].PartId;
   185	            }
   186	            return a;
   187	        }
   188	    }
   189	
   190	    /// <summary>ชื่อ slot แบบ const string + Categories สำหรับ UI</summary>
   191	    public static class AvatarSlots
   192	    {
   193	        public const string Base = "base";
   194	        public const string Body = "body";
   195	        public const string Head = "head";
   196	        public const string Eyes = "eyes";
   197	        public const string Brows = "brows";
   198	        public const string Mouth = "mouth";
   199	        public const string Nose = "nose";
   200	        public const string Hair = "hair";
   201	        public const string FaceMarking = "face_marking";
   202	        public const string Eyeshadow = "eyeshadow";
   203	        public const string Accessory = "accessory";
   204	
   205	        public static readonly string[] Equippable =
   206	        {
   207	            Body, Head, Eyes, Brows, Mouth, Nose,
   208	            Hair, FaceMarking, Eyeshadow, Accessory
   209	        };
   210	
   211	        public static readonly IReadOnlyDictionary<string, string[]> Categories =
   212	            new Dictionary<string, string[]>
   213	            {
   214	                { "ใบหน้า", new[] { Head, Eyes, Brows, Mouth, Nose } }, // Head รวมอยู่ในใบหน้าด้วย
   215	                { "ลักษณะ", new[] { Hair, FaceMarking, Eyeshadow } },
   216	                { "ร่างกาย", new[] { Body, Accessory } },
   217	            };
   218	
   219	        public static readonly IReadOnlyDictionary<string, string> Labels =
   220	            new Dictionary<string, string>
   221	            {
   222	                { Body,       "เสื้อผ้า" },
   223	                { Head,       "ใบหน้า" },
   224	                { Eyes,       "ตา" },
   225	                { Brows,      "คิ้ว" },
   226	                { Mouth,      "ปาก" },
   227	                { Nose,       "จมูก" },
   228	                { Hair,       "ทรงผม" },
   229	                { FaceMarking,"ลายหน้า" },
   230	                { Eyeshadow,  "อายแชโดว์" },
   231	                { Accessory,  "เครื่องประดับ" },
   232	            };
   233	    }
   234	}````

### Shared/MockSectData.cs (5765 bytes)
````
     1	// Sample SectEconomyState for wiring up UI before real gameplay
     2	// systems (resource gathering, crafting, quests) are implemented.
     3	using System.Collections.Generic;
     4	namespace Xianxia.Sect
     5	{
     6	    public static class MockSectData
     7	    {
     8	        public static SectEconomyState Create()
     9	        {
    10	            var state = new SectEconomyState();
    11	            
    12	            // --- Stockpile ---
    13	            state.Stockpile.RawResources["herb"] = 120;
    14	            state.Stockpile.RawResources["wood"] = 340;
    15	            state.Stockpile.RawResources["ore"] = 88;
    16	            state.Stockpile.RawResources["provisions"] = 260;
    17	            
    18	            state.Stockpile.CraftedGoods.Add(new InventoryItem
    19	            {
    20	                ItemDefId = "elixir_qi_gathering",
    21	                Quantity = 4,
    22	                Grade = 3,
    23	                OwnerScope = OwnerScope.SectStockpile
    24	            });
    25	
    26	            // --- Placed buildings ---
    27	            // None. The start state carries NO buildings: the player sees an empty
    28	            // sect and must build everything themselves. A start-state herb_plot
    29	            // used to be placed here for the task building-requirement gate, but it
    30	            // was invisible on screen, so state disagreed with what the player saw.
    31	
    32	            // --- Disciples (Updated for New Avatar System with SlotPart struct) ---
    33	            
    34	            // d000: Sect Master
    35	            state.Disciples.Add(new DiscipleState
    36	            {
    37	                DiscipleId = "d000",
    38	                DisplayName = "Liu YiFeng",
    39	                Sex = DiscipleSex.Male,
    40	                ChibiBackend = ChibiBackend.Spine, // SectMaster = Spine tier (แผน §4.1)
    41	                Rank = DiscipleRank.SectMaster,
    42	                Wallet = new CurrencyWallet { SpiritStones = 1200, Contribution = 3400 },
    43	                CurrentTask = "meditation",
    44	                PersonalInventory = new List<InventoryItem>(),
    45	                Avatar = AvatarAppearance.FromSlots(
    46	                    new SlotPart(AvatarSlots.Body, "body_robe_azure"),
    47	                    new SlotPart(AvatarSlots.Head, "head_male_01"),
    48	                    new SlotPart(AvatarSlots.Hair, "hair_topknot_long"),
    49	                    new SlotPart(AvatarSlots.Accessory, "acc_jade_crown")
    50	                )
    51	            });
    52	
    53	            // d001: Lin Feng (Outer Disciple)
    54	            state.Disciples.Add(new DiscipleState
    55	            {
    56	                DiscipleId = "d001",
    57	                DisplayName = "Lin Feng",
    58	                Sex = DiscipleSex.Female,
    59	                ChibiBackend = ChibiBackend.SpriteSheet, // Outer = SpriteSheet (แผน §4.1)
    60	                Rank = DiscipleRank.OuterDisciple,
    61	                Wallet = new CurrencyWallet { SpiritStones = 12, Contribution = 340 },
    62	                CurrentTask = "meditation", // was gathering_herb — herb_plot is no longer in start state, gate would block it anyway
    63	                PersonalInventory = new List<InventoryItem>(),
    64	                Avatar = AvatarAppearance.FromSlots(
    65	                    new SlotPart(AvatarSlots.Body, "body_robe_grey"),
    66	                    new SlotPart(AvatarSlots.Head, "head_male_01"),
    67	                    new SlotPart(AvatarSlots.Hair, "hair_short")
    68	                )
    69	            });
    70	
    71	            // d002: Su Yan (Inner Disciple - With face marking + twin tail)
    72	            state.Disciples.Add(new DiscipleState
    73	            {
    74	                DiscipleId = "d002",
    75	                DisplayName = "Su Yan",
    76	                Sex = DiscipleSex.Female,
    77	                ChibiBackend = ChibiBackend.SpriteSheet, // Inner = SpriteSheet (แผน §4.1)
    78	                Rank = DiscipleRank.InnerDisciple,
    79	                Wallet = new CurrencyWallet { SpiritStones = 45, Contribution = 1120 },
    80	                CurrentTask = "meditation", // was refining_elixir — pill_hall not in start state, gate would block it anyway
    81	                PersonalInventory = new List<InventoryItem>(),
    82	                Avatar = AvatarAppearance.FromSlots(
    83	                    new SlotPart(AvatarSlots.Body, "body_robe_white"),
    84	                    new SlotPart(AvatarSlots.Head, "head_female_01"),
    85	                    new SlotPart(AvatarSlots.Hair, "hair_twin_tail"),
    86	                    new SlotPart(AvatarSlots.FaceMarking, "face_marking_red_dot"),
    87	                    new SlotPart(AvatarSlots.Accessory, "acc_hairpin_silver")
    88	                )
    89	            });
    90	
    91	            // d003: Elder Zhao (Elder - Bald + Beard)
    92	            state.Disciples.Add(new DiscipleState
    93	            {
    94	                DiscipleId = "d003",
    95	                DisplayName = "Elder Zhao",
    96	                Sex = DiscipleSex.Male,
    97	                ChibiBackend = ChibiBackend.Spine, // Elder = Spine tier (แผน §4.1)
    98	                Rank = DiscipleRank.Elder,
    99	                Wallet = new CurrencyWallet { SpiritStones = 210, Contribution = 4300 },
   100	                CurrentTask = "meditation", // was forging_artifact — forge not in start state, gate would block it anyway
   101	                PersonalInventory = new List<InventoryItem>
   102	                {
   103	                    new InventoryItem
   104	                    {
   105	                        ItemDefId = "sword_azure_flame",
   106	                        Quantity = 1,
   107	                        Grade = 5,
   108	                        OwnerScope = OwnerScope.Personal
   109	                    }
   110	                },
   111	                Avatar = AvatarAppearance.FromSlots(
   112	                    new SlotPart(AvatarSlots.Body, "body_robe_black"),
   113	                    new SlotPart(AvatarSlots.Head, "head_male_elder"),
   114	                    new SlotPart(AvatarSlots.Hair, "hair_bald_beard"),
   115	                    new SlotPart(AvatarSlots.Accessory, "acc_gourd")
   116	                )
   117	            });
   118	
   119	            return state;
   120	        }
   121	    }
   122	}````

### McpBridge/Program.cs (8721 bytes)
````
     1	// MCP bridge process (external, matches the "MCP bridge process" box in the
     2	// architecture diagram). Runs as its own .NET console app, separate from
     3	// Unity. Written in C# specifically so it can use MessagePipe.Interprocess
     4	// directly instead of re-implementing MessagePipe's wire protocol in
     5	// Node/Python - the tradeoff flagged earlier is resolved by keeping both
     6	// sides on .NET, same as Wanxiang.Prelude's Frontend/Backend split.
     7	//
     8	// Talks to Unity over MessagePipe.Interprocess (TCP, localhost).
     9	// Talks to the AI GM client (e.g. Open-LLM-VTuber) over MCP via stdio,
    10	// using the official ModelContextProtocol C# SDK.
    11	//
    12	// NuGet packages needed: MessagePipe, MessagePipe.Interprocess,
    13	// ModelContextProtocol, Microsoft.Extensions.Hosting
    14	
    15	using System.ComponentModel;
    16	using System.Text.Json;
    17	using MessagePipe;
    18	using Microsoft.Extensions.DependencyInjection;
    19	using Microsoft.Extensions.Hosting;
    20	using Microsoft.Extensions.Logging;
    21	using ModelContextProtocol.Server;
    22	using Xianxia.Sect;
    23	using Xianxia.Sect.Messages;
    24	
    25	var builder = Host.CreateApplicationBuilder(args);
    26	
    27	// MCP over stdio uses stdout for protocol messages - logs must go to stderr.
    28	builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
    29	
    30	Console.Error.WriteLine("[mcp-bridge] starting - if you're running this " +
    31	    "directly with `dotnet run`, this is expected to look idle after this " +
    32	    "line: stdio transport just waits for an MCP client (e.g. MCP " +
    33	    "Inspector) to talk to it over stdin, it won't print anything else on " +
    34	    "its own until a client connects and calls a tool.");
    35	
    36	// --- connect into Unity's MessagePipe bus as a TCP client ---
    37	// AddMessagePipe() on IServiceCollection returns IMessagePipeBuilder
    38	// directly (unlike VContainer's IContainerBuilder, which needs an extra
    39	// ToMessagePipeBuilder() conversion step) - AddTcpInterprocess() hangs off
    40	// that builder, not off IServiceCollection itself.
    41	var messagePipeBuilder = builder.Services.AddMessagePipe();
    42	messagePipeBuilder.AddTcpInterprocess("127.0.0.1", 3215, options =>
    43	{
    44	    options.HostAsServer = false; // Unity hosts the endpoint; we connect as a client
    45	});
    46	
    47	// --- expose MCP tools to the AI GM client ---
    48	builder.Services
    49	    .AddMcpServer()
    50	    .WithStdioServerTransport()
    51	    .WithToolsFromAssembly();
    52	
    53	var app = builder.Build();
    54	await app.RunAsync();
    55	
    56	// Read (query) tools - map 1:1 to what was sketched in the architecture
    57	// discussion (get_sect_state, get_current_event, ...).
    58	[McpServerToolType]
    59	public static class SectQueryTools
    60	{
    61	    [McpServerTool, Description("Get the current sect state: disciples, wallets, stockpile.")]
    62	    public static async Task<string> GetSectState(
    63	        IRemoteRequestHandler<SectStateQuery, SectStateSnapshot> requestHandler)
    64	    {
    65	        var snapshot = await requestHandler.InvokeAsync(
    66	            new SectStateQuery { RequestId = Guid.NewGuid().ToString() });
    67	
    68	        // MessagePack stub decode - swap for the generated protobuf parser
    69	        // once economy.proto is compiled for real.
    70	        var state = SectEconomyState.FromByteArray(snapshot.EconomyStateBytes);
    71	        return JsonSerializer.Serialize(state);
    72	    }
    73	
    74	    [McpServerTool, Description("Block until the next world event that requires a GM decision.")]
    75	    public static async Task<string> AwaitNextWorldEvent(
    76	        IRemoteRequestHandler<AwaitWorldEventRequest, AwaitWorldEventResponse> requestHandler)
    77	    {
    78	        // Request-response, not subscribe - IDistributedSubscriber over this
    79	        // TCP transport always opens its own listen socket regardless of
    80	        // HostAsServer, which collides with Unity's already-bound port when
    81	        // called from a non-hub process like this bridge. Request-response
    82	        // only ever connects out (no listen), same mechanism get_sect_state
    83	        // already uses successfully.
    84	        var response = await requestHandler.InvokeAsync(
    85	            new AwaitWorldEventRequest { RequestId = Guid.NewGuid().ToString() });
    86	
    87	        // Full response now (description + choices), not just the bare
    88	        // event id - EventData ScriptableObjects actually author this
    89	        // content now instead of it being hardcoded/absent.
    90	        return JsonSerializer.Serialize(response);
    91	    }
    92	
    93	    [McpServerTool, Description("Read the sect's ownership-change log (P4 observability: one entry per real disciple ownership change, oldest first). STRICTLY read-only - there is deliberately no tool that assigns ownership; assignment stays a local dev-harness operation.")]
    94	    public static async Task<string> GetOwnershipLog(
    95	        IRemoteRequestHandler<OwnershipObservabilityQuery, OwnershipObservabilitySnapshot> requestHandler)
    96	    {
    97	        var response = await requestHandler.InvokeAsync(
    98	            new OwnershipObservabilityQuery { RequestId = Guid.NewGuid().ToString() });
    99	
   100	        return JsonSerializer.Serialize(response);
   101	    }
   102	
   103	    [McpServerTool, Description("Read the sect's task-change log (P5B observability: one entry per real disciple task change, oldest first; no-ops and rejections are not logged). STRICTLY read-only.")]
   104	    public static async Task<string> GetTaskChangeLog(
   105	        IRemoteRequestHandler<TaskChangeObservabilityQuery, TaskChangeObservabilitySnapshot> requestHandler)
   106	    {
   107	        var response = await requestHandler.InvokeAsync(
   108	            new TaskChangeObservabilityQuery { RequestId = Guid.NewGuid().ToString() });
   109	
   110	        return JsonSerializer.Serialize(response);
   111	    }
   112	
   113	    [McpServerTool, Description("Read the current viewer-protection state (P5B, STRICTLY read-only): one entry per viewer-owned disciple with whether the owner is still protected, the remaining protection time, whether the membership data is consistent, and whether the sect master / the owner may change its task right now (with the authority's reason).")]
   114	    public static async Task<string> GetTaskProtection(
   115	        IRemoteRequestHandler<TaskProtectionQuery, TaskProtectionSnapshot> requestHandler)
   116	    {
   117	        var response = await requestHandler.InvokeAsync(
   118	            new TaskProtectionQuery { RequestId = Guid.NewGuid().ToString() });
   119	
   120	        return JsonSerializer.Serialize(response);
   121	    }
   122	}
   123	
   124	// Write (execute) tools go in a separate type on purpose - keeps the
   125	// read/write split visible at a glance in the tool list, matching the
   126	// separation decided earlier (query tools vs execute tools).
   127	[McpServerToolType]
   128	public static class SectActionTools
   129	{
   130	    [McpServerTool, Description("Execute a decision for the current world event.")]
   131	    public static async Task<string> ExecuteDecision(
   132	        IDistributedPublisher<string, ExecuteDecisionMessage> publisher,
   133	        [Description("Event id from await_next_world_event")] string eventId,
   134	        [Description("Chosen option id")] string choiceId)
   135	    {
   136	        await publisher.PublishAsync(
   137	            InterprocessTopics.ExecuteDecision,
   138	            new ExecuteDecisionMessage { EventId = eventId, ChoiceId = choiceId });
   139	
   140	        return $"Decision sent to Unity: event={eventId} choice={choiceId}";
   141	    }
   142	
   143	    [McpServerTool, Description("Have a disciple buy an item from the sect stockpile using their contribution.")]
   144	    public static async Task<string> PurchaseItem(
   145	        IRemoteRequestHandler<PurchaseItemRequest, PurchaseItemResponse> requestHandler,
   146	        [Description("Disciple id, e.g. from get_sect_state")] string discipleId,
   147	        [Description("Item id, e.g. elixir_qi_gathering")] string itemDefId,
   148	        [Description("Item grade, from the stockpile entry in get_sect_state")] int grade,
   149	        [Description("How many to buy")] int quantity)
   150	    {
   151	        var response = await requestHandler.InvokeAsync(new PurchaseItemRequest
   152	        {
   153	            DiscipleId = discipleId,
   154	            ItemDefId = itemDefId,
   155	            Grade = grade,
   156	            Quantity = quantity,
   157	        });
   158	
   159	        return response.Message;
   160	    }
   161	
   162	    [McpServerTool, Description("Assign a task to a disciple. Always sent as SECT_MASTER (the AI GM acts as the sect master, so it may assign any disciple).")]
   163	    public static async Task<string> AssignTask(
   164	        IRemoteRequestHandler<AssignTaskRequest, AssignTaskResponse> requestHandler,
   165	        [Description("Disciple id, e.g. from get_sect_state")] string discipleId,
   166	        [Description("Task id: a gathering or crafting task id, or \"meditation\"")] string taskId)
   167	    {
   168	        var response = await requestHandler.InvokeAsync(new AssignTaskRequest
   169	        {
   170	            RequesterId = "SECT_MASTER",
   171	            DiscipleId = discipleId,
   172	            TaskId = taskId,
   173	        });
   174	
   175	        return response.Success
   176	            ? $"Task '{taskId}' assigned to {discipleId}."
   177	            : response.FailReason;
   178	    }
   179	}
````

### UnityProject/Assets/Scripts/Core/GameLifetimeScope.cs (3896 bytes)
````
     1	using UnityEngine;
     2	using VContainer;
     3	using VContainer.Unity;
     4	using Xianxia.Sect.Installers;
     5	using Xianxia.Sect.Tests;
     6	
     7	namespace Xianxia.Sect
     8	{
     9	    // Composition root for the whole game framework (see the architecture
    10	    // diagram: this is the "Game manager · VContainer composition root" box).
    11	    // Registers the internal MessagePipe bus, the MessagePipe.Interprocess
    12	    // TCP transport to the external MCP bridge process, and the four
    13	    // gameplay subsystems as VContainer entry points.
    14	    //
    15	    // PHASE 1 refactor: ตัว registration ถูกแตกเป็น static installer extensions
    16	    // ใน Core/Installers/ (Building/Visual/UI/Interprocess/Gameplay) — ไฟล์นี้
    17	    // เหลือหน้าที่ Injector/Awake/OnDestroy + ลำดับการเรียก installer.
    18	    // ⚠️ ลำดับการเรียกต้องตรงกับลำดับ registration เดิมทุกบรรทัด เพราะ VContainer
    19	    // รัน entry point ตามลำดับ registration (BuildingPlacementUISystem ก่อน
    20	    // ChibiFrameClock ตามเดิม เป็นต้น)
    21	    public class GameLifetimeScope : LifetimeScope
    22	    {
    23	        /// <summary>
    24	        /// Static access point to this scope's container (Editor verify tools only).
    25	        /// Set in Awake, cleared in OnDestroy — never used by production gameplay code
    26	        /// (those get constructor injection). Kept here instead of reflection so tools
    27	        /// stay reflection-free (C12).
    28	        /// </summary>
    29	        public static IObjectResolver Injector { get; private set; }
    30	
    31	        [SerializeField] private string interprocessHost = "127.0.0.1";
    32	        [SerializeField] private int interprocessPort = 3215;
    33	
    34	        [Header("UI (Xianxia.UI.MVP Lite)")]
    35	        [SerializeField] private Xianxia.Sect.UI.UIRoot uiRoot;
    36	        [SerializeField] private Xianxia.Sect.UI.UIPanelCatalog uiPanelCatalog;
    37	
    38	        protected override void Awake()
    39	        {
    40	            base.Awake();
    41	            Injector = Container;
    42	            DontDestroyOnLoad(gameObject);
    43	        }
    44	
    45	        protected override void OnDestroy()
    46	        {
    47	            Injector = null;
    48	            base.OnDestroy();
    49	        }
    50	
    51	        protected override void Configure(IContainerBuilder builder)
    52	        {
    53	            // Scene management
    54	            // Entry point = โหลดฉากเกมเพลย์เริ่มต้น (SectScene) อัตโนมัติ
    55	            // ทันทีที่ composition root พร้อม — .AsSelf() ให้ยัง inject เป็น concrete ได้
    56	            builder.RegisterEntryPoint<SceneLoader>(Lifetime.Singleton).AsSelf();
    57	            builder.RegisterComponentInHierarchy<AdditiveSceneTest>();
    58	            // Design-time data now sourced from Luban (see DataTables/ at
    59	            // the workspace root and Assets/Scripts/Data/LubanEventPool.cs),
    60	            // not a ScriptableObject dragged into the Inspector - so this is
    61	            // constructed directly rather than serialized.
    62	            builder.RegisterInstance(new LubanEventPool());
    63	
    64	            // Avatar part definitions loaded from Resources/Data/avatar_parts.json
    65	            builder.Register<AvatarPartPool>(Lifetime.Singleton);
    66	
    67	            // ลำดับด้านล่าง = ลำดับ registration เดิมทุกบรรทัด (Building → Visual → UI →
    68	            // interprocess → rig ports/gameplay entries) — ห้ามสลับ (ดูหัวไฟล์)
    69	            builder.RegisterBuildingSystem();
    70	            builder.RegisterVisualSystem();
    71	            builder.RegisterUI(uiRoot, uiPanelCatalog);
    72	            builder.RegisterInterprocess(interprocessHost, interprocessPort);
    73	            builder.RegisterGameplaySystems();
    74	        }
    75	    }
    76	}
````

### UnityProject/Assets/Scripts/Core/Installers/InterprocessInstaller.cs (7356 bytes)
````
     1	using MessagePipe;
     2	using VContainer;
     3	using Xianxia.Sect.Messages;
     4	
     5	namespace Xianxia.Sect.Installers
     6	{
     7	    /// <summary>
     8	    /// PHASE 1 refactor: บล็อก in-memory pub/sub + interprocess TCP ย้ายมาจาก
     9	    /// GameLifetimeScope.Configure() ครบทุกบรรทัด — ตามกฎงาน: RegisterMessagePipe +
    10	    /// ToMessagePipeBuilder + ทุก RegisterTcp* ต้องอยู่ที่ ROOT scope เท่านั้น
    11	    /// (bridge process ติดต่อ root เสมอ — ห้ามย้ายลง child scope)
    12	    /// คืนค่า MessagePipeOptions กลับให้ caller เผื่อใช้ต่อ (ค่า options ถูกใช้ภายใน
    13	    /// ตัว installer แล้วสำหรับ RegisterAsyncRequestHandler)
    14	    /// </summary>
    15	    internal static class InterprocessInstaller
    16	    {
    17	        public static MessagePipeOptions RegisterInterprocess(
    18	            this IContainerBuilder builder,
    19	            string interprocessHost,
    20	            int interprocessPort)
    21	        {
    22	            // --- in-memory pub/sub + request-response (internal subsystem bus) ---
    23	            var options = builder.RegisterMessagePipe(pipeOptions =>
    24	            {
    25	                pipeOptions.InstanceLifetime = InstanceLifetime.Singleton;
    26	            });
    27	
    28	            // --- interprocess transport: Unity <-> MCP bridge process, over TCP ---
    29	            // Unity hosts the TCP endpoint; the bridge process connects as a client.
    30	            var messagePipeBuilder = builder.ToMessagePipeBuilder();
    31	            var interprocess = messagePipeBuilder.AddTcpInterprocess(
    32	                interprocessHost,
    33	                interprocessPort,
    34	                tcpOptions =>
    35	                {
    36	                    tcpOptions.HostAsServer = true;
    37	                    tcpOptions.InstanceLifetime = InstanceLifetime.Singleton;
    38	                });
    39	
    40	            // Only register the message types the bridge actually needs on the
    41	            // wire. TimeSpeedChangedMessage stays internal-only on purpose -
    42	            // the bridge doesn't need per-frame speed changes.
    43	            // WorldEventTriggeredMessage is deliberately NOT registered here
    44	            // (see AwaitWorldEventRequest in GameMessages.cs) - the bridge
    45	            // can't safely IDistributedSubscriber over this TCP transport,
    46	            // it's request-response only for that one.
    47	            messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, DiscipleRecruitedMessage>(interprocess);
    48	            messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, DiscipleRankChangedMessage>(interprocess);
    49	            messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, SectResourceChangedMessage>(interprocess);
    50	            messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, ContributionEarnedMessage>(interprocess);
    51	            messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, ExecuteDecisionMessage>(interprocess);
    52	
    53	            // Request/response: bridge asks "what's the sect state right now".
    54	            // Correction from an earlier pass: RegisterTcpRemoteRequestHandler
    55	            // is needed here too, even though Unity is HostAsServer=true and
    56	            // holds the real handler. It's what wires the TCP worker to the
    57	            // registered IAsyncRequestHandler, not just a caller-side proxy -
    58	            // confirmed against Wanxiang.Guanxiangtai's FrontendIpcServer.cs,
    59	            // which registers both on its (HostAsServer=true) side.
    60	            messagePipeBuilder.RegisterTcpRemoteRequestHandler<SectStateQuery, SectStateSnapshot>(interprocess);
    61	            builder.RegisterAsyncRequestHandler<SectStateQuery, SectStateSnapshot, SectStateQueryHandler>(options);
    62	
    63	            // Request/response: bridge asks "wait for the next world event"
    64	            // and blocks until Unity's TimeSystem completes it - see the
    65	            // comment on AwaitWorldEventRequest for why this isn't pub/sub.
    66	            messagePipeBuilder.RegisterTcpRemoteRequestHandler<AwaitWorldEventRequest, AwaitWorldEventResponse>(interprocess);
    67	            builder.RegisterAsyncRequestHandler<AwaitWorldEventRequest, AwaitWorldEventResponse, AwaitWorldEventHandler>(options);
    68	
    69	            // Request/response: a disciple buying an item from the sect
    70	            // stockpile with contribution.
    71	            messagePipeBuilder.RegisterTcpRemoteRequestHandler<PurchaseItemRequest, PurchaseItemResponse>(interprocess);
    72	            builder.RegisterAsyncRequestHandler<PurchaseItemRequest, PurchaseItemResponse, PurchaseItemHandler>(options);
    73	
    74	            // Request/response: changing a disciple's avatar part
    75	            messagePipeBuilder.RegisterTcpRemoteRequestHandler<ChangeAvatarPartRequest, ChangeAvatarPartResponse>(interprocess);
    76	            builder.RegisterAsyncRequestHandler<ChangeAvatarPartRequest, ChangeAvatarPartResponse, ChangeAvatarPartHandler>(options);
    77	
    78	            // Request/response: assigning a task to a disciple (permission-checked).
    79	            // Both calls are required — RegisterTcpRemoteRequestHandler wires the TCP
    80	            // worker to the handler, RegisterAsyncRequestHandler registers the handler
    81	            // itself; missing either fails silently at runtime (same as PurchaseItem).
    82	            messagePipeBuilder.RegisterTcpRemoteRequestHandler<AssignTaskRequest, AssignTaskResponse>(interprocess);
    83	            builder.RegisterAsyncRequestHandler<AssignTaskRequest, AssignTaskResponse, AssignTaskHandler>(options);
    84	
    85	            // Request/response: the bridge reads the ownership-change log
    86	            // (P4 observability, READ-ONLY — deliberately no ownership-assignment
    87	            // pair or tool exists; the only writers stay the dev harness/fixtures).
    88	            messagePipeBuilder.RegisterTcpRemoteRequestHandler<OwnershipObservabilityQuery, OwnershipObservabilitySnapshot>(interprocess);
    89	            builder.RegisterAsyncRequestHandler<OwnershipObservabilityQuery, OwnershipObservabilitySnapshot, OwnershipObservabilityHandler>(options);
    90	
    91	            // Request/response: the bridge reads the task-change log (P5B
    92	            // observability, READ-ONLY). Task assignment keeps its own AssignTask
    93	            // pair; this pair only observes.
    94	            messagePipeBuilder.RegisterTcpRemoteRequestHandler<TaskChangeObservabilityQuery, TaskChangeObservabilitySnapshot>(interprocess);
    95	            builder.RegisterAsyncRequestHandler<TaskChangeObservabilityQuery, TaskChangeObservabilitySnapshot, TaskChangeObservabilityHandler>(options);
    96	
    97	            // Request/response: the bridge reads the current viewer-protection
    98	            // state (P5B, READ-ONLY) — protection, remaining time, membership
    99	            // consistency and who may change each Viewer-owned disciple's task.
   100	            messagePipeBuilder.RegisterTcpRemoteRequestHandler<TaskProtectionQuery, TaskProtectionSnapshot>(interprocess);
   101	            builder.RegisterAsyncRequestHandler<TaskProtectionQuery, TaskProtectionSnapshot, TaskProtectionHandler>(options);
   102	
   103	            // Interprocess pub/sub: broadcast avatar equipment changes to UI + MCP client
   104	            messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, AvatarEquipmentChangedMessage>(interprocess);
   105	
   106	            return options;
   107	        }
   108	    }
   109	}
````

### UnityProject/Assets/Scripts/Scenes/SectScene/SectSceneLifetimeScope.cs (4188 bytes)
````
     1	using VContainer;
     2	using VContainer.Unity;
     3	using Xianxia.Sect.Visual;
     4	
     5	namespace Xianxia.Sect
     6	{
     7	    /// <summary>
     8	    /// PHASE 2 refactor: child LifetimeScope ผูกกับฉากเกมเพลย์ SectScene
     9	    /// ย้ายมาจาก GameLifetimeScope.Configure() ที่เคยอยู่ root.
    10	    ///
    11	    /// ของที่อยู่ที่นี่ = สิ่งที่ผูกกับฉากเท่านั้น (rig ports + กล้อง/overlay/backdrop)
    12	    /// — ยืนยันด้วย grep ทั้งโปรเจกต์ (รวม Tests/ และ Editor/) แล้วว่าไม่มีใคร
    13	    /// resolve สิ่งเหล่านี้จาก root: EditMode tests ใช้ fake ของตัวเอง
    14	    /// (CameraRigControllerTests), verify runners ใช้แค่ ISectStateProvider/
    15	    /// SceneLoader/UIService/visual core (root ยังอยู่ครบ)
    16	    ///
    17	    /// ⚠️ ข้อจำกัด: กด Play จากฉากนี้ "เดี่ยว ๆ" จะไม่มี parent scope → กล้อง/overlay/
    18	    /// backdrop จะไม่ถูกสร้าง (ไม่มี MessagePipeRigBus ฯลฯ) — ต้อง Play จาก
    19	    /// SampleScene เสมอ (SceneLoader จะ EnqueueParent แล้วโหลดฉากนี้ให้)
    20	    ///
    21	    /// Parent reference: ผูกผ่าน LifetimeScope.EnqueueParent(root) ใน SceneLoader
    22	    /// ตอนโหลด — Parent Reference (Type) บน Inspector ปล่อยว่าง
    23	    /// </summary>
    24	    public sealed class SectSceneLifetimeScope : LifetimeScope
    25	    {
    26	        protected override void Configure(IContainerBuilder builder)
    27	        {
    28	            RegisterSceneBoundServices(builder);
    29	        }
    30	
    31	        /// <summary>
    32	        /// Registration ของ "ของที่ผูกกับฉาก" ทั้งหมด — แยกออกมาเป็น internal static
    33	        /// เพื่อให้ EditMode test (ดู AssemblyInfo.cs: InternalsVisibleTo) ตรวจ
    34	        /// registration ที่นี่ได้จริงผ่าน ContainerBuilder โดยไม่ต้องเปิด scope/เล่นเกม
    35	        /// (CompositionRootContainerTests). Configure() เรียกตัวนี้ตรง ๆ → ไม่มีทางที่
    36	        /// เนื้อ registration จะต่างจากที่เทสต์ตรวจ
    37	        /// </summary>
    38	        internal static void RegisterSceneBoundServices(IContainerBuilder builder)
    39	        {
    40	            // --- camera rig ports (test seams; see CameraRigPorts.cs) ---
    41	            // The rig talks to the bus/scene/Resources/Time only through these
    42	            // interfaces, so EditMode tests can drive it with fakes.
    43	            // (ย้ายมาจาก GameLifetimeScope.Configure ตอน Phase 2 — ผูกกับฉาก)
    44	            builder.Register<IRigMessageBus, MessagePipeRigBus>(Lifetime.Singleton);
    45	            builder.Register<ICameraRigEnvironment, SceneEnvironment>(Lifetime.Singleton);
    46	            builder.Register<ICellSpriteMetrics, ResourcesCellSpriteMetrics>(Lifetime.Singleton);
    47	            builder.Register<IMainThreadQueue, UniTaskMainThreadQueue>(Lifetime.Singleton);
    48	            builder.Register<IRigClock, UnityRigClock>(Lifetime.Singleton);
    49	            // pan input (คลิกขวาค้าง+ลาก) — ผูกกับ Unity Input เฉพาะ play mode ผ่าน
    50	            // CameraRigPanBootstrap ([RuntimeInitializeOnLoadMethod]); EditMode test
    51	            // ไม่มี wire → CameraRigController ข้าม input ทั้งหมด (test-safe)
    52	
    53	            // --- scene-bound entry points (กล้อง + grid overlay + backdrop) ---
    54	            builder.RegisterEntryPoint<CameraRigController>(Lifetime.Singleton).AsSelf();
    55	            builder.RegisterEntryPoint<GridOverlayRenderer>(Lifetime.Singleton).AsSelf();
    56	            builder.RegisterEntryPoint<TerrainBackdropRenderer>(Lifetime.Singleton).AsSelf();
    57	        }
    58	    }
    59	}
````

### UnityProject/Assets/Scripts/Core/TimeSystem.cs (17061 bytes)
````
     1	using System.Collections.Generic;
     2	using System.Threading;
     3	using Cysharp.Threading.Tasks;
     4	using MessagePipe;
     5	using UnityEngine;
     6	using VContainer.Unity;
     7	using Xianxia.Sect.Building;
     8	using Xianxia.Sect.Messages;
     9	
    10	namespace Xianxia.Sect
    11	{
    12	    // Example subsystem wired through the bus instead of holding direct
    13	    // references to other systems. Compare to the earlier plain GameManager
    14	    // sketch - the difference is TimeSystem doesn't know DiscipleSystem or
    15	    // BuildingSystem exist; it just publishes, and whoever cares subscribes.
    16	    public class TimeSystem : IStartable, ITickable
    17	    {
    18	        private readonly IPublisher<TimeSpeedChangedMessage> _speedPublisher;
    19	        private readonly IPublisher<WorldEventTriggeredMessage> _worldEventPublisher;
    20	
    21	        private int _speed = 1;
    22	        private bool _paused;
    23	
    24	        // Completed (and replaced with a fresh one) every time a world
    25	        // event fires - see WaitForNextWorldEventAsync()/RaiseWorldEvent().
    26	        private UniTaskCompletionSource<AwaitWorldEventResponse> _pendingEventSource =
    27	            new UniTaskCompletionSource<AwaitWorldEventResponse>();
    28	
    29	        // If a decision-requiring event fired before anyone called
    30	        // await_next_world_event, hand it back immediately on the next call
    31	        // instead of making a late caller wait for a completely new event.
    32	        // Cleared once handed out - a second call with nothing new pending
    33	        // goes back to waiting normally.
    34	        private AwaitWorldEventResponse _cachedPendingEvent;
    35	
    36	        public TimeSystem(
    37	            IPublisher<TimeSpeedChangedMessage> speedPublisher,
    38	            IPublisher<WorldEventTriggeredMessage> worldEventPublisher)
    39	        {
    40	            _speedPublisher = speedPublisher;
    41	            _worldEventPublisher = worldEventPublisher;
    42	        }
    43	
    44	        public void Start()
    45	        {
    46	            _speedPublisher.Publish(new TimeSpeedChangedMessage { Speed = _speed, Paused = _paused });
    47	        }
    48	
    49	        public void SetPaused(bool paused)
    50	        {
    51	            _paused = paused;
    52	            _speedPublisher.Publish(new TimeSpeedChangedMessage { Speed = _speed, Paused = _paused });
    53	        }
    54	
    55	        public void SetSpeed(int speed)
    56	        {
    57	            _speed = speed;
    58	            _speedPublisher.Publish(new TimeSpeedChangedMessage { Speed = _speed, Paused = _paused });
    59	        }
    60	
    61	        public void Tick()
    62	        {
    63	            if (_paused) return;
    64	            // TODO: advance world clock by _speed * UnityEngine.Time.deltaTime
    65	        }
    66	
    67	        public bool IsPaused => _paused;
    68	
    69	        // Resolved by AwaitWorldEventHandler - the bridge's await_next_world_event
    70	        // tool call blocks on this until the next RaiseWorldEvent(), unless
    71	        // there's already a cached one waiting (see _cachedPendingEvent).
    72	        //
    73	        // Remember: while _paused is true (a decision-requiring event is
    74	        // outstanding), RaiseWorldEvent never fires again - WorldEventSystem
    75	        // checks IsPaused and skips. Call execute_decision first to unpause,
    76	        // or this will time out waiting for an event that can't happen yet.
    77	        public UniTask<AwaitWorldEventResponse> WaitForNextWorldEventAsync()
    78	        {
    79	            if (_cachedPendingEvent != null)
    80	            {
    81	                Debug.Log("[TimeSystem] Returning cached world event immediately.");
    82	                var cached = _cachedPendingEvent;
    83	                _cachedPendingEvent = null;
    84	                return UniTask.FromResult(cached);
    85	            }
    86	
    87	            return _pendingEventSource.Task;
    88	        }
    89	
    90	        // Called by whatever system decides a world event fired (new
    91	        // applicant, monster incursion, ...). requiresDecision auto-pauses -
    92	        // this is the "checkpoint" the AI GM / vote window waits on, and
    93	        // stays paused until execute_decision is called.
    94	        //
    95	        // Not sent over the interprocess bus as pub/sub (see the comment on
    96	        // AwaitWorldEventRequest in GameMessages.cs for why) - completing
    97	        // _pendingEventSource is what actually delivers this to the bridge,
    98	        // via the request-response AwaitWorldEventHandler below. The
    99	        // in-memory Publish() call is just for any other in-Unity listener.
   100	        public void RaiseWorldEvent(string eventId, string description, bool requiresDecision, List<EventChoiceInfo> choices)
   101	        {
   102	            Debug.Log($"[TimeSystem] World event raised: {eventId} (requiresDecision={requiresDecision})");
   103	
   104	            if (requiresDecision) SetPaused(true);
   105	
   106	            _worldEventPublisher.Publish(new WorldEventTriggeredMessage
   107	            {
   108	                EventId = eventId,
   109	                RequiresDecision = requiresDecision,
   110	                Description = description,
   111	                Choices = choices ?? new List<EventChoiceInfo>(),
   112	            });
   113	
   114	            var response = new AwaitWorldEventResponse
   115	            {
   116	                EventId = eventId,
   117	                RequiresDecision = requiresDecision,
   118	                Description = description,
   119	                Choices = choices ?? new List<EventChoiceInfo>(),
   120	            };
   121	
   122	            if (requiresDecision)
   123	            {
   124	                _cachedPendingEvent = response;
   125	            }
   126	
   127	            var previous = _pendingEventSource;
   128	            _pendingEventSource = new UniTaskCompletionSource<AwaitWorldEventResponse>();
   129	            previous.TrySetResult(response);
   130	        }
   131	    }
   132	
   133	    // Answers AwaitWorldEventRequest coming in over the interprocess bus.
   134	    // Request-response, not pub/sub - see the comment on AwaitWorldEventRequest
   135	    // in GameMessages.cs for why.
   136	    public class AwaitWorldEventHandler : IAsyncRequestHandler<AwaitWorldEventRequest, AwaitWorldEventResponse>
   137	    {
   138	        private readonly TimeSystem _timeSystem;
   139	
   140	        public AwaitWorldEventHandler(TimeSystem timeSystem)
   141	        {
   142	            _timeSystem = timeSystem;
   143	        }
   144	
   145	        public UniTask<AwaitWorldEventResponse> InvokeAsync(AwaitWorldEventRequest request, CancellationToken cancellationToken = default)
   146	        {
   147	            return _timeSystem.WaitForNextWorldEventAsync();
   148	        }
   149	    }
   150	
   151	    // Answers SectStateQuery requests coming in over the interprocess bus
   152	    // from the MCP bridge. Aggregates whatever the subsystems currently hold
   153	    // into the SectEconomyState shape from economy.proto.
   154	    public class SectStateQueryHandler : IAsyncRequestHandler<SectStateQuery, SectStateSnapshot>
   155	    {
   156	        private readonly ISectStateProvider _stateProvider;
   157	
   158	        public SectStateQueryHandler(ISectStateProvider stateProvider)
   159	        {
   160	            _stateProvider = stateProvider;
   161	        }
   162	
   163	        public UniTask<SectStateSnapshot> InvokeAsync(SectStateQuery request, CancellationToken cancellationToken = default)
   164	        {
   165	            var state = _stateProvider.BuildSectEconomyState();
   166	            var snapshot = new SectStateSnapshot
   167	            {
   168	                RequestId = request.RequestId,
   169	                // MessagePack, committed choice (not a protobuf stub
   170	                // anymore - see project_summary.md for why).
   171	                EconomyStateBytes = state.ToByteArray()
   172	            };
   173	            return UniTask.FromResult(snapshot);
   174	        }
   175	    }
   176	
   177	    // ---------- P5B (Hybrid Permissions) — read-only permission contract ----------
   178	    // The UI must never infer permission from OwnerType != Npc on its own; it asks
   179	    // the authority (SectStateProvider) and renders the answer. Same evaluation the
   180	    // mutation path revalidates, so display and commit can never disagree.
   181	
   182	    /// <summary>Three-way outcome of a task permission evaluation.</summary>
   183	    public enum TaskPermissionOutcome
   184	    {
   185	        /// <summary>Requester may control this disciple right now.</summary>
   186	        Allowed = 0,
   187	        /// <summary>Requester is a known identity that simply does not have permission.</summary>
   188	        Denied = 1,
   189	        /// <summary>Membership/ownership data is missing or contradictory — fail closed, NEVER permission.</summary>
   190	        ConsistencyError = 2,
   191	    }
   192	
   193	    /// <summary>
   194	    /// P5B — read-only result of a task permission evaluation. Never mutates state;
   195	    /// the mutation path (TryAssignTask) revalidates the same rules before writing.
   196	    /// </summary>
   197	    public sealed class TaskPermissionResult
   198	    {
   199	        public TaskPermissionOutcome Outcome { get; private set; }
   200	        public string Reason { get; private set; } = string.Empty;
   201	
   202	        /// <summary>True when the requested task already is the disciple's current task (a valid no-op).</summary>
   203	        public bool IsNoOp { get; private set; }
   204	
   205	        /// <summary>True when a viewer owner is inside the protection window (SectMaster override not yet allowed).</summary>
   206	        public bool OwnerProtected { get; private set; }
   207	
   208	        /// <summary>Seconds left in the owner protection window (0 unless OwnerProtected).</summary>
   209	        public float OwnerProtectionRemainingSeconds { get; private set; }
   210	
   211	        public bool Allowed => Outcome == TaskPermissionOutcome.Allowed;
   212	
   213	        public static TaskPermissionResult Allow()
   214	            => new TaskPermissionResult { Outcome = TaskPermissionOutcome.Allowed };
   215	
   216	        public static TaskPermissionResult Denied(string reason)
   217	            => new TaskPermissionResult { Outcome = TaskPermissionOutcome.Denied, Reason = reason ?? string.Empty };
   218	
   219	        public static TaskPermissionResult ConsistencyError(string reason)
   220	            => new TaskPermissionResult { Outcome = TaskPermissionOutcome.ConsistencyError, Reason = reason ?? string.Empty };
   221	
   222	        internal TaskPermissionResult WithNoOp(bool isNoOp) { IsNoOp = isNoOp; return this; }
   223	
   224	        internal TaskPermissionResult WithProtection(float remainingSeconds)
   225	        {
   226	            OwnerProtected = true;
   227	            OwnerProtectionRemainingSeconds = remainingSeconds < 0f ? 0f : remainingSeconds;
   228	            return this;
   229	        }
   230	    }
   231	
   232	    // Thin seam so SectStateQueryHandler doesn't need to know about every
   233	    // subsystem directly - implement this on a small aggregator class that
   234	    // does hold references (it's allowed to, it's not part of the bus).
   235	    public interface ISectStateProvider
   236	    {
   237	        SectEconomyState BuildSectEconomyState();
   238	        void ApplyDecisionConsequence(string eventId, string choiceId);
   239	        void TickGathering(float deltaTimeSeconds);
   240	        void TickCrafting(float deltaTimeSeconds);
   241	        void RecruitOuterDisciple(DiscipleSex sex = DiscipleSex.Unspecified);
   242	        PurchaseItemResponse TryPurchaseItem(string discipleId, string itemDefId, int grade, int quantity);
   243	        bool TryChangeAvatarPart(string discipleId, string slot, string partId,
   244	                                out string failReason, out AvatarAppearance result);
   245	
   246	        /// <summary>
   247	        /// Task System v2 (§6) + P5B — assign a task to a disciple after a permission
   248	        /// and validity check. Permission follows CheckTaskPermission (revalidated
   249	        /// here): "SECT_MASTER" may control unowned NPCs and player-controlled
   250	        /// disciples, and may override a Viewer disciple only when the owner has been
   251	        /// inactive for strictly more than 10 real-time minutes; any other requester
   252	        /// only their own valid active membership. Empty/invalid requesters and
   253	        /// missing/conflicting membership data fail closed. A request for the task
   254	        /// already assigned is a no-op — no event, no progress reset (it may refresh
   255	        /// the requester's own activity). Actual task changes honour the configurable
   256	        /// cooldown. Unknown disciple or task fails closed with a reason. On success
   257	        /// sets CurrentTask and publishes DiscipleTaskChangedMessage (in-memory only).
   258	        /// </summary>
   259	        bool TryAssignTask(string requesterId, string discipleId, string taskId, out string failReason);
   260	
   261	        /// <summary>
   262	        /// P5B (Hybrid Permissions) — read-only permission query for UI. Evaluates the
   263	        /// SAME rules TryAssignTask revalidates on mutation, and never mutates state:
   264	        /// a valid requester may control its own active membership; "SECT_MASTER" may
   265	        /// control unowned NPCs and player-controlled disciples, and may override a
   266	        /// Viewer disciple only when the owner has been inactive for STRICTLY more than
   267	        /// 10 real-time minutes (injected clock). Empty/invalid requesters fail closed;
   268	        /// missing or conflicting membership data returns a ConsistencyError — never
   269	        /// automatic permission.
   270	        /// </summary>
   271	        TaskPermissionResult CheckTaskPermission(string requesterId, string discipleId, string taskId);
   272	
   273	        /// <summary>
   274	        /// P5B persistence — the membership slice worth surviving a session: the
   275	        /// viewer registry (status / binding / LastActiveAtUtc) TOGETHER with each
   276	        /// disciple's ownership, so the registry ⇄ ownership invariants hold after a
   277	        /// load. Read-only — the live state is not modified by exporting.
   278	        /// </summary>
   279	        Xianxia.Sect.Messages.SectViewerMembershipSave ExportViewerMembership();
   280	
   281	        /// <summary>
   282	        /// P5B persistence — restore a previously exported slice. Fully validated
   283	        /// BEFORE any mutation (version, unknown disciples, registry internal
   284	        /// consistency, and registry ⇄ ownership agreement); anything invalid fails
   285	        /// closed with the mock start state still in place. Ownership and registry are
   286	        /// applied together, so a half-restored state can never exist.
   287	        /// </summary>
   288	        bool TryImportViewerMembership(Xianxia.Sect.Messages.SectViewerMembershipSave save, out string failReason);
   289	
   290	        /// <summary>
   291	        /// Task building-requirement gate (§6 addendum). Reads the live
   292	        /// PlacedBuildings list — true when the task is known and either has no
   293	        /// required building or that building def id already exists in state.
   294	        /// Unknown task fails with its usual reason; failClosed — never mutates.
   295	        /// </summary>
   296	        bool IsTaskAvailable(string taskId, out string failReason);
   297	
   298	        /// <summary>
   299	        /// P3 (Task Assignment UI) — the known-task set TryAssignTask validates
   300	        /// against, in stable order (gathering, crafting, meditation). The SAME
   301	        /// source of truth as the assignment gate — the UI lists these directly,
   302	        /// so no second task list can drift from what assignment accepts.
   303	        /// Read-only; never mutates.
   304	        /// </summary>
   305	        System.Collections.Generic.IReadOnlyList<string> GetKnownTaskIds();
   306	
   307	        /// <summary>
   308	        /// P4 (local ownership test harness) — dev-only ownership assignment.
   309	        /// NOT exposed as a public viewer command or MCP tool; the intended caller
   310	        /// is the Editor debug harness / test fixtures. Validates fully before any
   311	        /// mutation (fail-closed): disciple exists, enum value defined, Npc
   312	        /// normalizes OwnerId to empty, non-Npc identities satisfy the identity
   313	        /// convention, and a Viewer identity cannot bind to two disciples.
   314	        /// Publishes DiscipleOwnerChangedMessage (in-memory) only on a real change.
   315	        /// P5B: bind/release keeps SectViewerRegistry in lock-step with the disciple
   316	        /// row (active viewer ⇔ matching active record, invariant #1/#2/#3) and
   317	        /// refreshes the owner's LastActiveAtUtc on a successful bind/reclaim.
   318	        /// </summary>
   319	        bool TrySetDiscipleOwner(string discipleId, Xianxia.Sect.DiscipleOwnerType ownerType,
   320	                                 string ownerId, out string failReason);
   321	
   322	        /// <summary>
   323	        /// Mutate DiscipleState.ChibiBackend (entitlement) + publish
   324	        /// DiscipleChibiBackendChangedMessage (in-memory). DiscipleVisualSystem respawns
   325	        /// the visual in place on that message — position/activity/facing preserved (§7).
   326	        /// </summary>
   327	        bool TrySetChibiBackend(string discipleId, ChibiBackend backend, out string failReason);
   328	
   329	        /// <summary>
   330	        /// Building Phase 1 — place a building on the sect grid (player-only, §8 Q3 default).
   331	        /// Validation order per building-system.md §3.2: def lookup → occupancy → cost,
   332	        /// then all-or-nothing resource deduction through the AdjustAndNotify choke point,
   333	        /// state append, and BuildingPlacedMessage publish (in-memory only).
   334	        /// Caller must call CanAffordBuilding / grid.CanPlace first for ghost preview;
   335	        /// this re-validates everything and fails closed with a reason.
   336	        /// </summary>
   337	        bool TryPlaceBuilding(string defId, int gridX, int gridZ, int rotation,
   338	                              BuildingGrid grid, out string failReason,
   339	                              out PlacedBuildingState placed);
   340	
   341	        /// <summary>Ghost preview check: can the sect pay this def's cost right now?
   342	        /// Read-only — no state mutation (occupied-cells check lives on BuildingGrid).</summary>
   343	        bool CanAffordBuilding(BuildingDef def);
   344	    }
   345	}
````

### UnityProject/Assets/Scripts/Systems/SectStateProvider.cs (67942 bytes)
````
     1	using System;
     2	using System.Collections.Generic;
     3	using System.Linq;
     4	using MessagePipe;
     5	using UnityEngine;
     6	using Xianxia.Sect.Building;
     7	using Xianxia.Sect.Messages;
     8	using Xianxia.Sect.Visual;
     9	
    10	namespace Xianxia.Sect
    11	{
    12	    // Implements the seam TimeSystem.cs defines (ISectStateProvider).
    13	    //
    14	    // IMPORTANT: this holds ONE live state instance for the process
    15	    // lifetime, created once from mock data. Earlier versions called
    16	    // MockSectData.Create() fresh on every query, which meant any mutation
    17	    // was invisible on the next get_sect_state call - it was building a
    18	    // brand new object every time, not reading back the one that got
    19	    // mutated. Swap _state's origin for real multi-subsystem aggregation
    20	    // later if this ends up needing more than gathering + recruiting - the
    21	    // interface doesn't need to change.
    22	    public class SectStateProvider : ISectStateProvider
    23	    {
    24	        // task id -> (resource id, units produced per second while assigned)
    25	        private static readonly Dictionary<string, (string Resource, float PerSecond)> GatheringRates = new()
    26	        {
    27	            ["gathering_herb"] = ("herb", 0.2f),
    28	            ["gathering_wood"] = ("wood", 0.2f),
    29	            ["gathering_ore"] = ("ore", 0.15f),
    30	            ["gathering_provisions"] = ("provisions", 0.25f),
    31	        };
    32	
    33	        private static readonly string[] GatheringTasks = GatheringRates.Keys.ToArray();
    34	
    35	        // task id -> recipe. CraftSeconds is how long one disciple assigned
    36	        // to that task takes to finish one item, once ingredients are
    37	        // available - if the stockpile runs short, progress holds at 100%
    38	        // and waits rather than losing accumulated time.
    39	        private static readonly Dictionary<string, CraftingRecipe> CraftingRecipes = new()
    40	        {
    41	            ["refining_elixir"] = new CraftingRecipe(
    42	                "elixir_qi_gathering", 3, 20f,
    43	                new Dictionary<string, int> { ["herb"] = 10 }),
    44	
    45	            ["forging_artifact"] = new CraftingRecipe(
    46	                "sword_azure_flame", 5, 30f,
    47	                new Dictionary<string, int> { ["ore"] = 15, ["wood"] = 10 }),
    48	        };        // Task System v2 (§6) — the known task set is the keys of the existing
    49	        // gathering + crafting dictionaries, plus "meditation". No new data
    50	        // pipeline: the dictionaries ARE the source of truth for what a disciple
    51	        // can be assigned. See open-questions.md §15 for the cultivation/meditation id question.
    52	        private static readonly HashSet<string> KnownTasks = BuildKnownTasks();
    53	
    54	        // P3 (Task Assignment UI) — stable ordered list of the same known tasks
    55	        // (gathering, crafting, meditation). Kept beside the HashSet so the UI
    56	        // query and the assignment gate can never drift apart; both are built
    57	        // from the same dictionaries.
    58	        private static readonly string[] KnownTaskOrder =
    59	            GatheringRates.Keys.Concat(CraftingRecipes.Keys).Concat(new[] { "meditation" }).ToArray();
    60	
    61	        private static HashSet<string> BuildKnownTasks()
    62	        {
    63	            var set = new HashSet<string>();
    64	            foreach (var task in GatheringRates.Keys) set.Add(task);
    65	            foreach (var task in CraftingRecipes.Keys) set.Add(task);
    66	            set.Add("meditation");
    67	            return set;
    68	 }
    69	
    70	        // Task building-requirements — static design data, not runtime state:
    71	        // task id -> building def id that must be in SectEconomyState.PlacedBuildings
    72	        // before that task can be assigned. Tasks absent from this dictionary
    73	        // (gathering_wood/ore/provisions, meditation) have no requirement.
    74	        // Placeholder lookup: list-scan of PlacedBuildings is fast enough while
    75	        // the roster is small; swap for an index later if it grows (same shape
    76	        // as NextBuildingInstanceId's scan).
    77	        private static readonly Dictionary<string, string> TaskRequiredBuilding = new Dictionary<string, string>
    78	        {
    79	            ["gathering_herb"] = "herb_plot",
    80	            ["refining_elixir"] = "pill_hall",
    81	            ["forging_artifact"] = "forge",
    82	        };
    83	
    84	        // Tick-side variant of IsTaskAvailable — same rules, but the caller
    85	        // supplies a placed-DefId set built ONCE per tick (never scan
    86	        // PlacedBuildings per disciple). Same failReason shape so log/UI text
    87	        // stays consistent with the gate on TryAssignTask.
    88	        private static bool IsTaskAvailableWithBuildings(string taskId, HashSet<string> placedDefIds, out string failReason)
    89	        {
    90	            failReason = string.Empty;
    91	
    92	            if (string.IsNullOrEmpty(taskId) || !KnownTasks.Contains(taskId))
    93	            {
    94	                failReason = $"Unknown task: '{taskId}'.";
    95	                return false;
    96	            }
    97	
    98	            string requiredBuilding;
    99	            if (!TaskRequiredBuilding.TryGetValue(taskId, out requiredBuilding))
   100	                return true; // no requirement — always available once known
   101	
   102	            if (placedDefIds != null && placedDefIds.Contains(requiredBuilding))
   103	                return true;
   104	
   105	            failReason = $"Task '{taskId}' requires an existing '{requiredBuilding}' building.";
   106	            return false;
   107	        }
   108	
   109	        /// <summary>Placed DefId set for one tick — built once, shared by both ticks.</summary>
   110	        private HashSet<string> CollectPlacedDefIds()
   111	        {
   112	            var set = new HashSet<string>();
   113	            for (int i = 0; i < _state.PlacedBuildings.Count; i++)
   114	            {
   115	                var pb = _state.PlacedBuildings[i];
   116	                if (pb != null && !string.IsNullOrEmpty(pb.DefId)) set.Add(pb.DefId);
   117	            }
   118	            return set;
   119	        }
   120	
   121	        // Placeholder name pool - swap for a real generator once there's a
   122	        // reason to (naming conventions, avoiding repeats at scale, etc.).
   123	        private static readonly string[] RecruitNamePool =
   124	        {
   125	            "Chen Wei", "Bai Ling", "Zhou Tao", "Xiao Mei", "Jiang Yu", "Wen Hao",
   126	        };
   127	
   128	        private static readonly string[] StarterHair = { "hair_short", "hair_topknot", "hair_twin_tail" };
   129	
   130	        private readonly SectEconomyState _state = MockSectData.Create();
   131	        private readonly IPublisher<DiscipleRecruitedMessage> _discipleRecruitedPublisher;
   132	        private readonly IPublisher<SectResourceChangedMessage> _resourceChangedPublisher;
   133	        private readonly IPublisher<AvatarEquipmentChangedMessage> _avatarChangedPublisher;
   134	        private readonly IPublisher<DiscipleChibiBackendChangedMessage> _chibiBackendPublisher;
   135	        private readonly IPublisher<BuildingPlacedMessage> _buildingPlacedPublisher;
   136	        private readonly IPublisher<DiscipleTaskChangedMessage> _discipleTaskChangedPublisher;
   137	        private readonly IPublisher<DiscipleOwnerChangedMessage> _ownershipChangedPublisher;
   138	        private readonly BuildingDefPool _buildingDefPool;
   139	        private readonly AvatarPartPool _avatarPartPool;
   140	        private readonly VisualRuntimeConfig _visualConfig;
   141	        private readonly IVisualEntitlementProvider _entitlementProvider;
   142	        /// <summary>Concrete ref to the injected provider (null when a test/substitute implements the interface directly) — used only for bind-late wiring, not for resolution.</summary>
   143	        private readonly DefaultEntitlementProvider _defaultEntitlementProvider;
   144	
   145	        // P5B — real-time clock for the viewer inactivity/activity rule. Injected so
   146	        // tests can drive it deterministically; NEVER scaled game time (game time can
   147	        // be paused/speed-changed, which would silently extend or shrink protection).
   148	        private readonly IClock _clock;
   149	
   150	        // P5B — real-time cooldown on ACTUAL task changes, keyed per disciple.
   151	        // Prototype balance value (see DefaultTaskChangeCooldownSeconds). A no-op
   152	        // request never consumes or checks it.
   153	        private readonly Dictionary<string, DateTime> _taskChangeLastAtUtc = new();
   154	
   155	        // Fractional resource accumulated per task since the last whole
   156	        // unit was added to the stockpile - avoids losing sub-1 production
   157	        // between ticks.
   158	        private readonly Dictionary<string, float> _gatherAccumulators = new();
   159	
   160	        // Seconds accumulated toward the current craft, keyed per disciple
   161	        // (not per task like gathering) - crafting has a resource cost, so
   162	        // two disciples on the same task must progress independently, not
   163	        // share one pooled timer.
   164	        private readonly Dictionary<string, float> _craftProgress = new();
   165	
   166	        public SectStateProvider(
   167	            IPublisher<DiscipleRecruitedMessage> discipleRecruitedPublisher,
   168	            IPublisher<SectResourceChangedMessage> resourceChangedPublisher,
   169	            IPublisher<AvatarEquipmentChangedMessage> avatarChangedPublisher,
   170	            IPublisher<DiscipleChibiBackendChangedMessage> chibiBackendPublisher,
   171	            AvatarPartPool avatarPartPool,
   172	            VisualRuntimeConfig visualConfig,
   173	            IVisualEntitlementProvider entitlementProvider,
   174	            BuildingDefPool buildingDefPool,
   175	            IPublisher<BuildingPlacedMessage> buildingPlacedPublisher,
   176	            IPublisher<DiscipleTaskChangedMessage> discipleTaskChangedPublisher,
   177	            IPublisher<DiscipleOwnerChangedMessage> ownershipChangedPublisher = null,
   178	            IClock clock = null)
   179	        {
   180	            _discipleRecruitedPublisher = discipleRecruitedPublisher;
   181	            _resourceChangedPublisher = resourceChangedPublisher;
   182	            _avatarChangedPublisher = avatarChangedPublisher;
   183	            _chibiBackendPublisher = chibiBackendPublisher;
   184	            _buildingDefPool = buildingDefPool;
   185	            _buildingPlacedPublisher = buildingPlacedPublisher;
   186	            _discipleTaskChangedPublisher = discipleTaskChangedPublisher;
   187	            // P4: optional (default null) so every existing test construction site
   188	            // stays valid; production wires it via VContainer in UIInstaller/GameLifetimeScope.
   189	            _ownershipChangedPublisher = ownershipChangedPublisher;
   190	            _avatarPartPool = avatarPartPool;
   191	            _visualConfig = visualConfig;
   192	            _entitlementProvider = entitlementProvider;
   193	            _defaultEntitlementProvider = entitlementProvider as DefaultEntitlementProvider;
   194	            // P5B: production registers UtcClock via DI; every existing test construction
   195	            // site omits it and keeps working (real UTC clock, which those tests never
   196	            // depend on because they never cross the 10-minute protection window).
   197	            _clock = clock ?? new UtcClock();
   198	
   199	            // Phase 5 — bind the provider's rank source HERE instead of injecting
   200	            // ISectStateProvider into the provider itself: that direction would be a
   201	            // DI cycle (SectStateProvider → provider → SectStateProvider). Bind-late
   202	            // keeps the provider ignorant of the state module; before this line runs,
   203	            // CanUse(discipleId, "owner") fails closed (Unspecified = deny).
   204	            //
   205	            // Note: inject the INTERFACE, not the concrete type. In this VContainer
   206	            // version Register<I, Impl> registers only the interface (concrete Resolve
   207	            // is not available), so consumers must resolve IVisualEntitlementProvider
   208	            // and reach the concrete for bind-late via a type test.
   209	            _defaultEntitlementProvider?.BindRankLookup(id =>
   210	            {
   211	                var d = FindDisciple(id);
   212	                return d != null ? d.Rank : DiscipleRank.Unspecified;
   213	            });
   214	        }
   215	
   216	        /// <summary>Single lookup helper — also used by the entitlement rank binding.</summary>
   217	        private DiscipleState FindDisciple(string discipleId)
   218	        {
   219	            if (string.IsNullOrEmpty(discipleId)) return null;
   220	            for (int i = 0; i < _state.Disciples.Count; i++)
   221	            {
   222	                var d = _state.Disciples[i];
   223	                if (d != null && d.DiscipleId == discipleId) return d;
   224	            }
   225	            return null;
   226	        }
   227	
   228	        public SectEconomyState BuildSectEconomyState()
   229	        {
   230	            return _state;
   231	        }
   232	
   233	        // Passive resource gathering - every disciple whose CurrentTask is
   234	        // a known gathering task contributes toward that resource. Called
   235	        // from DiscipleSystem.Tick(). Disciples whose task fails the building
   236	        // requirement are SKIPPED (CurrentTask is never rewritten here — the
   237	        // assignment gate is the only place that validates on assignment).
   238	        public void TickGathering(float deltaTimeSeconds)
   239	        {
   240	            var placedDefIds = CollectPlacedDefIds(); // once per tick, not per disciple
   241	            foreach (var disciple in _state.Disciples)
   242	            {
   243	                if (!IsTaskAvailableWithBuildings(disciple.CurrentTask, placedDefIds, out _)) continue;
   244	                if (!GatheringRates.TryGetValue(disciple.CurrentTask, out var rate)) continue;
   245	
   246	                var accKey = disciple.CurrentTask;
   247	                var acc = _gatherAccumulators.TryGetValue(accKey, out var existing) ? existing : 0f;
   248	                acc += rate.PerSecond * deltaTimeSeconds;
   249	
   250	                var wholeUnits = Mathf.FloorToInt(acc);
   251	                if (wholeUnits > 0)
   252	                {
   253	                    AdjustAndNotify(_state.Stockpile.RawResources, rate.Resource, wholeUnits);
   254	                    acc -= wholeUnits;
   255	                    Debug.Log($"[SectStateProvider] Gathered +{wholeUnits} {rate.Resource} (task={accKey})");
   256	                }
   257	
   258	                _gatherAccumulators[accKey] = acc;
   259	            }
   260	        }
   261	
   262	        // Disciple crafting: whoever's CurrentTask matches a known recipe
   263	        // accumulates progress; once a craft completes, consumes the raw
   264	        // resource cost and produces the item - into the sect stockpile for
   265	        // ordinary disciples, or straight into personal inventory for
   266	        // Elder+ (matches the ownership rule from the economy design:
   267	        // outer/inner disciples craft for the sect, elders keep their own).
   268	        // Called from ResourceCraftingSystem.Tick(). Disciples whose task
   269	        // fails the building requirement are SKIPPED — progress is HELD at
   270	        // its current value exactly like the out-of-materials path (never
   271	        // reset), and CurrentTask is never rewritten here.
   272	        public void TickCrafting(float deltaTimeSeconds)
   273	        {
   274	            var placedDefIds = CollectPlacedDefIds(); // once per tick, not per disciple
   275	            foreach (var disciple in _state.Disciples)
   276	            {
   277	                if (!IsTaskAvailableWithBuildings(disciple.CurrentTask, placedDefIds, out _)) continue;
   278	                if (!CraftingRecipes.TryGetValue(disciple.CurrentTask, out var recipe)) continue;
   279	
   280	                var progress = _craftProgress.TryGetValue(disciple.DiscipleId, out var existing) ? existing : 0f;
   281	                progress += deltaTimeSeconds;
   282	
   283	                if (progress < recipe.CraftSeconds) 
   284	                {
   285	                    _craftProgress[disciple.DiscipleId] = progress;
   286	                    continue;
   287	                }
   288	
   289	                if (!TryConsume(_state.Stockpile.RawResources, recipe.Costs))
   290	                {
   291	                    // Ready to complete but not enough raw resources - hold
   292	                    // at the completion threshold and wait rather than
   293	                    // losing the accumulated progress or overshooting.
   294	                    _craftProgress[disciple.DiscipleId] = recipe.CraftSeconds;
   295	                    continue;
   296	                }
   297	
   298	                foreach (var (resource, amount) in recipe.Costs)
   299	                {
   300	                    var newTotal = _state.Stockpile.RawResources.TryGetValue(resource, out var v) ? v : 0;
   301	                    _resourceChangedPublisher.Publish(new SectResourceChangedMessage
   302	                    {
   303	                        ResourceId = resource,
   304	                        Delta = -amount,
   305	                        NewTotal = newTotal,
   306	                    });
   307	                }
   308	
   309	                var item = new InventoryItem
   310	                {
   311	                    ItemDefId = recipe.ItemDefId,
   312	                    Quantity = 1,
   313	                    Grade = recipe.Grade,
   314	                    OwnerScope = disciple.Rank >= DiscipleRank.Elder ? OwnerScope.Personal : OwnerScope.SectStockpile,
   315	                };
   316	
   317	                if (item.OwnerScope == OwnerScope.Personal)
   318	                {
   319	                    disciple.PersonalInventory.Add(item);
   320	                }
   321	                else
   322	                {
   323	                    AddToStockpileGoods(item);
   324	                }
   325	
   326	                Debug.Log($"[SectStateProvider] {disciple.DisplayName} crafted {item.ItemDefId} " +
   327	                          $"(grade {item.Grade}, {item.OwnerScope})");
   328	
   329	                // Carry over any overshoot instead of resetting to exactly 0.
   330	                _craftProgress[disciple.DiscipleId] = progress - recipe.CraftSeconds;
   331	            }
   332	        }
   333	
   334	        // Adds a new Outer Disciple assigned to a gathering task, round-robin
   335	        // across GatheringTasks so recruits don't all pile onto one resource.
   336	        public void RecruitOuterDisciple(DiscipleSex sex = DiscipleSex.Unspecified)
   337	        {
   338	            var index = _state.Disciples.Count;
   339	            var task = GatheringTasks[index % GatheringTasks.Length];
   340	            var name = RecruitNamePool[index % RecruitNamePool.Length];
   341	
   342	            // Default parity: even index → Male, odd → Female (preserves existing mixed-roster look)
   343	            var resolvedSex = (sex != DiscipleSex.Unspecified) ? sex
   344	                : (index % 2 == 0) ? DiscipleSex.Male : DiscipleSex.Female;
   345	
   346	            var disciple = new DiscipleState
   347	            {
   348	                DiscipleId = $"d{index + 1:000}",
   349	                DisplayName = name,
   350	                Rank = DiscipleRank.OuterDisciple,
   351	                Wallet = new CurrencyWallet(),
   352	                PersonalInventory = new List<InventoryItem>(),
   353	                CurrentTask = task,
   354	                Sex = resolvedSex,
   355	                Avatar = CreateStarterAvatar(index, resolvedSex),
   356	            };
   357	
   358	            _state.Disciples.Add(disciple);
   359	            _discipleRecruitedPublisher.Publish(new DiscipleRecruitedMessage
   360	            {
   361	                DiscipleId = disciple.DiscipleId,
   362	                DisplayName = disciple.DisplayName,
   363	            });
   364	
   365	            Debug.Log($"[SectStateProvider] Recruited outer disciple: {disciple.DisplayName} ({disciple.DiscipleId}), sex={resolvedSex}, assigned to {task}");
   366	        }
   367	
   368	        private AvatarAppearance CreateStarterAvatar(int rosterIndex, DiscipleSex sex)
   369	        {
   370	            var a = new AvatarAppearance();
   371	            a.SetSlot(AvatarSlots.Body, "body_robe_grey");
   372	            a.SetSlot(AvatarSlots.Head, (sex == DiscipleSex.Female) ? "head_female_01" : "head_male_01");
   373	            a.SetSlot(AvatarSlots.Hair, StarterHair[rosterIndex % StarterHair.Length]);
   374	            return a;
   375	        }
   376	
   377	        public bool TryChangeAvatarPart(string discipleId, string slot, string partId,
   378	                                        out string failReason, out AvatarAppearance result)
   379	        {
   380	            failReason = string.Empty;
   381	            result = null;
   382	
   383	            var disciple = _state.Disciples.FirstOrDefault(d => d.DiscipleId == discipleId);
   384	            if (disciple == null) { failReason = $"No disciple with id: {discipleId}"; return false; }
   385	
   386	            if (System.Array.IndexOf(AvatarSlots.Equippable, slot) < 0)
   387	            { failReason = $"Invalid slot: {slot}"; return false; }
   388	
   389	            if (!_avatarPartPool.IsValidForSlot(slot, partId))
   390	            { failReason = $"PartId '{partId}' is not valid for slot '{slot}'"; return false; }
   391	
   392	            // Pose validation: reject a part whose poseId is non-empty and
   393	            // differs from the disciple's effective pose.
   394	            if (!string.IsNullOrEmpty(partId))
   395	            {
   396	                var partDef = _avatarPartPool.GetById(partId);
   397	                if (partDef != null && !string.IsNullOrEmpty(partDef.poseId))
   398	                {
   399	                    string effectivePose = (disciple.Avatar != null && !string.IsNullOrEmpty(disciple.Avatar.PoseId))
   400	                        ? disciple.Avatar.PoseId
   401	                        : "pose_idle_01";
   402	                    if (partDef.poseId != effectivePose)
   403	                    {
   404	                        failReason = $"Part '{partId}' requires pose '{partDef.poseId}' but disciple is in pose '{effectivePose}'.";
   405	                        return false;
   406	                    }
   407	                }
   408	            }
   409	
   410	            // Validation ชั้น 5 — coverage: part ต้องมี art อย่างน้อย 1 backend ที่เปิดใช้
   411	            // (empty layer = เจตนา "ถอดออก" ผ่านเสมอ — *_none defaults)
   412	            // Face split (Roadmap #1): face sub-layers เป็น portrait-only ตามดีไซน์ (R4 —
   413	            // chibi เก็บ feature baked-in) จึงผ่านด้วย Portrait เดี่ยว โดยไม่ต้องมี chibi/spine art
   414	            // (แก้ latent bug: acc_none — ชิ้น "ถอดเครื่องประดับ" ที่มีแต่ chibi art — เคยโดน reject)
   415	            if (!string.IsNullOrEmpty(partId))
   416	            {
   417	                var partDef = _avatarPartPool.GetById(partId);
   418	                if (partDef != null && !partDef.IsEmptyLayer)
   419	                {
   420	                    bool anyCovered =
   421	                        partDef.Supports(VisualBackend.Portrait) ||
   422	                        (_visualConfig != null && _visualConfig.SpriteSheetEnabled &&
   423	                         partDef.Supports(VisualBackend.SpriteSheet)) ||
   424	                        (_visualConfig != null && _visualConfig.SpineEnabled &&
   425	                         partDef.Supports(VisualBackend.Spine));
   426	                    if (!anyCovered)
   427	                    {
   428	                        failReason = $"Part '{partId}' has no art on any enabled backend (coverage check).";
   429	                        return false;
   430	                    }
   431	                }
   432	            }
   433	
   434	            // Validation ชั้น 6 — entitlement (Phase 5, §8): part ที่ติด entitlement
   435	            // ต้องผ่าน IVisualEntitlementProvider เท่านั้น — ต่างจากชั้น 5 ที่ป้องกัน
   436	            // "render ไม่ได้" ชั้นนี้ป้องกัน "ไม่มีสิทธิ์ใช้" — failReason เขียนให้
   437	            // AI/UI อ่านแล้วเข้าใจเหตุผล (ตามสเปก Phase 5)
   438	            if (!string.IsNullOrEmpty(partId) && _entitlementProvider != null)
   439	            {
   440	                var entitlementDef = _avatarPartPool.GetById(partId);
   441	                if (entitlementDef != null && !string.IsNullOrEmpty(entitlementDef.entitlement) &&
   442	                    !_entitlementProvider.CanUse(discipleId, entitlementDef.entitlement))
   443	                {
   444	                    failReason = $"Part '{partId}' requires entitlement '{entitlementDef.entitlement}'.";
   445	                    return false;
   446	                }
   447	            }
   448	
   449	            if (disciple.Avatar == null) disciple.Avatar = new AvatarAppearance();
   450	
   451	            var oldPart = disciple.Avatar.GetSlot(slot);
   452	            disciple.Avatar.SetSlot(slot, partId);
   453	
   454	            _avatarChangedPublisher.Publish(new AvatarEquipmentChangedMessage
   455	            {
   456	                DiscipleId = discipleId,
   457	                Slot       = slot,
   458	                OldPartId  = oldPart,
   459	                NewPartId  = partId
   460	            });
   461	
   462	            result = disciple.Avatar.Clone();   // return copy, not reference to live state
   463	            return true;
   464	        }
   465	
   466	        // ---------- P4 (local ownership test harness) — validated ownership assignment ----------
   467	        // Non-Npc OwnerId convention: a real Twitch user id is numeric, so the
   468	        // synthetic local-identity convention is "viewer_*" / "player_*" (alpha
   469	        // prefix + underscore, no whitespace) — clearly fake, never shaped like a
   470	        // real Twitch id. Real authentication (P5B+) replaces this entirely.
   471	        private const string OwnerIdPattern = "^[A-Za-z][A-Za-z0-9]*(_[A-Za-z0-9]+)*$";
   472	
   473	        public bool TrySetDiscipleOwner(string discipleId, DiscipleOwnerType ownerType,
   474	                                        string ownerId, out string failReason)
   475	        {
   476	            failReason = string.Empty;
   477	
   478	            // 1. disciple must exist
   479	            var disciple = FindDisciple(discipleId);
   480	            if (disciple == null)
   481	            {
   482	                failReason = $"No disciple with id: {discipleId}";
   483	                return false;
   484	            }
   485	
   486	            // 2. enum value must be defined (fail-closed on out-of-range casts)
   487	            if (!Enum.IsDefined(typeof(DiscipleOwnerType), ownerType))
   488	            {
   489	                failReason = $"Undefined DiscipleOwnerType value: {ownerType}";
   490	                return false;
   491	            }
   492	
   493	            var trimmedOwnerId = (ownerId ?? string.Empty).Trim();
   494	
   495	            // 3. Npc normalizes OwnerId to empty (no orphaned ids on Npc rows)
   496	            if (ownerType == DiscipleOwnerType.Npc)
   497	            {
   498	                if (!string.IsNullOrEmpty(trimmedOwnerId))
   499	                {
   500	                    failReason = "Npc ownership must carry an empty OwnerId.";
   501	                    return false;
   502	                }
   503	                trimmedOwnerId = string.Empty;
   504	            }
   505	            else
   506	            {
   507	                // 4. non-Npc identities satisfy the identity convention
   508	                if (string.IsNullOrEmpty(trimmedOwnerId) ||
   509	                    !System.Text.RegularExpressions.Regex.IsMatch(trimmedOwnerId, OwnerIdPattern))
   510	                {
   511	                    failReason = $"OwnerId '{trimmedOwnerId}' does not satisfy the identity convention " +
   512	                                 "(alpha prefix, [A-Za-z0-9_], no whitespace).";
   513	                    return false;
   514	                }
   515	            }
   516	
   517	            // 5. a Viewer identity must not control a different active disciple
   518	            if (ownerType == DiscipleOwnerType.Viewer && !string.IsNullOrEmpty(trimmedOwnerId))
   519	            {
   520	                for (int i = 0; i < _state.Disciples.Count; i++)
   521	                {
   522	                    var other = _state.Disciples[i];
   523	                    if (other == null || other.DiscipleId == discipleId) continue;
   524	                    if (other.OwnerType == DiscipleOwnerType.Viewer && other.OwnerId == trimmedOwnerId)
   525	                    {
   526	                        failReason = $"Viewer '{trimmedOwnerId}' already controls '{other.DiscipleId}'.";
   527	                        return false;
   528	                    }
   529	                }
   530	            }
   531	
   532	            // 6. no-op → no mutation, no message. A re-bind/reclaim of the SAME
   533	            // owner (P5B) still refreshes that owner's activity — it is a valid
   534	            // owner command, and it keeps an active player outside the override window.
   535	            if (disciple.OwnerType == ownerType && disciple.OwnerId == trimmedOwnerId)
   536	            {
   537	                if (ownerType == DiscipleOwnerType.Viewer)
   538	                    SyncViewerRegistryForBind(disciple, trimmedOwnerId);
   539	                return true;
   540	            }
   541	
   542	            // --- validation complete: single mutation block (no partial writes) ---
   543	            var oldType = disciple.OwnerType;
   544	            var oldOwnerId = disciple.OwnerId;
   545	            disciple.OwnerType = ownerType;
   546	            disciple.OwnerId = trimmedOwnerId;
   547	
   548	            // P5B: registry travels with the disciple row — release the previous
   549	            // viewer record (if any), then activate/bind the new one, so invariant
   550	            // #1/#2/#3 (active ⇔ bound; non-active ⇒ unbound) always holds.
   551	            if (oldType == DiscipleOwnerType.Viewer && !string.IsNullOrEmpty(oldOwnerId))
   552	                SyncViewerRegistryForRelease(oldOwnerId);
   553	            if (ownerType == DiscipleOwnerType.Viewer)
   554	                SyncViewerRegistryForBind(disciple, trimmedOwnerId);
   555	
   556	            _ownershipChangedPublisher?.Publish(new DiscipleOwnerChangedMessage
   557	            {
   558	                DiscipleId = discipleId,
   559	                OldType = oldType,
   560	                OldOwnerId = oldOwnerId,
   561	                NewType = ownerType,
   562	                NewOwnerId = trimmedOwnerId,
   563	            });
   564	            return true;
   565	        }
   566	
   567	        // ---------- P5B — SectViewerRegistry ⇄ disciple ownership sync ----------
   568	        // These are the ONLY writers of membership records, and they run inside the
   569	        // same mutation block as the disciple row, so the two can never disagree.
   570	
   571	        /// <summary>Activate (or create) the active record binding <paramref name="viewerId"/> to this disciple and stamp activity.</summary>
   572	        private void SyncViewerRegistryForBind(DiscipleState disciple, string viewerId)
   573	        {
   574	            var record = _state.ViewerRegistry.Find(viewerId);
   575	            if (record == null)
   576	            {
   577	                record = new ViewerRecord { ViewerId = viewerId, DisplayName = viewerId };
   578	                _state.ViewerRegistry.Records.Add(record);
   579	            }
   580	            record.Status = ViewerMembershipStatus.Active;
   581	            record.BoundDiscipleId = disciple.DiscipleId;
   582	            record.LastActiveAtUtc = _clock.UtcNow;
   583	        }
   584	
   585	        /// <summary>Release a record that no longer owns a disciple (kept as Left, never left bound — invariant #3).</summary>
   586	        private void SyncViewerRegistryForRelease(string viewerId)
   587	        {
   588	            var record = _state.ViewerRegistry.Find(viewerId);
   589	            if (record == null) return;
   590	            record.Status = ViewerMembershipStatus.Left;
   591	            record.BoundDiscipleId = string.Empty;
   592	        }
   593	
   594	        // ---------- P3 (Task Assignment UI) — read-only known-task query ----------
   595	        // Same source of truth as TryAssignTask's KnownTasks set — no second list.
   596	        public System.Collections.Generic.IReadOnlyList<string> GetKnownTaskIds()
   597	        {
   598	            return KnownTaskOrder;
   599	        }
   600	
   601	        // ---------- Task System v2 (§6) + P5B (Hybrid Permissions) ----------
   602	        // Validation order: disciple lookup → permission (read-only evaluation,
   603	        // revalidated here) → known task → building gate → no-op → cooldown → commit.
   604	        // Nothing mutates until every check passes (no partial mutation).
   605	        // This method never touches Stockpile.RawResources or any progress store.
   606	        // Public so read-only observers (bridge protection query, UI) use the SAME
   607	        // requester id the authority checks — no second spelling can drift.
   608	        public const string SectMasterRequesterId = "SECT_MASTER";
   609	
   610	        /// <summary>
   611	        /// P5B — how long a viewer owner is protected from a SectMaster override,
   612	        /// measured in REAL time on the injected clock. 10 minutes per the agreed
   613	        /// policy; the override requires inactivity STRICTLY greater than this
   614	        /// (exactly 10 minutes still counts as active → protected).
   615	        /// </summary>
   616	        private const double ViewerProtectionWindowSeconds = 600d;
   617	
   618	        /// <summary>
   619	        /// P5B — prototype balance value: minimum real-time gap between two ACTUAL
   620	        /// task changes on the same disciple. Configurable via
   621	        /// <see cref="TaskChangeCooldownSeconds"/> (0 disables it). A no-op request
   622	        /// (task already current) never checks or consumes it. This throttles task
   623	        /// thrash only — it does NOT guarantee crafting completion.
   624	        /// </summary>
   625	        public const float DefaultTaskChangeCooldownSeconds = 12f;
   626	
   627	        /// <summary>P5B — configurable real-time cooldown for actual task changes (see the const above). 0 disables.</summary>
   628	        public float TaskChangeCooldownSeconds { get; set; } = DefaultTaskChangeCooldownSeconds;
   629	
   630	        public bool TryAssignTask(string requesterId, string discipleId, string taskId, out string failReason)
   631	        {
   632	            failReason = string.Empty;
   633	
   634	            var disciple = FindDisciple(discipleId);
   635	            if (disciple == null)
   636	            {
   637	                failReason = $"No disciple with id: {discipleId}";
   638	                return false;
   639	            }
   640	
   641	            // 1. Permission — same evaluation the read-only UI query exposes
   642	            // (single authority, so display and commit can never disagree).
   643	            var permission = EvaluateTaskPermission(requesterId, disciple);
   644	            if (!permission.Allowed)
   645	            {
   646	                failReason = permission.Reason;
   647	                return false;
   648	            }
   649	
   650	            // 2. Known task + building requirement (no mutation on failure).
   651	            if (string.IsNullOrEmpty(taskId) || !KnownTasks.Contains(taskId))
   652	            {
   653	                failReason = $"Unknown task: '{taskId}'.";
   654	                return false;
   655	            }
   656	
   657	            if (!IsTaskAvailable(taskId, out failReason))
   658	                return false;
   659	
   660	            var trimmedRequester = (requesterId ?? string.Empty).Trim();
   661	
   662	            // 3. No-op BEFORE the cooldown: asking for the task already assigned is a
   663	            // valid no-op — no progress reset, no DiscipleTaskChangedMessage, and it
   664	            // must not consume or be blocked by the task-change cooldown. It may
   665	            // refresh the requester's OWN activity (never another viewer's).
   666	            if (disciple.CurrentTask == taskId)
   667	            {
   668	                RefreshRequesterActivity(trimmedRequester);
   669	                return true;
   670	            }
   671	
   672	            // 4. Cooldown on actual changes (per disciple, real time).
   673	            if (TaskChangeCooldownSeconds > 0f &&
   674	                _taskChangeLastAtUtc.TryGetValue(discipleId, out var lastChangeUtc))
   675	            {
   676	                var elapsed = (_clock.UtcNow - lastChangeUtc).TotalSeconds;
   677	                if (elapsed < TaskChangeCooldownSeconds)
   678	                {
   679	                    var remaining = TaskChangeCooldownSeconds - elapsed;
   680	                    failReason = $"Task change for '{discipleId}' is on cooldown " +
   681	                                 $"({TaskChangeCooldownSeconds:0.#}s) — {remaining:0.0}s remaining.";
   682	                    return false;
   683	                }
   684	            }
   685	
   686	            // --- validation complete: single mutation block (no partial writes) ---
   687	            disciple.CurrentTask = taskId;
   688	            _taskChangeLastAtUtc[discipleId] = _clock.UtcNow;
   689	            RefreshRequesterActivity(trimmedRequester);
   690	
   691	            _discipleTaskChangedPublisher.Publish(new DiscipleTaskChangedMessage
   692	            {
   693	                DiscipleId = discipleId,
   694	                TaskId = taskId,
   695	            });
   696	
   697	            Debug.Log($"[SectStateProvider] {trimmedRequester} assigned task '{taskId}' to {discipleId}.");
   698	            return true;
   699	        }
   700	
   701	        // ---------- P5B — permission authority (read-only; never mutates) ----------
   702	        // Rules, in precedence order:
   703	        //   0. empty/invalid requester → Denied (fail closed).
   704	        //   1. missing/contradictory membership data for a Viewer-owned disciple →
   705	        //      ConsistencyError (NEVER automatic permission), regardless of requester.
   706	        //   2. "SECT_MASTER": Npc (unowned) / Player (single-player identity
   707	        //      convention) → Allowed. Viewer-owned → Allowed only when the owner has
   708	        //      been inactive for strictly more than the protection window.
   709	        //   3. any other requester → only its own valid Active membership.
   710	        // Invalid/future owner timestamps are handled conservatively: the owner is
   711	        // treated as recently active, so protection holds (fail closed).
   712	        // There is deliberately NO AI-GM bypass: the trusted GM sends the same
   713	        // SectMasterRequesterId and follows the identical rule.
   714	        private TaskPermissionResult EvaluateTaskPermission(string requesterId, DiscipleState disciple)
   715	        {
   716	            var trimmedRequester = (requesterId ?? string.Empty).Trim();
   717	            if (trimmedRequester.Length == 0)
   718	                return TaskPermissionResult.Denied("Requester id is empty.");
   719	
   720	            bool isMaster = trimmedRequester == SectMasterRequesterId;
   721	            if (!isMaster && !IsValidIdentity(trimmedRequester))
   722	            {
   723	                return TaskPermissionResult.Denied(
   724	                    $"Requester id '{trimmedRequester}' does not satisfy the identity convention " +
   725	                    "(alpha prefix, [A-Za-z0-9_], no whitespace).");
   726	            }
   727	
   728	            // Membership data must be complete and agree before ANY decision about a
   729	            // Viewer-owned disciple — a gap is a consistency error, never a grant.
   730	            string consistencyReason = null;
   731	            ViewerRecord ownerRecord = null;
   732	
   733	            if (disciple.OwnerType == DiscipleOwnerType.Viewer)
   734	            {
   735	                if (!_state.ViewerRegistry.IsInternallyConsistent())
   736	                {
   737	                    consistencyReason = $"the viewer registry is internally inconsistent (disciple '{disciple.DiscipleId}').";
   738	                }
   739	                else
   740	                {
   741	                    ownerRecord = _state.ViewerRegistry.FindByBoundDisciple(disciple.DiscipleId);
   742	                    if (ownerRecord == null)
   743	                    {
   744	                        consistencyReason = $"disciple '{disciple.DiscipleId}' is viewer-owned but has no active membership record.";
   745	                    }
   746	                    else if (ownerRecord.Status != ViewerMembershipStatus.Active)
   747	                    {
   748	                        consistencyReason = $"membership record for '{ownerRecord.ViewerId}' is not active.";
   749	                    }
   750	                    else if (ownerRecord.ViewerId != disciple.OwnerId)
   751	                    {
   752	                        consistencyReason = $"disciple '{disciple.DiscipleId}' owner id disagrees with its membership record.";
   753	                    }
   754	                    else if (!ownerRecord.IsSelfConsistent() || !ownerRecord.HasValidIdentityConvention())
   755	                    {
   756	                        consistencyReason = $"membership record for '{ownerRecord.ViewerId}' is not self-consistent.";
   757	                    }
   758	                }
   759	            }
   760	
   761	            if (isMaster)
   762	            {
   763	                if (disciple.OwnerType == DiscipleOwnerType.Viewer)
   764	                {
   765	                    if (consistencyReason != null)
   766	                        return TaskPermissionResult.ConsistencyError(consistencyReason);
   767	
   768	                    return EvaluateMasterOverride(ownerRecord, disciple);
   769	                }
   770	
   771	                // Npc = unowned (SectMaster may control) and Player = the existing
   772	                // single-player identity convention (SectMaster may control).
   773	                return TaskPermissionResult.Allow();
   774	            }
   775	
   776	            // Non-master: only the disciple's own valid Active membership.
   777	            if (disciple.OwnerType != DiscipleOwnerType.Viewer)
   778	            {
   779	                return TaskPermissionResult.Denied(
   780	                    $"'{trimmedRequester}' is not allowed to assign tasks to '{disciple.DiscipleId}'.");
   781	            }
   782	
   783	            if (consistencyReason != null)
   784	                return TaskPermissionResult.ConsistencyError(consistencyReason);
   785	
   786	            if (ownerRecord.ViewerId != trimmedRequester)
   787	            {
   788	                return TaskPermissionResult.Denied(
   789	                    $"'{trimmedRequester}' is not allowed to assign tasks to '{disciple.DiscipleId}' " +
   790	                    $"(owned by '{disciple.OwnerId}').");
   791	            }
   792	
   793	            var requesterRecord = _state.ViewerRegistry.Find(trimmedRequester);
   794	            if (requesterRecord == null)

... [TRUNCATED: แสดง 40000 จาก 67942 bytes — ขอส่วนที่เหลือผ่าน need_files]
````

### DataTables/luban.conf (287 bytes)
````
     1	{
     2	    "groups":
     3	    [
     4	        {"names":["c"], "default":true}
     5	    ],
     6	    "schemaFiles":
     7	    [
     8	        {"fileName":"Defines/schema.xml", "type":""}
     9	    ],
    10	    "dataDir": "Data",
    11	    "targets":
    12	    [
    13	        {"name":"client", "manager":"Tables", "groups":["c"], "topModule":"cfg"}
    14	    ]
    15	}
````

### DataTables/Defines/schema.xml (960 bytes)
````
     1	<?xml version="1.0" encoding="utf-8"?>
     2	<module name="game">
     3	    <bean name="EventDef">
     4	        <var name="id" type="string"/>
     5	        <var name="description" type="string"/>
     6	        <var name="requiresDecision" type="bool"/>
     7	        <var name="weight" type="float"/>
     8	    </bean>
     9	    <table name="TbEventDef" value="EventDef" input="EventDef.csv" index="id"/>
    10	
    11	    <!-- Two flat tables joined by eventId instead of nesting choices as a
    12	         list-in-one-row inside EventDef. Two flat, bulk-editable CSVs
    13	         joined by a plain string key are simpler and safer to hand-author,
    14	         and still solve the original goal (no per-item Editor asset
    15	         clicking). -->
    16	    <bean name="EventChoiceDef">
    17	        <var name="eventId" type="string"/>
    18	        <var name="choiceId" type="string"/>
    19	        <var name="label" type="string"/>
    20	    </bean>
    21	    <table name="TbEventChoiceDef" value="EventChoiceDef" input="EventChoiceDef.csv" mode="list"/>
    22	</module>
````

### UnityProject/Assets/Resources/Data/building_defs.json (2754 bytes)
````
     1	{
     2	    "buildings": [
     3	        {
     4	            "Id": "herb_plot",
     5	            "DisplayName": "แปลงสมุนไพร",
     6	            "Category": "Production",
     7	            "GridWidth": 2,
     8	            "GridHeight": 2,
     9	            "Cost": [ { "ResourceId": "wood", "Amount": 20 } ],
    10	            "Rotatable": false,
    11	            "SpritePath": "",
    12	            "ThumbnailPath": ""
    13	        },
    14	        {
    15	            "Id": "pill_hall",
    16	            "DisplayName": "หอปรุงยา",
    17	            "Category": "Production",
    18	            "GridWidth": 3,
    19	            "GridHeight": 3,
    20	            "Cost": [ { "ResourceId": "wood", "Amount": 40 }, { "ResourceId": "ore", "Amount": 30 } ],
    21	            "Rotatable": false,
    22	            "SpritePath": "",
    23	            "ThumbnailPath": ""
    24	        },
    25	        {
    26	            "Id": "rest_pavilion",
    27	            "DisplayName": "ศาลาพักสำราญ",
    28	            "Category": "Convenience",
    29	            "GridWidth": 2,
    30	            "GridHeight": 2,
    31	            "Cost": [ { "ResourceId": "wood", "Amount": 30 } ],
    32	            "Rotatable": true,
    33	            "SpritePath": "",
    34	            "ThumbnailPath": ""
    35	        },
    36	        {
    37	            "Id": "spirit_market",
    38	            "DisplayName": "ตลาดหินเซียน",
    39	            "Category": "Shop",
    40	            "GridWidth": 3,
    41	            "GridHeight": 2,
    42	            "Cost": [ { "ResourceId": "wood", "Amount": 50 }, { "ResourceId": "provisions", "Amount": 25 } ],
    43	            "Rotatable": true,
    44	            "SpritePath": "",
    45	            "ThumbnailPath": ""
    46	        },
    47	        {
    48	            "Id": "stone_lantern",
    49	            "DisplayName": "โคมหิน",
    50	            "Category": "Landscape",
    51	            "GridWidth": 1,
    52	            "GridHeight": 1,
    53	            "Cost": [ { "ResourceId": "ore", "Amount": 10 } ],
    54	            "Rotatable": false,
    55	            "SpritePath": "",
    56	            "ThumbnailPath": ""
    57	        },
    58	        {
    59	            "Id": "spirit_pond",
    60	            "DisplayName": "บ่อน้ำเซียน",
    61	            "Category": "Landscape",
    62	            "GridWidth": 2,
    63	            "GridHeight": 3,
    64	            "Cost": [ { "ResourceId": "ore", "Amount": 15 }, { "ResourceId": "provisions", "Amount": 10 } ],
    65	            "Rotatable": true,
    66	            "SpritePath": "",
    67	            "ThumbnailPath": ""
    68	        },
    69	        {
    70	            "Id": "forge",
    71	            "DisplayName": "หรานตีดาบ",
    72	            "Category": "Production",
    73	            "GridWidth": 2,
    74	            "GridHeight": 2,
    75	            "Cost": [ { "ResourceId": "wood", "Amount": 30 }, { "ResourceId": "ore", "Amount": 20 } ],
    76	            "Rotatable": true,
    77	            "SpritePath": "",
    78	            "ThumbnailPath": ""
    79	        }
    80	    ]
    81	}
````

### UnityProject/Assets/Resources/Data/visual_overrides.json (783 bytes)
````
     1	{
     2	  "overrides": [
     3	    {
     4	      "discipleId": "d000",
     5	      "skeletonDataResourcePath": "Avatar/Spine/male/1113103_1",
     6	      "activityToAnimation": [
     7	        { "activity": "Idle",     "animation": "idle1" },
     8	        { "activity": "Walking",  "animation": "walk" },
     9	        { "activity": "Walk",     "animation": "walk" },
    10	        { "activity": "Running",  "animation": "run" }
    11	      ]
    12	    },
    13	    {
    14	      "discipleId": "d002",
    15	      "skeletonDataResourcePath": "Avatar/Spine/female/1123102_1_SkeletonData",
    16	      "activityToAnimation": [
    17	        { "activity": "Idle",     "animation": "idle1" },
    18	        { "activity": "Walking",  "animation": "walk" },
    19	        { "activity": "Walk",     "animation": "walk" },
    20	        { "activity": "Running",  "animation": "run" }
    21	      ]
    22	    }
    23	  ]
    24	}
````

### LLMWiki/wiki/index.md (4943 bytes)
````
     1	---
     2	title: Wiki Index
     3	type: index
     4	sources: []
     5	related:
     6	  - "[[overview]]"
     7	  - "[[conventions]]"
     8	created: 2026-08-31
     9	updated: 2026-08-31
    10	confidence: high
    11	tags: [index, navigation]
    12	---
    13	
    14	# Wiki Index
    15	
    16	> Master catalog for the Cultivation Together knowledge base.
    17	> **Read this first** on every query — then drill into relevant pages.
    18	
    19	## Project
    20	
    21	- [[overview|Project Overview]] — high-level summary, tech stack, what's done
    22	- [[conventions|Conventions]] — workflow rules and preferences
    23	
    24	## Sources (Human-maintained)
    25	
    26	Design documents and historical record:
    27	
    28	- [[sources/game-design-doc|Game Design Document]] — vision, pillars, two-currency economy
    29	- [[sources/architecture|Architecture]] — how the subsystems fit together
    30	- [[sources/mechanics|Mechanics]] — per-system design (combat, gathering, crafting, ...)
    31	- [[sources/avatar-appearance|Avatar Customization (Sprite Swap)]] — Heads / Hairs / Bodies / Accessories, sprite-slot data model
    32	- [[sources/sex-gender-system|Sex / Gender for Disciples]] — implemented `[Key(7)] DiscipleSex.Sex` + sex-aware starter avatar (UI selector ยังไม่ทำ)
    33	- [[sources/devlog-history|DevLog History]] — 13 lab rounds, 21-30 Aug 2026
    34	- [[sources/bug-log|Bug Log]] — real bugs + fixes from all labs
    35	- [[sources/open-questions|Open Questions]] — what's deliberately not decided
    36	- [[sources/visual-demo-scene|Visual Demo Scene (DEV-ONLY)]] — DiscipleVisualSystem Phase 3 demo บน Spine example rig + DevSpineOverride seam + guards
    37	- [[decisions/visual-overrides-straight-alpha|Visual Overrides — Straight-Alpha Conversion]] — ADR: atlas ของ rig เฉพาะตัวละคร (Q5) เป็น straight alpha บน Linear color space + วิธี re-export จาก Spine IDE + การเปิด Spine ถาวร (S4 activation — license ยืนยัน 2026-09-25, gate ตั้งที่ composition root)
    38	
    39	## Code Snippets (Important Scripts)
    40	
    41	- [[sources/code-snippets/GameLifetimeScope.cs.md|GameLifetimeScope.cs]] — composition root
    42	- [[sources/code-snippets/SectStateProvider.cs.md|SectStateProvider.cs]] — state aggregator
    43	- [[sources/code-snippets/WorldEventSystem.cs.md|WorldEventSystem.cs]] — event trigger
    44	- [[sources/code-snippets/TimeSystem.cs.md|TimeSystem.cs]] — game clock + await
    45	- [[sources/code-snippets/GameMessages.cs.md|GameMessages.cs]] — all message DTOs
    46	- [[sources/code-snippets/UIPresenter-and-UIViewBase.cs.md|UIPresenter + UIViewBase]] — MVP base
    47	- [[sources/code-snippets/EventPopupPresenter.cs.md|EventPopupPresenter.cs]] — choice click handler
    48	- [[sources/code-snippets/LogWindowPresenter.cs.md|LogWindowPresenter.cs]] — event log presenter
    49	- [[sources/code-snippets/LogWindowView.cs.md|LogWindowView.cs]] — scrolling TMP log view
    50	
    51	## Concepts (AI-maintained)
    52	
    53	System-level concepts:
    54	
    55	- [[concepts/vcontainer-composition|VContainer Composition Root]]
    56	- [[concepts/message-pipe-bus|MessagePipe Bus]]
    57	- [[concepts/mcp-bridge|MCP Bridge]]
    58	- [[concepts/decision-pipeline|Decision Pipeline]]
    59	- [[concepts/state-management|State Management]]
    60	- [[concepts/time-system|Time System]]
    61	- [[concepts/world-events|World Events]]
    62	- [[concepts/gathering-system|Gathering System]]
    63	- [[concepts/crafting-system|Crafting System]]
    64	- [[concepts/purchase-store|Purchase Store]]
    65	- [[concepts/mvp-ui|MVP UI Pattern]]
    66	- [[concepts/log-window|Log Window]]
    67	- [[concepts/data-pipeline|Data Pipeline (Luban)]]
    68	
    69	## Entities (AI-maintained)
    70	
    71	Catalogs of game entities:
    72	
    73	- [[entities/disciples|Disciples]] — characters
    74	- [[entities/world-events|World Events]] — current event catalog
    75	- [[entities/events|Events]] — event timeline + flow
    76	- [[entities/resources|Resources]] — raw materials
    77	- [[entities/items|Items]] — crafted goods
    78	- [[entities/sects|Sects]] — faction concept (player + others)
    79	- [[entities/avatar-appearance|Avatar Appearance]] — dictionary Parts/Colors schema, layered rendering, customization UI (implemented)
    80	
    81	## Quick Reference — Common Questions
    82	
    83	| Question | Start here |
    84	|---|---|
    85	| How does the AI play the game? | [[concepts/mcp-bridge]] |
    86	| How do world events work? | [[concepts/world-events]] → [[entities/world-events]] |
    87	| How is state stored? | [[concepts/state-management]] |
    88	| How do decisions apply? | [[concepts/decision-pipeline]] |
    89	| How do I add a new event? | [[concepts/data-pipeline]] → [[entities/world-events]] |
    90	| How do I add a new UI panel? | [[concepts/mvp-ui]] |
    91	| How does the event log work? | [[concepts/log-window]] |
    92	| How do I add a new resource? | [[entities/resources]] |
    93	| What bugs were hit during build? | [[sources/bug-log]] |
    94	| What's not decided yet? | [[sources/open-questions]] |
    95	| What was built in each lab? | [[sources/devlog-history]] |
    96	
    97	## Maintenance
    98	
    99	- Last index update: 2026-09-25
   100	- Total source pages: 11 (+ 9 code snippets)
   101	- Total concept pages: 14
   102	- Total entity pages: 7
   103	- Total bug log entries: 11 (across 13 labs)
````

### LLMWiki/wiki/conventions.md (3924 bytes)
````
     1	---
     2	title: Conventions
     3	type: conventions
     4	sources: []
     5	related: []
     6	created: 2026-08-31
     7	updated: 2026-08-31
     8	confidence: high
     9	tags: [workflow, preferences]
    10	---
    11	
    12	# Conventions
    13	
    14	> Your workflow rules and preferences. The LLM agent reads this to understand
    15	> how you like to work. Edit this freely — the agent will pick up changes.
    16	
    17	## Communication Style
    18	
    19	- **Language**: Mixed Thai/English (Thai for prose, English for technical terms)
    20	- **Response format**: Concise, bulleted, technical
    21	- **Code style**: Opinionated recommendations, not "it depends" — pick one
    22	  approach and justify it
    23	- **Tone**: Direct, no fluff. When uncertain, say so explicitly.
    24	
    25	## Coding Preferences
    26	
    27	- **C# version**: 8.0 compatible (Unity 6.3 default — no `record`/`init`/`global using`)
    28	- **Naming**: PascalCase for public, `_camelCase` for private fields (Unity convention)
    29	- **Architecture**:
    30	  - Composition over inheritance
    31	  - ScriptableObject for design-time data, plain C# for runtime state
    32	  - MessagePack (NOT protobuf — dropped in lab 7)
    33	  - Luban for bulk design-time data, not hand-created ScriptableObjects
    34	- **Open-source first**: every dependency in the project is OSS or Unity built-in
    35	  - Exception (approved 2026-09-25, S4): `Spine-Unity` runtime (Esoteric Software) เป็น dependency ของ visual system Tier-2 — license ยืนยันแล้วว่าครอบคลุม ([[decisions/visual-overrides-straight-alpha]]); example assets ของ Esoteric ห้ามตกค้างใน build จริง; asmdef แยกเพื่อสลับไป Unity 2D Animation ได้หากจำเป็น
    36	- **No reflection** in UI panel resolution (enum→Type mapping instead)
    37	
    38	## Wiki Usage
    39	
    40	- **Always read `wiki/index.md` first** when answering queries
    41	- **Always update `wiki/index.md` and `wiki/log.md`** on every operation
    42	- **Discuss key takeaways** before mass-updating wiki pages
    43	- **Cite sources** using `[[wikilinks]]` AND include the actual C# file path
    44	  in backticks, e.g. `` `Assets/Scripts/Systems/SectStateProvider.cs:177` ``
    45	- **Ask before** creating new top-level categories
    46	- **Code is the source of truth** — if a design doc disagrees with code, code
    47	  wins; flag the doc for update, don't change code silently
    48	
    49	## Code Reference Convention
    50	
    51	When explaining a system, link to the actual file with line number:
    52	
    53	```markdown
    54	Implemented in `Assets/Scripts/Systems/SectStateProvider.cs:177` —
    55	`RecruitOuterDisciple()` publishes `DiscipleRecruitedMessage` on success.
    56	```
    57	
    58	## DevLog Cadence
    59	
    60	- Write a devlog entry at the end of each work session in Thai
    61	- Format: `devlog-YYYY-MM-DD.md` in `wiki/sources/`
    62	- Include: what was done, what's next, any blockers, bugs found
    63	- Round-up entries (`devlog-history.md`) summarize 2+ sessions at a time
    64	
    65	## Bug Tracking
    66	
    67	- Every bug → entry in `wiki/sources/bug-log.md`
    68	- Format: date, repro, root cause, fix, related files, lessons learned
    69	- After fix, agent should update the relevant concept page
    70	- Bugs from the original lab rounds are preserved with `lab-N` prefix
    71	
    72	## MCP / Bridge Debugging
    73	
    74	When asking about MCP-related issues, include:
    75	- Which MCP tool (`get_sect_state` / `await_next_world_event` / `execute_decision` / `purchase_item`)
    76	- Unity console log
    77	- McpBridge console log
    78	- Whether Unity Editor's Pause button is engaged (separate from `TimeSystem.IsPaused`)
    79	- Whether Unity's `Run In Background` is enabled in Player Settings
    80	
    81	## Open Design Questions (don't assume, ask)
    82	
    83	See `[[sources/open-questions]]` for the full list. When asked about:
    84	- Combat → ask ACS-style vs Rimworld-style
    85	- Stat system → propose something simple, don't import a complex framework
    86	- Sect war → remind this is WIP, suggest deferring
    87	- World event conditions → mention that current impl is weighted-random, no state logic
    88	- Shop prices / event consequences → mention these are placeholders pending real balance
````

### LLMWiki/wiki/sources/architecture.md (19143 bytes)
````
     1	---
     2	title: Architecture
     3	type: architecture
     4	sources:
     5	  - ../../project_summary.md
     6	  - ../../README.md
     7	related:
     8	  - "[[sources/game-design-doc]]"
     9	  - "[[concepts/mcp-bridge]]"
    10	  - "[[concepts/vcontainer-composition]]"
    11	  - "[[concepts/message-pipe-bus]]"
    12	created: 2026-08-31
    13	updated: 2026-09-04
    14	confidence: high
    15	tags: [architecture, di, ipc, messagepipe]
    16	---
    17	
    18	# Architecture
    19	
    20	How the subsystems fit together. The single most important doc to read
    21	before touching any system.
    22	
    23	## High-Level Diagram
    24	
    25	```
    26	┌─────────────────────────────────────────────────────────────┐
    27	│  Unity (Host)                                               │
    28	│                                                             │
    29	│  ┌──────────────────┐    ┌──────────────────────────────┐   │
    30	│  │ GameLifetimeScope│    │  MessagePipe Bus             │   │
    31	│  │ (VContainer DI)  │───▶│  - in-memory pub/sub         │   │
    32	│  └──────────────────┘    │  - TCP interprocess to Bridge│   │
    33	│                          └──────────────────────────────┘   │
    34	│                                      ▲                      │
    35	│                                      ▼                      │
    36	│  ┌──────────────────────────────────────────────┐           │
    37	│  │ Subsystems (VContainer entry points)         │           │
    38	│  │  - TimeSystem (IStartable, ITickable)        │           │
    39	│  │  - DiscipleSystem (ITickable)                │           │
    40	│  │  - ResourceCraftingSystem (ITickable)        │           │
    41	│  │  - WorldEventSystem (ITickable)              │           │
    42	│  │  - BuildingSystem (ITickable, STUB)          │           │
    43	│  │  - DecisionLogger (IStartable)               │           │
    44	│  └──────────────────────────────────────────────┘           │
    45	│           │                                                  │
    46	│           ▼                                                  │
    47	│  ┌──────────────────────────────────────────────┐           │
    48	│  │  ISectStateProvider (single live state)      │           │
    49	│  │  - SectStateProvider                         │           │
    50	│  │    - BuildSectEconomyState()                 │           │
    51	│  │    - ApplyDecisionConsequence()              │           │
    52	│  │    - TickGathering() / TickCrafting()        │           │
    53	│  │    - RecruitOuterDisciple()                  │           │
    54	│  │    - TryPurchaseItem()                       │           │
    55	│  │    - TryChangeAvatarPart(slot, partId)       │           │
    56	│  └──────────────────────────────────────────────┘           │
    57	│           │                                                  │
    58	│           ▼                                                  │
    59	│  ┌──────────────────────────────────────────────┐           │
    60	│  │  Data: LubanEventPool (Excel → JSON → C#)    │           │
    61	│  │  Resources/DataTables/worldevent_*.json      │           │
    62	│  │  Data: AvatarPartPool (JSON → C#)            │           │
    63	│  │  Resources/Data/avatar_parts.json            │           │
    64	│  └──────────────────────────────────────────────┘     
    65	│                                                             │
    66	│  ┌──────────────────────────────────────────────┐           │
    67	│  │ UI (Xianxia.UI.MVP Lite — UGUI)              │           │
    68	│  │  - UIRoot (Canvas)                           │           │
    69	│  │  - UIPanelCatalog (ScriptableObject)         │           │
    70	│  │  - EventPopup (View + Presenter)             │           │
    71	│  │  - WalletHud (View + Presenter)              │           │
    72	│  │  - LogWindow (View + Presenter)              │           │
    73	│  │  - AvatarCustomization (View + Presenter,    │           │
    74	│  │    + AvatarRenderer layered sprites,         │           │
    75	│  │      slot-based / parts-only)   🔸           │           │
    76	│  └──────────────────────────────────────────────┘           │
    77	└─────────────────────────────────────────────────────────────┘
    78	          │ TCP 127.0.0.1:3215 (MessagePipe.Interprocess)
    79	          ▼
    80	┌─────────────────────────────────────────────────────────────┐
    81	│  McpBridge (.NET 8 console app)                             │
    82	│  - IRemoteRequestHandler / IPublisher → MCP tools           │
    83	│  - Tools: get_sect_state, await_next_world_event,           │
    84	│           execute_decision, purchase_item                   │
    85	│  - stdio → ModelContextProtocol SDK                         │
    86	└─────────────────────────────────────────────────────────────┘
    87	          │ stdio
    88	          ▼
    89	┌─────────────────────────────────────────────────────────────┐
    90	│  AI GM Client (e.g. Open-LLM-VTuber)                        │
    91	│  - Reads state, decides, calls tools                        │
    92	└─────────────────────────────────────────────────────────────┘
    93	```
    94	
    95	## Scene Pattern
    96	
    97	เกมใช้ **Additive Scene Loading** แทน Single Scene:
    98	
    99	- **CoreScene** (persistent) — Canvas + UIRoot, GameLifetimeScope,
   100	  core systems (TimeSystem, SectStateProvider, MessagePipe),
   101	  EventSystem, AudioListener, MainCamera → **never unloads**
   102	- **GameplayScene** (transient) — Environment, NPCs, Buildings,
   103	  scene-specific UI (EventPopup, Dialogue) → loads additive, unload/reload ได้
   104	
   105	**Scene Transition Flow:** CoreScene (Single) → GameplayScene (Additive) → ...
   106	
   107	## DI Registration
   108	
   109	```csharp
   110	// Data (Luban migration replacing ScriptableObject)
   111	builder.RegisterInstance(new LubanEventPool());
   112	
   113	// 🔸 Avatar part definitions loaded from Resources/Data/avatar_parts.json
   114	//    (parts-only table — outfit table ถูกยกเลิก, ดู sources/avatar-appearance §1)
   115	builder.Register<AvatarPartPool>(Lifetime.Singleton);
   116	
   117	// UI (Xianxia.UI.MVP Lite)
   118	builder.RegisterInstance(uiRoot);
   119	builder.RegisterInstance(uiPanelCatalog);
   120	builder.Register<Xianxia.Sect.UI.UIService>(Lifetime.Singleton);
   121	builder.Register<DecisionExecutor>(Lifetime.Singleton);
   122	builder.Register<Xianxia.Sect.UI.EventPopupPresenter>(Lifetime.Transient);
   123	builder.Register<Xianxia.Sect.UI.WalletHudPresenter>(Lifetime.Transient);
   124	builder.Register<Xianxia.Sect.UI.LogWindowPresenter>(Lifetime.Transient);
   125	builder.Register<Xianxia.Sect.UI.AvatarCustomizationPresenter>(Lifetime.Transient);
   126	builder.RegisterEntryPoint<Xianxia.Sect.UI.WorldEventUISystem>(Lifetime.Singleton);
   127	builder.RegisterEntryPoint<Xianxia.Sect.UI.UIBootstrap>(Lifetime.Singleton);
   128	
   129	// MessagePipe bus + TCP interprocess
   130	var options = builder.RegisterMessagePipe(pipeOptions => { ... });
   131	var messagePipeBuilder = builder.ToMessagePipeBuilder();
   132	var interprocess = messagePipeBuilder.AddTcpInterprocess("127.0.0.1", 3215, ...);
   133	
   134	// Interprocess-registered message brokers (pub/sub across process)
   135	messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, DiscipleRecruitedMessage>(interprocess);
   136	messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, SectResourceChangedMessage>(interprocess);
   137	// ...
   138	
   139	// Interprocess-registered request/response pairs
   140	messagePipeBuilder.RegisterTcpRemoteRequestHandler<SectStateQuery, SectStateSnapshot>(interprocess);
   141	messagePipeBuilder.RegisterTcpRemoteRequestHandler<AwaitWorldEventRequest, AwaitWorldEventResponse>(interprocess);
   142	messagePipeBuilder.RegisterTcpRemoteRequestHandler<PurchaseItemRequest, PurchaseItemResponse>(interprocess);
   143	messagePipeBuilder.RegisterTcpRemoteRequestHandler<ChangeAvatarPartRequest, ChangeAvatarPartResponse>(interprocess);
   144	builder.RegisterAsyncRequestHandler<ChangeAvatarPartRequest, ChangeAvatarPartResponse, ChangeAvatarPartHandler>(options);
   145	
   146	// Interprocess pub/sub: broadcast avatar equipment changes to UI + MCP client
   147	messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, AvatarEquipmentChangedMessage>(interprocess);
   148	
   149	// Subsystems (entry points)
   150	builder.RegisterEntryPoint<TimeSystem>(Lifetime.Singleton).AsSelf();
   151	builder.RegisterEntryPoint<DiscipleSystem>(Lifetime.Singleton).AsSelf();
   152	builder.RegisterEntryPoint<ResourceCraftingSystem>(Lifetime.Singleton).AsSelf();
   153	builder.RegisterEntryPoint<WorldEventSystem>(Lifetime.Singleton).AsSelf();
   154	builder.RegisterEntryPoint<DecisionLogger>(Lifetime.Singleton).AsSelf();
   155	builder.RegisterEntryPoint<Xianxia.Sect.UI.WorldEventUISystem>(Lifetime.Singleton);
   156	builder.RegisterEntryPoint<Xianxia.Sect.UI.UIBootstrap>(Lifetime.Singleton);
   157	
   158	// State aggregator
   159	builder.Register<ISectStateProvider, SectStateProvider>(Lifetime.Singleton);
   160	
   161	// MessagePipe bus + TCP interprocess
   162	var options = builder.RegisterMessagePipe(pipeOptions => { /* ... */ });
   163	var messagePipeBuilder = builder.ToMessagePipeBuilder();
   164	var interprocess = messagePipeBuilder.AddTcpInterprocess("127.0.0.1", 3215, /* ... */);
   165	```
   166	📎 Source: `Assets/Scripts/Core/GameLifetimeScope.cs`
   167	
   168	## Interprocess Contracts (Avatar) 🔸
   169	
   170	| Contract | รูปแบบ | สถานะ |
   171	|---|---|---|
   172	| `AvatarEquipmentChangedMessage` | pub/sub | ✅ ใช้งานอยู่ |
   173	| `ChangeAvatarPartRequest/Response` | request-response | ✅ registered, bridge tool `change_avatar_part` กำลังทำ |
   174	| ~~`AvatarOutfitChangedMessage`~~ | — | ❌ **ยกเลิก** |
   175	| ~~`ApplyOutfitRequest/Response`~~ | — | ❌ **ยกเลิก** |
   176	
   177	> **กฎการเลือก transport:** สิ่งที่ agent ต้องรู้ผลทันที (สำเร็จ/ล้มเหลว + reason)
   178	> ใช้ **request-response** เสมอ; สิ่งที่เป็นการแจ้งเตือน UI ใช้ **pub/sub**
   179	> การเปลี่ยน avatar part ต้องการ guaranteed feedback → request-response
   180	
   181	## Key Architectural Decisions (with rationale)
   182	
   183	### Why VContainer (not Zenject)
   184	- Lighter, faster, modern API
   185	- Direct support for entry points (`IStartable`, `ITickable`) without writing bootstrap code
   186	
   187	### Why MessagePipe (not UnityEvents / native C# events)
   188	- Zero-alloc pub/sub
   189	- Type-safe (no string-based event names)
   190	- Built-in request-response
   191	- Same API for in-memory and interprocess (TCP)
   192	- VContainer integration built-in
   193	
   194	### Why TCP Interprocess (not named pipes / shared memory)
   195	- Standard, well-tested by MessagePipe team
   196	- Cross-platform (Windows / macOS / Linux)
   197	- Easy to debug with Wireshark if needed
   198	- Unity = server, McpBridge = client (clear ownership)
   199	
   200	### Why MessagePack (not Protobuf) — decided in lab 7
   201	- Protobuf assumed "state must cross language boundaries" — never materialized
   202	  since bridge is C# too
   203	- MessagePack simpler, faster, no codegen pipeline
   204	- Already in dependency graph (for other reasons)
   205	- **Reversed**: dropped protobuf permanently, also dropped from Luban pipeline
   206	
   207	### Why Luban (not ScriptableObject) — for event data
   208	- Events are tabular (id, description, weight, requiresDecision) — perfect
   209	  for Excel
   210	- No clicking to create `.asset` files
   211	- One source of truth in `DataTables/Data/event.xlsx`
   212	- Version-controllable (Excel diffs in git work fine)
   213	
   214	### Why UGUI (not UI Toolkit)
   215	- Faster to wire up for MVP
   216	- More mature ecosystem / tutorials
   217	- UI Toolkit still in transition
   218	- Future-migration possible if needed
   219	
   220	### Why MVP-Lite (not MVVM, not pure View-MonoBehaviour)
   221	- Clean separation: View = MonoBehaviour (Unity lifecycle), Presenter = plain
   222	  C# (testable, fast)
   223	- No reflection for panel resolution (explicit enum→Type mapping)
   224	- Disposable subscriptions prevent memory leaks
   225	- Reference design: CycloneGames UIFramework
   226	
   227	### Why Per-Process Pub/Sub is BANNED inside Unity
   228	**Critical lesson** (lab 13 bug fix): `EventPopupPresenter` originally
   229	published via in-memory `IPublisher<ExecuteDecisionMessage>`, but
   230	`DecisionLogger` only listened on `IDistributedSubscriber<string,
   231	ExecuteDecisionMessage>` (the interprocess channel). Different graphs = silent
   232	no-op when clicking choice buttons. **Fixed** by extracting `DecisionExecutor`
   233	and having both paths call it directly.
   234	
   235	```csharp
   236	public class DecisionExecutor
   237	{
   238	    public void Execute(string eventId, string choiceId)
   239	    {
   240	        _stateProvider.ApplyDecisionConsequence(eventId, choiceId);
   241	        _timeSystem.SetPaused(false);
   242	
   243	        // Both DecisionLogger (bridge) and EventPopupPresenter (UI)
   244	        // funnel through here
   245	        _decisionExecutedPublisher.Publish(new DecisionExecutedMessage { EventId = eventId, ChoiceId = choiceId });
   246	    }
   247	}
   248	```
   249	> 📎 Source: Assets/Scripts/Core/DecisionExecutor.cs
   250	
   251	> **Rule**: in-process pub/sub in Unity is for events that have no Unity-side
   252	> listener beyond the publisher itself. Anything that needs to actually
   253	> trigger another in-Unity system should be a direct method call.
   254	
   255	## What Crosses the TCP Boundary
   256	
   257	**Pub/Sub (fire-and-forget, bridge can listen but doesn't have to):**
   258	- `DiscipleRecruitedMessage`
   259	- `DiscipleRankChangedMessage`
   260	- `SectResourceChangedMessage`
   261	- `ContributionEarnedMessage`
   262	- `ExecuteDecisionMessage` (bridge → Unity only)
   263	- `AvatarEquipmentChangedMessage` (Unity → bridge/UI, topic `sect.avatar_equipment_changed`)
   264	
   265	**Request/Response (bridge asks, Unity answers):**
   266	- `SectStateQuery` → `SectStateSnapshot` (full state snapshot)
   267	- `AwaitWorldEventRequest` → `AwaitWorldEventResponse` (block until next event)
   268	- `PurchaseItemRequest` → `PurchaseItemResponse` (atomic buy)
   269	- `ChangeAvatarPartRequest` → `ChangeAvatarPartResponse` (validate + mutate avatar part; wired Unity-side, bridge tool not yet exposed)
   270	
   271	**Not registered on interprocess (in-memory only):**
   272	- `TimeSpeedChangedMessage` (UI internal)
   273	- `WorldEventTriggeredMessage` (UI internal — bridge uses request-response instead)
   274	
   275	### Why `WorldEventTriggeredMessage` is NOT interprocess
   276	
   277	From `GameMessages.cs:89-102` — confirmed by reading `TcpDistributedSubscriber`
   278	source: it calls `worker.StartReceiver()` (binds its own listening socket)
   279	**regardless of `HostAsServer`** value. So a non-hub process (bridge) can
   280	never safely `IDistributedSubscriber` over this TCP transport — it would
   281	collide with the hub's already-bound port. Solution: use request-response
   282	(`AwaitWorldEventRequest`) which only ever uses the client connection
   283	(`Connect`, not `Listen`).
   284	
   285	## Implementation Details for AI Agent
   286	- **Namespace:** `Xianxia.Sect` (สำหรับ Data Model), `Xianxia.Sect.UI` (สำหรับ Renderer/View)
   287	- **File Location:** 
   288	  - Data Model: `UnityProject/Assets/Scripts/Shared/SectEconomyState.cs` (แก้ไขไฟล์เดิม)
   289	  - Renderer: `UnityProject/Assets/Scripts/UI/Views/AvatarRenderer.cs` (สร้างใหม่)
   290	  - Def Loader: `UnityProject/Assets/Scripts/Data/AvatarPartPool.cs` (สร้างใหม่)
   291	- **Avatar data flow:** `AvatarPartPool` (JSON def-table) → `AvatarRenderer` (resolve `AvatarAppearance` → sprite layers) → `AvatarCustomizationPresenter/View` (แต่งตัวผ่าน `TryChangeAvatarPart`) — รายละเอียดครบที่ [[entities/avatar-appearance]]
   292	
   293	## Scene Management
   294	
   295	### Additive Scene Pattern
   296	เกมใช้ **Additive Scene Loading** แทน Single Scene:
   297	
   298	- **CoreScene** (persistent):
   299	  - Canvas + UIRoot (persistent UI)
   300	  - GameLifetimeScope (VContainer root)
   301	  - Core systems (TimeSystem, SectStateProvider, MessagePipe)
   302	  - EventSystem, AudioListener, MainCamera
   303	  - Never unloads
   304	
   305	- **GameplayScene** (transient):
   306	  - Environment, NPCs, Buildings
   307	  - Scene-specific UI (EventPopup, Dialogue)
   308	  - Loads additive, can be unloaded/reloaded
   309	
   310	**Scene Transition Flow:**
   311	1. CoreScene loads (Single mode)
   312	2. GameplayScene loads (Additive mode)
   313	3. To change scene: Unload old GameplayScene → Load new GameplayScene
   314	4. CoreScene (and persistent UI) survives throughout
   315	
   316	See [[additive-scene-architecture]] for implementation details.
   317	
   318	## Workspace Layout (multi-project monorepo)
   319	
   320	```
   321	Cultivation Together/              ← workspace root
   322	├── Shared/                        ← canonical source for shared types
   323	│   ├── GameMessages.cs            ←   sync to Unity + Bridge
   324	│   ├── SectEconomyState.cs
   325	│   └── MockSectData.cs
   326	├── UnityProject/                  ← open in Unity Hub
   327	│   ├── Assets/Scripts/            ← all game C# logic
   328	│   └── ...
   329	├── McpBridge/                     ← .NET 8 console app
   330	│   └── Program.cs                 ← MCP server
   331	├── DataTables/                    ← Luban Excel sources
   332	│   ├── Data/event.xlsx
   333	│   ├── Data/event_choice.xlsx
   334	│   └── Defines/worldevent.xml
   335	├── Tools/Luban/                   ← Luban binary
   336	├── LLMWiki/                       ← this folder
   337	├── project_summary.md             ← THE design log (Thai)
   338	├── README.md                      ← workspace overview
   339	└── XIANXIA_UI_MVP_LITE_SPEC.md    ← UI spec
   340	```
   341	
   342	**Rule**: edit files in top-level `Shared/`, then run `./sync-shared.sh` to
   343	copy into both `UnityProject/Assets/Scripts/Shared/` and `McpBridge/Shared/`.
   344	Never edit the copies directly.
   345	
   346	
   347	
   348	## Related Pages
   349	
   350	- [[concepts/vcontainer-composition]]
   351	- [[concepts/message-pipe-bus]]
   352	- [[concepts/mcp-bridge]]
   353	- [[concepts/decision-pipeline]]
   354	- [[concepts/mvp-ui]] — Xianxia.UI.MVP Lite
   355	- [[concepts/data-pipeline]] — Luban
   356	- [[entities/avatar-appearance]] 🔸````

### LLMWiki/wiki/sources/open-questions.md (7418 bytes)
````
     1	---
     2	title: Open Questions
     3	type: design
     4	sources:
     5	  - ../../project_summary.md
     6	related:
     7	  - "[[sources/game-design-doc]]"
     8	  - "[[sources/mechanics]]"
     9	created: 2026-08-31
    10	updated: 2026-08-31
    11	confidence: high
    12	tags: [open-questions, design, deferred]
    13	---
    14	
    15	# Open Questions
    16	
    17	> Design decisions intentionally not yet made. When asked about these, do
    18	> not assume — propose options with tradeoffs, let the human decide.
    19	
    20	## 1. Final Core Loop
    21	
    22	**Status**: deliberately not fixed (per project_summary.md)
    23	**Why waiting**: need to finalize gameplay systems first
    24	**Current MVP shape**:
    25	```
    26	[Sect State] → [Event Trigger] → [Choices] → [Decision] →
    27	[Consequences] → [Check End] → loop
    28	```
    29	15s event interval, auto-pause on decisions, 1x default speed
    30	
    31	**When asked**: ask what gameplay system they're working on first; core loop
    32	will follow from those constraints
    33	
    34	## 2. Combat System
    35	
    36	**Status**: ❓ not decided
    37	**Options on the table**:
    38	- **ACS-style (Amazing Cultivation Simulator)**: faster resolve, fewer
    39	  per-fight decisions, more encounters per session
    40	- **Rimworld-style**: more granular, micro-positioning, slower resolve
    41	
    42	**Project lean** (per project_summary.md): ACS-style — "resolve เร็วกว่า
    43	เหมาะกับ AI ตัดสินใจ" (resolves faster, suitable for AI decision-making)
    44	
    45	**When asked**: lean toward ACS-style, but ask if they want one round of
    46	combat to be one decision or multiple
    47	
    48	## 3. Stat System
    49	
    50	**Status**: ❓ not decided
    51	**Open**: what stats? cultivation-only (realm/HP/Qi), or RPG-style (STR/DEX/INT/etc.)?
    52	**Lean**: keep it xianxia-flavored (realm + technique mastery, not D&D stats)
    53	
    54	## 4. Techniques / Manuals
    55	
    56	**Status**: ❓ not decided
    57	**Open**: how does a disciple learn a technique? (item consumption, time-based study, teacher assignment?)
    58	
    59	## 5. Items / Weapons / Armor / Elixirs (detailed)
    60	
    61	**Status**: ❓ not decided
    62	**Current**: only 2 placeholder items (`elixir_qi_gathering` grade 3,
    63	`sword_azure_flame` grade 5)
    64	**Open**: rarity tiers, set bonuses, durability, soul-binding, etc.
    65	
    66	## 6. Sect War
    67	
    68	**Status**: ⏳ WIP
    69	**Open**: is this a real feature or aspirational? (not in current MVP scope)
    70	
    71	## 7. Spirit Stone Robbery Between Disciples
    72	
    73	**Status**: ❓ mentioned in design, not in schema
    74	**Open**: is this gameplay-relevant or world-building? If gameplay, how is
    75	it triggered (random event? betrayal arc?)
    76	
    77	## 8. BuildingSystem
    78	
    79	**Status**: ✅ Phase 1 implemented (grid placement engine) — 26 Sep 2026
    80	**What changed**: ตาม `sources/building-system.md` — วางอาคารลง grid ได้
    81	(ไม่ใช่ "คลิกสร้างอย่างเดียว" อีกต่อไป): เลือกจาก BuildingMenu (4 หมวด) →
    82	ghost preview บน isometric grid → ✓ ยืนยันหักทรัพยากรผ่าน `AdjustAndNotify`
    83	→ `PlacedBuildingState` ([Key(2)] append-only) + `BuildingGrid` occupancy +
    84	`BuildingPlacedMessage` (in-memory). ไม่มี movement/task-gating (Phase 2)
    85	**Defaults ที่ agent ตัดสินใจแทน (รอ human confirm ก่อน merge)**:
    86	- Q1: JSON เขียนมือ `Resources/Data/building_defs.json` (ย้าย Luban ทีหลังได้)
    87	- Q2: หักจาก `Stockpile.RawResources` เดิม (ไม่สร้าง currency ใหม่)
    88	- Q3: ผู้เล่นเท่านั้น — ไม่เพิ่ม MCP tool และไม่ register interprocess
    89	- Q4: ใช้ `BuildingSystem.cs` เดิมเป็น entry point (แทน stub ว่าง)
    90	- Q5: logical grid 40×40, cell = 1 unit; world mapping ใช้ tile size ที่กล้องวัดจริง (`CameraFramingConfig` + `IsometricCellMath` — ตาม grid overlay เดิม)
    91	- Q6: Landscape กิน grid เหมือนกันหมด (ไม่มี object นอก grid)
    92	**ยังค้าง**: grid จริงกี่ cell ขึ้นกับขนาด art (Q5 ตัวเลข placeholder),
    93	หมุน/ทุบ UI เต็มรูป, task gating (Phase 2)
    94	
    95	## 9. WorldEventSystem — State-Conditional Logic
    96	
    97	**Status**: ⏳ random weighted pick only
    98	**Open**: should events depend on current state? (low herb → more herb
    99	discovery events, high money → more merchant events, etc.)
   100	**When to resume**: when event variety feels low
   101	
   102	## 10. Event Consequences & Shop Pricing — Balance
   103	
   104	**Status**: ⏳ placeholders
   105	**Current**:
   106	- `bandit_raid_001` → -20 provisions
   107	- `herb_garden_bloom` → +30 herb
   108	- `wandering_merchant` → -20 ore, +40 provisions
   109	- `new_disciple_applicant` (accept) → +1 outer disciple
   110	- `purchase_item` → `grade × 50` Contribution
   111	
   112	**When to resume**: after core loop is fixed and playtest starts
   113	
   114	## 11. DiscipleList Panel
   115	
   116	**Status**: ⏳ enum reserved, throws `NotImplementedException`
   117	**Why deferred**: HUD was higher priority; roster view comes after
   118	**When to resume**: after core gameplay feels complete
   119	
   120	## 12. Dedicated WalletChangedMessage
   121	
   122	**Status**: ⏳ `WalletHudPresenter` piggybacks on `SectResourceChangedMessage`
   123	**Tradeoff accepted**: works fine because all wallet-changing events also
   124	change resources (e.g. `purchase_item` deducts contribution after checking
   125	stock)
   126	
   127	## 13. UIRoot.ApplyLayout() String Matching
   128	
   129	**Status**: ✅ resolved 25 Sep 2026 — migrated to
   130	`UIViewBase.ApplyDefaultLayout()` virtual overrides, called from
   131	`UIService.Open()` and `UIRoot.Awake()` (lab 20). No prefab-name matching
   132	left in UIRoot.
   133	
   134	## 14. Save / Load System
   135	
   136	**Status**: ❓ not designed
   137	**Likely path**: serialize `SectEconomyState` via `ToByteArray()`
   138	(MessagePack), write to `Application.persistentDataPath`
   139	**No work on this until** the rest of the gameplay is stable
   140	
   141	## 15. Task Id: `cultivation` vs `meditation`
   142	
   143	**Status**: ⏳ default chosen, not confirmed
   144	**Context**: task-system-v2.md's example uses `"cultivation"`, but the live
   145	roster (`MockSectData` d000) already uses `"meditation"`, and `TaskActivityMapper`
   146	resolves `"meditation"` today.
   147	**Default picked (agent, did not wait)**: the known task set for
   148	`TryAssignTask` = keys of `GatheringRates` + `CraftingRecipes` + `"meditation"`.
   149	So `"cultivation"` is currently **rejected as unknown** until a human decides.
   150	**Question**: is `cultivation` the intended id (rename everywhere), or is
   151	`meditation` canonical and the spec example stale?
   152	**When asked**: treat as a rename decision, not a data-pipeline task.
   153	
   154	## 16. `forge` Building Def — Cost Numbers Are Placeholders
   155	
   156	**Status**: ⏳ def added 8 Oct 2026 for the task building-requirement step
   157	(`gathering_herb` → `herb_plot`, `refining_elixir` → `pill_hall`,
   158	`forging_artifact` → `forge` in SectStateProvider.TaskRequiredBuilding).
   159	**Open**: the cost numbers in `building_defs.json` for `forge`
   160	(wood 30 + ore 20) are **placeholders** — they were picked to exist, not from
   161	design. Display name and 2x2 footprint are equally provisional.
   162	**When asked**: treat as a balance pass, not a data-pipeline task.
   163	
   164	## When In Doubt
   165	
   166	- **Don't assume** — these are deliberately unresolved
   167	- **Propose with tradeoffs** when asked, e.g. "ACS-style would feel snappier
   168	  for AI; Rimworld-style gives more player agency per fight. Which fits your
   169	  design?"
   170	- **Link to this page** when the question is in this list, so the user can
   171	  remember they're tracked
````

### Missing (ไม่พบใน working tree)
- project_summary.md
- AGENTS.md

## File Tree (filtered by .aiignore)

### Files per directory (depth ≤ 5)
```
    7  .
    1  .agents
    1  .agents/skills/animation-create
    1  .agents/skills/animation-get-data
    1  .agents/skills/animation-modify
    1  .agents/skills/animator-create
    1  .agents/skills/animator-get-data
    1  .agents/skills/animator-modify
    1  .agents/skills/assets-copy
    1  .agents/skills/assets-create-folder
    1  .agents/skills/assets-delete
    1  .agents/skills/assets-find
    1  .agents/skills/assets-find-built-in
    1  .agents/skills/assets-get-data
    1  .agents/skills/assets-material-create
    1  .agents/skills/assets-modify
    1  .agents/skills/assets-move
    1  .agents/skills/assets-prefab-close
    1  .agents/skills/assets-prefab-create
    1  .agents/skills/assets-prefab-instantiate
    1  .agents/skills/assets-prefab-open
    1  .agents/skills/assets-prefab-save
    1  .agents/skills/assets-refresh
    1  .agents/skills/assets-shader-get-data
    1  .agents/skills/assets-shader-list-all
    1  .agents/skills/chroma-key-portrait-pipeline
    1  .agents/skills/cinemachine-add-extension
    1  .agents/skills/cinemachine-brain-ensure
    1  .agents/skills/cinemachine-camera-create
    1  .agents/skills/cinemachine-camera-get
    1  .agents/skills/cinemachine-camera-list
    1  .agents/skills/cinemachine-get
    1  .agents/skills/cinemachine-modify
    1  .agents/skills/cinemachine-set-aim
    1  .agents/skills/cinemachine-set-body
    1  .agents/skills/cinemachine-set-default-blend
    1  .agents/skills/cinemachine-set-lens
    1  .agents/skills/cinemachine-set-noise
    1  .agents/skills/cinemachine-set-priority
    1  .agents/skills/cinemachine-set-targets
    1  .agents/skills/console-clear-logs
    1  .agents/skills/console-get-logs
    1  .agents/skills/editor-application-get-state
    1  .agents/skills/editor-application-set-state
    1  .agents/skills/editor-selection-get
    1  .agents/skills/editor-selection-set
    1  .agents/skills/gameobject-component-add
    1  .agents/skills/gameobject-component-destroy
    1  .agents/skills/gameobject-component-get
    1  .agents/skills/gameobject-component-list-all
    1  .agents/skills/gameobject-component-modify
    1  .agents/skills/gameobject-create
    1  .agents/skills/gameobject-destroy
    1  .agents/skills/gameobject-duplicate
    1  .agents/skills/gameobject-find
    1  .agents/skills/gameobject-modify
    1  .agents/skills/gameobject-set-parent
    1  .agents/skills/inputsystem-action-add
    1  .agents/skills/inputsystem-action-remove
    1  .agents/skills/inputsystem-actionmap-add
    1  .agents/skills/inputsystem-actionmap-remove
    1  .agents/skills/inputsystem-asset-create
    1  .agents/skills/inputsystem-binding-add
    1  .agents/skills/inputsystem-binding-composite-add
    1  .agents/skills/inputsystem-binding-remove
    1  .agents/skills/inputsystem-binding-set
    1  .agents/skills/inputsystem-controlscheme-add
    1  .agents/skills/inputsystem-get
    1  .agents/skills/inputsystem-modify
    1  .agents/skills/inputsystem-save
    1  .agents/skills/navigation-agent-add
    1  .agents/skills/navigation-agent-set-destination
    1  .agents/skills/navigation-get
    1  .agents/skills/navigation-link-add
    1  .agents/skills/navigation-list
    1  .agents/skills/navigation-modifier-add
    1  .agents/skills/navigation-modifier-volume-add
    1  .agents/skills/navigation-modify
    1  .agents/skills/navigation-set-bake-settings
    1  .agents/skills/navigation-surface-add
    1  .agents/skills/navigation-surface-bake
    1  .agents/skills/object-get-data
    1  .agents/skills/object-modify
    1  .agents/skills/package-add
    1  .agents/skills/package-list
    1  .agents/skills/package-remove
    1  .agents/skills/package-search
    1  .agents/skills/ping
    1  .agents/skills/ponytail
    1  .agents/skills/pordee
    1  .agents/skills/pordee-stats
    1  .agents/skills/portrait-edit-prompt
    2  .agents/skills/portrait-edit-prompt/assets
    1  .agents/skills/portrait-edit-prompt/scripts
    1  .agents/skills/profiler-capture-frame
    1  .agents/skills/profiler-clear-data
    1  .agents/skills/profiler-enable-module
    1  .agents/skills/profiler-get-memory-stats
    1  .agents/skills/profiler-get-rendering-stats
    1  .agents/skills/profiler-get-script-stats
    1  .agents/skills/profiler-get-status
    1  .agents/skills/profiler-list-modules
    1  .agents/skills/profiler-load-data
    1  .agents/skills/profiler-save-data
    1  .agents/skills/profiler-start
    1  .agents/skills/profiler-stop
    1  .agents/skills/reflection-method-call
    1  .agents/skills/reflection-method-find
    1  .agents/skills/scene-create
    1  .agents/skills/scene-get-data
    1  .agents/skills/scene-list-opened
    1  .agents/skills/scene-open
    1  .agents/skills/scene-save
    1  .agents/skills/scene-set-active
    1  .agents/skills/scene-unload
    1  .agents/skills/screenshot-camera
    1  .agents/skills/screenshot-game-view
    1  .agents/skills/screenshot-isolated
    1  .agents/skills/screenshot-scene-view
    1  .agents/skills/script-delete
    1  .agents/skills/script-execute
    1  .agents/skills/script-read
    1  .agents/skills/script-update-or-create
    1  .agents/skills/tests-run
    1  .agents/skills/tool-set-enabled-state
    1  .agents/skills/type-get-json-schema
    2  .agents/skills/typesafe-ai
    1  .agents/skills/unity-initial-setup
    1  .agents/skills/unity-skill-create
    1  .agents/skills/unity-skill-generate
    1  .agents/skills/unity-tool-list
    1  .freebuff
    2  .opencode
    1  .opencode/skills/chroma-key-portrait-pipeline
    1  .opencode/skills/outfit-swap
    1  .opencode/skills/ponytail
    1  .opencode/skills/pordee
    1  .opencode/skills/pordee-stats
    1  .opencode/skills/portrait-edit-prompt
    2  .opencode/skills/portrait-edit-prompt/assets
    1  .opencode/skills/portrait-edit-prompt/scripts
    2  .zcode
    3  DataTables
    2  DataTables/Data
    1  DataTables/Defines
    2  LLMWiki
    6  LLMWiki/.obsidian
    4  LLMWiki/.obsidian/plugins/karpathywiki
    4  LLMWiki/.obsidian/plugins/smart-connections
    1  LLMWiki/.smart-env
    1  LLMWiki/.smart-env/embedding_models
    1  LLMWiki/.smart-env/event_logs
    1  LLMWiki/.smart-env/smart_blocks
    2  LLMWiki/.smart-env/smart_sources
    4  LLMWiki/wiki
   16  LLMWiki/wiki/concepts
    1  LLMWiki/wiki/decisions
    7  LLMWiki/wiki/entities
    1  LLMWiki/wiki/schema
   17  LLMWiki/wiki/sources
    9  LLMWiki/wiki/sources/code-snippets
    1  LLMWiki/wiki/sources/references
    2  McpBridge
    5  McpBridge/Shared
    6  Shared
   26  Tools/Luban
    1  Tools/Luban/Templates/common/cpp
    1  Tools/Luban/Templates/common/cs
    1  Tools/Luban/Templates/common/dart
    1  Tools/Luban/Templates/common/go
    1  Tools/Luban/Templates/common/java
    1  Tools/Luban/Templates/common/js
    1  Tools/Luban/Templates/common/php
    2  Tools/Luban/Templates/common/rs
    1  Tools/Luban/Templates/common/ts
    5  Tools/Luban/Templates/cpp-rawptr-bin
    5  Tools/Luban/Templates/cpp-sharedptr-bin
    3  Tools/Luban/Templates/cs-bin
    3  Tools/Luban/Templates/cs-dotnet-json
    2  Tools/Luban/Templates/cs-editor-json
    3  Tools/Luban/Templates/cs-newtonsoft-json
    3  Tools/Luban/Templates/cs-simple-json
    1  Tools/Luban/Templates/cs_pb
    3  Tools/Luban/Templates/dart-json
    1  Tools/Luban/Templates/flatbuffers
    1  Tools/Luban/Templates/gdscript-json
    3  Tools/Luban/Templates/go-bin
    3  Tools/Luban/Templates/go-json
    3  Tools/Luban/Templates/java-bin
    3  Tools/Luban/Templates/java-json
    1  Tools/Luban/Templates/javascript-bin
    1  Tools/Luban/Templates/javascript-json
    1  Tools/Luban/Templates/lua-bin
    1  Tools/Luban/Templates/lua-lua
    1  Tools/Luban/Templates/pb
    1  Tools/Luban/Templates/php-json
    1  Tools/Luban/Templates/python-json
    3  Tools/Luban/Templates/rust-bin
    3  Tools/Luban/Templates/rust-json
    1  Tools/Luban/Templates/typescript-bin
    1  Tools/Luban/Templates/typescript-json
    1  Tools/Luban/Templates/typescript-protobuf
   14  Tools/art
    3  UnityProject
    1  UnityProject/.claude/skills/animation-create
    1  UnityProject/.claude/skills/animation-get-data
    1  UnityProject/.claude/skills/animation-modify
    1  UnityProject/.claude/skills/animator-create
    1  UnityProject/.claude/skills/animator-get-data
    1  UnityProject/.claude/skills/animator-modify
    1  UnityProject/.claude/skills/assets-copy
    1  UnityProject/.claude/skills/assets-create-folder
    1  UnityProject/.claude/skills/assets-delete
    1  UnityProject/.claude/skills/assets-find
    1  UnityProject/.claude/skills/assets-find-built-in
    1  UnityProject/.claude/skills/assets-get-data
    1  UnityProject/.claude/skills/assets-material-create
    1  UnityProject/.claude/skills/assets-modify
    1  UnityProject/.claude/skills/assets-move
    1  UnityProject/.claude/skills/assets-prefab-close
    1  UnityProject/.claude/skills/assets-prefab-create
    1  UnityProject/.claude/skills/assets-prefab-instantiate
    1  UnityProject/.claude/skills/assets-prefab-open
    1  UnityProject/.claude/skills/assets-prefab-save
    1  UnityProject/.claude/skills/assets-refresh
    1  UnityProject/.claude/skills/assets-shader-get-data
    1  UnityProject/.claude/skills/assets-shader-list-all
    1  UnityProject/.claude/skills/console-clear-logs
    1  UnityProject/.claude/skills/console-get-logs
    1  UnityProject/.claude/skills/editor-application-get-state
    1  UnityProject/.claude/skills/editor-application-set-state
    1  UnityProject/.claude/skills/editor-selection-get
    1  UnityProject/.claude/skills/editor-selection-set
    1  UnityProject/.claude/skills/gameobject-component-add
    1  UnityProject/.claude/skills/gameobject-component-destroy
    1  UnityProject/.claude/skills/gameobject-component-get
    1  UnityProject/.claude/skills/gameobject-component-list-all
    1  UnityProject/.claude/skills/gameobject-component-modify
    1  UnityProject/.claude/skills/gameobject-create
    1  UnityProject/.claude/skills/gameobject-destroy
    1  UnityProject/.claude/skills/gameobject-duplicate
    1  UnityProject/.claude/skills/gameobject-find
    1  UnityProject/.claude/skills/gameobject-modify
    1  UnityProject/.claude/skills/gameobject-set-parent
    1  UnityProject/.claude/skills/object-get-data
    1  UnityProject/.claude/skills/object-modify
    1  UnityProject/.claude/skills/package-add
    1  UnityProject/.claude/skills/package-list
    1  UnityProject/.claude/skills/package-remove
    1  UnityProject/.claude/skills/package-search
    1  UnityProject/.claude/skills/ping
    1  UnityProject/.claude/skills/profiler-capture-frame
    1  UnityProject/.claude/skills/profiler-clear-data
    1  UnityProject/.claude/skills/profiler-enable-module
    1  UnityProject/.claude/skills/profiler-get-memory-stats
    1  UnityProject/.claude/skills/profiler-get-rendering-stats
    1  UnityProject/.claude/skills/profiler-get-script-stats
    1  UnityProject/.claude/skills/profiler-get-status
    1  UnityProject/.claude/skills/profiler-list-modules
    1  UnityProject/.claude/skills/profiler-load-data
    1  UnityProject/.claude/skills/profiler-save-data
    1  UnityProject/.claude/skills/profiler-start
    1  UnityProject/.claude/skills/profiler-stop
    1  UnityProject/.claude/skills/reflection-method-call
    1  UnityProject/.claude/skills/reflection-method-find
    1  UnityProject/.claude/skills/scene-create
    1  UnityProject/.claude/skills/scene-get-data
    1  UnityProject/.claude/skills/scene-list-opened
    1  UnityProject/.claude/skills/scene-open
    1  UnityProject/.claude/skills/scene-save
    1  UnityProject/.claude/skills/scene-set-active
    1  UnityProject/.claude/skills/scene-unload
    1  UnityProject/.claude/skills/screenshot-camera
    1  UnityProject/.claude/skills/screenshot-game-view
    1  UnityProject/.claude/skills/screenshot-isolated
    1  UnityProject/.claude/skills/screenshot-scene-view
    1  UnityProject/.claude/skills/script-delete
    1  UnityProject/.claude/skills/script-execute
    1  UnityProject/.claude/skills/script-read
    1  UnityProject/.claude/skills/script-update-or-create
    1  UnityProject/.claude/skills/tests-run
    1  UnityProject/.claude/skills/tool-set-enabled-state
    1  UnityProject/.claude/skills/type-get-json-schema
    1  UnityProject/.claude/skills/unity-initial-setup
    1  UnityProject/.claude/skills/unity-skill-create
    1  UnityProject/.claude/skills/unity-skill-generate
    1  UnityProject/.claude/skills/unity-tool-list
    1  UnityProject/.zcode
    3  UnityProject/AgentScripts
    5  UnityProject/Assets
   14  UnityProject/Assets/Editor
    2  UnityProject/Assets/Packages/MessagePack.3.1.8
    1  UnityProject/Assets/Packages/MessagePack.3.1.8/lib
    2  UnityProject/Assets/Packages/MessagePack.Annotations.3.1.8
    1  UnityProject/Assets/Packages/MessagePack.Annotations.3.1.8/lib
    2  UnityProject/Assets/Packages/MessagePackAnalyzer.3.1.8
    4  UnityProject/Assets/Packages/Microsoft.NET.StringTools.17.11.4
    1  UnityProject/Assets/Packages/Microsoft.NET.StringTools.17.11.4/lib
    1  UnityProject/Assets/Packages/Microsoft.NET.StringTools.17.11.4/notices
    7  UnityProject/Assets/Packages/System.Collections.Immutable.8.0.0
    4  UnityProject/Assets/Packages/System.Collections.Immutable.8.0.0/buildTransitive
    1  UnityProject/Assets/Packages/System.Collections.Immutable.8.0.0/lib
    1  UnityProject/Assets/Panel Catalog
    1  UnityProject/Assets/Plugins/MessagePipe.Interprocess
   19  UnityProject/Assets/Plugins/MessagePipe.Interprocess/Runtime
    1  UnityProject/Assets/Plugins/NuGet
   10  UnityProject/Assets/Prefabs
    5  UnityProject/Assets/Prefabs/Avatar
    5  UnityProject/Assets/Prefabs/UI
   36  UnityProject/Assets/Resources/Avatar
    4  UnityProject/Assets/Resources/Avatar/Icons
    3  UnityProject/Assets/Resources/Avatar/Portraits
   12  UnityProject/Assets/Resources/Avatar/Spine
    8  UnityProject/Assets/Resources/Data
   20  UnityProject/Assets/Resources/Data/Arts
    4  UnityProject/Assets/Resources/DataTables
    2  UnityProject/Assets/Resources/Fonts
    1  UnityProject/Assets/Resources/tilemap/map
   46  UnityProject/Assets/Resources/tilemap/tileassets
    5  UnityProject/Assets/Resources/ui/disciple_list
  176  UnityProject/Assets/Resources/ui/icons
    2  UnityProject/Assets/Resources/ui/ink
    8  UnityProject/Assets/Resources/ui/windows
    4  UnityProject/Assets/Scenes
    3  UnityProject/Assets/Scenes/Spikes
    1  UnityProject/Assets/Scenes/VisualDemo
    2  UnityProject/Assets/Scripts
    5  UnityProject/Assets/Scripts/Building
   13  UnityProject/Assets/Scripts/Core
    5  UnityProject/Assets/Scripts/Core/Installers
    2  UnityProject/Assets/Scripts/Data
    9  UnityProject/Assets/Scripts/Data/Gen
    1  UnityProject/Assets/Scripts/Scenes/SectScene
    5  UnityProject/Assets/Scripts/Shared
    5  UnityProject/Assets/Scripts/Systems
    1  UnityProject/Assets/Scripts/Tests
    9  UnityProject/Assets/Scripts/UI/Core
   11  UnityProject/Assets/Scripts/UI/Presenters
    4  UnityProject/Assets/Scripts/UI/Systems
   17  UnityProject/Assets/Scripts/UI/Views
    5  UnityProject/Assets/Scripts/UI/Widgets
   12  UnityProject/Assets/Scripts/Visual/Core
   11  UnityProject/Assets/Scripts/Visual/Scene
   28  UnityProject/Assets/Scripts/Visual/Spikes
    6  UnityProject/Assets/Scripts/Visual/Spine
    3  UnityProject/Assets/Scripts/Visual/Sprite
    3  UnityProject/Assets/Settings
    1  UnityProject/Assets/Settings/Pipeline
    1  UnityProject/Assets/Settings/Scenes
    3  UnityProject/Assets/Spine
    1  UnityProject/Assets/Spine Examples
    6  UnityProject/Assets/Spine Examples/Getting Started
    1  UnityProject/Assets/Spine Examples/Images
   23  UnityProject/Assets/Spine Examples/Other Examples
    2  UnityProject/Assets/Spine Examples/Other Examples/Animation Tester
    4  UnityProject/Assets/Spine Examples/Other Examples/Mix and Match Equip Assets
    2  UnityProject/Assets/Spine Examples/Other Examples/StateMachine SkeletonAnimation
   25  UnityProject/Assets/Spine Examples/Scripts
   10  UnityProject/Assets/Spine Examples/Scripts/Getting Started Scripts
    3  UnityProject/Assets/Spine Examples/Scripts/Mecanim as Logic
    3  UnityProject/Assets/Spine Examples/Scripts/MecanimAnimationMatchModifier
    6  UnityProject/Assets/Spine Examples/Scripts/Mix and Match Character Customize
   29  UnityProject/Assets/Spine Examples/Scripts/Sample Components
    4  UnityProject/Assets/Spine Examples/Sound
    2  UnityProject/Assets/Spine Examples/Spine Skeletons
    9  UnityProject/Assets/Spine Examples/Spine Skeletons/Dragon
    6  UnityProject/Assets/Spine Examples/Spine Skeletons/Eyes
   11  UnityProject/Assets/Spine Examples/Spine Skeletons/FootSoldier
    7  UnityProject/Assets/Spine Examples/Spine Skeletons/Gauge
    7  UnityProject/Assets/Spine Examples/Spine Skeletons/Goblins
   20  UnityProject/Assets/Spine Examples/Spine Skeletons/Hero
    6  UnityProject/Assets/Spine Examples/Spine Skeletons/Raggedy Spineboy
   10  UnityProject/Assets/Spine Examples/Spine Skeletons/Raptor
    8  UnityProject/Assets/Spine Examples/Spine Skeletons/Spineunitygirl
    8  UnityProject/Assets/Spine Examples/Spine Skeletons/Stretchyman
    6  UnityProject/Assets/Spine Examples/Spine Skeletons/mix-and-match
   10  UnityProject/Assets/Spine Examples/Spine Skeletons/raptor-pro-and-mask
   19  UnityProject/Assets/Spine Examples/Spine Skeletons/spineboy-pro
   30  UnityProject/Assets/Spine Examples/Spine Skeletons/spineboy-unity
    9  UnityProject/Assets/Spine Examples/Spine Skeletons/whirlyblendmodes
    1  UnityProject/Assets/Spine/Editor
   82  UnityProject/Assets/Spine/Editor/spine-unity
    1  UnityProject/Assets/Spine/Runtime
   42  UnityProject/Assets/Spine/Runtime/spine-csharp
  116  UnityProject/Assets/Spine/Runtime/spine-unity
   30  UnityProject/Assets/Tests/EditMode
    2  UnityProject/Assets/TextMesh Pro/Fonts
    3  UnityProject/Assets/TextMesh Pro/Resources
    4  UnityProject/Assets/TextMesh Pro/Resources/Fonts & Materials
    1  UnityProject/Assets/TextMesh Pro/Resources/Sprite Assets
    1  UnityProject/Assets/TextMesh Pro/Resources/Style Sheets
   23  UnityProject/Assets/TextMesh Pro/Shaders
    3  UnityProject/Assets/TextMesh Pro/Sprites
    2  UnityProject/Packages
   26  UnityProject/ProjectSettings
    3  UnityProject/UserSettings
    1  UnityProject/UserSettings/Layouts
    3  art
   10  art/disciple_list
    7  art/disciple_list/contact
   25  art/disciple_list/final
    4  art/disciple_list/final_round1
    5  art/disciple_list/guides
    4  art/disciple_list/raw/close_medallion
    4  art/disciple_list/raw/cloud_corner
    4  art/disciple_list/raw/scroll_paper
    4  art/disciple_list/raw/scroll_rod
    4  art/disciple_list/raw/title_plate
    4  art/disciple_list/raw_round1
    2  art/portraits_v2
   18  art/portraits_v2/_view
    5  art/portraits_v2/anchor
    1  art/portraits_v2/female
    3  art/portraits_v2/female/raw/d001
    3  art/portraits_v2/female/raw/d002
    2  art/portraits_v2/female/review
    6  art/portraits_v2/female/workflows
    2  art/portraits_v2/female_round2
    3  art/portraits_v2/female_round2/raw/d001
    4  art/portraits_v2/female_round2/raw/d002
    2  art/portraits_v2/female_round2/review
    6  art/portraits_v2/female_round2/workflows
    1  art/portraits_v2/female_round3
    1  art/portraits_v2/female_round3/raw/d001
    1  art/portraits_v2/female_round3/raw/d002
    3  art/portraits_v2/female_round3/review
    2  art/portraits_v2/female_round3/workflows
    1  art/portraits_v2/female_round4
    1  art/portraits_v2/female_round4/raw/d001
    1  art/portraits_v2/female_round4/raw/d002
    3  art/portraits_v2/female_round4/review
    2  art/portraits_v2/female_round4/workflows
    1  art/portraits_v2/female_round5
    4  art/portraits_v2/female_round5/raw/d001
    2  art/portraits_v2/female_round5/review
    4  art/portraits_v2/female_round5/workflows
    1  art/portraits_v2/female_round6
    4  art/portraits_v2/female_round6/raw/d001
    2  art/portraits_v2/female_round6/review
    4  art/portraits_v2/female_round6/workflows
    1  art/portraits_v2/female_round7
    3  art/portraits_v2/female_round7/raw/d001
    1  art/portraits_v2/female_round7/raw/d002
    2  art/portraits_v2/female_round7/review
    4  art/portraits_v2/female_round7/workflows
    9  art/portraits_v2/final
    1  art/portraits_v2/final/_backup_r3_d002
    1  art/portraits_v2/input
    2  art/portraits_v2/phase1
    2  art/portraits_v2/phase3
    1  art/portraits_v2/phase3/d001
    2  art/portraits_v2/phase3/d001/430101
    1  art/portraits_v2/phase3/d001/430102
    1  art/portraits_v2/phase3/d002
    1  art/portraits_v2/phase3/d002/430201
    1  art/portraits_v2/phase3/d002/430202
    1  art/portraits_v2/phase3/d003
    1  art/portraits_v2/phase3/d003/430301
    1  art/portraits_v2/phase3/d003/430302
    1  art/portraits_v2/review/d001_430102
    1  art/portraits_v2/review/d002_430201
    1  art/portraits_v2/review/d002_430202
    1  art/portraits_v2/review/d003_430301
    1  art/portraits_v2/review/d003_430302
   12  art/portraits_v2/workflows
   22  art/reference
   43  art/reference/female character
    2  art/ui_v1_backup/Assets/Editor
    1  art/ui_v1_backup/Assets/Panel Catalog
    2  art/ui_v1_backup/Assets/Prefabs/UI
   40  art/ui_v1_backup/Assets/Scripts/UI
    1  marooned-wiki
    6  marooned-wiki/.obsidian
    4  marooned-wiki/.obsidian/plugins/karpathywiki
    4  marooned-wiki/.obsidian/plugins/smart-connections
    1  marooned-wiki/.smart-env
    1  marooned-wiki/.smart-env/embedding_models
    1  marooned-wiki/.smart-env/event_logs
    1  marooned-wiki/.smart-env/smart_blocks
    2  marooned-wiki/.smart-env/smart_sources
    1  marooned-wiki/wiki
    1  marooned-wiki/wiki/schema
    1  marooned-wiki/wiki/sources/architecture
    5  marooned-wiki/wiki/sources/code-snippets
    4  scripts
    1  scripts/__pycache__
```

### Source/doc files (.cs .md .sh .csv .xml .conf .asmdef .json)
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
  .agents/skills/ponytail/SKILL.md
  .agents/skills/pordee-stats/SKILL.md
  .agents/skills/pordee/SKILL.md
  .agents/skills/portrait-edit-prompt/SKILL.md
  .agents/skills/portrait-edit-prompt/assets/presets.json
  .agents/skills/portrait-edit-prompt/assets/recipe.example.json
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
  .agents/skills/typesafe-ai/SKILL.md
  .agents/skills/unity-initial-setup/SKILL.md
  .agents/skills/unity-skill-create/SKILL.md
  .agents/skills/unity-skill-generate/SKILL.md
  .agents/skills/unity-tool-list/SKILL.md
  .opencode/AGENT.md
  .opencode/opencode.json
  .opencode/skills/chroma-key-portrait-pipeline/SKILL.md
  .opencode/skills/outfit-swap/SKILL.md
  .opencode/skills/ponytail/SKILL.md
  .opencode/skills/pordee-stats/SKILL.md
  .opencode/skills/pordee/SKILL.md
  .opencode/skills/portrait-edit-prompt/SKILL.md
  .opencode/skills/portrait-edit-prompt/assets/presets.json
  .opencode/skills/portrait-edit-prompt/assets/recipe.example.json
  .zcode/AGENT.md
  .zcode/config.json
  DataTables/Data/EventChoiceDef.csv
  DataTables/Data/EventDef.csv
  DataTables/Defines/schema.xml
  DataTables/gen.sh
  DataTables/luban.conf
  LLMWiki/.obsidian/app.json
  LLMWiki/.obsidian/appearance.json
  LLMWiki/.obsidian/community-plugins.json
  LLMWiki/.obsidian/core-plugins.json
  LLMWiki/.obsidian/graph.json
  LLMWiki/.obsidian/plugins/karpathywiki/data.json
  LLMWiki/.obsidian/plugins/karpathywiki/manifest.json
  LLMWiki/.obsidian/plugins/smart-connections/data.json
  LLMWiki/.obsidian/plugins/smart-connections/manifest.json
  LLMWiki/.obsidian/workspace.json
  LLMWiki/.smart-env/smart_env.json
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
  LLMWiki/wiki/sources/chibi-visual-system.md
  LLMWiki/wiki/sources/code-snippets/EventPopupPresenter.cs.md
  LLMWiki/wiki/sources/code-snippets/GameLifetimeScope.cs.md
  LLMWiki/wiki/sources/code-snippets/GameMessages.cs.md
  LLMWiki/wiki/sources/code-snippets/LogWindowPresenter.cs.md
  LLMWiki/wiki/sources/code-snippets/LogWindowView.cs.md
  LLMWiki/wiki/sources/code-snippets/SectStateProvider.cs.md
  LLMWiki/wiki/sources/code-snippets/TimeSystem.cs.md
  LLMWiki/wiki/sources/code-snippets/UIPresenter-and-UIViewBase.cs.md
  LLMWiki/wiki/sources/code-snippets/WorldEventSystem.cs.md
  LLMWiki/wiki/sources/devlog-history.md
  LLMWiki/wiki/sources/disciple-visual-system-phase0-results.md
  LLMWiki/wiki/sources/disciple-visual-system.md
  LLMWiki/wiki/sources/game-design-doc-draft.md
  LLMWiki/wiki/sources/game-design-doc.md
  LLMWiki/wiki/sources/mechanics.md
  LLMWiki/wiki/sources/open-questions.md
  LLMWiki/wiki/sources/references/chibi-visual-system-marooned.md
  LLMWiki/wiki/sources/sex-gender-system.md
  LLMWiki/wiki/sources/task-system-v2.md
  LLMWiki/wiki/sources/task-system.md
  LLMWiki/wiki/sources/visual-demo-scene.md
  McpBridge/Program.cs
  McpBridge/Shared/GameMessages.cs
  McpBridge/Shared/MessageTypes.cs
  McpBridge/Shared/MockSectData.cs
  McpBridge/Shared/SectEconomyState.cs
  McpBridge/Shared/ViewerMembership.cs
  README.md
  Shared/GameMessages.cs
  Shared/MessageTypes.cs
  Shared/MockSectData.cs
  Shared/SectEconomyState.cs
  Shared/ViewerMembership.cs
  Tools/Luban/Luban.Bson.deps.json
  Tools/Luban/Luban.CSharp.deps.json
  Tools/Luban/Luban.Core.deps.json
  Tools/Luban/Luban.Cpp.deps.json
  Tools/Luban/Luban.Dart.deps.json
  Tools/Luban/Luban.DataLoader.Builtin.deps.json
  Tools/Luban/Luban.DataTarget.Builtin.deps.json
  Tools/Luban/Luban.DataValidator.Builtin.deps.json
  Tools/Luban/Luban.FlatBuffers.deps.json
  Tools/Luban/Luban.Gdscript.deps.json
  Tools/Luban/Luban.Golang.deps.json
  Tools/Luban/Luban.Java.deps.json
  Tools/Luban/Luban.Javascript.deps.json
  Tools/Luban/Luban.L10N.deps.json
  Tools/Luban/Luban.Lua.deps.json
  Tools/Luban/Luban.MsgPack.deps.json
  Tools/Luban/Luban.PHP.deps.json
  Tools/Luban/Luban.Protobuf.deps.json
  Tools/Luban/Luban.Python.deps.json
  Tools/Luban/Luban.Rust.deps.json
  Tools/Luban/Luban.Schema.Builtin.deps.json
  Tools/Luban/Luban.Schema.Builtin.runtimeconfig.json
  Tools/Luban/Luban.Typescript.deps.json
  Tools/Luban/Luban.deps.json
  Tools/Luban/Luban.runtimeconfig.json
  Tools/Luban/nlog.xml
  Tools/art/verify_ink_ui.cs
  UnityProject/.claude/skills/animation-create/SKILL.md
  UnityProject/.claude/skills/animation-get-data/SKILL.md
  UnityProject/.claude/skills/animation-modify/SKILL.md
  UnityProject/.claude/skills/animator-create/SKILL.md
  UnityProject/.claude/skills/animator-get-data/SKILL.md
  UnityProject/.claude/skills/animator-modify/SKILL.md
  UnityProject/.claude/skills/assets-copy/SKILL.md
  UnityProject/.claude/skills/assets-create-folder/SKILL.md
  UnityProject/.claude/skills/assets-delete/SKILL.md
  UnityProject/.claude/skills/assets-find-built-in/SKILL.md
  UnityProject/.claude/skills/assets-find/SKILL.md
  UnityProject/.claude/skills/assets-get-data/SKILL.md
  UnityProject/.claude/skills/assets-material-create/SKILL.md
  UnityProject/.claude/skills/assets-modify/SKILL.md
  UnityProject/.claude/skills/assets-move/SKILL.md
  UnityProject/.claude/skills/assets-prefab-close/SKILL.md
  UnityProject/.claude/skills/assets-prefab-create/SKILL.md
  UnityProject/.claude/skills/assets-prefab-instantiate/SKILL.md
  UnityProject/.claude/skills/assets-prefab-open/SKILL.md
  UnityProject/.claude/skills/assets-prefab-save/SKILL.md
  UnityProject/.claude/skills/assets-refresh/SKILL.md
  UnityProject/.claude/skills/assets-shader-get-data/SKILL.md
  UnityProject/.claude/skills/assets-shader-list-all/SKILL.md
  UnityProject/.claude/skills/console-clear-logs/SKILL.md
  UnityProject/.claude/skills/console-get-logs/SKILL.md
  UnityProject/.claude/skills/editor-application-get-state/SKILL.md
  UnityProject/.claude/skills/editor-application-set-state/SKILL.md
  UnityProject/.claude/skills/editor-selection-get/SKILL.md
  UnityProject/.claude/skills/editor-selection-set/SKILL.md
  UnityProject/.claude/skills/gameobject-component-add/SKILL.md
  UnityProject/.claude/skills/gameobject-component-destroy/SKILL.md
  UnityProject/.claude/skills/gameobject-component-get/SKILL.md
  UnityProject/.claude/skills/gameobject-component-list-all/SKILL.md
  UnityProject/.claude/skills/gameobject-component-modify/SKILL.md
  UnityProject/.claude/skills/gameobject-create/SKILL.md
  UnityProject/.claude/skills/gameobject-destroy/SKILL.md
  UnityProject/.claude/skills/gameobject-duplicate/SKILL.md
  UnityProject/.claude/skills/gameobject-find/SKILL.md
  UnityProject/.claude/skills/gameobject-modify/SKILL.md
  UnityProject/.claude/skills/gameobject-set-parent/SKILL.md
  UnityProject/.claude/skills/object-get-data/SKILL.md
  UnityProject/.claude/skills/object-modify/SKILL.md
  UnityProject/.claude/skills/package-add/SKILL.md
  UnityProject/.claude/skills/package-list/SKILL.md
  UnityProject/.claude/skills/package-remove/SKILL.md
  UnityProject/.claude/skills/package-search/SKILL.md
  UnityProject/.claude/skills/ping/SKILL.md
  UnityProject/.claude/skills/profiler-capture-frame/SKILL.md
  UnityProject/.claude/skills/profiler-clear-data/SKILL.md
  UnityProject/.claude/skills/profiler-enable-module/SKILL.md
  UnityProject/.claude/skills/profiler-get-memory-stats/SKILL.md
  UnityProject/.claude/skills/profiler-get-rendering-stats/SKILL.md
  UnityProject/.claude/skills/profiler-get-script-stats/SKILL.md
  UnityProject/.claude/skills/profiler-get-status/SKILL.md
  UnityProject/.claude/skills/profiler-list-modules/SKILL.md
  UnityProject/.claude/skills/profiler-load-data/SKILL.md
  UnityProject/.claude/skills/profiler-save-data/SKILL.md
  UnityProject/.claude/skills/profiler-start/SKILL.md
  UnityProject/.claude/skills/profiler-stop/SKILL.md
  UnityProject/.claude/skills/reflection-method-call/SKILL.md
  UnityProject/.claude/skills/reflection-method-find/SKILL.md
  UnityProject/.claude/skills/scene-create/SKILL.md
  UnityProject/.claude/skills/scene-get-data/SKILL.md
  UnityProject/.claude/skills/scene-list-opened/SKILL.md
  UnityProject/.claude/skills/scene-open/SKILL.md
  UnityProject/.claude/skills/scene-save/SKILL.md
  UnityProject/.claude/skills/scene-set-active/SKILL.md
  UnityProject/.claude/skills/scene-unload/SKILL.md
  UnityProject/.claude/skills/screenshot-camera/SKILL.md
  UnityProject/.claude/skills/screenshot-game-view/SKILL.md
  UnityProject/.claude/skills/screenshot-isolated/SKILL.md
  UnityProject/.claude/skills/screenshot-scene-view/SKILL.md
  UnityProject/.claude/skills/script-delete/SKILL.md
  UnityProject/.claude/skills/script-execute/SKILL.md
  UnityProject/.claude/skills/script-read/SKILL.md
  UnityProject/.claude/skills/script-update-or-create/SKILL.md
  UnityProject/.claude/skills/tests-run/SKILL.md
  UnityProject/.claude/skills/tool-set-enabled-state/SKILL.md
  UnityProject/.claude/skills/type-get-json-schema/SKILL.md
  UnityProject/.claude/skills/unity-initial-setup/SKILL.md
  UnityProject/.claude/skills/unity-skill-create/SKILL.md
  UnityProject/.claude/skills/unity-skill-generate/SKILL.md
  UnityProject/.claude/skills/unity-tool-list/SKILL.md
  UnityProject/.zcode/config.json
  UnityProject/AgentScripts/DiscipleListLayoutVerify.cs
  UnityProject/AgentScripts/DiscipleListMissingScriptScan.cs
  UnityProject/AgentScripts/DiscipleListTaskBFlow.cs
  UnityProject/Assets/Editor/AvatarIconBaker.cs
  UnityProject/Assets/Editor/AvatarPrefabGenerator.cs
  UnityProject/Assets/Editor/BuildingMenuPanelGenerator.cs
  UnityProject/Assets/Editor/DiscipleDetailPanelGenerator.cs
  UnityProject/Assets/Editor/DiscipleListPanelGenerator.cs
  UnityProject/Assets/Editor/GeneratorRemoteControl.cs
  UnityProject/Assets/Editor/LandMaskBuilder.cs
  UnityProject/Assets/Editor/LandMaskPainterWindow.cs
  UnityProject/Assets/Editor/LocalOwnershipHarness.cs
  UnityProject/Assets/Editor/PortraitOverrideImporter.cs
  UnityProject/Assets/Editor/TaskAssignmentPanelGenerator.cs
  UnityProject/Assets/Editor/ThaiFontAtlasBaker.cs
  UnityProject/Assets/Editor/UiSpriteImporter.cs
  UnityProject/Assets/Packages/MessagePack.3.1.8/lib/netstandard2.1/MessagePack.xml
  UnityProject/Assets/Packages/MessagePack.Annotations.3.1.8/lib/netstandard2.0/MessagePack.Annotations.xml
  UnityProject/Assets/Packages/Microsoft.NET.StringTools.17.11.4/README.md
  UnityProject/Assets/Packages/Microsoft.NET.StringTools.17.11.4/lib/netstandard2.0/Microsoft.NET.StringTools.xml
  UnityProject/Assets/Packages/System.Collections.Immutable.8.0.0/PACKAGE.md
  UnityProject/Assets/Packages/System.Collections.Immutable.8.0.0/lib/netstandard2.0/System.Collections.Immutable.xml
  UnityProject/Assets/Plugins/MessagePipe.Interprocess/Runtime/Internal/ArrayPoolBufferWriter.cs
  UnityProject/Assets/Plugins/MessagePipe.Interprocess/Runtime/Internal/AsyncDisposableBridge.cs
  UnityProject/Assets/Plugins/MessagePipe.Interprocess/Runtime/Internal/Preserve.cs
  UnityProject/Assets/Plugins/MessagePipe.Interprocess/Runtime/Internal/TransformHandler.cs
  UnityProject/Assets/Plugins/MessagePipe.Interprocess/Runtime/MessageBuilder.cs
  UnityProject/Assets/Plugins/MessagePipe.Interprocess/Runtime/MessagePipe.Interprocess.asmdef
  UnityProject/Assets/Plugins/MessagePipe.Interprocess/Runtime/MessagePipeInterprocessOptions.cs
  UnityProject/Assets/Plugins/MessagePipe.Interprocess/Runtime/NamedPipeDistributedPublisherSubscribercs.cs
  UnityProject/Assets/Plugins/MessagePipe.Interprocess/Runtime/NamedPipeRemoteRequestHandler.cs
  UnityProject/Assets/Plugins/MessagePipe.Interprocess/Runtime/ServiceCollectionInterprocessExtensions.cs
  UnityProject/Assets/Plugins/MessagePipe.Interprocess/Runtime/TcpDistributedPublisherSubscriber.cs
  UnityProject/Assets/Plugins/MessagePipe.Interprocess/Runtime/TcpRemoteRequestHandler.cs
  UnityProject/Assets/Plugins/MessagePipe.Interprocess/Runtime/UdpDistributedPublisherSubscriber.cs
  UnityProject/Assets/Plugins/MessagePipe.Interprocess/Runtime/Unity/TaskShims.cs
  UnityProject/Assets/Plugins/MessagePipe.Interprocess/Runtime/Workers/NamedPipeWorker.cs
  UnityProject/Assets/Plugins/MessagePipe.Interprocess/Runtime/Workers/SocketTcpClient.cs
  UnityProject/Assets/Plugins/MessagePipe.Interprocess/Runtime/Workers/SocketUdpClient.cs
  UnityProject/Assets/Plugins/MessagePipe.Interprocess/Runtime/Workers/TcpWorker.cs
  UnityProject/Assets/Plugins/MessagePipe.Interprocess/Runtime/Workers/UdpWorker.cs
  UnityProject/Assets/Plugins/MessagePipe.Interprocess/package.json
  UnityProject/Assets/Plugins/NuGet/.nuget-installed.json
  UnityProject/Assets/Resources/Data/avatar_parts.json
  UnityProject/Assets/Resources/Data/building_defs.json
  UnityProject/Assets/Resources/Data/chibi_activity_map.json
  UnityProject/Assets/Resources/Data/chibi_activity_task_map.json
  UnityProject/Assets/Resources/Data/chibi_anim.json
  UnityProject/Assets/Resources/Data/demo_spine_rig_map.json
  UnityProject/Assets/Resources/Data/portrait_overrides.json
  UnityProject/Assets/Resources/Data/visual_overrides.json
  UnityProject/Assets/Resources/DataTables/game_tbeventchoicedef.json
  UnityProject/Assets/Resources/DataTables/game_tbeventdef.json
  UnityProject/Assets/Resources/DataTables/worldevent_tbevent.json
  UnityProject/Assets/Resources/DataTables/worldevent_tbeventchoice.json
  UnityProject/Assets/Scripts/AssemblyInfo.cs
  UnityProject/Assets/Scripts/Building/BuildingDef.cs
  UnityProject/Assets/Scripts/Building/BuildingDefPool.cs
  UnityProject/Assets/Scripts/Building/BuildingGrid.cs
  UnityProject/Assets/Scripts/Building/PlaceableLandMask.cs
  UnityProject/Assets/Scripts/Building/PlacementController.cs
  UnityProject/Assets/Scripts/Core/AssignTaskHandler.cs
  UnityProject/Assets/Scripts/Core/ChangeAvatarPartHandler.cs
  UnityProject/Assets/Scripts/Core/DecisionExecutor.cs
  UnityProject/Assets/Scripts/Core/DecisionLogger.cs
  UnityProject/Assets/Scripts/Core/GameLifetimeScope.cs
  UnityProject/Assets/Scripts/Core/Installers/BuildingInstaller.cs
  UnityProject/Assets/Scripts/Core/Installers/GameplayInstaller.cs
  UnityProject/Assets/Scripts/Core/Installers/InterprocessInstaller.cs
  UnityProject/Assets/Scripts/Core/Installers/UIInstaller.cs
  UnityProject/Assets/Scripts/Core/Installers/VisualInstaller.cs
  UnityProject/Assets/Scripts/Core/OwnershipObservability.cs
  UnityProject/Assets/Scripts/Core/PurchaseItemHandler.cs
  UnityProject/Assets/Scripts/Core/SceneLoader.cs
  UnityProject/Assets/Scripts/Core/SceneMessages.cs
  UnityProject/Assets/Scripts/Core/SceneNames.cs
  UnityProject/Assets/Scripts/Core/TaskObservability.cs
  UnityProject/Assets/Scripts/Core/TimeSystem.cs
  UnityProject/Assets/Scripts/Core/ViewerMembershipPersistence.cs
  UnityProject/Assets/Scripts/Data/AvatarPartPool.cs
  UnityProject/Assets/Scripts/Data/LubanEventPool.cs
  UnityProject/Assets/Scripts/Scenes/SectScene/SectSceneLifetimeScope.cs
  UnityProject/Assets/Scripts/Shared/GameMessages.cs
  UnityProject/Assets/Scripts/Shared/MessageTypes.cs
  UnityProject/Assets/Scripts/Shared/MockSectData.cs
  UnityProject/Assets/Scripts/Shared/SectEconomyState.cs
  UnityProject/Assets/Scripts/Shared/ViewerMembership.cs
  UnityProject/Assets/Scripts/Systems/BuildingSystem.cs
  UnityProject/Assets/Scripts/Systems/DiscipleSystem.cs
  UnityProject/Assets/Scripts/Systems/ResourceCraftingSystem.cs
  UnityProject/Assets/Scripts/Systems/SectStateProvider.cs
  UnityProject/Assets/Scripts/Systems/WorldEventSystem.cs
  UnityProject/Assets/Scripts/Tests/AdditiveSceneTest.cs
  UnityProject/Assets/Scripts/UI/Core/IUIView.cs
  UnityProject/Assets/Scripts/UI/Core/IUIViewPresenter.cs
  UnityProject/Assets/Scripts/UI/Core/UIPanelCatalog.cs
  UnityProject/Assets/Scripts/UI/Core/UIPanelDefinition.cs
  UnityProject/Assets/Scripts/UI/Core/UIPanelHandle.cs
  UnityProject/Assets/Scripts/UI/Core/UIPresenter.cs
  UnityProject/Assets/Scripts/UI/Core/UIPresenterKind.cs
  UnityProject/Assets/Scripts/UI/Core/UIRoot.cs
  UnityProject/Assets/Scripts/UI/Core/UIService.cs
  UnityProject/Assets/Scripts/UI/Presenters/AvatarCustomizationPresenter.cs
  UnityProject/Assets/Scripts/UI/Presenters/BottomMenuPresenter.cs
  UnityProject/Assets/Scripts/UI/Presenters/BuildingMenuPresenter.cs
  UnityProject/Assets/Scripts/UI/Presenters/DiscipleDetailPresenter.cs
  UnityProject/Assets/Scripts/UI/Presenters/DiscipleDetailTabPresenters.cs
  UnityProject/Assets/Scripts/UI/Presenters/DiscipleListPresenter.cs
  UnityProject/Assets/Scripts/UI/Presenters/EventPopupPresenter.cs
  UnityProject/Assets/Scripts/UI/Presenters/LogWindowPresenter.cs
  UnityProject/Assets/Scripts/UI/Presenters/ResourcePopupPresenter.cs
  UnityProject/Assets/Scripts/UI/Presenters/TaskAssignmentPresenter.cs
  UnityProject/Assets/Scripts/UI/Presenters/WalletHudPresenter.cs
  UnityProject/Assets/Scripts/UI/Systems/BuildingPlacementUISystem.cs
  UnityProject/Assets/Scripts/UI/Systems/DiscipleDetailUISystem.cs
  UnityProject/Assets/Scripts/UI/Systems/UIBootstrap.cs
  UnityProject/Assets/Scripts/UI/Systems/WorldEventUISystem.cs
  UnityProject/Assets/Scripts/UI/Views/AvatarCustomizationView.cs
  UnityProject/Assets/Scripts/UI/Views/AvatarCustomizationViewData.cs
  UnityProject/Assets/Scripts/UI/Views/AvatarOptionButton.cs
  UnityProject/Assets/Scripts/UI/Views/AvatarRenderer.cs
  UnityProject/Assets/Scripts/UI/Views/BottomMenuView.cs
  UnityProject/Assets/Scripts/UI/Views/BuildingMenuView.cs
  UnityProject/Assets/Scripts/UI/Views/BuildingPlacementView.cs
  UnityProject/Assets/Scripts/UI/Views/DiscipleDetailView.cs
  UnityProject/Assets/Scripts/UI/Views/DiscipleListView.cs
  UnityProject/Assets/Scripts/UI/Views/EventPopupView.cs
  UnityProject/Assets/Scripts/UI/Views/FitWidthGridLayoutGroup.cs
  UnityProject/Assets/Scripts/UI/Views/LogWindowView.cs
  UnityProject/Assets/Scripts/UI/Views/PortraitOverrideBinding.cs
  UnityProject/Assets/Scripts/UI/Views/ResourcePopupView.cs
  UnityProject/Assets/Scripts/UI/Views/TaskAssignmentView.cs
  UnityProject/Assets/Scripts/UI/Views/UIViewBase.cs
  UnityProject/Assets/Scripts/UI/Views/WalletHudView.cs
  UnityProject/Assets/Scripts/UI/Widgets/DiscipleModalScope.cs
  UnityProject/Assets/Scripts/UI/Widgets/InkTooltip.cs
  UnityProject/Assets/Scripts/UI/Widgets/InkWidgets.cs
  UnityProject/Assets/Scripts/UI/Widgets/RadarChartGraphic.cs
  UnityProject/Assets/Scripts/UI/Widgets/UiPalette.cs
  UnityProject/Assets/Scripts/Visual/Core/AppearanceResolver.cs
  UnityProject/Assets/Scripts/Visual/Core/AvatarIconCrop.cs
  UnityProject/Assets/Scripts/Visual/Core/DefaultEntitlementProvider.cs
  UnityProject/Assets/Scripts/Visual/Core/DiscipleVisualSystem.cs
  UnityProject/Assets/Scripts/Visual/Core/EntitlementRandom.cs
  UnityProject/Assets/Scripts/Visual/Core/IChibiVisual.cs
  UnityProject/Assets/Scripts/Visual/Core/IPortraitVisual.cs
  UnityProject/Assets/Scripts/Visual/Core/IVisualEntitlementProvider.cs
  UnityProject/Assets/Scripts/Visual/Core/PortraitOverrideMap.cs
  UnityProject/Assets/Scripts/Visual/Core/TaskActivityMapper.cs
  UnityProject/Assets/Scripts/Visual/Core/VisualRuntimeConfig.cs
  UnityProject/Assets/Scripts/Visual/Core/VisualTierPolicy.cs
  UnityProject/Assets/Scripts/Visual/Scene/CameraFramingConfig.cs
  UnityProject/Assets/Scripts/Visual/Scene/CameraRigController.cs
  UnityProject/Assets/Scripts/Visual/Scene/CameraRigPanBootstrap.cs
  UnityProject/Assets/Scripts/Visual/Scene/CameraRigPorts.cs
  UnityProject/Assets/Scripts/Visual/Scene/ChibiClickTarget.cs
  UnityProject/Assets/Scripts/Visual/Scene/ChibiSceneRoot.cs
  UnityProject/Assets/Scripts/Visual/Scene/GridOverlayRenderer.cs
  UnityProject/Assets/Scripts/Visual/Scene/IsometricCellMath.cs
  UnityProject/Assets/Scripts/Visual/Scene/RigPanInput.cs
  UnityProject/Assets/Scripts/Visual/Scene/RigZoomInput.cs
  UnityProject/Assets/Scripts/Visual/Scene/TerrainBackdropRenderer.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Core/Phase2VerifyDriver.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Core/PlaceholderSpriteBaker.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Core/SizeProbeSceneLauncher.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Core/SpikeRunners.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Core/SpikeSceneBootstrap.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Core/SpikeSceneLauncher.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Core/Visual.Core.Spikes.asmdef
  UnityProject/Assets/Scripts/Visual/Spikes/Core/VisualSpikeProfiler.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Editor/ChibiSheetBaker.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Editor/DemoVerifyRunner.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Editor/Phase2VerifyRunner.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Editor/Phase3VerifyRunner.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Editor/Phase4VerifyRunner.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Editor/Phase5VerifyRunner.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Editor/PortraitPlaceholderBaker.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Editor/Visual.Spikes.Editor.asmdef
  UnityProject/Assets/Scripts/Visual/Spikes/Editor/VisualCoverageValidator.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Editor/VisualDemoSceneTool.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Editor/VisualOverridesShotRunner.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Editor/VisualOverridesVerifyRunner.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Editor/VisualSpikeRemoteControl.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Editor/VisualSpikeScenes.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Editor/VisualSpikesEditor.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Spine/SpineSpikeRunner.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Spine/SpineSpikeSceneLauncher.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Spine/Visual.Spine.Spikes.asmdef
  UnityProject/Assets/Scripts/Visual/Spikes/Spine/VisualDemoHud.cs
  UnityProject/Assets/Scripts/Visual/Spikes/Spine/VisualDemoSpineEnabler.cs
  UnityProject/Assets/Scripts/Visual/Spine/ChibiActivityMap.cs
  UnityProject/Assets/Scripts/Visual/Spine/DemoSpineRigMap.cs
  UnityProject/Assets/Scripts/Visual/Spine/SpineChibiVisual.cs
  UnityProject/Assets/Scripts/Visual/Spine/Visual.Spine.asmdef
  UnityProject/Assets/Scripts/Visual/Spine/VisualOverrideMap.cs
  UnityProject/Assets/Scripts/Visual/Spine/VisualSpineBootstrap.cs
  UnityProject/Assets/Scripts/Visual/Sprite/ChibiFrameBank.cs
  UnityProject/Assets/Scripts/Visual/Sprite/ChibiFrameClock.cs
  UnityProject/Assets/Scripts/Visual/Sprite/SpriteChibiVisual.cs
  UnityProject/Assets/Scripts/Xianxia.Sect.Runtime.asmdef
  UnityProject/Assets/Spine Examples/Other Examples/Animation Tester/SpineAnimationTesterTool.cs
  UnityProject/Assets/Spine Examples/Scripts/AttackSpineboy.cs
  UnityProject/Assets/Spine Examples/Scripts/DataAssetsFromExportsExample.cs
  UnityProject/Assets/Spine Examples/Scripts/DraggableTransform.cs
  UnityProject/Assets/Spine Examples/Scripts/FootSoldierExample.cs
  UnityProject/Assets/Spine Examples/Scripts/Getting Started Scripts/BasicPlatformerController.cs
  UnityProject/Assets/Spine Examples/Scripts/Getting Started Scripts/ConstrainedCamera.cs
  UnityProject/Assets/Spine Examples/Scripts/Getting Started Scripts/Raptor.cs
  UnityProject/Assets/Spine Examples/Scripts/Getting Started Scripts/SpineBeginnerTwo.cs
  UnityProject/Assets/Spine Examples/Scripts/Getting Started Scripts/SpineBlinkPlayer.cs
  UnityProject/Assets/Spine Examples/Scripts/Getting Started Scripts/SpineboyBeginnerInput.cs
  UnityProject/Assets/Spine Examples/Scripts/Getting Started Scripts/SpineboyBeginnerModel.cs
  UnityProject/Assets/Spine Examples/Scripts/Getting Started Scripts/SpineboyBeginnerView.cs
  UnityProject/Assets/Spine Examples/Scripts/Getting Started Scripts/SpineboyTargetController.cs
  UnityProject/Assets/Spine Examples/Scripts/Getting Started Scripts/TransitionDictionaryExample.cs
  UnityProject/Assets/Spine Examples/Scripts/Goblins.cs
  UnityProject/Assets/Spine Examples/Scripts/HandleEventWithAudioExample.cs
  UnityProject/Assets/Spine Examples/Scripts/HeroEffectsHandlerExample.cs
  UnityProject/Assets/Spine Examples/Scripts/HurtFlashEffect.cs
  UnityProject/Assets/Spine Examples/Scripts/MaterialPropertyBlockExample.cs
  UnityProject/Assets/Spine Examples/Scripts/MaterialReplacementExample.cs
  UnityProject/Assets/Spine Examples/Scripts/Mecanim as Logic/DummyMecanimControllerExample.cs
  UnityProject/Assets/Spine Examples/Scripts/Mecanim as Logic/MecanimToAnimationHandleExample.cs
  UnityProject/Assets/Spine Examples/Scripts/Mecanim as Logic/SkeletonAnimationHandleExample.cs
  UnityProject/Assets/Spine Examples/Scripts/MecanimAnimationMatchModifier/AnimationMatchModifierAsset.cs
  UnityProject/Assets/Spine Examples/Scripts/Mix and Match Character Customize/EquipAssetExample.cs
  UnityProject/Assets/Spine Examples/Scripts/Mix and Match Character Customize/EquipButtonExample.cs
  UnityProject/Assets/Spine Examples/Scripts/Mix and Match Character Customize/EquipSystemExample.cs
  UnityProject/Assets/Spine Examples/Scripts/Mix and Match Character Customize/EquipsVisualsComponentExample.cs
  UnityProject/Assets/Spine Examples/Scripts/Mix and Match Character Customize/MixAndMatchSkinsButtonExample.cs
  UnityProject/Assets/Spine Examples/Scripts/Mix and Match Character Customize/MixAndMatchSkinsExample.cs
  UnityProject/Assets/Spine Examples/Scripts/MixAndMatch.cs
  UnityProject/Assets/Spine Examples/Scripts/MixAndMatchGraphic.cs
  UnityProject/Assets/Spine Examples/Scripts/RaggedySpineboy.cs
  UnityProject/Assets/Spine Examples/Scripts/ReloadSceneOnKeyDown.cs
  UnityProject/Assets/Spine Examples/Scripts/Rotator.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/BoneLocalOverride.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/CombinedSkin.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/Ghost/SkeletonGhost.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/Ghost/SkeletonGhostRenderer.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/Legacy/AtlasRegionAttacher.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/Legacy/CustomSkin.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/Legacy/SpriteAttacher.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/OutlineSkeletonGraphic.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/RenderExistingMesh.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/RootMotionDeltaCompensation.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/Sample VertexEffects/JitterEffectExample.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/Sample VertexEffects/TwoByTwoTransformEffectExample.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/SetRigidbodySolverIterations.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/SkeletonAnimationFixedTimestep.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/SkeletonAnimationMulti/SkeletonAnimationMulti.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/SkeletonColorInitialize.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/SkeletonGraphicMirror.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/SkeletonGraphicPlayAnimationAtEvent.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/SkeletonUtility Modules/Editor/SkeletonRagdoll2DInspector.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/SkeletonUtility Modules/Editor/SkeletonRagdollInspector.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/SkeletonUtility Modules/Editor/spine-unity-examples-editor.asmdef
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/SkeletonUtility Modules/SkeletonRagdoll.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/SkeletonUtility Modules/SkeletonRagdoll2D.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/SkeletonUtility Modules/SkeletonUtilityEyeConstraint.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/SkeletonUtility Modules/SkeletonUtilityGroundConstraint.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/SkeletonUtility Modules/SkeletonUtilityKinematicShadow.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/SlotTintBlackFollower.cs
  UnityProject/Assets/Spine Examples/Scripts/Sample Components/SpineEventUnityHandler.cs
  UnityProject/Assets/Spine Examples/Scripts/SpawnFromSkeletonDataExample.cs
  UnityProject/Assets/Spine Examples/Scripts/SpawnSkeletonGraphicExample.cs
  UnityProject/Assets/Spine Examples/Scripts/SpineGauge.cs
  UnityProject/Assets/Spine Examples/Scripts/Spineboy.cs
  UnityProject/Assets/Spine Examples/Scripts/SpineboyBodyTilt.cs
  UnityProject/Assets/Spine Examples/Scripts/SpineboyFacialExpression.cs
  UnityProject/Assets/Spine Examples/Scripts/SpineboyFootplanter.cs
  UnityProject/Assets/Spine Examples/Scripts/SpineboyFreeze.cs
  UnityProject/Assets/Spine Examples/Scripts/SpineboyPole.cs
  UnityProject/Assets/Spine Examples/Scripts/SpineboyPoleGraphic.cs
  UnityProject/Assets/Spine Examples/Spine Skeletons/Dragon/dragon.json
  UnityProject/Assets/Spine Examples/Spine Skeletons/Eyes/eyes.json
  UnityProject/Assets/Spine Examples/Spine Skeletons/FootSoldier/FootSoldier.json
  UnityProject/Assets/Spine Examples/Spine Skeletons/Gauge/Gauge.json
  UnityProject/Assets/Spine Examples/Spine Skeletons/Goblins/goblins.json
  UnityProject/Assets/Spine Examples/Spine Skeletons/Hero/hero-pro.json
  UnityProject/Assets/Spine Examples/Spine Skeletons/Raggedy Spineboy/raggedy spineboy.json
  UnityProject/Assets/Spine Examples/Spine Skeletons/Raptor/raptor.json
  UnityProject/Assets/Spine Examples/Spine Skeletons/Spineunitygirl/Doi.json
  UnityProject/Assets/Spine Examples/Spine Skeletons/Stretchyman/stretchyman.json
  UnityProject/Assets/Spine Examples/Spine Skeletons/mix-and-match/mix-and-match-pro.json
  UnityProject/Assets/Spine Examples/Spine Skeletons/raptor-pro-and-mask/raptor-pro.json
  UnityProject/Assets/Spine Examples/Spine Skeletons/spineboy-pro/spineboy-pro.json
  UnityProject/Assets/Spine Examples/Spine Skeletons/spineboy-unity/spineboy-unity.json
  UnityProject/Assets/Spine Examples/Spine Skeletons/whirlyblendmodes/whirlyblendmodes.json
  UnityProject/Assets/Spine Examples/spine-unity-examples.asmdef
  UnityProject/Assets/Spine/CHANGELOG.md
  UnityProject/Assets/Spine/Editor/spine-unity-editor.asmdef
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Asset Types/AnimationReferenceAssetEditor.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Asset Types/SkeletonDataAssetInspector.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Asset Types/SpineAtlasAssetInspector.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Asset Types/SpineSpriteAtlasAssetInspector.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Components/BoneFollowerGraphicInspector.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Components/BoneFollowerInspector.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Components/BoundingBoxFollowerGraphicInspector.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Components/BoundingBoxFollowerInspector.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Components/PointFollowerInspector.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Components/SkeletonAnimationInspector.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Components/SkeletonGraphicCustomMaterialsInspector.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Components/SkeletonGraphicInspector.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Components/SkeletonMecanimInspector.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Components/SkeletonMecanimRootMotionInspector.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Components/SkeletonRendererCustomMaterialsInspector.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Components/SkeletonRendererInspector.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Components/SkeletonRootMotionBaseInspector.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Components/SkeletonRootMotionInspector.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Components/SkeletonUtilityBoneInspector.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Components/SkeletonUtilityInspector.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Menus.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Shaders/SpineShaderWithOutlineGUI.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Shaders/SpineSpriteShaderGUI.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/SpineAttributeDrawers.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Utility/AssetDatabaseAvailabilityDetector.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Utility/AssetUtility.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Utility/BlendModeMaterialsUtility.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Utility/BuildSettings.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Utility/DataReloadHandler.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Utility/Icons.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Utility/Instantiation.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Utility/Preferences.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Utility/SpineEditorUtilities.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Utility/SpineHandles.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Utility/SpineInspectorUtility.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Utility/SpineMaskUtilities.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Windows/SkeletonBaker.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Windows/SkeletonBakingWindow.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Windows/SkeletonDebugWindow.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Windows/SpinePreferences.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Editor/Windows/SpriteAtlasImportWindow.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Modules/SkeletonRenderSeparator/Editor/SkeletonPartsRendererInspector.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Modules/SkeletonRenderSeparator/Editor/SkeletonRenderSeparatorInspector.cs
  UnityProject/Assets/Spine/Editor/spine-unity/Modules/SlotBlendModes/Editor/SlotBlendModesEditor.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/Animation.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/AnimationState.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/AnimationStateData.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/Atlas.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/Attachments/AtlasAttachmentLoader.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/Attachments/Attachment.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/Attachments/AttachmentLoader.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/Attachments/AttachmentType.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/Attachments/BoundingBoxAttachment.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/Attachments/ClippingAttachment.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/Attachments/MeshAttachment.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/Attachments/PathAttachment.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/Attachments/PointAttachment.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/Attachments/RegionAttachment.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/Attachments/VertexAttachment.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/BlendMode.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/Bone.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/BoneData.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/Collections/OrderedDictionary.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/ConstraintData.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/Event.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/EventData.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/ExposedList.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/IUpdatable.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/IkConstraint.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/IkConstraintData.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/Json.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/MathUtils.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/PathConstraint.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/PathConstraintData.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/Skeleton.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/SkeletonBinary.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/SkeletonBounds.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/SkeletonClipping.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/SkeletonData.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/SkeletonJson.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/Skin.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/Slot.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/SlotData.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/TransformConstraint.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/TransformConstraintData.cs
  UnityProject/Assets/Spine/Runtime/spine-csharp/Triangulator.cs
  UnityProject/Assets/Spine/Runtime/spine-unity.asmdef
  UnityProject/Assets/Spine/Runtime/spine-unity/Asset Types/AnimationReferenceAsset.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Asset Types/AtlasAssetBase.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Asset Types/BlendModeMaterials.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Asset Types/EventDataReferenceAsset.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Asset Types/RegionlessAttachmentLoader.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Asset Types/SkeletonDataAsset.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Asset Types/SkeletonDataCompatibility.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Asset Types/SkeletonDataModifierAsset.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Asset Types/SpineAtlasAsset.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Asset Types/SpineSpriteAtlasAsset.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/Following/BoneFollower.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/Following/BoneFollowerGraphic.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/Following/BoundingBoxFollower.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/Following/BoundingBoxFollowerGraphic.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/Following/PointFollower.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/RootMotion/SkeletonMecanimRootMotion.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/RootMotion/SkeletonRootMotion.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/RootMotion/SkeletonRootMotionBase.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/SkeletonAnimation.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/SkeletonGraphic.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/SkeletonMecanim.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/SkeletonRenderSeparator/SkeletonPartsRenderer.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/SkeletonRenderSeparator/SkeletonRenderSeparator.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/SkeletonRenderer.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/SkeletonRendererCustomMaterials/SkeletonGraphicCustomMaterials.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/SkeletonRendererCustomMaterials/SkeletonRendererCustomMaterials.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/SkeletonUtility/ActivateBasedOnFlipDirection.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/SkeletonUtility/FollowLocationRigidbody.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/SkeletonUtility/FollowLocationRigidbody2D.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/SkeletonUtility/FollowSkeletonUtilityRootRotation.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/SkeletonUtility/SkeletonUtility.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/SkeletonUtility/SkeletonUtilityBone.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Components/SkeletonUtility/SkeletonUtilityConstraint.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Deprecated/SlotBlendModes/SlotBlendModes.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/ISkeletonAnimation.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Mesh Generation/DoubleBuffered.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Mesh Generation/MeshGenerator.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Mesh Generation/MeshRendererBuffers.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Mesh Generation/SkeletonRendererInstruction.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Mesh Generation/SpineMesh.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Modules/TK2D/SpriteCollectionAttachmentLoader.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Shaders/Sprite/README.md
  UnityProject/Assets/Spine/Runtime/spine-unity/SkeletonDataModifierAssets/BlendModeMaterials/BlendModeMaterialsAsset.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/SpineAttributes.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Utility/AtlasUtilities.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Utility/AttachmentCloneExtensions.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Utility/AttachmentRegionExtensions.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Utility/MaterialChecks.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Utility/SkeletonExtensions.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Utility/SkinUtilities.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Utility/TimelineExtensions.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Utility/YieldInstructions/WaitForSpineAnimation.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Utility/YieldInstructions/WaitForSpineAnimationComplete.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Utility/YieldInstructions/WaitForSpineAnimationEnd.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Utility/YieldInstructions/WaitForSpineEvent.cs
  UnityProject/Assets/Spine/Runtime/spine-unity/Utility/YieldInstructions/WaitForSpineTrackEntryEnd.cs
  UnityProject/Assets/Spine/package.json
  UnityProject/Assets/Tests/EditMode/AppearanceResolverTests.cs
  UnityProject/Assets/Tests/EditMode/AssignTaskTests.cs
  UnityProject/Assets/Tests/EditMode/AvatarIconCropTests.cs
  UnityProject/Assets/Tests/EditMode/AvatarPartCoverageTests.cs
  UnityProject/Assets/Tests/EditMode/BuildingGridTests.cs
  UnityProject/Assets/Tests/EditMode/BuildingMenuPresenterTests.cs
  UnityProject/Assets/Tests/EditMode/BuildingPlacementTests.cs
  UnityProject/Assets/Tests/EditMode/CameraFramingMathTests.cs
  UnityProject/Assets/Tests/EditMode/CameraRigControllerTests.cs
  UnityProject/Assets/Tests/EditMode/ChibiFrameBankTests.cs
  UnityProject/Assets/Tests/EditMode/CompositionRootContainerTests.cs
  UnityProject/Assets/Tests/EditMode/CompositionRootTests.cs
  UnityProject/Assets/Tests/EditMode/DiscipleListPresenterTests.cs
  UnityProject/Assets/Tests/EditMode/EntitlementTests.cs
  UnityProject/Assets/Tests/EditMode/FaceSplitTests.cs
  UnityProject/Assets/Tests/EditMode/HybridPermissionTests.cs
  UnityProject/Assets/Tests/EditMode/MockStartStateTests.cs
  UnityProject/Assets/Tests/EditMode/OwnershipHarnessTests.cs
  UnityProject/Assets/Tests/EditMode/PlaceableLandMaskTests.cs
  UnityProject/Assets/Tests/EditMode/PortraitOverrideTests.cs
  UnityProject/Assets/Tests/EditMode/RadarChartGraphicTests.cs
  UnityProject/Assets/Tests/EditMode/SceneNamesTests.cs
  UnityProject/Assets/Tests/EditMode/TaskActivityMapperTests.cs
  UnityProject/Assets/Tests/EditMode/TaskAssignmentPresenterTests.cs
  UnityProject/Assets/Tests/EditMode/TaskObservabilityTests.cs
  ... [140 more files truncated — ปรับ TREE_MAX_LINES หรือ .aiignore]
```

## Recent Wiki Log (last 15 entries, each cut to 400 chars)

```
## [2026-10-02] new-feature | Tools/art/bake_land_mask.py + Scripts/Building/{PlaceableLandMask.cs,BuildingGrid.cs} + Systems/BuildingSystem.cs + Visual/Scene/GridOverlayRenderer.cs + Tests/EditMode/{PlaceableLandMaskTests,BuildingGridTests}.cs | Grid placement เฉพาะส่วนที่เป็นพื้นดินจริงของเกาะ (แทนโซน 4 สี่เ�
## [2026-10-02] update | Tools/art/bake_land_mask.py + Scripts/Building/PlaceableLandMask.cs + Tests/EditMode/PlaceableLandMaskTests.cs | เกณฑ์ที่สองของการ bake land mask: "ผืนที่วางได้ต้องรับ footprint ใหญ่สุดของเกมได้" — ผู้ใช้กากบาท 4 จุดบน preview รอ�
## [2026-10-02] update | Scripts/UI/Views/BottomMenuView.cs + Prefabs/UI/BottomMenuPrefab.prefab | ขยับแถบเมนูล่าง (ปุ่ม สร้าง/ศิษย์/คลัง) ออกจากขอบจอตามฟีดแบ็ก: เดิม bar วางที่ y=0 แล้วปุ่ม 96x95 ลอยอยู่สูงจากขอบล่า�
## [2026-10-02] new-tool | Assets/Editor/LandMaskPainterWindow.cs + Assets/Editor/LandMaskBuilder.cs | เครื่องมือ Unity Editor สำหรับ mask พื้นที่วางได้ (Xianxia > Land Mask Painter) ไว้ใช้ตอนเปลี่ยนภาพภูเขา/เกาะลอยฟ้าใหม่: หน้าต่างวาดภาพ
## [2026-10-04] new-feature | Shared/GameMessages.cs (+2 mirrors) + Scripts/Systems/SectStateProvider.cs + Core/TimeSystem.cs + Core/AssignTaskHandler.cs + Core/Installers/InterprocessInstaller.cs + McpBridge/Program.cs | Task System v2 Phase A–C: [Key(9)] OwnerType/[Key(10)] OwnerId + AssignTaskRequest/Response/DiscipleTaskChangedMessage; ISectStateProvider.TryAssignTask (disciple→permission:
## [2026-10-08] new-feature | Scripts/Shared/MockSectData.cs + Scripts/Systems/SectStateProvider.cs + Tests/EditMode/{TaskRequirementTests,MockStartStateTests,BuildingPlacementTests,BuildingMenuPresenterTests}.cs + LLMWiki/wiki/sources/open-questions.md | Task building-requirements + start-state fix: TaskRequiredBuilding static dict (gathering_herb→herb_plot, refining_elixir→pill_hall, forging
## [2026-10-08] verify | UnityProject/Assets/Tests/EditMode/MockStartStateTests.cs | P1 (task availability) gap-fill: ยืนยันว่า requirement ทั้งหมด implement อยู่แล้วจาก entry ก่อนหน้า (TaskRequiredBuilding + IsTaskAvailable side-effect-free + TryAssignTask gate + TickGathering/TickCrafting per-tick DefId set; forge ใน build
## [2026-10-08] verify | UnityProject/Assets/Tests/EditMode/MockStartStateTests.cs | P2 (fresh mock state consistency) gap-fill: ยืนยัน state เริ่มต้นถูกต้องอยู่แล้ว (b001 herb_plot เดียวที่ (-11,-8) rot 0 บน grid 50x50 origin -25,-25 — test วัดผ่าน mask จริง ไม่ใช่คำเคลม�
## [2026-10-08] new-feature | Scripts/UI/Views/TaskAssignmentView.cs + Scripts/UI/Presenters/TaskAssignmentPresenter.cs + Scripts/UI/Core/UIPresenterKind.cs + Scripts/UI/Core/UIService.cs + Scripts/Core/Installers/UIInstaller.cs + Scripts/Core/TimeSystem.cs + Scripts/Systems/SectStateProvider.cs + Editor/TaskAssignmentPanelGenerator.cs + Scripts/UI/Presenters/BottomMenuPresenter.cs + Scripts/UI/Sy
## [2026-10-08] new-feature | Shared/GameMessages.cs (+2 mirrors) + Scripts/Core/TimeSystem.cs + Scripts/Systems/SectStateProvider.cs + Editor/LocalOwnershipHarness.cs + Tests/EditMode/OwnershipHarnessTests.cs + Tests/EditMode/DiscipleListPresenterTests.cs | P4 local ownership test harness: ใช้ DiscipleOwnerType/OwnerId เดิม (ไม่เพิ่ม ControllerViewerId หรือ e
## [2026-10-08] new-feature | Shared/GameMessages.cs (+2 mirrors) + Scripts/Core/OwnershipObservability.cs + Scripts/Core/Installers/{GameplayInstaller,InterprocessInstaller}.cs + Editor/LocalOwnershipHarness.cs (resolve fix) + McpBridge/Program.cs + Tests/EditMode/OwnershipHarnessTests.cs | P4 observability: bridge อ่าน log การเปลี่ยนเจ้าของได้ โ�
## [2026-10-08] new-feature | Shared/ViewerMembership.cs (+2 mirrors) + Shared/SectEconomyState.cs (+2 mirrors) + Tests/EditMode/ViewerMembershipTests.cs | P5A viewer membership & activity data (data + persistence contracts only): ไม่มี ViewerRecord เดิมใน repo (มีแค่ wiki note รอ ViewerId) จึงสร้างใหม่แบบ minimal — ViewerRecord [
## [2026-10-09] new-feature | Scripts/Core/TimeSystem.cs + Scripts/Systems/SectStateProvider.cs + Scripts/Core/Installers/GameplayInstaller.cs + Scripts/UI/Presenters/TaskAssignmentPresenter.cs + Tests/EditMode/{HybridPermissionTests(new),AssignTaskTests,TaskAssignmentPresenterTests,DiscipleListPresenterTests}.cs | P5B Hybrid Permissions + authoritative queries (ไม่ต่อ Twitch, ไม�
## [2026-10-09] new-feature | Shared/GameMessages.cs (+2 mirrors) + Scripts/Core/TaskObservability.cs + Scripts/Core/ViewerMembershipPersistence.cs + Scripts/Core/Installers/{GameplayInstaller,InterprocessInstaller}.cs + Scripts/Core/TimeSystem.cs + Scripts/Systems/SectStateProvider.cs + Scripts/UI/{Views/TaskAssignmentView,Presenters/TaskAssignmentPresenter}.cs + McpBridge/Program.cs + Tests/Edit
## [2026-10-09] update | Shared/MockSectData.cs (+2 mirrors) + Tests/EditMode/{MockStartStateTests,BuildingPlacementTests,BuildingMenuPresenterTests,AssignTaskTests,TaskRequirementTests,HybridPermissionTests,TaskObservabilityTests,OwnershipHarnessTests,TaskAssignmentPresenterTests}.cs + LLMWiki/wiki/log.md | Start state = no buildings at all: ลบ herb_plot b001 ที่ (-11,-8) ออกจ�
```

