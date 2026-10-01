---
title: VContainer Composition Root
type: concept
sources:
  - UnityProject/Assets/Scripts/Core/GameLifetimeScope.cs
  - UnityProject/Assets/Scripts/Core/Installers/
  - UnityProject/Assets/Scripts/Scenes/SectScene/SectSceneLifetimeScope.cs
related:
  - "[[sources/architecture]]"
  - "[[concepts/message-pipe-bus]]"
  - "[[concepts/additive-scene-architecture]]"
created: 2026-08-31
updated: 2026-10-02
confidence: high
tags: [di, vcontainer, composition-root, lifetime]
---

# VContainer Composition Root

> The single point where all dependencies are wired. Lives in
> `Assets/Scripts/Core/GameLifetimeScope.cs`.

## What is it

`GameLifetimeScope : VContainer.LifetimeScope` is a `MonoBehaviour` placed on
a GameObject in `SampleScene.unity` (= CoreScene). On `Awake`, VContainer calls
`Configure(IContainerBuilder builder)`, which registers every service,
system, message broker, and request handler in the game.

## Composition layout (Phase 1–2 refactor, 2026-10)

Root `Configure()` เป็นแค่ "สารบัญ" — มันเรียก installer extension 5 ตัวตามลำดับ
แล้วจบ (ไม่มีบล็อก registration ยาว ๆ อีก):

| Installer (`Assets/Scripts/Core/Installers/`) | ลงทะเบียนอะไร |
|---|---|
| `BuildingInstaller` | BuildingGrid/DefPool/PlacementController/BuildingMenuPresenter + `BuildingPlacementUISystem` |
| `VisualInstaller` | VisualRuntimeConfig (gate Spine), ChibiFrameBank/Clock, DiscipleVisualSystem, entitlement, VisualTierPolicy |
| `UIInstaller` | UIService, DecisionExecutor, presenter 6 ตัว (Transient), `UIBootstrap`/`DiscipleDetailUISystem`/`WorldEventUISystem` |
| `InterprocessInstaller` | MessagePipe + TCP interprocess brokers/request handlers (คืน `MessagePipeOptions`) |
| `GameplayInstaller` | TimeSystem/DiscipleSystem/ResourceCraftingSystem/BuildingSystem/DecisionLogger/WorldEventSystem + `ISectStateProvider` |

`SceneLoader` (entry point) ยังอยู่ที่ root เพราะเป็นเจ้าของการโหลดฉาก

### Child scope ของฉากเกมเพลย์ (SectScene)
`SectSceneLifetimeScope` (ใน `Assets/Scripts/Scenes/SectScene/`) เป็น **child** ของ
root scope — เก็บของที่ผูกกับฉากเท่านั้น: camera rig ports 5 ตัว (IRigMessageBus,
ICameraRigEnvironment, ICellSpriteMetrics, IMainThreadQueue, IRigClock) + entry
points 3 ตัว (CameraRigController / GridOverlayRenderer / TerrainBackdropRenderer)

การผูก parent ใช้ `LifetimeScope.EnqueueParent(rootScope)` ใน `SceneLoader`
(ห่อช่วง `LoadSceneAsync`) — VContainer เก็บ parent ไว้ใน static stack แล้ว child
pop ตอน `Awake` ระหว่างโหลด จึงไม่ต้องตั้ง Parent Reference บน Inspector เลย

MessagePipe ยังทำงานข้าม scope ได้เพราะทั้ง root และ child resolve
`ISubscriber<T>`/`IPublisher<T>` จาก MessagePipe global ชุดเดียวกัน (child ได้จาก
parent) — publish จาก root จึงถึง subscriber ใน child (ยืนยัน live แล้ว)

### Pitfalls ที่เคยเจอจริง

- ❌ **ห้าม register entry point/port ซ้ำที่ root และ child** — เดิม rig ports +
  กล้อง/overlay/backdrop ค้างอยู่ที่ root พร้อมกับ child ทำให้เกิด instance ละ 2 ตัว:
  log `Framing source values` / `Backdrop placed` ออก 2 ครั้งต่อการโหลด, pan/zoom
  ถูกประมวลผลซ้ำ → ย้าย = ย้าย (ลบต้นทางเสมอ) ไม่ใช่ copy
