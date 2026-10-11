using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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
    /// P13B — a pending world event survives a full session save/load, and its decision UI is
    /// REBUILT on load without ever re-applying a consequence.
    ///
    /// Covers: the instance id and the instance-specific payload (description, choices,
    /// optional speaker) round-tripping through the real envelope on a real disk file; a
    /// decision taken after the restore applying exactly once; loading the same save again
    /// paying nothing a second time; the player/AI race resolving to exactly one winner;
    /// the legacy summary-only save still restoring; and the contract carrying no live
    /// machinery (no waiter, no subscription, no Unity object).
    /// </summary>
    public class PendingEventSaveRoundTripTests
    {
        private const string Raid = "bandit_raid_001";
        private const string Merchant = "wandering_merchant";
        private const string Applicant = "new_disciple_applicant";
        private const string CatalogPath = "Assets/Panel Catalog/MainPanelCatalog.asset";
        private static readonly DateTime T0 = new DateTime(2026, 10, 11, 4, 0, 0, DateTimeKind.Utc);

        private string _root;
        private SaveSlotRepository _repository;
        private Session _session;
        private readonly List<GameObject> _tempRoots = new List<GameObject>();
        private readonly List<Action> _tempCleanup = new List<Action>();

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "sect_event_roundtrip_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);

            _repository = new SaveSlotRepository(new SystemSaveFileSystem(), new SaveSlotPaths(_root));
            _session = NewSession(NewProvider(), _repository);
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

            // DiscipleModalScope is a static counter only OnDisable decrements: drain it so one
            // broken test cannot cascade modal-blocks into the rest of the suite.
            for (int guard = 0; DiscipleModalScope.IsOpen && guard < 8; guard++)
            {
                var drain = new GameObject("PendingEventRoundTrip_ModalDrain", typeof(RectTransform));
                drain.AddComponent<DiscipleModalScope>();
                UnityEngine.Object.DestroyImmediate(drain);
            }

            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (Directory.Exists(_root)) Directory.Delete(_root, true);
                    return;
                }
                catch (IOException) { Thread.Sleep(25); }
                catch (UnauthorizedAccessException) { Thread.Sleep(25); }
            }
        }

        // ══════════════════════════════════════════════════════ 1. instance + payload

        [Test]
        public void Save_WithAPendingEvent_PersistsTheInstanceAndItsInstanceSpecificPayload()
        {
            var instanceId = RaisePending(Raid, "send_inner_disciples", "pay_tribute",
                                          speakerDiscipleId: "d001", speakerDisplayName: "หลี่เวิง");

            var envelope = SaveAndReadBackEnvelope();

            Assert.AreEqual(1, envelope.Snapshot.PendingEvents.Count,
                            "the WHOLE pending queue is captured - nothing is left behind");
            var saved = envelope.Snapshot.PendingEvents[0];
            Assert.AreEqual(instanceId, saved.InstanceId, "the occurrence keeps its identity across a save");
            Assert.AreEqual(Raid, saved.EventId);
            Assert.AreEqual(2, saved.Choices.Count, "the choices the occurrence offered are captured");
            Assert.AreEqual("send_inner_disciples", saved.Choices[0].ChoiceId);
            Assert.AreEqual("pay_tribute", saved.Choices[1].ChoiceId);
            Assert.AreEqual("d001", saved.SpeakerDiscipleId, "the optional speaker is instance-specific payload, so it is saved");
            Assert.AreEqual("หลี่เวิง", saved.SpeakerDisplayName);
            Assert.IsTrue(envelope.Snapshot.PendingDecisionPaused,
                          "the decision pause travels with the event that owns it");

            // Keys 4-7 stay a mirror of the CURRENT occurrence for a reader that predates
            // PendingEvents - the compatibility behaviour, not the authority.
            Assert.AreEqual(Raid, envelope.Snapshot.PendingEventId);
        }

        [Test]
        public void Save_WithNoPendingEvent_RecordsAnEmptyQueueRatherThanAStaleOne()
        {
            RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            _session.Time.ClearPendingDecisions();

            var envelope = SaveAndReadBackEnvelope();

            Assert.AreEqual(0, envelope.Snapshot.PendingEvents.Count);
            Assert.IsNull(envelope.Snapshot.PendingEventId);
            Assert.IsFalse(envelope.Snapshot.HasPendingDecision);
        }

        // ══════════════════════════════════════════════════════ 2. load + decide

        [Test]
        public void Load_RestoresThePendingEventUnderItsOwnInstance_AndADecisionAfterRestoreAppliesOnce()
        {
            var instanceId = RaisePending(Merchant, "trade", "decline");
            WriteSlot();

            // Everything about the live session is replaced by the load, including its pending
            // state - then the SAME save is committed.
            _session.Time.ClearPendingDecisions();
            Assert.IsFalse(_session.Time.HasPendingDecision);

            var load = Load();
            Assert.IsTrue(load.Success, load.Error);

            Assert.IsTrue(_session.Time.HasPendingDecision, "the pending event comes back with the session");
            Assert.AreEqual(instanceId, _session.Time.PendingInstanceId,
                            "the occurrence keeps the id it was saved under, so a decision addressed to it is still valid");
            Assert.AreEqual(Merchant, _session.Time.PendingEventId);
            Assert.AreEqual(2, _session.Time.PendingChoices.Count);

            // A decision taken AFTER the restore applies exactly once.
            int oreRestored = Raw("ore");
            int provisionsRestored = Raw("provisions");

            var decided = _session.Executor.Execute(instanceId, Merchant, "trade");
            Assert.IsTrue(decided.Accepted, decided.Reason);

            Assert.AreEqual(provisionsRestored + 40, Raw("provisions"), "the trade pays out exactly once");
            Assert.AreEqual(oreRestored - 20, Raw("ore"));
            Assert.IsFalse(_session.Time.HasPendingDecision, "resolving it empties the pending queue");

            // ...and it cannot be applied a second time.
            var replayed = _session.Executor.Execute(instanceId, Merchant, "trade");
            Assert.IsFalse(replayed.Accepted, "a replayed decision is refused, not re-applied");
            Assert.AreEqual(provisionsRestored + 40, Raw("provisions"), "no second payout");
            Assert.AreEqual(oreRestored - 20, Raw("ore"));
        }

        [Test]
        public void Load_RestoresEveryOutstandingOccurrence_NotOnlyTheCurrentOne()
        {
            var raid = RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            var merchant = RaisePending(Merchant, "trade", "decline");
            WriteSlot();

            _session.Time.ClearPendingDecisions();
            Assert.IsTrue(Load().Success);

            Assert.AreEqual(2, _session.Time.PendingDecisionCount,
                            "the whole outstanding queue is restored, oldest first");
            Assert.AreEqual(raid, _session.Time.PendingInstanceId, "the oldest undecided occurrence is current again");

            _session.Time.TryGetPendingDecision(merchant, out var second);
            Assert.IsNotNull(second, "the second occurrence kept its own id");
            Assert.AreEqual(Merchant, second.EventId);
        }

        // ══════════════════════════════════════════════════════ 3. repeated load

        [Test]
        public void RepeatedLoad_PaysNothingTwice()
        {
            // A recruiting event is the strongest case: its consequence ADDS a disciple, so a
            // re-applied consequence would be visible as a growing roster.
            RaisePending(Applicant, "accept", "reject");
            WriteSlot();

            var rosterAtSave = _session.Provider.BuildSectEconomyState().Disciples.Count;

            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(Load().Success, $"load #{i + 1} succeeded");
                Assert.AreEqual(rosterAtSave, _session.Provider.BuildSectEconomyState().Disciples.Count,
                                $"load #{i + 1} restored the roster without re-applying the pending event's consequence");
                Assert.AreEqual(1, _session.Time.PendingDecisionCount,
                                $"load #{i + 1} left exactly one decision outstanding");
            }

            // The reward is paid only when the player actually decides.
            Assert.IsTrue(_session.Executor.Execute(Applicant, "accept").Accepted);
            Assert.AreEqual(rosterAtSave + 1, _session.Provider.BuildSectEconomyState().Disciples.Count,
                            "the applicant is recruited exactly once, by the decision");
        }

        // ══════════════════════════════════════════════════════ 4. player / AI race

        [Test]
        public void PlayerAiRace_AfterRestore_ExactlyOneWinnerAndNoSecondConsequence()
        {
            var instanceId = RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            WriteSlot();
            Assert.IsTrue(Load().Success);

            int provisionsBefore = Raw("provisions");
            int oreBefore = Raw("ore");

            // The AI GM (over the bridge) wins the race. It names the instance it saw, which is
            // the same instance the restored player UI is showing.
            var ai = _session.Executor.Execute(instanceId, Raid, "pay_tribute");
            Assert.IsTrue(ai.Accepted, ai.Reason);
            Assert.AreEqual(provisionsBefore - 20, Raw("provisions"));

            // The player's click reaches the same single decision path a moment later and is
            // refused - no second consequence, and the refusal is explicit.
            var player = _session.Executor.Execute(instanceId, Raid, "send_inner_disciples");
            Assert.IsFalse(player.Accepted, "the loser of the race is refused");
            Assert.IsFalse(string.IsNullOrEmpty(player.Reason), "and is told why");
            Assert.AreEqual(provisionsBefore - 20, Raw("provisions"), "the raid is paid for once, not twice");
            Assert.AreEqual(oreBefore, Raw("ore"));
        }

        [Test]
        public void PlayerAiRace_OnADifferentOccurrence_DoesNotBlockTheOtherPlayer()
        {
            var raid = RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            var merchant = RaisePending(Merchant, "trade", "decline");
            WriteSlot();
            Assert.IsTrue(Load().Success);

            int provisionsBefore = Raw("provisions");
            int oreBefore = Raw("ore");

            // The AI resolves one occurrence; the player still resolves the OTHER one.
            Assert.IsTrue(_session.Executor.Execute(raid, Raid, "send_inner_disciples").Accepted);
            var player = _session.Executor.Execute(merchant, Merchant, "trade");

            Assert.IsTrue(player.Accepted, "an unrelated outstanding occurrence is still decidable");
            Assert.AreEqual(provisionsBefore - 20 + 40, Raw("provisions"));
            Assert.AreEqual(oreBefore - 20, Raw("ore"));
            Assert.IsFalse(_session.Time.HasPendingDecision, "both occurrences are resolved");
        }

        // ══════════════════════════════════════════════════════ 5. restore the decision UI

        [Test]
        public void Restore_RebuildsTheDecisionUiFromTheRestoredInstance_AndAppliesNoConsequence()
        {
            var uiRootGo = new GameObject("PendingEventRoundTrip_UIRoot", typeof(RectTransform));
            _tempRoots.Add(uiRootGo);
            var uiRoot = uiRootGo.AddComponent<UIRoot>();
            var catalog = AssetDatabase.LoadAssetAtPath<UIPanelCatalog>(CatalogPath);
            Assert.IsNotNull(catalog, "the generated MainPanelCatalog must exist");

            var instanceId = RaisePending(Raid, "send_inner_disciples", "pay_tribute",
                                          speakerDiscipleId: "d001", speakerDisplayName: "หลี่เวิง");
            WriteSlot();

            // The session being loaded is a FRESH one: the save is what carries the event, not
            // some live leftover. Otherwise "the popup is up after the load" would prove nothing.
            _session.Time.ClearPendingDecisions();

            // A real UIService container, exactly as the game wires it.
            var builder = new ContainerBuilder();
            builder.RegisterMessagePipe();
            builder.RegisterInstance(_session.Time);
            builder.RegisterInstance<ISectStateProvider>(_session.Provider);
            builder.Register<DecisionExecutor>(Lifetime.Singleton);
            builder.Register<EventPopupPresenter>(Lifetime.Transient);
            builder.Register<WorldEventChipPresenter>(Lifetime.Transient);
            builder.RegisterInstance(uiRoot);
            builder.RegisterInstance(catalog);
            builder.Register<UIService>(Lifetime.Singleton);
            var container = builder.Build();
            _tempCleanup.Add(container.Dispose);

            var uiSystem = new WorldEventUISystem(
                container.Resolve<ISubscriber<WorldEventTriggeredMessage>>(),
                container.Resolve<UIService>(),
                _session.Time);
            _tempCleanup.Add(uiSystem.Dispose);
            uiSystem.Start(); // nothing pending yet - the popup is closed

            Assert.IsNull(FindChild<EventPopupView>(uiRoot.transform), "nothing is on screen before the restore");

            int provisionsBefore = Raw("provisions");
            int oreBefore = Raw("ore");

            int announcedBefore = _session.Events.Messages.Count;
            Assert.IsTrue(Load().Success);

            // The restore RE-ANNOUNCES the occurrence it brought back, under the same instance
            // id, so every derived subscriber is rebuilt from the restored state (§4) rather
            // than from anything that was serialized.
            Assert.AreEqual(announcedBefore + 1, _session.Events.Messages.Count,
                            "the restore re-announces the restored occurrence");
            var announced = _session.Events.Messages[announcedBefore];
            Assert.AreEqual(instanceId, announced.InstanceId, "and it announces the SAME instance");
            Assert.AreEqual(Raid, announced.EventId);
            Assert.AreEqual("หลี่เวิง", announced.SpeakerDisplayName, "including the instance-specific speaker payload");

            // In the game that re-announce IS the MessagePipe bus this UI subscribes to. The
            // test's TimeSystem publishes into its own sink (so the assertion above can inspect
            // exactly what it sent), so deliver the same signal on the UI's bus here.
            container.Resolve<IPublisher<WorldEventTriggeredMessage>>().Publish(announced);

            var popup = FindChild<EventPopupView>(uiRoot.transform);
            Assert.IsNotNull(popup, "the restored pending event opens its decision UI");
            Assert.IsTrue(popup.isActiveAndEnabled);
            Assert.AreEqual(Raid, popup.TitleForTest);
            Assert.AreEqual(2, popup.ChoiceCountForTest, "the restored occurrence offers the choices it was saved with");
            Assert.IsTrue(popup.IsChoiceInteractableForTest("pay_tribute"));
            Assert.IsFalse(popup.IsShowingResolvedNoteForTest, "a restored event is PENDING, not resolved");
            Assert.IsTrue(string.IsNullOrEmpty(popup.StatusForTest), "no stale status is carried onto a restored event");

            // The optional speaker is re-derived from the authoritative roster and the
            // instance-specific payload - not from anything serialized as a picture.
            Assert.IsTrue(popup.SpeakerVisibleForTest, "the restored event names a speaker, so the area is shown");
            Assert.AreEqual("หลี่เวิง", popup.SpeakerNameForTest);

            // Restoring the event rebuilt the UI and applied NOTHING.
            Assert.AreEqual(provisionsBefore, Raw("provisions"), "a restore never applies the consequence");
            Assert.AreEqual(oreBefore, Raw("ore"));

            // And the restored panel resolves the restored instance exactly once.
            popup.InvokeChoiceForTest("pay_tribute");
            Assert.AreEqual(provisionsBefore - 20, Raw("provisions"));
            Assert.IsNull(FindChild<EventPopupView>(uiRoot.transform),
                          "an accepted decision closes the popup (the opener owns the panel)");
        }

        [Test]
        public void Restore_WithNoSpeaker_HidesTheSpeakerAreaInsteadOfDrawingABlankOne()
        {
            RaisePending(Raid, "send_inner_disciples", "pay_tribute");
            WriteSlot();
            _session.Time.ClearPendingDecisions();

            var uiRootGo = new GameObject("PendingEventRoundTrip_UIRoot2", typeof(RectTransform));
            _tempRoots.Add(uiRootGo);
            var uiRoot = uiRootGo.AddComponent<UIRoot>();
            var catalog = AssetDatabase.LoadAssetAtPath<UIPanelCatalog>(CatalogPath);

            var builder = new ContainerBuilder();
            builder.RegisterMessagePipe();
            builder.RegisterInstance(_session.Time);
            builder.RegisterInstance<ISectStateProvider>(_session.Provider);
            builder.Register<DecisionExecutor>(Lifetime.Singleton);
            builder.Register<EventPopupPresenter>(Lifetime.Transient);
            builder.Register<WorldEventChipPresenter>(Lifetime.Transient);
            builder.RegisterInstance(uiRoot);
            builder.RegisterInstance(catalog);
            builder.Register<UIService>(Lifetime.Singleton);
            var container = builder.Build();
            _tempCleanup.Add(container.Dispose);

            var uiSystem = new WorldEventUISystem(
                container.Resolve<ISubscriber<WorldEventTriggeredMessage>>(),
                container.Resolve<UIService>(),
                _session.Time);
            _tempCleanup.Add(uiSystem.Dispose);
            uiSystem.Start();
            Assert.IsNull(FindChild<EventPopupView>(uiRoot.transform), "nothing is on screen before the restore");

            int announcedBefore = _session.Events.Messages.Count;
            Assert.IsTrue(Load().Success);

            // See the speaker test above: the restore's re-announce is what drives the UI in the
            // game (one bus); here the test's own sink is inspected and the same signal is
            // delivered on the UI's bus.
            Assert.AreEqual(announcedBefore + 1, _session.Events.Messages.Count);
            container.Resolve<IPublisher<WorldEventTriggeredMessage>>().Publish(_session.Events.Messages[announcedBefore]);

            var popup = FindChild<EventPopupView>(uiRoot.transform);
            Assert.IsNotNull(popup, "the restored pending event opens its decision UI");
            Assert.IsFalse(popup.SpeakerVisibleForTest, "no speaker in the event = no portrait area at all");
            Assert.IsTrue(string.IsNullOrEmpty(popup.SpeakerNameForTest));
        }

        // ══════════════════════════════════════════════════════ 6. compatibility + refusals

        [Test]
        public void LegacySave_WithOnlyTheSingleSummary_StillRestoresAsOneOccurrence()
        {
            var envelope = new SectSessionSnapshotEnvelope
            {
                Version = SectSessionSnapshotEnvelope.CurrentVersion,
                SavedAtUtc = T0,
                Snapshot = new SectSessionSnapshot
                {
                    PendingEventId = Merchant,
                    PendingDescription = "A merchant arrives.",
                    PendingChoices = new List<EventChoiceInfo>
                    {
                        new EventChoiceInfo { ChoiceId = "trade", Label = "trade" },
                        new EventChoiceInfo { ChoiceId = "decline", Label = "decline" },
                    },
                    PendingDecisionPaused = true,
                },
            };

            string reason;
            Assert.IsTrue(_session.Snapshots.TryRestoreEnvelope(envelope, out reason), reason);

            Assert.AreEqual(1, _session.Time.PendingDecisionCount, "an old save's pending event is not silently dropped");
            Assert.AreEqual(Merchant, _session.Time.PendingEventId);
            Assert.AreEqual(2, _session.Time.PendingChoices.Count);
            Assert.IsTrue(_session.Time.IsPendingDecisionPaused, "its pause flag is honoured");
            Assert.IsFalse(string.IsNullOrEmpty(_session.Time.PendingInstanceId),
                           "a save with no instance id gets a fresh one rather than an unusable empty one");
            Assert.AreEqual(0, envelope.Snapshot.PendingEvents.Count, "the old save really had no queue");
        }

        [Test]
        public void Restore_IsRefusedWhenThePendingQueueItselfIsInconsistent()
        {
            AssertRefused(queue: new List<PendingEventSnapshot>
            {
                new PendingEventSnapshot { InstanceId = "evt_000001", EventId = Raid },
                new PendingEventSnapshot { InstanceId = "evt_000001", EventId = Merchant }, // duplicate id
            }, expect: "repeats pending event instance id");

            AssertRefused(queue: new List<PendingEventSnapshot>
            {
                new PendingEventSnapshot { InstanceId = "evt_000001", EventId = null }, // no event id
            }, expect: "has no event id");

            AssertRefused(queue: new List<PendingEventSnapshot>
            {
                new PendingEventSnapshot
                {
                    InstanceId = "evt_000001",
                    EventId = Raid,
                    Choices = new List<EventChoiceInfo> { new EventChoiceInfo { ChoiceId = null } }, // invalid choice
                },
            }, expect: "invalid choice");

            var tooMany = new List<PendingEventSnapshot>();
            for (int i = 1; i <= TimeSystem.MaxPendingDecisions + 1; i++)
                tooMany.Add(new PendingEventSnapshot { InstanceId = "evt_" + i.ToString("000000"), EventId = Raid });
            AssertRefused(tooMany, "more than the live queue can hold");
        }

        [Test]
        public void Contract_CarriesNoWaiterSubscriptionOrUnityObject()
        {
            foreach (var type in new[] { typeof(SectSessionSnapshot), typeof(PendingEventSnapshot),
                                         typeof(SectSessionSnapshotEnvelope) })
            {
                foreach (var member in type.GetProperties())
                    AssertNothingLive(member.PropertyType, type.Name + "." + member.Name);
                foreach (var field in type.GetFields())
                    AssertNothingLive(field.FieldType, type.Name + "." + field.Name);
            }
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>
        /// A serializable member may only ever be plain data: a live waiter, a subscription, a
        /// delegate or a Unity object in the contract would be revived on load (§4).
        /// </summary>
        private static void AssertNothingLive(Type type, string where)
        {
            Assert.IsFalse(typeof(IDisposable).IsAssignableFrom(type),
                           $"{where} is IDisposable — a live subscription/waiter must never be serialized");
            Assert.IsFalse(typeof(Delegate).IsAssignableFrom(type),
                           $"{where} is a delegate — a callback must never be serialized");
            Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(type),
                           $"{where} is a Unity object — never serialize engine objects");
            Assert.IsFalse(type.FullName != null && type.FullName.Contains("UniTaskCompletionSource"),
                           $"{where} is a UniTaskCompletionSource — the waiter is rebuilt, never saved");

            if (!type.IsGenericType) return;
            foreach (var argument in type.GetGenericArguments()) AssertNothingLive(argument, where);
        }

        private void AssertRefused(List<PendingEventSnapshot> queue, string expect)
        {
            var envelope = new SectSessionSnapshotEnvelope
            {
                Version = SectSessionSnapshotEnvelope.CurrentVersion,
                SavedAtUtc = T0,
                Snapshot = new SectSessionSnapshot { PendingEvents = queue, PendingDecisionPaused = true },
            };

            string reason;
            Assert.IsFalse(_session.Snapshots.TryRestoreEnvelope(envelope, out reason),
                           "an inconsistent pending queue must be refused before anything is replaced");
            StringAssert.Contains(expect, reason);
            Assert.IsFalse(_session.Time.HasPendingDecision, "a refused restore left live pending state untouched");
        }

        /// <summary>
        /// Raises a decision-requiring event and returns the instance id of THAT occurrence
        /// (taken from the trigger message it published — NOT from PendingInstanceId, which is
        /// the queue HEAD and would hand back the first occurrence's id for every raise).
        /// </summary>
        private string RaisePending(string eventId, string choiceA, string choiceB,
                                    string speakerDiscipleId = null, string speakerDisplayName = null)
        {
            int before = _session.Events.Messages.Count;
            Assert.IsTrue(_session.Time.RaiseWorldEvent(eventId, eventId + " description", true,
                                                        Choices(choiceA, choiceB),
                                                        speakerDiscipleId, speakerDisplayName),
                          "the decision event was stored");
            Assert.AreEqual(before + 1, _session.Events.Messages.Count, "one trigger message per raise");

            var instanceId = _session.Events.Messages[before].InstanceId;
            Assert.IsFalse(string.IsNullOrEmpty(instanceId), "the trigger message carries the instance id");
            return instanceId;
        }

        private static List<EventChoiceInfo> Choices(params string[] ids)
        {
            var list = new List<EventChoiceInfo>();
            foreach (var id in ids) list.Add(new EventChoiceInfo { ChoiceId = id, Label = id });
            return list;
        }

        private void WriteSlot()
        {
            var envelope = _session.Snapshots.CaptureEnvelope();
            var result = _repository.Write(envelope, CancellationToken.None);
            Assert.IsTrue(result.Success, result.Error);
        }

        private SectSessionSnapshotEnvelope SaveAndReadBackEnvelope() => _session.Snapshots.CaptureEnvelope();

        private SaveSlotLoadResult Load() => _session.Saves.LoadAsync().GetAwaiter().GetResult();

        private int Raw(string resourceId)
        {
            int value;
            return _session.Provider.BuildSectEconomyState().Stockpile.RawResources.TryGetValue(resourceId, out value)
                ? value
                : 0;
        }

        private static T FindChild<T>(Transform parent) where T : Component
        {
            var found = parent.GetComponentsInChildren<T>(true);
            return found.Length > 0 ? found[0] : null;
        }

        private sealed class Session
        {
            public SectStateProvider Provider;
            public TimeSystem Time;
            public BufferPublisher<WorldEventTriggeredMessage> Events;
            public BufferPublisher<SessionRestoredMessage> Restored;
            public SessionSnapshotService Snapshots;
            public SaveSessionService Saves;
            public DecisionExecutor Executor;
        }

        private static Session NewSession(SectStateProvider provider, SaveSlotRepository repository)
        {
            var session = new Session
            {
                Provider = provider,
                Events = new BufferPublisher<WorldEventTriggeredMessage>(),
                Restored = new BufferPublisher<SessionRestoredMessage>(),
            };
            session.Time = new TimeSystem(
                new BufferPublisher<TimeSpeedChangedMessage>(),
                session.Events,
                new TimeRuntimeConfig { AutoPauseOnDecisionEvent = true });

            session.Snapshots = new SessionSnapshotService(
                session.Provider, session.Time, session.Restored, new MutableClock(T0));
            session.Saves = new SaveSessionService(
                session.Snapshots, repository, new InlineSaveWorkScheduler(),
                new SessionTransitionTracker(), new SaveOperationGate());
            session.Executor = new DecisionExecutor(
                session.Provider, session.Time, new BufferPublisher<DecisionExecutedMessage>());
            return session;
        }

        private static SectStateProvider NewProvider()
        {
            return new SectStateProvider(
                new BufferPublisher<DiscipleRecruitedMessage>(),
                new BufferPublisher<SectResourceChangedMessage>(),
                new BufferPublisher<AvatarEquipmentChangedMessage>(),
                new BufferPublisher<DiscipleChibiBackendChangedMessage>(),
                new AvatarPartPool(),
                VisualRuntimeConfig.Instance,
                new DefaultEntitlementProvider(),
                new BuildingDefPool(),
                new BufferPublisher<BuildingPlacedMessage>(),
                new BufferPublisher<DiscipleTaskChangedMessage>(),
                new BufferPublisher<DiscipleOwnerChangedMessage>(),
                new MutableClock(T0));
        }

        private sealed class InlineSaveWorkScheduler : ISaveWorkScheduler
        {
            public UniTask<T> RunOnWorkerAsync<T>(Func<T> work, CancellationToken cancellationToken)
                => UniTask.FromResult(work());

            public UniTask SwitchToMainThreadAsync() => UniTask.CompletedTask;
        }

        private sealed class MutableClock : IClock
        {
            public MutableClock(DateTime now) { UtcNow = now; }
            public DateTime UtcNow { get; set; }
        }

        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public readonly List<T> Messages = new List<T>();
            public void Publish(T message) => Messages.Add(message);
        }
    }
}
