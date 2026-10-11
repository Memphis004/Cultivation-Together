# Cultivation-Together Context Pack

- Repository: Memphis004/Cultivation-Together
- Commit: 2a30d99
- Branch: main
- Generated: 2026-10-11
- Uncommitted changes in working tree: 31 file(s) (ไฟล์ที่แนบมาอ่านจาก working tree ไม่ใช่จาก commit — ถ้า > 0 อาจต่างจาก commit)
- Files after .aiignore filter: 2092

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
- `McpBridge/Shared/DiscipleAttributes.cs`: static class DiscipleAttributesConfig, class DiscipleAttributes
- `McpBridge/Shared/GameMessages.cs`: class DiscipleRecruitedMessage, class DiscipleRankChangedMessage, class SectResourceChangedMessage, class ContributionEarnedMessage, class SceneLoadedMessage, class WorldEventTriggeredMessage, class TimeSpeedChangedMessage, class SectStateQuery, class SectStateSnapshot, class AwaitWorldEventRequest, class AwaitWorldEventResponse, class EventChoiceInfo, class ExecuteDecisionMessage, class DecisionExecutedMessage, class BuildModeStartedMessage, class BuildModeEndedMessage, class PurchaseItemRequest, class PurchaseItemResponse, class BuildingPlacedMessage, class DiscipleChibiBackendChangedMessage, class DiscipleSelectedMessage, class AvatarEquipmentChangedMessage, class ChangeAvatarPartRequest, class ChangeAvatarPartResponse, class AssignTaskRequest, class AssignTaskResponse, class DiscipleTaskChangedMessage, class DiscipleOwnerChangedMessage, class DiscipleControlModeChangedMessage, class OwnershipObservabilityQuery, class OwnershipObservabilitySnapshot, class TaskChangeObservabilityQuery, class TaskChangeObservabilitySnapshot, class TaskProtectionQuery, class TaskProtectionEntry, class TaskProtectionSnapshot, class SectViewerMembershipSave, class SectSavedOwnership, class SessionRestoredMessage, static class InterprocessTopics
- `McpBridge/Shared/MockSectData.cs`: static class MockSectData
- `McpBridge/Shared/SectEconomyState.cs`: enum OwnerScope, enum DiscipleRank, enum DiscipleSex, enum DiscipleOwnerType, enum DiscipleControlMode, enum ChibiBackend, class CurrencyWallet, class InventoryItem, class DiscipleState, class SectStockpile, class PlacedBuildingState, class SectEconomyState, struct SlotPart, sealed class AvatarAppearance, static class AvatarSlots
- `McpBridge/Shared/SectSessionSnapshot.cs`: class SectSessionSnapshotEnvelope, class SectSessionSnapshot, class PendingEventSnapshot
- `McpBridge/Shared/ViewerMembership.cs`: enum ViewerMembershipStatus, class ViewerRecord, class PendingViewerApplication, interface IClock, sealed class UtcClock, class SectViewerRegistry
- `Shared/DiscipleAttributes.cs`: static class DiscipleAttributesConfig, class DiscipleAttributes
- `Shared/GameMessages.cs`: class DiscipleRecruitedMessage, class DiscipleRankChangedMessage, class SectResourceChangedMessage, class ContributionEarnedMessage, class SceneLoadedMessage, class WorldEventTriggeredMessage, class TimeSpeedChangedMessage, class SectStateQuery, class SectStateSnapshot, class AwaitWorldEventRequest, class AwaitWorldEventResponse, class EventChoiceInfo, class ExecuteDecisionMessage, class DecisionExecutedMessage, class BuildModeStartedMessage, class BuildModeEndedMessage, class PurchaseItemRequest, class PurchaseItemResponse, class BuildingPlacedMessage, class DiscipleChibiBackendChangedMessage, class DiscipleSelectedMessage, class AvatarEquipmentChangedMessage, class ChangeAvatarPartRequest, class ChangeAvatarPartResponse, class AssignTaskRequest, class AssignTaskResponse, class DiscipleTaskChangedMessage, class DiscipleOwnerChangedMessage, class DiscipleControlModeChangedMessage, class OwnershipObservabilityQuery, class OwnershipObservabilitySnapshot, class TaskChangeObservabilityQuery, class TaskChangeObservabilitySnapshot, class TaskProtectionQuery, class TaskProtectionEntry, class TaskProtectionSnapshot, class SectViewerMembershipSave, class SectSavedOwnership, class SessionRestoredMessage, static class InterprocessTopics
- `Shared/MockSectData.cs`: static class MockSectData
- `Shared/SectEconomyState.cs`: enum OwnerScope, enum DiscipleRank, enum DiscipleSex, enum DiscipleOwnerType, enum DiscipleControlMode, enum ChibiBackend, class CurrencyWallet, class InventoryItem, class DiscipleState, class SectStockpile, class PlacedBuildingState, class SectEconomyState, struct SlotPart, sealed class AvatarAppearance, static class AvatarSlots
- `Shared/SectSessionSnapshot.cs`: class SectSessionSnapshotEnvelope, class SectSessionSnapshot, class PendingEventSnapshot
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
- `UnityProject/Assets/Scripts/Core/MainThreadDispatch.cs`: static class MainThreadDispatch, sealed class MainThreadUnavailableException
- `UnityProject/Assets/Scripts/Core/OwnershipObservability.cs`: class OwnershipObservabilityBuffer, class OwnershipObservabilityHandler
- `UnityProject/Assets/Scripts/Core/Persistence/SaveFileSystem.cs`: interface ISaveFileSystem, sealed class SystemSaveFileSystem
- `UnityProject/Assets/Scripts/Core/Persistence/SaveOperationGate.cs`: sealed class SaveOperationGate
- `UnityProject/Assets/Scripts/Core/Persistence/SaveSessionService.cs`: sealed class SaveSessionService
- `UnityProject/Assets/Scripts/Core/Persistence/SaveSlotPersistence.cs`: enum SaveIoErrorKind, sealed class SaveSlotMetadata, sealed class SaveSlotResult, sealed class SaveSlotLoadResult, sealed class SaveSlotPaths
- `UnityProject/Assets/Scripts/Core/Persistence/SaveSlotRepository.cs`: sealed class SaveSlotRepository
- `UnityProject/Assets/Scripts/Core/Persistence/SaveWorkScheduler.cs`: interface ISaveWorkScheduler, sealed class UnitySaveWorkScheduler
- `UnityProject/Assets/Scripts/Core/Persistence/SessionTransitionTracker.cs`: sealed class SessionTransitionTracker
- `UnityProject/Assets/Scripts/Core/PurchaseItemHandler.cs`: class PurchaseItemHandler
- `UnityProject/Assets/Scripts/Core/SceneLoader.cs`: class SceneLoader
- `UnityProject/Assets/Scripts/Core/SceneMessages.cs`: class SceneUnloadedMessage
- `UnityProject/Assets/Scripts/Core/SceneNames.cs`: static class SceneNames
- `UnityProject/Assets/Scripts/Core/Session/GameSessionCoordinator.cs`: sealed class GameSessionCoordinator
- `UnityProject/Assets/Scripts/Core/Session/GameSessionPhase.cs`: enum GameSessionPhase, enum SessionTransitionOutcome, sealed class SessionTransitionResult
- `UnityProject/Assets/Scripts/Core/Session/IGameSessionCoordinator.cs`: interface ISessionGate, static class SessionGate, interface IGameSessionCoordinator, interface IStarterStateFactory, interface ISessionUiCloser, interface IGameplaySceneLoader
- `UnityProject/Assets/Scripts/Core/Session/PrototypeStarterStateFactory.cs`: sealed class PrototypeStarterStateFactory
- `UnityProject/Assets/Scripts/Core/Session/SessionMessages.cs`: class SessionPhaseChangedMessage
- `UnityProject/Assets/Scripts/Core/Session/SessionRuntimeConfig.cs`: sealed class SessionRuntimeConfig
- `UnityProject/Assets/Scripts/Core/Session/SessionUiCloser.cs`: sealed class SessionUiCloser
- `UnityProject/Assets/Scripts/Core/SessionSnapshotService.cs`: interface ISessionRestoreAuthority, class SessionSnapshotService
- `UnityProject/Assets/Scripts/Core/TaskObservability.cs`: class TaskChangeObservabilityBuffer, class TaskChangeObservabilityHandler, class TaskProtectionHandler
- `UnityProject/Assets/Scripts/Core/TimeRuntimeConfig.cs`: sealed class TimeRuntimeConfig
- `UnityProject/Assets/Scripts/Core/TimeSystem.cs`: enum TimePauseReason, sealed class PendingWorldEvent, class TimeSystem, class AwaitWorldEventHandler, class SectStateQueryHandler, enum TaskPermissionOutcome, sealed class TaskPermissionResult, interface ISectStateProvider
- `UnityProject/Assets/Scripts/Core/ViewerMembershipPersistence.cs`: class ViewerMembershipPersistenceSystem
- `UnityProject/Assets/Scripts/Core/WorldEventRuntimeConfig.cs`: sealed class WorldEventRuntimeConfig
- `UnityProject/Assets/Scripts/Data/AvatarPartPool.cs`: class AvatarPartDef, class AvatarPartTable, class AvatarPartPool
- `UnityProject/Assets/Scripts/Data/LubanEventPool.cs`: class LubanEventPool
- `UnityProject/Assets/Scripts/Scenes/SectScene/SectSceneLifetimeScope.cs`: sealed class SectSceneLifetimeScope
- `UnityProject/Assets/Scripts/Shared/DiscipleAttributes.cs`: static class DiscipleAttributesConfig, class DiscipleAttributes
- `UnityProject/Assets/Scripts/Shared/GameMessages.cs`: class DiscipleRecruitedMessage, class DiscipleRankChangedMessage, class SectResourceChangedMessage, class ContributionEarnedMessage, class SceneLoadedMessage, class WorldEventTriggeredMessage, class TimeSpeedChangedMessage, class SectStateQuery, class SectStateSnapshot, class AwaitWorldEventRequest, class AwaitWorldEventResponse, class EventChoiceInfo, class ExecuteDecisionMessage, class DecisionExecutedMessage, class BuildModeStartedMessage, class BuildModeEndedMessage, class PurchaseItemRequest, class PurchaseItemResponse, class BuildingPlacedMessage, class DiscipleChibiBackendChangedMessage, class DiscipleSelectedMessage, class AvatarEquipmentChangedMessage, class ChangeAvatarPartRequest, class ChangeAvatarPartResponse, class AssignTaskRequest, class AssignTaskResponse, class DiscipleTaskChangedMessage, class DiscipleOwnerChangedMessage, class DiscipleControlModeChangedMessage, class OwnershipObservabilityQuery, class OwnershipObservabilitySnapshot, class TaskChangeObservabilityQuery, class TaskChangeObservabilitySnapshot, class TaskProtectionQuery, class TaskProtectionEntry, class TaskProtectionSnapshot, class SectViewerMembershipSave, class SectSavedOwnership, class SessionRestoredMessage, static class InterprocessTopics
- `UnityProject/Assets/Scripts/Shared/MockSectData.cs`: static class MockSectData
- `UnityProject/Assets/Scripts/Shared/SectEconomyState.cs`: enum OwnerScope, enum DiscipleRank, enum DiscipleSex, enum DiscipleOwnerType, enum DiscipleControlMode, enum ChibiBackend, class CurrencyWallet, class InventoryItem, class DiscipleState, class SectStockpile, class PlacedBuildingState, class SectEconomyState, struct SlotPart, sealed class AvatarAppearance, static class AvatarSlots
- `UnityProject/Assets/Scripts/Shared/SectSessionSnapshot.cs`: class SectSessionSnapshotEnvelope, class SectSessionSnapshot, class PendingEventSnapshot
- `UnityProject/Assets/Scripts/Shared/ViewerMembership.cs`: enum ViewerMembershipStatus, class ViewerRecord, class PendingViewerApplication, interface IClock, sealed class UtcClock, class SectViewerRegistry
- `UnityProject/Assets/Scripts/Systems/AutoTaskScheduler.cs`: sealed class AutoEvaluationOutcome, class AutoTaskScheduler
- `UnityProject/Assets/Scripts/Systems/AutoTaskScoring.cs`: enum SectTaskKind, sealed class SectTaskInfo, sealed class AutoTaskFacts, sealed class AutoTaskScore, enum AutoRecoveryState, sealed class AutoTaskDecision, static class AutoTaskWeights, static class AutoTaskTargets, static class AutoTaskScoring
- `UnityProject/Assets/Scripts/Systems/BuildingSystem.cs`: class BuildingSystem
- `UnityProject/Assets/Scripts/Systems/DiscipleSystem.cs`: class DiscipleSystem
- `UnityProject/Assets/Scripts/Systems/ResourceCraftingSystem.cs`: class ResourceCraftingSystem
- `UnityProject/Assets/Scripts/Systems/SectStateProvider.cs`: enum DiscipleWorkOutcome, class SectStateProvider
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
- `UnityProject/Assets/Scripts/UI/Presenters/IngameMenuPresenter.cs`: sealed class IngameMenuArgs, class IngameMenuPresenter
- `UnityProject/Assets/Scripts/UI/Presenters/LogWindowPresenter.cs`: class LogWindowPresenter
- `UnityProject/Assets/Scripts/UI/Presenters/ResourcePopupPresenter.cs`: sealed class ResourcePopupArgs, class ResourcePopupPresenter
- `UnityProject/Assets/Scripts/UI/Presenters/TaskAssignmentPresenter.cs`: class TaskAssignmentPresenter
- `UnityProject/Assets/Scripts/UI/Presenters/TimeControlPresenter.cs`: class TimeControlPresenter
- `UnityProject/Assets/Scripts/UI/Presenters/TitleMenuPresenter.cs`: class TitleMenuPresenter
- `UnityProject/Assets/Scripts/UI/Presenters/WalletHudPresenter.cs`: class WalletHudPresenter
- `UnityProject/Assets/Scripts/UI/Presenters/WorldEventChipPresenter.cs`: sealed class WorldEventChipArgs, class WorldEventChipPresenter
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
- `UnityProject/Assets/Scripts/UI/Views/IngameMenuView.cs`: class IngameMenuView
- `UnityProject/Assets/Scripts/UI/Views/LogWindowView.cs`: class LogWindowView
- `UnityProject/Assets/Scripts/UI/Views/PortraitOverrideBinding.cs`: sealed class PortraitOverrideBinding
- `UnityProject/Assets/Scripts/UI/Views/ResourcePopupView.cs`: sealed class ResourcePopupRow, class ResourcePopupView
- `UnityProject/Assets/Scripts/UI/Views/TaskAssignmentView.cs`: sealed class TaskAssignmentArgs, sealed class TaskAssignmentRowCell, class TaskAssignmentView
- `UnityProject/Assets/Scripts/UI/Views/TimeControlView.cs`: enum TimeControlActiveButton, class TimeControlView
- `UnityProject/Assets/Scripts/UI/Views/TitleMenuView.cs`: class TitleMenuView
- `UnityProject/Assets/Scripts/UI/Views/UIViewBase.cs`: abstract class UIViewBase
- `UnityProject/Assets/Scripts/UI/Views/WalletHudView.cs`: class ResourceSlotBinding, class WalletHudView
- `UnityProject/Assets/Scripts/UI/Views/WorldEventChipView.cs`: class WorldEventChipView
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

