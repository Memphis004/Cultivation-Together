using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    // Answers PurchaseItemRequest coming in over the interprocess bus - a
    // disciple buying an item from the sect stockpile with contribution.
    // See SectStateProvider.TryPurchaseItem for the actual rules.
    //
    // THREADING (T1): InvokeAsync runs on the MessagePipe.Interprocess TCP background
    // thread. Buying mutates the disciple's wallet AND the shared stockpile, so the
    // call runs after a hop to the Unity main thread (MainThreadDispatch.RunAsync).
    public class PurchaseItemHandler : IAsyncRequestHandler<PurchaseItemRequest, PurchaseItemResponse>
    {
        private readonly ISectStateProvider _stateProvider;
        private readonly ISessionGate _session;

        public PurchaseItemHandler(ISectStateProvider stateProvider, ISessionGate session = null)
        {
            _stateProvider = stateProvider;
            _session = session;
        }

        public UniTask<PurchaseItemResponse> InvokeAsync(PurchaseItemRequest request, CancellationToken cancellationToken = default)
        {
            return MainThreadDispatch.RunAsync(
                nameof(PurchaseItemHandler),
                $"disciple={request?.DiscipleId} item={request?.ItemDefId} grade={request?.Grade} qty={request?.Quantity}",
                () => _session != null && !_session.IsPlaying
                    ? new PurchaseItemResponse { Success = false, Message = SessionGate.NoActiveSessionReason }
                    : _stateProvider.TryPurchaseItem(
                        request.DiscipleId, request.ItemDefId, request.Grade, request.Quantity),
                reason => new PurchaseItemResponse
                {
                    Success = false,
                    Message = "internal error: " + reason,
                });
        }
    }
}
