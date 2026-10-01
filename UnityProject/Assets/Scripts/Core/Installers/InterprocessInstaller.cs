using MessagePipe;
using VContainer;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.Installers
{
    /// <summary>
    /// PHASE 1 refactor: บล็อก in-memory pub/sub + interprocess TCP ย้ายมาจาก
    /// GameLifetimeScope.Configure() ครบทุกบรรทัด — ตามกฎงาน: RegisterMessagePipe +
    /// ToMessagePipeBuilder + ทุก RegisterTcp* ต้องอยู่ที่ ROOT scope เท่านั้น
    /// (bridge process ติดต่อ root เสมอ — ห้ามย้ายลง child scope)
    /// คืนค่า MessagePipeOptions กลับให้ caller เผื่อใช้ต่อ (ค่า options ถูกใช้ภายใน
    /// ตัว installer แล้วสำหรับ RegisterAsyncRequestHandler)
    /// </summary>
    internal static class InterprocessInstaller
    {
        public static MessagePipeOptions RegisterInterprocess(
            this IContainerBuilder builder,
            string interprocessHost,
            int interprocessPort)
        {
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

            return options;
        }
    }
}