### Shared/GameMessages.cs (25586 bytes)
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
    72	
    73	        /// <summary>
    74	        /// P13A (APPENDED field — old payloads simply read back null/empty and every
    75	        /// existing consumer keeps working): the unique id of THIS occurrence of the event.
    76	        /// Two occurrences of the same definition differ only here, and only an id that is
    77	        /// still pending can be decided. Empty for a non-decision event (there is nothing to
    78	        /// decide).
    79	        /// </summary>
    80	        [Key(4)] public string InstanceId { get; set; }
    81	
    82	        /// <summary>
    83	        /// P13B (APPENDED field — an old reader ignores it and an old payload reads back
    84	        /// empty, which means "no speaker"): the disciple whose portrait the popup renders
    85	        /// for this occurrence. Optional by design — an event with no speaker draws no
    86	        /// portrait area at all, rather than a blank one.
    87	        /// </summary>
    88	        [Key(5)] public string SpeakerDiscipleId { get; set; }
    89	
    90	        /// <summary>Display name shown beside the speaker portrait; empty when there is no speaker.</summary>
    91	        [Key(6)] public string SpeakerDisplayName { get; set; }
    92	    }
    93	
    94	    // Internal-only (not registered on the interprocess bus) - the UI and
    95	    // other in-Unity systems care about this, the MCP bridge doesn't.
    96	    [MessagePackObject]
    97	    public class TimeSpeedChangedMessage
    98	    {
    99	        [Key(0)] public int Speed { get; set; }
   100	        [Key(1)] public bool Paused { get; set; }
   101	    }
   102	
   103	    // Request/response pair: the MCP bridge asks Unity for a full state
   104	    // snapshot on demand (e.g. when the AI GM calls the get_sect_state tool).
   105	    [MessagePackObject]
   106	    public class SectStateQuery
   107	    {
   108	        [Key(0)] public string RequestId { get; set; }
   109	    }
   110	
   111	    [MessagePackObject]
   112	    public class SectStateSnapshot
   113	    {
   114	        [Key(0)] public string RequestId { get; set; }
   115	        [Key(1)] public byte[] EconomyStateBytes { get; set; } // SectEconomyState, MessagePack-serialized
   116	    }
   117	
   118	    // Request/response pair for await_next_world_event.
   119	    //
   120	    // This is deliberately request-response, not pub/sub, even though
   121	    // conceptually it's "wait for the next event". Confirmed root cause:
   122	    // MessagePipe.Interprocess's TcpDistributedSubscriber unconditionally
   123	    // calls worker.StartReceiver() (binds its own listening socket)
   124	    // regardless of HostAsServer - so a non-hub process (the bridge) can
   125	    // never safely use IDistributedSubscriber over this TCP transport, it
   126	    // collides with the hub's (Unity's) already-bound port. Request-response
   127	    // only ever uses the client connection (Connect, not Listen), which is
   128	    // exactly what get_sect_state already proved works fine. The handler on
   129	    // the Unity side just holds the response open (via a UniTaskCompletionSource)
   130	    // until an event actually fires - same observed behavior as a
   131	    // subscription, without the broken transport.
   132	    [MessagePackObject]
   133	    public class AwaitWorldEventRequest
   134	    {
   135	        [Key(0)] public string RequestId { get; set; }
   136	    }
   137	
   138	    [MessagePackObject]
   139	    public class AwaitWorldEventResponse
   140	    {
   141	        [Key(0)] public string EventId { get; set; }
   142	        [Key(1)] public bool RequiresDecision { get; set; }
   143	        [Key(2)] public string Description { get; set; }
   144	        [Key(3)] public List<EventChoiceInfo> Choices { get; set; } = new List<EventChoiceInfo>();
   145	
   146	        /// <summary>
   147	        /// P13A (APPENDED field — an old reader ignores it, an old payload reads back
   148	        /// empty): the instance id of the event being handed over, which is what a decision
   149	        /// must name. Empty for a non-decision event. An event whose id is no longer pending
   150	        /// by the time a decision arrives is refused, never re-applied.
   151	        /// </summary>
   152	        [Key(4)] public string InstanceId { get; set; }
   153	
   154	        /// <summary>
   155	        /// P13B (APPENDED field — an old reader ignores it, an old payload reads back empty):
   156	        /// the optional speaker of this occurrence, so the AI GM knows who is talking rather
   157	        /// than only what is being asked. Empty = the event has no speaker.
   158	        /// </summary>
   159	        [Key(5)] public string SpeakerDiscipleId { get; set; }
   160	
   161	        /// <summary>Display name matching <see cref="SpeakerDiscipleId"/>; empty when there is none.</summary>
   162	        [Key(6)] public string SpeakerDisplayName { get; set; }
   163	    }
   164	
   165	    // One selectable option for a world event - authored per-event in the
   166	    // EventData ScriptableObject, carried over the wire so the AI GM sees
   167	    // real choices instead of just a bare event id.
   168	    [MessagePackObject]
   169	    public class EventChoiceInfo
   170	    {
   171	        [Key(0)] public string ChoiceId { get; set; }
   172	        [Key(1)] public string Label { get; set; }
   173	    }
   174	
   175	    // Sent from the MCP bridge back into Unity when the AI GM (or a vote
   176	    // result) picks an option for the current world event. Completes the
   177	    // write path that SectActionTools.ExecuteDecision was a stub for.
   178	    [MessagePackObject]
   179	    public class ExecuteDecisionMessage
   180	    {
   181	        [Key(0)] public string EventId { get; set; }
   182	        [Key(1)] public string ChoiceId { get; set; }
   183	
   184	        /// <summary>
   185	        /// P13A (APPENDED field — deliberately optional so the existing bridge contract keeps
   186	        /// working unchanged): the instance the decision targets. An old/naive sender leaves
   187	        /// it empty and the decision is matched by <see cref="EventId"/> alone — accepted only
   188	        /// while EXACTLY ONE pending instance has that definition id, and refused as ambiguous
   189	        /// otherwise (never silently applied to whichever one happens to be current).
   190	        /// </summary>
   191	        [Key(2)] public string InstanceId { get; set; }
   192	    }
   193	
   194	    // Published in-memory by DecisionExecutor (not sent over interprocess -
   195	    // this is the "it already happened" notification for local listeners
   196	    // like the in-game log window, distinct from ExecuteDecisionMessage
   197	    // which is the incoming command from the bridge). Whether the decision
   198	    // originated from the bridge or from clicking a choice in the UI,
   199	    // DecisionExecutor is the single place both paths funnel through, so
   200	    // this fires exactly once per decision regardless of source.
   201	    [MessagePackObject]
   202	    public class DecisionExecutedMessage
   203	    {
   204	        [Key(0)] public string EventId { get; set; }
   205	        [Key(1)] public string ChoiceId { get; set; }
   206	
   207	        /// <summary>
   208	        /// P13A (APPENDED field): which occurrence was decided, so a listener can tell two
   209	        /// instances of one definition apart. Empty when the decision came from a caller that
   210	        /// only knew the definition id (legacy/bridge caller) — such a listener falls back to
   211	        /// <see cref="EventId"/>.
   212	        /// </summary>
   213	        [Key(2)] public string InstanceId { get; set; }
   214	    }
   215	
   216	    // Internal-only: entering build (placement) mode. CameraRigController
   217	    // zooms to the Placement preset and GridOverlayRenderer shows the grid;
   218	    // Payload carries the ghost transform when one exists (may be null).
   219	    [MessagePackObject]
   220	    public class BuildModeStartedMessage
   221	    {
   222	        [Key(0)] public string SourceId { get; set; }
   223	        [Key(1)] public string GhostId { get; set; }
   224	    }
   225	
   226	    // Internal-only: leaving build mode (confirm, cancel, or system stop).
   227	    [MessagePackObject]
   228	    public class BuildModeEndedMessage
   229	    {
   230	        [Key(0)] public string SourceId { get; set; }
   231	        [Key(1)] public bool Confirmed { get; set; }
   232	    }
   233	
   234	    // Request/response pair: a disciple buying an item from the sect
   235	    // stockpile (CraftedGoods) with their own contribution. Request-response
   236	    // rather than a fire-and-forget message because the bridge needs to
   237	    // know immediately whether the purchase actually succeeded (enough
   238	    // contribution, enough stock) - same reasoning as SectStateQuery.
   239	    [MessagePackObject]
   240	    public class PurchaseItemRequest
   241	    {
   242	        [Key(0)] public string DiscipleId { get; set; }
   243	        [Key(1)] public string ItemDefId { get; set; }
   244	        [Key(2)] public int Grade { get; set; }
   245	        [Key(3)] public int Quantity { get; set; }
   246	    }
   247	
   248	    [MessagePackObject]
   249	    public class PurchaseItemResponse
   250	    {
   251	        [Key(0)] public bool Success { get; set; }
   252	        [Key(1)] public string Message { get; set; }
   253	        [Key(2)] public long RemainingContribution { get; set; }
   254	    }
   255	
   256	    // ---------- Building placed (Phase 1 — grid placement engine) ----------
   257	    // Published in-memory by SectStateProvider.TryPlaceBuilding after a
   258	    // successful commit. BuildingSystem listens and rebuilds grid occupancy +
   259	    // visuals. ⚠️ Deliberately NOT added to InterprocessTopics — building
   260	    // placement is player-only in Phase 1 (building-system.md §8 Q3 default),
   261	    // so the MCP bridge never needs it on the wire.
   262	    [MessagePackObject]
   263	    public class BuildingPlacedMessage
   264	    {
   265	        [Key(0)] public string InstanceId { get; set; } = string.Empty;
   266	        [Key(1)] public string DefId { get; set; } = string.Empty;
   267	        [Key(2)] public int GridX { get; set; }
   268	        [Key(3)] public int GridZ { get; set; }
   269	        [Key(4)] public int Rotation { get; set; }
   270	    }
   271	
   272	    // ---------- Chibi backend entitlement change (Phase 3) ----------
   273	    // Published in-memory by SectStateProvider.TrySetChibiBackend after mutating
   274	    // DiscipleState.ChibiBackend. DiscipleVisualSystem respawns the visual IN PLACE
   275	    // (same position/activity/facing) on this message.
   276	    // ⚠️ Deliberately NOT added to InterprocessTopics — visual-tier churn is
   277	    // irrelevant to the MCP bridge; keep it off the TCP broker (same rule as
   278	    // every DiscipleVisualSystem message).
   279	    [MessagePackObject]
   280	    public class DiscipleChibiBackendChangedMessage
   281	    {
   282	        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
   283	        [Key(1)] public ChibiBackend Old { get; set; }
   284	        [Key(2)] public ChibiBackend New { get; set; }
   285	    }
   286	
   287	    // ---------- Disciple selected (Phase 4 — chibi click) ----------
   288	    // Published in-memory by ChibiClickTarget when the player clicks a chibi
   289	    // in the gameplay scene. DiscipleDetailUISystem opens the DiscipleDetail
   290	    // panel on this message. ⚠️ Deliberately NOT added to InterprocessTopics —
   291	    // same in-memory-only rule as every DiscipleVisualSystem message.
   292	    [MessagePackObject]
   293	    public class DiscipleSelectedMessage
   294	    {
   295	        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
   296	    }
   297	
   298	    // ---------- Avatar equipment change ----------
   299	    // Broadcast after a successful avatar part change (pub/sub, goes to UI + MCP client)
   300	    [MessagePackObject]
   301	    public class AvatarEquipmentChangedMessage
   302	    {
   303	        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
   304	        [Key(1)] public string Slot { get; set; } = string.Empty;   // AvatarSlots.*
   305	        [Key(2)] public string OldPartId { get; set; } = string.Empty;
   306	        [Key(3)] public string NewPartId { get; set; } = string.Empty;
   307	    }
   308	
   309	    // Request/response pair for changing an avatar part.
   310	    // Request-response (not fire-and-forget) because the bridge needs
   311	    // to know immediately whether the PartId is valid or the disciple
   312	    // id is wrong - same reasoning as PurchaseItemRequest.
   313	    [MessagePackObject]
   314	    public class ChangeAvatarPartRequest
   315	    {
   316	        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
   317	        [Key(1)] public string Slot { get; set; } = string.Empty;
   318	        [Key(2)] public string PartId { get; set; } = string.Empty;   // "" = back to default
   319	    }
   320	
   321	    [MessagePackObject]
   322	    public class ChangeAvatarPartResponse
   323	    {
   324	        [Key(0)] public bool Success { get; set; }
   325	        [Key(1)] public string FailReason { get; set; } = string.Empty;
   326	        [Key(2)] public AvatarAppearance ResultAvatar { get; set; }
   327	    }
   328	
   329	    // ---------- Task assignment (task-system-v2.md §6) ----------
   330	    // Request/response pair: the MCP bridge (or AI GM) asks Unity to assign a
   331	    // task to a disciple. Request-response because the caller needs the
   332	    // permission/validity verdict immediately - same reasoning as
   333	    // PurchaseItemRequest. RequesterId drives the ownership check
   334	    // ("SECT_MASTER" may assign anyone; others only their own disciples).
   335	    [MessagePackObject]
   336	    public class AssignTaskRequest
   337	    {
   338	        [Key(0)] public string RequesterId { get; set; } = string.Empty;
   339	        [Key(1)] public string DiscipleId { get; set; } = string.Empty;
   340	        [Key(2)] public string TaskId { get; set; } = string.Empty;
   341	    }
   342	
   343	    [MessagePackObject]
   344	    public class AssignTaskResponse
   345	    {
   346	        [Key(0)] public bool Success { get; set; }
   347	        [Key(1)] public string FailReason { get; set; } = string.Empty;
   348	    }
   349	
   350	    // Published in-memory by SectStateProvider.TryAssignTask after a successful
   351	    // commit. TaskActivityMapper / visual systems react to the new task.
   352	    // ⚠️ Deliberately NOT added to InterprocessTopics — same in-memory-only
   353	    // rule as DecisionExecutedMessage / BuildingPlacedMessage.
   354	    [MessagePackObject]
   355	    public class DiscipleTaskChangedMessage
   356	    {
   357	        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
   358	        [Key(1)] public string TaskId { get; set; } = string.Empty;
   359	    }
   360	
   361	    // ---------- Disciple ownership change (P4 — local ownership test harness) ----------
   362	    // Published in-memory by SectStateProvider.TrySetDiscipleOwner after a real
   363	    // successful change (no-op when the values are already identical). The local
   364	    // dev harness is the only intended caller — there is no public viewer command.
   365	    // ⚠️ Deliberately NOT added to InterprocessTopics — ownership assignment is a
   366	    // development/debug concern until real account linking exists (P5B+).
   367	    // Observability: the MCP bridge can READ the change log via the
   368	    // OwnershipObservabilityQuery/Snapshot request-response pair below (same
   369	    // mechanism as SectStateQuery — IDistributedSubscriber is unusable from the
   370	    // bridge, see AwaitWorldEventRequest). The bridge can never WRITE ownership:
   371	    // no assignment request/response or tool exists on the wire.
   372	    [MessagePackObject]
   373	    public class DiscipleOwnerChangedMessage
   374	    {
   375	        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
   376	        [Key(1)] public DiscipleOwnerType OldType { get; set; }
   377	        [Key(2)] public string OldOwnerId { get; set; } = string.Empty;
   378	        [Key(3)] public DiscipleOwnerType NewType { get; set; }
   379	        [Key(4)] public string NewOwnerId { get; set; } = string.Empty;
   380	    }
   381	
   382	    // ---------- P9A — explicit Manual/Auto control-mode change ----------
   383	    // Published in-memory by SectStateProvider when a disciple's ControlMode actually
   384	    // changes: an explicit player toggle, an ownership change that invalidates Auto,
   385	    // or a successful manual task assignment taking a disciple out of Auto.
   386	    // ⚠️ Deliberately NOT added to InterprocessTopics — autonomy is a local
   387	    // player/UI concern, same in-memory-only rule as DiscipleOwnerChangedMessage.
   388	    [MessagePackObject]
   389	    public class DiscipleControlModeChangedMessage
   390	    {
   391	        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
   392	        [Key(1)] public DiscipleControlMode OldMode { get; set; }
   393	        [Key(2)] public DiscipleControlMode NewMode { get; set; }
   394	    }
   395	
   396	    // Request/response pair: the MCP bridge reads the disciples' ownership-change
   397	    // log (filled by OwnershipObservabilityBuffer, one entry per real change).
   398	    // Observability ONLY — deliberately no request/response pair or MCP tool that
   399	    // assigns ownership: the only writer stays the local dev harness / fixtures.
   400	    [MessagePackObject]
   401	    public class OwnershipObservabilityQuery
   402	    {
   403	        [Key(0)] public string RequestId { get; set; }
   404	    }
   405	
   406	    [MessagePackObject]
   407	    public class OwnershipObservabilitySnapshot
   408	    {
   409	        [Key(0)] public string RequestId { get; set; }
   410	        [Key(1)] public List<DiscipleOwnerChangedMessage> Events { get; set; } = new List<DiscipleOwnerChangedMessage>();
   411	    }
   412	
   413	    // ---------- P5B observability: task-change log + protection state (READ-ONLY) ----------
   414	    // Both pairs are pulled on demand by the MCP bridge (request-response, same
   415	    // mechanism as OwnershipObservabilityQuery — the bridge cannot
   416	    // IDistributedSubscriber over the TCP transport). Neither pair has a write
   417	    // path: task assignment keeps its own AssignTaskRequest/Response, and these
   418	    // are observation only. DiscipleTaskChangedMessage stays in-memory-only; the
   419	    // bridge pulls the buffered log instead.
   420	    [MessagePackObject]
   421	    public class TaskChangeObservabilityQuery
   422	    {
   423	        [Key(0)] public string RequestId { get; set; }
   424	    }
   425	
   426	    [MessagePackObject]
   427	    public class TaskChangeObservabilitySnapshot
   428	    {
   429	        [Key(0)] public string RequestId { get; set; }
   430	        [Key(1)] public List<DiscipleTaskChangedMessage> Events { get; set; } = new List<DiscipleTaskChangedMessage>();
   431	    }
   432	
   433	    [MessagePackObject]
   434	    public class TaskProtectionQuery
   435	    {
   436	        [Key(0)] public string RequestId { get; set; }
   437	    }
   438	
   439	    /// <summary>
   440	    /// One Viewer-owned disciple (the set the hybrid protection policy governs),
   441	    /// with the read-only answers the bridge needs: is the owner still protected,
   442	    /// how long is left, is the membership data consistent, and may the SectMaster
   443	    /// / the owner change its task right now (with the authority's reason).
   444	    /// Computed from ISectStateProvider.CheckTaskPermission — the same authority
   445	    /// TryAssignTask revalidates, so this view can never disagree with a commit.
   446	    /// </summary>
   447	    [MessagePackObject]
   448	    public class TaskProtectionEntry
   449	    {
   450	        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
   451	        [Key(1)] public string CurrentTask { get; set; } = string.Empty;
   452	        [Key(2)] public DiscipleOwnerType OwnerType { get; set; }
   453	        [Key(3)] public string OwnerId { get; set; } = string.Empty;
   454	        [Key(4)] public bool OwnerProtected { get; set; }
   455	        [Key(5)] public float OwnerProtectionRemainingSeconds { get; set; }
   456	        [Key(6)] public DateTime LastActiveAtUtc { get; set; }
   457	        [Key(7)] public bool MembershipConsistent { get; set; }
   458	        [Key(8)] public bool SectMasterMayChange { get; set; }
   459	        [Key(9)] public string SectMasterReason { get; set; } = string.Empty;
   460	        [Key(10)] public bool OwnerMayChange { get; set; }
   461	        [Key(11)] public string OwnerReason { get; set; } = string.Empty;
   462	    }
   463	
   464	    [MessagePackObject]
   465	    public class TaskProtectionSnapshot
   466	    {
   467	        [Key(0)] public string RequestId { get; set; }
   468	        [Key(1)] public List<TaskProtectionEntry> Entries { get; set; } = new List<TaskProtectionEntry>();
   469	    }
   470	
   471	    // ---------- P5B persistence: viewer membership slice (local disk, not on the wire) ----------
   472	    // Persisted together so the registry ⇄ disciple ownership pair can never be
   473	    // restored half-way (invariants #1/#2/#3 hold across sessions). Deliberately
   474	    // a SLICE, not the whole SectEconomyState: only membership + ownership +
   475	    // activity survive; economy/roster still come from MockSectData.
   476	    [MessagePackObject]
   477	    public class SectViewerMembershipSave
   478	    {
   479	        /// <summary>Only this shape is accepted; anything else is ignored on load (fail-closed to mock start).</summary>
   480	        public const int CurrentVersion = 1;
   481	
   482	        /// <summary>Bumped only if the shape changes incompatibly; a mismatch is ignored on load (fail-closed to mock start).</summary>
   483	        [Key(0)] public int Version { get; set; } = CurrentVersion;
   484	
   485	        /// <summary>When the file was written (UTC) — informational.</summary>
   486	        [Key(1)] public DateTime SavedAtUtc { get; set; }
   487	
   488	        [Key(2)] public List<ViewerRecord> Records { get; set; } = new List<ViewerRecord>();
   489	        [Key(3)] public List<PendingViewerApplication> PendingApplications { get; set; } = new List<PendingViewerApplication>();
   490	
   491	        /// <summary>discipleId → owner, captured in the SAME snapshot so ownership and registry agree on load.</summary>
   492	        [Key(4)] public List<SectSavedOwnership> OwnerByDisciple { get; set; } = new List<SectSavedOwnership>();
   493	    }
   494	
   495	    [MessagePackObject]
   496	    public class SectSavedOwnership
   497	    {
   498	        [Key(0)] public string DiscipleId { get; set; } = string.Empty;
   499	        [Key(1)] public DiscipleOwnerType OwnerType { get; set; }
   500	        [Key(2)] public string OwnerId { get; set; } = string.Empty;
   501	    }
   502	
   503	    // ---------- P11A — session restored notification ----------
   504	    // Published in-memory AFTER a full-session snapshot has been committed to live
   505	    // state (SectStateProvider + TimeSystem). Derived systems react by rebuilding
   506	    // caches and dropping stale session references:
   507	    //   - BuildingSystem       rebuilds grid occupancy + placed markers
   508	    //   - DiscipleVisualSystem reconciles (despawn gone / spawn restored)
   509	    //   - AutoTaskScheduler    clears its simulation-time scheduling maps
   510	    //   - WalletHudPresenter   re-reads the authoritative wallet/stockpile
   511	    // ⚠️ Deliberately NOT added to InterprocessTopics — session load is a local
   512	    // player concern; the bridge observes state via SectStateQuery, not this.
   513	    // Never published on a failed restore (invalid data leaves the session unchanged).
   514	    [MessagePackObject]
   515	    public class SessionRestoredMessage
   516	    {
   517	        /// <summary>True when a full-session snapshot was committed (as opposed to a legacy slice).</summary>
   518	        [Key(0)] public bool FullSession { get; set; }
   519	
   520	        /// <summary>UTC time the restore committed.</summary>
   521	        [Key(1)] public DateTime RestoredAtUtc { get; set; }
   522	
   523	        /// <summary>Roster size after the restore (diagnostic / UI priming).</summary>
   524	        [Key(2)] public int DiscipleCount { get; set; }
   525	
   526	        /// <summary>Placed-building count after the restore (diagnostic / UI priming).</summary>
   527	        [Key(3)] public int BuildingCount { get; set; }
   528	    }
   529	
   530	    // Topic keys for the keyed (IDistributedPublisher<TKey,TMessage>) channels.
   531	    public static class InterprocessTopics
   532	    {
   533	        public const string DiscipleRecruited = "sect.disciple_recruited";
   534	        public const string DiscipleRankChanged = "sect.disciple_rank_changed";
   535	        public const string ResourceChanged = "sect.resource_changed";
   536	        public const string ContributionEarned = "sect.contribution_earned";
   537	        public const string WorldEvent = "sect.world_event";
   538	        public const string ExecuteDecision = "sect.execute_decision";
   539	        public const string AvatarEquipmentChanged = "sect.avatar_equipment_changed";
   540	    }
   541	}
