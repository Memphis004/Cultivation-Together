using System;
using System.Collections.Generic;
using MessagePipe;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Xianxia.Sect;
using Xianxia.Sect.Messages;
using Xianxia.Sect.UI;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// E1 — pause reasons + time-control HUD. Covers reason combinations, the
    /// decision/user independence rule, ResumeByPlayer, the SimulationDelta gate,
    /// speed validation, the refresh-trigger message (including when IsPaused does
    /// not change), the legacy SetPaused mapping, AutoPauseOnDecisionEvent, and the
    /// presenter's three label states.
    ///
    /// E1.1 — the Play button is gone, so the highlight rule (ActiveButton) and the
    /// Pause/Nx click behaviour are covered here too. Only two tests genuinely
    /// depended on Play and were rewritten (see their comments); nothing was weakened.
    /// </summary>
    public class TimeControlTests
    {
        private BufferPublisher<TimeSpeedChangedMessage> _speedMessages;
        private BufferPublisher<WorldEventTriggeredMessage> _worldEvents;
        private TimeSystem _time;

        [SetUp]
        public void SetUp()
        {
            _speedMessages = new BufferPublisher<TimeSpeedChangedMessage>();
            _worldEvents = new BufferPublisher<WorldEventTriggeredMessage>();
            _time = new TimeSystem(_speedMessages, _worldEvents);
        }

        private static TimeSystem NewTime(TimeSpeedChangedMessageBuffer buffer, bool autoPause)
        {
            return new TimeSystem(
                buffer.Publisher,
                new BufferPublisher<WorldEventTriggeredMessage>(),
                new TimeRuntimeConfig { AutoPauseOnDecisionEvent = autoPause });
        }

        private void RaiseDecisionEvent(string eventId = "bandit_raid_001")
        {
            _time.RaiseWorldEvent(eventId, "Bandits", true, new List<EventChoiceInfo>
            {
                new EventChoiceInfo { ChoiceId = "send_inner_disciples", Label = "ส่งศิษย์ใน" },
                new EventChoiceInfo { ChoiceId = "pay_tribute", Label = "จ่ายส่วย" },
            });
        }

        // ------------------------------------------------ reason combinations

        [Test]
        public void Reasons_AreIndependent_AndIsPausedIsAnyOf()
        {
            Assert.IsFalse(_time.IsPaused);
            Assert.IsFalse(_time.IsUserPaused);
            Assert.IsFalse(_time.IsPendingDecisionPaused);

            _time.Pause(TimePauseReason.User);
            Assert.IsTrue(_time.IsPaused);
            Assert.IsTrue(_time.IsUserPaused);
            Assert.IsFalse(_time.IsPendingDecisionPaused);

            _time.Pause(TimePauseReason.PendingDecision);
            Assert.IsTrue(_time.IsPaused);
            Assert.IsTrue(_time.IsUserPaused, "adding PendingDecision must not disturb User");
            Assert.IsTrue(_time.IsPendingDecisionPaused);
        }

        [Test]
        public void ResolvingDecision_DoesNotClearUserPause()
        {
            _time.Pause(TimePauseReason.User);
            _time.Pause(TimePauseReason.PendingDecision);

            _time.Resume(TimePauseReason.PendingDecision);

            Assert.IsFalse(_time.IsPendingDecisionPaused, "the decision was resolved");
            Assert.IsTrue(_time.IsUserPaused, "clearing PendingDecision must never clear User");
            Assert.IsTrue(_time.IsPaused, "still paused by the player");
        }

        [Test]
        public void ResumingUser_DoesNotClearPendingDecision()
        {
            _time.Pause(TimePauseReason.User);
            _time.Pause(TimePauseReason.PendingDecision);

            _time.Resume(TimePauseReason.User);

            Assert.IsFalse(_time.IsUserPaused);
            Assert.IsTrue(_time.IsPendingDecisionPaused, "clearing User must never clear PendingDecision");
            Assert.IsTrue(_time.IsPaused);
        }

        [Test]
        public void ResumeByPlayer_ClearsBoth()
        {
            _time.Pause(TimePauseReason.User);
            _time.Pause(TimePauseReason.PendingDecision);

            _time.ResumeByPlayer();

            Assert.IsFalse(_time.IsUserPaused);
            Assert.IsFalse(_time.IsPendingDecisionPaused);
            Assert.IsFalse(_time.IsPaused);
        }

        // ------------------------------------------------ simulation gate

        [Test]
        public void SimulationDelta_IsZero_WhileAnyReasonIsActive()
        {
            const float frame = 0.5f;

            _time.SetSpeed(3); // 3x so the unpaused value proves speed is applied
            Assert.AreEqual(1.5f, TimeSystem.ComputeSimulationDelta(_time.IsPaused, _time.Speed, frame), 1e-6f);

            _time.Pause(TimePauseReason.User);
            Assert.AreEqual(0f, TimeSystem.ComputeSimulationDelta(_time.IsPaused, _time.Speed, frame), "User pause freezes");

            _time.Resume(TimePauseReason.User);
            _time.Pause(TimePauseReason.PendingDecision);
            Assert.AreEqual(0f, TimeSystem.ComputeSimulationDelta(_time.IsPaused, _time.Speed, frame), "PendingDecision freezes");
        }

        // ------------------------------------------------ speed validation

        [Test]
        public void SetSpeed_IsValidatedTo1Through3()
        {
            _time.SetSpeed(0);
            Assert.AreEqual(1, _time.Speed, "0 clamps up to 1x");
            _time.SetSpeed(-7);
            Assert.AreEqual(1, _time.Speed, "negative clamps up to 1x");
            _time.SetSpeed(99);
            Assert.AreEqual(3, _time.Speed, "99 clamps down to 3x");
            _time.SetSpeed(2);
            Assert.AreEqual(2, _time.Speed);
        }

        // ------------------------------------------------ refresh trigger message

        [Test]
        public void SpeedChangedMessage_EmittedOnEveryEffectiveChange_IncludingWhenPausedStaysTrue()
        {
            int before = _speedMessages.Messages.Count;

            _time.Pause(TimePauseReason.User);
            Assert.AreEqual(before + 1, _speedMessages.Messages.Count, "adding a pause reason publishes");

            // IsPaused is ALREADY true here - a second reason must still refresh.
            _time.Pause(TimePauseReason.PendingDecision);
            Assert.AreEqual(before + 2, _speedMessages.Messages.Count,
                            "a second reason publishes even though IsPaused stays true");
            Assert.AreEqual(true, _speedMessages.Messages[before + 1].Paused);

            _time.Pause(TimePauseReason.PendingDecision); // idempotent - no change
            Assert.AreEqual(before + 2, _speedMessages.Messages.Count, "a no-op pause publishes nothing");

            _time.Resume(TimePauseReason.PendingDecision);
            Assert.AreEqual(before + 3, _speedMessages.Messages.Count, "clearing a reason publishes");

            _time.SetSpeed(3);
            Assert.AreEqual(before + 4, _speedMessages.Messages.Count, "speed change publishes");
            Assert.AreEqual(3, _speedMessages.Messages[before + 3].Speed);

            _time.SetSpeed(3); // same value - no change
            Assert.AreEqual(before + 4, _speedMessages.Messages.Count, "no-op speed publishes nothing");
        }

        // ------------------------------------------------ legacy mapping

        [Test]
        public void LegacySetPaused_MapsToUser_AndDoesNotClearDecision()
        {
            _time.SetPaused(true);
            Assert.IsTrue(_time.IsUserPaused);
            Assert.IsFalse(_time.IsPendingDecisionPaused);

            _time.Pause(TimePauseReason.PendingDecision);
            _time.SetPaused(false);
            Assert.IsFalse(_time.IsUserPaused, "legacy false clears User only");
            Assert.IsTrue(_time.IsPendingDecisionPaused, "legacy false must not clear the decision pause");
        }

        // ------------------------------------------------ config

        [Test]
        public void AutoPauseOnDecisionEvent_True_AddsPendingDecision()
        {
            RaiseDecisionEvent();
            Assert.IsTrue(_time.IsPendingDecisionPaused);
            Assert.IsFalse(_time.IsUserPaused);
        }

        [Test]
        public void AutoPauseOnDecisionEvent_False_LeavesGameRunning()
        {
            var buffer = new TimeSpeedChangedMessageBuffer();
            var time = NewTime(buffer, autoPause: false);

            time.RaiseWorldEvent("bandit_raid_001", "Bandits", true, new List<EventChoiceInfo>());

            Assert.IsFalse(time.IsPaused, "config disabled auto-pause → the game keeps running");
            Assert.IsFalse(time.IsPendingDecisionPaused);
        }

        // ------------------------------------------------ E1.1 highlight rule (pure)

        [Test]
        public void ActiveButton_WhileRunning_FollowsTheSpeed()
        {
            Assert.AreEqual(TimeControlActiveButton.Speed1, TimeControlView.ActiveButton(isPaused: false, speed: 1));
            Assert.AreEqual(TimeControlActiveButton.Speed2, TimeControlView.ActiveButton(false, 2));
            Assert.AreEqual(TimeControlActiveButton.Speed3, TimeControlView.ActiveButton(false, 3));
        }

        [Test]
        public void ActiveButton_PausedByPlayer_HighlightsPauseAndNoSpeed()
        {
            _time.Pause(TimePauseReason.User);
            _time.SetSpeed(3);

            Assert.AreEqual(TimeControlActiveButton.Pause, TimeControlView.ActiveButton(_time.IsPaused, _time.Speed));
            Assert.AreEqual(TimeControlActiveButton.Pause, TimeControlView.ActiveButton(_time.IsPaused, 1),
                            "no speed button is highlighted while paused - at ANY speed");
        }

        [Test]
        public void ActiveButton_DecisionPaused_HighlightsPauseAndNoSpeed()
        {
            RaiseDecisionEvent();
            _time.SetSpeed(2);

            Assert.IsTrue(_time.IsPendingDecisionPaused);
            Assert.AreEqual(TimeControlActiveButton.Pause, TimeControlView.ActiveButton(_time.IsPaused, _time.Speed));
            Assert.AreEqual(TimeControlActiveButton.Pause, TimeControlView.ActiveButton(_time.IsPaused, 3));
        }

        [Test]
        public void ActiveButton_BothReasons_HighlightsPauseAndNoSpeed()
        {
            RaiseDecisionEvent();
            _time.Pause(TimePauseReason.User);

            Assert.IsTrue(_time.IsUserPaused);
            Assert.IsTrue(_time.IsPendingDecisionPaused);
            Assert.AreEqual(TimeControlActiveButton.Pause, TimeControlView.ActiveButton(_time.IsPaused, _time.Speed));
        }

        [Test]
        public void AutoPauseDisabled_WithEventPending_KeepsTheSpeedHighlighted()
        {
            var buffer = new TimeSpeedChangedMessageBuffer();
            var time = NewTime(buffer, autoPause: false);
            time.SetSpeed(2);

            time.RaiseWorldEvent("bandit_raid_001", "Bandits", true, new List<EventChoiceInfo>());

            Assert.IsTrue(time.HasPendingDecision, "the event is still pending");
            Assert.IsFalse(time.IsPaused, "with auto-pause off the game keeps running");
            Assert.AreEqual(TimeControlActiveButton.Speed2, TimeControlView.ActiveButton(time.IsPaused, time.Speed),
                            "a pending event that does not pause leaves the speed button highlighted");
        }

        // ------------------------------------------------ E1.1 presenter: labels + highlight

        [Test]
        public void Presenter_ShowsTheThreeLabelStates_AndTheHighlightFollowsTheState()
        {
            var h = new Harness(_time);

            Assert.AreEqual(string.Format(TimeControlPresenter.RunningLabelFormat, 1), h.View.StatusTextForTest);
            Assert.IsTrue(h.View.IsSpeedActiveForTest(1), "running 1x highlights 1x");
            Assert.IsFalse(h.View.IsPauseActiveForTest);

            // A click only mutates — the label/highlight must stay put until the trigger.
            h.View.InvokePauseForTest();
            Assert.AreEqual(string.Format(TimeControlPresenter.RunningLabelFormat, 1), h.View.StatusTextForTest,
                            "refresh only on the trigger message - not as a side effect of the click");
            Assert.IsFalse(h.View.IsPauseActiveForTest, "highlight also waits for the trigger");
            h.Deliver();
            Assert.AreEqual(TimeControlPresenter.PausedByPlayerLabel, h.View.StatusTextForTest);
            Assert.IsTrue(h.View.IsPauseActiveForTest, "paused → Pause highlighted");
            Assert.IsFalse(h.View.IsSpeedActiveForTest(1), "paused → NO speed button highlighted");

            h.View.InvokePauseForTest(); // toggle off
            h.Deliver();
            Assert.AreEqual(string.Format(TimeControlPresenter.RunningLabelFormat, 1), h.View.StatusTextForTest);
            Assert.IsTrue(h.View.IsSpeedActiveForTest(1), "the speed highlight comes back on resume");

            // decision pause arriving from outside → refresh via the trigger message
            _time.Pause(TimePauseReason.PendingDecision);
            h.Deliver();
            Assert.AreEqual(TimeControlPresenter.AwaitingDecisionLabel, h.View.StatusTextForTest);
            Assert.IsTrue(h.View.IsPauseActiveForTest);

            // both reasons active → awaiting-decision wording wins, still no speed highlight
            _time.SetSpeed(3);
            _time.Pause(TimePauseReason.User);
            h.Deliver();
            Assert.AreEqual(TimeControlPresenter.AwaitingDecisionLabel, h.View.StatusTextForTest);
            Assert.IsFalse(h.View.IsSpeedActiveForTest(3));

            // 3x while paused by both reasons → resumes at 3x (never locked out)
            h.View.InvokeSpeedForTest(3);
            h.Deliver();
            Assert.AreEqual(string.Format(TimeControlPresenter.RunningLabelFormat, 3), h.View.StatusTextForTest);
            Assert.IsTrue(h.View.IsSpeedActiveForTest(3));
            Assert.IsFalse(h.View.IsPauseActiveForTest);

            h.Dispose();
        }

        // ------------------------------------------------ E1.1 click behaviour

        [Test]
        public void SpeedWhilePausedByPlayer_ResumesAtThatSpeed()
        {
            var h = new Harness(_time);
            _time.Pause(TimePauseReason.User);
            int before = _speedMessages.Messages.Count;

            h.View.InvokeSpeedForTest(2);

            Assert.AreEqual(2, _time.Speed);
            Assert.IsFalse(_time.IsPaused, "Nx resumes from the player pause");
            Assert.IsFalse(_time.IsUserPaused);

            // Ordering proof: the speed is applied FIRST, so no frame can run at 1x.
            Assert.AreEqual(before + 2, _speedMessages.Messages.Count, "speed change + resume = two effective changes");
            Assert.AreEqual(2, _speedMessages.Messages[before].Speed);
            Assert.AreEqual(true, _speedMessages.Messages[before].Paused, "the speed changed while still paused");
            Assert.AreEqual(false, _speedMessages.Messages[before + 1].Paused, "then it resumed at the new speed");

            h.Deliver();
            Assert.AreEqual(string.Format(TimeControlPresenter.RunningLabelFormat, 2), h.View.StatusTextForTest);
            Assert.IsTrue(h.View.IsSpeedActiveForTest(2));
            Assert.IsFalse(h.View.IsPauseActiveForTest);

            h.Dispose();
        }

        [Test]
        public void SpeedWhileDecisionPaused_Resumes_KeepsEventPending_AndChipConditionStillTrue()
        {
            var h = new Harness(_time);
            RaiseDecisionEvent(); // decision pause + a pending event
            Assert.IsTrue(_time.IsPaused);
            Assert.IsTrue(_time.HasPendingDecision);

            h.View.InvokeSpeedForTest(1); // 1x is the current speed → SetSpeed is a no-op, resume still happens

            Assert.IsFalse(_time.IsPaused, "Nx resumes even while a decision is pending (never locked out)");
            Assert.IsFalse(_time.IsPendingDecisionPaused);
            Assert.IsTrue(_time.HasPendingDecision, "the pending event is untouched - only the pause cleared");
            Assert.IsTrue(WorldEventChipPresenter.ShouldShow(_time.HasPendingDecision, popupVisible: false, modalOpen: false),
                          "the chip condition stays true (popup hidden)");

            h.Deliver();
            Assert.AreEqual(string.Format(TimeControlPresenter.RunningLabelFormat, 1), h.View.StatusTextForTest);
            Assert.IsTrue(h.View.IsSpeedActiveForTest(1), "running again → the speed button is highlighted");

            h.Dispose();
        }

        [Test]
        public void SpeedWhileDecisionPaused_AtADifferentSpeed_ResumesAtThatSpeed()
        {
            var h = new Harness(_time);
            RaiseDecisionEvent();
            int before = _speedMessages.Messages.Count;

            h.View.InvokeSpeedForTest(3);

            Assert.AreEqual(3, _time.Speed);
            Assert.IsFalse(_time.IsPaused);
            Assert.AreEqual(before + 2, _speedMessages.Messages.Count);
            Assert.AreEqual(3, _speedMessages.Messages[before].Speed);
            Assert.AreEqual(true, _speedMessages.Messages[before].Paused, "3x applied before the resume");
            Assert.AreEqual(3, _speedMessages.Messages[before + 1].Speed);
            Assert.AreEqual(false, _speedMessages.Messages[before + 1].Paused);

            h.Dispose();
        }

        [Test]
        public void SpeedWhileRunning_OnlyChangesSpeed_AndSameSpeedDoesNothing()
        {
            var h = new Harness(_time);
            Assert.IsFalse(_time.IsPaused);

            int before = _speedMessages.Messages.Count;
            h.View.InvokeSpeedForTest(3);

            Assert.AreEqual(3, _time.Speed);
            Assert.IsFalse(_time.IsPaused, "a running game is not resumed by a speed click");
            Assert.AreEqual(before + 1, _speedMessages.Messages.Count, "exactly one effective change");
            Assert.AreEqual(false, _speedMessages.Messages[before].Paused);
            Assert.AreEqual(3, _speedMessages.Messages[before].Speed);

            h.View.InvokeSpeedForTest(3); // already running at 3x
            Assert.AreEqual(3, _time.Speed);
            Assert.AreEqual(before + 1, _speedMessages.Messages.Count, "already at 3x and running → do nothing");

            h.Dispose();
        }

        [Test]
        public void PauseTogglesOnAndOff_AndResumeKeepsTheCurrentSpeed()
        {
            var h = new Harness(_time);
            _time.SetSpeed(2);
            h.Deliver();

            h.View.InvokePauseForTest();
            Assert.IsTrue(_time.IsUserPaused, "pause adds the User reason");
            Assert.IsFalse(_time.IsPendingDecisionPaused);

            h.View.InvokePauseForTest();
            Assert.IsFalse(_time.IsPaused, "the second press resumes");
            Assert.AreEqual(2, _time.Speed, "the resume keeps the current speed");

            h.Deliver();
            Assert.AreEqual(string.Format(TimeControlPresenter.RunningLabelFormat, 2), h.View.StatusTextForTest);
            Assert.IsTrue(h.View.IsSpeedActiveForTest(2));

            // Pause resumes from a DECISION pause too (same rule as the button).
            RaiseDecisionEvent();
            h.View.InvokePauseForTest();
            Assert.IsFalse(_time.IsPaused, "Pause resumes regardless of which reason paused");
            Assert.IsTrue(_time.HasPendingDecision);

            h.Dispose();
        }

        [Test]
        public void SpaceToggle_BehavesLikeThePauseButton()
        {
            var h = new Harness(_time);
            _time.SetSpeed(3);

            h.View.InvokeToggleForTest();
            Assert.IsTrue(_time.IsUserPaused, "Space pauses like the Pause button");

            h.View.InvokeToggleForTest();
            Assert.IsFalse(_time.IsPaused, "Space resumes like the Pause button");
            Assert.AreEqual(3, _time.Speed, "the resume uses the last speed");

            h.Deliver();
            Assert.AreEqual(string.Format(TimeControlPresenter.RunningLabelFormat, 3), h.View.StatusTextForTest);

            h.Dispose();
        }

        [Test]
        public void EveryEffectiveClick_PublishesOneTrigger_PerEffectiveChange()
        {
            var h = new Harness(_time);
            int count = _speedMessages.Messages.Count;

            h.View.InvokePauseForTest();                       // pause
            Assert.AreEqual(count + 1, _speedMessages.Messages.Count);

            h.View.InvokePauseForTest();                       // resume
            Assert.AreEqual(count + 2, _speedMessages.Messages.Count);

            h.View.InvokeSpeedForTest(2);                      // speed only
            Assert.AreEqual(count + 3, _speedMessages.Messages.Count);

            h.View.InvokeSpeedForTest(2);                      // no-op
            Assert.AreEqual(count + 3, _speedMessages.Messages.Count, "an ineffective click publishes nothing");

            h.View.InvokePauseForTest();                       // pause at 2x
            Assert.AreEqual(count + 4, _speedMessages.Messages.Count);

            h.View.InvokeSpeedForTest(3);                      // speed + resume = two effective changes
            Assert.AreEqual(count + 6, _speedMessages.Messages.Count);

            Assert.AreEqual(3, _time.Speed);
            Assert.IsFalse(_time.IsPaused);

            h.Dispose();
        }

        [Test]
        public void Presenter_Dispose_StopsListeningToTheTrigger()
        {
            var h = new Harness(_time);
            h.View.InvokePauseForTest();
            h.Deliver();
            Assert.AreEqual(TimeControlPresenter.PausedByPlayerLabel, h.View.StatusTextForTest);

            h.Presenter.Dispose();
            Assert.IsNull(h.Subscriber.Handler, "Dispose releases the trigger subscription");

            _time.ResumeByPlayer();
            h.Deliver(); // no handler left - nothing to refresh
            Assert.AreEqual(TimeControlPresenter.PausedByPlayerLabel, h.View.StatusTextForTest,
                            "a disposed presenter no longer refreshes");

            h.Dispose();
        }

        // ------------------------------------------------ helpers

        private static TimeControlView BuildView()
        {
            var root = new GameObject("TimeControlTestRoot", typeof(RectTransform));
            root.SetActive(false);
            var view = root.AddComponent<TimeControlView>();

            Button MakeButton(string name)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(root.transform, false);
                var img = go.GetComponent<Image>();
                var btn = go.GetComponent<Button>();
                btn.targetGraphic = img;
                return btn;
            }

            var pause = MakeButton("PauseButton");
            var s1 = MakeButton("Speed1Button");
            var s2 = MakeButton("Speed2Button");
            var s3 = MakeButton("Speed3Button");

            var statusGo = new GameObject("StatusText", typeof(RectTransform), typeof(TextMeshProUGUI));
            statusGo.transform.SetParent(root.transform, false);

            var so = new SerializedObject(view);
            so.FindProperty("pauseButton").objectReferenceValue = pause;
            so.FindProperty("speed1Button").objectReferenceValue = s1;
            so.FindProperty("speed2Button").objectReferenceValue = s2;
            so.FindProperty("speed3Button").objectReferenceValue = s3;
            so.FindProperty("statusText").objectReferenceValue = statusGo.GetComponent<TextMeshProUGUI>();
            so.ApplyModifiedPropertiesWithoutUndo();

            root.SetActive(true);
            view.Show();
            return view;
        }

        /// <summary>View + presenter bound together; the fake subscriber delivers the refresh
        /// trigger by hand, so a test can prove the trigger is the ONLY refresh cue.</summary>
        private sealed class Harness : IDisposable
        {
            private readonly TimeSystem _time;
            public readonly TimeControlView View;
            public readonly FakeSubscriber Subscriber = new FakeSubscriber();
            public readonly TimeControlPresenter Presenter;

            public Harness(TimeSystem time)
            {
                _time = time;
                View = BuildView();
                Presenter = new TimeControlPresenter(time, Subscriber);
                Presenter.Bind(View);
            }

            /// <summary>Mirrors production: TimeSystem publishes this synchronously on every
            /// effective change.</summary>
            public void Deliver() => Subscriber.Handler?.Handle(
                new TimeSpeedChangedMessage { Speed = _time.Speed, Paused = _time.IsPaused });

            public void Dispose()
            {
                Presenter.Dispose();
                UnityEngine.Object.DestroyImmediate(View.gameObject);
            }
        }

        private sealed class TimeSpeedChangedMessageBuffer
        {
            public readonly BufferPublisher<TimeSpeedChangedMessage> Publisher = new BufferPublisher<TimeSpeedChangedMessage>();
        }

        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public readonly List<T> Messages = new List<T>();
            public void Publish(T message) => Messages.Add(message);
        }

        private sealed class FakeSubscriber : ISubscriber<TimeSpeedChangedMessage>
        {
            public IMessageHandler<TimeSpeedChangedMessage> Handler;

            public IDisposable Subscribe(IMessageHandler<TimeSpeedChangedMessage> handler,
                                         params MessageHandlerFilter<TimeSpeedChangedMessage>[] filters)
            {
                Handler = handler;
                return new Subscription(this);
            }

            /// <summary>Mirrors MessagePipe: disposing the subscription stops delivery.</summary>
            private sealed class Subscription : IDisposable
            {
                private readonly FakeSubscriber _owner;
                public Subscription(FakeSubscriber owner) { _owner = owner; }
                public void Dispose() { _owner.Handler = null; }
            }
        }
    }
}
