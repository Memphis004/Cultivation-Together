using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    // Answers ChangeAvatarPartRequest coming in over the interprocess bus -
    // changing a disciple's avatar part (body, head, hair, accessory).
    // See SectStateProvider.TryChangeAvatarPart for the actual rules.
    public class ChangeAvatarPartHandler : IAsyncRequestHandler<ChangeAvatarPartRequest, ChangeAvatarPartResponse>
    {
        private readonly ISectStateProvider _stateProvider;

        public ChangeAvatarPartHandler(ISectStateProvider stateProvider)
        {
            _stateProvider = stateProvider;
        }

        public UniTask<ChangeAvatarPartResponse> InvokeAsync(ChangeAvatarPartRequest request, CancellationToken cancellationToken = default)
        {
            var success = _stateProvider.TryChangeAvatarPart(
                request.DiscipleId, request.Slot, request.PartId,
                out var failReason, out var resultAvatar);

            return UniTask.FromResult(new ChangeAvatarPartResponse
            {
                Success = success,
                FailReason = failReason,
                ResultAvatar = resultAvatar
            });
        }
    }
}
