using System;
using System.Collections.Generic;
using MessagePipe;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Xianxia.Sect;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;
using Xianxia.Sect.UI;
using Xianxia.Sect.Visual;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// P3 — Task Assignment presenter acceptance (ผ่าน view จริง + UGUI hierarchy เล็ก ๆ,
    /// แพทเทิร์นเดียวกับ DiscipleListPresenterTests — SerializedObject wiring, ไม่ใช้ reflection):
    ///   - OnOpen สร้าง 1 แถว/ศิษย์ + baseline/ownership/status ครบ
    ///   - Confirm ยิง TryAssignTask เฉพาะแถวที่เปลี่ยน (§5 changed-only)
    ///   - multi-row confirm ไม่ atomic: แถวสำเร็จ advance baseline, แถวล้มเหลวเก็บ draft + per-row error
    ///   - Cancel ไม่แก้ state เลย (§5)
    ///   - DiscipleTaskChangedMessage จากภายนอกบนแถว dirty = conflict, บล็อก commit จนกว่าจะเลือกใหม่ (§6)
    ///   - Open → Dispose → Bind → Open = subscription ตัวเดียวเสมอ (§8 ไม่ leak)
    /// </summary>
    public class TaskAssignmentPresenterTests
    {
        private SectStateProvider _provider;
        private BufferPublisher<DiscipleTaskChangedMessage> _taskChangedOut;
        private RecordingSubscriber _taskChangedIn;
        private TaskAssignmentPresenter _presenter;
        private TaskAssignmentView _view;
        private GameObject _rootGo;

        [SetUp]
        public void SetUp()
        {
            _taskChangedOut = new BufferPublisher<DiscipleTaskChangedMessage>();
            _taskChangedIn = new RecordingSubscriber();

            _provider = new SectStateProvider(
                new BufferPublisher<DiscipleRecruitedMessage>(),
                new BufferPublisher<SectResourceChangedMessage>(),
                new BufferPublisher<AvatarEquipmentChangedMessage>(),
                new BufferPublisher<DiscipleChibiBackendChangedMessage>(),
                new AvatarPartPool(),
                VisualRuntimeConfig.Instance,
                new DefaultEntitlementProvider(),
                new BuildingDefPool(),
                new BufferPublisher<BuildingPlacedMessage>(),
                _taskChangedOut);

            // The start state is building-free, so committing gathering_herb below needs
            // its own herb_plot (state presence is all the requirement gate checks).
            string placeReason;
            PlacedBuildingState placedPlot;
            Assert.IsTrue(_provider.TryPlaceBuilding("herb_plot", 0, 0, 0, new BuildingGrid(10, 10),
                                                     out placeReason, out placedPlot), placeReason);

            _presenter = new TaskAssignmentPresenter(_provider, _taskChangedIn);

            // ── minimal real view hierarchy (rowRoot + buttons + banner) ──
            _rootGo = new GameObject("TaskAssignmentTestRoot", typeof(RectTransform));
            _rootGo.SetActive(false); // defer Awake จน wire refs เสร็จ
            _view = _rootGo.AddComponent<TaskAssignmentView>();

            var rowRootGo = new GameObject("RowRoot", typeof(RectTransform));
            rowRootGo.transform.SetParent(_rootGo.transform, false);

            var closeGo = new GameObject("CloseButton", typeof(Image), typeof(Button));
            closeGo.transform.SetParent(_rootGo.transform, false);
            var confirmGo = new GameObject("ConfirmButton", typeof(Image), typeof(Button));
            confirmGo.transform.SetParent(_rootGo.transform, false);
            var cancelGo = new GameObject("CancelButton", typeof(Image), typeof(Button));
            cancelGo.transform.SetParent(_rootGo.transform, false);

            var bannerGo = new GameObject("ConflictBanner", typeof(TextMeshProUGUI));
            bannerGo.transform.SetParent(_rootGo.transform, false);

            var so = new SerializedObject(_view);
            so.FindProperty("rowRoot").objectReferenceValue = (RectTransform)rowRootGo.transform;
            so.FindProperty("closeButton").objectReferenceValue = closeGo.GetComponent<Button>();
            so.FindProperty("confirmButton").objectReferenceValue = confirmGo.GetComponent<Button>();
            so.FindProperty("cancelButton").objectReferenceValue = cancelGo.GetComponent<Button>();
            so.FindProperty("conflictBanner").objectReferenceValue = bannerGo.GetComponent<TextMeshProUGUI>();
            so.ApplyModifiedPropertiesWithoutUndo();

            _rootGo.SetActive(true);
            _view.Show(); // production path: UIService.Open เรียก Show() เสมอ
            _presenter.Bind(_view);
        }

        [TearDown]
        public void TearDown()
        {
            if (_rootGo != null) UnityEngine.Object.DestroyImmediate(_rootGo);
        }

        private SectEconomyState State => _provider.BuildSectEconomyState();

        private DiscipleState Find(string id)
        {
            foreach (var d in State.Disciples)
                if (d.DiscipleId == id) return d;
            return null;
        }

        private List<TaskAssignmentRowCell> Rows()
        {
            // live-row accessor (not hierarchy iteration): ClearRows' Destroy is
            // deferred in EditMode, so stale rows would still appear as children
            return new List<TaskAssignmentRowCell>(_view.LiveRowsForTest);
        }

        private TaskAssignmentRowCell RowOf(string discipleId)
        {
            foreach (var row in Rows())
                if (row.DiscipleIdForTest == discipleId) return row;
            return null;
        }

        private int OptionIndexOf(TaskAssignmentRowCell row, string taskId)
        {
            for (int i = 0; i < row.OptionsForTest.Count; i++)
                if (row.OptionsForTest[i] == taskId) return i;
            return -1;
        }

        private string DumpTasks()
        {
            var parts = new List<string>();
            foreach (var d in State.Disciples)
                parts.Add(d.DiscipleId + "=" + d.CurrentTask);
            return string.Join("|", parts);
        }

        // ---- rows / baseline / ownership ----

        [Test]
        public void OnOpen_BuildsOneRowPerDisciple_WithBaselineAndOwnership()
        {
            _presenter.OnOpen(null);

            var rows = Rows();
            Assert.AreEqual(State.Disciples.Count, rows.Count, "1 row per disciple");

            var d000 = RowOf("d000");
            Assert.IsNotNull(d000);
            Assert.AreEqual("meditation", d000.DropdownValueText, "d000 mock baseline");
            Assert.AreEqual("ศิษย์สำนัก", d000.OwnerLabelForTest, "Npc ownership label (render only — authority stays TryAssignTask)");
            Assert.IsFalse(_view.IsConfirmInteractableForTest, "no drafts yet — confirm disabled");
        }

        [Test]
        public void OnOpen_ShowsOwnerProtection_AndRemainingTime()
        {
            // P5B: bind a viewer owner through the real path — activity = now, so the
            // owner is inside the 10-minute protection window.
            string bindReason;
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d000", DiscipleOwnerType.Viewer,
                                                        "viewer_test_01", out bindReason), bindReason);

            _presenter.OnOpen(null);

            var label = RowOf("d000").OwnerLabelForTest;
            StringAssert.Contains("viewer: viewer_test_01", label);
            StringAssert.Contains("protected", label, "P5B must surface owner protection in the row");
            StringAssert.Contains("10:00", label, "remaining protection time is rendered (m:ss)");
        }

        [Test]
        public void OnOpen_ProtectedRow_DropdownDisabled_WithAuthorityReason()
        {
            // P5B: bind a viewer owner through the real path — activity = now, so the
            // owner is protected and SECT_MASTER may not change d000's task.
            string bindReason;
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d000", DiscipleOwnerType.Viewer,
                                                        "viewer_test_01", out bindReason), bindReason);

            _presenter.OnOpen(null);

            var protectedRow = RowOf("d000");
            Assert.IsFalse(protectedRow.IsTaskSelectableForTest,
                "the panel must BLOCK a protected row, not just label it");
            StringAssert.Contains("protected", protectedRow.StatusTextForTest,
                "the row shows the authority's reason");

            Assert.IsTrue(RowOf("d001").IsTaskSelectableForTest,
                "an unowned NPC row stays editable");
        }

        [Test]
        public void BlockedRow_ProgrammaticSelection_CreatesNoDraft()
        {
            string bindReason;
            Assert.IsTrue(_provider.TrySetDiscipleOwner("d000", DiscipleOwnerType.Viewer,
                                                        "viewer_test_01", out bindReason), bindReason);
            _presenter.OnOpen(null);

            var row = RowOf("d000");
            row.SelectTask(OptionIndexOf(row, "gathering_wood"));

            Assert.AreEqual("meditation", row.DropdownValueText, "selection snapped back to the baseline");
            Assert.IsFalse(_view.IsConfirmInteractableForTest, "a blocked row must not create a draft");
            Assert.AreEqual("meditation", Find("d000").CurrentTask, "state untouched");
        }

        [Test]
        public void KnownTaskOptions_ComeFromProvider_NotASecondList()
        {
            _presenter.OnOpen(null);

            var expected = _provider.GetKnownTaskIds();
            Assert.Greater(expected.Count, 0, "provider must expose the known tasks");
            foreach (var row in Rows())
                CollectionAssert.AreEqual(expected, row.OptionsForTest, "row options == provider known tasks (same source as TryAssignTask)");
        }

        // ---- confirm: changed rows only + partial success (§5) ----

        [Test]
        public void Confirm_ChangedRowsOnly_UnchangedRowsNeverTouched()
        {
            _presenter.OnOpen(null);

            var d000 = RowOf("d000");
            d000.SelectTask(OptionIndexOf(d000, "gathering_herb"));
            Assert.IsTrue(_view.IsConfirmInteractableForTest, "draft exists — confirm enabled");

            _view.InvokeConfirmForTest();

            Assert.AreEqual("gathering_herb", Find("d000").CurrentTask, "changed row committed");
            Assert.AreEqual("gathering_herb", d000.DropdownValueText, "baseline advanced with the commit");
            Assert.AreEqual("meditation", Find("d001").CurrentTask, "untouched row d001");
            Assert.AreEqual("meditation", Find("d002").CurrentTask, "untouched row d002");
            Assert.AreEqual("meditation", Find("d003").CurrentTask, "untouched row d003");
            Assert.IsFalse(_view.IsConfirmInteractableForTest, "no drafts left after success");
            Assert.AreEqual(1, _taskChangedOut.Messages.Count, "one publish for the one change");
        }

        [Test]
        public void Confirm_PartialSuccess_SuccessRowsAdvance_FailedRowsKeepDraftAndError()
        {
            _presenter.OnOpen(null);

            var d000 = RowOf("d000");
            var d001 = RowOf("d001");
            d000.SelectTask(OptionIndexOf(d000, "refining_elixir")); // FAILS: no pill_hall
            d001.SelectTask(OptionIndexOf(d001, "gathering_wood"));  // succeeds: no requirement

            _view.InvokeConfirmForTest();

            Assert.AreEqual("gathering_wood", Find("d001").CurrentTask, "successful row committed");
            Assert.AreEqual("gathering_wood", d001.DropdownValueText, "success baseline advanced");
            Assert.AreEqual("meditation", Find("d000").CurrentTask, "failed row: state unchanged");
            Assert.AreEqual("refining_elixir", d000.DropdownValueText, "failed draft RETAINED in the dropdown for correction");
            StringAssert.Contains("pill_hall", d000.StatusTextForTest, "per-row error names the missing building");
            Assert.IsTrue(_view.IsConfirmInteractableForTest, "unresolved draft retained");
            Assert.AreEqual(1, _taskChangedOut.Messages.Count, "publish only for the successful row");
            Assert.AreEqual("d001", _taskChangedOut.Messages[0].DiscipleId);
        }

        // ---- cancel (§5) ----

        [Test]
        public void Cancel_DoesNotMutateState_AtAll()
        {
            _presenter.OnOpen(null);

            string before = DumpTasks();
            int msgsBefore = _taskChangedOut.Messages.Count;

            var d000 = RowOf("d000");
            d000.SelectTask(OptionIndexOf(d000, "gathering_herb"));
            _view.InvokeCancelForTest();

            Assert.AreEqual(before, DumpTasks(), "cancel changes nothing in state");
            Assert.AreEqual(msgsBefore, _taskChangedOut.Messages.Count, "no message published");
            Assert.IsFalse(_view.IsConfirmInteractableForTest, "drafts dropped");
            Assert.AreEqual("meditation", d000.DropdownValueText, "dropdown snapped back to baseline");
        }

        // ---- external change / conflict (§6) ----

        [Test]
        public void ExternalChange_OnDirtyRow_MarksConflict_AndBlocksCommit()
        {
            _presenter.OnOpen(null);

            var d000 = RowOf("d000");
            d000.SelectTask(OptionIndexOf(d000, "gathering_wood")); // dirty draft

            // external change arrives (AI GM path) while the draft is dirty
            _taskChangedIn.Emit(new DiscipleTaskChangedMessage { DiscipleId = "d000", TaskId = "meditation" });

            StringAssert.Contains("d000", _view.ConflictBannerForTest, "conflict banner shown for the row");

            _view.InvokeConfirmForTest();

            // the conflict must NOT silently overwrite — external value stands
            Assert.AreEqual("meditation", Find("d000").CurrentTask, "conflicted draft must not commit");
            StringAssert.Contains("ขัดแย้ง", d000.StatusTextForTest,
                                  "row keeps a blocking conflict error");
        }

        [Test]
        public void ExternalChange_OnCleanRow_AdvancesBaseline_NoConflict()
        {
            _presenter.OnOpen(null);

            _taskChangedIn.Emit(new DiscipleTaskChangedMessage { DiscipleId = "d002", TaskId = "gathering_herb" });

            Assert.IsEmpty(_view.ConflictBannerForTest, "no dirty draft on d002 — no conflict");
            Assert.AreEqual("gathering_herb", RowOf("d002").DropdownValueText,
                            "baseline follows the external change");
        }

        // ---- subscription lifecycle (§8) ----

        [Test]
        public void Reopen_AddsNoDuplicateSubscription()
        {
            _presenter.OnOpen(null);
            Assert.AreEqual(1, _taskChangedIn.ActiveSubscriptionCount);

            // UIService.Close runs Dispose, then a reopen re-binds + re-opens
            _presenter.Dispose();
            _presenter.Bind(_view);
            _presenter.OnOpen(null);
            Assert.AreEqual(1, _taskChangedIn.ActiveSubscriptionCount,
                            "exactly one live subscription after reopen");

            // and external updates still flow after the reopen
            _taskChangedIn.Emit(new DiscipleTaskChangedMessage { DiscipleId = "d003", TaskId = "gathering_herb" });
            Assert.AreEqual("gathering_herb", RowOf("d003").DropdownValueText);
        }

        [Test]
        public void Dispose_Unsubscribes_NoFurtherExternalUpdates()
        {
            _presenter.OnOpen(null);
            _presenter.Dispose();

            _taskChangedIn.Emit(new DiscipleTaskChangedMessage { DiscipleId = "d000", TaskId = "gathering_wood" });
            Assert.AreEqual("meditation", RowOf("d000").DropdownValueText,
                            "no baseline update after dispose — subscription truly removed");
        }

        // ---- fakes ----

        /// <summary>
        /// Minimal ISubscriber recording Subscribe calls + replaying to live handlers.
        /// Implements MessagePipe's real shape: Subscribe(IMessageHandler&lt;T&gt;, filters) —
        /// MessagePipe's Action-style Subscribe is an extension over IMessageHandler,
        /// so the fake handler here doubles as the IMessageHandler implementation.
        /// </summary>
        private sealed class RecordingSubscriber : ISubscriber<DiscipleTaskChangedMessage>
        {
            private readonly List<Handler> _handlers = new List<Handler>();
            public int ActiveSubscriptionCount => _handlers.Count;

            public void Emit(DiscipleTaskChangedMessage message)
            {
                for (int i = _handlers.Count - 1; i >= 0; i--) _handlers[i].Handle(message);
            }

            public IDisposable Subscribe(IMessageHandler<DiscipleTaskChangedMessage> handler,
                                         params MessageHandlerFilter<DiscipleTaskChangedMessage>[] filters)
            {
                var wrapped = new Handler(handler);
                _handlers.Add(wrapped);
                return new Subscription(this, wrapped);
            }

            private sealed class Handler : IMessageHandler<DiscipleTaskChangedMessage>
            {
                private readonly IMessageHandler<DiscipleTaskChangedMessage> _inner;
                public Handler(IMessageHandler<DiscipleTaskChangedMessage> inner) => _inner = inner;
                public void Handle(DiscipleTaskChangedMessage message) => _inner.Handle(message);
            }

            private sealed class Subscription : IDisposable
            {
                private RecordingSubscriber _owner;
                private Handler _handler;
                public Subscription(RecordingSubscriber owner, Handler handler)
                {
                    _owner = owner; _handler = handler;
                }
                public void Dispose()
                {
                    if (_owner == null) return;
                    _owner._handlers.Remove(_handler);
                    _owner = null; _handler = null;
                }
            }
        }

        /// <summary>Minimal IPublisher over a list — same pattern as MockStartStateTests.</summary>
        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public readonly List<T> Messages = new List<T>();
            public void Publish(T message) => Messages.Add(message);
        }
    }
}
