using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MessagePipe;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VContainer;
using Xianxia.Sect;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;
using Xianxia.Sect.UI;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// E2-lite — events never deadlock, are never lost, are never applied twice.
    /// Covers the constructor-free grace start, the single authoritative pending
    /// decision, the spawn filter, the single validated decision path, and a UI
    /// that starts after the event was raised.
    /// </summary>
    public class WorldEventLiteTests
    {
        private const string DecisionEvent = "bandit_raid_001";
        private const float Grace = 10f;

        private BufferPublisher<WorldEventTriggeredMessage> _events;
        private TimeRuntimeConfig _timeConfig;
        private WorldEventRuntimeConfig _worldConfig;
        private TimeSystem _time;

        [SetUp]
        public void SetUp()
        {
            _events = new BufferPublisher<WorldEventTriggeredMessage>();
            _timeConfig = new TimeRuntimeConfig { AutoPauseOnDecisionEvent = true };
            _worldConfig = new WorldEventRuntimeConfig { StartGraceSeconds = Grace };
            _time = new TimeSystem(new BufferPublisher<TimeSpeedChangedMessage>(), _events, _timeConfig);
        }

        // ---------------------------------------------------------- helpers

        private static List<EventChoiceInfo> Choices(params string[] ids)
        {
            var list = new List<EventChoiceInfo>();
            foreach (var id in ids) list.Add(new EventChoiceInfo { ChoiceId = id, Label = id });
            return list;
        }

        private WorldEventSystem NewSpawner() => new WorldEventSystem(_time, new LubanEventPool(), _worldConfig);

        private static SectStateProvider NewProvider()
        {
            var provider = new SectStateProvider(
                new BufferPublisher<DiscipleRecruitedMessage>(),
                new BufferPublisher<SectResourceChangedMessage>(),
                new BufferPublisher<AvatarEquipmentChangedMessage>(),
                new BufferPublisher<DiscipleChibiBackendChangedMessage>(),
                new AvatarPartPool(),
                VisualRuntimeConfig.Instance,
                new DefaultEntitlementProvider(),
                new BuildingDefPool(),
                new BufferPublisher<BuildingPlacedMessage>(),
                new BufferPublisher<DiscipleTaskChangedMessage>());
            provider.TaskChangeCooldownSeconds = 0f;
            return provider;
        }

        private static int Raw(SectStateProvider provider, string id)
        {
            int value;
            return provider.BuildSectEconomyState().Stockpile.RawResources.TryGetValue(id, out value) ? value : 0;
        }

        private DecisionExecutor NewExecutor(SectStateProvider provider) =>
            new DecisionExecutor(provider, _time, new BufferPublisher<DecisionExecutedMessage>());

        // ---------------------------------------------------------- 1. grace

        [Test]
        public void NoEventBeforeGrace_AndNoneFromConstructor()
        {
            var spawner = NewSpawner();

            Assert.AreEqual(0, _events.Messages.Count, "no event is raised from the constructor");

            spawner.Advance(9f);
            Assert.AreEqual(0, _events.Messages.Count, "nothing fires before the grace elapses");

            spawner.Advance(2f); // 11s > 10s grace
            Assert.AreEqual(1, _events.Messages.Count, "the first event fires once the grace elapses");
        }

        [Test]
        public void Grace_DoesNotAdvanceWhilePaused()
        {
            var spawner = NewSpawner();

            _time.Pause(TimePauseReason.User);
            spawner.Advance(1000f);
            Assert.AreEqual(0, _events.Messages.Count, "the grace timer must not advance while paused");

            _time.ResumeByPlayer();
            spawner.Advance(9f);
            Assert.AreEqual(0, _events.Messages.Count, "still inside the grace after being unpaused");

            spawner.Advance(2f);
            Assert.AreEqual(1, _events.Messages.Count, "the grace elapses in simulation time once unpaused");
        }

        [Test]
        public void Grace_RespectsSimulationSpeed()
        {
            var spawner = NewSpawner();

            // 3.4 real seconds at 3x = 10.2 simulation seconds -> grace elapsed.
            _time.SetSpeed(3);
            spawner.Advance(TimeSystem.ComputeSimulationDelta(false, 3, 3.4f));

            Assert.AreEqual(1, _events.Messages.Count,
                            "the grace is measured in simulation time, so 3x reaches it in a third of the real time");
        }

        // ---------------------------------------------------------- 3. spawn filter

        [Test]
        public void Spawner_SkipsDecisionEventsWhilePending_NonDecisionStillFires()
        {
            _time.RaiseWorldEvent(DecisionEvent, "Raids.", true, Choices("send_inner_disciples", "pay_tribute"));
            Assert.IsTrue(_time.HasPendingDecision);

            // Player pressed Play: not paused any more, but the event is still pending.
            _time.ResumeByPlayer();

            var spawner = NewSpawner();
            _events.Messages.Clear();

            spawner.Advance(11f); // grace elapses
            Assert.AreEqual(1, _events.Messages.Count, "a non-decision event may still fire while a decision is pending");
            Assert.IsFalse(_events.Messages[0].RequiresDecision);

            _events.Messages.Clear();
            spawner.Advance(16f); // next interval
            Assert.AreEqual(1, _events.Messages.Count);
            Assert.IsFalse(_events.Messages[0].RequiresDecision,
                           "decision events are excluded from the pick while one is already pending");
        }

        [Test]
        public void Spawner_DoesNotPileUp_AtMostOneEventPerAdvance()
        {
            _time.RaiseWorldEvent(DecisionEvent, "Raids.", true, Choices("send_inner_disciples"));
            _time.ResumeByPlayer();
            var spawner = NewSpawner();
            spawner.Advance(11f); // eat the grace

            _events.Messages.Clear();
            spawner.Advance(10000f); // a huge delta must not loop/spin
            Assert.LessOrEqual(_events.Messages.Count, 1, "the spawner never raises more than one event per interval");
        }

        [Test]
        public void EventPool_NothingEligible_ReturnsNull_AndFilterNeverPicksDecisions()
        {
            var pool = new LubanEventPool();

            Assert.IsNull(pool.GetRandomEvent(_ => false),
                          "no eligible event -> null, so the spawner skips this interval (no loop, no spin)");

            for (int i = 0; i < 50; i++)
            {
                var filtered = pool.GetRandomEvent(excludeDecisionEvents: true);
                Assert.IsNotNull(filtered);
                Assert.IsFalse(filtered.RequiresDecision, "the decision-excluding pick never returns a decision event");
            }

            bool sawDecision = false;
            for (int i = 0; i < 300 && !sawDecision; i++)
                sawDecision = pool.GetRandomEvent().RequiresDecision;
            Assert.IsTrue(sawDecision, "the unfiltered pool still offers decision events (data sanity)");
        }

        // ---------------------------------------------------------- 5. validation

        [Test]
        public void ValidDecision_AppliesOnce_DuplicateIsRejected()
        {
            var provider = NewProvider();
            var executor = NewExecutor(provider);

            _time.RaiseWorldEvent("herb_garden_bloom", "Bloom.", true, Choices("accept"));
            int before = Raw(provider, "herb");

            var first = executor.Execute("herb_garden_bloom", "accept");
            Assert.IsTrue(first.Accepted, first.Reason);
            Assert.AreEqual(before + 30, Raw(provider, "herb"), "the consequence applied exactly once");
            Assert.IsFalse(_time.HasPendingDecision, "a valid decision clears the pending state");

            var second = executor.Execute("herb_garden_bloom", "accept");
            Assert.IsFalse(second.Accepted, "a duplicate decision must be rejected");
            Assert.IsFalse(string.IsNullOrEmpty(second.Reason), "the rejection carries a reason");
            Assert.AreEqual(before + 30, Raw(provider, "herb"), "the duplicate applied nothing");
        }

        [Test]
        public void StaleEventId_AndInvalidChoice_AreRejected_WithReason_WithoutSideEffects()
        {
            var provider = NewProvider();
            var executor = NewExecutor(provider);

            _time.RaiseWorldEvent(DecisionEvent, "Raids.", true, Choices("send_inner_disciples", "pay_tribute"));
            int provisions = Raw(provider, "provisions");

            var stale = executor.Execute("wandering_merchant", "trade");
            Assert.IsFalse(stale.Accepted, "a stale event id is rejected");
            Assert.IsFalse(string.IsNullOrEmpty(stale.Reason));

            var invalid = executor.Execute(DecisionEvent, "not_a_real_choice");
            Assert.IsFalse(invalid.Accepted, "an invalid choice id is rejected");
            Assert.IsFalse(string.IsNullOrEmpty(invalid.Reason));

            Assert.IsTrue(_time.HasPendingDecision, "a rejected decision leaves the event pending");
            Assert.IsTrue(_time.IsPendingDecisionPaused, "and leaves the pause untouched");
            Assert.AreEqual(provisions, Raw(provider, "provisions"), "and changes no state");
        }

        [Test]
        public void UserPause_SurvivesAResolvedDecision()
        {
            var provider = NewProvider();
            var executor = NewExecutor(provider);

            _time.Pause(TimePauseReason.User);
            _time.RaiseWorldEvent(DecisionEvent, "Raids.", true, Choices("pay_tribute"));

            var result = executor.Execute(DecisionEvent, "pay_tribute");
            Assert.IsTrue(result.Accepted, result.Reason);

            Assert.IsFalse(_time.IsPendingDecisionPaused, "the decision pause is cleared");
            Assert.IsTrue(_time.IsUserPaused, "the player pause must survive");
            Assert.IsTrue(_time.IsPaused);
        }

        // ---------------------------------------------------------- 6. Play vs pending

        [Test]
        public void ResumeByPlayer_KeepsEventPending_AndDoesNotRepauseIt()
        {
            _time.RaiseWorldEvent(DecisionEvent, "Raids.", true, Choices("send_inner_disciples"));
            Assert.IsTrue(_time.IsPendingDecisionPaused);

            _time.ResumeByPlayer();
            Assert.IsFalse(_time.IsPaused, "Play clears the pause");
            Assert.IsTrue(_time.HasPendingDecision, "the event stays pending until it is decided");

            // Raising further events must not re-add the pause for the pending one.
            _time.RaiseWorldEvent("herb_garden_bloom", "Second.", true, Choices("accept"));
            Assert.IsFalse(_time.IsPendingDecisionPaused, "an already-pending event is not re-paused");
            Assert.IsTrue(_time.HasPendingDecision);
        }

        // ---------------------------------------------------------- 7. UI from state

        [Test]
        public void UiStartedLate_StillFindsThePendingEvent()
        {
            // The event fires BEFORE any UI exists.
            _time.RaiseWorldEvent(DecisionEvent, "Raids.", true, Choices("send_inner_disciples", "pay_tribute"));
            Assert.IsTrue(_time.HasPendingDecision);

            var provider = NewProvider();
            var uiRootGo = new GameObject("E2Test_UIRoot", typeof(RectTransform));
            var uiRoot = uiRootGo.AddComponent<UIRoot>();
            var catalog = ScriptableObject.CreateInstance<UIPanelCatalog>();
            var popupPrefab = new GameObject("E2Test_EventPopupPrefab", typeof(RectTransform));
            popupPrefab.AddComponent<EventPopupView>();
            popupPrefab.SetActive(false);

            // catalog.panels is serialized-private - wire it the project's way.
            var catalogSo = new SerializedObject(catalog);
            var panels = catalogSo.FindProperty("panels");
            panels.arraySize = 1;
            var element = panels.GetArrayElementAtIndex(0);
            element.FindPropertyRelative("PanelId").stringValue = "EventPopup";
            element.FindPropertyRelative("Prefab").objectReferenceValue = popupPrefab;
            element.FindPropertyRelative("PresenterKind").enumValueIndex = (int)UIPresenterKind.EventPopup;
            catalogSo.ApplyModifiedPropertiesWithoutUndo();

            var builder = new ContainerBuilder();
            builder.RegisterMessagePipe();
            builder.RegisterInstance(_time);
            builder.RegisterInstance<ISectStateProvider>(provider);
            builder.Register<DecisionExecutor>(Lifetime.Singleton);
            builder.Register<EventPopupPresenter>(Lifetime.Transient);
            builder.RegisterInstance(uiRoot);
            builder.RegisterInstance(catalog);
            builder.Register<UIService>(Lifetime.Singleton);
            var container = builder.Build();

            WorldEventUISystem uiSystem = null;
            try
            {
                var uiService = container.Resolve<UIService>();
                var subscriber = container.Resolve<ISubscriber<WorldEventTriggeredMessage>>();
                uiSystem = new WorldEventUISystem(subscriber, uiService, _time);

                uiSystem.Start(); // starts LATE - must derive from authoritative state

                var popup = FindChild<EventPopupView>(uiRoot.transform);
                Assert.IsNotNull(popup, "a late-started UI still opens the popup for the pending event");
                Assert.IsTrue(popup.isActiveAndEnabled);
            }
            finally
            {
                uiSystem?.Dispose();
                container.Dispose();
                Object.DestroyImmediate(uiRootGo);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(popupPrefab);
            }
        }

        private static T FindChild<T>(Transform parent) where T : Component
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var component = parent.GetChild(i).GetComponent<T>();
                if (component != null) return component;
            }
            return null;
        }

        // ---------------------------------------------------------- cache + config

        [Test]
        public void AwaitWorldEventCache_BehaviourIsUnchanged()
        {
            _time.RaiseWorldEvent(DecisionEvent, "Raids.", true, Choices("send_inner_disciples"));

            var first = _time.WaitForNextWorldEventAsync();
            Assert.AreEqual(UniTaskStatus.Succeeded, first.Status, "a pending decision is handed to the bridge immediately");
            Assert.AreEqual(DecisionEvent, first.GetAwaiter().GetResult().EventId);
            Assert.IsTrue(_time.HasPendingDecision,
                          "reading the bridge cache must NOT clear the authoritative pending state");

            var second = _time.WaitForNextWorldEventAsync();
            Assert.AreEqual(UniTaskStatus.Pending, second.Status, "the cached event is handed out only once");

            // A later (non-decision) event completes the outstanding waiter, as before.
            _time.RaiseWorldEvent("herb_garden_bloom", "Bloom.", false, new List<EventChoiceInfo>());
            Assert.AreEqual(UniTaskStatus.Succeeded, second.Status);
            Assert.AreEqual("herb_garden_bloom", second.GetAwaiter().GetResult().EventId);
        }

        [Test]
        public void AutoPauseDisabled_PendingStateIsSet_ButGameKeepsRunning()
        {
            var time = new TimeSystem(
                new BufferPublisher<TimeSpeedChangedMessage>(),
                new BufferPublisher<WorldEventTriggeredMessage>(),
                new TimeRuntimeConfig { AutoPauseOnDecisionEvent = false });

            time.RaiseWorldEvent(DecisionEvent, "Raids.", true, Choices("send_inner_disciples"));

            Assert.IsTrue(time.HasPendingDecision, "the pending state is still authoritative");
            Assert.IsFalse(time.IsPendingDecisionPaused);
            Assert.IsFalse(time.IsPaused, "with auto-pause off the game keeps running");
        }

        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public readonly List<T> Messages = new List<T>();
            public void Publish(T message) => Messages.Add(message);
        }
    }
}
