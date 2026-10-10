using System;
using System.Collections.Generic;
using MessagePipe;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using Xianxia.Sect;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;
using Xianxia.Sect.UI;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// E3 — the player can always see, reopen, hide and answer a pending event.
    /// Presenter-level tests wrap a real (minimal) view hierarchy wired through
    /// SerializedObject — the same pattern as TaskAssignmentPresenterTests — plus a
    /// system-level test that goes through the REAL generated prefabs and the REAL
    /// MainPanelCatalog, and prefab-invariant checks on the generated artifacts.
    /// </summary>
    public class EventPopupLiteTests
    {
        private const string EventId = "bandit_raid_001";
        private const string PopupPrefabPath = "Assets/Prefabs/EventPopupPrefab.prefab";
        private const string ChipPrefabPath = "Assets/Prefabs/UI/EventChipPanel.prefab";
        private const string CatalogPath = "Assets/Panel Catalog/MainPanelCatalog.asset";
        private const string ThaiFontPath = "Assets/Resources/Fonts/THSarabunPSK SDF.asset";
        private static readonly string[] ChoiceIds = { "send_inner_disciples", "pay_tribute" };

        private TimeSystem _time;
        private SectStateProvider _provider;
        private FakeSubscriber<DecisionExecutedMessage> _executedIn;   // both the presenter's bus and the decision sink
        private DecisionExecutor _executor;
        private EventPopupPresenter _presenter;
        private EventPopupView _view;
        private readonly List<GameObject> _tempRoots = new List<GameObject>();
        private readonly List<Action> _tempCleanup = new List<Action>();
        private int _closeCount;

        [SetUp]
        public void SetUp()
        {
            _executedIn = new FakeSubscriber<DecisionExecutedMessage>();
            _time = new TimeSystem(
                new BufferPublisher<TimeSpeedChangedMessage>(),
                new BufferPublisher<WorldEventTriggeredMessage>(),
                new TimeRuntimeConfig { AutoPauseOnDecisionEvent = true });
            _provider = NewProvider();
            // The executor publishes into the same fake the presenter subscribes to, which is
            // exactly the real wiring (in-process MessagePipe bus): the presenter really sees
            // its own decision message and the bridge/external path is the same code path.
            _executor = new DecisionExecutor(_provider, _time, new FakePublisher<DecisionExecutedMessage>(_executedIn));
            _closeCount = 0;

            _presenter = new EventPopupPresenter(_executor, _time, _executedIn);
            _view = BuildView("EventPopupTestRoot", _presenter);
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

            // DiscipleModalScope.IsOpen is a static counter that only OnDisable decrements.
            // If a test fails between opening and closing its modal, drain it here so one
            // broken test cannot cascade modal-blocks into the rest of the suite.
            for (int guard = 0; DiscipleModalScope.IsOpen && guard < 8; guard++)
            {
                var drain = new GameObject("EventPopupTest_ModalDrain", typeof(RectTransform));
                drain.AddComponent<DiscipleModalScope>();
                UnityEngine.Object.DestroyImmediate(drain);
            }
        }

        // ---------------------------------------------------------------- helpers

        private static GameObject TextChild(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>Minimal real popup hierarchy (title/description/status/choices/hide) with
        /// the same wiring as the prefab, then Bind — exactly what UIService.Open does.</summary>
        private EventPopupView BuildView(string rootName, EventPopupPresenter presenter)
        {
            var root = new GameObject(rootName, typeof(RectTransform));
            root.SetActive(false); // defer Awake until the refs are wired
            _tempRoots.Add(root);

            var view = root.AddComponent<EventPopupView>();
            var titleGo = TextChild(root.transform, "TitleText");
            var descGo = TextChild(root.transform, "DescriptionText");
            var statusGo = TextChild(root.transform, "StatusText");

            var choicesGo = new GameObject("ChoicesRoot", typeof(RectTransform), typeof(VerticalLayoutGroup));
            choicesGo.transform.SetParent(root.transform, false);

            var templateGo = new GameObject("ChoiceButtonTemplate", typeof(RectTransform), typeof(Image), typeof(Button));
            templateGo.transform.SetParent(root.transform, false);
            var templateButton = templateGo.GetComponent<Button>();
            templateButton.targetGraphic = templateGo.GetComponent<Image>();
            TextChild(templateGo.transform, "Label");
            templateGo.SetActive(false);   // the real prefab stores the template inactive

            var hideGo = new GameObject("HideButton", typeof(RectTransform), typeof(Image), typeof(Button));
            hideGo.transform.SetParent(root.transform, false);
            hideGo.GetComponent<Button>().targetGraphic = hideGo.GetComponent<Image>();

            var so = new SerializedObject(view);
            so.FindProperty("titleText").objectReferenceValue = titleGo.GetComponent<TMP_Text>();
            so.FindProperty("descriptionText").objectReferenceValue = descGo.GetComponent<TMP_Text>();
            so.FindProperty("statusText").objectReferenceValue = statusGo.GetComponent<TMP_Text>();
            so.FindProperty("choicesRoot").objectReferenceValue = (RectTransform)choicesGo.transform;
            so.FindProperty("choiceButtonPrefab").objectReferenceValue = templateButton;
            so.FindProperty("hideButton").objectReferenceValue = hideGo.GetComponent<Button>();
            so.ApplyModifiedPropertiesWithoutUndo();

            root.SetActive(true);
            view.Show();               // production path: UIService.Open calls Show()
            presenter.Bind(view);
            return view;
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

        private static int Raw(SectStateProvider provider, string id)
        {
            int value;
            return provider.BuildSectEconomyState().Stockpile.RawResources.TryGetValue(id, out value) ? value : 0;
        }

        private static List<EventChoiceInfo> ChoiceInfos(params string[] ids)
        {
            var list = new List<EventChoiceInfo>();
            foreach (var id in ids) list.Add(new EventChoiceInfo { ChoiceId = id, Label = id });
            return list;
        }

        private void RaisePendingEvent(params string[] choiceIds)
        {
            _time.RaiseWorldEvent(EventId, "Raiders at the gate.", true, ChoiceInfos(choiceIds));
        }

        /// <summary>Opens the popup the way WorldEventUISystem does (ids taken from the authoritative state).</summary>
        private void OpenFromPendingState(List<EventChoiceInfo> overrideChoices = null)
        {
            _presenter.OnOpen(new EventPopupOpenArgs
            {
                EventId = _time.PendingEventId,
                Description = _time.PendingDescription,
                Choices = overrideChoices ?? new List<EventChoiceInfo>(_time.PendingChoices),
                CloseCallback = () => _closeCount++,
            });
        }

        // ------------------------------------------------- 1. hide + Escape (item 3/9)

        [Test]
        public void Hide_KeepsEventPending_AndChangesNoGameState()
        {
            RaisePendingEvent(ChoiceIds);
            OpenFromPendingState();
            int provisionsBefore = Raw(_provider, "provisions");

            _view.InvokeHideForTest();          // the real "ซ่อน" button
            Assert.AreEqual(1, _closeCount, "the popup asks its opener to close");
            Assert.IsTrue(_time.HasPendingDecision, "hiding must not clear the pending event");
            Assert.IsTrue(_time.IsPendingDecisionPaused, "hiding must not resume time");

            _view.PressEscapeForTest();         // Escape does the same
            Assert.AreEqual(2, _closeCount, "Escape hides exactly like the button");
            Assert.AreEqual(EventId, _time.PendingEventId, "the same event is still pending");
            Assert.AreEqual(provisionsBefore, Raw(_provider, "provisions"), "hiding changes no game state");
        }

        // ------------------------------------------------- 2. one click = one decision (item 6/9)

        [Test]
        public void ChoiceClick_AppliesExactlyOneDecision_AndDisablesChoicesWhileProcessing()
        {
            RaisePendingEvent(ChoiceIds);
            OpenFromPendingState();
            int provisionsBefore = Raw(_provider, "provisions");

            // Probe the mid-flight state from inside the decision itself: the executor
            // publishes DecisionExecutedMessage before Execute returns.
            bool? interactableDuringExecute = null;
            _executedIn.AlsoHandle(_ => interactableDuringExecute = _view.IsChoiceInteractableForTest("send_inner_disciples"));

            _view.InvokeChoiceForTest("send_inner_disciples");

            Assert.AreEqual(provisionsBefore - 20, Raw(_provider, "provisions"),
                            "bandit_raid_001's consequence applied exactly once");
            Assert.IsFalse(_time.HasPendingDecision, "the valid decision cleared the pending state");
            Assert.AreEqual(1, _closeCount, "the popup closes on success");
            Assert.IsTrue(interactableDuringExecute.HasValue, "the decision published its message");
            Assert.IsFalse(interactableDuringExecute.Value,
                           "every choice is disabled while the decision is being processed");
            Assert.AreEqual(string.Empty, _view.StatusForTest, "a successful decision leaves no error text");
        }

        // ------------------------------------------------- 3/4. rejection handling (item 6/9)

        [Test]
        public void RejectedChoice_ShowsReasonInline_AndKeepsChoicesEnabledWhileStillPending()
        {
            RaisePendingEvent(ChoiceIds);

            // The popup was opened with a choice list that does not match the pending
            // event's choices: exactly the stale-content case the executor must reject.
            OpenFromPendingState(overrideChoices: ChoiceInfos("send_inner_disciples", "bribe_the_bandits"));
            int provisionsBefore = Raw(_provider, "provisions");

            _view.InvokeChoiceForTest("bribe_the_bandits");

            Assert.IsTrue(_view.StatusForTest.Contains("bribe_the_bandits"),
                          "the rejection reason is shown inline: " + _view.StatusForTest);
            Assert.IsTrue(_time.HasPendingDecision, "a rejected decision leaves the event pending");
            Assert.AreEqual(provisionsBefore, Raw(_provider, "provisions"), "and changes no state");
            Assert.AreEqual(0, _closeCount, "a rejected decision must not close the popup");
            Assert.IsTrue(_view.IsChoiceInteractableForTest("send_inner_disciples"),
                          "choices re-enable while the event is still pending");
        }

        [Test]
        public void RejectedChoice_WhenNothingPending_ShowsReason_AndLeavesChoicesDisabled()
        {
            RaisePendingEvent(ChoiceIds);
            OpenFromPendingState();

            // The AI GM resolves it elsewhere (bridge path → the same DecisionExecutor).
            var external = _executor.Execute(EventId, "pay_tribute");
            Assert.IsTrue(external.Accepted, external.Reason);

            // A click now can only be stale. Drive it anyway (the external resolve above
            // also delivered its DecisionExecutedMessage to the presenter's own channel).
            _view.InvokeChoiceForTest("send_inner_disciples");

            Assert.IsFalse(string.IsNullOrEmpty(_view.StatusForTest), "the stale click explains itself");
            Assert.IsFalse(_view.IsChoiceInteractableForTest("send_inner_disciples"),
                           "with nothing pending there is nothing left to retry - choices stay disabled");
        }

        // ------------------------------------------------- 5. resolved elsewhere (item 6/9)

        [Test]
        public void ExternalResolution_ShowsResolvedNote_ThenClosesThePopup()
        {
            RaisePendingEvent(ChoiceIds);
            OpenFromPendingState();

            // The bridge / AI GM path: DecisionLogger calls this same executor.
            var external = _executor.Execute(EventId, "pay_tribute");
            Assert.IsTrue(external.Accepted, external.Reason);

            Assert.AreEqual(EventPopupView.ResolvedElsewhereText, _view.StatusForTest,
                            "an event resolved elsewhere says so");
            Assert.IsTrue(_view.IsShowingResolvedNoteForTest);
            Assert.IsFalse(_view.IsChoiceInteractableForTest("send_inner_disciples"),
                           "choices are locked while the note is up");
            Assert.AreEqual(0, _closeCount, "the popup does not vanish instantly - the player gets to read it");

            _view.AdvanceForTest(EventPopupPresenter.ResolvedNoteSeconds + 0.1f);

            Assert.IsFalse(_view.VisibleForTest, "after the note the popup closes");
            Assert.AreEqual(1, _closeCount, "the opener is told so it can drop its handle");
        }

        [Test]
        public void OwnChoiceClick_DoesNotTripTheResolvedElsewhereNote()
        {
            RaisePendingEvent(ChoiceIds);
            OpenFromPendingState();

            _view.InvokeChoiceForTest("pay_tribute");

            Assert.AreEqual(string.Empty, _view.StatusForTest, "our own accepted click is not 'resolved elsewhere'");
            Assert.IsFalse(_view.IsShowingResolvedNoteForTest);
        }

        // ------------------------------------------------- 7. lifecycle (item 7/9)

        [Test]
        public void CloseThenOpenAgain_DisposesOldSubscription_AndAppliesTheNextDecisionOnce()
        {
            RaisePendingEvent(ChoiceIds);
            OpenFromPendingState();
            Assert.AreEqual(1, _executedIn.ActiveSubscriptionCount);

            // UIService.Close: presenter.Dispose() then the panel is destroyed.
            _presenter.Dispose();
            Assert.AreEqual(0, _executedIn.ActiveSubscriptionCount, "closing disposes the subscription");

            // Reopening (a fresh panel instance) must not accumulate subscriptions.
            var secondPresenter = new EventPopupPresenter(_executor, _time, _executedIn);
            var secondView = BuildView("EventPopupTestRoot2", secondPresenter);
            secondPresenter.OnOpen(new EventPopupOpenArgs
            {
                EventId = _time.PendingEventId,
                Description = _time.PendingDescription,
                Choices = new List<EventChoiceInfo>(_time.PendingChoices),
                CloseCallback = () => _closeCount++,
            });
            Assert.AreEqual(1, _executedIn.ActiveSubscriptionCount, "exactly one live subscription, never two");

            int provisionsBefore = Raw(_provider, "provisions");
            secondView.InvokeChoiceForTest("pay_tribute");

            Assert.AreEqual(provisionsBefore - 20, Raw(_provider, "provisions"),
                            "bandit_raid_001's consequence (-20 provisions) applied exactly once");
            Assert.AreEqual(1, _closeCount, "one click → one decision → one close");
            Assert.IsFalse(_time.HasPendingDecision);

            secondPresenter.Dispose();
            Assert.AreEqual(0, _executedIn.ActiveSubscriptionCount, "and closing the second one disposes too");
        }

        // ------------------------------------------------- 4. chip rules (item 4/9)

        [Test]
        public void Chip_ShowsWhilePendingAndPopupHidden_HidesWhenNothingIsPending()
        {
            var chipView = BuildChip();
            var chipPresenter = new WorldEventChipPresenter(_time);
            chipPresenter.Bind(chipView);
            chipPresenter.OnOpen(new WorldEventChipArgs { IsPopupVisible = () => false, Clicked = () => { } });
            _tempCleanup.Add(chipPresenter.Dispose);

            chipPresenter.Refresh();
            Assert.IsFalse(chipView.ShownForTest, "no pending event → no chip");

            RaisePendingEvent(ChoiceIds);
            chipPresenter.Refresh();
            Assert.IsTrue(chipView.ShownForTest, "pending + popup hidden → the chip is the visible signal");
            Assert.AreEqual(WorldEventChipPresenter.PendingLabel, chipView.LabelForTest);

            // A new event flashes; the flash is brief and does not repeat on refresh.
            chipView.AdvanceForTest(WorldEventChipPresenter.HighlightSeconds + 0.1f);
            chipPresenter.Refresh();
            Assert.IsFalse(chipView.IsHighlightedForTest, "an unchanged event does not re-flash");

            _executor.Execute(EventId, "pay_tribute");
            chipPresenter.Refresh();
            Assert.IsFalse(chipView.ShownForTest, "the resolved event removes the chip");
        }

        [Test]
        public void Chip_ShowsWhenPendingWhileThePopupIsVisibleOnlyIfAModalBlocksIt()
        {
            var chipView = BuildChip();
            var chipPresenter = new WorldEventChipPresenter(_time);
            chipPresenter.Bind(chipView);
            chipPresenter.OnOpen(new WorldEventChipArgs { IsPopupVisible = () => true, Clicked = () => { } });
            _tempCleanup.Add(chipPresenter.Dispose);

            RaisePendingEvent(ChoiceIds);
            chipPresenter.Refresh();
            Assert.IsFalse(chipView.ShownForTest, "the popup itself is the signal, so the chip stays hidden");

            // A blocking modal opens (OnEnable only fires in the editor for [ExecuteAlways],
            // so the counter is driven explicitly) — the popup now yields to it.
            DiscipleModalScope.PushBlocker();
            _tempCleanup.Add(DiscipleModalScope.PopBlocker);
            chipPresenter.Refresh();

            Assert.IsTrue(chipView.ShownForTest, "while a modal blocks the popup the chip carries the signal");
            Assert.IsTrue(chipView.IsHighlightedForTest,
                          "an event that arrived while the popup was up flashes once it becomes the signal");
        }

        [Test]
        public void Chip_Rule_CoversPopupVisibleAndModalCases()
        {
            Assert.IsTrue(WorldEventChipPresenter.ShouldShow(true, popupVisible: false, modalOpen: false),
                          "pending + hidden");
            Assert.IsTrue(WorldEventChipPresenter.ShouldShow(true, popupVisible: true, modalOpen: true),
                          "pending while a modal blocks the popup");
            Assert.IsFalse(WorldEventChipPresenter.ShouldShow(true, popupVisible: true, modalOpen: false),
                           "the popup itself is the signal");
            Assert.IsFalse(WorldEventChipPresenter.ShouldShow(false, popupVisible: false, modalOpen: true),
                           "nothing pending → nothing shown");
        }

        // ------------------------------------------------- 6. modal interplay (item 5/9)

        [Test]
        public void Popup_IsNotForcedOverAModal_AndOpensWhenTheModalCloses()
        {
            var uiRootGo = new GameObject("EventPopupTest_UIRoot", typeof(RectTransform));
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

            var uiService = container.Resolve<UIService>();
            var subscriber = container.Resolve<ISubscriber<WorldEventTriggeredMessage>>();
            var triggerPublisher = container.Resolve<IPublisher<WorldEventTriggeredMessage>>();
            var uiSystem = new WorldEventUISystem(subscriber, uiService, _time);
            _tempCleanup.Add(uiSystem.Dispose);

            // A blocking modal is already open when the event arrives. (OnEnable only fires
            // in the editor for [ExecuteAlways] components, so the shared counter is driven
            // explicitly rather than by instantiating a modal.)
            DiscipleModalScope.PushBlocker();
            bool modalOpen = true;
            try
            {
                Assert.IsTrue(DiscipleModalScope.IsOpen);

                RaisePendingEvent(ChoiceIds);
                uiSystem.Start();

                Assert.IsNull(FindChild<EventPopupView>(uiRoot.transform),
                              "the popup is never forced over an open modal");
                var chip = FindChild<WorldEventChipView>(uiRoot.transform);
                Assert.IsNotNull(chip, "the chip is opened at Start");
                Assert.IsTrue(chip.isActiveAndEnabled, "never wait silently: the chip is visible while blocked");

                // The modal closes → the next trigger (production: the per-frame canvas
                // hook) opens the popup.
                DiscipleModalScope.PopBlocker();
                modalOpen = false;
                Assert.IsFalse(DiscipleModalScope.IsOpen);
                triggerPublisher.Publish(new WorldEventTriggeredMessage
                {
                    EventId = EventId,
                    RequiresDecision = true,
                    Description = "Raiders at the gate.",
                });

                var popup = FindChild<EventPopupView>(uiRoot.transform);
                Assert.IsNotNull(popup, "the popup opens as soon as the modal closes");
                Assert.IsTrue(popup.isActiveAndEnabled);
                Assert.IsFalse(chip.isActiveAndEnabled, "the popup takes over as the visible signal");

                // And it is built from the authoritative pending state.
                Assert.AreEqual(EventId, popup.TitleForTest);
                Assert.AreEqual(2, popup.ChoiceCountForTest, "one visible button per choice");
                Assert.IsTrue(popup.IsChoiceInteractableForTest("pay_tribute"), "the clones are active and clickable");
            }
            finally
            {
                if (modalOpen) DiscipleModalScope.PopBlocker();
            }
        }

        [Test]
        public void LateStartedUi_ShowsTheChipForAPendingEvent_WithoutForcingThePopupOnTopOfAModal()
        {
            DiscipleModalScope.PushBlocker();
            _tempCleanup.Add(DiscipleModalScope.PopBlocker);

            var uiRootGo = new GameObject("EventPopupTest_UIRoot3", typeof(RectTransform));
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

            var uiSystem = new WorldEventUISystem(
                container.Resolve<ISubscriber<WorldEventTriggeredMessage>>(),
                container.Resolve<UIService>(),
                _time);
            _tempCleanup.Add(uiSystem.Dispose);

            // The event exists BEFORE any UI does (that is what the grace period replaced).
            RaisePendingEvent(ChoiceIds);
            uiSystem.Start();

            var chip = FindChild<WorldEventChipView>(uiRoot.transform);
            Assert.IsNotNull(chip, "the chip exists for a late-started UI");
            Assert.IsTrue(chip.isActiveAndEnabled, "and it reports the pending event it found in authoritative state");
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

        // ------------------------------------------------- chip view harness

        private WorldEventChipView BuildChip()
        {
            var root = new GameObject("EventChipTestRoot", typeof(RectTransform), typeof(Image), typeof(Button));
            root.SetActive(false);
            _tempRoots.Add(root);

            var view = root.AddComponent<WorldEventChipView>();
            var label = TextChild(root.transform, "Label");
            var button = root.GetComponent<Button>();
            button.targetGraphic = root.GetComponent<Image>();

            var so = new SerializedObject(view);
            so.FindProperty("chipButton").objectReferenceValue = button;
            so.FindProperty("background").objectReferenceValue = root.GetComponent<Image>();
            so.FindProperty("label").objectReferenceValue = label.GetComponent<TMP_Text>();
            so.ApplyModifiedPropertiesWithoutUndo();

            root.SetActive(true);
            view.Show();
            return view;
        }

        // ------------------------------------------------- prefab invariants (item 8)

        [Test]
        public void Prefab_Popup_IsReadable_AndHasNoContentSizeFitter_AndItsChoiceTemplateWorks()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<UIPanelCatalog>(CatalogPath);
            var prefab = catalog.Get("EventPopup").Prefab;

            Assert.IsNotNull(prefab);
            Assert.IsNull(prefab.GetComponent<ContentSizeFitter>(), "no ContentSizeFitter on the popup root (spec §2)");

            var dim = prefab.transform.Find("Dim");
            Assert.IsNotNull(dim, "a dim sits behind the panel");
            var dimImage = dim.GetComponent<Image>();
            Assert.IsNotNull(dimImage);
            Assert.IsFalse(dimImage.raycastTarget, "the dim must not block clicks on the game behind it (spec §2)");
            Assert.Less(dimImage.color.a, 0.6f, "the dim is light, not a full blackout");

            var panel = prefab.transform.Find("Panel");
            Assert.IsNotNull(panel, "the framed panel exists");
            var panelImage = panel.GetComponent<Image>();
            Assert.IsNotNull(panelImage);
            Assert.AreEqual(1f, panelImage.color.a, "the panel has a solid background");
            Assert.IsNotNull(panel.transform.Find("TitleStrip"), "and a darker title strip");

            var view = prefab.GetComponent<EventPopupView>();
            var so = new SerializedObject(view);
            foreach (var field in new[] { "titleText", "descriptionText", "statusText", "choicesRoot", "choiceButtonPrefab", "hideButton" })
            {
                Assert.IsNotNull(so.FindProperty(field).objectReferenceValue, "EventPopupView." + field + " must be wired");
            }

            var title = (TMP_Text)so.FindProperty("titleText").objectReferenceValue;
            var description = (TMP_Text)so.FindProperty("descriptionText").objectReferenceValue;
            Assert.AreEqual(ThaiFontPath, AssetDatabase.GetAssetPath(title.font), "title uses the project's Thai font");
            Assert.AreEqual(ThaiFontPath, AssetDatabase.GetAssetPath(description.font), "description uses the same font");
            Assert.Greater(description.fontSize, title.fontSize, "the description is the larger text (spec §2)");

            var template = (Button)so.FindProperty("choiceButtonPrefab").objectReferenceValue;
            Assert.IsFalse(template.gameObject.activeSelf, "the template stays inactive out of the layout");
            Assert.IsNotNull(template.targetGraphic, "the choice button needs a target graphic or it is invisible");
            Assert.IsNotNull(template.GetComponent<Image>());
            Assert.GreaterOrEqual(template.GetComponent<LayoutElement>().minHeight, 44f, "a minimum row height");
            var templateLabel = template.GetComponentInChildren<TMP_Text>();
            Assert.AreEqual(ThaiFontPath, AssetDatabase.GetAssetPath(templateLabel.font), "choice labels use the Thai font");
            Assert.AreNotEqual(template.colors.normalColor, template.colors.pressedColor,
                               "hover/pressed tints differ from the normal colour");
        }

        [Test]
        public void Prefab_Chip_IsWired_AndCatalogListsBothPanels()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<UIPanelCatalog>(CatalogPath);
            Assert.AreEqual(UIPresenterKind.EventPopup, catalog.Get("EventPopup").PresenterKind);

            var entry = catalog.Get("EventChip");
            Assert.AreEqual(UIPresenterKind.EventChip, entry.PresenterKind, "the chip is a first-class panel");
            Assert.IsNotNull(entry.Prefab);
            Assert.AreEqual(ChipPrefabPath, AssetDatabase.GetAssetPath(entry.Prefab));

            var view = entry.Prefab.GetComponent<WorldEventChipView>();
            Assert.IsNotNull(view, "the chip prefab carries the chip view");
            var so = new SerializedObject(view);
            var button = (Button)so.FindProperty("chipButton").objectReferenceValue;
            var background = (Image)so.FindProperty("background").objectReferenceValue;
            var label = (TMP_Text)so.FindProperty("label").objectReferenceValue;
            Assert.IsNotNull(button);
            Assert.IsNotNull(background);
            Assert.AreEqual(background, button.targetGraphic, "the plate is the button's target graphic");
            Assert.IsNotNull(label);
            Assert.AreEqual(WorldEventChipPresenter.PendingLabel, label.text);
            Assert.AreEqual(ThaiFontPath, AssetDatabase.GetAssetPath(label.font), "the chip label uses the Thai font");

            // The popup prefab must have kept its asset path (the catalog guid reference).
            Assert.AreEqual(PopupPrefabPath, AssetDatabase.GetAssetPath(catalog.Get("EventPopup").Prefab));
        }

        // ------------------------------------------------- fakes

        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public readonly List<T> Messages = new List<T>();
            public void Publish(T message) => Messages.Add(message);
        }

        /// <summary>Publishes through a fake subscriber so the test can observe the message
        /// and the presenter receives it too — the one-bus shape of the real container.</summary>
        private sealed class FakePublisher<T> : IPublisher<T>
        {
            private readonly FakeSubscriber<T> _subscriber;
            public FakePublisher(FakeSubscriber<T> subscriber) => _subscriber = subscriber;
            public void Publish(T message) => _subscriber.Publish(message);
        }

        /// <summary>
        /// MessagePipe's real shape — a handler list so a test can both drive the
        /// production handler and add its own probe, and assert disposal (item 7).
        /// </summary>
        private sealed class FakeSubscriber<T> : ISubscriber<T>
        {
            private readonly List<IMessageHandler<T>> _handlers = new List<IMessageHandler<T>>();

            public int ActiveSubscriptionCount => _handlers.Count;

            public IDisposable Subscribe(IMessageHandler<T> handler, params MessageHandlerFilter<T>[] filters)
            {
                _handlers.Add(handler);
                return new Stub(this, handler);
            }

            public void Publish(T message)
            {
                for (int i = _handlers.Count - 1; i >= 0; i--) _handlers[i].Handle(message);
            }

            /// <summary>Extra test-local probe handler (never removed - assertions only).</summary>
            public void AlsoHandle(Action<T> probe) => _handlers.Add(new Probe(probe));

            private sealed class Probe : IMessageHandler<T>
            {
                private readonly Action<T> _action;
                public Probe(Action<T> action) => _action = action;
                public void Handle(T message) => _action(message);
            }

            private sealed class Stub : IDisposable
            {
                private readonly FakeSubscriber<T> _owner;
                private readonly IMessageHandler<T> _handler;
                public Stub(FakeSubscriber<T> owner, IMessageHandler<T> handler) { _owner = owner; _handler = handler; }
                public void Dispose() => _owner._handlers.Remove(_handler);
            }
        }
    }
}
