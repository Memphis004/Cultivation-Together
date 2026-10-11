using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    // Answers AttemptBreakthroughRequest coming in over the interprocess bus — the
    // AI GM (via the MCP bridge's attempt_breakthrough tool) advancing a disciple
    // exactly one cultivation stage. See SectStateProvider.TryBreakthrough for the
    // rules: full fail-closed validation first, then ONE atomic mutation
    // (stage = next, progress = 0).
    //
    // THREADING (T1): same contract as AssignTaskHandler. InvokeAsync runs on a
    // MessagePipe.Interprocess TCP background thread, and the call mutates live
    // gameplay state (and publishes the in-memory DiscipleCultivationChangedMessage
    // the ข้อมูล tab reacts to), so the body runs AFTER a hop to the Unity main
    // thread — see MainThreadDispatch.RunAsync, the one shared place that owns the
    // hop and the failure contract.
    //
    // PERMISSION: this handler deliberately has no policy of its own. It forwards the
    // requester id straight into TryBreakthrough, which reuses EvaluateTaskPermission —
    // the identical P5B evaluation AssignTaskHandler goes through, Hybrid viewers'
    // 10-minute activity window included. The bridge always sends "SECT_MASTER", so it
    // gets exactly the authority a human sect master has and never a bypass; a valid
    // owner command would refresh that viewer's activity, a SectMaster command does not.
    public class AttemptBreakthroughHandler : IAsyncRequestHandler<AttemptBreakthroughRequest, AttemptBreakthroughResponse>
    {
        private readonly ISectStateProvider _stateProvider;
        private readonly ISessionGate _session;

        public AttemptBreakthroughHandler(ISectStateProvider stateProvider, ISessionGate session = null)
        {
            _stateProvider = stateProvider;
            _session = session;
        }

        public UniTask<AttemptBreakthroughResponse> InvokeAsync(AttemptBreakthroughRequest request, CancellationToken cancellationToken = default)
        {
            return MainThreadDispatch.RunAsync(
                nameof(AttemptBreakthroughHandler),
                $"requester={request?.RequesterId} disciple={request?.DiscipleId}",
                () =>
                {
                    // P12A — a mutation outside a Playing session is refused with a clear
                    // result; the check runs AFTER the main-thread hop so it reads the live phase.
                    if (_session != null && !_session.IsPlaying)
                        return new AttemptBreakthroughResponse { Success = false, FailReason = SessionGate.NoActiveSessionReason };

                    string failReason;
                    var success = _stateProvider.TryBreakthrough(
                        request.RequesterId, request.DiscipleId, out failReason);

                    return new AttemptBreakthroughResponse
                    {
                        Success = success,
                        FailReason = failReason,
                    };
                },
                reason => new AttemptBreakthroughResponse
                {
                    Success = false,
                    FailReason = "internal error: " + reason,
                });
        }
    }
}
