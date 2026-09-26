using System.Collections.Generic;
using MessagePipe;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Xianxia.Sect.Building;
using Xianxia.Sect.Messages;
using Xianxia.Sect.UI;

namespace Xianxia.Sect.Tests
{
    /// <summary>
    /// BuildingMenu acceptance (task AC #4/#7/#8/#9 — MVP Lite checklist):
    ///   - 4 หมวดหมู่แสดงครบตาม building_defs.json
    ///   - แท็บสลับหมวดแล้วการ์ดเปลี่ยนตาม def ของหมวดนั้น
    ///   - คลิกการ์ด → placement mode → ✓ ยืนยัน → หักทรัพยากร + state append จริง
    ///   - ✕ ยกเลิก → state ไม่เปลี่ยนเลย
    /// Wire refs ผ่าน SerializedObject (กลไกเดียวกับ generator) — ไม่ใช้
    /// FindObjectOfType / reflection (C12). ปุ่ม ✓/✕/⟲ invoke ผ่าน
    /// Button.onClick จริงของ BuildingPlacementView (runtime-built ใต้ fixture root)
    /// </summary>
    public class BuildingMenuPresenterTests
    {
        private SectStateProvider _stateProvider;
        private BuildingGrid _grid;
        private BuildingDefPool _defPool;
        private PlacementController _placement;
        private BuildingPlacementUISystem _placementUI;
        private BuildingMenuPresenter _presenter;
        private BuildingMenuView _view;
        private GameObject _uiRootGo;
        private GameObject _rootGo;
        private BufferPublisher<SectResourceChangedMessage> _resourceBuffer;
        private BufferPublisher<BuildingPlacedMessage> _placedBuffer;
        private BufferPublisher<BuildModeStartedMessage> _buildStartedBuffer;
        private BufferPublisher<BuildModeEndedMessage> _buildEndedBuffer;
        private int _closeRequested;

        /// <summary>ISubscriber จำลอง — signature ตรงกับ MessagePipe จริง (IMessageHandler + params filters)
        /// Subscribe คืน disposable ปลอม (broker จริงต้อง build container เต็ม)</summary>
        private sealed class BufferSubscriber<T> : ISubscriber<T>
        {
            public System.IDisposable Subscribe(IMessageHandler<T> handler, params MessageHandlerFilter<T>[] filters)
                => new NoopDisposable();

            private sealed class NoopDisposable : System.IDisposable
            {
                public void Dispose() { }
            }
        }

        [SetUp]
        public void SetUp()
        {
            _resourceBuffer = new BufferPublisher<SectResourceChangedMessage>();
            _placedBuffer = new BufferPublisher<BuildingPlacedMessage>();
            _buildStartedBuffer = new BufferPublisher<BuildModeStartedMessage>();
            _buildEndedBuffer = new BufferPublisher<BuildModeEndedMessage>();
            _defPool = new BuildingDefPool();

            _stateProvider = new SectStateProvider(
                new BufferPublisher<DiscipleRecruitedMessage>(),
                _resourceBuffer,
                new BufferPublisher<AvatarEquipmentChangedMessage>(),
                new BufferPublisher<DiscipleChibiBackendChangedMessage>(),
                new AvatarPartPool(),
                Visual.VisualRuntimeConfig.Instance,
                new Visual.DefaultEntitlementProvider(),
                _defPool,
                _placedBuffer);

            _grid = new BuildingGrid(10, 10);
            _placement = new PlacementController(_defPool, _stateProvider);

            // Minimal UIRoot stand-in — system ใช้แค่ Root property เป็น parent
            // ของ floating placement controls
            _uiRootGo = new GameObject("TestUIRoot", typeof(RectTransform), typeof(UIRoot));

            // persistent system = เจ้าของ flow การวาง (ปุ่มลอย + BuildMode messages) —
            // BuildingSystem ส่ง null ได้ (system รองรับ: ปุ่มลอยไม่เกาะ ghost ใน EditMode
            // เพราะไม่มี play loop วาด ghost ให้ดึงตำแหน่ง)
            _placementUI = new BuildingPlacementUISystem(
                _placement, _grid, _uiRootGo.GetComponent<UIRoot>(), null,
                _buildStartedBuffer, _buildEndedBuffer);

            _presenter = new BuildingMenuPresenter(_defPool, _placement, _placementUI);

            _rootGo = CreateMenuViewRoot("BuildingMenuTestRoot");

            _presenter.Bind(_view);
            _presenter.OnOpen(new BuildingMenuArgs { CloseCallback = () => _closeRequested++ });
        }

