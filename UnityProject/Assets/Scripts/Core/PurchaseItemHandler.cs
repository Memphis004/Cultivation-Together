using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    // Answers PurchaseItemRequest coming in over the interprocess bus - a
    // disciple buying an item from the sect stockpile with contribution.
    // See SectStateProvider.TryPurchaseItem for the actual rules.
    public class PurchaseItemHandler : IAsyncRequestHandler<PurchaseItemRequest, PurchaseItemResponse>
    {
        private readonly ISectStateProvider _stateProvider;

        public PurchaseItemHandler(ISectStateProvider stateProvider)
        {
            _stateProvider = stateProvider;
        }

        public UniTask<PurchaseItemResponse> InvokeAsync(PurchaseItemRequest request, CancellationToken cancellationToken = default)
        {
            var response = _stateProvider.TryPurchaseItem(
                request.DiscipleId, request.ItemDefId, request.Grade, request.Quantity);

            return UniTask.FromResult(response);
        }
    }
}
