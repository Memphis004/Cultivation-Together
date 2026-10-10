using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    /// <summary>
    /// P5B observability — records every DiscipleTaskChangedMessage into a small
    /// in-memory ring buffer (latest MaxEntries wins). The MCP bridge READS this
    /// log through TaskChangeObservabilityHandler (request-response, same
    /// mechanism as OwnershipObservabilityQuery — the bridge cannot
    /// IDistributedSubscriber over the TCP transport).
    ///
    /// Read-only by design: this class never assigns a task. Task assignment
    /// already has its own AssignTask pair; this is observation only.
    ///
    /// IStartable: registered as an ENTRY POINT so VContainer activates it at
    /// container build — a plain Register would leave it un-instantiated until
    /// the first query, silently missing every change made before that
    /// (same bug class as the P4 ownership buffer).
    /// </summary>
    public class TaskChangeObservabilityBuffer
        : IMessageHandler<DiscipleTaskChangedMessage>, VContainer.Unity.IStartable
    {
        public const int MaxEntries = 100;

        private readonly object _lock = new object();
        private readonly List<DiscipleTaskChangedMessage> _entries = new List<DiscipleTaskChangedMessage>(MaxEntries);

        public TaskChangeObservabilityBuffer(ISubscriber<DiscipleTaskChangedMessage> subscriber)
        {
            // Subscribing at construction (root scope, singleton) so no change is
            // missed even if the first query arrives long after boot.
            Subscription = subscriber.Subscribe(this);
        }

        /// <summary>Kept so the subscription can outlive implicit rebinds / be released explicitly later.</summary>
        public IDisposable Subscription { get; }

        // Entry-point activation only — the subscription already happened in the ctor.
        public void Start() { }

        // MessagePipe callback — fired once per REAL task change. The publisher
        // side guarantees no message on a no-op/rejection (see TryAssignTask).
        public void Handle(DiscipleTaskChangedMessage message)
        {
            if (message == null) return;
            lock (_lock)
            {
                _entries.Add(message);
                if (_entries.Count > MaxEntries)
                    _entries.RemoveAt(0); // drop oldest — bounded memory
            }
        }

        /// <summary>
        /// Snapshot of the log. The lock is kept even though the query handler now hops to the
        /// main thread (T1): the writer is an in-memory MessagePipe subscriber, so the buffer
        /// never assumes which thread its callers are on.
        /// </summary>
        public DiscipleTaskChangedMessage[] Snapshot()
        {
            lock (_lock)
            {
                return _entries.ToArray();
            }
        }
    }

    /// <summary>
    /// Answers TaskChangeObservabilityQuery — the bridge's read-only get_task_change_log tool.
    /// Read-only, but T1 still applies the uniform rule: the buffer read happens after a hop to
    /// the Unity main thread (MainThreadDispatch.RunAsync).
    /// </summary>
    public class TaskChangeObservabilityHandler
        : IAsyncRequestHandler<TaskChangeObservabilityQuery, TaskChangeObservabilitySnapshot>
    {
        private readonly TaskChangeObservabilityBuffer _buffer;

        public TaskChangeObservabilityHandler(TaskChangeObservabilityBuffer buffer)
        {
            _buffer = buffer;
        }

        public UniTask<TaskChangeObservabilitySnapshot> InvokeAsync(
            TaskChangeObservabilityQuery request, CancellationToken cancellationToken = default)
        {
            return MainThreadDispatch.RunAsync(
                nameof(TaskChangeObservabilityHandler),
                $"requestId={request?.RequestId}",
                () => new TaskChangeObservabilitySnapshot
                {
                    RequestId = request.RequestId,
                    Events = new List<DiscipleTaskChangedMessage>(_buffer.Snapshot()),
                },
                reason => new TaskChangeObservabilitySnapshot
                {
                    // Read-only contract has no failure field: no events + the logged reason.
                    RequestId = request?.RequestId,
                    Events = new List<DiscipleTaskChangedMessage>(),
                });
        }
    }

    /// <summary>
    /// Answers TaskProtectionQuery — the bridge's read-only get_task_protection tool.
    /// One entry per Viewer-owned disciple (the set the hybrid protection policy
    /// governs), computed through ISectStateProvider.CheckTaskPermission so the
    /// reported protection/permission always matches what TryAssignTask would do.
    /// Never mutates state; no clock is needed here because the authority reads it. T1: it reads
    /// LIVE state (BuildSectEconomyState over every disciple), so the read happens after a hop to
    /// the Unity main thread (MainThreadDispatch.RunAsync).
    /// </summary>
    public class TaskProtectionHandler
        : IAsyncRequestHandler<TaskProtectionQuery, TaskProtectionSnapshot>
    {
        private readonly ISectStateProvider _stateProvider;

        public TaskProtectionHandler(ISectStateProvider stateProvider)
        {
            _stateProvider = stateProvider;
        }

        public UniTask<TaskProtectionSnapshot> InvokeAsync(
            TaskProtectionQuery request, CancellationToken cancellationToken = default)
        {
            return MainThreadDispatch.RunAsync(
                nameof(TaskProtectionHandler),
                $"requestId={request?.RequestId}",
                () => BuildSnapshot(request),
                reason => new TaskProtectionSnapshot
                {
                    // Read-only contract has no failure field: no entries + the logged reason.
                    RequestId = request?.RequestId,
                });
        }

        /// <summary>
        /// The existing aggregation, unchanged — now entered on the Unity main thread (T1) because
        /// it reads live state through the authority.
        /// </summary>
        private TaskProtectionSnapshot BuildSnapshot(TaskProtectionQuery request)
        {
            var snapshot = new TaskProtectionSnapshot { RequestId = request?.RequestId };

            var state = _stateProvider.BuildSectEconomyState();
            if (state?.Disciples == null) return snapshot;

            for (int i = 0; i < state.Disciples.Count; i++)
            {
                var d = state.Disciples[i];
                if (d == null || d.OwnerType != DiscipleOwnerType.Viewer) continue;

                // Recomputed here, never inferred: the authority answers both.
                var asMaster = _stateProvider.CheckTaskPermission(
                    SectStateProvider.SectMasterRequesterId, d.DiscipleId, d.CurrentTask);
                var asOwner = _stateProvider.CheckTaskPermission(d.OwnerId, d.DiscipleId, d.CurrentTask);

                var record = state.ViewerRegistry?.Find(d.OwnerId);

                snapshot.Entries.Add(new TaskProtectionEntry
                {
                    DiscipleId = d.DiscipleId,
                    CurrentTask = d.CurrentTask ?? string.Empty,
                    OwnerType = d.OwnerType,
                    OwnerId = d.OwnerId ?? string.Empty,
                    OwnerProtected = asMaster.OwnerProtected,
                    OwnerProtectionRemainingSeconds = asMaster.OwnerProtectionRemainingSeconds,
                    LastActiveAtUtc = record != null ? record.LastActiveAtUtc : DateTime.MinValue,
                    MembershipConsistent = asMaster.Outcome != TaskPermissionOutcome.ConsistencyError,
                    SectMasterMayChange = asMaster.Allowed,
                    SectMasterReason = asMaster.Reason ?? string.Empty,
                    OwnerMayChange = asOwner.Allowed,
                    OwnerReason = asOwner.Reason ?? string.Empty,
                });
            }

            return snapshot;
        }
    }
}