````

### Shared/SectEconomyState.cs (12696 bytes)
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
    20	    /// P9A — who decides a disciple's task: the human/owner (Manual) or the trusted
    21	    /// auto-assigner (Auto). Ownership and autonomy are DIFFERENT: an Npc-owned
    22	    /// disciple is Manual until someone explicitly opts it in, and only Npc-owned
    23	    /// disciples are eligible for Auto in this MVP. 0 = Manual so state/roster saved
    24	    /// before this key existed deserializes as Manual (backward-compatible behavior).
    25	    /// </summary>
    26	    public enum DiscipleControlMode
    27	    {
    28	        Manual = 0,
    29	        Auto = 1,
    30	    }
    31	
    32	    /// <summary>
    33	    /// Entitlement ของศิษย์: "ได้สิทธิ์" แสดง chibi ในฉากด้วย backend ไหน — ไม่ใช่สิ่งที่ render จริงเสมอไป
    34	    /// (runtime อาจ degrade Spine → SpriteSheet เมื่อเกิน SpineBudget โดยไม่แก้ค่านี้ — ดู §7 ของแผน)
    35	    /// 0 = default → save/roster เก่าที่ไม่มี [Key(8)] deserialize ได้ SpriteSheet (L5)
    36	    /// </summary>
    37	    public enum ChibiBackend
    38	    {
    39	        SpriteSheet = 0,
    40	        Spine = 1
    41	    }
    42	
    43	    [MessagePackObject]
    44	    public class CurrencyWallet
    45	    {
    46	        [Key(0)] public long SpiritStones { get; set; }
    47	        [Key(1)] public long Contribution { get; set; }
    48	    }
    49	
    50	    [MessagePackObject]
    51	    public class InventoryItem
    52	    {
    53	        [Key(0)] public string ItemDefId { get; set; }
    54	        [Key(1)] public int Quantity { get; set; }
    55	        [Key(2)] public int Grade { get; set; }          // 1-5
    56	        [Key(3)] public OwnerScope OwnerScope { get; set; }
    57	    }
    58	
    59	    [MessagePackObject]
    60	    public class DiscipleState
    61	    {
    62	        [Key(0)] public string DiscipleId { get; set; }
    63	        [Key(1)] public string DisplayName { get; set; }
    64	        [Key(2)] public DiscipleRank Rank { get; set; }
    65	        [Key(3)] public CurrencyWallet Wallet { get; set; } = new CurrencyWallet();
    66	        [Key(4)] public List<InventoryItem> PersonalInventory { get; set; } = new List<InventoryItem>();
    67	        [Key(5)] public string CurrentTask { get; set; }
    68	        [Key(6)] public AvatarAppearance Avatar { get; set; } = new AvatarAppearance();
    69	        [Key(7)] public DiscipleSex Sex { get; set; } = DiscipleSex.Unspecified;
    70	        /// <summary>Entitlement (แกนแยกจาก AvatarAppearance) — default SpriteSheet เมื่อ deserialize state เก่า (L1/L5)</summary>
    71	        [Key(8)] public ChibiBackend ChibiBackend { get; set; } = ChibiBackend.SpriteSheet;
    72	
    73	        /// <summary>Ownership kind (task-system-v2.md §1.1) — append-only; old state default Npc</summary>
    74	        [Key(9)] public DiscipleOwnerType OwnerType { get; set; } = DiscipleOwnerType.Npc;
    75	        /// <summary>Owner identity (player/viewer id); empty for Npc-owned</summary>
    76	        [Key(10)] public string OwnerId { get; set; } = string.Empty;
    77	
    78	        /// <summary>
    79	        /// P9A — explicit autonomy (see DiscipleControlMode). Default Manual; only an
    80	        /// Npc-owned disciple may be Auto. Append-only key; old state without [Key(11)]
    81	        /// deserializes as Manual.
    82	        /// </summary>
    83	        [Key(11)] public DiscipleControlMode ControlMode { get; set; } = DiscipleControlMode.Manual;
    84	
    85	        /// <summary>
    86	        /// P10A — attribute DATA only (stamina + skill XP; see DiscipleAttributes).
    87	        /// Append-only key; old state without [Key(12)] deserializes to a class default
    88	        /// and is repaired by <see cref="DiscipleAttributes.Normalize"/> on load/import.
    89	        /// </summary>
    90	        [Key(12)] public DiscipleAttributes Attributes { get; set; } = new DiscipleAttributes();
    91	    }
    92	
    93	    [MessagePackObject]
    94	    public class SectStockpile
    95	    {
    96	        [Key(0)] public Dictionary<string, int> RawResources { get; set; } = new Dictionary<string, int>();
    97	        [Key(1)] public List<InventoryItem> CraftedGoods { get; set; } = new List<InventoryItem>();
    98	    }
    99	
   100	    /// <summary>
   101	    /// อาคาร 1 หลังที่วางบน grid สำนักแล้ว (building-system.md §3.3)
   102	    /// GridX/GridZ = มุมบนซ้าย (cell แรก) ของ footprint; Rotation = 0/90/180/270
   103	    /// (90/270 ทำให้ footprint กว้าง×สูงสลับกัน — ดู BuildingGrid.ResolveFootprint)
   104	    /// </summary>
   105	    [MessagePackObject]
   106	    public class PlacedBuildingState
   107	    {
   108	        [Key(0)] public string InstanceId { get; set; }
   109	        [Key(1)] public string DefId { get; set; }
   110	        [Key(2)] public int GridX { get; set; }
   111	        [Key(3)] public int GridZ { get; set; }
   112	        [Key(4)] public int Rotation { get; set; }
   113	    }
   114	
   115	    [MessagePackObject]
   116	    public class SectEconomyState
   117	    {
   118	        [Key(0)] public List<DiscipleState> Disciples { get; set; } = new List<DiscipleState>();
   119	        [Key(1)] public SectStockpile Stockpile { get; set; } = new SectStockpile();
   120	        /// <summary>Append-only key (building-system.md §3.3) — ห้ามแก้ [Key(0)]/[Key(1)] เดิม</summary>
   121	        [Key(2)] public List<PlacedBuildingState> PlacedBuildings { get; set; } = new List<PlacedBuildingState>();
   122	        /// <summary>
   123	        /// P5A append-only key — สมุดทะเบียนสมาชิกผู้ชม + คำขอรออนุมัติ (แยก list ตาม P5A §4).
   124	        /// Old saves without [Key(3)] deserialize as empty registry (fail-closed: ไม่มีสมาชิกจนกว่าจะมี mutation).
   125	        /// ⚠️ ยังไม่มี save/load mechanism ใน repo (open-questions §14) — contract เท่านั้น,
   126	        /// cross-session reclaim ยังใช้ไม่ได้จนกว่าจะมี persistence จริง
   127	        /// </summary>
   128	        [Key(3)] public SectViewerRegistry ViewerRegistry { get; set; } = new SectViewerRegistry();
   129	
   130	        public byte[] ToByteArray() => MessagePackSerializer.Serialize(this);
   131	
   132	        public static SectEconomyState FromByteArray(byte[] bytes)
   133	        {
   134	            var state = MessagePackSerializer.Deserialize<SectEconomyState>(bytes);
   135	            NormalizeDisciples(state); // P10A — repair attributes on every load/import
   136	            return state;
   137	        }
   138	
   139	        /// <summary>
   140	        /// P10A — normalize every disciple's attributes (null / NaN / Infinity / range /
   141	        /// missing category keys) on a load/import boundary. Null-safe; a null or empty
   142	        /// roster is a no-op. Never treats a saved 0 as missing.
   143	        /// </summary>
   144	        public static void NormalizeDisciples(SectEconomyState state)
   145	        {
   146	            if (state == null || state.Disciples == null) return;
   147	
   148	            for (int i = 0; i < state.Disciples.Count; i++)
   149	            {
   150	                var disciple = state.Disciples[i];
   151	                if (disciple == null) continue;
   152	                disciple.Attributes = DiscipleAttributes.Normalize(disciple.Attributes);
   153	            }
   154	        }
   155	    }
   156	
   157	    // --- Avatar System (New Dictionary-Based Schema) ---
   158	
   159	    /// <summary>
   160	    /// Struct สำหรับ Factory Method FromSlots (เลี่ยง Value Tuple เพื่อความเข้ากันได้กับ Unity ทุกเวอร์ชัน)
   161	    /// </summary>
   162	    public struct SlotPart
   163	    {
   164	        public string Slot;
   165	        public string PartId;
   166	        public SlotPart(string slot, string partId)
   167	        {
   168	            Slot = slot;
   169	            PartId = partId;
   170	        }
   171	    }
   172	
   173	    /// <summary>
   174	    /// หน้าตาของตัวละคร — dictionary-based รองรับ slot จำนวน任意
   175	    /// Parts: slot → partId (ว่าง = ใช้ default ของ slot นั้น)
   176	    /// Colors: slot → colorId (มีเฉพาะ slot ที่ tintable)
   177	    /// </summary>
   178	    [MessagePackObject]
   179	    public sealed class AvatarAppearance
   180	    {
   181	        [Key(0)] public Dictionary<string, string> Parts { get; set; } = new Dictionary<string, string>();
   182	        [Key(1)] public Dictionary<string, string> Colors { get; set; } = new Dictionary<string, string>();
   183	        [Key(2)] public string PoseId { get; set; } = string.Empty;
   184	
   185	        public string GetSlot(string slot)
   186	        {
   187	            string v;
   188	            return Parts.TryGetValue(slot, out v) ? v : string.Empty;
   189	        }
   190	
   191	        public bool SetSlot(string slot, string partId)
   192	        {
   193	            if (string.IsNullOrEmpty(partId)) { Parts.Remove(slot); return true; }
   194	            Parts[slot] = partId;
   195	            return true;
   196	        }
   197	
   198	        public string GetColor(string slot)
   199	        {
   200	            string v;
   201	            return Colors.TryGetValue(slot, out v) ? v : string.Empty;
   202	        }
   203	
   204	        public void SetColor(string slot, string colorId)
   205	        {
   206	            if (string.IsNullOrEmpty(colorId)) { Colors.Remove(slot); return; }
   207	            Colors[slot] = colorId;
   208	        }
   209	
   210	        public AvatarAppearance Clone()
   211	        {
   212	            var c = new AvatarAppearance();
   213	            c.Parts = new Dictionary<string, string>(Parts);
   214	            c.Colors = new Dictionary<string, string>(Colors);
   215	            c.PoseId = PoseId;
   216	            return c;
   217	        }
   218	
   219	        [IgnoreMember]
   220	        /// <summary>Effective pose: PoseId if set, else fallback to "pose_idle_01".</summary>
   221	        public string EffectivePose
   222	        {
   223	            get { return string.IsNullOrEmpty(PoseId) ? "pose_idle_01" : PoseId; }
   224	        }
   225	
   226	        /// <summary>Factory สำหรับสร้าง AvatarAppearance จาก list — ใช้ใน MockData</summary>
   227	        public static AvatarAppearance FromSlots(params SlotPart[] slots)
   228	        {
   229	            var a = new AvatarAppearance();
   230	            for (int i = 0; i < slots.Length; i++)
   231	            {
   232	                if (!string.IsNullOrEmpty(slots[i].PartId))
   233	                    a.Parts[slots[i].Slot] = slots[i].PartId;
   234	            }
   235	            return a;
   236	        }
   237	    }
   238	
   239	    /// <summary>ชื่อ slot แบบ const string + Categories สำหรับ UI</summary>
   240	    public static class AvatarSlots
   241	    {
   242	        public const string Base = "base";
   243	        public const string Body = "body";
   244	        public const string Head = "head";
   245	        public const string Eyes = "eyes";
   246	        public const string Brows = "brows";
   247	        public const string Mouth = "mouth";
   248	        public const string Nose = "nose";
   249	        public const string Hair = "hair";
   250	        public const string FaceMarking = "face_marking";
   251	        public const string Eyeshadow = "eyeshadow";
   252	        public const string Accessory = "accessory";
   253	
   254	        public static readonly string[] Equippable =
   255	        {
   256	            Body, Head, Eyes, Brows, Mouth, Nose,
   257	            Hair, FaceMarking, Eyeshadow, Accessory
   258	        };
   259	
   260	        public static readonly IReadOnlyDictionary<string, string[]> Categories =
   261	            new Dictionary<string, string[]>
   262	            {
   263	                { "ใบหน้า", new[] { Head, Eyes, Brows, Mouth, Nose } }, // Head รวมอยู่ในใบหน้าด้วย
   264	                { "ลักษณะ", new[] { Hair, FaceMarking, Eyeshadow } },
   265	                { "ร่างกาย", new[] { Body, Accessory } },
   266	            };
   267	
   268	        public static readonly IReadOnlyDictionary<string, string> Labels =
   269	            new Dictionary<string, string>
   270	            {
   271	                { Body,       "เสื้อผ้า" },
   272	                { Head,       "ใบหน้า" },
   273	                { Eyes,       "ตา" },
   274	                { Brows,      "คิ้ว" },
   275	                { Mouth,      "ปาก" },
   276	                { Nose,       "จมูก" },
   277	                { Hair,       "ทรงผม" },
   278	                { FaceMarking,"ลายหน้า" },
   279	                { Eyeshadow,  "อายแชโดว์" },
   280	                { Accessory,  "เครื่องประดับ" },
   281	            };
   282	    }
   283	}````

### Shared/MockSectData.cs (7200 bytes)
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
    50	                ),
    51	                // P10A TEST DATA (prototype): d000 = no skill XP.
    52	                Attributes = TestAttributes(0f, 0f, 0f)
    53	            });
    54	
    55	            // d001: Lin Feng (Outer Disciple)
    56	            state.Disciples.Add(new DiscipleState
    57	            {
    58	                DiscipleId = "d001",
    59	                DisplayName = "Lin Feng",
    60	                Sex = DiscipleSex.Female,
    61	                ChibiBackend = ChibiBackend.SpriteSheet, // Outer = SpriteSheet (แผน §4.1)
    62	                Rank = DiscipleRank.OuterDisciple,
    63	                Wallet = new CurrencyWallet { SpiritStones = 12, Contribution = 340 },
    64	                CurrentTask = "meditation", // was gathering_herb — herb_plot is no longer in start state, gate would block it anyway
    65	                PersonalInventory = new List<InventoryItem>(),
    66	                Avatar = AvatarAppearance.FromSlots(
    67	                    new SlotPart(AvatarSlots.Body, "body_robe_grey"),
    68	                    new SlotPart(AvatarSlots.Head, "head_male_01"),
    69	                    new SlotPart(AvatarSlots.Hair, "hair_short")
    70	                ),
    71	                // P10A TEST DATA (prototype): gathering-leaning founder.
    72	                Attributes = TestAttributes(200f, 0f, 0f)
    73	            });
    74	
    75	            // d002: Su Yan (Inner Disciple - With face marking + twin tail)
    76	            state.Disciples.Add(new DiscipleState
    77	            {
    78	                DiscipleId = "d002",
    79	                DisplayName = "Su Yan",
    80	                Sex = DiscipleSex.Female,
    81	                ChibiBackend = ChibiBackend.SpriteSheet, // Inner = SpriteSheet (แผน §4.1)
    82	                Rank = DiscipleRank.InnerDisciple,
    83	                Wallet = new CurrencyWallet { SpiritStones = 45, Contribution = 1120 },
    84	                CurrentTask = "meditation", // was refining_elixir — pill_hall not in start state, gate would block it anyway
    85	                PersonalInventory = new List<InventoryItem>(),
    86	                Avatar = AvatarAppearance.FromSlots(
    87	                    new SlotPart(AvatarSlots.Body, "body_robe_white"),
    88	                    new SlotPart(AvatarSlots.Head, "head_female_01"),
    89	                    new SlotPart(AvatarSlots.Hair, "hair_twin_tail"),
    90	                    new SlotPart(AvatarSlots.FaceMarking, "face_marking_red_dot"),
    91	                    new SlotPart(AvatarSlots.Accessory, "acc_hairpin_silver")
    92	                ),
    93	                // P10A TEST DATA (prototype): alchemy-leaning founder.
    94	                Attributes = TestAttributes(0f, 200f, 0f)
    95	            });
    96	
    97	            // d003: Elder Zhao (Elder - Bald + Beard)
    98	            state.Disciples.Add(new DiscipleState
    99	            {
   100	                DiscipleId = "d003",
   101	                DisplayName = "Elder Zhao",
   102	                Sex = DiscipleSex.Male,
   103	                ChibiBackend = ChibiBackend.Spine, // Elder = Spine tier (แผน §4.1)
   104	                Rank = DiscipleRank.Elder,
   105	                Wallet = new CurrencyWallet { SpiritStones = 210, Contribution = 4300 },
   106	                CurrentTask = "meditation", // was forging_artifact — forge not in start state, gate would block it anyway
   107	                PersonalInventory = new List<InventoryItem>
   108	                {
   109	                    new InventoryItem
   110	                    {
   111	                        ItemDefId = "sword_azure_flame",
   112	                        Quantity = 1,
   113	                        Grade = 5,
   114	                        OwnerScope = OwnerScope.Personal
   115	                    }
   116	                },
   117	                Avatar = AvatarAppearance.FromSlots(
   118	                    new SlotPart(AvatarSlots.Body, "body_robe_black"),
   119	                    new SlotPart(AvatarSlots.Head, "head_male_elder"),
   120	                    new SlotPart(AvatarSlots.Hair, "hair_bald_beard"),
   121	                    new SlotPart(AvatarSlots.Accessory, "acc_gourd")
   122	                ),
   123	                // P10A TEST DATA (prototype): forging-leaning founder.
   124	                Attributes = TestAttributes(0f, 0f, 400f)
   125	            });
   126	
   127	            return state;
   128	        }
   129	
   130	        /// <summary>
   131	        /// P10A TEST DATA (prototype) — build a normalized attribute block for a
   132	        /// founder. Stamina is 100 for everyone; the four founders differ only in
   133	        /// which skill category they have XP in, so later phases can tell them apart.
   134	        /// These are placeholders, NOT balance values.
   135	        /// </summary>
   136	        private static DiscipleAttributes TestAttributes(float gatheringXp, float alchemyXp, float forgingXp)
   137	        {
   138	            var attributes = new DiscipleAttributes { Stamina = DiscipleAttributesConfig.StaminaDefault };
   139	            attributes.SkillXp[DiscipleAttributesConfig.CategoryGathering] = gatheringXp;
   140	            attributes.SkillXp[DiscipleAttributesConfig.CategoryAlchemy] = alchemyXp;
   141	            attributes.SkillXp[DiscipleAttributesConfig.CategoryForging] = forgingXp;
   142	            return DiscipleAttributes.Normalize(attributes);
   143	        }
   144	    }
   145	}````