        private GameObject CreateMenuViewRoot(string name)
        {
            var rootGo = new GameObject(name, typeof(RectTransform));
            rootGo.SetActive(false); // defer Awake จน wire refs เสร็จ
            _view = rootGo.AddComponent<BuildingMenuView>();

            var titleGo = new GameObject("TitleText", typeof(RectTransform));
            titleGo.transform.SetParent(rootGo.transform, false);
            var title = titleGo.AddComponent<TextMeshProUGUI>();

            var closeGo = new GameObject("CloseButton", typeof(Image), typeof(Button));
            closeGo.transform.SetParent(rootGo.transform, false);

            var tabsGo = new GameObject("TabRoot", typeof(RectTransform));
            tabsGo.transform.SetParent(rootGo.transform, false);

            var itemsGo = new GameObject("ItemRoot", typeof(RectTransform));
            itemsGo.transform.SetParent(rootGo.transform, false);

            var so = new SerializedObject(_view);
            so.FindProperty("titleText").objectReferenceValue = title;
            so.FindProperty("closeButton").objectReferenceValue = closeGo.GetComponent<Button>();
            so.FindProperty("tabRoot").objectReferenceValue = (RectTransform)tabsGo.transform;
            so.FindProperty("itemRoot").objectReferenceValue = (RectTransform)itemsGo.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            rootGo.SetActive(true);
            _view.Show();
            return rootGo;
        }

        [TearDown]
        public void TearDown()
        {
            _presenter.Dispose();
            if (_rootGo != null) Object.DestroyImmediate(_rootGo);
            if (_uiRootGo != null) Object.DestroyImmediate(_uiRootGo);
        }

        private List<BuildingTabCell> Tabs()
        {
            var tabs = new List<BuildingTabCell>();
            foreach (Transform child in _view.TabRoot)
            {
                var tab = child.GetComponent<BuildingTabCell>();
                if (tab != null) tabs.Add(tab);
            }
            return tabs;
        }

        private List<BuildingItemCell> Cards()
        {
            var cards = new List<BuildingItemCell>();
            foreach (Transform child in _view.ItemRoot)
            {
                var card = child.GetComponent<BuildingItemCell>();
                if (card != null) cards.Add(card);
            }
            return cards;
        }

        private BuildingPlacementView PlacementView()
        {
            // floating controls ถูก Build ใต้ fixture root หลังคลิกการ์ด (โดย system)
            return _uiRootGo.GetComponentInChildren<BuildingPlacementView>(true);
        }

        private int Raw(string id)
        {
            int v;
            return _stateProvider.BuildSectEconomyState().Stockpile.RawResources.TryGetValue(id, out v) ? v : 0;
        }

        // ---- 4 หมวดหมู่ครบตาม building_defs.json ----

        [Test]
        public void OnOpen_BuildsFourCategoryTabs()
        {
            var tabs = Tabs();
            Assert.AreEqual(4, tabs.Count, "Production/Convenience/Shop/Landscape — ครบ 4 แท็บ");
        }

        [Test]
        public void FirstTab_ShowsAllProductionDefs()
        {
            var cards = Cards();
            Assert.AreEqual(2, cards.Count, "building_defs.json หมวด Production มี herb_plot + pill_hall");
            Assert.AreEqual("herb_plot", cards[0].DefId);
        }

        [Test]
        public void ClickingTab_SwitchesCardGrid()
        {
            Tabs()[3].Click(); // Landscape → stone_lantern + spirit_pond

            var cards = Cards();
            Assert.AreEqual(2, cards.Count);
            Assert.AreEqual("stone_lantern", cards[0].DefId);
        }

        // ---- คลิกการ์ด → placement → ✓ ยืนยัน ----

        [Test]
        public void CardClick_EntersPlacementMode_AndPublishesBuildModeStarted()
        {
            Cards()[0].Click(); // herb_plot

            Assert.IsTrue(_placement.IsActive, "ghost must be live after picking a card");
            Assert.AreEqual("herb_plot", _placement.DefId);
            Assert.AreEqual(1, _buildStartedBuffer.Messages.Count, "camera/overlay need the bus message");
            Assert.IsNotNull(PlacementView(), "floating controls must exist after picking a card");
        }

