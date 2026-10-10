using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    // Answers AssignTaskRequest coming in over the interprocess bus — the AI GM
    // (via the MCP bridge's assign_task tool) assigning a task to a disciple.
    // See SectStateProvider.TryAssignTask for the permission + validity rules
    // (disciple lookup → ownership → known task set).
    //
    // THREADING (T1): InvokeAsync runs on a MessagePipe.Interprocess TCP background
    // thread, NOT the Unity main thread. TryAssignTask mutates live gameplay state
    // (and publishes an in-memory DiscipleTaskChangedMessage other systems react to),
    // so the whole call now runs AFTER a hop to the Unity main thread — see
    // MainThreadDispatch.RunAsync, the one shared place that owns the hop and the
    // failure contract. The earlier note here ("plain managed state only, no Unity
    // API, therefore safe") was wrong: an off-thread read/mutate of live state while
    // the main thread ticks is a race regardless of which API it touches.
    public class AssignTaskHandler : IAsyncRequestHandler<AssignTaskRequest, AssignTaskResponse>
    {
        private readonly ISectStateProvider _stateProvider;

        public AssignTaskHandler(ISectStateProvider stateProvider)
        {
            _stateProvider = stateProvider;
        }

        public UniTask<AssignTaskResponse> InvokeAsync(AssignTaskRequest request, CancellationToken cancellationToken = default)
        {
            return MainThreadDispatch.RunAsync(
                nameof(AssignTaskHandler),
                $"requester={request?.RequesterId} disciple={request?.DiscipleId} task={request?.TaskId}",
                () =>
                {
                    string failReason;
                    var success = _stateProvider.TryAssignTask(
                        request.RequesterId, request.DiscipleId, request.TaskId, out failReason);

                    return new AssignTaskResponse
                    {
                        Success = success,
                        FailReason = failReason,
                    };
                },
                reason => new AssignTaskResponse
                {
                    Success = false,
                    FailReason = "internal error: " + reason,
                });
        }
    }
}