### McpBridge/Program.cs (9561 bytes)
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
   130	    [McpServerTool, Description("Execute a decision for a world event. The decision is applied at most once - a duplicate or a decision for an event that is no longer pending is refused by Unity. Pass instanceId to target a specific occurrence when the same event id is pending more than once.")]
   131	    public static async Task<string> ExecuteDecision(
   132	        IDistributedPublisher<string, ExecuteDecisionMessage> publisher,
   133	        [Description("Event id from await_next_world_event")] string eventId,
   134	        [Description("Chosen option id")] string choiceId,
   135	        // P13A — optional so the existing two-argument call keeps working unchanged. Unity
   136	        // refuses an omitted id when the event id alone is ambiguous (more than one pending
   137	        // occurrence), so it can never silently decide the wrong one; the id to pass back is
   138	        // returned by await_next_world_event as "InstanceId".
   139	        [Description("Instance id from await_next_world_event (recommended; omit only when a single occurrence of eventId is pending)")] string instanceId = null)
   140	    {
   141	        await publisher.PublishAsync(
   142	            InterprocessTopics.ExecuteDecision,
   143	            new ExecuteDecisionMessage { EventId = eventId, ChoiceId = choiceId, InstanceId = instanceId });
   144	
   145	        return $"Decision sent to Unity: event={eventId} choice={choiceId}" +
   146	               (string.IsNullOrEmpty(instanceId) ? string.Empty : $" instance={instanceId}");
   147	    }
   148	
   149	    [McpServerTool, Description("Have a disciple buy an item from the sect stockpile using their contribution.")]
   150	    public static async Task<string> PurchaseItem(
   151	        IRemoteRequestHandler<PurchaseItemRequest, PurchaseItemResponse> requestHandler,
   152	        [Description("Disciple id, e.g. from get_sect_state")] string discipleId,
   153	        [Description("Item id, e.g. elixir_qi_gathering")] string itemDefId,
   154	        [Description("Item grade, from the stockpile entry in get_sect_state")] int grade,
   155	        [Description("How many to buy")] int quantity)
   156	    {
   157	        var response = await requestHandler.InvokeAsync(new PurchaseItemRequest
   158	        {
   159	            DiscipleId = discipleId,
   160	            ItemDefId = itemDefId,
   161	            Grade = grade,
   162	            Quantity = quantity,
   163	        });
   164	
   165	        return response.Message;
   166	    }
   167	
   168	    [McpServerTool, Description("Assign a task to a disciple. Always sent as SECT_MASTER (the AI GM acts as the sect master, so it may assign any disciple).")]
   169	    public static async Task<string> AssignTask(
   170	        IRemoteRequestHandler<AssignTaskRequest, AssignTaskResponse> requestHandler,
   171	        [Description("Disciple id, e.g. from get_sect_state")] string discipleId,
   172	        [Description("Task id: a gathering or crafting task id, or \"meditation\"")] string taskId)
   173	    {
   174	        var response = await requestHandler.InvokeAsync(new AssignTaskRequest
   175	        {
   176	            RequesterId = "SECT_MASTER",
   177	            DiscipleId = discipleId,
   178	            TaskId = taskId,
   179	        });
   180	
   181	        return response.Success
   182	            ? $"Task '{taskId}' assigned to {discipleId}."
   183	            : response.FailReason;
   184	    }
   185	}
````

### UnityProject/Assets/Scripts/Core/GameLifetimeScope.cs (4173 bytes)
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
    54	            // P12A — SceneLoader ไม่ auto-load เองอีกต่อไป (GameSessionCoordinator เป็น
    55	            // เจ้าของ lifecycle). ยัง register เป็น entry point + AsSelf (Editor verify
    56	            // runners resolve concrete) และเพิ่ม As<IGameplaySceneLoader> ให้ coordinator
    57	            // ฉีด seam เดียวกัน — ยังมี loader ตัวเดียวในโปรเจกต์
    58	            builder.RegisterEntryPoint<SceneLoader>(Lifetime.Singleton)
    59	                   .AsSelf()
    60	                   .As<IGameplaySceneLoader>();
    61	            builder.RegisterComponentInHierarchy<AdditiveSceneTest>();
    62	            // Design-time data now sourced from Luban (see DataTables/ at
    63	            // the workspace root and Assets/Scripts/Data/LubanEventPool.cs),
    64	            // not a ScriptableObject dragged into the Inspector - so this is
    65	            // constructed directly rather than serialized.
    66	            builder.RegisterInstance(new LubanEventPool());
    67	
    68	            // Avatar part definitions loaded from Resources/Data/avatar_parts.json
    69	            builder.Register<AvatarPartPool>(Lifetime.Singleton);
    70	
    71	            // ลำดับด้านล่าง = ลำดับ registration เดิมทุกบรรทัด (Building → Visual → UI →
    72	            // interprocess → rig ports/gameplay entries) — ห้ามสลับ (ดูหัวไฟล์)
    73	            builder.RegisterBuildingSystem();
    74	            builder.RegisterVisualSystem();
    75	            builder.RegisterUI(uiRoot, uiPanelCatalog);
    76	            builder.RegisterInterprocess(interprocessHost, interprocessPort);
    77	            builder.RegisterGameplaySystems();
    78	        }
    79	    }
    80	}
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

