---
title: VContainer Composition Root
type: concept
sources:
  - UnityProject/Assets/Scripts/Core/GameLifetimeScope.cs
related:
  - "[[sources/architecture]]"
  - "[[concepts/message-pipe-bus]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [di, vcontainer, composition-root, lifetime]
---

# VContainer Composition Root

> The single point where all dependencies are wired. Lives in
> `Assets/Scripts/Core/GameLifetimeScope.cs`.

## What is it

`GameLifetimeScope : VContainer.LifetimeScope` is a `MonoBehaviour` placed on
a GameObject in `SampleScene.unity`. On `Awake`, VContainer calls
`Configure(IContainerBuilder builder)`, which registers every service,
system, message broker, and request handler in the game.

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
2. Add to `GameLifetimeScope.Configure`:
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
