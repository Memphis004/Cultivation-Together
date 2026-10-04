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
    // THREADING: InvokeAsync runs on a MessagePipe.Interprocess TCP background
    // thread, NOT the Unity main thread. The code called synchronously here
    // touches only plain managed state (DiscipleState strings, the known-task
    // HashSet, an in-memory MessagePipe publish) plus Debug.Log — which, like
    // the existing TryPurchaseItem path served by PurchaseItemHandler, is
    // thread-safe and calls no scene/object API. There must be NO
    // Instantiate/GameObject/transform access on this call path. If a future
    // downstream step needs a Unity object, it has to cross back first with
    // `await UniTask.SwitchToMainThread();` (see DecisionExecutor.cs for the
    // pattern) before touching it.
    public class AssignTaskHandler : IAsyncRequestHandler<AssignTaskRequest, AssignTaskResponse>
    {
        private readonly ISectStateProvider _stateProvider;

        public AssignTaskHandler(ISectStateProvider stateProvider)
        {
            _stateProvider = stateProvider;
        }

        public UniTask<AssignTaskResponse> InvokeAsync(AssignTaskRequest request, CancellationToken cancellationToken = default)
        {
            string failReason;
            var success = _stateProvider.TryAssignTask(
                request.RequesterId, request.DiscipleId, request.TaskId, out failReason);

            return UniTask.FromResult(new AssignTaskResponse
            {
                Success = success,
                FailReason = failReason,
            });
        }
    }
}