### UnityProject/Assets/Scripts/Core/TimeSystem.cs (57202 bytes)
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
    16	    //
    17	    // NOTE (P10 simulation time): TimeSystem deliberately does NOT implement ITickable.
    18	    // Simulation time is derived on demand by SimulationDelta, and game speed is applied
    19	    // ONLY there — there is intentionally no per-frame hook a second speed multiply could
    20	    // be added to later.
    21	    /// <summary>
    22	    /// E1 — explicit pause owners. A flags set, not one boolean: a decision pause
    23	    /// can never be mistaken for a player pause, and clearing one never clears the
    24	    /// other.
    25	    ///
    26	    /// P13A defines <see cref="SessionTransition"/> (held by
    27	    /// <see cref="GameSessionCoordinator"/> for the duration of a transition), so the
    28	    /// event pause releasing can never resume a transition that is still in flight.
    29	    /// </summary>
    30	    [System.Flags]
    31	    public enum TimePauseReason
    32	    {
    33	        None = 0,
    34	        /// <summary>The player paused (TimeControl HUD / Space hotkey).</summary>
    35	        User = 1,
    36	        /// <summary>A decision-requiring world event is outstanding.</summary>
    37	        PendingDecision = 2,
    38	        /// <summary>P13A — a session transition is in flight (GameSessionCoordinator owns this one).</summary>
    39	        SessionTransition = 4,
    40	    }
    41	
    42	    /// <summary>
    43	    /// P13A — ONE outstanding, undecided world event.
    44	    ///
    45	    /// <see cref="InstanceId"/> identifies this OCCURRENCE; <see cref="EventId"/> is the
    46	    /// authored definition it was raised from, and many instances can share one definition.
    47	    /// A decision is addressed to an instance, never to a definition, so a replayed, stale
    48	    /// or duplicate decision can never resolve a different occurrence of the same event.
    49	    /// Instances are only ever created by <see cref="TimeSystem.RaiseWorldEvent"/> (or by a
    50	    /// restore, which re-mints one) and are removed only by resolving or clearing them.
    51	    /// </summary>
    52	    public sealed class PendingWorldEvent
    53	    {
    54	        public PendingWorldEvent(string instanceId, string eventId, string description, List<EventChoiceInfo> choices,
    55	                                 string speakerDiscipleId = null, string speakerDisplayName = null)
    56	        {
    57	            InstanceId = instanceId;
    58	            EventId = eventId;
    59	            Description = description;
    60	            Choices = choices ?? new List<EventChoiceInfo>();
    61	            SpeakerDiscipleId = speakerDiscipleId ?? string.Empty;
    62	            SpeakerDisplayName = speakerDisplayName ?? string.Empty;
    63	        }
    64	
    65	        /// <summary>Unique per raised occurrence (never reused within a session; stable across a save/load).</summary>
    66	        public string InstanceId { get; }
    67	
    68	        /// <summary>The authored event definition id (<c>cfg.game.EventDef.Id</c>).</summary>
    69	        public string EventId { get; }
    70	
    71	        /// <summary>Authoring text shown to the player / the AI GM.</summary>
    72	        public string Description { get; }
    73	
    74	        /// <summary>The choices this occurrence offers (never null).</summary>
    75	        public IReadOnlyList<EventChoiceInfo> Choices { get; }
    76	
    77	        /// <summary>
    78	        /// P13B — optional speaker: the disciple whose portrait the popup renders. Empty for an
    79	        /// event with no speaker (the popup then draws no portrait at all rather than a blank one).
    80	        /// This is the instance-specific payload that has to survive a save, and it is the ONLY
    81	        /// part of the popup a restore needs — the portrait itself is rebuilt from the roster.
    82	        /// </summary>
    83	        public string SpeakerDiscipleId { get; }
    84	
    85	        /// <summary>Display name shown beside the portrait; empty when <see cref="SpeakerDiscipleId"/> is.</summary>
    86	        public string SpeakerDisplayName { get; }
    87	
    88	        /// <summary>True when this occurrence names a speaker to render.</summary>
    89	        public bool HasSpeaker => !string.IsNullOrEmpty(SpeakerDiscipleId);
    90	
    91	        /// <summary>
    92	        /// Exact choice-id match against THIS occurrence's choices — never a substring or
    93	        /// prefix test, and never a lookup into the definition's authored rows, so a choice
    94	        /// that was not offered here can never be accepted.
    95	        /// </summary>
    96	        public bool HasChoice(string choiceId)
    97	        {
    98	            for (int i = 0; i < Choices.Count; i++)
    99	            {
   100	                var choice = Choices[i];
   101	                if (choice != null && choice.ChoiceId == choiceId) return true;
   102	            }
   103	            return false;
   104	        }
   105	
   106	        public override string ToString() => $"{InstanceId}({EventId})";
   107	    }
   108	
   109	    public class TimeSystem : IStartable
   110	    {
   111	        private readonly IPublisher<TimeSpeedChangedMessage> _speedPublisher;
   112	        private readonly IPublisher<WorldEventTriggeredMessage> _worldEventPublisher;
   113	        private readonly TimeRuntimeConfig _config;
   114	
   115	        /// <summary>Lowest supported simulation speed (1x).</summary>
   116	        public const int MinSpeed = 1;
   117	        /// <summary>Highest supported simulation speed (3x).</summary>
   118	        public const int MaxSpeed = 3;
   119	
   120	        // Initialised to 1x (SetSpeed only publishes the UI-facing change message).
   121	        private int _speed = 1;
   122	        private TimePauseReason _reasons = TimePauseReason.None;
   123	
   124	        // P12A — whether a live gameplay session is active. Deliberately a SEPARATE fact
   125	        // from the pause reasons: "no active game" (Title / session transition) is not a
   126	        // player pause and not a pending-decision pause, so SimulationDelta freezes for
   127	        // either, while IsPaused / IsUserPaused / IsPendingDecisionPaused stay honest.
   128	        // Defaults to active so a TimeSystem built outside the session coordinator
   129	        // (tests / legacy construction) keeps its previous behaviour.
   130	        private bool _sessionActive = true;
   131	
   132	        // Completed (and replaced with a fresh one) every time a world
   133	        // event fires - see WaitForNextWorldEventAsync()/RaiseWorldEvent().
   134	        private UniTaskCompletionSource<AwaitWorldEventResponse> _pendingEventSource =
   135	            new UniTaskCompletionSource<AwaitWorldEventResponse>();
   136	
   137	        // If a decision-requiring event fired before anyone called
   138	        // await_next_world_event, hand it back immediately on the next call
   139	        // instead of making a late caller wait for a completely new event.
   140	        // Cleared once handed out - a second call with nothing new pending
   141	        // goes back to waiting normally.
   142	        //
   143	        // NOTE (E2-lite): this cache is the BRIDGE's handoff and its semantics are
   144	        // deliberately unchanged (cleared on read). The authoritative pending
   145	        // decision below is a SEPARATE concern and is never cleared by a read.
   146	        //
   147	        // P13A: this stays a single slot ON PURPOSE. It is "the newest event to hand a
   148	        // late awaiter", not pending-state storage — that is _pendingDecisions below,
   149	        // which is bounded and whose entries are never overwritten.
   150	        private AwaitWorldEventResponse _cachedPendingEvent;
   151	
   152	        /// <summary>
   153	        /// P13A — how many undecided events may be outstanding at once. The bound is
   154	        /// deliberate: the pending state is authoritative and must not grow without limit.
   155	        /// At the cap a further decision event is REFUSED, loudly (see
   156	        /// <see cref="RaiseWorldEvent"/>); an already-pending decision is never evicted,
   157	        /// replaced or overwritten to make room for a newer one.
   158	        /// </summary>
   159	        public const int MaxPendingDecisions = 8;
   160	
   161	        // P13A — authoritative pending decisions in ARRIVAL ORDER (oldest first). The head
   162	        // (index 0) is the CURRENT event: it is what the UI opens, what the bridge handoff
   163	        // cache carries, and what a snapshot captures. Any pending instance may be resolved
   164	        // by its own instance id, which removes exactly that one.
   165	        //
   166	        // This replaces E2-lite's single-slot fields, which silently overwrote an
   167	        // outstanding decision as soon as a second one was raised.
   168	        private readonly List<PendingWorldEvent> _pendingDecisions = new List<PendingWorldEvent>();
   169	        private int _nextEventInstanceNumber = 1;
   170	        private static readonly List<EventChoiceInfo> NoChoices = new List<EventChoiceInfo>();
   171	
   172	        /// <summary>Prefix of a minted instance id; the numeric suffix is what a restore re-seeds past.</summary>
   173	        private const string InstanceIdPrefix = "evt_";
   174	
   175	        public TimeSystem(
   176	            IPublisher<TimeSpeedChangedMessage> speedPublisher,
   177	            IPublisher<WorldEventTriggeredMessage> worldEventPublisher,
   178	            TimeRuntimeConfig config = null)
   179	        {
   180	            _speedPublisher = speedPublisher;
   181	            _worldEventPublisher = worldEventPublisher;
   182	            _config = config ?? TimeRuntimeConfig.Instance;
   183	        }
   184	
   185	        public void Start()
   186	        {
   187	            // Unconditional initial publish so any listener that subscribes after
   188	            // Start still gets one refresh cue (the HUD also reads state on bind).
   189	            PublishState();
   190	        }
   191	
   192	        /// <summary>Adds a pause reason (idempotent). Publishes only on a real change.</summary>
   193	        public void Pause(TimePauseReason reason) => SetReason(reason, true);
   194	
   195	        /// <summary>Clears a pause reason. Never touches any other reason.</summary>
   196	        public void Resume(TimePauseReason reason) => SetReason(reason, false);
   197	
   198	        /// <summary>
   199	        /// Player "Play": clears BOTH User and PendingDecision. This is what the HUD
   200	        /// Play button / Space hotkey call, so the player can never be locked out by a
   201	        /// missing decision popup.
   202	        /// </summary>
   203	        public void ResumeByPlayer() => SetReasons(TimePauseReason.None);
   204	
   205	        /// <summary>Legacy single-flag API — maps to the User reason (not a decision pause).</summary>
   206	        public void SetPaused(bool paused)
   207	        {
   208	            if (paused) Pause(TimePauseReason.User);
   209	            else Resume(TimePauseReason.User);
   210	        }
   211	
   212	        /// <summary>
   213	        /// P12A — toggles whether a live gameplay session is active (set by
   214	        /// <see cref="GameSessionCoordinator"/> on every phase change). Publishing on the
   215	        /// effective change keeps any speed/pause listener refreshed without a new bus.
   216	        /// </summary>
   217	        public void SetSessionActive(bool active)
   218	        {
   219	            if (_sessionActive == active) return;
   220	            _sessionActive = active;
   221	            PublishState();
   222	        }
   223	
   224	        /// <summary>
   225	        /// P12A — end the current session's simulation clock exactly once at the seam
   226	        /// between two sessions: freeze simulation (session inactive), clear every pause
   227	        /// reason (a fresh session starts unpaused), clear the authoritative pending
   228	        /// decision, and complete any outstanding world-event awaiter with a defined
   229	        /// "session ended" response. Real-time (UTC) viewer/cooldown clocks are
   230	        /// independent of this class and are not affected.
   231	        /// </summary>
   232	        public void EndSession(string reason)
   233	        {
   234	            SetSessionActive(false);
   235	            ClearPendingDecisions();
   236	
   237	            // P13A — clear exactly the reasons a session boundary OWNS: the player pause
   238	            // (a fresh session starts unpaused) and the decision pause (its events are
   239	            // gone). A SessionTransition pause is PRESERVED: the transition that holds it
   240	            // is the very caller of this method and is still in flight, so ending the
   241	            // session must not resume a transition that has not finished.
   242	            SetReasons(_reasons & TimePauseReason.SessionTransition);
   243	            CancelPendingWorldEventWait(reason);
   244	        }
   245	
   246	        /// <summary>
   247	        /// P12A — never retain a stale cached world-event response from a prior session,
   248	        /// and never strand an existing awaiter: the handoff cache is dropped and the
   249	        /// current wait is completed with an empty event id (the response contract's
   250	        /// clearest "no event" answer) plus an explicit reason.
   251	        /// </summary>
   252	        public void CancelPendingWorldEventWait(string reason)
   253	        {
   254	            _cachedPendingEvent = null;
   255	
   256	            var previous = _pendingEventSource;
   257	            _pendingEventSource = new UniTaskCompletionSource<AwaitWorldEventResponse>();
   258	            previous.TrySetResult(new AwaitWorldEventResponse
   259	            {
   260	                EventId = string.Empty,
   261	                Description = string.IsNullOrEmpty(reason)
   262	                    ? "session ended before a world event fired"
   263	                    : "session ended before a world event fired: " + reason,
   264	            });
   265	        }
   266	
   267	        /// <summary>
   268	        /// P13A — the SINGLE decision-validation path, reused by the in-game UI click
   269	        /// (EventPopupPresenter) and by the bridge decision (DecisionLogger ->
   270	        /// DecisionExecutor). A decision is valid only when a decision is pending, it names
   271	        /// that pending INSTANCE, <paramref name="eventId"/> agrees with the instance's
   272	        /// definition, and <paramref name="choiceId"/> is one of the choices THAT INSTANCE
   273	        /// offered (exact id match). On success exactly that instance is removed, so a
   274	        /// duplicate / replayed / stale / unknown decision can never be applied twice. On any
   275	        /// failure nothing changes (not even the current event), false is returned, and
   276	        /// <paramref name="reason"/> says why.
   277	        /// </summary>
   278	        /// <param name="instanceId">
   279	        /// The target occurrence. Null/empty is accepted for a legacy caller that only knows
   280	        /// the definition id: the decision then applies only when EXACTLY ONE pending instance
   281	        /// has that definition id. An ambiguous match is refused rather than guessed at, so a
   282	        /// bare event id can never resolve the wrong occurrence of a repeated event.
   283	        /// </param>
   284	        public bool TryResolvePendingDecision(string instanceId, string eventId, string choiceId, out string reason)
   285	        {
   286	            if (!HasPendingDecision)
   287	            {
   288	                // P13A — a duplicate/replayed decision lands here once its occurrence is gone, so
   289	                // the rejection still names what was asked for instead of a bare "nothing pending".
   290	                reason = string.IsNullOrEmpty(instanceId)
   291	                    ? "no decision event is pending"
   292	                    : $"instanceId '{instanceId}' is not a pending decision (none is pending)";
   293	                return false;
   294	            }
   295	
   296	            PendingWorldEvent target;
   297	            if (!string.IsNullOrEmpty(instanceId))
   298	            {
   299	                if (!TryGetPendingDecision(instanceId, out target))
   300	                {
   301	                    reason = $"instanceId '{instanceId}' is not a pending decision";
   302	                    return false;
   303	                }
   304	                if (eventId != target.EventId)
   305	                {
   306	                    reason = $"instanceId '{instanceId}' is event '{target.EventId}', not '{eventId}'";
   307	                    return false;
   308	                }
   309	            }
   310	            else
   311	            {
   312	                target = null;
   313	                int matches = 0;
   314	                for (int i = 0; i < _pendingDecisions.Count; i++)
   315	                {
   316	                    if (_pendingDecisions[i].EventId != eventId) continue;
   317	                    target = _pendingDecisions[i];
   318	                    matches++;
   319	                }
   320	                if (matches == 0)
   321	                {
   322	                    reason = $"eventId '{eventId}' is not a pending event";
   323	                    return false;
   324	                }
   325	                if (matches > 1)
   326	                {
   327	                    reason = $"eventId '{eventId}' matches {matches} pending instances - " +
   328	                             "a decision must name the instance id";
   329	                    return false;
   330	                }
   331	            }
   332	
   333	            if (!target.HasChoice(choiceId))
   334	            {
   335	                reason = $"choiceId '{choiceId}' is not one of the choices of {target}";
   336	                return false;
   337	            }
   338	
   339	            _pendingDecisions.Remove(target);
   340	            reason = null;
   341	            return true;
   342	        }
   343	
   344	        /// <summary>
   345	        /// Legacy two-argument form (definition id only) — see the four-argument overload for
   346	        /// the compatibility rule it follows.
   347	        /// </summary>
   348	        public bool TryResolvePendingDecision(string eventId, string choiceId, out string reason)
   349	            => TryResolvePendingDecision(null, eventId, choiceId, out reason);
   350	
   351	        /// <summary>
   352	        /// P13A — drop every outstanding pending decision. Used at a session boundary and by a
   353	        /// restore (which replaces the pending state outright); never by a decision.
   354	        /// </summary>
   355	        public void ClearPendingDecisions() => _pendingDecisions.Clear();
   356	
   357	        /// <summary>
   358	        /// P13A — look up one outstanding pending decision by its instance id. Read-only; a
   359	        /// null/empty id never matches (instance ids are never empty).
   360	        /// </summary>
   361	        public bool TryGetPendingDecision(string instanceId, out PendingWorldEvent pending)
   362	        {
   363	            if (!string.IsNullOrEmpty(instanceId))
   364	            {
   365	                for (int i = 0; i < _pendingDecisions.Count; i++)
   366	                {
   367	                    if (_pendingDecisions[i].InstanceId != instanceId) continue;
   368	                    pending = _pendingDecisions[i];
   369	                    return true;
   370	                }
   371	            }
   372	
   373	            pending = null;
   374	            return false;
   375	        }
   376	
   377	        /// <summary>
   378	        /// P13A — store one newly raised pending decision and mint its instance id. Returns
   379	        /// null when <see cref="MaxPendingDecisions"/> is already reached: the raise is then
   380	        /// refused (logged here) and NOTHING was stored — no pending decision is ever replaced
   381	        /// or evicted, and the caller must not report the event as pending.
   382	        /// </summary>
   383	        private PendingWorldEvent EnqueuePendingDecision(string eventId, string description, List<EventChoiceInfo> choices,
   384	                                                         string speakerDiscipleId = null, string speakerDisplayName = null,
   385	                                                         string explicitInstanceId = null)
   386	        {
   387	            if (_pendingDecisions.Count >= MaxPendingDecisions)
   388	            {
   389	                Debug.LogWarning(
   390	                    $"[TimeSystem] Refused to queue world event '{eventId}': {_pendingDecisions.Count} decisions are " +
   391	                    $"already outstanding (cap {MaxPendingDecisions}). The oldest pending decision " +
   392	                    $"('{PendingInstanceId}') is kept - an outstanding decision is never overwritten to make room.");
   393	                return null;
   394	            }
   395	
   396	            // P13B — a restore supplies the id the occurrence already had, so a decision in
   397	            // flight across a save/load still names a live instance. A fresh raise always mints one.
   398	            string instanceId = explicitInstanceId;
   399	            if (string.IsNullOrEmpty(instanceId))
   400	            {
   401	                instanceId = $"{InstanceIdPrefix}{_nextEventInstanceNumber:000000}";
   402	                _nextEventInstanceNumber++;
   403	            }
   404	
   405	            var pending = new PendingWorldEvent(instanceId, eventId, description, choices,
   406	                                                speakerDiscipleId, speakerDisplayName);
   407	            _pendingDecisions.Add(pending);
   408	            return pending;
   409	        }
   410	
   411	        /// <summary>
   412	        /// P13B — the outstanding pending decisions to persist, oldest first. PLAIN DATA only:
   413	        /// the snapshot contract holds no waiter, no subscription and no Unity object, so this
   414	        /// copies across the serializable fields and nothing else. Returns a fresh list.
   415	        /// </summary>
   416	        public List<PendingEventSnapshot> CapturePendingDecisions()
   417	        {
   418	            var captured = new List<PendingEventSnapshot>(_pendingDecisions.Count);
   419	            for (int i = 0; i < _pendingDecisions.Count; i++)
   420	            {
   421	                var pending = _pendingDecisions[i];
   422	                captured.Add(new PendingEventSnapshot
   423	                {
   424	                    InstanceId = pending.InstanceId,
   425	                    EventId = pending.EventId,
   426	                    Description = pending.Description,
   427	                    Choices = new List<EventChoiceInfo>(pending.Choices),
   428	                    SpeakerDiscipleId = pending.SpeakerDiscipleId,
   429	                    SpeakerDisplayName = pending.SpeakerDisplayName,
   430	                });
   431	            }
   432	            return captured;
   433	        }
   434	
   435	        /// <summary>
   436	        /// P13B — re-seed the instance-id counter PAST every restored id, so a newly raised event
   437	        /// can never be minted with an id an outstanding restored occurrence already owns.
   438	        /// An unrecognisable id (a hand-authored or future-format one) leaves the counter alone
   439	        /// rather than parsing loosely.
   440	        /// </summary>
   441	        private void ReserveInstanceId(string instanceId)
   442	        {
   443	            if (string.IsNullOrEmpty(instanceId)) return;
   444	            if (!instanceId.StartsWith(InstanceIdPrefix, System.StringComparison.Ordinal)) return;
   445	
   446	            int number;
   447	            if (!int.TryParse(instanceId.Substring(InstanceIdPrefix.Length),
   448	                              System.Globalization.NumberStyles.None,
   449	                              System.Globalization.CultureInfo.InvariantCulture, out number))
   450	                return;
   451	
   452	            int candidate = number + 1; // numbers are only compared, never formatted back
   453	            if (candidate > _nextEventInstanceNumber) _nextEventInstanceNumber = candidate;
   454	        }
   455	
   456	        /// <summary>
   457	        /// P11A — restore simulation state from a validated session snapshot, on the
   458	        /// Unity main thread. Sets the (pre-validated) speed, then re-establishes or
   459	        /// clears the authoritative pending decision. A restored pending decision is
   460	        /// re-published as a world-event message so the UI/AI can react; the bridge
   461	        /// handoff cache is deliberately NOT restored (it is consumed-on-read
   462	        /// transport state — see SectSessionSnapshot.cs classification). Nothing here
   463	        /// touches gameplay state; SectStateProvider owns that half.
   464	        /// </summary>
   465	        public void RestoreSimulationState(int speed, string pendingEventId, string pendingDescription,
   466	                                           List<EventChoiceInfo> pendingChoices, bool pendingDecisionPaused)
   467	        {
   468	            // Compatibility path for a caller / an older save that carries only the single
   469	            // pending-decision summary (no instance id): it becomes one occurrence under a
   470	            // FRESH id, which is the best that can be done without an id to restore.
   471	            RestorePendingDecisions(speed, string.IsNullOrEmpty(pendingEventId)
   472	                ? NoPendingEvents
   473	                : new List<PendingEventSnapshot>
   474	                {
   475	                    new PendingEventSnapshot
   476	                    {
   477	                        EventId = pendingEventId,
   478	                        Description = pendingDescription,
   479	                        Choices = pendingChoices != null ? new List<EventChoiceInfo>(pendingChoices) : new List<EventChoiceInfo>(),
   480	                    },
   481	                },
   482	                pendingDecisionPaused);
   483	        }
   484	
   485	        private static readonly List<PendingEventSnapshot> NoPendingEvents = new List<PendingEventSnapshot>();
   486	
   487	        /// <summary>
   488	        /// P13B — restore the WHOLE pending-decision queue from a validated snapshot, on the
   489	        /// Unity main thread. A restored occurrence keeps the instance id it was saved under
   490	        /// (so a decision already addressed to it is still valid), and its instance-specific
   491	        /// payload (description, choices, optional speaker) comes back with it.
   492	        ///
   493	        /// Nothing here APPLIES a consequence: this only re-establishes what is still
   494	        /// outstanding. Applying is the decision's job, and a decision can only ever be applied
   495	        /// once, so restoring the same save twice cannot pay twice.
   496	        ///
   497	        /// The restored occurrence is re-published as a world-event message so the UI, the chip
   498	        /// and the bridge can react — the UI is REBUILT from this authoritative state rather than
   499	        /// from anything serialized (§4). The bridge hand-off cache is deliberately NOT restored
   500	        /// (it is consumed-on-read transport state).
   501	        /// </summary>
   502	        public void RestorePendingDecisions(int speed, List<PendingEventSnapshot> pendingEvents,
   503	                                            bool pendingDecisionPaused)
   504	        {
   505	            SetSpeed(speed);
   506	
   507	            // A restore replaces the pending state outright (it describes the session being
   508	            // loaded), so whatever the session being left had queued goes first.
   509	            ClearPendingDecisions();
   510	
   511	            int restored = 0;
   512	            if (pendingEvents != null)
   513	            {
   514	                for (int i = 0; i < pendingEvents.Count && restored < MaxPendingDecisions; i++)
   515	                {
   516	                    var entry = pendingEvents[i];
   517	                    if (entry == null || string.IsNullOrEmpty(entry.EventId)) continue;
   518	
   519	                    // A duplicate id would make two live occurrences indistinguishable and let
   520	                    // one decision apply twice — skip it rather than store an ambiguous queue.
   521	                    PendingWorldEvent existing;
   522	                    if (!string.IsNullOrEmpty(entry.InstanceId) && TryGetPendingDecision(entry.InstanceId, out existing))
   523	                    {
   524	                        Debug.LogWarning($"[TimeSystem] Restore skipped pending event '{entry.EventId}': instance id " +
   525	                                         $"'{entry.InstanceId}' is already restored — a duplicate id would make the " +
   526	                                         "occurrences indistinguishable.");
   527	                        continue;
   528	                    }
   529	
   530	                    var stored = EnqueuePendingDecision(entry.EventId, entry.Description, entry.Choices,
   531	                                                        entry.SpeakerDiscipleId, entry.SpeakerDisplayName,
   532	                                                        entry.InstanceId);
   533	                    if (stored == null) break;
   534	
   535	                    ReserveInstanceId(stored.InstanceId);
   536	                    restored++;
   537	                }
   538	            }
   539	
   540	            // The snapshot's pause flag is authoritative: a restore that says "not paused"
   541	            // must not leave the clock frozen on a pause whose event no longer exists.
   542	            if (restored == 0 || !pendingDecisionPaused) Resume(TimePauseReason.PendingDecision);
   543	            else Pause(TimePauseReason.PendingDecision);
   544	
   545	            // Re-announce each restored occurrence, oldest first, so derived UI/bridge state is
   546	            // rebuilt from the authoritative pending queue instead of from the save.
   547	            for (int i = 0; i < _pendingDecisions.Count; i++)
   548	            {
   549	                var pending = _pendingDecisions[i];
   550	                _worldEventPublisher.Publish(new WorldEventTriggeredMessage
   551	                {
   552	                    EventId = pending.EventId,
   553	                    InstanceId = pending.InstanceId,
   554	                    RequiresDecision = true,
   555	                    Description = pending.Description,
   556	                    Choices = new List<EventChoiceInfo>(pending.Choices),
   557	                    SpeakerDiscipleId = pending.SpeakerDiscipleId,
   558	                    SpeakerDisplayName = pending.SpeakerDisplayName,
   559	                });
   560	            }
   561	        }
   562	
   563	        /// <summary>Sets the simulation speed, validated to <see cref="MinSpeed"/>..<see cref="MaxSpeed"/>.</summary>
   564	        public void SetSpeed(int speed)
   565	        {
   566	            int clamped = ClampSpeed(speed);
   567	            if (clamped == _speed) return; // effective change only
   568	            _speed = clamped;
   569	            PublishState();
   570	        }
   571	
   572	        /// <summary>True while ANY pause reason is active.</summary>
   573	        public bool IsPaused => _reasons != TimePauseReason.None;
   574	
   575	        /// <summary>
   576	        /// P12A — true while a live gameplay session is active. When false (Title or a
   577	        /// session transition) <see cref="SimulationDelta"/> is exactly 0, but that is NOT
   578	        /// reported as a pause (see <see cref="IsPaused"/>). UTC/viewer clocks are
   579	        /// unaffected — they never read simulation time.
   580	        /// </summary>
   581	        public bool IsSessionActive => _sessionActive;
   582	
   583	        /// <summary>True when the player has paused (User reason).</summary>
   584	        public bool IsUserPaused => HasReason(TimePauseReason.User);
   585	
   586	        /// <summary>True when a decision-requiring event is outstanding (the pause reason).</summary>
   587	        public bool IsPendingDecisionPaused => HasReason(TimePauseReason.PendingDecision);
   588	
   589	        // ---------- P13A: authoritative pending-decision state ----------
   590	        // Separate from IsPendingDecisionPaused: the player can clear the pause
   591	        // (ResumeByPlayer) while the event is still pending and undecided.
   592	
   593	        /// <summary>True while at least one decision-requiring event is outstanding and undecided.</summary>
   594	        public bool HasPendingDecision => _pendingDecisions.Count > 0;
   595	
   596	        /// <summary>How many decisions are outstanding right now (0..<see cref="MaxPendingDecisions"/>).</summary>
   597	        public int PendingDecisionCount => _pendingDecisions.Count;
   598	
   599	        /// <summary>
   600	        /// The CURRENT pending decision — the OLDEST undecided one — or null when none. This is
   601	        /// the single ordering every consumer reads (UI, bridge handoff, snapshot), so "which
   602	        /// event am I looking at" can never differ between them.
   603	        /// </summary>
   604	        public PendingWorldEvent PendingDecision =>
   605	            _pendingDecisions.Count > 0 ? _pendingDecisions[0] : null;
   606	
   607	        /// <summary>Instance id of the current pending decision, or null when none.</summary>
   608	        public string PendingInstanceId => PendingDecision?.InstanceId;
   609	
   610	        /// <summary>Authored definition id of the current pending decision, or null when none.</summary>
   611	        public string PendingEventId => PendingDecision?.EventId;
   612	
   613	        /// <summary>Description of the current pending decision, or null when none.</summary>
   614	        public string PendingDescription => PendingDecision?.Description;
   615	
   616	        /// <summary>Choices of the current pending decision (never null; empty when none).</summary>
   617	        public IReadOnlyList<EventChoiceInfo> PendingChoices =>
   618	            PendingDecision?.Choices ?? (IReadOnlyList<EventChoiceInfo>)NoChoices;
   619	
   620	        /// <summary>Current (validated) simulation speed.</summary>
   621	        public int Speed => _speed;
   622	
   623	        private bool HasReason(TimePauseReason reason) => (_reasons & reason) == reason;
   624	
   625	        private void SetReason(TimePauseReason reason, bool active)
   626	        {
   627	            if (reason == TimePauseReason.None) return;
   628	            SetReasons(active ? (_reasons | reason) : (_reasons & ~reason));
   629	        }
   630	
   631	        private void SetReasons(TimePauseReason next)
   632	        {
   633	            if (next == _reasons) return; // no effective change - no message
   634	            _reasons = next;
   635	            PublishState();
   636	        }
   637	
   638	        // Refresh trigger only (Paused = IsPaused). The HUD reads reasons/speed
   639	        // directly from TimeSystem; it never infers them from this message.
   640	        private void PublishState()
   641	        {
   642	            _speedPublisher.Publish(new TimeSpeedChangedMessage { Speed = _speed, Paused = IsPaused });
   643	        }
   644	
   645	        /// <summary>
   646	        /// Simulation delta for gameplay progression, in seconds — the ONLY place game
   647	        /// speed is applied. Returns 0 while paused, so a paused game freezes every
   648	        /// consumer without each one having to re-implement the pause rule.
   649	        /// <para>Main thread only: reads <see cref="UnityEngine.Time.deltaTime"/>.</para>
   650	        /// </summary>
   651	        /// <remarks>
   652	        /// Time.timeScale is deliberately never written by this class: it is already
   653	        /// folded into Time.deltaTime, so multiplying by speed here as well would apply
   654	        /// game speed twice. Speed below <see cref="MinSpeed"/> is clamped up to 1x and
   655	        /// above <see cref="MaxSpeed"/> down to 3x, so a stale/out-of-range speed (or a
   656	        /// forgotten SetSpeed) can never yield a zero or unbounded delta while unpaused.
   657	        /// </remarks>
   658	        public float SimulationDelta
   659	        {
   660	            // P12A — gameplay progression requires BOTH an unpaused clock and an active
   661	            // Playing session. The two are independent: a stale "no session" state can
   662	            // never be mistaken for, or clear, a player/decision pause.
   663	            get { return ComputeSimulationDelta(IsPaused || !_sessionActive, _speed, Time.deltaTime); }
   664	        }
   665	
   666	        /// <summary>
   667	        /// Pure core of <see cref="SimulationDelta"/> — the single speed application,
   668	        /// split out so the multiplier can be asserted without a rendered frame.
   669	        /// </summary>
   670	        public static float ComputeSimulationDelta(bool paused, int speed, float frameDelta)
   671	        {
   672	            if (paused) return 0f;
   673	            return ClampSpeed(speed) * frameDelta;
   674	        }
   675	
   676	        private static int ClampSpeed(int speed)
   677	        {
   678	            if (speed < MinSpeed) return MinSpeed;
   679	            if (speed > MaxSpeed) return MaxSpeed;
   680	            return speed;
   681	        }
   682	
   683	        // Resolved by AwaitWorldEventHandler - the bridge's await_next_world_event
   684	        // tool call blocks on this until the next RaiseWorldEvent(), unless
   685	        // there's already a cached one waiting (see _cachedPendingEvent).
   686	        //
   687	        // Remember: while a pause reason is active (e.g. a decision-requiring
   688	        // event is outstanding), RaiseWorldEvent never fires again -
   689	        // WorldEventSystem checks IsPaused and skips. Resolve the decision (or
   690	        // ResumeByPlayer) first, or this will time out waiting for an event that
   691	        // can't happen yet.
   692	        public UniTask<AwaitWorldEventResponse> WaitForNextWorldEventAsync()
   693	        {
   694	            // P12A — outside a Playing session there is no event stream to wait on, so a
   695	            // new awaiter is answered immediately with a clear "no active session" result
   696	            // instead of being parked until some future session fires an event.
   697	            if (!_sessionActive)
   698	            {
   699	                return UniTask.FromResult(new AwaitWorldEventResponse
   700	                {
   701	                    EventId = string.Empty,
   702	                    Description = "no active session: gameplay is not in a Playing state",
   703	                });
   704	            }
   705	
   706	            if (_cachedPendingEvent != null)
   707	            {
   708	                Debug.Log("[TimeSystem] Returning cached world event immediately.");
   709	                var cached = _cachedPendingEvent;
   710	                _cachedPendingEvent = null;
   711	                return UniTask.FromResult(cached);
   712	            }
   713	
   714	            return _pendingEventSource.Task;
   715	        }
   716	
   717	        // Called by whatever system decides a world event fired (new
   718	        // applicant, monster incursion, ...). requiresDecision auto-pauses -
   719	        // this is the "checkpoint" the AI GM / vote window waits on, and
   720	        // stays paused until execute_decision is called.
   721	        //
   722	        // Not sent over the interprocess bus as pub/sub (see the comment on
   723	        // AwaitWorldEventRequest in GameMessages.cs for why) - completing
   724	        // _pendingEventSource is what actually delivers this to the bridge,
   725	        // via the request-response AwaitWorldEventHandler below. The
   726	        // in-memory Publish() call is just for any other in-Unity listener.
   727	        //
   728	        // P13A: a decision-requiring event is stored as its OWN pending instance,
   729	        // queued behind anything already outstanding and identified by a unique id.
   730	        /// <returns>
   731	        /// False only when a decision-requiring event could not be stored because
   732	        /// <see cref="MaxPendingDecisions"/> is reached: nothing is pending for it then, no
   733	        /// message is published, no awaiter is completed, and no outstanding decision was
   734	        /// touched. Always true for a non-decision event.
   735	        /// </returns>
   736	        public bool RaiseWorldEvent(string eventId, string description, bool requiresDecision, List<EventChoiceInfo> choices,
   737	                                    string speakerDiscipleId = null, string speakerDisplayName = null)
   738	        {
   739	            Debug.Log($"[TimeSystem] World event raised: {eventId} (requiresDecision={requiresDecision})");
   740	
   741	            var effectiveChoices = choices ?? new List<EventChoiceInfo>();
   742	            string instanceId = null;
   743	
   744	            if (requiresDecision)
   745	            {
   746	                bool alreadyPending = HasPendingDecision;
   747	
   748	                var pending = EnqueuePendingDecision(eventId, description, effectiveChoices,
   749	                                                    speakerDiscipleId, speakerDisplayName);
   750	                if (pending == null) return false; // cap reached - nothing was stored, nothing changed
   751	
   752	                instanceId = pending.InstanceId;
   753	                effectiveChoices = new List<EventChoiceInfo>(pending.Choices);
   754	
   755	                // E2-lite/P13A: the pause reason is added only for the event that turns an
   756	                // EMPTY pending state into a non-empty one - if any decision is still
   757	                // outstanding (e.g. the player pressed Play but has not decided yet),
   758	                // raising further events must NOT re-add the pause...
   759	                if (!alreadyPending && _config.AutoPauseOnDecisionEvent)
   760	                    Pause(TimePauseReason.PendingDecision);
   761	                // ...and must never clear it either: another instance is still undecided.
   762	            }
   763	
   764	            _worldEventPublisher.Publish(new WorldEventTriggeredMessage
   765	            {
   766	                EventId = eventId,
   767	                InstanceId = instanceId,
   768	                RequiresDecision = requiresDecision,
   769	                Description = description,
   770	                Choices = effectiveChoices,
   771	                SpeakerDiscipleId = speakerDiscipleId,
   772	                SpeakerDisplayName = speakerDisplayName,
   773	            });
   774	
   775	            var response = new AwaitWorldEventResponse
   776	            {
   777	                EventId = eventId,
   778	                InstanceId = instanceId,
   779	                RequiresDecision = requiresDecision,
   780	                Description = description,
   781	                Choices = effectiveChoices,
   782	                SpeakerDiscipleId = speakerDiscipleId,
   783	                SpeakerDisplayName = speakerDisplayName,
   784	            };
   785	
   786	            if (requiresDecision)
   787	            {
   788	                _cachedPendingEvent = response;
   789	            }
   790	
   791	            var previous = _pendingEventSource;
   792	            _pendingEventSource = new UniTaskCompletionSource<AwaitWorldEventResponse>();
   793	            previous.TrySetResult(response);
   794	            return true;
   795	        }
   796	    }
   797	
   798	    // Answers AwaitWorldEventRequest coming in over the interprocess bus.
   799	    // Request-response, not pub/sub - see the comment on AwaitWorldEventRequest
   800	    // in GameMessages.cs for wh
