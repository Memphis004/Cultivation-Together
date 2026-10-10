using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    // Answers ChangeAvatarPartRequest coming in over the interprocess bus -
    // changing a disciple's avatar part (body, head, hair, accessory).
    // See SectStateProvider.TryChangeAvatarPart for the actual rules.
    //
    // THREADING (T1): InvokeAsync runs on the MessagePipe.Interprocess TCP background
    // thread and changing a part mutates live disciple state (plus publishes an
    // in-memory AvatarEquipmentChangedMessage), so it runs after a hop to the Unity
    // main thread (MainThreadDispatch.RunAsync).
    public class ChangeAvatarPartHandler : IAsyncRequestHandler<ChangeAvatarPartRequest, ChangeAvatarPartResponse>
    {
        private readonly ISectStateProvider _stateProvider;

        public ChangeAvatarPartHandler(ISectStateProvider stateProvider)
        {
            _stateProvider = stateProvider;
        }

        public UniTask<ChangeAvatarPartResponse> InvokeAsync(ChangeAvatarPartRequest request, CancellationToken cancellationToken = default)
        {
            return MainThreadDispatch.RunAsync(
                nameof(ChangeAvatarPartHandler),
                $"disciple={request?.DiscipleId} slot={request?.Slot} part={request?.PartId}",
                () =>
                {
                    var success = _stateProvider.TryChangeAvatarPart(
                        request.DiscipleId, request.Slot, request.PartId,
                        out var failReason, out var resultAvatar);

                    return new ChangeAvatarPartResponse
                    {
                        Success = success,
                        FailReason = failReason,
                        ResultAvatar = resultAvatar
                    };
                },
                reason => new ChangeAvatarPartResponse
                {
                    Success = false,
                    FailReason = "internal error: " + reason,
                    ResultAvatar = null
                });
        }
    }
}
