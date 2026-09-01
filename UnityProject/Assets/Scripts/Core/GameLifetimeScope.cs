using MessagePipe;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    // Composition root for the whole game framework (see the architecture
    // diagram: this is the "Game manager · VContainer composition root" box).
    // Registers the internal MessagePipe bus, the MessagePipe.Interprocess
    // TCP transport to the external MCP bridge process, and the four
    // gameplay subsystems as VContainer entry points.
    public class GameLifetimeScope : LifetimeScope
    {
        [SerializeField] private string interprocessHost = "127.0.0.1";
        [SerializeField] private int interprocessPort = 3215;

        [Header("UI (Xianxia.UI.MVP Lite)")]
        [SerializeField] private Xianxia.Sect.UI.UIRoot uiRoot;
        [SerializeField] private Xianxia.Sect.UI.UIPanelCatalog uiPanelCatalog;

        protected override void Configure(IContainerBuilder builder)
        {
            // Design-time data now sourced from Luban (see DataTables/ at
            // the workspace root and Assets/Scripts/Data/LubanEventPool.cs),
            // not a ScriptableObject dragged into the Inspector - so this is
            // constructed directly rather than serialized.
            builder.RegisterInstance(new LubanEventPool());

            // Avatar part definitions loaded from Resources/Data/avatar_parts.json
            builder.Register<AvatarPartPool>(Lifetime.Singleton);

            // --- UI (Xianxia.UI.MVP Lite) ---
            // SectHudView is gone - replaced by the ResourceHud panel below,
            // which gets its data from SectResourceChangedMessage instead of
            // polling ISectStateProvider on a timer.
            builder.RegisterInstance(uiRoot);
            builder.RegisterInstance(uiPanelCatalog);
            builder.Register<Xianxia.Sect.UI.UIService>(Lifetime.Singleton);
            builder.Register<DecisionExecutor>(Lifetime.Singleton);
            builder.Register<Xianxia.Sect.UI.EventPopupPresenter>(Lifetime.Transient);
            builder.Register<Xianxia.Sect.UI.ResourceHudPresenter>(Lifetime.Transient);
            builder.Register<Xianxia.Sect.UI.LogWindowPresenter>(Lifetime.Transient);
            builder.Register<Xianxia.Sect.UI.AvatarCustomizationPresenter>(Lifetime.Transient);
            builder.RegisterEntryPoint<Xianxia.Sect.UI.WorldEventUISystem>(Lifetime.Singleton);
            builder.RegisterEntryPoint<Xianxia.Sect.UI.UIBootstrap>(Lifetime.Singleton);

            // --- in-memory pub/sub + request-response (internal subsystem bus) ---
            var options = builder.RegisterMessagePipe(pipeOptions =>
            {
                pipeOptions.InstanceLifetime = InstanceLifetime.Singleton;
            });

            // --- interprocess transport: Unity <-> MCP bridge process, over TCP ---
            // Unity hosts the TCP endpoint; the bridge process connects as a client.
            var messagePipeBuilder = builder.ToMessagePipeBuilder();
            var interprocess = messagePipeBuilder.AddTcpInterprocess(
                interprocessHost,
                interprocessPort,
                tcpOptions =>
                {
                    tcpOptions.HostAsServer = true;
                    tcpOptions.InstanceLifetime = InstanceLifetime.Singleton;
                });

            // Only register the message types the bridge actually needs on the
            // wire. TimeSpeedChangedMessage stays internal-only on purpose -
            // the bridge doesn't need per-frame speed changes.
            // WorldEventTriggeredMessage is deliberately NOT registered here
            // (see AwaitWorldEventRequest in GameMessages.cs) - the bridge
            // can't safely IDistributedSubscriber over this TCP transport,
            // it's request-response only for that one.
            messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, DiscipleRecruitedMessage>(interprocess);
            messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, DiscipleRankChangedMessage>(interprocess);
            messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, SectResourceChangedMessage>(interprocess);
            messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, ContributionEarnedMessage>(interprocess);
            messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, ExecuteDecisionMessage>(interprocess);

            // Request/response: bridge asks "what's the sect state right now".
            // Correction from an earlier pass: RegisterTcpRemoteRequestHandler
            // is needed here too, even though Unity is HostAsServer=true and
            // holds the real handler. It's what wires the TCP worker to the
            // registered IAsyncRequestHandler, not just a caller-side proxy -
            // confirmed against Wanxiang.Guanxiangtai's FrontendIpcServer.cs,
            // which registers both on its (HostAsServer=true) side.
            messagePipeBuilder.RegisterTcpRemoteRequestHandler<SectStateQuery, SectStateSnapshot>(interprocess);
            builder.RegisterAsyncRequestHandler<SectStateQuery, SectStateSnapshot, SectStateQueryHandler>(options);

            // Request/response: bridge asks "wait for the next world event"
            // and blocks until Unity's TimeSystem completes it - see the
            // comment on AwaitWorldEventRequest for why this isn't pub/sub.
            messagePipeBuilder.RegisterTcpRemoteRequestHandler<AwaitWorldEventRequest, AwaitWorldEventResponse>(interprocess);
            builder.RegisterAsyncRequestHandler<AwaitWorldEventRequest, AwaitWorldEventResponse, AwaitWorldEventHandler>(options);

            // Request/response: a disciple buying an item from the sect
            // stockpile with contribution.
            messagePipeBuilder.RegisterTcpRemoteRequestHandler<PurchaseItemRequest, PurchaseItemResponse>(interprocess);
            builder.RegisterAsyncRequestHandler<PurchaseItemRequest, PurchaseItemResponse, PurchaseItemHandler>(options);

            // Request/response: changing a disciple's avatar part
            messagePipeBuilder.RegisterTcpRemoteRequestHandler<ChangeAvatarPartRequest, ChangeAvatarPartResponse>(interprocess);
            builder.RegisterAsyncRequestHandler<ChangeAvatarPartRequest, ChangeAvatarPartResponse, ChangeAvatarPartHandler>(options);

            // Interprocess pub/sub: broadcast avatar equipment changes to UI + MCP client
            messagePipeBuilder.RegisterTcpInterprocessMessageBroker<string, AvatarEquipmentChangedMessage>(interprocess);

            // --- gameplay subsystems, started/ticked by VContainer ---
            builder.RegisterEntryPoint<TimeSystem>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint<DiscipleSystem>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint<ResourceCraftingSystem>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint<BuildingSystem>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint<DecisionLogger>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint<WorldEventSystem>(Lifetime.Singleton).AsSelf();

            // Aggregates the subsystems above into one SectEconomyState for
            // SectStateQueryHandler to serve. See ISectStateProvider.
            builder.Register<ISectStateProvider, SectStateProvider>(Lifetime.Singleton);
        }
    }
}