... [TRUNCATED: แสดง 40000 จาก 57202 bytes — ขอส่วนที่เหลือผ่าน need_files]
````

### UnityProject/Assets/Scripts/Systems/SectStateProvider.cs (113534 bytes)
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
    12	    /// <summary>
    13	    /// P10B — what one work tick did for one disciple. Exactly ONE of these is
    14	    /// recorded per disciple per tick; <see cref="None"/> means the tick changed no
    15	    /// attribute (unknown / other task).
    16	    /// </summary>
    17	    public enum DiscipleWorkOutcome
    18	    {
    19	        /// <summary>Any other task — no attribute change.</summary>
    20	        None = 0,
    21	        /// <summary>Gathering passed its building gate and produced its rate this tick.</summary>
    22	        ProductiveGathering = 1,
    23	        /// <summary>Crafting passed its gate and advanced (a completion tick counts as productive).</summary>
    24	        ProductiveCrafting = 2,
    25	        /// <summary>Gate failed (building missing) or the craft is held waiting for materials.</summary>
    26	        Blocked = 3,
    27	        /// <summary>Meditation.</summary>
    28	        Resting = 4,
    29	    }
    30	
    31	    // Implements the seam TimeSystem.cs defines (ISectStateProvider).
    32	    //
    33	    // IMPORTANT: this holds ONE live state instance for the process
    34	    // lifetime, created once from mock data. Earlier versions called
    35	    // MockSectData.Create() fresh on every query, which meant any mutation
    36	    // was invisible on the next get_sect_state call - it was building a
    37	    // brand new object every time, not reading back the one that got
    38	    // mutated. Swap _state's origin for real multi-subsystem aggregation
    39	    // later if this ends up needing more than gathering + recruiting - the
    40	    // interface doesn't need to change.
    41	    public class SectStateProvider : ISectStateProvider
    42	    {
    43	        // task id -> (resource id, units produced per second while assigned)
    44	        private static readonly Dictionary<string, (string Resource, float PerSecond)> GatheringRates = new()
    45	        {
    46	            ["gathering_herb"] = ("herb", 0.2f),
    47	            ["gathering_wood"] = ("wood", 0.2f),
    48	            ["gathering_ore"] = ("ore", 0.15f),
    49	            ["gathering_provisions"] = ("provisions", 0.25f),
    50	        };
    51	
    52	        private static readonly string[] GatheringTasks = GatheringRates.Keys.ToArray();
    53	
    54	        // task id -> recipe. CraftSeconds is how long one disciple assigned
    55	        // to that task takes to finish one item, once ingredients are
    56	        // available - if the stockpile runs short, progress holds at 100%
    57	        // and waits rather than losing accumulated time.
    58	        //
    59	        // P10B: this dictionary is the ONE definition of the task -> skill-category
    60	        // mapping — a completed craft awards XP to exactly the category of the recipe
    61	        // it finished (refining_elixir -> alchemy, forging_artifact -> forging).
    62	        // Gathering needs no table: every gathering task maps to the single
    63	        // CategoryGathering constant. Meditation grants no skill XP.
    64	        private static readonly Dictionary<string, CraftingRecipe> CraftingRecipes = new()
    65	        {
    66	            ["refining_elixir"] = new CraftingRecipe(
    67	                "elixir_qi_gathering", 3, 20f,
    68	                new Dictionary<string, int> { ["herb"] = 10 },
    69	                DiscipleAttributesConfig.CategoryAlchemy),
    70	
    71	            ["forging_artifact"] = new CraftingRecipe(
    72	                "sword_azure_flame", 5, 30f,
    73	                new Dictionary<string, int> { ["ore"] = 15, ["wood"] = 10 },
    74	                DiscipleAttributesConfig.CategoryForging),
    75	        };        // Task System v2 (§6) — the known task set is the keys of the existing
    76	        // gathering + crafting dictionaries, plus "meditation". No new data
    77	        // pipeline: the dictionaries ARE the source of truth for what a disciple
    78	        // can be assigned. See open-questions.md §15 for the cultivation/meditation id question.
    79	        private static readonly HashSet<string> KnownTasks = BuildKnownTasks();
    80	
    81	        // P3 (Task Assignment UI) — stable ordered list of the same known tasks
    82	        // (gathering, crafting, meditation). Kept beside the HashSet so the UI
    83	        // query and the assignment gate can never drift apart; both are built
    84	        // from the same dictionaries.
    85	        private static readonly string[] KnownTaskOrder =
    86	            GatheringRates.Keys.Concat(CraftingRecipes.Keys).Concat(new[] { "meditation" }).ToArray();
    87	
    88	        private static HashSet<string> BuildKnownTasks()
    89	        {
    90	            var set = new HashSet<string>();
    91	            foreach (var task in GatheringRates.Keys) set.Add(task);
    92	            foreach (var task in CraftingRecipes.Keys) set.Add(task);
    93	            set.Add("meditation");
    94	            return set;
    95	 }
    96	
    97	        // Task building-requirements — static design data, not runtime state:
    98	        // task id -> building def id that must be in SectEconomyState.PlacedBuildings
    99	        // before that task can be assigned. Tasks absent from this dictionary
   100	        // (gathering_wood/ore/provisions, meditation) have no requirement.
   101	        // Placeholder lookup: list-scan of PlacedBuildings is fast enough while
   102	        // the roster is small; swap for an index later if it grows (same shape
   103	        // as NextBuildingInstanceId's scan).
   104	        private static readonly Dictionary<string, string> TaskRequiredBuilding = new Dictionary<string, string>
   105	        {
   106	            ["gathering_herb"] = "herb_plot",
   107	            ["refining_elixir"] = "pill_hall",
   108	            ["forging_artifact"] = "forge",
   109	        };
   110	
   111	        // Tick-side variant of IsTaskAvailable — same rules, but the caller
   112	        // supplies a placed-DefId set built ONCE per tick (never scan
   113	        // PlacedBuildings per disciple). Same failReason shape so log/UI text
   114	        // stays consistent with the gate on TryAssignTask.
   115	        private static bool IsTaskAvailableWithBuildings(string taskId, HashSet<string> placedDefIds, out string failReason)
   116	        {
   117	            failReason = string.Empty;
   118	
   119	            if (string.IsNullOrEmpty(taskId) || !KnownTasks.Contains(taskId))
   120	            {
   121	                failReason = $"Unknown task: '{taskId}'.";
   122	                return false;
   123	            }
   124	
   125	            string requiredBuilding;
   126	            if (!TaskRequiredBuilding.TryGetValue(taskId, out requiredBuilding))
   127	                return true; // no requirement — always available once known
   128	
   129	            if (placedDefIds != null && placedDefIds.Contains(requiredBuilding))
   130	                return true;
   131	
   132	            failReason = $"Task '{taskId}' requires an existing '{requiredBuilding}' building.";
   133	            return false;
   134	        }
   135	
   136	        /// <summary>Placed DefId set for one tick — built once, shared by both ticks.</summary>
   137	        private HashSet<string> CollectPlacedDefIds()
   138	        {
   139	            var set = new HashSet<string>();
   140	            for (int i = 0; i < _state.PlacedBuildings.Count; i++)
   141	            {
   142	                var pb = _state.PlacedBuildings[i];
   143	                if (pb != null && !string.IsNullOrEmpty(pb.DefId)) set.Add(pb.DefId);
   144	            }
   145	            return set;
   146	        }
   147	
   148	        // Placeholder name pool - swap for a real generator once there's a
   149	        // reason to (naming conventions, avoiding repeats at scale, etc.).
   150	        private static readonly string[] RecruitNamePool =
   151	        {
   152	            "Chen Wei", "Bai Ling", "Zhou Tao", "Xiao Mei", "Jiang Yu", "Wen Hao",
   153	        };
   154	
   155	        private static readonly string[] StarterHair = { "hair_short", "hair_topknot", "hair_twin_tail" };
   156	
   157	        private readonly SectEconomyState _state = MockSectData.Create();
   158	        private readonly IPublisher<DiscipleRecruitedMessage> _discipleRecruitedPublisher;
   159	        private readonly IPublisher<SectResourceChangedMessage> _resourceChangedPublisher;
   160	        private readonly IPublisher<AvatarEquipmentChangedMessage> _avatarChangedPublisher;
   161	        private readonly IPublisher<DiscipleChibiBackendChangedMessage> _chibiBackendPublisher;
   162	        private readonly IPublisher<BuildingPlacedMessage> _buildingPlacedPublisher;
   163	        private readonly IPublisher<DiscipleTaskChangedMessage> _discipleTaskChangedPublisher;
   164	        private readonly IPublisher<DiscipleOwnerChangedMessage> _ownershipChangedPublisher;
   165	        // P9A — in-memory only, same rule as the ownership publisher (autonomy is a
   166	        // local player/UI concern; it never crosses the TCP wire).
   167	        private readonly IPublisher<DiscipleControlModeChangedMessage> _controlModeChangedPublisher;
   168	        private readonly BuildingDefPool _buildingDefPool;
   169	        private readonly AvatarPartPool _avatarPartPool;
   170	        private readonly VisualRuntimeConfig _visualConfig;
   171	        private readonly IVisualEntitlementProvider _entitlementProvider;
   172	        /// <summary>Concrete ref to the injected provider (null when a test/substitute implements the interface directly) — used only for bind-late wiring, not for resolution.</summary>
   173	        private readonly DefaultEntitlementProvider _defaultEntitlementProvider;
   174	
   175	        // P5B — real-time clock for the viewer inactivity/activity rule. Injected so
   176	        // tests can drive it deterministically; NEVER scaled game time (game time can
   177	        // be paused/speed-changed, which would silently extend or shrink protection).
   178	        private readonly IClock _clock;
   179	
   180	        // P5B — real-time cooldown on ACTUAL task changes, keyed per disciple.
   181	        // Prototype balance value (see DefaultTaskChangeCooldownSeconds). A no-op
   182	        // request never consumes or checks it.
   183	        private readonly Dictionary<string, DateTime> _taskChangeLastAtUtc = new();
   184	
   185	        // Fractional resource accumulated per task since the last whole
   186	        // unit was added to the stockpile - avoids losing sub-1 production
   187	        // between ticks.
   188	        private readonly Dictionary<string, float> _gatherAccumulators = new();
   189	
   190	        // Seconds accumulated toward the current craft, keyed per disciple
   191	        // (not per task like gathering) - crafting has a resource cost, so
   192	        // two disciples on the same task must progress independently, not
   193	        // share one pooled timer.
   194	        private readonly Dictionary<string, float> _craftProgress = new();
   195	
   196	        // P10B — per-tick scratch: disciple id -> that tick's single work record.
   197	        // Cleared at the start of a tick and consumed by ApplyWorkAttributes, so an
   198	        // outcome can never be written twice or applied twice. One small map, one
   199	        // applier — no second clock, no fixed-step loop, no per-frame allocation.
   200	        private struct WorkTickRecord
   201	        {
   202	            public DiscipleWorkOutcome Outcome;
   203	            /// <summary>Non-null only on a craft-completion tick (P10B §4).</summary>
   204	            public string CompletedCraftCategory;
   205	        }
   206	
   207	        private readonly Dictionary<string, WorkTickRecord> _workTick = new();
   208	        private int _workTickRecordCount;
   209	
   210	        public SectStateProvider(
   211	            IPublisher<DiscipleRecruitedMessage> discipleRecruitedPublisher,
   212	            IPublisher<SectResourceChangedMessage> resourceChangedPublisher,
   213	            IPublisher<AvatarEquipmentChangedMessage> avatarChangedPublisher,
   214	            IPublisher<DiscipleChibiBackendChangedMessage> chibiBackendPublisher,
   215	            AvatarPartPool avatarPartPool,
   216	            VisualRuntimeConfig visualConfig,
   217	            IVisualEntitlementProvider entitlementProvider,
   218	            BuildingDefPool buildingDefPool,
   219	            IPublisher<BuildingPlacedMessage> buildingPlacedPublisher,
   220	            IPublisher<DiscipleTaskChangedMessage> discipleTaskChangedPublisher,
   221	            IPublisher<DiscipleOwnerChangedMessage> ownershipChangedPublisher = null,
   222	            IClock clock = null,
   223	            IPublisher<DiscipleControlModeChangedMessage> controlModeChangedPublisher = null)
   224	        {
   225	            _discipleRecruitedPublisher = discipleRecruitedPublisher;
   226	            _resourceChangedPublisher = resourceChangedPublisher;
   227	            _avatarChangedPublisher = avatarChangedPublisher;
   228	            _chibiBackendPublisher = chibiBackendPublisher;
   229	            _buildingDefPool = buildingDefPool;
   230	            _buildingPlacedPublisher = buildingPlacedPublisher;
   231	            _discipleTaskChangedPublisher = discipleTaskChangedPublisher;
   232	            // P4: optional (default null) so every existing test construction site
   233	            // stays valid; production wires it via VContainer in UIInstaller/GameLifetimeScope.
   234	            _ownershipChangedPublisher = ownershipChangedPublisher;
   235	            // P9A: optional (default null) for the same reason — appended LAST so the
   236	            // positional test construction sites (ownerChanged, clock) are unchanged.
   237	            _controlModeChangedPublisher = controlModeChangedPublisher;
   238	            _avatarPartPool = avatarPartPool;
   239	            _visualConfig = visualConfig;
   240	            _entitlementProvider = entitlementProvider;
   241	            _defaultEntitlementProvider = entitlementProvider as DefaultEntitlementProvider;
   242	            // P5B: production registers UtcClock via DI; every existing test construction
   243	            // site omits it and keeps working (real UTC clock, which those tests never
   244	            // depend on because they never cross the 10-minute protection window).
   245	            _clock = clock ?? new UtcClock();
   246	
   247	            // Phase 5 — bind the provider's rank source HERE instead of injecting
   248	            // ISectStateProvider into the provider itself: that direction would be a
   249	            // DI cycle (SectStateProvider → provider → SectStateProvider). Bind-late
   250	            // keeps the provider ignorant of the state module; before this line runs,
   251	            // CanUse(discipleId, "owner") fails closed (Unspecified = deny).
   252	            //
   253	            // Note: inject the INTERFACE, not the concrete type. In this VContainer
   254	            // version Register<I, Impl> registers only the interface (concrete Resolve
   255	            // is not available), so consumers must resolve IVisualEntitlementProvider
   256	            // and reach the concrete for bind-late via a type test.
   257	            _defaultEntitlementProvider?.BindRankLookup(id =>
   258	            {
   259	                var d = FindDisciple(id);
   260	                return d != null ? d.Rank : DiscipleRank.Unspecified;
   261	            });
   262	        }
   263	
   264	        /// <summary>Single lookup helper — also used by the entitlement rank binding.</summary>
   265	        private DiscipleState FindDisciple(string discipleId)
   266	        {
   267	            if (string.IsNullOrEmpty(discipleId)) return null;
   268	            for (int i = 0; i < _state.Disciples.Count; i++)
   269	            {
   270	                var d = _state.Disciples[i];
   271	                if (d != null && d.DiscipleId == discipleId) return d;
   272	            }
   273	            return null;
   274	        }
   275	
   276	        public SectEconomyState BuildSectEconomyState()
   277	        {
   278	            return _state;
   279	        }
   280	
   281	        // Passive resource gathering - every disciple whose CurrentTask is
   282	        // a known gathering task contributes toward that resource. Called
   283	        // from DiscipleSystem.Tick(). Disciples whose task fails the building
   284	        // requirement are SKIPPED (CurrentTask is never rewritten here — the
   285	        // assignment gate is the only place that validates on assignment).
   286	        //
   287	        // P10B: production is unchanged; this tick additionally records ONE work
   288	        // outcome per disciple and then applies the attribute change once.
   289	        public void TickGathering(float deltaTimeSeconds)
   290	        {
   291	            var placedDefIds = CollectPlacedDefIds(); // once per tick, not per disciple
   292	            BeginWorkTick(); // P10B
   293	            foreach (var disciple in _state.Disciples)
   294	            {
   295	                // P10B — EVERY disciple gets exactly one outcome in this tick. Meditation
   296	                // rests; anything that is not a gathering task (including a crafting task,
   297	                // whose tick owns its change) is None = no attribute change here, so the
   298	                // outcome cannot be applied twice across the two ticks of a frame. The
   299	                // empty-task guard must come first so no dictionary is probed with a null key.
   300	                if (string.IsNullOrEmpty(disciple.CurrentTask))
   301	                {
   302	                    RecordWorkOutcome(disciple, DiscipleWorkOutcome.None);
   303	                    continue;
   304	                }
   305	
   306	                if (!GatheringRates.TryGetValue(disciple.CurrentTask, out var rate))
   307	                {
   308	                    RecordWorkOutcome(disciple, IsMeditationTask(disciple.CurrentTask)
   309	                        ? DiscipleWorkOutcome.Resting
   310	                        : DiscipleWorkOutcome.None);
   311	                    continue;
   312	                }
   313	
   314	                if (!IsTaskAvailableWithBuildings(disciple.CurrentTask, placedDefIds, out _))
   315	                {
   316	                    // P10B: the gate failed (building missing) → Blocked (small regen).
   317	                    RecordWorkOutcome(disciple, DiscipleWorkOutcome.Blocked);
   318	                    continue;
   319	                }
   320	
   321	                var accKey = disciple.CurrentTask;
   322	                var acc = _gatherAccumulators.TryGetValue(accKey, out var existing) ? existing : 0f;
   323	                acc += rate.PerSecond * deltaTimeSeconds;
   324	
   325	                var wholeUnits = Mathf.FloorToInt(acc);
   326	                if (wholeUnits > 0)
   327	                {
   328	                    AdjustAndNotify(_state.Stockpile.RawResources, rate.Resource, wholeUnits);
   329	                    acc -= wholeUnits;
   330	                    Debug.Log($"[SectStateProvider] Gathered +{wholeUnits} {rate.Resource} (task={accKey})");
   331	                }
   332	
   333	                _gatherAccumulators[accKey] = acc;
   334	
   335	                // P10B: reaching here means the gate passed → Productive.
   336	                RecordWorkOutcome(disciple, DiscipleWorkOutcome.ProductiveGathering);
   337	            }
   338	
   339	            ApplyWorkAttributes(deltaTimeSeconds); // P10B — the single apply step
   340	        }
   341	
   342	        // Disciple crafting: whoever's CurrentTask matches a known recipe
   343	        // accumulates progress; once a craft completes, consumes the raw
   344	        // resource cost and produces the item - into the sect stockpile for
   345	        // ordinary disciples, or straight into personal inventory for
   346	        // Elder+ (matches the ownership rule from the economy design:
   347	        // outer/inner disciples craft for the sect, elders keep their own).
   348	        // Called from ResourceCraftingSystem.Tick(). Disciples whose task
   349	        // fails the building requirement are SKIPPED — progress is HELD at
   350	        // its current value exactly like the out-of-materials path (never
   351	        // reset), and CurrentTask is never rewritten here.
   352	        public void TickCrafting(float deltaTimeSeconds)
   353	        {
   354	            var placedDefIds = CollectPlacedDefIds(); // once per tick, not per disciple
   355	            BeginWorkTick(); // P10B
   356	            foreach (var disciple in _state.Disciples)
   357	            {
   358	                // P10B — EVERY disciple gets exactly one outcome in this tick too. Only a
   359	                // crafting task changes an attribute here; everything else (gathering task,
   360	                // meditation, unknown/empty) is None = handled by TickGathering, so no
   361	                // stamina change is applied twice across the two ticks of a frame.
   362	                if (string.IsNullOrEmpty(disciple.CurrentTask))
   363	                {
   364	                    RecordWorkOutcome(disciple, DiscipleWorkOutcome.None);
   365	                    continue;
   366	                }
   367	
   368	                if (!CraftingRecipes.TryGetValue(disciple.CurrentTask, out var recipe))
   369	                {
   370	                    RecordWorkOutcome(disciple, DiscipleWorkOutcome.None);
   371	                    continue;
   372	                }
   373	
   374	                if (!IsTaskAvailableWithBuildings(disciple.CurrentTask, placedDefIds, out _))
   375	                {
   376	                    // P10B: the gate failed (building missing) → Blocked (small regen).
   377	                    RecordWorkOutcome(disciple, DiscipleWorkOutcome.Blocked);
   378	                    continue;
   379	                }
   380	
   381	                var progress = _craftProgress.TryGetValue(disciple.DiscipleId, out var existing) ? existing : 0f;
   382	                progress += deltaTimeSeconds;
   383	
   384	                if (progress < recipe.CraftSeconds) 
   385	                {
   386	                    _craftProgress[disciple.DiscipleId] = progress;
   387	                    // P10B: progress advanced → Productive (no completion yet).
   388	                    RecordWorkOutcome(disciple, DiscipleWorkOutcome.ProductiveCrafting);
   389	                    continue;
   390	                }
   391	
   392	                if (!TryConsume(_state.Stockpile.RawResources, recipe.Costs))
   393	                {
   394	                    // Ready to complete but not enough raw resources - hold
   395	                    // at the completion threshold and wait rather than
   396	                    // losing the accumulated progress or overshooting.
   397	                    _craftProgress[disciple.DiscipleId] = recipe.CraftSeconds;
   398	                    // P10B: held waiting for materials → Blocked; no XP is awarded.
   399	                    RecordWorkOutcome(disciple, DiscipleWorkOutcome.Blocked);
   400	                    continue;
   401	                }
   402	
   403	                foreach (var (resource, amount) in recipe.Costs)
   404	                {
   405	                    var newTotal = _state.Stockpile.RawResources.TryGetValue(resource, out var v) ? v : 0;
   406	                    _resourceChangedPublisher.Publish(new SectResourceChangedMessage
   407	                    {
   408	                        ResourceId = resource,
   409	                        Delta = -amount,
   410	                        NewTotal = newTotal,
   411	                    });
   412	                }
   413	
   414	                var item = new InventoryItem
   415	                {
   416	                    ItemDefId = recipe.ItemDefId,
   417	                    Quantity = 1,
   418	                    Grade = recipe.Grade,
   419	                    OwnerScope = disciple.Rank >= DiscipleRank.Elder ? OwnerScope.Personal : OwnerScope.SectStockpile,
   420	                };
   421	
   422	                if (item.OwnerScope == OwnerScope.Personal)
   423	                {
   424	                    disciple.PersonalInventory.Add(item);
   425	                }
   426	                else
   427	                {
   428	                    AddToStockpileGoods(item);
   429	                }
   430	
   431	                Debug.Log($"[SectStateProvider] {disciple.DisplayName} crafted {item.ItemDefId} " +
   432	                          $"(grade {item.Grade}, {item.OwnerScope})");
   433	
   434	                // Carry over any overshoot instead of resetting to exactly 0.
   435	                _craftProgress[disciple.DiscipleId] = progress - recipe.CraftSeconds;
   436	
   437	                // P10B: this is the authoritative completion point. The completion tick
   438	                // counts as Productive (§4 simplification) and carries the recipe's
   439	                // skill category so the XP is awarded ONCE, in ApplyWorkAttributes.
   440	                RecordWorkOutcome(disciple, DiscipleWorkOutcome.ProductiveCrafting, recipe.SkillCategory);
   441	            }
   442	
   443	            ApplyWorkAttributes(deltaTimeSeconds); // P10B — the single apply step
   444	        }
   445	
   446	        // ---------- P10B: work outcome → attribute change ----------
   447	
   448	        /// <summary>Starts one tick's outcome scratch (P10B).</summary>
   449	        private void BeginWorkTick()
   450	        {
   451	            _workTick.Clear();
   452	            _workTickRecordCount = 0;
   453	        }
   454	
   455	        /// <summary>
   456	        /// Records this tick's outcome for one disciple. Each tick writes exactly ONE
   457	        /// record per disciple (the two ticks of a frame cover different task domains and
   458	        /// the other side records None), which is what keeps the apply step from
   459	        /// double-counting a change.
   460	        /// </summary>
   461	        private void RecordWorkOutcome(DiscipleState disciple, DiscipleWorkOutcome outcome,
   462	                                       string completedCraftCategory = null)
   463	        {
   464	            if (disciple == null || string.IsNullOrEmpty(disciple.DiscipleId)) return;
   465	
   466	            _workTick[disciple.DiscipleId] = new WorkTickRecord
   467	            {
   468	                Outcome = outcome,
   469	                CompletedCraftCategory = completedCraftCategory,
   470	            };
   471	            _workTickRecordCount++;
   472	        }
   473	
   474	        /// <summary>
   475	        /// The ONLY place P10B mutates attributes (one function, called once per tick,
   476	        /// after that tick's outcomes are recorded — so nothing can be applied twice).
   477	        /// Rates are linear in the tick's simulation delta (rate * delta, then clamped);
   478	        /// a zero or non-finite delta (pause) changes no rate-driven value. A completed
   479	        /// craft is an event, not a rate, so its XP is not scaled by the delta.
   480	        /// </summary>
   481	        private void ApplyWorkAttributes(float deltaTimeSeconds)
   482	        {
   483	            if (_workTick.Count == 0) return;
   484	
   485	            bool ratesApply = deltaTimeSeconds > 0f; // false for 0 (paused) and for NaN
   486	
   487	            foreach (var disciple in _state.Disciples)
   488	            {
   489	                if (disciple == null || string.IsNullOrEmpty(disciple.DiscipleId)) continue;
   490	
   491	                WorkTickRecord record;
   492	                if (!_workTick.TryGetValue(disciple.DiscipleId, out record)) continue;
   493	
   494	                // Attributes are created + normalized by P10A; never invented or repaired here.
   495	                var attributes = disciple.Attributes;
   496	                if (attributes == null) continue;
   497	
   498	                float staminaPerSecond;
   499	                string xpCategory = null;
   500	                float xpPerSecond = 0f;
   501	
   502	                switch (record.Outcome)
   503	                {
   504	                    case DiscipleWorkOutcome.ProductiveGathering:
   505	                        staminaPerSecond = -DiscipleAttributesConfig.WorkStaminaDrainPerSecondGathering;
   506	                        xpCategory = DiscipleAttributesConfig.CategoryGathering;
   507	                        xpPerSecond = DiscipleAttributesConfig.WorkGatheringXpPerSecond;
   508	                        break;
   509	                    case DiscipleWorkOutcome.ProductiveCrafting:
   510	                        staminaPerSecond = -DiscipleAttributesConfig.WorkStaminaDrainPerSecondCrafting;
   511	                        break;
   512	                    case DiscipleWorkOutcome.Resting:
   513	                        staminaPerSecond = DiscipleAttributesConfig.WorkStaminaRegenPerSecondResting;
   514	                        break;
   515	                    case DiscipleWorkOutcome.Blocked:
   516	                        staminaPerSecond = DiscipleAttributesConfig.WorkStaminaRegenPerSecondBlocked;
   517	                        break;
   518	                    default:
   519	                        continue; // None — no attribute change at all
   520	                }
   521	
   522	                if (ratesApply)
   523	                {
   524	                    attributes.Stamina = ClampStamina(attributes.Stamina + staminaPerSecond * deltaTimeSeconds);
   525	                    if (xpCategory != null)
   526	                        AddSkillXp(attributes, xpCategory, xpPerSecond * deltaTimeSeconds);
   527	                }
   528	
   529	                // Craft completion XP: awarded where the item was actually produced. A
   530	                // craft already held at CraftSeconds can complete on a zero-delta frame
   531	                // (the item is still made), so the award follows the completion, not the delta.
   532	                if (!string.IsNullOrEmpty(record.CompletedCraftCategory))
   533	                    AddSkillXp(attributes, record.CompletedCraftCategory,
   534	                               DiscipleAttributesConfig.WorkCraftXpPerCompletion);
   535	            }
   536	        }
   537	
   538	        /// <summary>Adds XP to one category, reading through the shared safe accessor and clamping to the cap.</summary>
   539	        private static void AddSkillXp(DiscipleAttributes attributes, string category, float amount)
   540	        {
   541	            if (string.IsNullOrEmpty(category) || amount == 0f) return;
   542	            if (attributes.SkillXp == null) attributes.SkillXp = new Dictionary<string, float>();
   543	
   544	            float current = DiscipleAttributes.GetSkillXp(attributes, category);
   545	            attributes.SkillXp[category] = ClampSkillXp(current + amount);
   546	        }
   547	
   548	        /// <summary>Clamps to the configured stamina bounds (a non-finite value is left for Normalize to repair).</summary>
   549	        private static float ClampStamina(float value)
   550	        {
   551	            if (float.IsNaN(value) || float.IsInfinity(value)) return value;
   552	            if (value < DiscipleAttributesConfig.StaminaMin) return DiscipleAttributesConfig.StaminaMin;
   553	            if (value > DiscipleAttributesConfig.StaminaMax) return DiscipleAttributesConfig.StaminaMax;
   554	            return value;
   555	        }
   556	
   557	        /// <summary>Clamps to the configured XP bounds (0..cap).</summary>
   558	        private static float ClampSkillXp(float value)
   559	        {
   560	            if (float.IsNaN(value) || float.IsInfinity(value)) return DiscipleAttributesConfig.SkillXpDefault;
   561	            if (value < DiscipleAttributesConfig.SkillXpMin) return DiscipleAttributesConfig.SkillXpMin;
   562	            if (value > DiscipleAttributesConfig.SkillXpMax) return DiscipleAttributesConfig.SkillXpMax;
   563	            return value;
   564	        }
   565	
   566	        /// <summary>P10B — the resting task id (same literal KnownTasks is built with).</summary>
   567	        private static bool IsMeditationTask(string taskId)
   568	        {
   569	            return taskId == "meditation";
   570	        }
   571	
   572	        // ---- P10B test seams (EditMode tests, no reflection) ----
   573	
   574	        /// <summary>The outcome recorded for one disciple in the tick that just ran.</summary>
   575	        public bool TryGetLastWorkOutcome(string discipleId, out DiscipleWorkOutcome outcome)
   576	        {
   577	            WorkTickRecord record;
   578	            if (!string.IsNullOrEmpty(discipleId) && _workTick.TryGetValue(discipleId, out record))
   579	            {
   580	                outcome = record.Outcome;
   581	                return true;
   582	            }
   583	
   584	            outcome = DiscipleWorkOutcome.None;
   585	            return false;
   586	        }
   587	
   588	        /// <summary>
   589	        /// How many outcome records the last tick wrote. Equal to the number of
   590	        /// disciples means exactly one record each — a second record for the same
   591	        /// disciple would be one more than the roster size.
   592	        /// </summary>
   593	        public int LastTickOutcomeRecordCountForTest => _workTickRecordCount;
   594	
   595	        // Adds a new Outer Disciple assigned to a gathering task, round-robin
   596	        // across GatheringTasks so recruits don't all pile onto one resource.
   597	        public void RecruitOuterDisciple(DiscipleSex sex = DiscipleSex.Unspecified)
   598	        {
   599	            var index = _state.Disciples.Count;
   600	            var task = GatheringTasks[index % GatheringTasks.Length];
   601	            var name = RecruitNamePool[index % RecruitNamePool.Length];
   602	
   603	            // Default parity: even index → Male, odd → Female (preserves existing mixed-roster look)
   604	            var resolvedSex = (sex != DiscipleSex.Unspecified) ? sex
   605	                : (index % 2 == 0) ? DiscipleSex.Male : DiscipleSex.Female;
   606	
   607	            var disciple = new DiscipleState
   608	            {
   609	                DiscipleId = $"d{index + 1:000}",
   610	                DisplayName = name,
   611	                Rank = DiscipleRank.OuterDisciple,
   612	                Wallet = new CurrencyWallet(),
   613	                PersonalInventory = new List<InventoryItem>(),
   614	                CurrentTask = task,
   615	                Sex = resolvedSex,
   616	                Avatar = CreateStarterAvatar(index, resolvedSex),
   617	                // P9A — new disciples start Manual (explicit, matching the field default).
   618	                ControlMode = DiscipleControlMode.Manual,
   619	                // P10A — fresh, normalized attribute block (stamina 100, all 3 skill
   620	                // categories present at 0 XP). Data only; nothing consumes it yet.
   621	                Attributes = DiscipleAttributes.Normalize(new DiscipleAttributes()),
   622	            };
   623	
   624	            _state.Disciples.Add(disciple);
   625	            _discipleRecruitedPublisher.Publish(new DiscipleRecruitedMessage
   626	            {
   627	                DiscipleId = disciple.DiscipleId,
   628	                DisplayName = disciple.DisplayName,
   629	            });
   630	
   631	            Debug.Log($"[SectStateProvider] Recruited outer disciple: {disciple.DisplayName} ({disciple.DiscipleId}), sex={resolvedSex}, assigned to {task}");
   632	        }
   633	
   634	        private AvatarAppearance CreateStarterAvatar(int rosterIndex, DiscipleSex sex)
   635	        {
   636	            var a = new AvatarAppearance();
   637	            a.SetSlot(AvatarSlots.Body, "body_robe_grey");
   638	            a.SetSlot(AvatarSlots.Head, (sex == DiscipleSex.Female) ? "head_female_01" : "head_male_01");
   639	            a.SetSlot(AvatarSlots.Hair, StarterHair[rosterIndex % StarterHair.Length]);
   640	            return a;
   641	        }
   642	
   643	        public bool TryChangeAvatarPart(string discipleId, string slot, string partId,
   644	                                        out string failReason, out AvatarAppearance result)
   645	        {
   646	            failReason = string.Empty;
   647	            result = null;
   648	
   649	            var disciple = _state.Disciples.FirstOrDefault(d => d.DiscipleId == discipleId);
   650	            if (disciple == null) { failReason = $"No disciple with id: {discipleId}"; return false; }
   651	
   652	            if (System.Array.IndexOf(AvatarSlots.Equippable, slot) < 0)
   653	            { failReason = $"Invalid slot: {slot}"; return false; }
   654	
   655	            if (!_avatarPartPool.IsValidForSlot(slot, partId))
   656	            { failReason = $"PartId '{partId}' is not valid for slot '{slot}'"; return false; }
   657	
   658	            // Pose validation: reject a part whose poseId is non-empty and
   659	            // differs from the disciple's effective pose.
   660	            if (!string.IsNullOrEmpty(partId))
   661	            {
   662	                var partDef = _avatarPartPool.GetById(partId);
   663	                if (partDef != null && !string.IsNullOrEmpty(partDef.poseId))
   664	                {
   665	                    string effectivePose = (disciple.Avatar != null && !string.IsNullOrEmpty(disciple.Avatar.PoseId))
   666	                        ? disciple.Avatar.PoseId
   667	                        : "pose_idle_01";
   668	                    if (partDef.poseId != effectivePose)
   669	                    {
   670	                        failReason = $"Part '{partId}' requires pose '{partDef.poseId}' but disciple is in pose '{effectivePose}'.";
   671	                        return false;
   672	                    }
   673	                }
   674	            }
   675	
   676	            // Validation ชั้น 5 — coverage: part ต้องมี art อย่างน้อย 1 backend ที่เปิดใช้
   677	            // (empty layer = เจตนา "ถอดออก" ผ่านเสมอ — *_none defaults)
   678	            // Face split (Roadmap #1): face sub-layers เป็น portrait-only ตามดีไซน์ (R4 —
   679	            // chibi เก็บ feature baked-in) จึงผ่านด้วย Portrait เดี่ยว โดยไม่ต้องมี chibi/spine art
   680	            // (แก้ latent bug: acc_none — ชิ้น "ถอดเครื่องประดับ" ที่มีแต่ chibi art — เคยโดน reject)
   681	            if (!string.IsNullOrEmpty(partId))
   682	            {
   683	                var partDef = _avatarPartPool.GetById(partId);
   684	                if (partDef != null && !partDef.IsEmptyLayer)
   685	                {
   686	                    bool anyCovered =
   687	                        partDef.Supports(VisualBackend.Portrait) ||
   688	                        (_visualConfig != null && _visualConfig.SpriteSheetEnabled &&
   689	                         partDef.Supports(VisualBackend.SpriteSheet)) ||
   690	                        (_visualConfig != null && _visualConfig.SpineEnabled &&
   691	                         partDef.Supports(VisualBackend.Spine));
   692	                    if (!anyCovered)
   693	                    {
   694	                        failReason = $"Part '{partId}' has no art on any enabled backend (coverage check).";
   695	                        return false;
   696	                    }
   697	                }
   698	            }
   699	
   700	            // Validation ชั้น 6 — entitlement (Phase 5, §8): part ที่ติด entitlement
   701	            // ต้องผ่าน IVisualEntitlementProvider เท่านั้น — ต่างจากชั้น 5 ที่ป้องกัน
   702	            // "render ไม่ได้" ชั้นนี้ป้องกัน "ไม่มีสิทธิ์ใช้" — failReason เขียนให้
   703	            // AI/UI อ่านแล้วเข้าใจเหตุผล (ตามสเปก Phase 5)
   704	            if (!string.IsNullOrEmpty(partId) && _entitlementProvider != null)
   705	            {
   706	                var entitlementDef = _avatarPartPool.GetById(partId);
   707	                if (entitlementDef != null && !string.IsNullOrEmpty(entitlementDef.entitlement) &&
   708	                    !_entitlementProvider.CanUse(discipleId, entitlementDef.entitlement))
   709	                {
   710	                    failReason = $"Part '{partId}' requires entitlement '{entitlementDef.entitlement}'.";
   711	                    return false;
   712	                }
   713	            }
   714	
   715	            if (disciple.Avatar == null) disciple.Avatar = new AvatarAppearance();
   716	
   717	            var oldPart = disciple.Avatar.GetSlot(slot);
   718	            disciple.Avatar.SetSlot(slot, partId);
   719	
   720	            _avatarChangedPublisher.Publish(new AvatarEquipmentChangedMessage
   721	            {
   722	                DiscipleId = discipleId,
   723	                Slot       = slot,
   724	                OldPartId  = oldPart,
   725	                NewPartId  = partId
   726	            });
   727	
   728	            result = disciple.Avatar.Clone();   // return copy, not reference to live state
   729	            return true;
   730	        }
   731	
   732	        // ---------- P4 (local ownership test harness) — validated ownership assignment ----------
   733	        // Non-Npc OwnerId convention: a real Twitch user id is numeric, so the
   734	        // synthetic local-identity convention is "viewer_*" / "player_*" (alpha
   735	        // prefix + underscore, no whitespace) — clearly fake, never shaped like a
   736	        // real Twitch id. Real authentication (P5B+) replaces this entirely.
   737	        private const string OwnerIdPattern = "^[A-Za-z][A-Za-z0-9]*(_[A-Za-z0-9]+)*$";
   738	
   739	        public bool TrySetDiscipleOwner(string discipleId, DiscipleOwnerType ownerType,
   740	                                        string ownerId, out string failReason)
   741	        {
   742	            failReason = string.Empty;
   743	
   744	            // 1. disciple must exist
   745	            var disciple = FindDisciple(discipleId);
   746	            if (disciple == null)
   747	            {
   748	                failReason = $"No disciple with id: {discipleId}";
   749	                return false;
   750	            }
   751	
   752	            // 2. enum value must be defined (fail-closed on out-of-range casts)
   753	            if (!Enum.IsDefined(typeof(DiscipleOwnerType), ownerType))
   754	            {
   755	                failReason = $"Undefined DiscipleOwnerType value: {ownerType}";
   756	                return false;
   757	            }
   758	
   759	            var trimmedOwnerId = (ownerId ?? string.Empty).Trim();
   760	
   761	            // 3. Npc normalizes OwnerId to empty (no orphaned ids on Npc rows)
   762	            if (ownerType == DiscipleOwnerType.Npc)
   763	            {
   764	                if (!string.IsNullOrEmpty(trimmedOwnerId))
   765	                {
   766	                    failReason = "Npc ownership must carry an empty OwnerId.";
   767	                    return false;
   768	                }
   769	                trimmedOwnerId = string.Empty;
   770	            }
   771	            else
   772	            {
   773	                // 4. non-Npc identities satisfy the identity convention
   774	                if (string.IsNullOrEmpty(trimmedOwnerId) ||
   775	                    !System.Text.RegularExpressions.Regex.IsMatch(trimmedOwnerId, OwnerIdPattern))
   776	                {
   777	                    failReason = $"OwnerId '{trimmedOwnerId}' does not satisfy the identity convention " +
   778	                                 "(alpha prefix, [A-Za-z0-9_], no whitespace).";
   779	                    return false;
   780	                }
   781	            }
   782	
   783	            // 5. a Viewer identity must not control a different active disciple
   784	            if (ownerType == DiscipleOwnerType.Viewer && !string.IsNullOrE
... [TRUNCATED: แสดง 40000 จาก 113534 bytes — ขอส่วนที่เหลือผ่าน need_files]
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
    8  .
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
    7  McpBridge/Shared
    8  Shared
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
   18  UnityProject/Assets/Editor
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
    9  UnityProject/Assets/Prefabs/UI
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
   17  UnityProject/Assets/Scripts/Core
    5  UnityProject/Assets/Scripts/Core/Installers
    7  UnityProject/Assets/Scripts/Core/Persistence
    7  UnityProject/Assets/Scripts/Core/Session
    2  UnityProject/Assets/Scripts/Data
    9  UnityProject/Assets/Scripts/Data/Gen
    1  UnityProject/Assets/Scripts/Scenes/SectScene
    7  UnityProject/Assets/Scripts/Shared
    7  UnityProject/Assets/Scripts/Systems
    1  UnityProject/Assets/Scripts/Tests
    9  UnityProject/Assets/Scripts/UI/Core
   15  UnityProject/Assets/Scripts/UI/Presenters
    4  UnityProject/Assets/Scripts/UI/Systems
   21  UnityProject/Assets/Scripts/UI/Views
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
   49  UnityProject/Assets/Tests/EditMode
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
  Common-Rules.md
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
  McpBridge/Shared/DiscipleAttributes.cs
  McpBridge/Shared/GameMessages.cs
  McpBridge/Shared/MessageTypes.cs
  McpBridge/Shared/MockSectData.cs
  McpBridge/Shared/SectEconomyState.cs
  McpBridge/Shared/SectSessionSnapshot.cs
  McpBridge/Shared/ViewerMembership.cs
  README.md
  Shared/DiscipleAttributes.cs
  Shared/GameMessages.cs
  Shared/MessageTypes.cs
  Shared/MockSectData.cs
  Shared/SectEconomyState.cs
  Shared/SectSessionSnapshot.cs
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
  UnityProject/Assets/Editor/EventPopupPanelGenerator.cs
  UnityProject/Assets/Editor/GeneratorRemoteControl.cs
  UnityProject/Assets/Editor/IngameMenuPanelGenerator.cs
  UnityProject/Assets/Editor/LandMaskBuilder.cs
  UnityProject/Assets/Editor/LandMaskPainterWindow.cs
  UnityProject/Assets/Editor/LocalOwnershipHarness.cs
  UnityProject/Assets/Editor/PortraitOverrideImporter.cs
  UnityProject/Assets/Editor/TaskAssignmentPanelGenerator.cs
  UnityProject/Assets/Editor/ThaiFontAtlasBaker.cs
  UnityProject/Assets/Editor/TimeControlPanelGenerator.cs
  UnityProject/Assets/Editor/TitleMenuPanelGenerator.cs
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
  UnityProject/Assets/Scripts/Core/MainThreadDispatch.cs
  UnityProject/Assets/Scripts/Core/OwnershipObservability.cs
  UnityProject/Assets/Scripts/Core/Persistence/SaveFileSystem.cs
  UnityProject/Assets/Scripts/Core/Persistence/SaveOperationGate.cs
  UnityProject/Assets/Scripts/Core/Persistence/SaveSessionService.cs
  UnityProject/Assets/Scripts/Core/Persistence/SaveSlotPersistence.cs
  UnityProject/Assets/Scripts/Core/Persistence/SaveSlotRepository.cs
  UnityProject/Assets/Scripts/Core/Persistence/SaveWorkScheduler.cs
  UnityProject/Assets/Scripts/Core/Persistence/SessionTransitionTracker.cs
  UnityProject/Assets/Scripts/Core/PurchaseItemHandler.cs
  UnityProject/Assets/Scripts/Core/SceneLoader.cs
  UnityProject/Assets/Scripts/Core/SceneMessages.cs
  UnityProject/Assets/Scripts/Core/SceneNames.cs
  UnityProject/Assets/Scripts/Core/Session/GameSessionCoordinator.cs
  UnityProject/Assets/Scripts/Core/Session/GameSessionPhase.cs
  UnityProject/Assets/Scripts/Core/Session/IGameSessionCoordinator.cs
  UnityProject/Assets/Scripts/Core/Session/PrototypeStarterStateFactory.cs
  UnityProject/Assets/Scripts/Core/Session/SessionMessages.cs
  UnityProject/Assets/Scripts/Core/Session/SessionRuntimeConfig.cs
  UnityProject/Assets/Scripts/Core/Session/SessionUiCloser.cs
  UnityProject/Assets/Scripts/Core/SessionSnapshotService.cs
  UnityProject/Assets/Scripts/Core/TaskObservability.cs
  UnityProject/Assets/Scripts/Core/TimeRuntimeConfig.cs
  UnityProject/Assets/Scripts/Core/TimeSystem.cs
  UnityProject/Assets/Scripts/Core/ViewerMembershipPersistence.cs
  UnityProject/Assets/Scripts/Core/WorldEventRuntimeConfig.cs
  UnityProject/Assets/Scripts/Data/AvatarPartPool.cs
  UnityProject/Assets/Scripts/Data/LubanEventPool.cs
  UnityProject/Assets/Scripts/Scenes/SectScene/SectSceneLifetimeScope.cs
  UnityProject/Assets/Scripts/Shared/DiscipleAttributes.cs
  UnityProject/Assets/Scripts/Shared/GameMessages.cs
  UnityProject/Assets/Scripts/Shared/MessageTypes.cs
  UnityProject/Assets/Scripts/Shared/MockSectData.cs
  UnityProject/Assets/Scripts/Shared/SectEconomyState.cs
  UnityProject/Assets/Scripts/Shared/SectSessionSnapshot.cs
  UnityProject/Assets/Scripts/Shared/ViewerMembership.cs
  UnityProject/Assets/Scripts/Systems/AutoTaskScheduler.cs
  UnityProject/Assets/Scripts/Systems/AutoTaskScoring.cs
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
  UnityProject/Assets/Scripts/UI/Presenters/IngameMenuPresenter.cs
  UnityProject/Assets/Scripts/UI/Presenters/LogWindowPresenter.cs
  UnityProject/Assets/Scripts/UI/Presenters/ResourcePopupPresenter.cs
  UnityProject/Assets/Scripts/UI/Presenters/TaskAssignmentPresenter.cs
  UnityProject/Assets/Scripts/UI/Presenters/TimeControlPresenter.cs
  UnityProject/Assets/Scripts/UI/Presenters/TitleMenuPresenter.cs
  UnityProject/Assets/Scripts/UI/Presenters/WalletHudPresenter.cs
  UnityProject/Assets/Scripts/UI/Presenters/WorldEventChipPresenter.cs
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
  UnityProject/Assets/Scripts/UI/Views/IngameMenuView.cs
  UnityProject/Assets/Scripts/UI/Views/LogWindowView.cs
  UnityProject/Assets/Scripts/UI/Views/PortraitOverrideBinding.cs
  UnityProject/Assets/Scripts/UI/Views/ResourcePopupView.cs
  UnityProject/Assets/Scripts/UI/Views/TaskAssignmentView.cs
  UnityProject/Assets/Scripts/UI/Views/TimeControlView.cs
  UnityProject/Assets/Scripts/UI/Views/TitleMenuView.cs
  UnityProject/Assets/Scripts/UI/Views/UIViewBase.cs
  UnityProject/Assets/Scripts/UI/Views/WalletHudView.cs
  UnityProject/Assets/Scripts/UI/Views/WorldEventChipView.cs
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
  ... [198 more files truncated — ปรับ TREE_MAX_LINES หรือ .aiignore]
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