- ❌ **ห้าม `builder.RegisterInstance(this)`** สำหรับ scope เอง — VContainer ลง
  `RegisterInstance<LifetimeScope>(this).AsSelf()` ให้แล้วใน `InstallTo`; เพิ่มซ้ำ
  จะได้ `VContainerException: Conflict implementation type` และ container build
  ล้มทั้งเกม — inject ด้วย **base type** `LifetimeScope` แทน
- ✅ entry point ที่ทำงานกับฉากควรมี **idempotent guard** เพราะ `Start()` fallback
  (`IsSceneLoaded(...)`) กับ `SceneLoadedMessage` อาจเข้า handler เดียวกันสองรอบ

## Key API

| Method | Purpose |
|---|---|
| `builder.RegisterInstance(instance)` | Singleton by reference (e.g. `LubanEventPool`, `UIRoot`) |
| `builder.Register<T>(Lifetime)` | New instance per resolution; Lifetime controls scope |
| `builder.RegisterEntryPoint<T>(Lifetime)` | Same as Register + auto-invoke `IStartable.Start()` on Awake, `ITickable.Tick()` every frame |
| `builder.AsSelf()` | Also register as the concrete type (for direct injection) |
| `builder.Register<ISectStateProvider, SectStateProvider>(Lifetime)` | Register concrete as interface |
| `builder.RegisterMessagePipe(...)` | Returns options for in-memory bus config |
| `builder.ToMessagePipeBuilder()` | Switch to MessagePipe builder for `.AddTcpInterprocess(...)` etc. |
| `builder.RegisterAsyncRequestHandler<TReq, TRes, THandler>(options)` | Wires an in-memory `IAsyncRequestHandler` to the bus |

## Lifetimes Used in This Project

| Lifetime | Used for |
|---|---|
| `Singleton` | Stateless services, state holders, message brokers, UI service |
| `Transient` | UI presenters (fresh per panel open, no stale subscriptions) |
| `EntryPoint` | Subsystems (auto-started by VContainer) |

## Why VContainer (vs Zenject)

- Lighter, faster
- Modern async-friendly API
- Direct `IStartable`/`ITickable` support without writing bootstrap code
- MessagePipe integration package is well-maintained

## Anti-Patterns to Avoid

- ❌ Don't use `FindObjectOfType` — everything should be DI-resolved
- ❌ Don't `new` services that should be DI-registered (e.g. `new UIService()`)
- ❌ Don't register concrete classes as both interface and concrete unless
  you have a reason — pick one
- ❌ Don't put business logic in `GameLifetimeScope` — it's for wiring only

## Adding a New Subsystem — Checklist

1. Create the class implementing `IStartable` / `ITickable` (or both)
2. เลือก installer ให้ถูกชั้น:
   - **persistent/ไม่ผูกฉาก** → เพิ่มใน installer ที่ตรงหมวด (`GameplayInstaller`,
     `UIInstaller`, `VisualInstaller`, `BuildingInstaller`) — ห้ามยัดกลับเข้า
     `GameLifetimeScope.Configure()` ตรง ๆ
   - **ผูกกับฉากเกมเพลย์** (กล้อง/ฉาก/grid ของ SectScene) → `SectSceneLifetimeScope`
   ```csharp
   builder.RegisterEntryPoint<MyNewSystem>(Lifetime.Singleton).AsSelf();
   ```
3. Inject dependencies via constructor (VContainer resolves automatically)
4. If it needs to talk to other systems, use `ISubscriber<T>` / `IPublisher<T>`
   (injected), not direct references

## Related Pages

- [[sources/architecture|Architecture]]
- [[concepts/message-pipe-bus|MessagePipe Bus]]
- [[concepts/mcp-bridge|MCP Bridge]]
