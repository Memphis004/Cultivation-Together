---
title: GameLifetimeScope.cs
type: snippet
sources:
  - UnityProject/Assets/Scripts/Core/GameLifetimeScope.cs
related:
  - "[[concepts/vcontainer-composition]]"
  - "[[concepts/message-pipe-bus]]"
  - "[[concepts/mcp-bridge]]"
created: 2026-08-31
updated: 2026-08-31
confidence: high
tags: [di, vcontainer, composition-root, messagepipe]
---

# GameLifetimeScope.cs

> The composition root. Reads the entire system architecture in one file.
> **File path**: `UnityProject/Assets/Scripts/Core/GameLifetimeScope.cs` (109 lines)

## Purpose

Wires the entire game framework:
- VContainer DI container
- MessagePipe in-memory bus
- MessagePipe.Interprocess TCP transport (Unity = server, McpBridge = client)
- UI services and presenters
- All 4 gameplay subsystems + 2 UI entry points + state aggregator

This is the single source of truth for "what exists in the game".

## Public API

| Member | Type | Description |
|---|---|---|
| `interprocessHost` | `string` (SerializeField) | TCP bind host, default `127.0.0.1` |
| `interprocessPort` | `int` (SerializeField) | TCP bind port, default `3215` |
| `uiRoot` | `UIRoot` (SerializeField) | Canvas root for UI |
| `uiPanelCatalog` | `UIPanelCatalog` (SerializeField) | ScriptableObject with panel definitions |
| `Configure(IContainerBuilder)` | override | Wires everything (called by VContainer on Awake) |

## Dependencies

- **VContainer** — `LifetimeScope` base class, `IContainerBuilder`
- **MessagePipe** — `RegisterMessagePipe`, `ToMessagePipeBuilder`, `AddTcpInterprocess`
- **All subsystems** — registered as `IStartable`/`ITickable` entry points

## Code

```csharp
using MessagePipe;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    // Composition root for the whole game framework.
    public class GameLifetimeScope : LifetimeScope
    {
        [SerializeField] private string interprocessHost = "127.0.0.1";
        [SerializeField] private int interprocessPort = 3215;

        [Header("UI (Xianxia.UI.MVP Lite)")]
        [SerializeField] private Xianxia.Sect.UI.UIRoot uiRoot;
        [SerializeField] private Xianxia.Sect.UI.UIPanelCatalog uiPanelCatalog;

        protected override void Configure(IContainerBuilder builder)
        {
            // Design-time data from Luban (not ScriptableObject)
            builder.RegisterInstance(new LubanEventPool());

            // --- UI (Xianxia.UI.MVP Lite) ---
            builder.RegisterInstance(uiRoot);
            builder.RegisterInstance(uiPanelCatalog);
            builder.Register<Xianxia.Sect.UI.UIService>(Lifetime.Singleton);
            builder.Register<DecisionExecutor>(Lifetime.Singleton);
            builder.Register<Xianxia.Sect.UI.EventPopupPresenter>(Lifetime.Transient);
            builder.Register<Xianxia.Sect.UI.WalletHudPresenter>(Lifetime.Transient);
            builder.RegisterEntryPoint<Xianxia.Sect.UI.WorldEventUISystem>(Lifetime.Singleton);
            builder.RegisterEntryPoint<Xianxia.Sect.UI.UIBootstrap>(Lifetime.Singleton);

            // --- in-memory pub/sub + request-response ---
            var options = builder.RegisterMessagePipe(pipeOptions =>
            {
                pipeOptions.InstanceLifetime = InstanceLifetime.Singleton;
            });

            // --- interprocess transport: Unity <-> MCP bridge over TCP ---
            var messagePipeBuilder = builder.ToMessagePipeBuilder();
            var interprocess = messagePipeBuilder.AddTcpInterprocess(
                interprocessHost, interprocessPort,
                tcpOptions =>
                {
                    tcpOptions.HostAsServer = true;
                    tcpOptions.InstanceLifetime = InstanceLifetime.Singleton;
                });

            // Only register the message types the bridge actually needs on the wire.
            // WorldEventTriggeredMessage is deliberately NOT registered here -
            // bridge uses request-response (AwaitWorldEventRequest) instead,
            // because IDistributedSubscriber can't safely cross TCP from bridge side.
            messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, DiscipleRecruitedMessage>(interprocess);
            messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, DiscipleRankChangedMessage>(interprocess);
            messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, SectResourceChangedMessage>(interprocess);
            messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, ContributionEarnedMessage>(interprocess);
            messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, ExecuteDecisionMessage>(interprocess);

            // Request/response: bridge asks "what's the sect state right now"
            messagePipeBuilder.RegisterTcpRemoteRequestHandler<SectStateQuery, SectStateSnapshot>(interprocess);
            builder.RegisterAsyncRequestHandler<SectStateQuery, SectStateSnapshot, SectStateQueryHandler>(options);

            // Request/response: bridge asks "wait for the next world event"
            messagePipeBuilder.RegisterTcpRemoteRequestHandler<AwaitWorldEventRequest, AwaitWorldEventResponse>(interprocess);
            builder.RegisterAsyncRequestHandler<AwaitWorldEventRequest, AwaitWorldEventResponse, AwaitWorldEventHandler>(options);

            // Request/response: disciple buys from stockpile
            messagePipeBuilder.RegisterTcpRemoteRequestHandler<PurchaseItemRequest, PurchaseItemResponse>(interprocess);
            builder.RegisterAsyncRequestHandler<PurchaseItemRequest, PurchaseItemResponse, PurchaseItemHandler>(options);

            // --- gameplay subsystems ---
            builder.RegisterEntryPoint<TimeSystem>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint<DiscipleSystem>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint<ResourceCraftingSystem>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint<BuildingSystem>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint<DecisionLogger>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint<WorldEventSystem>(Lifetime.Singleton).AsSelf();

            // Aggregates the subsystems above into one SectEconomyState
            builder.Register<ISectStateProvider, SectStateProvider>(Lifetime.Singleton);
        }
    }
}
```

## Key Patterns

1. **`RegisterEntryPoint<T>`** — VContainer auto-calls `IStartable.Start()` on Awake, `ITickable.Tick()` every frame
2. **`AsSelf()`** — also resolves as `T` for direct injection (e.g. `TimeSystem` injected into `AwaitWorldEventHandler`)
3. **`RegisterTcpRemoteRequestHandler` on BOTH sides** — common mistake to skip on the hub side (lab 3 bug)
4. **`HostAsServer = true` on Unity** — Unity binds the listening socket; bridge only connects
5. **`RegisterMessagePipe` returns options** — pass to `RegisterAsyncRequestHandler` for in-memory handlers

## What's NOT in this file (but related)

- `SectStateQueryHandler` / `AwaitWorldEventHandler` / `PurchaseItemHandler` — defined in same files as the services they query
- The actual `Awake()` lifecycle — handled by VContainer `LifetimeScope` base class

## Known Issues

- None currently; the file is stable post-lab 13

## Related Pages

- [[concepts/vcontainer-composition|VContainer Composition Root]]
- [[concepts/message-pipe-bus|MessagePipe Bus]]
- [[concepts/mcp-bridge|MCP Bridge]]
- [[sources/architecture|Architecture]]
