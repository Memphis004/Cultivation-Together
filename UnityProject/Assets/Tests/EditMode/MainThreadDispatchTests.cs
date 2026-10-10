using System;
using System.Collections.Generic;
using System.Threading;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using MessagePipe;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Xianxia.Sect;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// T1 — the main-thread dispatch contract every MessagePipe.Interprocess handler
    /// now shares (MainThreadDispatch).
    ///
    /// What is proven here, and why it is the interesting part:
    ///   - a handler does NOT touch gameplay state before the main-thread hop is
    ///     pumped (so a read/write can never race the ticking main thread),
    ///   - once pumped, the body runs on the thread the hop resumed on,
    ///   - a hop that cannot be reached (shutdown) and a body that throws both come
    ///     back as the handler's OWN failure response — the bridge is never left
    ///     waiting, and nothing escapes to the TCP receive thread.
    ///
    /// The hop is replaced with a manual gate rather than a real thread, so the
    /// assertions are deterministic instead of timing-dependent.
    /// </summary>
    public class MainThreadDispatchTests
    {
        private UniTaskCompletionSource _gate;
        private int _switchCalls;

        [SetUp]
        public void SetUp()
        {
            _switchCalls = 0;
            _gate = null;
            MainThreadDispatch.ResetForTests();
            MainThreadDispatch.OverrideSwitchForTests(GatedSwitch);
        }

        [TearDown]
        public void TearDown()
        {
            MainThreadDispatch.ResetForTests();
        }

        /// <summary>Stands in for UniTask.SwitchToMainThread: incomplete until Pump() is called.</summary>
        private UniTask GatedSwitch()
        {
            _switchCalls++;
            _gate = new UniTaskCompletionSource();
            return _gate.Task;
        }

        /// <summary>Releases the hop and returns the thread the queued continuation resumes on.</summary>
        private int Pump()
        {
            Assert.IsNotNull(_gate, "the handler must have asked for the hop before it can be pumped");
            _gate.TrySetResult();
            return Thread.CurrentThread.ManagedThreadId;
        }

        // ---- contract ----

        [Test]
        public void RunAsync_DoesNotRunBody_BeforeTheHopIsPumped()
        {
            bool ran = false;

            var pending = MainThreadDispatch.RunAsync(
                "UnitTestHandler", "req=1", () => { ran = true; return 42; }, _ => -1);

            Assert.AreEqual(1, _switchCalls, "the hop must be requested");
            Assert.IsFalse(pending.Status.IsCompleted(), "the response must still be pending");
            Assert.IsFalse(ran, "the body must not run off the main thread");

            Pump();
            Assert.AreEqual(42, pending.GetAwaiter().GetResult());
            Assert.IsTrue(ran, "the body runs after the hop");
        }

        [Test]
        public void RunAsync_RunsBody_OnTheThreadThatPumpedTheHop()
        {
            int bodyThreadId = -1;

            var pending = MainThreadDispatch.RunAsync(
                "UnitTestHandler", "req=2",
                () => { bodyThreadId = Thread.CurrentThread.ManagedThreadId; return 1; },
                _ => -1);

            var pumpThreadId = Pump();
            pending.GetAwaiter().GetResult();

            Assert.AreEqual(pumpThreadId, bodyThreadId,
                            "the body must run on the thread the hop resumed on");
            Assert.AreEqual(pumpThreadId, MainThreadDispatch.LastHopThreadId);
        }

        // ---- failure contract: never leave the bridge waiting, never throw ----

        [Test]
        public void RunAsync_WhileShuttingDown_ReturnsFailureResponse_WithoutRunningBody()
        {
            bool ran = false;
            MainThreadDispatch.MarkShuttingDown();

            var pending = MainThreadDispatch.RunAsync(
                "UnitTestHandler", "req=3", () => { ran = true; return 7; }, reason => -2);

            var response = pending.GetAwaiter().GetResult();

            Assert.AreEqual(-2, response, "failure must be shaped by the handler's own response");
            Assert.IsFalse(ran, "a hop that can never be pumped must not run the body");
            Assert.AreEqual(0, _switchCalls, "an unreachable hop is detected before queueing");
        }

        [Test]
        public void RunAsync_WhileShuttingDown_AfterTheHopWasQueued_ReturnsFailureResponse()
        {
            bool ran = false;

            var pending = MainThreadDispatch.RunAsync(
                "UnitTestHandler", "req=4", () => { ran = true; return 7; }, reason => -3);

            MainThreadDispatch.MarkShuttingDown();
            Pump();

            Assert.AreEqual(-3, pending.GetAwaiter().GetResult(),
                            "shutdown after the hop was queued must still fail closed");
            Assert.IsFalse(ran);
        }

        [Test]
        public void RunAsync_BodyThrows_ReturnsFailureResponse_InsteadOfThrowing()
        {
            // The runner logs the exception (it must stay visible in the Editor console)
            // and answers with the failure response — asserted as a deliberate log, not a
            // stray one, so the failure path is covered without hiding the diagnostic.
            LogAssert.Expect(LogType.Error, new Regex("unhandled exception on the main thread"));

            var pending = MainThreadDispatch.RunAsync<int>(
                "UnitTestHandler", "req=5",
                () => throw new InvalidOperationException("boom"),
                reason => -4);

            Pump();

            Assert.AreEqual(-4, pending.GetAwaiter().GetResult());
        }

        [Test]
        public void RunAwaitingAsync_KeptOpenBody_FailsClosed_OnShutdown()
        {
            MainThreadDispatch.MarkShuttingDown();

            var pending = MainThreadDispatch.RunAwaitingAsync(
                "UnitTestHandler", "req=6",
                () => UniTask.FromResult(9),
                reason => -5);

            Assert.AreEqual(-5, pending.GetAwaiter().GetResult());
        }

        [Test]
        public void RunAwaitingAsync_KeptOpenBody_RunsOnTheThreadThatPumpedTheHop()
        {
            // The await-world-event shape: hop first, then await work that stays open.
            // A kept-open body must not start before the hop is pumped either.
            int startedThreadId = -1;
            var gate = new UniTaskCompletionSource<int>();

            var pending = MainThreadDispatch.RunAwaitingAsync(
                "UnitTestHandler", "req=7",
                async () =>
                {
                    startedThreadId = Thread.CurrentThread.ManagedThreadId;
                    return await gate.Task;
                },
                reason => -6);

            Assert.AreEqual(-1, startedThreadId, "a kept-open body must not start off the main thread");

            var pumpThreadId = Pump();
            Assert.AreEqual(pumpThreadId, startedThreadId,
                            "the kept-open body must start on the thread the hop resumed on");

            gate.TrySetResult(11);
            Assert.AreEqual(11, pending.GetAwaiter().GetResult());
        }

        [Test]
        public void AwaitWorldEventHandler_KeptOpenWait_ConsumesTheCacheOnThePumpedThread()
        {
            // The cached-event branch is the one that finishes synchronously; it must not
            // be reached before the hop, or the cache would be consumed off the main thread.
            var timeSystem = new TimeSystem(
                new BufferPublisher<TimeSpeedChangedMessage>(),
                new BufferPublisher<WorldEventTriggeredMessage>(),
                TimeRuntimeConfig.Instance);
            // requiresDecision=true is what populates the bridge handoff cache the
            // handler consumes on its first (synchronous) pass.
            timeSystem.RaiseWorldEvent("evt_cached", "cached event", true, null);

            var handler = new AwaitWorldEventHandler(timeSystem);
            var pending = handler.InvokeAsync(new AwaitWorldEventRequest { RequestId = "req-8" });

            Assert.IsFalse(pending.Status.IsCompleted(), "the handler must wait for the hop first");

            Pump();
            var response = pending.GetAwaiter().GetResult();

            Assert.AreEqual("evt_cached", response.EventId,
                            "the cached event is handed out after the hop, not before it");
        }

        [Test]
        public void ChangeAvatarPartHandler_MutatesNothing_UntilTheHopIsPumped()
        {
            // The one mutating handler with no bridge tool of its own: it still has to
            // obey the same contract as the tool-backed ones.
            var provider = BuildProvider();
            var handler = new ChangeAvatarPartHandler(provider);
            var before = FindPart(provider, "d001", AvatarSlots.Accessory);

            var pending = handler.InvokeAsync(new ChangeAvatarPartRequest
            {
                DiscipleId = "d001",
                Slot = AvatarSlots.Accessory,
                PartId = "acc_gourd",
            });

            Assert.AreEqual(before, FindPart(provider, "d001", AvatarSlots.Accessory),
                            "state must be untouched while the hop is queued");

            Pump();
            var response = pending.GetAwaiter().GetResult();

            Assert.IsTrue(response.Success, response.FailReason);
            Assert.AreEqual("acc_gourd", FindPart(provider, "d001", AvatarSlots.Accessory));
        }

        // ---- the real handlers, through the same gate ----

        [Test]
        public void AssignTaskHandler_MutatesNothing_UntilTheHopIsPumped()
        {
            var provider = BuildProvider();
            var handler = new AssignTaskHandler(provider);
            string reason;
            Assert.IsTrue(provider.TryPlaceBuilding("herb_plot", 0, 0, 0, new BuildingGrid(10, 10),
                                                    out reason, out _), reason);

            var before = FindTask(provider, "d000");

            var pending = handler.InvokeAsync(new AssignTaskRequest
            {
                RequesterId = SectStateProvider.SectMasterRequesterId,
                DiscipleId = "d000",
                TaskId = "gathering_herb",
            });

            Assert.AreEqual(before, FindTask(provider, "d000"),
                            "state must be untouched while the hop is queued");

            Pump();
            var response = pending.GetAwaiter().GetResult();

            Assert.IsTrue(response.Success, response.FailReason);
            Assert.AreEqual("gathering_herb", FindTask(provider, "d000"));
        }

        [Test]
        public void AssignTaskHandler_WhileShuttingDown_FailsClosed_AndStateIsUntouched()
        {
            var provider = BuildProvider();
            var handler = new AssignTaskHandler(provider);
            string reason;
            Assert.IsTrue(provider.TryPlaceBuilding("herb_plot", 0, 0, 0, new BuildingGrid(10, 10),
                                                    out reason, out _), reason);

            var before = FindTask(provider, "d000");

            MainThreadDispatch.MarkShuttingDown();
            var response = handler.InvokeAsync(new AssignTaskRequest
            {
                RequesterId = SectStateProvider.SectMasterRequesterId,
                DiscipleId = "d000",
                TaskId = "gathering_herb",
            }).GetAwaiter().GetResult();

            Assert.IsFalse(response.Success);
            StringAssert.Contains("shutting down", response.FailReason);
            Assert.AreEqual(before, FindTask(provider, "d000"));
        }

        [Test]
        public void PurchaseItemHandler_WhileShuttingDown_FailsClosed()
        {
            var handler = new PurchaseItemHandler(BuildProvider());

            MainThreadDispatch.MarkShuttingDown();
            var response = handler.InvokeAsync(new PurchaseItemRequest
            {
                DiscipleId = "d001",
                ItemDefId = "elixir_qi_gathering",
                Grade = 3,
                Quantity = 1,
            }).GetAwaiter().GetResult();

            Assert.IsFalse(response.Success);
            StringAssert.Contains("shutting down", response.Message);
        }

        // ---- fixtures (same wiring the provider tests use) ----

        private static SectStateProvider BuildProvider()
        {
            return new SectStateProvider(
                new BufferPublisher<DiscipleRecruitedMessage>(),
                new BufferPublisher<SectResourceChangedMessage>(),
                new BufferPublisher<AvatarEquipmentChangedMessage>(),
                new BufferPublisher<DiscipleChibiBackendChangedMessage>(),
                new AvatarPartPool(),
                Visual.VisualRuntimeConfig.Instance,
                new Visual.DefaultEntitlementProvider(),
                new BuildingDefPool(),
                new BufferPublisher<BuildingPlacedMessage>(),
                new BufferPublisher<DiscipleTaskChangedMessage>());
        }

        private static string FindTask(SectStateProvider provider, string discipleId)
        {
            foreach (var d in provider.BuildSectEconomyState().Disciples)
            {
                if (d.DiscipleId == discipleId) return d.CurrentTask ?? string.Empty;
            }
            return null;
        }

        private static string FindPart(SectStateProvider provider, string discipleId, string slot)
        {
            foreach (var d in provider.BuildSectEconomyState().Disciples)
            {
                if (d.DiscipleId == discipleId) return d.Avatar.GetSlot(slot) ?? string.Empty;
            }
            return null;
        }

        /// <summary>
        /// Minimal IPublisher&lt;T&gt; over a list — same pattern as AssignTaskTests;
        /// the real MessagePipe broker needs a full container.
        /// </summary>
        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public readonly List<T> Messages = new List<T>();
            public void Publish(T message) => Messages.Add(message);
        }
    }
}
