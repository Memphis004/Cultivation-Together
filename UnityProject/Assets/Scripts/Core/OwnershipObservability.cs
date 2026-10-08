using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect
{
    /// <summary>
    /// P4 observability — records every DiscipleOwnerChangedMessage into a small
    /// in-memory ring buffer (latest MaxEntries wins). The MCP bridge READS this
    /// log through OwnershipLogHandler (OwnershipObservabilityQuery → Snapshot,
    /// request-response like SectStateQuery — the bridge cannot
    /// IDistributedSubscriber over the TCP transport, see AwaitWorldEventRequest
    /// in GameMessages.cs).
    ///
    /// Read-only by design: this class never assigns ownership and no request/
    /// response pair or MCP tool exposes TrySetDiscipleOwner — the only writers
    /// stay the local dev harness (Editor/LocalOwnershipHarness.cs) and test
    /// fixtures. The message itself remains in-memory-only (NOT on
    /// InterprocessTopics); the bridge pulls the log on demand.
    ///
    /// IStartable: registered as an ENTRY POINT so VContainer activates it at
    /// container build — a plain Register would leave it un-instantiated until
    /// the first query, silently missing every change made before that.
    /// </summary>
    public class OwnershipObservabilityBuffer
        : IMessageHandler<DiscipleOwnerChangedMessage>, VContainer.Unity.IStartable
    {
        public const int MaxEntries = 100;

        private readonly object _lock = new object();
        private readonly List<DiscipleOwnerChangedMessage> _entries = new List<DiscipleOwnerChangedMessage>(MaxEntries);

        public OwnershipObservabilityBuffer(ISubscriber<DiscipleOwnerChangedMessage> subscriber)
        {
            // MessagePipe's real shape: IDisposable Subscribe(IMessageHandler<T>, ...).
            // Subscribing at construction (root scope, singleton) so no change is
            // missed even if the first query arrives long after boot.
            Subscription = subscriber.Subscribe(this);
        }

        /// <summary>Kept so the subscription can outlive implicit rebinds / be released explicitly later.</summary>
        public IDisposable Subscription { get; }

        // Entry-point activation only — the subscription already happened in the ctor.
        public void Start() { }

        // MessagePipe callback — fired once per real ownership change (publisher
        // side guarantees no message on no-op/rejection, see TrySetDiscipleOwner).
        public void Handle(DiscipleOwnerChangedMessage message)
        {
            lock (_lock)
            {
                _entries.Add(message);
                if (_entries.Count > MaxEntries)
                    _entries.RemoveAt(0); // drop oldest — bounded memory
            }
        }

        /// <summary>Thread-safe snapshot (query handler runs on a TCP background thread).</summary>
        public DiscipleOwnerChangedMessage[] Snapshot()
        {
            lock (_lock)
            {
                return _entries.ToArray();
            }
        }
    }

    /// <summary>
    /// Answers OwnershipObservabilityQuery coming in over the interprocess bus —
    /// the MCP bridge's read-only get_ownership_log tool. Same threading note as
    /// AssignTaskHandler: InvokeAsync runs on a TCP background thread, so the
    /// buffer read goes through the buffer's lock via Snapshot().
    /// </summary>
    public class OwnershipObservabilityHandler
        : IAsyncRequestHandler<OwnershipObservabilityQuery, OwnershipObservabilitySnapshot>
    {
        private readonly OwnershipObservabilityBuffer _buffer;

        public OwnershipObservabilityHandler(OwnershipObservabilityBuffer buffer)
        {
            _buffer = buffer;
        }

        public UniTask<OwnershipObservabilitySnapshot> InvokeAsync(
            OwnershipObservabilityQuery request, CancellationToken cancellationToken = default)
        {
            return UniTask.FromResult(new OwnershipObservabilitySnapshot
            {
                RequestId = request.RequestId,
                Events = new List<DiscipleOwnerChangedMessage>(_buffer.Snapshot()),
            });
        }
    }
}
