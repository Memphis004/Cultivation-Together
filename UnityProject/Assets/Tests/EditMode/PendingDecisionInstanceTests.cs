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
    /// P13A — a decision targets a specific pending EVENT INSTANCE, at most once.
    ///
    /// Covers: a unique instance id per occurrence (definition id ≠ occurrence id),
    /// bounded pending storage with a defined order (FIFO, oldest = current) and overflow
    /// rule (refuse loudly, never replace), simultaneous decisions on different instances,
    /// duplicate / replayed / stale / ambiguous / invalid decisions, session changes, and
    /// pause ownership surviving a resolved decision.
    /// </summary>
    public class PendingDecisionInstanceTests
    {
        private const string Raid = "bandit_raid_001";
        private const string Merchant = "wandering_merchant";
        private const string Bloom = "herb_garden_bloom";
        private const string CatalogPath = "Assets/Panel Catalog/MainPanelCatalog.asset";

        private BufferPublisher<WorldEventTriggeredMessage> _events;
        private TimeSystem _time;
        private SectStateProvider _provider;
        private DecisionExecutor _executor;
        private readonly List<GameObject> _tempRoots = new List<GameObject>();
        private readonly List<System.Action> _tempCleanup = new List<System.Action>();

        [SetUp]
        public void SetUp()
        {
            _events = new BufferPublisher<WorldEventTriggeredMessage>();
            _time = new TimeSystem(
                new BufferPublisher<TimeSpeedChangedMessage>(),
                _events,
                new TimeRuntimeConfig { AutoPauseOnDecisionEvent = true });
            _provider = NewProvider();
            _executor = new DecisionExecutor(_provider, _time, new BufferPublisher<DecisionExecutedMessage>());
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _tempCleanup.Count - 1; i >= 0; i--) _tempCleanup[i]();
            _tempCleanup.Clear();

            for (int i = _tempRoots.Count - 1; i >= 0; i--)
            {
                if (_tempRoots[i] != null) UnityEngine.Object.DestroyImmediate(_tempRoots[i]);
            }
            _tempRoots.Clear();
        }

        // ---------------------------------------------------------------- helpers

        private static List<EventChoiceInfo> Choices(params string[] ids)
        {
            var list = new List<EventChoiceInfo>();
            foreach (var id in ids) list.Add(new EventChoiceInfo { ChoiceId = id, Label = id });
            return list;
        }

        /// <summary>
        /// Raises a decision-requiring event and returns the INSTANCE id it was stored under,
        /// taken from the trigger message (also proving the message carries it).
        /// </summary>
        private string RaisePending(string eventId, params string[] choiceIds)
        {
            _events.Messages.Clear();
            Assert.IsTrue(_time.RaiseWorldEvent(eventId, eventId, true, Choices(choiceIds)),
                          "the decision event was stored");
            Assert.AreEqual(1, _events.Messages.Count, "one trigger message per raise");
            Assert.IsFalse(string.IsNullOrEmpty(_events.Messages[0].InstanceId),
                           "the trigger message carries the instance id");
            return _events.Messages[0].InstanceId;
        }

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

        private int Raw(string resourceId)
        {
            int value;
            return _provider.BuildSectEconomyState().Stockpile.RawResources.TryGetValue(resourceId, out value)
                ? value
                : 0;
        }

        // ------------------------------------------------- 1. definition vs instance

        [Test]
        public void EachRaise_GetsItsOwnInstanceId_AndTheOldestIsCurrent()
        {
            var first = RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            var second = RaisePending(Raid, "send_inner_disciples", "pay_tribute");

            Assert.AreNotEqual(first, second, "two occurrences of ONE definition must not share an id");
            Assert.AreEqual(2, _time.PendingDecisionCount);
            Assert.AreEqual(Raid, _time.PendingEventId);

            // Ordering is defined: the CURRENT event is the OLDEST undecided one (FIFO).
            Assert.AreEqual(first, _time.PendingInstanceId, "the oldest undecided event is the current one");

            Assert.IsTrue(_executor.Execute(first, Raid, "pay_tribute").Accepted);
            Assert.AreEqual(second, _time.PendingInstanceId, "resolving the head promotes the next arrival, in order");
            Assert.AreEqual(1, _time.PendingDecisionCount);
        }

        // ------------------------------------------------- 2. simultaneous decisions

        [Test]
        public void Decision_OnOneInstance_LeavesTheOtherPending_AndHoldsTheEventPause()
        {
            var raid = RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            var merchant = RaisePending(Merchant, "trade", "decline");
            Assert.IsTrue(_time.IsPendingDecisionPaused, "the first outstanding decision takes the event pause");

            int provisions = Raw("provisions");
            int ore = Raw("ore");

            var traded = _executor.Execute(merchant, Merchant, "trade");
            Assert.IsTrue(traded.Accepted, traded.Reason);
            Assert.AreEqual(provisions + 40, Raw("provisions"));
            Assert.AreEqual(ore - 20, Raw("ore"), "the trade's cost applied exactly once");

            Assert.AreEqual(1, _time.PendingDecisionCount, "the other outstanding decision is untouched");
            Assert.AreEqual(raid, _time.PendingInstanceId, "and is still the current one");
            Assert.IsTrue(_time.IsPendingDecisionPaused,
                          "while a decision is still outstanding the event pause is still its pause");

            var repelled = _executor.Execute(raid, Raid, "send_inner_disciples");
            Assert.IsTrue(repelled.Accepted, repelled.Reason);
            Assert.AreEqual(provisions + 40 - 20, Raw("provisions"), "both consequences applied, once each");
            Assert.AreEqual(0, _time.PendingDecisionCount);
            Assert.IsFalse(_time.IsPendingDecisionPaused, "the LAST decision releases the event pause");
        }

        [Test]
        public void TwoInstancesOfOneDefinition_EachAppliedExactlyOnce()
        {
            var a = RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            var b = RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            int before = Raw("provisions");

            Assert.IsTrue(_executor.Execute(b, Raid, "pay_tribute").Accepted);
            Assert.AreEqual(before - 20, Raw("provisions"));

            Assert.IsTrue(_executor.Execute(a, Raid, "send_inner_disciples").Accepted);
            Assert.AreEqual(before - 40, Raw("provisions"), "each instance applied its consequence once");

            Assert.IsFalse(_executor.Execute(a, Raid, "send_inner_disciples").Accepted);
            Assert.IsFalse(_executor.Execute(b, Raid, "pay_tribute").Accepted);
            Assert.AreEqual(before - 40, Raw("provisions"), "and neither can be applied a second time");
        }

        // ------------------------------------------------- 3. duplicate / replay / stale

        [Test]
        public void DuplicateDecision_OnTheResolvedInstance_IsRejected_WithoutConsequence()
        {
            var raid = RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            int before = Raw("provisions");

            Assert.IsTrue(_executor.Execute(raid, Raid, "pay_tribute").Accepted);
            Assert.AreEqual(before - 20, Raw("provisions"));

            var replay = _executor.Execute(raid, Raid, "pay_tribute");
            Assert.IsFalse(replay.Accepted, "the same instance cannot be decided twice");
            StringAssert.Contains(raid, replay.Reason, "the rejection names the instance: " + replay.Reason);
            Assert.AreEqual(before - 20, Raw("provisions"), "a replay changes nothing");
        }

        [Test]
        public void StaleInstanceId_AfterTheQueueAdvanced_IsRejected()
        {
            var first = RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            Assert.IsTrue(_executor.Execute(first, Raid, "pay_tribute").Accepted);

            var second = RaisePending(Merchant, "trade", "decline");
            int provisions = Raw("provisions");
            int ore = Raw("ore");

            var stale = _executor.Execute(first, Raid, "pay_tribute");
            Assert.IsFalse(stale.Accepted, "a decision addressed to a finished occurrence is stale");
            Assert.AreEqual(provisions, Raw("provisions"), "and applies nothing");
            Assert.AreEqual(ore, Raw("ore"));
            Assert.AreEqual(second, _time.PendingInstanceId, "the live event is untouched");
            Assert.AreEqual(1, _time.PendingDecisionCount);
        }

        [Test]
        public void Decision_NamingTheWrongDefinitionForAnInstance_IsRejected()
        {
            var raid = RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            int provisions = Raw("provisions");

            var mismatched = _executor.Execute(raid, Merchant, "trade");

            Assert.IsFalse(mismatched.Accepted, "the instance id and the definition id must agree");
            StringAssert.Contains(Merchant, mismatched.Reason);
            Assert.AreEqual(1, _time.PendingDecisionCount, "nothing was consumed");
            Assert.AreEqual(provisions, Raw("provisions"));
        }

        // ------------------------------------------------- 4. invalid choices

        [Test]
        public void ChoiceNotOfferedByTheTargetInstance_IsRejected()
        {
            var raid = RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            var merchant = RaisePending(Merchant, "trade", "decline");
            int provisions = Raw("provisions");
            int ore = Raw("ore");

            // A real choice id — but of a DIFFERENT pending event.
            var crossInstance = _executor.Execute(merchant, Merchant, "pay_tribute");

            Assert.IsFalse(crossInstance.Accepted, "a choice this instance never offered is invalid");
            StringAssert.Contains("pay_tribute", crossInstance.Reason);
            Assert.AreEqual(2, _time.PendingDecisionCount, "a rejection consumes nothing");
            Assert.AreEqual(provisions, Raw("provisions"));
            Assert.AreEqual(ore, Raw("ore"));
            Assert.AreEqual(raid, _time.PendingInstanceId, "and the queue order is unchanged");
        }

        [Test]
        public void UnknownChoice_IsRejected_WithReason_WithoutSideEffects()
        {
            var raid = RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            int provisions = Raw("provisions");

            var invalid = _executor.Execute(raid, Raid, "not_a_real_choice");

            Assert.IsFalse(invalid.Accepted);
            StringAssert.Contains("not_a_real_choice", invalid.Reason);
            Assert.AreEqual(provisions, Raw("provisions"));
            Assert.AreEqual(1, _time.PendingDecisionCount);
            Assert.IsTrue(_time.IsPendingDecisionPaused, "a rejection leaves the pause untouched");
        }

        // ------------------------------------------------- 5. compatibility (definition only)

        [Test]
        public void LegacyEventIdOnlyDecision_IsAcceptedWhenUnambiguous_AndRefusedWhenAmbiguous()
        {
            RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            int before = Raw("provisions");

            // The pre-P13A contract: no instance id, only the event id.
            var legacy = _executor.Execute(Raid, "pay_tribute");

            Assert.IsTrue(legacy.Accepted, "one pending instance makes the definition id unambiguous: " + legacy.Reason);
            Assert.AreEqual(before - 20, Raw("provisions"));
            Assert.AreEqual(0, _time.PendingDecisionCount);

            RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            int now = Raw("provisions");

            var ambiguous = _executor.Execute(Raid, "pay_tribute");

            Assert.IsFalse(ambiguous.Accepted, "two occurrences share the definition id, so it is ambiguous");
            StringAssert.Contains("instance id", ambiguous.Reason);
            Assert.AreEqual(now, Raw("provisions"), "an ambiguous decision is refused, never guessed at");
            Assert.AreEqual(2, _time.PendingDecisionCount);
        }

        // ------------------------------------------------- 6. bounded storage

        [Test]
        public void PendingStorage_IsBounded_AndOverflowNeverReplacesAnOutstandingDecision()
        {
            for (int i = 0; i < TimeSystem.MaxPendingDecisions; i++)
                RaisePending(Raid, "send_inner_disciples", "pay_tribute");

            Assert.AreEqual(TimeSystem.MaxPendingDecisions, _time.PendingDecisionCount, "the pending storage is full");
            string head = _time.PendingInstanceId;
            int before = Raw("provisions");

            // One more decision event: the cap refuses it outright.
            _events.Messages.Clear();
            Assert.IsFalse(_time.RaiseWorldEvent(Raid, "Raids.", true, Choices("send_inner_disciples")),
                           "a raise that cannot be stored reports failure");
            Assert.AreEqual(0, _events.Messages.Count, "a refused event is never announced as pending");

            Assert.AreEqual(TimeSystem.MaxPendingDecisions, _time.PendingDecisionCount, "nothing was added");
            Assert.AreEqual(head, _time.PendingInstanceId, "the oldest outstanding decision was NOT replaced");
            Assert.IsTrue(_time.IsPendingDecisionPaused);

            // The oldest decision is still fully answerable — nothing about it was lost.
            Assert.IsTrue(_executor.Execute(head, Raid, "send_inner_disciples").Accepted);
            Assert.AreEqual(before - 20, Raw("provisions"));
            Assert.AreEqual(TimeSystem.MaxPendingDecisions - 1, _time.PendingDecisionCount);
        }

        // ------------------------------------------------- 7. explicit choice ids

        [Test]
        public void ExplicitChoiceIds_DriveTheConsequence_NotTheEventIdAlone()
        {
            var decline = RaisePending(Merchant, "trade", "decline");
            int provisions = Raw("provisions");
            int ore = Raw("ore");

            Assert.IsTrue(_executor.Execute(decline, Merchant, "decline").Accepted);
            Assert.AreEqual(provisions, Raw("provisions"), "declining the trade exchanges nothing");
            Assert.AreEqual(ore, Raw("ore"));

            var trade = RaisePending(Merchant, "trade", "decline");
            Assert.IsTrue(_executor.Execute(trade, Merchant, "trade").Accepted);
            Assert.AreEqual(provisions + 40, Raw("provisions"));
            Assert.AreEqual(ore - 20, Raw("ore"));
        }

        // ------------------------------------------------- 8. session changes

        [Test]
        public void SessionInactive_RefusesTheDecision_WithoutConsumingTheInstance()
        {
            var raid = RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            _time.SetSessionActive(false);

            int provisions = Raw("provisions");
            var result = _executor.Execute(raid, Raid, "pay_tribute");

            Assert.IsFalse(result.Accepted, "outside a Playing session nothing may be decided");
            StringAssert.Contains("no active session", result.Reason);
            Assert.AreEqual(provisions, Raw("provisions"));
            Assert.AreEqual(1, _time.PendingDecisionCount, "a refused decision must not consume the pending instance");
            Assert.AreEqual(raid, _time.PendingInstanceId);
        }

        [Test]
        public void EndSession_ClearsEveryPendingDecision_AndCompletesTheAwaiter()
        {
            RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            RaisePending(Merchant, "trade", "decline");

            // The bridge's handoff cache answers a late waiter first; only then is the wait parked.
            Assert.AreEqual(UniTaskStatus.Succeeded, _time.WaitForNextWorldEventAsync().Status);
            var parked = _time.WaitForNextWorldEventAsync();
            Assert.AreEqual(UniTaskStatus.Pending, parked.Status);

            _time.EndSession("returned to title");

            Assert.AreEqual(0, _time.PendingDecisionCount, "the session boundary clears EVERY pending decision");
            Assert.IsFalse(_time.HasPendingDecision);
            Assert.IsFalse(_time.IsPendingDecisionPaused);
            Assert.AreEqual(UniTaskStatus.Succeeded, parked.Status, "an outstanding awaiter is completed, never stranded");
            Assert.AreEqual(string.Empty, parked.GetAwaiter().GetResult().EventId,
                            "with the response contract's clearest \"no event\" answer");

            Assert.IsFalse(_executor.Execute(Raid, "pay_tribute").Accepted,
                           "and a decision arriving after the boundary is refused");
        }

        // ------------------------------------------------- 9. pause ownership

        [Test]
        public void UserPause_SurvivesResolvingEveryDecision()
        {
            _time.Pause(TimePauseReason.User);
            var a = RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            var b = RaisePending(Merchant, "trade", "decline");

            Assert.IsTrue(_executor.Execute(a, Raid, "pay_tribute").Accepted);
            Assert.IsTrue(_time.IsUserPaused, "the player pause is never touched by a decision");
            Assert.IsTrue(_time.IsPendingDecisionPaused, "and the event pause is still needed by the remaining one");

            Assert.IsTrue(_executor.Execute(b, Merchant, "decline").Accepted);
            Assert.IsFalse(_time.IsPendingDecisionPaused, "the event pause is released");
            Assert.IsTrue(_time.IsUserPaused, "the player pause survives");
            Assert.IsTrue(_time.IsPaused);
        }

        [Test]
        public void SessionTransitionPause_SurvivesAResolvedDecision_AndASessionBoundary()
        {
            _time.Pause(TimePauseReason.SessionTransition);
            var raid = RaisePending(Raid, "send_inner_disciples", "pay_tribute");

            Assert.IsTrue(_executor.Execute(raid, Raid, "pay_tribute").Accepted);

            Assert.IsFalse(_time.IsPendingDecisionPaused);
            Assert.IsFalse(_time.IsUserPaused);
            Assert.IsTrue(_time.IsPaused,
                          "a session-transition pause is not the event pause, so resolving the event cannot clear it");

            // The session boundary clears the player pause (a fresh session is unpaused) but
            // must not resume a transition that is still in flight.
            _time.Pause(TimePauseReason.User);
            RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            _time.EndSession("a new game started");

            Assert.IsFalse(_time.IsUserPaused, "the boundary owns the player pause");
            Assert.IsFalse(_time.IsPendingDecisionPaused, "and the decision pause");
            Assert.IsTrue(_time.IsPaused, "but the transition's pause is preserved");
        }

        // ------------------------------------------------- 10. restore

        [Test]
        public void Restore_MintsAFreshInstanceId_SoAPreSaveInstanceIdIsStale()
        {
            var original = RaisePending(Raid, "send_inner_disciples", "pay_tribute");

            _time.RestoreSimulationState(1, Raid, "Raiders.", Choices("send_inner_disciples", "pay_tribute"), true);

            Assert.AreEqual(1, _time.PendingDecisionCount);
            string restored = _time.PendingInstanceId;
            Assert.AreNotEqual(original, restored, "an instance id names a live occurrence, so a restore re-mints one");
            Assert.IsTrue(_time.IsPendingDecisionPaused, "the snapshot's pause flag is authoritative");

            int before = Raw("provisions");
            Assert.IsFalse(_executor.Execute(original, Raid, "pay_tribute").Accepted,
                           "a decision addressed to a pre-save instance is stale after a load");
            Assert.AreEqual(before, Raw("provisions"));

            Assert.IsTrue(_executor.Execute(restored, Raid, "pay_tribute").Accepted,
                          "while the restored event itself stays answerable");
            Assert.AreEqual(before - 20, Raw("provisions"));
        }

        [Test]
        public void Restore_ReplacesThePendingState_AndItsPauseFlagWins()
        {
            RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            RaisePending(Merchant, "trade", "decline");
            Assert.IsTrue(_time.IsPendingDecisionPaused);

            _time.RestoreSimulationState(1, Bloom, "Bloom.", Choices(), false);

            Assert.AreEqual(1, _time.PendingDecisionCount, "a restore replaces the pending state outright");
            Assert.AreEqual(Bloom, _time.PendingEventId);
            Assert.IsFalse(_time.IsPendingDecisionPaused,
                           "a snapshot that says \"not paused\" must not leave the clock frozen on a stale pause");
        }

        // ------------------------------------------------- 11. the popup follows the head

        [Test]
        public void Popup_ReTargetsTheCurrentInstance_WhenTheHeadIsResolvedElsewhere()
        {
            var uiRootGo = new GameObject("P13A_UIRoot", typeof(RectTransform));
            _tempRoots.Add(uiRootGo);
            var uiRoot = uiRootGo.AddComponent<UIRoot>();
            var catalog = AssetDatabase.LoadAssetAtPath<UIPanelCatalog>(CatalogPath);
            Assert.IsNotNull(catalog, "the generated MainPanelCatalog must exist");

            var builder = new ContainerBuilder();
            builder.RegisterMessagePipe();
            builder.RegisterInstance(_time);
            builder.RegisterInstance<ISectStateProvider>(_provider);
            builder.Register<DecisionExecutor>(Lifetime.Singleton);
            builder.Register<EventPopupPresenter>(Lifetime.Transient);
            builder.Register<WorldEventChipPresenter>(Lifetime.Transient);
            builder.RegisterInstance(uiRoot);
            builder.RegisterInstance(catalog);
            builder.Register<UIService>(Lifetime.Singleton);
            var container = builder.Build();
            _tempCleanup.Add(container.Dispose);

            var triggerPublisher = container.Resolve<IPublisher<WorldEventTriggeredMessage>>();
            var uiSystem = new WorldEventUISystem(
                container.Resolve<ISubscriber<WorldEventTriggeredMessage>>(),
                container.Resolve<UIService>(),
                _time);
            _tempCleanup.Add(uiSystem.Dispose);

            var raid = RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            uiSystem.Start();

            var popup = FindChild<EventPopupView>(uiRoot.transform);
            Assert.IsNotNull(popup, "the popup opens for the pending event");
            Assert.AreEqual(Raid, popup.TitleForTest);

            // A second decision event arrives while the first is undecided, then the FIRST is
            // resolved elsewhere (the bridge path). The popup must not keep showing the event
            // that is already finished - a click from it could only ever be rejected.
            var merchant = RaisePending(Merchant, "trade", "decline");
            Assert.IsTrue(_executor.Execute(raid, Raid, "pay_tribute").Accepted);
            triggerPublisher.Publish(new WorldEventTriggeredMessage
            {
                EventId = Merchant,
                InstanceId = merchant,
                RequiresDecision = true,
            });

            Assert.AreEqual(Merchant, popup.TitleForTest, "the popup re-targets the current (oldest undecided) instance");
            Assert.IsTrue(popup.IsChoiceInteractableForTest("decline"), "and shows THAT instance's own choices");

            int provisions = Raw("provisions");
            popup.InvokeChoiceForTest("decline");

            Assert.AreEqual(provisions, Raw("provisions"), "the click landed on the re-targeted instance, once");
            Assert.IsFalse(_time.HasPendingDecision);
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

        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public readonly List<T> Messages = new List<T>();
            public void Publish(T message) => Messages.Add(message);
        }
    }
}