        [Test]
        public void Confirm_PlacesBuilding_DeductsCost_AndEndsPlacement()
        {
            int woodBefore = Raw("wood");

            Cards()[0].Click(); // herb_plot — menu closes, floating controls appear
            _placement.UpdatePosition(2, 2);
            PlacementView().ConfirmButton.onClick.Invoke(); // ✓ ปุ่มจริง

            Assert.AreEqual(woodBefore - 20, Raw("wood"), "cost deducted through AdjustAndNotify");
            Assert.AreEqual(1, _stateProvider.BuildSectEconomyState().PlacedBuildings.Count);
            Assert.IsFalse(_placement.IsActive, "auto-commit exits placement mode");
            Assert.AreEqual(1, _buildEndedBuffer.Messages.Count, "camera returns to overview");
            Assert.AreEqual(1, _placedBuffer.Messages.Count);
            Assert.AreEqual(1, _closeRequested, "menu closed itself after picking a card");
        }

        [Test]
        public void Confirm_AtOverlappingCell_ShowsReason_AndKeepsMode()
        {
            Cards()[0].Click(); // herb_plot ใบแรก
            _placement.UpdatePosition(2, 2);
            PlacementView().ConfirmButton.onClick.Invoke(); // วางสำเร็จที่ (2,2)

            // เปิดเมนูรอบใหม่ (presenter เป็น Transient — instance ใหม่ + view ใหม่;
            // system เดิมยังถือ flow การวางเป็นของ persistent)
            _presenter.Dispose();
            _presenter = new BuildingMenuPresenter(_defPool, _placement, _placementUI);
            _rootGo = CreateMenuViewRoot("BuildingMenuTestRoot2");
            _presenter.Bind(_view);
            _presenter.OnOpen(new BuildingMenuArgs());

            Cards()[0].Click();
            _placement.UpdatePosition(2, 2); // ซ้อนหลังแรกพอดี
            PlacementView().ConfirmButton.onClick.Invoke();

            var status = PlacementView().StatusText != null ? PlacementView().StatusText.text : string.Empty;
            Assert.IsTrue(status.Length > 0, "must surface a reason (แดง/ข้อความ §3.2)");
            Assert.IsTrue(_placement.IsActive, "failed commit keeps placement mode for retry");
            Assert.AreEqual(1, _stateProvider.BuildSectEconomyState().PlacedBuildings.Count,
                            "no state append on failed commit");
        }

        // ---- ✕ ยกเลิก: ไม่มีอะไรเปลี่ยนใน state เลย ----

        [Test]
        public void Cancel_AfterCardClick_ChangesNothingInState()
        {
            int wood = Raw("wood");
            int ore = Raw("ore");
            int provisions = Raw("provisions");

            Cards()[0].Click();
            _placement.UpdatePosition(4, 4);
            PlacementView().CancelButton.onClick.Invoke(); // ✕ ปุ่มจริง

            var state = _stateProvider.BuildSectEconomyState();
            Assert.AreEqual(wood, Raw("wood"));
            Assert.AreEqual(ore, Raw("ore"));
            Assert.AreEqual(provisions, Raw("provisions"));
            Assert.AreEqual(0, state.PlacedBuildings.Count, "no state append on cancel");
            Assert.AreEqual(0, _resourceBuffer.Messages.Count, "no resource message on cancel");
            Assert.AreEqual(0, _placedBuffer.Messages.Count);
            Assert.IsFalse(_placement.IsActive);
        }

        [Test]
        public void Dispose_MenuDoesNotKillLiveGhost()
        {
            Cards()[0].Click();
            Assert.IsTrue(_placement.IsActive);

            _presenter.Dispose();

            Assert.IsTrue(_placement.IsActive,
                "flow การวางต้องรอดจากการปิดเมนู (bug เดิม: Dispose ยกเลิก ghost ที่เพิ่งเริ่ม)");
            Assert.IsNotNull(PlacementView(), "ปุ่มลอยต้องยังอยู่กับ persistent system");

            // ปิดด้วยปุ่มยกเลิกของ system เท่านั้น
            PlacementView().CancelButton.onClick.Invoke();
            Assert.IsFalse(_placement.IsActive);
        }

        // ---- helpers ----

        private sealed class BufferPublisher<T> : IPublisher<T>
        {
            public readonly List<T> Messages = new List<T>();
            public void Publish(T message) { Messages.Add(message); }
        }
    }
}
